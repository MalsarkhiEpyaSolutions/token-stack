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
        var py = HeadroomComponent.RenderLauncher(cfg);
        Assert.Contains("\"8123\"", py);
        Assert.DoesNotContain("{{ARGV}}", py);
        // the load-bearing ordering: redirect must precede the import
        var redirect = py.IndexOf("sys.stdout = _log", StringComparison.Ordinal);
        var import_ = py.IndexOf("from headroom.cli import main", StringComparison.Ordinal);
        Assert.True(redirect >= 0 && import_ > redirect);
    }

    [Fact]
    public void InstallCommands_UseUvNativePipAgainstVenvPython()
    {
        var runner = new FakeRunner();
        var cfg = StackConfig.CreateDefault(@"C:\ts");
        var hc = new HeadroomComponent(runner, new FakePort(), new FakeHttp());
        var cmds = hc.PlanInstallCommands(cfg, uvPath: "uv");

        Assert.Contains(cmds, c => c == @"uv venv --clear --python 3.12 C:\ts\venv");
        // uv venvs have NO pip inside — uv pip install with --python is the correct form
        Assert.Contains(cmds, c =>
            c == @"uv pip install --python C:\ts\venv\Scripts\python.exe headroom-ai[proxy]==0.24.0");
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
