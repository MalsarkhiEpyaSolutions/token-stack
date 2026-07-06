namespace TokenStack.Core.Components;

/// <summary>Generates a per-model launcher .cmd: it sets the backend env vars for THAT window
/// only, then runs `claude`. So MiniMax / OpenRouter / any endpoint run in parallel, each in
/// its own terminal, without touching your normal global Claude.</summary>
public static class LauncherWriter
{
    // ponytail: key is stored in the .cmd in plain text; fine for a local launcher. A key with
    // a literal `%`/`&`/`^` would need cmd-escaping — none of the vendor keys use those.
    // Isolated home (USERPROFILE/HOME/CLAUDE_CONFIG_DIR → a per-model dir) so this window has NO
    // claude.ai OAuth session — a logged-in session otherwise overrides ANTHROPIC_AUTH_TOKEN and
    // Claude sends the wrong bearer to the vendor → 401. With an empty home, the AUTH_TOKEN (Bearer)
    // is used cleanly (vendors accept it), and the user's REAL Claude login (default home) is
    // untouched — no /logout. Verified live against MiniMax.
    public static string BuildCmd(string label, string baseUrl, string token, string model)
    {
        var slug = ProfileWiring.Slug(label);
        return
            "@echo off\r\n" +
            "rem TokenSaver launcher - isolated Claude identity, this window only.\r\n" +
            "rem No /logout; your real Claude login (default home) is untouched.\r\n" +
            $"set \"TS_HOME=%LOCALAPPDATA%\\TokenSaver\\home-{slug}\"\r\n" +
            "if not exist \"%TS_HOME%\\.claude\" mkdir \"%TS_HOME%\\.claude\"\r\n" +
            "set \"USERPROFILE=%TS_HOME%\"\r\n" +
            "set \"HOME=%TS_HOME%\"\r\n" +
            "set \"CLAUDE_CONFIG_DIR=%TS_HOME%\\.claude\"\r\n" +
            $"set \"ANTHROPIC_BASE_URL={baseUrl}\"\r\n" +
            $"set \"ANTHROPIC_AUTH_TOKEN={token}\"\r\n" +
            $"set \"ANTHROPIC_MODEL={model}\"\r\n" +
            "claude %*\r\n";
    }

    /// <summary>Writes "Claude - &lt;label&gt;.cmd" to the desktop; returns its full path.</summary>
    public static string WriteToDesktop(string label, string baseUrl, string token, string model)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var path = Path.Combine(desktop, $"Claude - {label}.cmd");
        File.WriteAllText(path, BuildCmd(label, baseUrl, token, model));
        return path;
    }
}
