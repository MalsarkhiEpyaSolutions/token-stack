using TokenStack.Core.Config;
using Xunit;

namespace TokenStack.Tests;

public class ConfigTests
{
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "ts-tests", Guid.NewGuid().ToString("N"), "config.json");

    [Fact]
    public void Load_OldConfigWithoutUpstreamUrl_DefaultsToEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), "ts-cfg", Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"schemaVersion":1,"installRoot":"C:\\ts","headroom":{"enabled":true,"port":8787,"mode":"token","version":"0.30.0","pythonVersion":"3.12","extraArgs":[]}}""");
        var c = ConfigStore.Load(path);
        Assert.Equal("", c.Headroom.UpstreamUrl);
    }

    [Fact]
    public void CreateDefault_HasSpecDefaults()
    {
        var c = StackConfig.CreateDefault(@"C:\Users\x\AppData\Local\token-stack");
        Assert.Equal(1, c.SchemaVersion);
        Assert.True(c.Headroom.Enabled);
        Assert.Equal(8787, c.Headroom.Port);
        Assert.Equal("token", c.Headroom.Mode);
        Assert.Equal("0.30.0", c.Headroom.Version);
        Assert.Equal("3.12", c.Headroom.PythonVersion);
        Assert.Equal("", c.Headroom.UpstreamUrl); // default = Anthropic (today's behavior)
        Assert.Equal("0.42.3", c.Rtk.Version);
        Assert.Equal("github:rtk-ai/rtk", c.Rtk.Source);
        Assert.Equal("Bash", c.Rtk.HookMatcher);
        Assert.Equal("latest", c.Semble.Version);
        Assert.True(c.Routing.Cli);
        Assert.True(c.Routing.Desktop);
        Assert.True(c.Hooks.SessionStatusLine);
        Assert.Equal("auto", c.Bootstrap.UvInstaller);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var path = TempFile();
        var c = StackConfig.CreateDefault(@"C:\ts");
        c.Headroom.Port = 9000;
        ConfigStore.Save(c, path);
        var back = ConfigStore.Load(path);
        Assert.Equal(9000, back.Headroom.Port);
        Assert.Equal(@"C:\ts", back.InstallRoot);
    }

    [Fact]
    public void Load_PreservesUnknownKeys_OnSave()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"schemaVersion":1,"installRoot":"C:\\ts","futureKey":{"x":1},"headroom":{"enabled":true,"port":8787,"mode":"token","version":"0.24.0","pythonVersion":"3.12","extraArgs":[]},"rtk":{"enabled":true,"version":"0.42.3","source":"github:rtk-ai/rtk","hookMatcher":"Bash"},"semble":{"enabled":true,"version":"latest"},"routing":{"cli":true,"desktop":true},"hooks":{"sessionStatusLine":true},"bootstrap":{"uvInstaller":"auto"}}""");
        ConfigStore.SetValue(path, "headroom.port", "9001");
        var raw = File.ReadAllText(path);
        Assert.Contains("futureKey", raw);
        Assert.Contains("9001", raw);
    }

    [Theory]
    [InlineData("headroom.port", "9000")]
    [InlineData("headroom.mode", "cache")]
    [InlineData("routing.desktop", "false")]
    [InlineData("rtk.enabled", "false")]
    public void SetValue_ThenGetValue_RoundTrips(string key, string value)
    {
        var path = TempFile();
        ConfigStore.Save(StackConfig.CreateDefault(@"C:\ts"), path);
        ConfigStore.SetValue(path, key, value);
        Assert.Equal(value, ConfigStore.GetValue(path, key));
    }

    [Fact]
    public void SetValue_UnknownKey_Throws()
    {
        var path = TempFile();
        ConfigStore.Save(StackConfig.CreateDefault(@"C:\ts"), path);
        Assert.Throws<ArgumentException>(() => ConfigStore.SetValue(path, "headroom.nope", "1"));
    }

    [Theory]
    [InlineData(@"C:\proxy tokens", "installRoot")]      // spaces — the motivating mistake
    [InlineData(@"relative\path", "installRoot")]
    public void Validate_RejectsBadInstallRoot(string root, string expectedField)
    {
        var c = StackConfig.CreateDefault(root);
        var errors = ConfigValidator.Validate(c);
        Assert.Contains(errors, e => e.Contains(expectedField));
    }

    [Fact]
    public void Validate_RejectsBadPortModeMatcher()
    {
        var c = StackConfig.CreateDefault(@"C:\ts");
        c.Headroom.Port = 80;            // < 1024 reserved
        c.Headroom.Mode = "turbo";       // not in token|cache|passthrough
        c.Rtk.HookMatcher = "PowerShell"; // forbidden by design
        var errors = ConfigValidator.Validate(c);
        Assert.Equal(3, errors.Count);
    }

    [Fact]
    public void Validate_AcceptsDefault()
    {
        Assert.Empty(ConfigValidator.Validate(StackConfig.CreateDefault(@"C:\ts")));
    }
}
