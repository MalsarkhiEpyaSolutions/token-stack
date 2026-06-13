using Spectre.Console;
using Spectre.Console.Cli;
using TokenStack.Core.Claude;
using TokenStack.Core.Doctor;

namespace TokenStack.Cli.Commands;

public sealed class DoctorCommand : Command<DoctorCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--fix")] public bool Fix { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var cfg = Services.LoadConfigOrDefault();
        var settingsEditor = new ClaudeFileEditor(Services.SettingsPath);
        var claudeJsonEditor = new ClaudeFileEditor(Services.ClaudeJsonPath);
        var ctx = new DoctorContext(cfg, settingsEditor.Load(), claudeJsonEditor.Load(),
            Services.Env, Services.Port, Services.Runner, Services.Http);

        var table = new Table().AddColumns("Check", "Result", "Detail");
        var failures = 0;
        foreach (var check in DoctorRegistry.All)
        {
            var r = check.Detect(ctx);
            if (!r.Ok && settings.Fix && check.Fix(ctx))
            {
                var after = check.Detect(ctx);
                table.AddRow(check.Id,
                    after.Ok ? "[green]FIXED[/]" : "[red]STILL FAILING[/]",
                    Markup.Escape(r.Detail));
                if (!after.Ok) failures++;
            }
            else
            {
                table.AddRow(check.Id,
                    r.Ok ? "[green]ok[/]" : r.CanFix ? "[yellow]FAIL (fixable)[/]" : "[red]FAIL[/]",
                    Markup.Escape(r.Detail));
                if (!r.Ok) failures++;
            }
        }
        if (ctx.SettingsChanged) settingsEditor.SaveWithBackup(ctx.Settings);
        if (ctx.ClaudeJsonChanged) claudeJsonEditor.SaveWithBackup(ctx.ClaudeJson);

        AnsiConsole.Write(table);
        if (failures > 0 && !settings.Fix)
            AnsiConsole.MarkupLine("[yellow]Run `token-stack doctor --fix` to apply the safe remediations.[/]");
        return failures == 0 ? 0 : 1;
    }
}
