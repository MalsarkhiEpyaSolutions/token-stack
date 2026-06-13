using TokenStack.Core.Config;
using TokenStack.Core.Install;
using Xunit;

namespace TokenStack.Tests;

public class PipelineTests
{
    [Fact]
    public void PlanSteps_FollowSpecOrder_AndHonorEnabledFlags()
    {
        var cfg = StackConfig.CreateDefault(@"C:\ts");
        var all = InstallPipeline.PlanSteps(cfg).Select(s => s.Name).ToArray();
        Assert.Equal(new[]
        {
            "preflight", "bootstrap-uv", "headroom", "rtk", "semble", "routing", "hooks", "save-config",
        }, all);

        cfg.Rtk.Enabled = false;
        cfg.Semble.Enabled = false;
        var subset = InstallPipeline.PlanSteps(cfg).Select(s => s.Name).ToArray();
        Assert.DoesNotContain("rtk", subset);
        Assert.DoesNotContain("semble", subset);
    }

    [Fact]
    public void Preflight_RejectsSpacesInRoot()
    {
        var cfg = StackConfig.CreateDefault(@"C:\proxy tokens");
        var ex = Record.Exception(() => InstallPipeline.Preflight(cfg));
        Assert.NotNull(ex);
        Assert.Contains("spaces", ex!.Message);
    }

    [Fact]
    public void Preflight_AcceptsCleanRoot()
    {
        var cfg = StackConfig.CreateDefault(Path.Combine(Path.GetTempPath(), "tsclean"));
        InstallPipeline.Preflight(cfg); // must not throw
    }
}
