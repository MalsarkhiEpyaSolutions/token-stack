namespace TokenStack.Core.Windows;

/// <summary>schtasks.exe wrapper for the HeadroomProxy task. Uses /xml registration
/// (full fidelity) and /query CSV for state.</summary>
public sealed class ScheduledTaskManager(IProcessRunner runner, string? taskName = null)
{
    public const string DefaultTaskName = "HeadroomProxy";
    public string TaskName { get; } = taskName ?? DefaultTaskName;

    public bool Exists() =>
        runner.Run("schtasks", $"/query /tn {TaskName}").Ok;

    public bool IsRunning()
    {
        var r = runner.Run("schtasks", $"/query /tn {TaskName} /fo csv /nh");
        return r.Ok && r.StdOut.Contains("\"Running\"", StringComparison.OrdinalIgnoreCase);
    }

    public void RegisterOrUpdate(string xml, string tempDir)
    {
        Directory.CreateDirectory(tempDir);
        var xmlPath = Path.Combine(tempDir, $"{TaskName}.task.xml");
        File.WriteAllText(xmlPath, xml, System.Text.Encoding.Unicode); // schtasks expects UTF-16
        var r = runner.Run("schtasks", $"/create /tn {TaskName} /xml \"{xmlPath}\" /f");
        if (!r.Ok) throw new InvalidOperationException($"schtasks /create failed: {r.StdErr}{r.StdOut}");
    }

    public void Start() => runner.Run("schtasks", $"/run /tn {TaskName}");
    public void Stop() => runner.Run("schtasks", $"/end /tn {TaskName}");
    public void Enable() => runner.Run("schtasks", $"/change /tn {TaskName} /enable");
    public void Disable() => runner.Run("schtasks", $"/change /tn {TaskName} /disable");
    public void Unregister() => runner.Run("schtasks", $"/delete /tn {TaskName} /f");

    /// <summary>Kill any pythonw still running a proxy launcher — covers zombies AND the
    /// legacy hand-built install holding the port during adoption.</summary>
    public void KillOrphans() =>
        runner.Run("powershell",
            "-NoProfile -Command \"Get-CimInstance Win32_Process -Filter \\\"Name='pythonw.exe'\\\" | " +
            "Where-Object { $_.CommandLine -match 'run_proxy|headroom' } | " +
            "ForEach-Object { Stop-Process -Id $_.ProcessId -Force }\"");

    /// <summary>Zombie recovery: stop task, kill orphan pythonw running run_proxy, restart.</summary>
    public void RestartWithZombieKill()
    {
        Stop();
        KillOrphans();
        Thread.Sleep(2000);
        Start();
    }
}
