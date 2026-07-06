using Spectre.Console;
using Spectre.Console.Cli;
using TokenStack.Core.Claude;
using TokenStack.Core.Components;

namespace TokenStack.Cli.Commands;

/// <summary>Interactive "add your vendor API key" screen. Writes the vendor endpoint + key +
/// model into Claude Code's settings.json env (the secure, canonical place — never into
/// TokenSaver's own config). `install` then detects and routes it through the proxy.</summary>
public sealed class SetupCommand : Command
{
    protected override int Execute(CommandContext context, CancellationToken ct)
    {
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<Providers.SetupChoice>()
                .Title("Which provider is your [bold]Claude Code[/] using?")
                .UseConverter(c => $"{c.Label}  ({c.Endpoint})")
                .AddChoices(Providers.SetupChoices));

        var key = AnsiConsole.Prompt(
            new TextPrompt<string>($"Paste your [bold]{choice.Label}[/] API key:").Secret());

        var model = AnsiConsole.Prompt(
            new TextPrompt<string>("Model (Enter for the suggested one):").DefaultValue(choice.DefaultModel));

        var editor = new ClaudeFileEditor(Services.SettingsPath);
        var settings = editor.Load();
        ClaudeSurgeon.SetEnvVar(settings, "ANTHROPIC_BASE_URL", choice.Endpoint);
        ClaudeSurgeon.SetEnvVar(settings, "ANTHROPIC_AUTH_TOKEN", key);
        ClaudeSurgeon.SetEnvVar(settings, "ANTHROPIC_MODEL", model);
        editor.SaveWithBackup(settings);

        AnsiConsole.MarkupLineInterpolated(
            $"[green]{choice.Label} written to Claude settings.json[/] (your key lives there, not in TokenSaver).");
        AnsiConsole.MarkupLineInterpolated(
            $"Next: run [bold]token-saver install[/] to route {choice.Label} through the proxy, then fully quit + relaunch Claude.");
        return 0;
    }
}
