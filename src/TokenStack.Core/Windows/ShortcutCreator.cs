namespace TokenStack.Core.Windows;

/// <summary>Creates/removes the "Token Stack" desktop shortcut — the one-press button that runs
/// `token-stack toggle --notify`. Uses the WScript.Shell COM object via PowerShell so there's no
/// extra dependency and it works on every Windows edition.</summary>
public sealed class ShortcutCreator(IProcessRunner runner)
{
    public static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Token Stack.lnk");

    public string Create(string exePath)
    {
        var lnk = ShortcutPath;
        var ps =
            $"$s=(New-Object -ComObject WScript.Shell).CreateShortcut('{lnk}');" +
            $"$s.TargetPath='{exePath}';" +
            "$s.Arguments='toggle --notify';" +
            $"$s.IconLocation='{exePath},0';" +
            "$s.WindowStyle=7;" +                              // minimized — console barely flashes
            "$s.Description='Toggle the token stack on/off';" +
            "$s.Save()";
        var r = runner.Run("powershell", $"-NoProfile -ExecutionPolicy Bypass -Command \"{ps}\"", 30000);
        if (!r.Ok)
            throw new InvalidOperationException($"could not create desktop shortcut: {r.StdErr}{r.StdOut}");
        return lnk;
    }

    public void Remove()
    {
        if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
    }
}
