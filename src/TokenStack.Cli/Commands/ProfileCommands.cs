using Spectre.Console;
using Spectre.Console.Cli;
using TokenStack.Core.Components;
using TokenStack.Core.Config;
using TokenStack.Core.Windows;

namespace TokenStack.Cli.Commands;

internal static class ProfileHelp
{
    public static StackConfig Load() => Services.LoadConfigOrDefault();
    public static void Save(StackConfig c) => ConfigStore.Save(c, ConfigStore.DefaultPath);

    public static ProfileConfig? Find(StackConfig c, string name) =>
        c.Profiles.FirstOrDefault(p =>
            ProfileWiring.Slug(p.Name).Equals(ProfileWiring.Slug(name), System.StringComparison.OrdinalIgnoreCase));
}

public sealed class ProfileNameSettings : CommandSettings
{
    [CommandArgument(0, "<name>")] public string Name { get; set; } = "";
}

/// <summary>Interactive: add a model that runs its OWN compression proxy on its own port, in
/// parallel with Claude and other profiles. Creates a desktop launcher + on/off toggle.</summary>
public sealed class ProfileAddCommand : Command
{
    protected override int Execute(CommandContext context, CancellationToken ct)
    {
        var cfg = ProfileHelp.Load();
        var pythonw = Path.Combine(cfg.InstallRoot, "venv", "Scripts", "pythonw.exe");
        if (!File.Exists(pythonw))
        {
            AnsiConsole.MarkupLine("[red]Run [bold]token-saver install[/] first[/] — profiles reuse its proxy venv.");
            return 1;
        }

        var known = Providers.SetupChoices;
        AnsiConsole.MarkupLine("Pick a backend for this model [grey](type a number)[/]:");
        for (var i = 0; i < known.Length; i++)
            AnsiConsole.MarkupLineInterpolated($"  [bold]{i + 1}[/]) {known[i].Label}  [grey]({known[i].Endpoint})[/]");
        var customN = known.Length + 1;
        AnsiConsole.MarkupLineInterpolated($"  [bold]{customN}[/]) Custom  [grey](any Anthropic-compatible endpoint)[/]");
        var pick = AnsiConsole.Prompt(new TextPrompt<int>("Number:")
            .Validate(n => n >= 1 && n <= customN ? ValidationResult.Success() : ValidationResult.Error($"enter 1-{customN}")));

        string label, upstream;
        string? suggested = null;
        if (pick == customN)
        {
            label = AnsiConsole.Ask<string>("Name for this model [grey](e.g. DeepSeek)[/]:");
            upstream = AnsiConsole.Ask<string>("Anthropic-compatible base URL:");
        }
        else { label = known[pick - 1].Label; upstream = known[pick - 1].Endpoint; suggested = known[pick - 1].DefaultModel; }

        var modelPrompt = new TextPrompt<string>("Model id [grey](type any model)[/]:");
        if (suggested is not null) modelPrompt.DefaultValue(suggested);
        var model = AnsiConsole.Prompt(modelPrompt);
        var key = AnsiConsole.Prompt(new TextPrompt<string>($"Paste the [bold]{label}[/] API key [grey](hidden)[/]:").Secret());

        var port = ProfilePorts.NextFree(cfg.Profiles.Select(p => p.Port));
        cfg.Profiles.RemoveAll(p => ProfileWiring.Slug(p.Name).Equals(ProfileWiring.Slug(label), System.StringComparison.OrdinalIgnoreCase));
        var profile = new ProfileConfig { Name = label, Upstream = upstream, Model = model, Port = port };
        cfg.Profiles.Add(profile);
        ProfileHelp.Save(cfg);

        AnsiConsole.MarkupLineInterpolated($"Starting [bold]{label}[/] proxy on :{port} (cold load 25-105s)...");
        new ProfileService(Services.Runner).Install(cfg, profile);

        var exe = Path.Combine(cfg.InstallRoot, Branding.ExeName);
        var cmd = LauncherWriter.Write(label, ProfileWiring.ProxyUrl(port), key, model);
        var toggle = new ShortcutCreator(Services.Runner).CreateProfileToggle(exe, label, ProfileWiring.Slug(label));

        AnsiConsole.MarkupLineInterpolated($"[green]Added {label}[/] → proxy :{port} → {upstream}");
        AnsiConsole.MarkupLineInterpolated($"  launcher: {cmd}");
        AnsiConsole.MarkupLineInterpolated($"  on/off button: {toggle}");
        AnsiConsole.MarkupLine("Double-click the launcher to use it (compressed + in parallel with Claude).");
        return 0;
    }
}

