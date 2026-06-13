using Spectre.Console;
using Spectre.Console.Cli;
using TokenStack.Core.Components;
using TokenStack.Core.Install;

namespace TokenStack.Cli.Commands;

/// <summary>Build an offline bundle on an online machine (requires a prior online install so
/// rtk.exe + the HuggingFace models exist locally).</summary>
public sealed class PackCommand : Command<PackCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--out <PATH>")]
        public string? Out { get; init; }
    }

    protected override int Execute(CommandContext context, Settings s, CancellationToken cancellationToken)
    {
        var cfg = Services.LoadConfigOrDefault();
        var outZip = s.Out ?? Path.Combine(Directory.GetCurrentDirectory(), "token-stack-offline-v1.0.0.zip");
        try
        {
            var uv = new Bootstrap(Services.Runner).EnsureUv();
            AnsiConsole.MarkupLine("[bold]Packing offline bundle[/] (uv + portable python + wheelhouse + rtk + HF models)...");
            OfflinePacker.Pack(cfg, Services.Runner, uv, outZip,
                m => AnsiConsole.MarkupLineInterpolated($"[grey]{m}[/]"));
            AnsiConsole.MarkupLineInterpolated($"[green]Offline bundle ready:[/] {outZip}");
            AnsiConsole.MarkupLine(
                "Hand it to an air-gapped machine → unzip → [bold]token-stack.exe install[/] " +
                "(auto-detects the vendor\\ bundle and installs with zero network).");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]pack failed:[/] {ex.Message}");
            return 1;
        }
    }
}
