using TokenStack.Core.Components;
using Xunit;

namespace TokenStack.Tests;

public class BootstrapTests
{
    [Fact]
    public void StandaloneInstallPlan_UsesFullPowershellPath_NotBareName()
    {
        var (exe, args) = Bootstrap.StandaloneInstallPlan();
        // bare "powershell" fails Process.Start on some machines — must be the full path.
        Assert.EndsWith(@"WindowsPowerShell\v1.0\powershell.exe", exe);
        Assert.True(Path.IsPathRooted(exe));
        Assert.Contains("astral.sh/uv/install.ps1", args);
    }

    [Fact]
    public void EnsureUv_WhenAlreadyOnPath_ReturnsUv_WithoutInstalling()
    {
        var runner = new FakeRunner { Handler = (f, a) => f == "uv" && a.Contains("--version")
            ? new(0, "uv 0.5.0", "") : new(1, "", "") };

        var result = new Bootstrap(runner).EnsureUv();

        Assert.Equal("uv", result);
        Assert.DoesNotContain(runner.Calls, c => c.Contains("install.ps1") || c.Contains("winget"));
    }

    [Fact]
    public void EnsureUv_TriesStandaloneBeforeWinget()
    {
        // uv never resolvable + installer never lands the file → falls through to winget then throws.
        var runner = new FakeRunner { Handler = (_, _) => new(1, "", "boom") };

        Assert.Throws<InvalidOperationException>(() => new Bootstrap(runner).EnsureUv());

        var standaloneAt = runner.Calls.FindIndex(c => c.Contains("install.ps1"));
        var wingetAt = runner.Calls.FindIndex(c => c.Contains("winget"));
        Assert.True(standaloneAt >= 0 && wingetAt >= 0);
        Assert.True(standaloneAt < wingetAt, "standalone installer must run before winget");
    }
}
