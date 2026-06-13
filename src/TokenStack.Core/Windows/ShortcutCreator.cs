using System.Text;

namespace TokenStack.Core.Windows;

public sealed record ShortcutSpec(string FileName, string Arguments);

/// <summary>Creates/removes the desktop toggle buttons via WScript.Shell COM (through
/// PowerShell — no extra dependency, works on every Windows edition):
///   • a loose "Token Stack" shortcut = the whole-stack quick toggle,
///   • a "Token Stack Controls" folder holding one per-layer toggle (Headroom/RTK/Semble).</summary>
public sealed class ShortcutCreator(IProcessRunner runner)
{
    private static string Desktop =>
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public static string WholeStackPath => Path.Combine(Desktop, "Token Stack.lnk");
    public static string ControlsFolder => Path.Combine(Desktop, "Token Stack Controls");

    /// <summary>The per-layer toggles that live inside the controls folder.</summary>
    public static IReadOnlyList<ShortcutSpec> LayerSpecs() => new[]
    {
        new ShortcutSpec("Headroom (toggle).lnk", "toggle headroom --notify"),
        new ShortcutSpec("RTK (toggle).lnk", "toggle rtk --notify"),
        new ShortcutSpec("Semble (toggle).lnk", "toggle semble --notify"),
    };

    /// <summary>Create the loose whole-stack button + the per-layer folder. Returns the folder.</summary>
    public string CreateAll(string exePath)
    {
        Directory.CreateDirectory(ControlsFolder);

        var ps = new StringBuilder("$w=New-Object -ComObject WScript.Shell;");
        Append(ps, WholeStackPath, exePath, "toggle --notify");
        foreach (var s in LayerSpecs())
            Append(ps, Path.Combine(ControlsFolder, s.FileName), exePath, s.Arguments);

        var r = runner.Run("powershell", $"-NoProfile -ExecutionPolicy Bypass -Command \"{ps}\"", 30000);
        if (!r.Ok)
            throw new InvalidOperationException($"could not create desktop shortcuts: {r.StdErr}{r.StdOut}");
        return ControlsFolder;
    }

    private static void Append(StringBuilder ps, string lnk, string exe, string args) =>
        ps.Append($"$s=$w.CreateShortcut('{lnk}');$s.TargetPath='{exe}';$s.Arguments='{args}';")
          .Append($"$s.IconLocation='{exe},0';$s.WindowStyle=7;$s.Description='token-stack toggle';$s.Save();");

    public void Remove()
    {
        try { if (File.Exists(WholeStackPath)) File.Delete(WholeStackPath); } catch { /* best effort */ }
        try { if (Directory.Exists(ControlsFolder)) Directory.Delete(ControlsFolder, recursive: true); }
        catch { /* best effort */ }
    }
}
