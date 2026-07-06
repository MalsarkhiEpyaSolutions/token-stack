using TokenStack.Core.Windows;

namespace TokenStack.Core.Components;

/// <summary>Locates or installs uv (the only bootstrap dependency; it then provisions
/// Python for Headroom and the tool install for Semble).</summary>
public sealed class Bootstrap(IProcessRunner runner)
{
    /// <summary>Deterministic install target of the astral standalone installer.</summary>
    public static string LocalUvExe() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "uv.exe");

    /// <summary>(exe, args) for the standalone installer. Uses the FULL powershell.exe path —
    /// launching bare "powershell" via Process.Start(UseShellExecute=false) fails with
    /// "cannot find the file specified" on machines where it isn't PATH-resolvable to a child
    /// process (seen live). Installs uv to LocalUvExe() with no PATH/winget dependency.</summary>
    public static (string Exe, string Args) StandaloneInstallPlan() => (
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe"),
        "-NoProfile -ExecutionPolicy Bypass -Command \"irm https://astral.sh/uv/install.ps1 | iex\"");

    /// <summary>Returns the uv invocation path, installing uv if needed. Standalone installer
    /// FIRST (deterministic target, no winget needed, continues in THIS process — no "open a
    /// new terminal"); winget only as a fallback.</summary>
    public string EnsureUv()
    {
        if (runner.Run("uv", "--version", 15000).Ok) return "uv";
        var local = LocalUvExe();
        if (File.Exists(local)) return local;

        // 1. Standalone installer — lands at LocalUvExe(), which we then return directly.
        var (pwsh, args) = StandaloneInstallPlan();
        var standalone = runner.Run(pwsh, args, 300000);
        if (File.Exists(local)) return local;

        // 2. Fallback: winget (may be absent or install to an unpredictable, PATH-only location).
        var winget = runner.Run("winget",
            "install --id astral-sh.uv -e --accept-source-agreements --accept-package-agreements",
            300000);
        if (File.Exists(local)) return local;
        if (runner.Run("uv", "--version", 15000).Ok) return "uv";

        throw new InvalidOperationException(
            "Could not install uv automatically. Install it manually in this terminal:\n" +
            "  powershell -ExecutionPolicy Bypass -c \"irm https://astral.sh/uv/install.ps1 | iex\"\n" +
            "then open a NEW terminal and re-run `token-stack install`.\n" +
            $"standalone: {standalone.StdErr}{standalone.StdOut}\nwinget: {winget.StdErr}{winget.StdOut}");
    }

    /// <summary>The actual uv.exe FILE path (for bundling into an offline pack). EnsureUv may
    /// return the bare command name "uv" when it's on PATH — that can't be File.Copy'd.</summary>
    public string ResolveUvExe()
    {
        var local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "uv.exe");
        if (File.Exists(local)) return local;

        var where = runner.Run("where", "uv", 10000);
        if (where.Ok)
        {
            var hit = where.StdOut.Split('\n', '\r')
                .Select(s => s.Trim())
                .FirstOrDefault(s => s.EndsWith("uv.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(s));
            if (hit is not null) return hit;
        }
        throw new InvalidOperationException(
            "could not resolve the uv.exe file path for bundling — ensure uv is installed " +
            "(https://docs.astral.sh/uv/).");
    }
}
