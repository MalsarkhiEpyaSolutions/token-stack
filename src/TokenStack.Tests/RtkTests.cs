using System.IO.Compression;
using TokenStack.Core.Components;
using TokenStack.Core.Config;
using Xunit;

namespace TokenStack.Tests;

public class RtkTests
{
    [Fact]
    public void ReleaseUrl_BuiltFromConfigSourceAndVersion()
    {
        var cfg = new RtkConfig { Version = "0.42.3", Source = "github:rtk-ai/rtk" };
        Assert.Equal(
            "https://github.com/rtk-ai/rtk/releases/download/v0.42.3/rtk-x86_64-pc-windows-msvc.zip",
            RtkComponent.ReleaseUrl(cfg));
    }

    [Fact]
    public void ReleaseUrl_RejectsNonGithubSource()
    {
        var cfg = new RtkConfig { Source = "https://evil.example/rtk" };
        Assert.Throws<ArgumentException>(() => RtkComponent.ReleaseUrl(cfg));
    }

    [Fact]
    public void ExtractRtkExe_FindsExeAnywhereInZip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ts-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var zipPath = Path.Combine(dir, "rtk.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("rtk-x86_64-pc-windows-msvc/rtk.exe");
            using var s = entry.Open();
            s.Write("MZfake"u8);
        }

        var dest = Path.Combine(dir, "out");
        RtkComponent.ExtractRtkExe(zipPath, dest);
        Assert.True(File.Exists(Path.Combine(dest, "rtk.exe")));
    }

    [Fact]
    public void ExtractRtkExe_Throws_WhenNoExeInZip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ts-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var zipPath = Path.Combine(dir, "bad.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            zip.CreateEntry("readme.txt");
        Assert.Throws<InvalidDataException>(() =>
            RtkComponent.ExtractRtkExe(zipPath, Path.Combine(dir, "out")));
    }
}
