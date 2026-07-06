using TokenStack.Core.Config;
using TokenStack.Core.Windows;

namespace TokenStack.Core.Components;

/// <summary>Installs/toggles/removes a per-model proxy: its own run_proxy_&lt;slug&gt;.py on its own
/// port forwarding to its own upstream, run by its own scheduled task. Reuses the venv + headroom
/// that the default install already provisioned — no extra pip install.</summary>
public sealed class ProfileService(IProcessRunner runner)
{
    private static HeadroomConfig ProxyCfg(StackConfig cfg, ProfileConfig p) => new()
    {
        Enabled = true,
        Port = p.Port,
        UpstreamUrl = p.Upstream,
        Mode = cfg.Headroom.Mode,
        Version = cfg.Headroom.Version,
        PythonVersion = cfg.Headroom.PythonVersion,
        ExtraArgs = cfg.Headroom.ExtraArgs,
    };

    private ScheduledTaskManager Task(ProfileConfig p) => new(runner, ProfileWiring.TaskName(p.Name));

    public void Install(StackConfig cfg, ProfileConfig p)
    {
        var script = Path.Combine(cfg.InstallRoot, ProfileWiring.ScriptFile(p.Name));
        var hf = Path.Combine(cfg.InstallRoot, "hf-cache");
        File.WriteAllText(script, HeadroomComponent.RenderLauncher(
            ProxyCfg(cfg, p), Directory.Exists(hf) ? hf : null));

        var xml = TaskXml.Render(
            Path.Combine(cfg.InstallRoot, "venv", "Scripts", "pythonw.exe"),
            script, cfg.InstallRoot,
            Environment.UserDomainName + "\\" + Environment.UserName);
        var t = Task(p);
        t.RegisterOrUpdate(xml, Path.Combine(cfg.InstallRoot, "tmp"));
        t.Enable();
        t.Start();
    }

    public void SetEnabled(ProfileConfig p, bool on)
    {
        var t = Task(p);
        if (on) { t.Enable(); t.Start(); }
        else { t.Stop(); t.Disable(); }
    }

    public void Remove(StackConfig cfg, ProfileConfig p)
    {
        var t = Task(p);
        if (t.Exists()) { t.Stop(); t.Unregister(); }
        try
        {
            var script = Path.Combine(cfg.InstallRoot, ProfileWiring.ScriptFile(p.Name));
            if (File.Exists(script)) File.Delete(script);
        }
        catch { /* best effort */ }
    }
}
