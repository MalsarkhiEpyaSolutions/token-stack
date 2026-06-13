using Spectre.Console;
using Spectre.Console.Cli;
using TokenStack.Core.Components;
using TokenStack.Core.Config;

namespace TokenStack.Cli.Commands;

public sealed class UpdateCommand : Command<UpdateCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--component <NAME>")] public string Component { get; init; } = "";
        [CommandOption("--version <VER>")] public string? Version { get; init; }
    }

    protected override int Execute(CommandContext context, Settings s, CancellationToken cancellationToken)
    {
        var path = ConfigStore.DefaultPath;
        if (!File.Exists(path))
        {
            AnsiConsole.MarkupLine("[red]No config yet[/] — run [bold]token-stack install[/] first.");
            return 1;
        }
        var cfg = ConfigStore.Load(path);
        var uv = new Bootstrap(Services.Runner).EnsureUv();
        switch (s.Component)
        {
            case "headroom":
            {
                if (s.Version is not null) { cfg.Headroom.Version = s.Version; ConfigStore.Save(cfg, path); }
                var py = Path.Combine(cfg.InstallRoot, "venv", "Scripts", "python.exe");
                var r = Services.Runner.Run(uv,
                    $"pip install --python {py} headroom-ai[proxy]=={cfg.Headroom.Version}", 600000);
                if (!r.Ok) { AnsiConsole.MarkupLineInterpolated($"[red]{r.StdErr}[/]"); return 1; }
                AnsiConsole.MarkupLine("[green]updated headroom[/] — run `token-stack restart` to load it.");
                return 0;
            }
            case "rtk":
                if (s.Version is not null) { cfg.Rtk.Version = s.Version; ConfigStore.Save(cfg, path); }
                new RtkComponent(Services.Runner, Services.Env).Install(cfg, InstallSource.Online);
                AnsiConsole.MarkupLine("[green]updated rtk[/]");
                return 0;
            case "semble":
                if (s.Version is not null) { cfg.Semble.Version = s.Version; ConfigStore.Save(cfg, path); }
                new SembleComponent(Services.Runner).Install(cfg.Semble, uv, InstallSource.Online, skipIfPresent: false);
                AnsiConsole.MarkupLine("[green]updated semble[/]");
                return 0;
            default:
                AnsiConsole.MarkupLine("usage: token-stack update --component headroom|rtk|semble [--version <v>]");
                return 1;
        }
    }
}
