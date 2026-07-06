using TokenStack.Core.Components;
using TokenStack.Core.Windows;
using Xunit;

namespace TokenStack.Tests;

public class ProfilesTests
{
    [Theory]
    [InlineData("MiniMax", "MiniMax")]
    [InlineData("Open Router!", "OpenRouter")]
    [InlineData("deep-seek v4", "deepseekv4")]
    public void Slug_KeepsOnlyAlphanumerics(string name, string expected) =>
        Assert.Equal(expected, ProfileWiring.Slug(name));

    [Fact]
    public void Wiring_DerivesTaskScriptAndProxyUrl()
    {
        Assert.Equal("TokenSaver-MiniMax", ProfileWiring.TaskName("MiniMax"));
        Assert.Equal("run_proxy_MiniMax.py", ProfileWiring.ScriptFile("MiniMax"));
        Assert.Equal("http://127.0.0.1:8788", ProfileWiring.ProxyUrl(8788));
    }

    [Fact]
    public void ScheduledTaskManager_UsesGivenTaskName()
    {
        var runner = new FakeRunner();
        new ScheduledTaskManager(runner, "TokenSaver-MiniMax").Exists();
        Assert.Contains(runner.Calls, c => c.Contains("/tn TokenSaver-MiniMax"));
    }

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
