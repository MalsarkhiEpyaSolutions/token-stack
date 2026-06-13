using System.Reflection;
using System.Text.Json;
using TokenStack.Core.Config;
using TokenStack.Core.Windows;

namespace TokenStack.Core.Components;

public sealed class HeadroomComponent(IProcessRunner runner, IPortProbe port, IHttpProbe http)
{
    /// <summary>argv for `headroom proxy` as a Python list literal for the launcher template.
    /// Mode map: token = default (no flag), cache = --mode cache, passthrough = --no-optimize.
    /// NOTE: verify flag names against `headroom proxy --help` during integration (Task 12);
    /// they come from the reference setup doc.</summary>
    public static string BuildArgv(HeadroomConfig cfg)
    {
        var argv = new List<string> { "headroom", "proxy", "--port", cfg.Port.ToString() };
        if (cfg.Mode == "cache") argv.AddRange(new[] { "--mode", "cache" });
        if (cfg.Mode == "passthrough") argv.Add("--no-optimize");
        argv.AddRange(cfg.ExtraArgs);
        return JsonSerializer.Serialize(argv);
    }

    public static string RenderLauncher(HeadroomConfig cfg)
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("TokenStack.Core.Resources.run_proxy.py.tmpl")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("{{ARGV}}", BuildArgv(cfg));
    }

    /// <summary>The exact install commands, exposed for tests and --verbose tracing.</summary>
    public List<string> PlanInstallCommands(StackConfig cfg, string uvPath)
    {
        var venvPython = Path.Combine(cfg.InstallRoot, "venv", "Scripts", "python.exe");
        return new List<string>
        {
            // --clear keeps install idempotent: a partial venv from an interrupted run is replaced
            $"{uvPath} venv --clear --python {cfg.Headroom.PythonVersion} {Path.Combine(cfg.InstallRoot, "venv")}",
            $"{uvPath} pip install --python {venvPython} headroom-ai[proxy]=={cfg.Headroom.Version}",
        };
    }

    public void Install(StackConfig cfg, string uvPath)
    {
        foreach (var cmd in PlanInstallCommands(cfg, uvPath))
        {
            var space = cmd.IndexOf(' ');
            var r = runner.Run(cmd[..space], cmd[(space + 1)..], 600000);
            if (!r.Ok) throw new InvalidOperationException($"Failed: {cmd}\n{r.StdErr}\n{r.StdOut}");
        }
        File.WriteAllText(Path.Combine(cfg.InstallRoot, "run_proxy.py"), RenderLauncher(cfg.Headroom));

        var tasks = new ScheduledTaskManager(runner);
        // Free the port first: stop any prior task instance and kill orphaned proxies
        // (incl. a legacy hand-built install being adopted) before starting the managed one.
        if (tasks.Exists()) tasks.Stop();
        tasks.KillOrphans();
        var xml = TaskXml.Render(
            Path.Combine(cfg.InstallRoot, "venv", "Scripts", "pythonw.exe"),
            Path.Combine(cfg.InstallRoot, "run_proxy.py"),
            cfg.InstallRoot,
            Environment.UserDomainName + "\\" + Environment.UserName);
        tasks.RegisterOrUpdate(xml, Path.Combine(cfg.InstallRoot, "tmp"));
        tasks.Start();
    }

    /// <summary>Cold load is 25–105s; poll /readyz then fall back to a bound port.</summary>
    public bool WaitReady(int proxyPort, int maxWaitMs = 120000, int pollMs = 2000)
    {
        var deadline = Environment.TickCount64 + maxWaitMs;
        while (Environment.TickCount64 < deadline)
        {
            if (http.Get($"http://127.0.0.1:{proxyPort}/readyz", 3000) is not null) return true;
            if (port.IsListening(proxyPort)) return true;
            Thread.Sleep(pollMs);
        }
        return false;
    }

    /// <summary>summary.api_requests from /stats, or null when unreachable (it is slow — ~20s
    /// under load; hook mode uses a 3s budget and simply omits reqs on miss).</summary>
    public long? ReadApiRequests(int proxyPort, int timeoutMs = 3000)
    {
        var body = http.Get($"http://127.0.0.1:{proxyPort}/stats", timeoutMs);
        if (body is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.GetProperty("summary").GetProperty("api_requests").GetInt64();
        }
        catch { return null; }
    }

    public void Unwire(StackConfig cfg)
    {
        var tasks = new ScheduledTaskManager(runner);
        tasks.Stop();
        tasks.Unregister();
    }
}
