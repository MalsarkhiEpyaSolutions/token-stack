using TokenStack.Core.Components;
using TokenStack.Core.Config;
using Xunit;

namespace TokenStack.Tests;

public class HeadroomTests
{
    [Theory] // NOTE: System.Text.Json emits compact arrays — no spaces after commas
    [InlineData("token", 8787, "[\"headroom\",\"proxy\",\"--port\",\"8787\"]")]
    [InlineData("cache", 8787, "[\"headroom\",\"proxy\",\"--port\",\"8787\",\"--mode\",\"cache\"]")]
    [InlineData("passthrough", 9000, "[\"headroom\",\"proxy\",\"--port\",\"9000\",\"--no-optimize\"]")]
    public void BuildArgv_MapsModeAndPort(string mode, int port, string expected)
    {
        var cfg = new HeadroomConfig { Mode = mode, Port = port };
        Assert.Equal(expected, HeadroomComponent.BuildArgv(cfg));
    }

    [Fact]
    public void BuildArgv_AppendsExtraArgs()
    {
        var cfg = new HeadroomConfig { ExtraArgs = { "--verbose" } };
        Assert.EndsWith("\"--verbose\"]", HeadroomComponent.BuildArgv(cfg));
    }

    [Fact]
    public void RenderLauncher_SubstitutesArgv_AndKeepsRedirectBeforeImport()
    {
        var cfg = new HeadroomConfig { Port = 8123 };
        var py = HeadroomComponent.RenderLauncher(cfg, hfHome: null);
        Assert.Contains("\"8123\"", py);
        Assert.DoesNotContain("{{ARGV}}", py);
        Assert.DoesNotContain("{{HF_ENV}}", py);
        Assert.DoesNotContain("HF_HUB_OFFLINE", py); // online: no HF pinning
        // the load-bearing ordering: redirect must precede the import
        var redirect = py.IndexOf("sys.stdout = _log", StringComparison.Ordinal);
        var import_ = py.IndexOf("from headroom.cli import main", StringComparison.Ordinal);
        Assert.True(redirect >= 0 && import_ > redirect);
    }

    [Fact]
    public void RenderLauncher_Offline_InjectsHfOfflineEnv_BeforeImport()
    {
        var py = HeadroomComponent.RenderLauncher(new HeadroomConfig(), hfHome: @"C:\token-stack\hf-cache");
        Assert.Contains(@"os.environ[""HF_HOME""] = r""C:\token-stack\hf-cache""", py);
        Assert.Contains("os.environ[\"HF_HUB_OFFLINE\"] = \"1\"", py);
        var hf = py.IndexOf("HF_HUB_OFFLINE", StringComparison.Ordinal);
        var import_ = py.IndexOf("from headroom.cli import main", StringComparison.Ordinal);
        Assert.True(hf >= 0 && import_ > hf); // env set before headroom imports huggingface_hub
    }

    [Fact]
    public void InstallCommands_Online_UseUvNativePipAgainstVenvPython()
    {
        var hc = new HeadroomComponent(new FakeRunner(), new FakePort(), new FakeHttp());
        var cmds = hc.PlanInstallCommands(StackConfig.CreateDefault(@"C:\ts"), "uv", InstallSource.Online);

        Assert.Contains(cmds, c => c == @"uv venv --clear --python 3.12 C:\ts\venv");
        Assert.Contains(cmds, c =>
            c == @"uv pip install --python C:\ts\venv\Scripts\python.exe headroom-ai[proxy]==0.24.0");
    }

    [Fact]
    public void InstallCommands_Offline_UseVendorPythonAndFindLinks()
    {
        var hc = new HeadroomComponent(new FakeRunner(), new FakePort(), new FakeHttp());
        var src = new InstallSource(true, @"C:\dl\vendor");
        var cmds = hc.PlanInstallCommands(StackConfig.CreateDefault(@"C:\ts"), @"C:\dl\vendor\uv.exe", src);

        Assert.Contains(cmds, c => c == @"C:\dl\vendor\uv.exe venv --clear --python C:\dl\vendor\python C:\ts\venv");
        Assert.Contains(cmds, c => c ==
            @"C:\dl\vendor\uv.exe pip install --python C:\ts\venv\Scripts\python.exe --offline --no-index --find-links C:\dl\vendor\wheelhouse headroom-ai[proxy]==0.24.0");
    }

    [Fact]
    public void WaitReady_PollsReadyz_UntilBody()
    {
        var http = new FakeHttp();
        http.Responses["http://127.0.0.1:8787/readyz"] = "ok";
        var hc = new HeadroomComponent(new FakeRunner(), new FakePort { Listening = true }, http);
        Assert.True(hc.WaitReady(8787, maxWaitMs: 100, pollMs: 10));
    }

    [Fact]
    public void WaitReady_TimesOut_WhenNeverReady()
    {
        var hc = new HeadroomComponent(new FakeRunner(), new FakePort(), new FakeHttp());
        Assert.False(hc.WaitReady(8787, maxWaitMs: 50, pollMs: 10));
    }
}
