using System.Text.Json.Nodes;
using TokenStack.Core.Claude;
using TokenStack.Core.Components;
using TokenStack.Core.Config;
using TokenStack.Core.Windows;

namespace TokenStack.Core.Doctor;

public sealed record DoctorContext(
    StackConfig Config,
    JsonNode Settings,      // loaded %USERPROFILE%\.claude\settings.json
    JsonNode ClaudeJson,    // loaded %USERPROFILE%\.claude.json
    IEnvStore Env,
    IPortProbe Port,
    IProcessRunner Runner,
    IHttpProbe Http)
{
    public bool SettingsChanged { get; set; }
    public bool ClaudeJsonChanged { get; set; }
}

public sealed record CheckResult(string Id, bool Ok, string Detail, bool CanFix);

public interface IDoctorCheck
{
    string Id { get; }
    CheckResult Detect(DoctorContext ctx);
    /// <summary>Apply the remediation. Returns true when something was changed.</summary>
    bool Fix(DoctorContext ctx);
}

public static class DoctorRegistry
{
    public static IReadOnlyList<IDoctorCheck> All { get; } = new IDoctorCheck[]
    {
        new RoutingBypassedCheck(), new ProxyZombieCheck(), new ProxyExtraMissingCheck(),
        new SembleUvxCheck(), new RtkHookMissingCheck(), new RtkHookPowershellCheck(),
        new ModelPinLeftoverCheck(), new PathSpacesCheck(), new TaskMisconfiguredCheck(),
        new DisabledDriftCheck(),
    };
}

public sealed class RoutingBypassedCheck : IDoctorCheck
{
    public string Id => "routing-bypassed";
    public CheckResult Detect(DoctorContext ctx)
    {
        if (!ctx.Config.Routing.Desktop && !ctx.Config.Routing.Cli)
            return new(Id, true, "routing disabled in config", false);
        var d = new RoutingManager(ctx.Env).Diagnose(ctx.Config.Headroom.Port);
        if (d.SessionRouted) return new(Id, true, "session is ROUTED", false);
        var why = d.Conflict
            ? "User-scope var is correct but THIS session inherited a different value — fully quit Claude (tray) and relaunch"
            : "ANTHROPIC_BASE_URL does not point at the proxy";
        return new(Id, false, why, true);
    }
    public bool Fix(DoctorContext ctx)
    {
        var rm = new RoutingManager(ctx.Env);
        if (ctx.Config.Routing.Desktop) rm.ApplyDesktop(ctx.Config.Headroom.Port);
        if (ctx.Config.Routing.Cli)
            ctx.SettingsChanged |= ClaudeSurgeon.SetEnvBaseUrl(
                ctx.Settings, RoutingManager.ProxyUrl(ctx.Config.Headroom.Port));
        return true;
    }
}

public sealed class ProxyZombieCheck : IDoctorCheck
{
    public string Id => "proxy-zombie";
    public CheckResult Detect(DoctorContext ctx)
    {
        if (!ctx.Config.Headroom.Enabled) return new(Id, true, "headroom disabled", false);
        var tasks = new ScheduledTaskManager(ctx.Runner);
        var zombie = tasks.IsRunning() && !ctx.Port.IsListening(ctx.Config.Headroom.Port);
        return zombie
            ? new(Id, false, "task Running but port dead (zombie listener)", true)
            : new(Id, true, "proxy lifecycle healthy", false);
    }
    public bool Fix(DoctorContext ctx)
    {
        new ScheduledTaskManager(ctx.Runner).RestartWithZombieKill();
        return true;
    }
}

public sealed class ProxyExtraMissingCheck : IDoctorCheck
{
    public string Id => "proxy-extra-missing";
    public CheckResult Detect(DoctorContext ctx)
    {
        if (!ctx.Config.Headroom.Enabled) return new(Id, true, "headroom disabled", false);
        var sitePackages = Path.Combine(ctx.Config.InstallRoot, "venv", "Lib", "site-packages");
        if (!Directory.Exists(sitePackages)) return new(Id, true, "venv not installed yet", false);
        var ok = Directory.Exists(Path.Combine(sitePackages, "fastapi"));
        return ok
            ? new(Id, true, "[proxy] extra present (fastapi found)", false)
            : new(Id, false, "venv lacks fastapi — installed without the [proxy] extra", true);
    }
    public bool Fix(DoctorContext ctx)
    {
        var uv = new Bootstrap(ctx.Runner).EnsureUv();
        var py = Path.Combine(ctx.Config.InstallRoot, "venv", "Scripts", "python.exe");
        return ctx.Runner.Run(uv,
            $"pip install --python {py} headroom-ai[proxy]=={ctx.Config.Headroom.Version}", 600000).Ok;
    }
}

