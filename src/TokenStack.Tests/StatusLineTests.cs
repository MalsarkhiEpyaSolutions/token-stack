using TokenStack.Core.Status;
using Xunit;

namespace TokenStack.Tests;

public class StatusLineTests
{
    private static StackStatus Healthy() => new(
        TaskRunning: true, PortListening: true, Routed: true, Reqs: 42,
        Port: 8787, RtkOnPath: true, SembleWired: true);

    [Fact]
    public void AllUp_Routed_WithReqs()
    {
        Assert.Equal(
            "[token-stack] Headroom: up (:8787, ROUTED, reqs=42) | RTK: up | Semble: up (MCP)",
            StatusLine.Build(Healthy()));
    }

    [Fact]
    public void Bypassed_WhenSessionEnvNotRouted()
    {
        var s = Healthy() with { Routed = false };
        Assert.Contains("BYPASSED", StatusLine.Build(s));
    }

    [Fact]
    public void ReqsOmitted_WhenStatsUnreachable()
    {
        var s = Healthy() with { Reqs = null };
        Assert.Equal(
            "[token-stack] Headroom: up (:8787, ROUTED) | RTK: up | Semble: up (MCP)",
            StatusLine.Build(s));
    }

    [Fact]
    public void Starting_WhenTaskRunningButPortDead()
    {
        var s = Healthy() with { PortListening = false, Reqs = null };
        Assert.Contains("Headroom: starting (cold-load ~50s)", StatusLine.Build(s));
    }

    [Fact]
    public void Down_WhenTaskNotRunning()
    {
        var s = Healthy() with { TaskRunning = false, PortListening = false, Reqs = null };
        Assert.Contains("Headroom: DOWN", StatusLine.Build(s));
    }

    [Fact]
    public void MissingLayers_Reported()
    {
        var s = Healthy() with { RtkOnPath = false, SembleWired = false };
        var line = StatusLine.Build(s);
        Assert.Contains("RTK: MISSING", line);
        Assert.Contains("Semble: MISSING", line);
    }

    [Fact]
    public void CustomPort_AppearsInLine()
    {
        var s = Healthy() with { Port = 9000 };
        Assert.Contains("(:9000,", StatusLine.Build(s));
    }
}
