using TokenStack.Core.Install;
using Xunit;

namespace TokenStack.Tests;

public class OfflinePackerTests
{
    [Fact]
    public void PlanWheelhouseArgs_DownloadsBothComponentsWithMcpExtra()
    {
        Assert.Equal(
            @"-m pip download headroom-ai[proxy] semble[mcp] -d C:\stage\vendor\wheelhouse",
            OfflinePacker.PlanWheelhouseArgs(@"C:\stage\vendor\wheelhouse"));
    }

    [Fact]
    public void PlanWheelhouseArgs_QuotesSpacedDir()
    {
        Assert.Equal(
            "-m pip download headroom-ai[proxy] semble[mcp] -d \"C:\\my stage\\wheelhouse\"",
            OfflinePacker.PlanWheelhouseArgs(@"C:\my stage\wheelhouse"));
    }
}