public sealed class SembleUvxCheck : IDoctorCheck
{
    public string Id => "semble-uvx";
    public CheckResult Detect(DoctorContext ctx)
    {
        if (!ctx.Config.Semble.Enabled) return new(Id, true, "semble disabled", false);
        var cmd = ctx.ClaudeJson["mcpServers"]?["semble"]?["command"]?.GetValue<string>();
        if (cmd is null) return new(Id, false, "semble MCP not registered", true);
        if (cmd.Contains("uvx", StringComparison.OrdinalIgnoreCase) || !Path.IsPathRooted(cmd))
            return new(Id, false, "semble MCP uses uvx/relative command — handshake will silently fail", true);
        if (!File.Exists(cmd)) return new(Id, false, $"semble exe missing at {cmd}", true);
        return new(Id, true, "semble MCP wired to installed exe", false);
    }
    public bool Fix(DoctorContext ctx)
    {
        ctx.ClaudeJsonChanged |= ClaudeSurgeon.EnsureSembleMcp(ctx.ClaudeJson, SembleComponent.ExePath());
        return true;
    }
}

public sealed class RtkHookMissingCheck : IDoctorCheck
{
    public string Id => "rtk-hook-missing";
    public CheckResult Detect(DoctorContext ctx)
    {
        if (!ctx.Config.Rtk.Enabled) return new(Id, true, "rtk disabled", false);
        var pre = ctx.Settings["hooks"]?["PreToolUse"]?.AsArray();
        var entry = pre?.FirstOrDefault(e =>
            e?["hooks"]?[0]?["command"]?.GetValue<string>()
                ?.Contains("rtk.exe", StringComparison.OrdinalIgnoreCase) == true);
        if (entry is null) return new(Id, false, "PreToolUse rtk hook absent", true);
        var exe = Path.Combine(ctx.Config.InstallRoot, "rtk", "rtk.exe");
        var cmd = entry["hooks"]![0]!["command"]!.GetValue<string>();
        return cmd.Contains(exe, StringComparison.OrdinalIgnoreCase)
            ? new(Id, true, "rtk hook wired", false)
            : new(Id, false, $"rtk hook points at a stale path: {cmd}", true);
    }
    public bool Fix(DoctorContext ctx)
    {
        ctx.SettingsChanged |= ClaudeSurgeon.EnsureRtkHook(ctx.Settings,
            Path.Combine(ctx.Config.InstallRoot, "rtk", "rtk.exe"), ctx.Config.Rtk.HookMatcher);
        return true;
    }
}

public sealed class RtkHookPowershellCheck : IDoctorCheck
{
    public string Id => "rtk-hook-powershell";
    public CheckResult Detect(DoctorContext ctx)
    {
        var pre = ctx.Settings["hooks"]?["PreToolUse"]?.AsArray();
        var bad = pre?.Any(e =>
            e?["matcher"]?.GetValue<string>()?.Contains("PowerShell", StringComparison.OrdinalIgnoreCase) == true
            && e["hooks"]?[0]?["command"]?.GetValue<string>()
                ?.Contains("rtk", StringComparison.OrdinalIgnoreCase) == true) == true;
        return bad
            ? new(Id, false, "rtk hook has a PowerShell matcher — rtk cannot wrap PS aliases", true)
            : new(Id, true, "no PowerShell rtk matcher", false);
    }
    public bool Fix(DoctorContext ctx)
    {
        var pre = ctx.Settings["hooks"]?["PreToolUse"]?.AsArray();
        if (pre is null) return false;
        var bad = pre.Where(e =>
            e?["matcher"]?.GetValue<string>()?.Contains("PowerShell", StringComparison.OrdinalIgnoreCase) == true
            && e["hooks"]?[0]?["command"]?.GetValue<string>()
                ?.Contains("rtk", StringComparison.OrdinalIgnoreCase) == true).ToList();
        foreach (var b in bad) pre.Remove(b);
        ctx.SettingsChanged |= bad.Count > 0;
        return bad.Count > 0;
    }
}

