using System.Reflection;
using System.Text.Json;
using TokenStack.Core.Config;
using TokenStack.Core.Windows;
using static TokenStack.Core.Components.Quoting;

namespace TokenStack.Core.Components;

public sealed class HeadroomComponent(IProcessRunner runner, IPortProbe port, IHttpProbe http)
{
    /// <summary>argv for `headroom proxy` as a Python list literal for the launcher template.
    /// Mode map: token = default (no flag), cache = --mode cache, passthrough = --no-optimize.</summary>
    public static string BuildArgv(HeadroomConfig cfg)
    {
        var argv = new List<string> { "headroom", "proxy", "--port", cfg.Port.ToString() };
        if (cfg.Mode == "cache") argv.AddRange(new[] { "--mode", "cache" });
        if (cfg.Mode == "passthrough") argv.Add("--no-optimize");
        argv.AddRange(cfg.ExtraArgs);
        return JsonSerializer.Serialize(argv);
    }

    /// <summary>Renders run_proxy.py. hfHome non-null (offline) pins HuggingFace to the bundled
    /// model cache and forces offline mode, set BEFORE headroom imports huggingface_hub.</summary>
    public static string RenderLauncher(HeadroomConfig cfg, string? hfHome)
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("TokenStack.Core.Resources.run_proxy.py.tmpl")!;
        using var reader = new StreamReader(stream);

        var hfBlock = hfHome is null
            ? ""
            : $"os.environ[\"HF_HOME\"] = r\"{hfHome}\"\n" +
              "os.environ[\"HF_HUB_OFFLINE\"] = \"1\"\n" +
              "os.environ[\"TRANSFORMERS_OFFLINE\"] = \"1\"";

        var targetBlock = string.IsNullOrWhiteSpace(cfg.UpstreamUrl)
            ? ""
            : $"os.environ[\"ANTHROPIC_TARGET_API_URL\"] = r\"{cfg.UpstreamUrl}\"";

        return reader.ReadToEnd()
            .Replace("{{ARGV}}", BuildArgv(cfg))
            .Replace("{{HF_ENV}}", hfBlock)
            .Replace("{{TARGET_ENV}}", targetBlock);
    }

    /// <summary>The exact install commands, exposed for tests and --verbose tracing. Offline
    /// uses the bundled python + a local wheelhouse; online provisions python + PyPI via uv.</summary>
    public List<string> PlanInstallCommands(StackConfig cfg, string uvPath, InstallSource src)
    {
        var venv = Path.Combine(cfg.InstallRoot, "venv");
        var venvPython = Path.Combine(venv, "Scripts", "python.exe");
        if (src.IsOffline)
        {
            return new List<string>
            {
                $"{uvPath} venv --clear --python {Q(src.PythonDir)} {Q(venv)}",
                $"{uvPath} pip install --python {Q(venvPython)} --offline --no-index " +
                    $"--find-links {Q(src.Wheelhouse)} headroom-ai[proxy]=={cfg.Headroom.Version}",
            };
        }
        return new List<string>
        {
            $"{uvPath} venv --clear --python {cfg.Headroom.PythonVersion} {Q(venv)}",
            // --no-build: never compile from an sdist on the user's machine (needs Rust+MSVC —
            // seen failing live). If the pinned version has no wheel, fail fast with a clear error.
            $"{uvPath} pip install --python {Q(venvPython)} --no-build headroom-ai[proxy]=={cfg.Headroom.Version}",
        };
    }

    public void Install(StackConfig cfg, string uvPath, InstallSource src)
    {
        var tasks = new ScheduledTaskManager(runner);
        // Stop/kill FIRST: a running proxy locks the venv binaries (`uv venv --clear` → Access
        // denied) and holds the port the new instance needs.
        if (tasks.Exists()) tasks.Stop();
        tasks.KillOrphans();

        foreach (var cmd in PlanInstallCommands(cfg, uvPath, src))
        {
            // Split on the KNOWN exe prefix (not the first space) so a spaced uv path is safe.
            var argLine = cmd.Substring(uvPath.Length).TrimStart();
            var r = runner.Run(uvPath, argLine, 600000);
            if (!r.Ok) throw new InvalidOperationException($"Failed: {cmd}\n{r.StdErr}\n{r.StdOut}");
        }

        string? hfHome = null;
        if (src.IsOffline)
        {
            hfHome = Path.Combine(cfg.InstallRoot, "hf-cache");
            if (!Directory.Exists(src.HfCache))
                throw new DirectoryNotFoundException($"offline HuggingFace cache missing in bundle: {src.HfCache}");
            FsUtil.CopyDirectory(src.HfCache, hfHome); // seed the HuggingFace models for offline runtime
        }
        File.WriteAllText(Path.Combine(cfg.InstallRoot, "run_proxy.py"), RenderLauncher(cfg.Headroom, hfHome));

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

    /// <summary>summary.api_requests from /stats, or null when unreachable.</summary>
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
