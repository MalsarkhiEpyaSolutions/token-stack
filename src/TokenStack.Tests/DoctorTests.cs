using System.Text.Json.Nodes;
using TokenStack.Core.Config;
using TokenStack.Core.Doctor;
using Xunit;

namespace TokenStack.Tests;

public class DoctorTests
{
    private static DoctorContext Ctx(
        Action<FakeEnv>? env = null, bool portListening = true,
        string settings = "{}", string claudeJson = "{}",
        StackConfig? cfg = null, FakeRunner? runner = null)
    {
        var e = new FakeEnv();
        env?.Invoke(e);
        return new DoctorContext(
            cfg ?? StackConfig.CreateDefault(@"C:\ts"),
            JsonNode.Parse(settings)!, JsonNode.Parse(claudeJson)!,
            e, new FakePort { Listening = portListening },
            runner ?? new FakeRunner(), new FakeHttp());
    }

    [Fact]
    public void RoutingBypassed_Detected_AndFixed()
    {
        var ctx = Ctx(e =>
        {
            e.User["ANTHROPIC_BASE_URL"] = "https://api.anthropic.com"; // wrong scope value
            e.Process["ANTHROPIC_BASE_URL"] = "https://api.anthropic.com";
        });
        var check = new RoutingBypassedCheck();
        Assert.False(check.Detect(ctx).Ok);
        Assert.True(check.Fix(ctx));
        Assert.Equal("http://127.0.0.1:8787",
            ((FakeEnv)ctx.Env).User["ANTHROPIC_BASE_URL"]);
    }

    [Fact]
    public void ProxyZombie_Detected_WhenTaskRunningPortDead()
    {
        var runner = new FakeRunner
        {
            Handler = (f, a) => a.Contains("/query") && a.Contains("/fo csv")
                ? new(0, "\"HeadroomProxy\",\"N/A\",\"Running\"", "")
                : new(0, "", ""),
        };
        var ctx = Ctx(portListening: false, runner: runner);
        Assert.False(new ProxyZombieCheck().Detect(ctx).Ok);
    }

    [Fact]
    public void SembleUvx_Detected_WhenCommandIsUvx()
    {
        var ctx = Ctx(claudeJson:
            """{ "mcpServers": { "semble": { "command": "uvx", "args": ["--from","semble[mcp]","semble"] } } }""");
        Assert.False(new SembleUvxCheck().Detect(ctx).Ok);
    }

    [Fact]
    public void RtkHookMissing_Detected_OnEmptySettings()
    {
        Assert.False(new RtkHookMissingCheck().Detect(Ctx()).Ok);
    }

    [Fact]
    public void RtkHookPowershell_Detected()
    {
        var ctx = Ctx(settings:
            """{ "hooks": { "PreToolUse": [ { "matcher": "PowerShell", "hooks": [ { "type":"command","command":"\"C:\\ts\\rtk\\rtk.exe\" hook claude" } ] } ] } }""");
        Assert.False(new RtkHookPowershellCheck().Detect(ctx).Ok);
    }

    [Fact]
    public void ModelPin_DetectedAndFixed()
    {
        var ctx = Ctx(e => e.User["ANTHROPIC_MODEL"] = "claude-opus-4-6");
        var check = new ModelPinLeftoverCheck();
        Assert.False(check.Detect(ctx).Ok);
        Assert.True(check.Fix(ctx));
        Assert.False(((FakeEnv)ctx.Env).User.ContainsKey("ANTHROPIC_MODEL"));
    }

    [Fact]
    public void PathSpaces_Detected()
    {
        var ctx = Ctx(cfg: StackConfig.CreateDefault(@"C:\proxy tokens"));
        var r = new PathSpacesCheck().Detect(ctx);
        Assert.False(r.Ok);
        Assert.False(r.CanFix); // guided reinstall, not auto-fix
    }

    [Fact]
    public void DisabledDrift_Detected_WhenRtkDisabledButHookPresent()
    {
        var cfg = StackConfig.CreateDefault(@"C:\ts");
        cfg.Rtk.Enabled = false;
        var ctx = Ctx(cfg: cfg, settings:
            """{ "hooks": { "PreToolUse": [ { "matcher": "Bash", "hooks": [ { "type":"command","command":"\"C:\\ts\\rtk\\rtk.exe\" hook claude" } ] } ] } }""");
        Assert.False(new DisabledDriftCheck().Detect(ctx).Ok);
    }

    [Fact]
    public void OfflineModels_Ok_WhenNoHfCache_OnlineInstall()
    {
        // C:\ts has no hf-cache dir → online install → N/A → ok
        Assert.True(new OfflineModelsPresentCheck().Detect(Ctx()).Ok);
    }

    [Fact]
    public void OfflineModels_Fails_WhenHfCachePresentButEmpty()
    {
        var root = Path.Combine(Path.GetTempPath(), "ts-doc", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "hf-cache")); // present but no models
        var ctx = Ctx(cfg: StackConfig.CreateDefault(root));
        Assert.False(new OfflineModelsPresentCheck().Detect(ctx).Ok);
    }

    [Fact]
    public void OfflineModels_Ok_WhenModelsPresent()
    {
        var root = Path.Combine(Path.GetTempPath(), "ts-doc", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "hf-cache", "hub", "models--answerdotai--ModernBERT-base"));
        var ctx = Ctx(cfg: StackConfig.CreateDefault(root));
        Assert.True(new OfflineModelsPresentCheck().Detect(ctx).Ok);
    }

    [Fact]
    public void Registry_ContainsAllTwelveChecks()
    {
        Assert.Equal(12, DoctorRegistry.All.Count);
        Assert.Equal(new[]
        {
            "routing-bypassed", "proxy-zombie", "proxy-extra-missing", "semble-uvx",
            "rtk-hook-missing", "rtk-hook-powershell", "cco-hook-missing", "model-pin-leftover",
            "path-spaces", "task-misconfigured", "stack-disabled-drift", "offline-models-present",
        }, DoctorRegistry.All.Select(c => c.Id).ToArray());
    }

    [Fact]
    public void CcoHookMissingCheck_FailsThenFixes_WhenEnabledButUnwired()
    {
        var cfg = StackConfig.CreateDefault(@"C:\ts"); // cco enabled
        var ctx = new DoctorContext(cfg, JsonNode.Parse("{}")!, JsonNode.Parse("{}")!,
            new FakeEnv(), new FakePort(), new FakeRunner(), new FakeHttp());

        var check = new CcoHookMissingCheck();
        Assert.False(check.Detect(ctx).Ok);      // enabled but no hook present
        Assert.True(check.Fix(ctx));             // adds it
        Assert.True(ctx.SettingsChanged);
        Assert.True(check.Detect(ctx).Ok);       // now present
    }
}