public sealed class ModelPinLeftoverCheck : IDoctorCheck
{
    public string Id => "model-pin-leftover";
    public CheckResult Detect(DoctorContext ctx) =>
        ctx.Env.GetUser("ANTHROPIC_MODEL") is { } v
            ? new(Id, false, $"stray User-scope ANTHROPIC_MODEL={v} pins every session's model", true)
            : new(Id, true, "no model pin", false);
    public bool Fix(DoctorContext ctx) { ctx.Env.SetUser("ANTHROPIC_MODEL", null); return true; }
}

public sealed class PathSpacesCheck : IDoctorCheck
{
    public string Id => "path-spaces";
    public CheckResult Detect(DoctorContext ctx) =>
        ctx.Config.InstallRoot.Contains(' ')
            ? new(Id, false, "installRoot contains spaces — reinstall to a space-free root " +
                  "(token-stack uninstall, edit config, token-stack install)", false)
            : new(Id, true, "installRoot is space-free", false);
    public bool Fix(DoctorContext ctx) => false; // guided, never automatic
}

public sealed class TaskMisconfiguredCheck : IDoctorCheck
{
    public string Id => "task-misconfigured";
    public CheckResult Detect(DoctorContext ctx)
    {
        if (!ctx.Config.Headroom.Enabled) return new(Id, true, "headroom disabled", false);
        var r = ctx.Runner.Run("schtasks", $"/query /tn {ScheduledTaskManager.TaskName} /xml", 15000);
        if (!r.Ok) return new(Id, false, "HeadroomProxy task not registered", true);
        var expectedCmd = Path.Combine(ctx.Config.InstallRoot, "venv", "Scripts", "pythonw.exe");
        return r.StdOut.Contains(expectedCmd, StringComparison.OrdinalIgnoreCase)
            ? new(Id, true, "task action matches managed layout", false)
            : new(Id, false, "task action drifted from managed layout", true);
    }
    public bool Fix(DoctorContext ctx)
    {
        var xml = TaskXml.Render(
            Path.Combine(ctx.Config.InstallRoot, "venv", "Scripts", "pythonw.exe"),
            Path.Combine(ctx.Config.InstallRoot, "run_proxy.py"),
            ctx.Config.InstallRoot,
            Environment.UserDomainName + "\\" + Environment.UserName);
        new ScheduledTaskManager(ctx.Runner)
            .RegisterOrUpdate(xml, Path.Combine(ctx.Config.InstallRoot, "tmp"));
        return true;
    }
}

public sealed class DisabledDriftCheck : IDoctorCheck
{
    public string Id => "stack-disabled-drift";
    public CheckResult Detect(DoctorContext ctx)
    {
        var drift = new List<string>();
        var rtkWired = ctx.Settings["hooks"]?["PreToolUse"]?.AsArray()?.Any(e =>
            e?["hooks"]?[0]?["command"]?.GetValue<string>()
                ?.Contains("rtk.exe", StringComparison.OrdinalIgnoreCase) == true) == true;
        if (!ctx.Config.Rtk.Enabled && rtkWired) drift.Add("rtk disabled but hook wired");
        var sembleWired = ctx.ClaudeJson["mcpServers"]?["semble"] is not null;
        if (!ctx.Config.Semble.Enabled && sembleWired) drift.Add("semble disabled but MCP wired");
        return drift.Count == 0
            ? new(Id, true, "wiring matches config", false)
            : new(Id, false, string.Join("; ", drift), true);
    }
    public bool Fix(DoctorContext ctx)
    {
        var changed = false;
        if (!ctx.Config.Rtk.Enabled)
        {
            var c1 = ClaudeSurgeon.RemoveRtkHook(ctx.Settings);
            ctx.SettingsChanged |= c1; changed |= c1;
        }
        if (!ctx.Config.Semble.Enabled)
        {
            var c2 = ClaudeSurgeon.RemoveSembleMcp(ctx.ClaudeJson);
            ctx.ClaudeJsonChanged |= c2; changed |= c2;
        }
        return changed;
    }
}
