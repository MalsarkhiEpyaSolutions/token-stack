using System.Text.Json.Nodes;
using TokenStack.Core.Claude;
using Xunit;

namespace TokenStack.Tests;

public class ClaudeSurgeonTests
{
    private const string RealisticSettings = """
    {
      "env": { "ANTHROPIC_BASE_URL": "http://127.0.0.1:8787" },
      "permissions": { "defaultMode": "auto" },
      "hooks": {
        "SessionStart": [
          { "matcher": "startup|resume", "hooks": [
              { "type": "command", "command": "\"D:\\proxy tokens\\venv\\Scripts\\headroom.exe\" init hook ensure --profile init-user --marker headroom-init-claude", "timeout": 15 } ] },
          { "matcher": "startup|resume", "hooks": [
              { "type": "command", "command": "powershell -NoProfile -ExecutionPolicy Bypass -File \"C:\\Users\\u\\.claude\\hooks\\ensure-stack.ps1\"", "timeout": 30 } ] }
        ],
        "PreToolUse": [
          { "matcher": "Bash", "hooks": [
              { "type": "command", "command": "\"D:\\proxy tokens\\rtk\\rtk.exe\" hook claude" } ] }
        ]
      },
      "enabledPlugins": { "superpowers@claude-plugins-official": true },
      "extraKnownMarketplaces": { "x": { "source": { "source": "github", "repo": "a/b" } } },
      "theme": "dark-daltonized"
    }
    """;

    private static JsonNode Parse(string s) => JsonNode.Parse(s)!;

    // ---------- RTK PreToolUse hook ----------

    [Fact]
    public void EnsureRtkHook_RewritesStalePath_NoDuplicate()
    {
        var root = Parse(RealisticSettings);
        var changed = ClaudeSurgeon.EnsureRtkHook(root, @"C:\ts\rtk\rtk.exe", "Bash");
        Assert.True(changed);
        var pre = root["hooks"]!["PreToolUse"]!.AsArray();
        Assert.Single(pre);
        var cmd = pre[0]!["hooks"]![0]!["command"]!.GetValue<string>();
        Assert.Equal("\"C:\\ts\\rtk\\rtk.exe\" hook claude", cmd);
    }

    [Fact]
    public void EnsureRtkHook_Idempotent()
    {
        var root = Parse(RealisticSettings);
        ClaudeSurgeon.EnsureRtkHook(root, @"C:\ts\rtk\rtk.exe", "Bash");
        var changed = ClaudeSurgeon.EnsureRtkHook(root, @"C:\ts\rtk\rtk.exe", "Bash");
        Assert.False(changed);
        Assert.Single(root["hooks"]!["PreToolUse"]!.AsArray());
    }

    [Fact]
    public void EnsureRtkHook_CreatesStructure_WhenNoHooksObject()
    {
        var root = Parse("""{ "theme": "dark" }""");
        ClaudeSurgeon.EnsureRtkHook(root, @"C:\ts\rtk\rtk.exe", "Bash");
        Assert.Equal("Bash", root["hooks"]!["PreToolUse"]![0]!["matcher"]!.GetValue<string>());
        Assert.Equal("dark", root["theme"]!.GetValue<string>()); // untouched sibling
    }

    [Fact]
    public void RemoveRtkHook_RemovesOnlyOurs()
    {
        var root = Parse(RealisticSettings);
        var changed = ClaudeSurgeon.RemoveRtkHook(root);
        Assert.True(changed);
        Assert.Empty(root["hooks"]!["PreToolUse"]!.AsArray());
        Assert.NotNull(root["hooks"]!["SessionStart"]); // untouched
    }

    // ---------- SessionStart status hook ----------

    [Fact]
    public void EnsureSessionStatusHook_ReplacesBothLegacyEntries_WithSingleExeCall()
    {
        var root = Parse(RealisticSettings);
        var changed = ClaudeSurgeon.EnsureSessionStatusHook(root, @"C:\ts\token-stack.exe");
        Assert.True(changed);
        var ss = root["hooks"]!["SessionStart"]!.AsArray();
        Assert.Single(ss);
        var entry = ss[0]!;
        Assert.Equal("startup|resume", entry["matcher"]!.GetValue<string>());
        var hook = entry["hooks"]![0]!;
        Assert.Equal("\"C:\\ts\\token-stack.exe\" status --hook", hook["command"]!.GetValue<string>());
        Assert.Equal(30, hook["timeout"]!.GetValue<int>());
    }

    [Fact]
    public void EnsureSessionStatusHook_PreservesForeignSessionStartEntries()
    {
        var root = Parse("""
        { "hooks": { "SessionStart": [
            { "matcher": "startup", "hooks": [ { "type": "command", "command": "my-own-tool.exe" } ] }
        ] } }
        """);
        ClaudeSurgeon.EnsureSessionStatusHook(root, @"C:\ts\token-stack.exe");
        var ss = root["hooks"]!["SessionStart"]!.AsArray();
        Assert.Equal(2, ss.Count); // foreign + ours
        Assert.Equal("my-own-tool.exe", ss[0]!["hooks"]![0]!["command"]!.GetValue<string>());
    }

