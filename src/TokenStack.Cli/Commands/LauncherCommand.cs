using Spectre.Console;
using Spectre.Console.Cli;
using TokenStack.Core.Components;

namespace TokenStack.Cli.Commands;

/// <summary>Interactive "make a model launcher" screen. Numbered (not arrow-menu, which some
/// Windows consoles don't render) so it works everywhere. Generates a desktop .cmd that runs
/// Claude on a chosen backend in its own window — parallel, no global change.</summary>
public sealed class LauncherCommand : Command
{
    protected override int Execute(CommandContext context, CancellationToken ct)
    {
        var known = Providers.SetupChoices; // GLM, Kimi, MiniMax, OpenRouter
        AnsiConsole.MarkupLine("Pick a backend for this launcher [grey](type a number)[/]:");
        for (var i = 0; i < known.Length; i++)
            AnsiConsole.MarkupLineInterpolated($"  [bold]{i + 1}[/]) {known[i].Label}  [grey]({known[i].Endpoint})[/]");
        var customN = known.Length + 1;
        AnsiConsole.MarkupLineInterpolated($"  [bold]{customN}[/]) Custom  [grey](any Anthropic-compatible endpoint — any model)[/]");

        var pick = AnsiConsole.Prompt(new TextPrompt<int>("Number:")
            .Validate(n => n >= 1 && n <= customN
                ? ValidationResult.Success()
                : ValidationResult.Error($"enter 1-{customN}")));

        string label, baseUrl;
        string? suggested = null;
        if (pick == customN)
        {
            label = AnsiConsole.Ask<string>("Name for this launcher [grey](e.g. DeepSeek)[/]:");
            baseUrl = AnsiConsole.Ask<string>("Anthropic-compatible base URL:");
        }
        else
        {
            label = known[pick - 1].Label;
            baseUrl = known[pick - 1].Endpoint;
            suggested = known[pick - 1].DefaultModel;
        }

        var modelPrompt = new TextPrompt<string>("Model id [grey](type any model you want)[/]:");
        if (suggested is not null) modelPrompt.DefaultValue(suggested);
        var model = AnsiConsole.Prompt(modelPrompt);

        var key = AnsiConsole.Prompt(
            new TextPrompt<string>($"Paste the [bold]{label}[/] API key [grey](hidden)[/]:").Secret());

        var path = LauncherWriter.WriteToDesktop(label, baseUrl, key, model);

        AnsiConsole.MarkupLineInterpolated($"[green]Created[/] {path}");
        AnsiConsole.MarkupLineInterpolated(
            $"Double-click it to run Claude on [bold]{label}[/] ({model}) in its own window — alongside your normal Claude.");
        AnsiConsole.MarkupLine("[yellow]Note:[/] your key is saved in that .cmd in plain text — keep the file private.");
        return 0;
    }
}
