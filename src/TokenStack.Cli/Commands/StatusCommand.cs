using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using TokenStack.Core.Status;

namespace TokenStack.Cli.Commands;

public sealed class StatusCommand : Command<StatusCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--hook")] public bool Hook { get; init; }
        [CommandOption("--json")] public bool Json { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var cfg = Services.LoadConfigOrDefault();
        var probe = new StatusProbe(Services.Runner, Services.Env, Services.Port, Services.Http);

        if (settings.Hook)
        {
            // Session hook: must be fast, must never throw, exactly one plain line.
            var s = probe.GatherForHook(cfg, Services.ClaudeJsonPath);
            Console.WriteLine(StatusLine.Build(s));
            return 0;
        }

        var status = probe.Gather(cfg, Services.ClaudeJsonPath, statsTimeoutMs: 25000);
        if (settings.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(status));
            return 0;
        }

        var t = new Table().AddColumns("Layer", "State", "Detail");
        t.AddRow("Headroom",
            !status.TaskRunning ? "[red]DOWN[/]" : status.PortListening ? "[green]up[/]" : "[yellow]starting[/]",
            $"port {status.Port}, {(status.Routed ? "[green]ROUTED[/]" : "[red]BYPASSED[/]")}" +
            (status.Reqs is { } n ? $", reqs={n}" : ""));
        t.AddRow("RTK", status.RtkOnPath ? "[green]up[/]" : "[red]MISSING[/]", "PreToolUse Bash filter");
        t.AddRow("Semble", status.SembleWired ? "[green]up[/]" : "[red]MISSING[/]", "stdio MCP (absolute exe)");
        AnsiConsole.Write(t);
        Console.WriteLine();
        Console.WriteLine(StatusLine.Build(status));
        return status is { TaskRunning: true, PortListening: true, RtkOnPath: true, SembleWired: true } ? 0 : 1;
    }
}
