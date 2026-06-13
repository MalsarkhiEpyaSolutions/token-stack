using Spectre.Console;
using Spectre.Console.Cli;
using TokenStack.Core.Windows;

namespace TokenStack.Cli.Commands;

public sealed class StartCommand : Command
{
    protected override int Execute(CommandContext context, CancellationToken cancellationToken)
    {
        new ScheduledTaskManager(Services.Runner).Start();
        AnsiConsole.MarkupLine("[green]HeadroomProxy task started[/] (cold load 25-105s before the port binds).");
        return 0;
    }
}

public sealed class StopCommand : Command
{
    protected override int Execute(CommandContext context, CancellationToken cancellationToken)
    {
        new ScheduledTaskManager(Services.Runner).Stop();
        AnsiConsole.MarkupLine("[yellow]HeadroomProxy task stopped.[/] Routed Claude sessions will fail until start.");
        return 0;
    }
}

public sealed class RestartCommand : Command
{
    protected override int Execute(CommandContext context, CancellationToken cancellationToken)
    {
        new ScheduledTaskManager(Services.Runner).RestartWithZombieKill();
        AnsiConsole.MarkupLine("[green]Restarted with zombie recovery.[/]");
        return 0;
    }
}
