using TokenStack.Core.Components;
using Xunit;

namespace TokenStack.Tests;

public class CcoComponentTests
{
    [Fact]
    public void ExtractSnapshot_WritesReadCacheJsAndPackageJson()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ts-cco", Guid.NewGuid().ToString("N"));
        CcoComponent.ExtractSnapshot(dir);

        Assert.True(File.Exists(Path.Combine(dir, "src", "read-cache.js")));
        var pkg = File.ReadAllText(Path.Combine(dir, "package.json"));
        Assert.Contains("\"type\": \"module\"", pkg);
    }

    [Fact]
    public void DisableBigFileDigest_SetsFalse_OnEmpty()
    {
        var json = CcoComponent.DisableBigFileDigest(null);
        Assert.Contains("\"bigFileDigest\": false", json);
    }

    [Fact]
    public void DisableBigFileDigest_PreservesExistingKeys()
    {
        var json = CcoComponent.DisableBigFileDigest("""{ "bigFileThreshold": 1500, "bigFileDigest": true }""");
        Assert.Contains("\"bigFileThreshold\": 1500", json);
        Assert.Contains("\"bigFileDigest\": false", json); // overridden
        Assert.DoesNotContain("true", json);
    }

    [Fact]
    public void NodePresent_FollowsRunnerResult()
    {
        var present = new CcoComponent(new FakeRunner { Handler = (f, a) => new(0, "v24.2.0", "") });
        var absent = new CcoComponent(new FakeRunner { Handler = (f, a) => new(1, "", "not found") });
        Assert.True(present.NodePresent());
        Assert.False(absent.NodePresent());
    }
}
