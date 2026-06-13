using TokenStack.Core.Windows;

namespace TokenStack.Core.Components;

/// <summary>Locates or installs uv (the only bootstrap dependency; it then provisions
/// Python for Headroom and the tool install for Semble).</summary>
public sealed class Bootstrap(IProcessRunner runner)
{
    /// <summary>Returns the uv invocation path, installing uv if needed.
    /// Order: PATH → known install dir → winget → standalone installer.</summary>
    public string EnsureUv()
    {
        if (runner.Run("uv", "--version", 15000).Ok) return "uv";

        var local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "uv.exe");
        if (File.Exists(local)) return local;

        var winget = runner.Run("winget",
            "install --id astral-sh.uv -e --accept-source-agreements --accept-package-agreements",
            300000);
        if (!winget.Ok)
        {
            var standalone = runner.Run("powershell",
                "-NoProfile -ExecutionPolicy Bypass -Command \"irm https://astral.sh/uv/install.ps1 | iex\"",
                300000);
            if (!standalone.Ok)
                throw new InvalidOperationException(
                    "Could not install uv via winget or the standalone installer. " +
                    "Install manually from https://docs.astral.sh/uv/ then re-run. " +
                    $"winget: {winget.StdErr} | standalone: {standalone.StdErr}");
        }

        if (runner.Run("uv", "--version", 15000).Ok) return "uv";
        if (File.Exists(local)) return local;
        throw new InvalidOperationException(
            "uv was installed but is not yet resolvable in this process. " +
            "Open a new terminal and re-run `token-stack install`.");
    }
}
