using TokenStack.Core.Components;
using TokenStack.Core.Config;
using Xunit;

namespace TokenStack.Tests;

public class ToggleServiceTests
{
    private static (string settings, string claudeJson) TempFiles(string settings, string claudeJson)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ts-toggle", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var sp = Path.Combine(dir, "settings.json");
        var cj = Path.Combine(dir, ".claude.json");
        File.WriteAllText(sp, settings);
        File.WriteAllText(cj, claudeJson);
        return (sp, cj);
    }

    private static FakeRunner TaskRunningRunner() => new()
    {
        Handler = (_, a) => a.Contains("/query") && a.Contains("/fo csv")
            ? new(0, "\"HeadroomProxy\",\"N/A\",\"Running\"", "")
            : new(0, "", ""),
    };

    [Fact]
    public void ApplyWiring_HeadroomOff_RemovesRouting_AndStopsTask()
    {
        var env = new FakeEnv();
        env.User["ANTHROPIC_BASE_URL"] = "http://127.0.0.1:8787";
        var runner = TaskRunningRunner();
        var (sp, cj) = TempFiles(
            """{ "env": { "ANTHROPIC_BASE_URL": "http://127.0.0.1:8787" } }""", "{}");
        var cfg = StackConfig.CreateDefault(@"C:\ts");
        cfg.Headroom.Enabled = false;

        var state = new ToggleService(runner, env, sp, cj).ApplyWiring(cfg);

        Assert.False(state.Headroom);
        Assert.False(env.User.ContainsKey("ANTHROPIC_BASE_URL"));   // desktop routing removed
        Assert.Contains(runner.Calls, c => c.Contains("/end"));      // proxy task stopped
        Assert.DoesNotContain("ANTHROPIC_BASE_URL", File.ReadAllText(sp)); // settings.json routing removed
    }

    [Fact]
    public void ApplyWiring_RtkOff_RemovesHook_LeavesOthers()
    {
        var (sp, cj) = TempFiles(
            """{ "hooks": { "PreToolUse": [ { "matcher": "Bash", "hooks": [ { "type":"command","command":"\"C:\\ts\\rtk\\rtk.exe\" hook claude" } ] } ] } }""",
            "{}");
        var cfg = StackConfig.CreateDefault(@"C:\ts");
        cfg.Rtk.Enabled = false;

        new ToggleService(new FakeRunner(), new FakeEnv(), sp, cj).ApplyWiring(cfg);

        Assert.DoesNotContain("rtk.exe", File.ReadAllText(sp));
    }

    [Fact]
    public void ApplyWiring_SembleOff_RemovesMcp_LeavesOtherServers()
    {
        var (sp, cj) = TempFiles("{}",
            """{ "mcpServers": { "semble": { "command": "x" }, "playwright": { "command": "npx" } } }""");
        var cfg = StackConfig.CreateDefault(@"C:\ts");
        cfg.Semble.Enabled = false;

        new ToggleService(new FakeRunner(), new FakeEnv(), sp, cj).ApplyWiring(cfg);

        var c = File.ReadAllText(cj);
        Assert.DoesNotContain("\"semble\"", c);
        Assert.Contains("playwright", c);
    }

    [Fact]
    public void ApplyWiring_AllOn_WiresEverything()
    {
        var env = new FakeEnv();
        var (sp, cj) = TempFiles("{}", "{}");
        var cfg = StackConfig.CreateDefault(@"C:\ts"); // all enabled

        var st = new ToggleService(TaskRunningRunner(), env, sp, cj).ApplyWiring(cfg);

        Assert.True(st is { Headroom: true, Rtk: true, Semble: true });
        Assert.Equal("http://127.0.0.1:8787", env.User["ANTHROPIC_BASE_URL"]);
        Assert.Contains("rtk.exe", File.ReadAllText(sp));
        Assert.Contains("semble", File.ReadAllText(cj));
    }
}
