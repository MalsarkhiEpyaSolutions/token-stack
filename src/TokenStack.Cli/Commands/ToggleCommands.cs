using Spectre.Console;
using Spectre.Console.Cli;
using TokenStack.Core.Components;
using TokenStack.Core.Config;
using TokenStack.Core.Windows;

namespace TokenStack.Cli.Commands;

internal enum ToggleMode { On, Off, Toggle }

internal static class ToggleRunner
{
    public static int Run(string? layerArg, ToggleMode mode, bool notify)
    {
        var path = ConfigStore.DefaultPath;
        if (!File.Exists(path))
        {
            AnsiConsole.MarkupLine("[red]No config[/] — run [bold]token-stack install[/] first.");
            return 1;
        }
        var cfg = ConfigStore.Load(path);

        StackLayer layer;
        try { layer = ToggleLogic.ParseLayer(layerArg); }
        catch (ArgumentException ex) { AnsiConsole.MarkupLineInterpolated($"[red]{ex.Message}[/]"); return 1; }

        switch (mode)
        {
            case ToggleMode.On: ToggleLogic.SetFlag(cfg, layer, true); break;
            case ToggleMode.Off: ToggleLogic.SetFlag(cfg, layer, false); break;
            case ToggleMode.Toggle: ToggleLogic.Flip(cfg, layer); break;
        }

        var state = new ToggleService(
            Services.Runner, Services.Env, Services.SettingsPath, Services.ClaudeJsonPath).ApplyWiring(cfg);
        ConfigStore.Save(cfg, path);

        string On(bool b) => b ? "[green]ON[/]" : "[red]OFF[/]";
        AnsiConsole.MarkupLine($"Headroom: {On(state.Headroom)}  RTK: {On(state.Rtk)}  Semble: {On(state.Semble)}");
        AnsiConsole.MarkupLine("[yellow]Restart Claude for the change to take effect[/] " +
            "(Desktop: tray → Quit → relaunch).");
        if (notify) ShowNotification(state);
        return 0;
    }

    /// <summary>A universal MessageBox (WPF ships with Windows) so the desktop button gives clear
    /// feedback. Best-effort — the console line above is the real source of truth.</summary>
    private static void ShowNotification(LayerState s)
    {
        string Onoff(bool b) => b ? "ON" : "OFF";
        var msg = $"Headroom: {Onoff(s.Headroom)}  |  RTK: {Onoff(s.Rtk)}  |  Semble: {Onoff(s.Semble)}" +
                  "      (restart Claude to apply)";
        var anyOn = s.Headroom || s.Rtk || s.Semble;
        var title = anyOn ? "Token Stack" : "Token Stack - OFF";
        var ps = "Add-Type -AssemblyName PresentationFramework;" +
                 $"[System.Windows.MessageBox]::Show('{msg}','{title}')|Out-Null";
        try { Services.Runner.Run("powershell", $"-NoProfile -Command \"{ps}\"", 60000); }
        catch { /* notification is optional */ }
    }
}

public sealed class OnCommand : Command<OnCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[layer]")] public string? Layer { get; init; }
    }
    protected override int Execute(CommandContext context, Settings s, CancellationToken ct) =>
        ToggleRunner.Run(s.Layer, ToggleMode.On, notify: false);
}

public sealed class OffCommand : Command<OffCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[layer]")] public string? Layer { get; init; }
    }
    protected override int Execute(CommandContext context, Settings s, CancellationToken ct) =>
        ToggleRunner.Run(s.Layer, ToggleMode.Off, notify: false);
}

public sealed class ToggleCommand : Command<ToggleCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[layer]")] public string? Layer { get; init; }
        [CommandOption("--notify")] public bool Notify { get; init; }   // set by the desktop button
    }
    protected override int Execute(CommandContext context, Settings s, CancellationToken ct) =>
        ToggleRunner.Run(s.Layer, ToggleMode.Toggle, s.Notify);
}

public sealed class ShortcutCommand : Command
{
    protected override int Execute(CommandContext context, CancellationToken ct)
    {
        var exe = Path.Combine(Services.LoadConfigOrDefault().InstallRoot, Branding.ExeName);
        if (!File.Exists(exe)) exe = Environment.ProcessPath ?? exe;
        try
        {
            var folder = new ShortcutCreator(Services.Runner).CreateAll(exe);
            AnsiConsole.MarkupLine("[green]Desktop buttons created:[/]");
            AnsiConsole.MarkupLine("  • [bold]Token Stack[/] (loose icon) — toggles the whole stack");
            AnsiConsole.MarkupLineInterpolated($"  • [bold]{folder}[/] — folder with per-layer toggles (Headroom/RTK/Semble)");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]{ex.Message}[/]");
            return 1;
        }
    }
}
