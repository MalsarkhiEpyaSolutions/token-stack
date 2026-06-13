using Spectre.Console;
using Spectre.Console.Cli;
using TokenStack.Core.Install;

namespace TokenStack.Cli.Commands;

public sealed class InstallCommand : Command<InstallCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--component <NAME>")]
        public string? Component { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var cfg = Services.LoadConfigOrDefault();
        if (settings.Component is { } only)
        {
            // narrow THIS RUN only — persistConfig:false below keeps config.json intact
            cfg.Headroom.Enabled = cfg.Headroom.Enabled && only == "headroom";
            cfg.Rtk.Enabled = cfg.Rtk.Enabled && only == "rtk";
            cfg.Semble.Enabled = cfg.Semble.Enabled && only == "semble";
        }
        var pipeline = new InstallPipeline(
            Services.Runner, Services.Env, Services.Port, Services.Http,
            msg => AnsiConsole.MarkupLineInterpolated($"[grey]{msg}[/]"));
        try
        {
            pipeline.Run(cfg, persistConfig: settings.Component is null);
            AnsiConsole.MarkupLine("[green]Install complete.[/] Run [bold]token-stack status[/] after restarting Claude.");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Install failed:[/] {ex.Message}");
            AnsiConsole.MarkupLine("[yellow]Re-running `token-stack install` resumes from the failed step.[/]");
            return 1;
        }
    }
}
