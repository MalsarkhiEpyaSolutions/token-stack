using TokenStack.Core.Status;
using Xunit;

namespace TokenStack.Tests;

public class StatusLineTests
{
    private static StackStatus Healthy() => new(
        TaskRunning: true, PortListening: true, Routed: true, Reqs: 42,
        Port: 8787, RtkOnPath: true, SembleWired: true,
        HeadroomEnabled: true, RtkEnabled: true, SembleEnabled: true);

    [Fact]
    public void AllUp_Routed_WithReqs()
    {
        Assert.Equal(
            "[TokenSaver] Headroom: up (:8787, ROUTED, reqs=42) | RTK: up | Semble: up (MCP)",
            StatusLine.Build(Healthy()));
    }

    [Fact]
    public void DefaultAnthropic_HasNoProviderSuffix()
    {
        Assert.DoesNotContain("ROUTED→", StatusLine.Build(Healthy())); // ProviderLabel defaults to Anthropic
    }

    [Fact]
    public void Vendor_AppendsProviderToRoute()
    {
        var s = Healthy() with { ProviderLabel = "Kimi" };
        Assert.Contains("ROUTED→Kimi", StatusLine.Build(s));
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
            "[TokenSaver] Headroom: up (:8787, ROUTED) | RTK: up | Semble: up (MCP)",
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

    [Fact]
    public void HeadroomOff_ShowsOff_RegardlessOfProbe()
    {
        var s = Healthy() with { HeadroomEnabled = false };
        Assert.Contains("Headroom: OFF", StatusLine.Build(s));
        Assert.DoesNotContain("ROUTED", StatusLine.Build(s));
    }

    [Fact]
    public void RtkOff_AndSembleOff_ShowOff()
    {
        var s = Healthy() with { RtkEnabled = false, SembleEnabled = false };
        var line = StatusLine.Build(s);
        Assert.Contains("RTK: OFF", line);
        Assert.Contains("Semble: OFF", line);
    }
}