public sealed class ProfileListCommand : Command
{
    protected override int Execute(CommandContext context, CancellationToken ct)
    {
        var cfg = ProfileHelp.Load();
        if (cfg.Profiles.Count == 0) { AnsiConsole.MarkupLine("[grey]No profiles. Add one: token-saver profile add[/]"); return 0; }
        var svc = new ProfileService(Services.Runner);
        var table = new Table().AddColumns("Model", "Port", "Upstream", "State");
        foreach (var p in cfg.Profiles)
            table.AddRow(p.Name, p.Port.ToString(), p.Upstream, svc.IsOn(p) ? "[green]ON[/]" : "OFF");
        AnsiConsole.Write(table);
        return 0;
    }
}

public sealed class ProfileOnCommand : Command<ProfileNameSettings>
{
    protected override int Execute(CommandContext context, ProfileNameSettings s, CancellationToken ct) =>
        Toggle(s.Name, force: true);
    internal static int Toggle(string name, bool force)
    {
        var cfg = ProfileHelp.Load();
        var p = ProfileHelp.Find(cfg, name);
        if (p is null) { AnsiConsole.MarkupLineInterpolated($"[red]No profile named {name}[/]"); return 1; }
        new ProfileService(Services.Runner).SetEnabled(p, on: true);
        AnsiConsole.MarkupLineInterpolated($"[green]{p.Name} ON[/] (:{p.Port})");
        return 0;
    }
}

public sealed class ProfileOffCommand : Command<ProfileNameSettings>
{
    protected override int Execute(CommandContext context, ProfileNameSettings s, CancellationToken ct)
    {
        var cfg = ProfileHelp.Load();
        var p = ProfileHelp.Find(cfg, s.Name);
        if (p is null) { AnsiConsole.MarkupLineInterpolated($"[red]No profile named {s.Name}[/]"); return 1; }
        new ProfileService(Services.Runner).SetEnabled(p, on: false);
        AnsiConsole.MarkupLineInterpolated($"[yellow]{p.Name} OFF[/]");
        return 0;
    }
}

public sealed class ProfileToggleCommand : Command<ProfileNameSettings>
{
    protected override int Execute(CommandContext context, ProfileNameSettings s, CancellationToken ct)
    {
        var cfg = ProfileHelp.Load();
        var p = ProfileHelp.Find(cfg, s.Name);
        if (p is null) { AnsiConsole.MarkupLineInterpolated($"[red]No profile named {s.Name}[/]"); return 1; }
        var svc = new ProfileService(Services.Runner);
        var now = !svc.IsOn(p);
        svc.SetEnabled(p, now);
        AnsiConsole.MarkupLineInterpolated($"{p.Name} {(now ? "[green]ON[/]" : "[yellow]OFF[/]")}");
        return 0;
    }
}

public sealed class ProfileRemoveCommand : Command<ProfileNameSettings>
{
    protected override int Execute(CommandContext context, ProfileNameSettings s, CancellationToken ct)
    {
        var cfg = ProfileHelp.Load();
        var p = ProfileHelp.Find(cfg, s.Name);
        if (p is null) { AnsiConsole.MarkupLineInterpolated($"[red]No profile named {s.Name}[/]"); return 1; }
        new ProfileService(Services.Runner).Remove(cfg, p);
        cfg.Profiles.RemoveAll(x => ReferenceEquals(x, p));
        ProfileHelp.Save(cfg);
        AnsiConsole.MarkupLineInterpolated($"[green]Removed {p.Name}[/]. (Delete its desktop launcher/toggle manually.)");
        return 0;
    }
}