    [Fact]
    public void RemoveSessionStatusHook_Idempotent()
    {
        var root = Parse(RealisticSettings);
        ClaudeSurgeon.EnsureSessionStatusHook(root, @"C:\ts\token-stack.exe");
        Assert.True(ClaudeSurgeon.RemoveSessionStatusHook(root));
        Assert.False(ClaudeSurgeon.RemoveSessionStatusHook(root));
        Assert.Empty(root["hooks"]!["SessionStart"]!.AsArray());
    }

    // ---------- env.ANTHROPIC_BASE_URL ----------

    [Fact]
    public void SetEnvBaseUrl_PreservesSiblings()
    {
        var root = Parse("""{ "env": { "OTHER": "x" }, "theme": "dark" }""");
        ClaudeSurgeon.SetEnvBaseUrl(root, "http://127.0.0.1:9000");
        Assert.Equal("http://127.0.0.1:9000", root["env"]!["ANTHROPIC_BASE_URL"]!.GetValue<string>());
        Assert.Equal("x", root["env"]!["OTHER"]!.GetValue<string>());
    }

    [Fact]
    public void RemoveEnvBaseUrl_DropsEmptyEnvObject()
    {
        var root = Parse("""{ "env": { "ANTHROPIC_BASE_URL": "http://127.0.0.1:8787" } }""");
        Assert.True(ClaudeSurgeon.RemoveEnvBaseUrl(root));
        Assert.Null(root["env"]);
    }

    // ---------- mcpServers.semble (in ~/.claude.json) ----------

    [Fact]
    public void EnsureSembleMcp_AddsAbsoluteExePath_PreservingOtherServers()
    {
        var root = Parse("""{ "mcpServers": { "playwright": { "command": "npx" } }, "projects": {} }""");
        var changed = ClaudeSurgeon.EnsureSembleMcp(root, @"C:\u\.local\bin\semble.exe");
        Assert.True(changed);
        var semble = root["mcpServers"]!["semble"]!;
        Assert.Equal("stdio", semble["type"]!.GetValue<string>());
        Assert.Equal(@"C:\u\.local\bin\semble.exe", semble["command"]!.GetValue<string>());
        Assert.Empty(semble["args"]!.AsArray());
        Assert.NotNull(root["mcpServers"]!["playwright"]); // untouched
        Assert.NotNull(root["projects"]);                   // untouched
    }

    [Fact]
    public void EnsureSembleMcp_RejectsUvxStyleCommand_ByRewriting()
    {
        var root = Parse("""{ "mcpServers": { "semble": { "type": "stdio", "command": "uvx", "args": ["--from","semble[mcp]","semble"] } } }""");
        var changed = ClaudeSurgeon.EnsureSembleMcp(root, @"C:\u\.local\bin\semble.exe");
        Assert.True(changed);
        Assert.Equal(@"C:\u\.local\bin\semble.exe",
            root["mcpServers"]!["semble"]!["command"]!.GetValue<string>());
    }

    [Fact]
    public void RemoveSembleMcp_LeavesOtherServers()
    {
        var root = Parse("""{ "mcpServers": { "semble": { "command": "x" }, "playwright": { "command": "npx" } } }""");
        Assert.True(ClaudeSurgeon.RemoveSembleMcp(root));
        Assert.Null(root["mcpServers"]!["semble"]);
        Assert.NotNull(root["mcpServers"]!["playwright"]);
    }

    // ---------- file editor + backup ----------

    [Fact]
    public void Editor_SaveWithBackup_WritesTimestampedSibling_WithOriginalContent()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ts-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "settings.json");
        File.WriteAllText(file, """{ "a": 1 }""");

        var editor = new ClaudeFileEditor(file, () => new DateTimeOffset(2026, 6, 13, 10, 30, 0, TimeSpan.Zero));
        var root = editor.Load();
        root.AsObject()["b"] = 2;
        editor.SaveWithBackup(root);

        var backup = Path.Combine(dir, "settings.json.token-stack-backup-20260613-103000");
        Assert.True(File.Exists(backup));
        Assert.Equal("""{ "a": 1 }""", File.ReadAllText(backup)); // byte-exact original
        Assert.Contains("\"b\"", File.ReadAllText(file));
    }

    [Fact]
    public void Editor_Load_MissingFile_ReturnsEmptyObject()
    {
        var file = Path.Combine(Path.GetTempPath(), "ts-tests", Guid.NewGuid().ToString("N"), "settings.json");
        var editor = new ClaudeFileEditor(file, () => DateTimeOffset.UtcNow);
        Assert.Empty(editor.Load().AsObject());
    }
}
