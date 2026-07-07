using System.Text.Json.Nodes;
using TokenStack.Core.Components;
using TokenStack.Core.Config;
using TokenStack.Core.Windows;

namespace TokenStack.Core.Status;

/// <summary>Gathers a live StackStatus. statsTimeoutMs: hook mode uses 3000 (never slow a
/// session start); interactive status uses 25000 (/stats can take ~20s under load).</summary>
public sealed class StatusProbe(
    IProcessRunner runner, IEnvStore env, IPortProbe port, IHttpProbe http)
{
    public StackStatus Gather(StackConfig cfg, string claudeJsonPath, int statsTimeoutMs = 3000)
    {
        var tasks = new ScheduledTaskManager(runner);
        var headroom = new HeadroomComponent(runner, port, http);
        var routing = new RoutingManager(env);

        var taskRunning = cfg.Headroom.Enabled && tasks.IsRunning();
        var listening = cfg.Headroom.Enabled && port.IsListening(cfg.Headroom.Port);
        var reqs = listening ? headroom.ReadApiRequests(cfg.Headroom.Port, statsTimeoutMs) : null;

        var sembleWired = false;
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(claudeJsonPath));
            var cmd = root?["mcpServers"]?["semble"]?["command"]?.GetValue<string>();
            sembleWired = cmd is not null && File.Exists(cmd);
        }
        catch { /* missing/corrupt file = not wired */ }

        return new StackStatus(
            TaskRunning: taskRunning,
            PortListening: listening,
            Routed: routing.IsSessionRouted(cfg.Headroom.Port),
            Reqs: reqs,
            Port: cfg.Headroom.Port,
            RtkOnPath: cfg.Rtk.Enabled && runner.Run("rtk", "--version", 10000).Ok,
            SembleWired: sembleWired,
            HeadroomEnabled: cfg.Headroom.Enabled,
            RtkEnabled: cfg.Rtk.Enabled,
            SembleEnabled: cfg.Semble.Enabled,
            ProviderLabel: Providers.Label(cfg.Headroom.UpstreamUrl),
            CcoEnabled: cfg.Cco.Enabled,
            CcoWired: cfg.Cco.Enabled && File.Exists(Path.Combine(cfg.InstallRoot, "cco", "src", "read-cache.js")));
    }

    /// <summary>Hook-mode side effects: start the task if down + zombie-recover, then report.</summary>
    public StackStatus GatherForHook(StackConfig cfg, string claudeJsonPath)
    {
        try
        {
            var tasks = new ScheduledTaskManager(runner);
            if (cfg.Headroom.Enabled && tasks.Exists() && !tasks.IsRunning()) tasks.Start();
            var marker = Path.Combine(cfg.InstallRoot, "tmp", "first-seen-dead.marker");
            if (cfg.Headroom.Enabled && tasks.IsRunning() && !port.IsListening(cfg.Headroom.Port))
            {
                // zombie window: tolerate the cold load, restart only after a 3-min grace
                // (tracked via a marker file so successive session starts share the clock)
                Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
                if (!File.Exists(marker)) File.WriteAllText(marker, DateTimeOffset.UtcNow.ToString("O"));
                else if (DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(marker) > TimeSpan.FromMinutes(3))
                { tasks.RestartWithZombieKill(); File.Delete(marker); }
            }
            else if (File.Exists(marker))
            {
                File.Delete(marker);
            }
        }
        catch { /* the hook must never throw */ }
        return Gather(cfg, claudeJsonPath, statsTimeoutMs: 3000);
    }
}
