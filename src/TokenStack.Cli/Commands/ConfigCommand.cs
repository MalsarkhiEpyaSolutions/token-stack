using System.Diagnostics;
using Spectre.Console;
using Spectre.Console.Cli;
using TokenStack.Core.Config;
using TokenStack.Core.Install;

namespace TokenStack.Cli.Commands;

public sealed class ConfigCommand : Command<ConfigCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<action>")] public string Action { get; init; } = "";
        [CommandArgument(1, "[key]")] public string? Key { get; init; }
        [CommandArgument(2, "[value]")] public string? Value { get; init; }
    }

    protected override int Execute(CommandContext context, Settings s, CancellationToken cancellationToken)
    {
        var path = ConfigStore.DefaultPath;
        if (!File.Exists(path))
        {
            AnsiConsole.MarkupLine("[red]No config yet[/] — run [bold]token-stack install[/] first.");
            return 1;
        }
        switch (s.Action)
        {
            case "list":
                Console.WriteLine(File.ReadAllText(path));
                return 0;
            case "get" when s.Key is not null:
                Console.WriteLine(ConfigStore.GetValue(path, s.Key));
                return 0;
            case "set" when s.Key is not null && s.Value is not null:
                ConfigStore.SetValue(path, s.Key, s.Value);
                // re-apply wiring so the change takes effect (port/hooks/routing/launcher)
                var cfg = ConfigStore.Load(path);
                var pipeline = new InstallPipeline(
                    Services.Runner, Services.Env, Services.Port, Services.Http,
                    m => AnsiConsole.MarkupLineInterpolated($"[grey]{m}[/]"));
                pipeline.ApplyClaudeWiring(cfg);
                if (s.Key.StartsWith("headroom.", StringComparison.Ordinal))
                    AnsiConsole.MarkupLine("[yellow]Headroom setting changed — run `token-stack install --component headroom` to re-render the launcher/task, then `token-stack restart`.[/]");
                AnsiConsole.MarkupLineInterpolated($"[green]{s.Key} = {s.Value}[/]");
                return 0;
            case "open":
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return 0;
            default:
                AnsiConsole.MarkupLine("usage: token-stack config list | get <key> | set <key> <value> | open");
                return 1;
        }
    }
}
