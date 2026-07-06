using Spectre.Console.Cli;
using TokenStack.Cli.Commands;

var app = new CommandApp();
app.Configure(c =>
{
    c.SetApplicationName("token-saver");
    c.AddCommand<InstallCommand>("install")
        .WithDescription("Install + wire the full stack (idempotent)");
    c.AddCommand<LauncherCommand>("launcher")
        .WithDescription("Make a desktop launcher for a model (GLM/Kimi/MiniMax/OpenRouter/custom) — runs in parallel, doesn't touch your normal Claude");
    c.AddBranch("profile", p =>
    {
        p.SetDescription("Per-model compression proxies: parallel + savings, each with an on/off button");
        p.AddCommand<ProfileAddCommand>("add").WithDescription("Add a model (own proxy + launcher + on/off button)");
        p.AddCommand<ProfileListCommand>("list").WithDescription("List models and their on/off state");
        p.AddCommand<ProfileOnCommand>("on").WithDescription("Turn a model's proxy on");
        p.AddCommand<ProfileOffCommand>("off").WithDescription("Turn a model's proxy off");
        p.AddCommand<ProfileToggleCommand>("toggle").WithDescription("Flip a model's proxy on/off");
        p.AddCommand<ProfileRemoveCommand>("remove").WithDescription("Remove a model (stops + unregisters its proxy)");
    });
    c.AddCommand<StatusCommand>("status")
        .WithDescription("Live stack status (--hook = one-line session mode)");
    c.AddCommand<StartCommand>("start").WithDescription("Start the Headroom proxy task");
    c.AddCommand<StopCommand>("stop").WithDescription("Stop the Headroom proxy task");
    c.AddCommand<RestartCommand>("restart").WithDescription("Restart with zombie recovery");
    c.AddCommand<OnCommand>("on").WithDescription("Turn a layer (or all) ON: on (headroom|rtk|semble)");
    c.AddCommand<OffCommand>("off").WithDescription("Turn a layer (or all) OFF: off (headroom|rtk|semble)");
    c.AddCommand<ToggleCommand>("toggle").WithDescription("Flip a layer (or all) on/off");
    c.AddCommand<ShortcutCommand>("shortcut").WithDescription("Create the desktop toggle button");
    c.AddCommand<ConfigCommand>("config")
        .WithDescription("config list | get <key> | set <key> <value> | open");
    c.AddCommand<DoctorCommand>("doctor").WithDescription("Diagnose (and --fix) known failure modes");
    c.AddCommand<UpdateCommand>("update").WithDescription("Update a component to a pinned version");
    c.AddCommand<PackCommand>("pack").WithDescription("Build an offline bundle (run on an online machine)");
    c.AddCommand<GainCommand>("gain").WithDescription("Unified savings report (rtk + headroom)");
    c.AddCommand<UninstallCommand>("uninstall").WithDescription("Full rollback");
});
return app.Run(args);
