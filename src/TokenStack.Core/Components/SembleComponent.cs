using TokenStack.Core.Config;
using TokenStack.Core.Windows;

namespace TokenStack.Core.Components;

public sealed class SembleComponent(IProcessRunner runner)
{
    /// <summary>The [mcp] extra is mandatory — without it the exe cannot serve MCP.</summary>
    public static string InstallSpec(SembleConfig cfg) =>
        cfg.Version == "latest" ? "semble[mcp]" : $"semble[mcp]=={cfg.Version}";

    /// <summary>uv tool exes land in %USERPROFILE%\.local\bin — often NOT on PATH, which is
    /// exactly why the MCP registration must use this absolute path (spec §5.5).</summary>
    public static string ExePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".local", "bin", "semble.exe");

    /// <summary>skipIfPresent: an unpinned ("latest") install treats an existing working exe
    /// as satisfied — adoption-friendly and avoids rewriting uv's Roaming tool dir from inside
    /// an MSIX container (live failure: os error 4395 deleting a real dir through the
    /// virtualization overlay). Explicit `update` passes false to force the reinstall.</summary>
    public void Install(SembleConfig cfg, string uvPath, bool verifyExe = true, bool skipIfPresent = true)
    {
        if (skipIfPresent && cfg.Version == "latest" && File.Exists(ExePath()))
            return; // already installed — `token-stack update --component semble` upgrades

        var r = runner.Run(uvPath, $"tool install --force {InstallSpec(cfg)}", 600000);
        if (!r.Ok)
            throw new InvalidOperationException($"uv tool install semble failed: {r.StdErr}{r.StdOut}");
        if (verifyExe && !File.Exists(ExePath()))
            throw new InvalidOperationException(
                $"semble installed but exe not found at {ExePath()} — " +
                "check `uv tool list` and `uv tool dir` for a non-default location.");
    }

    /// <summary>Smoke test against a throwaway folder (first run builds an index; tiny repo
    /// keeps it fast). UTF-8 forced — some semble commands crash on cp1252 consoles.</summary>
    public bool SmokeTest()
    {
        var dir = Path.Combine(Path.GetTempPath(), "token-stack-semble-smoke");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "hello.py"), "def authenticate(user):\n    return True\n");
        var r = runner.Run("cmd",
            $"/c set PYTHONIOENCODING=utf-8&& \"{ExePath()}\" search \"authentication\" \"{dir}\"",
            120000);
        return r.Ok;
    }

    public void Unwire(string uvPath) => runner.Run(uvPath, "tool uninstall semble", 120000);
}
