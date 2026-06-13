using Spectre.Console;
using Spectre.Console.Cli;
using TokenStack.Core.Install;

namespace TokenStack.Cli.Commands;

public sealed class UninstallCommand : Command<UninstallCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--keep-config")] public bool KeepConfig { get; init; }
        [CommandOption("-y|--yes")] public bool Yes { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!settings.Yes &&
            !AnsiConsole.Confirm("Remove the token stack (task, env vars, hooks, MCP entry)?", false))
            return 1;
        var cfg = Services.LoadConfigOrDefault();
        new InstallPipeline(Services.Runner, Services.Env, Services.Port, Services.Http,
                m => AnsiConsole.MarkupLineInterpolated($"[grey]{m}[/]"))
            .Uninstall(cfg, settings.KeepConfig);
        AnsiConsole.MarkupLine("[green]Uninstalled.[/] Claude config backups (*.token-stack-backup-*) were kept.");
        return 0;
    }
}
