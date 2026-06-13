using System.IO.Compression;
using TokenStack.Core.Config;
using TokenStack.Core.Windows;

namespace TokenStack.Core.Components;

public sealed class RtkComponent(IProcessRunner runner, IEnvStore env)
{
    public static string ReleaseUrl(RtkConfig cfg)
    {
        const string prefix = "github:";
        if (!cfg.Source.StartsWith(prefix, StringComparison.Ordinal))
            throw new ArgumentException(
                $"rtk.source must be 'github:<owner>/<repo>' (got '{cfg.Source}')");
        var repo = cfg.Source[prefix.Length..];
        return $"https://github.com/{repo}/releases/download/v{cfg.Version}/rtk-x86_64-pc-windows-msvc.zip";
    }

    public static void ExtractRtkExe(string zipPath, string destDir)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.Entries.FirstOrDefault(e =>
                e.Name.Equals("rtk.exe", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"rtk.exe not found inside {zipPath}");
        Directory.CreateDirectory(destDir);
        entry.ExtractToFile(Path.Combine(destDir, "rtk.exe"), overwrite: true);
    }

    public string RtkDir(StackConfig cfg) => Path.Combine(cfg.InstallRoot, "rtk");
    public string RtkExe(StackConfig cfg) => Path.Combine(RtkDir(cfg), "rtk.exe");

    public void Install(StackConfig cfg)
    {
        var url = ReleaseUrl(cfg.Rtk);
        var tmpZip = Path.Combine(cfg.InstallRoot, "tmp", "rtk.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(tmpZip)!);

        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
        using (var stream = http.GetStreamAsync(url).GetAwaiter().GetResult())
        using (var file = File.Create(tmpZip))
            stream.CopyTo(file);

        ExtractRtkExe(tmpZip, RtkDir(cfg));
        File.Delete(tmpZip);
        UserPath.EnsureContains(env, RtkDir(cfg));

        var verify = runner.Run(RtkExe(cfg), "--version", 15000);
        if (!verify.Ok)
            throw new InvalidOperationException($"rtk.exe extracted but failed to run: {verify.StdErr}");
    }

    /// <summary>Hook wiring is done by the pipeline via ClaudeSurgeon.EnsureRtkHook —
    /// kept out of this class so all settings.json writes share one editor/backup path.</summary>
    public bool IsOnPath() => runner.Run("rtk", "--version", 10000).Ok;

    public void Unwire(StackConfig cfg) => UserPath.Remove(env, RtkDir(cfg));
}
