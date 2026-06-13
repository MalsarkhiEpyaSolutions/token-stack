using TokenStack.Core.Components;
using Xunit;

namespace TokenStack.Tests;

public class RoutingTests
{
    [Fact]
    public void Apply_SetsUserEnvVar_WhenDesktopRouting()
    {
        var env = new FakeEnv();
        var rm = new RoutingManager(env);
        rm.ApplyDesktop(8787);
        Assert.Equal("http://127.0.0.1:8787", env.User["ANTHROPIC_BASE_URL"]);
    }

    [Fact]
    public void RemoveDesktop_DeletesUserEnvVar()
    {
        var env = new FakeEnv();
        env.User["ANTHROPIC_BASE_URL"] = "http://127.0.0.1:8787";
        new RoutingManager(env).RemoveDesktop();
        Assert.False(env.User.ContainsKey("ANTHROPIC_BASE_URL"));
    }

    [Theory]
    [InlineData("http://127.0.0.1:8787", 8787, true)]
    [InlineData("http://localhost:8787", 8787, true)]
    [InlineData("http://127.0.0.1:8787/", 8787, true)]
    [InlineData("https://api.anthropic.com", 8787, false)]  // today's BYPASSED case
    [InlineData(null, 8787, false)]
    [InlineData("http://127.0.0.1:9000", 8787, false)]      // wrong port = not routed
    public void IsSessionRouted_JudgesProcessEnv(string? processVal, int port, bool expected)
    {
        var env = new FakeEnv();
        if (processVal is not null) env.Process["ANTHROPIC_BASE_URL"] = processVal;
        Assert.Equal(expected, new RoutingManager(env).IsSessionRouted(port));
    }

    [Fact]
    public void Diagnose_FlagsConflict_BetweenProcessAndUserScope()
    {
        var env = new FakeEnv();
        env.User["ANTHROPIC_BASE_URL"] = "http://127.0.0.1:8787";
        env.Process["ANTHROPIC_BASE_URL"] = "https://api.anthropic.com";
        var d = new RoutingManager(env).Diagnose(8787);
        Assert.False(d.SessionRouted);
        Assert.True(d.UserScopeCorrect);
        Assert.True(d.Conflict);   // → "fully quit Claude from the tray and relaunch"
    }

    [Fact]
    public void Diagnose_FlagsModelPinLeftover()
    {
        var env = new FakeEnv();
        env.User["ANTHROPIC_MODEL"] = "claude-opus-4-6";
        Assert.True(new RoutingManager(env).Diagnose(8787).ModelPinPresent);
    }
}
