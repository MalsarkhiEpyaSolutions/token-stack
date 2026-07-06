namespace TokenStack.Core.Components;

/// <summary>Generates a per-model launcher .cmd: it sets the backend env vars for THAT window
/// only, then runs `claude`. So MiniMax / OpenRouter / any endpoint run in parallel, each in
/// its own terminal, without touching your normal global Claude.</summary>
public static class LauncherWriter
{
    // ponytail: key is stored in the .cmd in plain text; fine for a local launcher. A key with
    // a literal `%`/`&`/`^` would need cmd-escaping — none of the vendor keys use those.
    public static string BuildCmd(string baseUrl, string token, string model) =>
        "@echo off\r\n" +
        "rem TokenSaver launcher - Claude Code on one backend, this window only.\r\n" +
        $"set \"ANTHROPIC_BASE_URL={baseUrl}\"\r\n" +
        $"set \"ANTHROPIC_AUTH_TOKEN={token}\"\r\n" +
        $"set \"ANTHROPIC_MODEL={model}\"\r\n" +
        "claude %*\r\n";

    /// <summary>Writes "Claude - &lt;label&gt;.cmd" to the desktop; returns its full path.</summary>
    public static string WriteToDesktop(string label, string baseUrl, string token, string model)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var path = Path.Combine(desktop, $"Claude - {label}.cmd");
        File.WriteAllText(path, BuildCmd(baseUrl, token, model));
        return path;
    }
}
