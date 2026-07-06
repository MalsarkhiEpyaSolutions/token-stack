using TokenStack.Core.Components;
using Xunit;

namespace TokenStack.Tests;

public class LauncherWriterTests
{
    [Fact]
    public void BuildCmd_SetsAllThreeEnvVars_ThenRunsClaude()
    {
        var cmd = LauncherWriter.BuildCmd("https://api.minimax.io/anthropic", "sk-1", "MiniMax-M2");
        Assert.Contains("set \"ANTHROPIC_BASE_URL=https://api.minimax.io/anthropic\"", cmd);
        Assert.Contains("set \"ANTHROPIC_API_KEY=sk-1\"", cmd);
        Assert.Contains("set \"ANTHROPIC_MODEL=MiniMax-M2\"", cmd);
        // claude must be launched AFTER the env is set.
        Assert.True(cmd.IndexOf("claude", System.StringComparison.Ordinal)
                    > cmd.IndexOf("ANTHROPIC_MODEL", System.StringComparison.Ordinal));
    }
}
