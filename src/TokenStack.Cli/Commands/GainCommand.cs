using Spectre.Console;
using Spectre.Console.Cli;
using TokenStack.Core.Components;

namespace TokenStack.Cli.Commands;

public sealed class GainCommand : Command
{
    protected override int Execute(CommandContext context, CancellationToken cancellationToken)
    {
        var cfg = Services.LoadConfigOrDefault();

        AnsiConsole.MarkupLine("[bold]RTK[/] (command-output filter):");
        var rtk = Services.Runner.Run("rtk", "gain", 30000);
        Console.WriteLine(rtk.Ok ? rtk.StdOut : "  rtk unreachable (not on PATH?)");

        AnsiConsole.MarkupLine($"[bold]Headroom[/] (/stats on :{cfg.Headroom.Port} — can take ~20s):");
        var headroom = new HeadroomComponent(Services.Runner, Services.Port, Services.Http);
        var reqs = headroom.ReadApiRequests(cfg.Headroom.Port, timeoutMs: 25000);
        Console.WriteLine(reqs is { } n
            ? $"  api_requests served through the proxy: {n}"
            : "  proxy unreachable (down or cold-loading)");
        Console.WriteLine("  full dashboard: http://127.0.0.1:" + cfg.Headroom.Port + "/stats");
        return 0;
    }
}
