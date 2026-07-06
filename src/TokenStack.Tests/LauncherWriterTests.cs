using TokenStack.Core.Components;
using Xunit;

namespace TokenStack.Tests;

public class LauncherWriterTests
{
    [Fact]
    public void BuildCmd_IsolatesHome_SetsEnv_ThenRunsClaude()
    {
        var cmd = LauncherWriter.BuildCmd("MiniMax", "https://api.minimax.io/anthropic", "sk-1", "MiniMax-M2");
        Assert.Contains("set \"ANTHROPIC_BASE_URL=https://api.minimax.io/anthropic\"", cmd);
        Assert.Contains("set \"ANTHROPIC_AUTH_TOKEN=sk-1\"", cmd);
        Assert.Contains("set \"ANTHROPIC_MODEL=MiniMax-M2\"", cmd);
        // isolated home so this window has no claude.ai session (real login untouched)
        Assert.Contains("home-MiniMax", cmd);
        Assert.Contains("set \"USERPROFILE=%TS_HOME%\"", cmd);
        // claude must be launched AFTER the env is set.
        Assert.True(cmd.IndexOf("claude %*", System.StringComparison.Ordinal)
                    > cmd.IndexOf("ANTHROPIC_MODEL", System.StringComparison.Ordinal));
    }
}
