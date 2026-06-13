using TokenStack.Core.Install;
using Xunit;

namespace TokenStack.Tests;

public class OfflinePackerTests
{
    // pip WHEEL (not download): builds wheels on the online machine so the air-gapped install
    // is pure-wheel — critical because headroom-ai ships only sdists past 0.20.15, which can't
    // be built offline. Specs are pinned to match the online install exactly.
    [Fact]
    public void PlanWheelhouseArgs_BuildsPinnedWheels()
    {
        Assert.Equal(
            @"-m pip wheel headroom-ai[proxy]==0.24.0 semble[mcp] -w C:\stage\vendor\wheelhouse",
            OfflinePacker.PlanWheelhouseArgs(@"C:\stage\vendor\wheelhouse",
                "headroom-ai[proxy]==0.24.0", "semble[mcp]"));
    }

    [Fact]
    public void PlanWheelhouseArgs_QuotesSpacedDir()
    {
        Assert.Equal(
            "-m pip wheel headroom-ai[proxy]==0.24.0 semble[mcp] -w \"C:\\my stage\\wheelhouse\"",
            OfflinePacker.PlanWheelhouseArgs(@"C:\my stage\wheelhouse",
                "headroom-ai[proxy]==0.24.0", "semble[mcp]"));
    }
}
