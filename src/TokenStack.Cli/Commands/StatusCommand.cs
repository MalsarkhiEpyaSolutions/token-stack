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
            !status.HeadroomEnabled ? "[grey]OFF[/]"
                : !status.TaskRunning ? "[red]DOWN[/]"
                : status.PortListening ? "[green]up[/]" : "[yellow]starting[/]",
            !status.HeadroomEnabled ? "toggled off (`token-stack on headroom`)"
                : $"port {status.Port}, {(status.Routed ? "[green]ROUTED[/]" : "[red]BYPASSED[/]")}" +
                  (status.Reqs is { } n ? $", reqs={n}" : ""));
        t.AddRow("RTK",
            !status.RtkEnabled ? "[grey]OFF[/]" : status.RtkOnPath ? "[green]up[/]" : "[red]MISSING[/]",
            "PreToolUse Bash filter");
        t.AddRow("Semble",
            !status.SembleEnabled ? "[grey]OFF[/]" : status.SembleWired ? "[green]up[/]" : "[red]MISSING[/]",
            "stdio MCP (absolute exe)");
        t.AddRow("CCO",
            !status.CcoEnabled ? "[grey]OFF[/]" : status.CcoWired ? "[green]up[/]" : "[red]MISSING[/]",
            "PreToolUse Read dedup");
        AnsiConsole.Write(t);
        Console.WriteLine();
        Console.WriteLine(StatusLine.Build(status));

        // Healthy = every ENABLED layer is up; a layer that's intentionally OFF is not a failure.
        var healthy = (!status.HeadroomEnabled || (status.TaskRunning && status.PortListening))
            && (!status.RtkEnabled || status.RtkOnPath)
            && (!status.SembleEnabled || status.SembleWired)
            && (!status.CcoEnabled || status.CcoWired);
        return healthy ? 0 : 1;
    }
}
