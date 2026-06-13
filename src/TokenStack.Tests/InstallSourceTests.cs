using TokenStack.Core.Components;
using Xunit;

namespace TokenStack.Tests;

public class InstallSourceTests
{
    private static string MakeExeDir(bool withVendor)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ts-src", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        if (withVendor)
        {
            var vendor = Path.Combine(dir, "vendor");
            Directory.CreateDirectory(vendor);
            File.WriteAllText(Path.Combine(vendor, "uv.exe"), "stub");
        }
        return dir;
    }

    [Fact]
    public void Resolve_AutoDetectsOffline_WhenVendorPresent()
    {
        var src = InstallSourceResolver.Resolve(MakeExeDir(withVendor: true), forceOffline: null);
        Assert.True(src.IsOffline);
        Assert.EndsWith("vendor", src.VendorDir);
    }

    [Fact]
    public void Resolve_DefaultsOnline_WhenNoVendor()
    {
        var src = InstallSourceResolver.Resolve(MakeExeDir(withVendor: false), forceOffline: null);
        Assert.False(src.IsOffline);
    }

    [Fact]
    public void Resolve_ForceOnline_IgnoresVendor()
    {
        var src = InstallSourceResolver.Resolve(MakeExeDir(withVendor: true), forceOffline: false);
        Assert.False(src.IsOffline);
    }

    [Fact]
    public void Resolve_ForceOffline_ThrowsWhenNoVendor()
    {
        var ex = Record.Exception(() =>
            InstallSourceResolver.Resolve(MakeExeDir(withVendor: false), forceOffline: true));
        Assert.NotNull(ex);
        Assert.Contains("vendor", ex!.Message);
    }

    [Fact]
    public void OfflineSource_ExposesBundlePaths()
    {
        var src = new InstallSource(true, @"C:\dl\vendor");
        Assert.Equal(@"C:\dl\vendor\uv.exe", src.Uv);
        Assert.Equal(@"C:\dl\vendor\python", src.PythonDir);
        Assert.Equal(@"C:\dl\vendor\wheelhouse", src.Wheelhouse);
        Assert.Equal(@"C:\dl\vendor\rtk.exe", src.RtkExe);
        Assert.Equal(@"C:\dl\vendor\hf-cache", src.HfCache);
    }
}
