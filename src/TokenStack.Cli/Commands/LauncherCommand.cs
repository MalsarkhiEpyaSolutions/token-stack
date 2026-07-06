using Spectre.Console;
using Spectre.Console.Cli;
using TokenStack.Core.Components;

namespace TokenStack.Cli.Commands;

/// <summary>Interactive "add a model launcher" screen. Generates a desktop .cmd that runs Claude
/// Code against a chosen backend (GLM/Kimi/MiniMax/OpenRouter, or any custom Anthropic-compatible
/// endpoint) in its own window — so they all run in parallel and your normal Claude is untouched.</summary>
public sealed class LauncherCommand : Command
{
    private static readonly Providers.SetupChoice Custom =
        new("Custom", "", ""); // sentinel: prompt for URL + model

    protected override int Execute(CommandContext context, CancellationToken ct)
    {
        var choices = new List<Providers.SetupChoice>(Providers.SetupChoices) { Custom };
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<Providers.SetupChoice>()
                .Title("Which backend should this launcher run [bold]Claude[/] on?")
                .UseConverter(c => c.Label == "Custom"
                    ? "Custom  (any Anthropic-compatible endpoint)"
                    : $"{c.Label}  ({c.Endpoint})")
                .AddChoices(choices));

        var label = choice.Label;
        var baseUrl = choice.Endpoint;
        var model = choice.DefaultModel;
        if (choice.Label == "Custom")
        {
            label = AnsiConsole.Prompt(new TextPrompt<string>("Name for this launcher (e.g. DeepSeek):"));
            baseUrl = AnsiConsole.Prompt(new TextPrompt<string>("Anthropic-compatible base URL:"));
            model = AnsiConsole.Prompt(new TextPrompt<string>("Model id:"));
        }
        else
        {
            model = AnsiConsole.Prompt(
                new TextPrompt<string>("Model (Enter for the suggested one):").DefaultValue(choice.DefaultModel));
        }

        var key = AnsiConsole.Prompt(
            new TextPrompt<string>($"Paste the [bold]{label}[/] API key:").Secret());

        var path = LauncherWriter.WriteToDesktop(label, baseUrl, key, model);

        AnsiConsole.MarkupLineInterpolated($"[green]Created[/] {path}");
        AnsiConsole.MarkupLineInterpolated(
            $"Double-click it to run Claude on [bold]{label}[/] — in its own window, alongside your normal Claude.");
        AnsiConsole.MarkupLine(
            "[yellow]Note:[/] your key is saved in that .cmd in plain text — keep the file private.");
        return 0;
    }
}
