using TokenStack.Core.Components;
using TokenStack.Core.Config;
using Xunit;

namespace TokenStack.Tests;

public class SembleTests
{
    [Theory]
    [InlineData("latest", "semble[mcp]")]
    [InlineData("1.2.3", "semble[mcp]==1.2.3")]
    public void InstallSpec_PinsOnlyWhenNotLatest(string version, string expected)
    {
        Assert.Equal(expected, SembleComponent.InstallSpec(new SembleConfig { Version = version }));
    }

    [Fact]
    public void Install_RunsUvToolInstall_WithForceForUpdates()
    {
        var runner = new FakeRunner();
        var sc = new SembleComponent(runner);
        sc.Install(new SembleConfig(), uvPath: "uv", verifyExe: false); // no exe on test machines
        Assert.Contains("uv tool install --force semble[mcp]", runner.Calls);
    }

    [Fact]
    public void ExePath_IsUnderUserLocalBin()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "bin", "semble.exe");
        Assert.Equal(expected, SembleComponent.ExePath());
    }
}
