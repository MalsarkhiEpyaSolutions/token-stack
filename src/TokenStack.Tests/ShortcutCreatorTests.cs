using TokenStack.Core.Windows;
using Xunit;

namespace TokenStack.Tests;

public class ShortcutCreatorTests
{
    [Fact]
    public void LayerSpecs_AreThreePerLayerToggles()
    {
        var specs = ShortcutCreator.LayerSpecs();
        Assert.Equal(3, specs.Count);
        Assert.Contains(specs, s => s.Arguments == "toggle headroom --notify");
        Assert.Contains(specs, s => s.Arguments == "toggle rtk --notify");
        Assert.Contains(specs, s => s.Arguments == "toggle semble --notify");
        Assert.All(specs, s => Assert.EndsWith(".lnk", s.FileName));
    }

    [Fact]
    public void Locations_AreOnTheDesktop()
    {
        Assert.EndsWith("Token Stack.lnk", ShortcutCreator.WholeStackPath);      // loose quick button
        Assert.EndsWith("Token Stack Controls", ShortcutCreator.ControlsFolder); // folder for the 3
        Assert.Contains("Desktop", ShortcutCreator.WholeStackPath);
        Assert.Contains("Desktop", ShortcutCreator.ControlsFolder);
    }
}
