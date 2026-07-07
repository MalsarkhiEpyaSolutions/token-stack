using TokenStack.Core.Components;
using TokenStack.Core.Config;
using Xunit;

namespace TokenStack.Tests;

public class ToggleTests
{
    [Theory]
    [InlineData("headroom", StackLayer.Headroom)]
    [InlineData("rtk", StackLayer.Rtk)]
    [InlineData("semble", StackLayer.Semble)]
    [InlineData("all", StackLayer.All)]
    [InlineData("", StackLayer.All)]
    [InlineData(null, StackLayer.All)]
    public void ParseLayer_MapsNamesAndDefault(string? s, StackLayer expected)
        => Assert.Equal(expected, ToggleLogic.ParseLayer(s));

    [Fact]
    public void ParseLayer_Invalid_Throws()
        => Assert.Throws<ArgumentException>(() => ToggleLogic.ParseLayer("xyz"));

    [Fact]
    public void SetFlag_All_SetsAllThree()
    {
        var cfg = StackConfig.CreateDefault(@"C:\ts");
        ToggleLogic.SetFlag(cfg, StackLayer.All, false);
        Assert.False(cfg.Headroom.Enabled);
        Assert.False(cfg.Rtk.Enabled);
        Assert.False(cfg.Semble.Enabled);
    }

    [Fact]
    public void SetFlag_Single_TouchesOnlyThatLayer()
    {
        var cfg = StackConfig.CreateDefault(@"C:\ts");
        ToggleLogic.SetFlag(cfg, StackLayer.Rtk, false);
        Assert.True(cfg.Headroom.Enabled);
        Assert.False(cfg.Rtk.Enabled);
        Assert.True(cfg.Semble.Enabled);
    }

    [Fact]
    public void CurrentlyOn_All_TrueIfAnyOn()
    {
        var cfg = StackConfig.CreateDefault(@"C:\ts");
        cfg.Rtk.Enabled = false;
        cfg.Semble.Enabled = false;
        Assert.True(ToggleLogic.CurrentlyOn(cfg, StackLayer.All)); // headroom still on
        cfg.Headroom.Enabled = false;
        cfg.Cco.Enabled = false; // cco defaults on — must also be off for All to read off
        Assert.False(ToggleLogic.CurrentlyOn(cfg, StackLayer.All));
    }

    [Fact]
    public void Flip_All_InvertsWholeStack()
    {
        var cfg = StackConfig.CreateDefault(@"C:\ts"); // all on
        ToggleLogic.Flip(cfg, StackLayer.All);          // any on → all off
        Assert.False(cfg.Headroom.Enabled);
        Assert.False(cfg.Rtk.Enabled);
        Assert.False(cfg.Semble.Enabled);
        ToggleLogic.Flip(cfg, StackLayer.All);          // all off → all on
        Assert.True(cfg.Headroom.Enabled);
        Assert.True(cfg.Rtk.Enabled);
        Assert.True(cfg.Semble.Enabled);
    }

    [Fact]
    public void Flip_Single_TogglesOnlyThatLayer()
    {
        var cfg = StackConfig.CreateDefault(@"C:\ts");
        ToggleLogic.Flip(cfg, StackLayer.Semble);
        Assert.True(cfg.Headroom.Enabled);
        Assert.True(cfg.Rtk.Enabled);
        Assert.False(cfg.Semble.Enabled);
    }

    [Fact]
    public void ParseLayer_Cco()
    {
        Assert.Equal(StackLayer.Cco, ToggleLogic.ParseLayer("cco"));
        Assert.Equal(StackLayer.Cco, ToggleLogic.ParseLayer("CCO"));
    }

    [Fact]
    public void SetFlag_Cco_TogglesOnlyCco()
    {
        var c = StackConfig.CreateDefault(@"C:\ts");
        ToggleLogic.SetFlag(c, StackLayer.Cco, false);
        Assert.False(c.Cco.Enabled);
        Assert.True(c.Headroom.Enabled); // others untouched
    }

    [Fact]
    public void CurrentlyOn_All_IncludesCco()
    {
        var c = StackConfig.CreateDefault(@"C:\ts");
        c.Headroom.Enabled = c.Rtk.Enabled = c.Semble.Enabled = false;
        Assert.True(ToggleLogic.CurrentlyOn(c, StackLayer.All)); // cco still on
        ToggleLogic.SetFlag(c, StackLayer.Cco, false);
        Assert.False(ToggleLogic.CurrentlyOn(c, StackLayer.All));
    }
}
