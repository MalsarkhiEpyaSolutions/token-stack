using TokenStack.Core.Components;
using Xunit;

namespace TokenStack.Tests;

public class ProfilesTests
{
    [Fact]
    public void NextFreePort_StartsAt8788_SkipsUsed()
    {
        Assert.Equal(8788, ProfilePorts.NextFree(new int[0]));
        Assert.Equal(8790, ProfilePorts.NextFree(new[] { 8788, 8789 }));
        Assert.Equal(8789, ProfilePorts.NextFree(new[] { 8788, 8790 })); // fills the gap
    }

    [Fact]
    public void NextFreePort_NeverReturns8787()
    {
        // 8787 is the default Claude proxy — profiles must not collide with it.
        var used = Enumerable.Range(8788, 5).ToArray();
        Assert.True(ProfilePorts.NextFree(used) > 8787);
        Assert.NotEqual(8787, ProfilePorts.NextFree(used));
    }
}
