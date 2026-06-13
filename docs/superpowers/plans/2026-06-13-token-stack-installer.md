# token-stack.exe v1.0 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `token-stack.exe` — a self-contained Windows CLI that installs, configures, supervises, diagnoses, and uninstalls the Headroom + RTK + Semble token-optimization stack for Claude Code.

**Architecture:** Three projects: `TokenStack.Cli` (Spectre.Console.Cli command surface, thin), `TokenStack.Core` (all logic: config, Claude-file JSON surgery, Windows adapters, components, doctor), `TokenStack.Tests` (xUnit, fixture-driven). All side effects go through small injected interfaces (`IProcessRunner`, `IEnvStore`, `IPortProbe`, `IHttpProbe`) so logic is unit-testable; the real adapters are thin. Spec: `docs/superpowers/specs/2026-06-13-token-stack-installer-design.md`.

**Tech Stack:** .NET 10 (`net10.0`), C#, Spectre.Console + Spectre.Console.Cli, System.Text.Json (`JsonNode` for surgical merges), xUnit. Publish: `win-x64`, self-contained, single file.

**Working directory for ALL commands:** `D:\token-stack` (the repo root).

---

## File structure (locked decomposition)

```
src/TokenStack.Cli/
  TokenStack.Cli.csproj
  Program.cs                      # CommandApp registration only
  Commands/                       # one file per command, thin: parse → Core → render
    InstallCommand.cs  StatusCommand.cs  ServiceCommands.cs (start/stop/restart)
    ConfigCommand.cs   DoctorCommand.cs  UpdateCommand.cs
    GainCommand.cs     UninstallCommand.cs
src/TokenStack.Core/
  TokenStack.Core.csproj
  Config/StackConfig.cs           # POCO schema + defaults
  Config/ConfigStore.cs           # load/save/default-path/dot-path get+set
  Config/ConfigValidator.cs       # domain validation (no-space root, port, mode, matcher)
  Status/StackStatus.cs           # status model + HeadroomState enum
  Status/StatusLine.cs            # the one-line builder (truth table)
  Status/StatusProbe.cs           # gathers live StackStatus via adapters
  Claude/ClaudeSurgeon.cs         # pure JsonNode operations on settings.json/.claude.json
  Claude/ClaudeFileEditor.cs      # load/backup/save wrapper
  Windows/Interfaces.cs           # IProcessRunner, IEnvStore, IPortProbe, IHttpProbe (+records)
  Windows/RealAdapters.cs         # production implementations
  Windows/TaskXml.cs              # scheduled-task XML renderer
  Windows/ScheduledTaskManager.cs # schtasks create/start/stop/delete/query
  Windows/UserPath.cs             # User PATH ensure/remove (dedup-safe)
  Components/Bootstrap.cs         # uv detect/install
  Components/HeadroomComponent.cs # venv, pip, run_proxy.py render, task, readyz
  Components/RtkComponent.cs      # download/extract/PATH/hook
  Components/SembleComponent.cs   # uv tool install, MCP registration
  Components/RoutingManager.cs    # settings env + User env var + conflict report
  Install/InstallPipeline.cs      # ordered steps with verification gates
  Doctor/DoctorChecks.cs          # ICheck + the 10 checks + runner
  Resources/run_proxy.py.tmpl     # embedded template
src/TokenStack.Tests/
  TokenStack.Tests.csproj
  ConfigTests.cs  StatusLineTests.cs  ClaudeSurgeonTests.cs  TaskXmlTests.cs
  UserPathTests.cs  HeadroomTests.cs  RtkTests.cs  SembleTests.cs
  RoutingTests.cs  DoctorTests.cs
  Fakes.cs                        # FakeRunner/FakeEnv/FakePort/FakeHttp shared fakes
publish.ps1                       # builds dist/token-stack-v<ver>.zip
README.md
.gitignore
```

---

### Task 0: Solution scaffold

**Files:**
- Create: `.gitignore`, `TokenStack.sln`, `src/TokenStack.Core/TokenStack.Core.csproj`, `src/TokenStack.Cli/TokenStack.Cli.csproj`, `src/TokenStack.Tests/TokenStack.Tests.csproj`, `src/TokenStack.Cli/Program.cs`

- [ ] **Step 0.1: Create projects and solution**

Run (from `D:\token-stack`):
```powershell
dotnet new sln -n TokenStack
dotnet new classlib -n TokenStack.Core -o src/TokenStack.Core -f net10.0
dotnet new console  -n TokenStack.Cli  -o src/TokenStack.Cli  -f net10.0
dotnet new xunit    -n TokenStack.Tests -o src/TokenStack.Tests -f net10.0
dotnet sln add src/TokenStack.Core src/TokenStack.Cli src/TokenStack.Tests
dotnet add src/TokenStack.Cli reference src/TokenStack.Core
dotnet add src/TokenStack.Tests reference src/TokenStack.Core
dotnet add src/TokenStack.Cli package Spectre.Console.Cli
dotnet add src/TokenStack.Cli package Spectre.Console
Remove-Item src/TokenStack.Core/Class1.cs
Remove-Item src/TokenStack.Tests/UnitTest1.cs
```

- [ ] **Step 0.2: Set CLI assembly name + write .gitignore**

Edit `src/TokenStack.Cli/TokenStack.Cli.csproj` so the `<PropertyGroup>` contains:
```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <TargetFramework>net10.0</TargetFramework>
  <ImplicitUsings>enable</ImplicitUsings>
  <Nullable>enable</Nullable>
  <AssemblyName>token-stack</AssemblyName>
  <InvariantGlobalization>true</InvariantGlobalization>
</PropertyGroup>
```

Create `.gitignore`:
```gitignore
bin/
obj/
dist/
*.user
.vs/
```

Replace `src/TokenStack.Cli/Program.cs` with a placeholder main (replaced in Task 10):
```csharp
Console.WriteLine("token-stack (commands wired in Task 10)");
```

- [ ] **Step 0.3: Verify build**

Run: `dotnet build TokenStack.sln`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 0.4: Commit**

```powershell
git add -A
git commit -m "chore: scaffold TokenStack solution (Core, Cli, Tests)"
```

---

### Task 1: Config schema, store, dot-path access, validation

**Files:**
- Create: `src/TokenStack.Core/Config/StackConfig.cs`, `src/TokenStack.Core/Config/ConfigStore.cs`, `src/TokenStack.Core/Config/ConfigValidator.cs`
- Test: `src/TokenStack.Tests/ConfigTests.cs`

- [ ] **Step 1.1: Write the failing tests**

Create `src/TokenStack.Tests/ConfigTests.cs`:
```csharp
using TokenStack.Core.Config;
using Xunit;

namespace TokenStack.Tests;

public class ConfigTests
{
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "ts-tests", Guid.NewGuid().ToString("N"), "config.json");

    [Fact]
    public void CreateDefault_HasSpecDefaults()
    {
        var c = StackConfig.CreateDefault(@"C:\Users\x\AppData\Local\token-stack");
        Assert.Equal(1, c.SchemaVersion);
        Assert.True(c.Headroom.Enabled);
        Assert.Equal(8787, c.Headroom.Port);
        Assert.Equal("token", c.Headroom.Mode);
        Assert.Equal("0.24.0", c.Headroom.Version);
        Assert.Equal("3.12", c.Headroom.PythonVersion);
        Assert.Equal("0.42.3", c.Rtk.Version);
        Assert.Equal("github:rtk-ai/rtk", c.Rtk.Source);
        Assert.Equal("Bash", c.Rtk.HookMatcher);
        Assert.Equal("latest", c.Semble.Version);
        Assert.True(c.Routing.Cli);
        Assert.True(c.Routing.Desktop);
        Assert.True(c.Hooks.SessionStatusLine);
        Assert.Equal("auto", c.Bootstrap.UvInstaller);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var path = TempFile();
        var c = StackConfig.CreateDefault(@"C:\ts");
        c.Headroom.Port = 9000;
        ConfigStore.Save(c, path);
        var back = ConfigStore.Load(path);
        Assert.Equal(9000, back.Headroom.Port);
        Assert.Equal(@"C:\ts", back.InstallRoot);
    }

    [Fact]
    public void Load_PreservesUnknownKeys_OnSave()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"schemaVersion":1,"installRoot":"C:\\ts","futureKey":{"x":1},"headroom":{"enabled":true,"port":8787,"mode":"token","version":"0.24.0","pythonVersion":"3.12","extraArgs":[]},"rtk":{"enabled":true,"version":"0.42.3","source":"github:rtk-ai/rtk","hookMatcher":"Bash"},"semble":{"enabled":true,"version":"latest"},"routing":{"cli":true,"desktop":true},"hooks":{"sessionStatusLine":true},"bootstrap":{"uvInstaller":"auto"}}""");
        ConfigStore.SetValue(path, "headroom.port", "9001");
        var raw = File.ReadAllText(path);
        Assert.Contains("futureKey", raw);
        Assert.Contains("9001", raw);
    }

    [Theory]
    [InlineData("headroom.port", "9000")]
    [InlineData("headroom.mode", "cache")]
    [InlineData("routing.desktop", "false")]
    [InlineData("rtk.enabled", "false")]
    public void SetValue_ThenGetValue_RoundTrips(string key, string value)
    {
        var path = TempFile();
        ConfigStore.Save(StackConfig.CreateDefault(@"C:\ts"), path);
        ConfigStore.SetValue(path, key, value);
        Assert.Equal(value, ConfigStore.GetValue(path, key));
    }

    [Fact]
    public void SetValue_UnknownKey_Throws()
    {
        var path = TempFile();
        ConfigStore.Save(StackConfig.CreateDefault(@"C:\ts"), path);
        Assert.Throws<ArgumentException>(() => ConfigStore.SetValue(path, "headroom.nope", "1"));
    }

    [Theory]
    [InlineData(@"C:\proxy tokens", "installRoot")]      // spaces — the motivating mistake
    [InlineData(@"relative\path", "installRoot")]
    public void Validate_RejectsBadInstallRoot(string root, string expectedField)
    {
        var c = StackConfig.CreateDefault(root);
        var errors = ConfigValidator.Validate(c);
        Assert.Contains(errors, e => e.Contains(expectedField));
    }

    [Fact]
    public void Validate_RejectsBadPortModeMatcher()
    {
        var c = StackConfig.CreateDefault(@"C:\ts");
        c.Headroom.Port = 80;            // < 1024 reserved
        c.Headroom.Mode = "turbo";       // not in token|cache|passthrough
        c.Rtk.HookMatcher = "PowerShell"; // forbidden by design
        var errors = ConfigValidator.Validate(c);
        Assert.Equal(3, errors.Count);
    }

    [Fact]
    public void Validate_AcceptsDefault()
    {
        Assert.Empty(ConfigValidator.Validate(StackConfig.CreateDefault(@"C:\ts")));
    }
}
```

- [ ] **Step 1.2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ConfigTests"`
Expected: FAIL — compile errors (`StackConfig` not defined).

- [ ] **Step 1.3: Implement**

Create `src/TokenStack.Core/Config/StackConfig.cs`:
```csharp
using System.Text.Json.Serialization;

namespace TokenStack.Core.Config;

public sealed class StackConfig
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("installRoot")] public string InstallRoot { get; set; } = "";
    [JsonPropertyName("headroom")] public HeadroomConfig Headroom { get; set; } = new();
    [JsonPropertyName("rtk")] public RtkConfig Rtk { get; set; } = new();
    [JsonPropertyName("semble")] public SembleConfig Semble { get; set; } = new();
    [JsonPropertyName("routing")] public RoutingConfig Routing { get; set; } = new();
    [JsonPropertyName("hooks")] public HooksConfig Hooks { get; set; } = new();
    [JsonPropertyName("bootstrap")] public BootstrapConfig Bootstrap { get; set; } = new();

    public static StackConfig CreateDefault(string installRoot) => new() { InstallRoot = installRoot };
}

public sealed class HeadroomConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("port")] public int Port { get; set; } = 8787;
    [JsonPropertyName("mode")] public string Mode { get; set; } = "token";
    [JsonPropertyName("version")] public string Version { get; set; } = "0.24.0";
    [JsonPropertyName("pythonVersion")] public string PythonVersion { get; set; } = "3.12";
    [JsonPropertyName("extraArgs")] public List<string> ExtraArgs { get; set; } = new();
}

public sealed class RtkConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("version")] public string Version { get; set; } = "0.42.3";
    [JsonPropertyName("source")] public string Source { get; set; } = "github:rtk-ai/rtk";
    [JsonPropertyName("hookMatcher")] public string HookMatcher { get; set; } = "Bash";
}

public sealed class SembleConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("version")] public string Version { get; set; } = "latest";
}

public sealed class RoutingConfig
{
    [JsonPropertyName("cli")] public bool Cli { get; set; } = true;
    [JsonPropertyName("desktop")] public bool Desktop { get; set; } = true;
}

public sealed class HooksConfig
{
    [JsonPropertyName("sessionStatusLine")] public bool SessionStatusLine { get; set; } = true;
}

public sealed class BootstrapConfig
{
    [JsonPropertyName("uvInstaller")] public string UvInstaller { get; set; } = "auto";
}
```

Create `src/TokenStack.Core/Config/ConfigStore.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TokenStack.Core.Config;

public static class ConfigStore
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "token-stack", "config.json");

    public static StackConfig Load(string path)
    {
        var node = JsonNode.Parse(File.ReadAllText(path))
                   ?? throw new InvalidDataException($"Empty config: {path}");
        return node.Deserialize<StackConfig>(Opts)
               ?? throw new InvalidDataException($"Unreadable config: {path}");
    }

    public static void Save(StackConfig cfg, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(cfg, Opts));
    }

    /// <summary>Dot-path read straight from the file (preserving-unknown-keys path).</summary>
    public static string GetValue(string path, string key)
    {
        var node = JsonNode.Parse(File.ReadAllText(path))!;
        var target = Walk(node, key) ?? throw new ArgumentException($"Unknown config key: {key}");
        return target.ToJsonString().Trim('"');
    }

    /// <summary>Dot-path write. Validates the key exists in the schema, preserves unknown keys,
    /// and validates the resulting config before saving.</summary>
    public static void SetValue(string path, string key, string value)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))!;
        var existing = Walk(root, key) ?? throw new ArgumentException($"Unknown config key: {key}");

        JsonNode newNode = existing.GetValueKind() switch
        {
            JsonValueKind.Number => JsonValue.Create(long.Parse(value)),
            JsonValueKind.True or JsonValueKind.False => JsonValue.Create(bool.Parse(value)),
            JsonValueKind.Array => JsonNode.Parse(value)!,
            _ => JsonValue.Create(value)!,
        };

        var parts = key.Split('.');
        var parent = parts.Length == 1 ? root : Walk(root, string.Join('.', parts[..^1]))!;
        parent.AsObject()[parts[^1]] = newNode;

        var candidate = root.Deserialize<StackConfig>(Opts)!;
        var errors = ConfigValidator.Validate(candidate);
        if (errors.Count > 0)
            throw new ArgumentException($"Invalid value for {key}: {string.Join("; ", errors)}");

        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static JsonNode? Walk(JsonNode root, string dotPath)
    {
        JsonNode? cur = root;
        foreach (var part in dotPath.Split('.'))
        {
            cur = cur?.AsObject().TryGetPropertyValue(part, out var next) == true ? next : null;
            if (cur is null) return null;
        }
        return cur;
    }
}
```

Create `src/TokenStack.Core/Config/ConfigValidator.cs`:
```csharp
namespace TokenStack.Core.Config;

public static class ConfigValidator
{
    private static readonly string[] Modes = { "token", "cache", "passthrough" };

    public static List<string> Validate(StackConfig c)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(c.InstallRoot) || !Path.IsPathRooted(c.InstallRoot))
            errors.Add("installRoot must be an absolute path");
        else if (c.InstallRoot.Contains(' '))
            errors.Add("installRoot must not contain spaces (breaks hook quoting; see spec §5.0)");
        if (c.Headroom.Port is < 1024 or > 65535)
            errors.Add("headroom.port must be 1024..65535");
        if (!Modes.Contains(c.Headroom.Mode))
            errors.Add("headroom.mode must be one of: token, cache, passthrough");
        if (c.Rtk.HookMatcher != "Bash")
            errors.Add("rtk.hookMatcher must be \"Bash\" (RTK cannot wrap PowerShell aliases; see spec §5.4)");
        return errors;
    }
}
```

- [ ] **Step 1.4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~ConfigTests"`
Expected: PASS (9 tests).

- [ ] **Step 1.5: Commit**

```powershell
git add -A
git commit -m "feat(core): config schema, store with dot-path get/set, validation"
```

---

### Task 2: Status model + one-line builder (truth table)

**Files:**
- Create: `src/TokenStack.Core/Status/StackStatus.cs`, `src/TokenStack.Core/Status/StatusLine.cs`
- Test: `src/TokenStack.Tests/StatusLineTests.cs`

- [ ] **Step 2.1: Write the failing tests**

Create `src/TokenStack.Tests/StatusLineTests.cs`:
```csharp
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
```

- [ ] **Step 2.2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~StatusLineTests"`
Expected: FAIL — compile errors (`StackStatus` not defined).

- [ ] **Step 2.3: Implement**

Create `src/TokenStack.Core/Status/StackStatus.cs`:
```csharp
namespace TokenStack.Core.Status;

/// <summary>Snapshot of live stack health. Routed = the CURRENT process env points at the proxy.</summary>
public sealed record StackStatus(
    bool TaskRunning,
    bool PortListening,
    bool Routed,
    long? Reqs,
    int Port,
    bool RtkOnPath,
    bool SembleWired);
```

Create `src/TokenStack.Core/Status/StatusLine.cs`:
```csharp
namespace TokenStack.Core.Status;

/// <summary>Renders the exact one-line session status (format-compatible with the
/// retired ensure-stack.ps1 so users see a familiar line).</summary>
public static class StatusLine
{
    public static string Build(StackStatus s)
    {
        string headroom;
        if (!s.TaskRunning) headroom = "DOWN";
        else if (!s.PortListening) headroom = "starting (cold-load ~50s)";
        else
        {
            var route = s.Routed ? "ROUTED" : "BYPASSED";
            var reqs = s.Reqs is { } n ? $", reqs={n}" : "";
            headroom = $"up (:{s.Port}, {route}{reqs})";
        }
        var rtk = s.RtkOnPath ? "up" : "MISSING";
        var semble = s.SembleWired ? "up (MCP)" : "MISSING";
        return $"[token-stack] Headroom: {headroom} | RTK: {rtk} | Semble: {semble}";
    }
}
```

- [ ] **Step 2.4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~StatusLineTests"`
Expected: PASS (7 tests).

- [ ] **Step 2.5: Commit**

```powershell
git add -A
git commit -m "feat(core): status model + one-line builder (ROUTED/BYPASSED/cold-load truth table)"
```

---

### Task 3: Claude config surgery (settings.json / .claude.json) + backups

The highest-risk area: merging into the user's real Claude files without clobbering
anything. All operations are pure `JsonNode → bool changed` functions; file IO + backup
is a separate tiny wrapper.

**Files:**
- Create: `src/TokenStack.Core/Claude/ClaudeSurgeon.cs`, `src/TokenStack.Core/Claude/ClaudeFileEditor.cs`
- Test: `src/TokenStack.Tests/ClaudeSurgeonTests.cs`

- [ ] **Step 3.1: Write the failing tests**

Create `src/TokenStack.Tests/ClaudeSurgeonTests.cs` (fixture mirrors the real-world
settings.json shape from the reference machine — plugins, marketplaces, legacy hooks):
```csharp
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
```

- [ ] **Step 3.2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ClaudeSurgeonTests"`
Expected: FAIL — compile errors (`ClaudeSurgeon` not defined).

- [ ] **Step 3.3: Implement**

Create `src/TokenStack.Core/Claude/ClaudeSurgeon.cs`:
```csharp
using System.Text.Json.Nodes;

namespace TokenStack.Core.Claude;

/// <summary>Pure surgical operations on Claude config JSON trees. Every method returns
/// true when it changed the tree. Identification of "our" entries is by content
/// signature (command contains rtk.exe / token-stack.exe / the legacy scripts) — never
/// by array position, so user-owned entries are preserved.</summary>
public static class ClaudeSurgeon
{
    private static readonly string[] LegacySessionMarkers =
        { "ensure-stack.ps1", "headroom.exe\" init hook ensure", "token-stack.exe\" status --hook" };

    // ---------- RTK PreToolUse ----------

    public static bool EnsureRtkHook(JsonNode root, string rtkExePath, string matcher)
    {
        var wanted = $"\"{rtkExePath}\" hook claude";
        var pre = GetOrCreateArray(root, "hooks", "PreToolUse");
        var ours = pre.Where(IsRtkEntry).ToList();

        if (ours.Count == 1
            && ours[0]!["matcher"]?.GetValue<string>() == matcher
            && ours[0]!["hooks"]![0]!["command"]!.GetValue<string>() == wanted)
            return false;

        foreach (var o in ours) pre.Remove(o);
        pre.Add(new JsonObject
        {
            ["matcher"] = matcher,
            ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = wanted }),
        });
        return true;
    }

    public static bool RemoveRtkHook(JsonNode root)
    {
        var pre = root["hooks"]?["PreToolUse"]?.AsArray();
        if (pre is null) return false;
        var ours = pre.Where(IsRtkEntry).ToList();
        foreach (var o in ours) pre.Remove(o);
        return ours.Count > 0;
    }

    private static bool IsRtkEntry(JsonNode? entry) =>
        FirstCommand(entry)?.Contains("rtk.exe", StringComparison.OrdinalIgnoreCase) == true;

    // ---------- SessionStart status hook ----------

    public static bool EnsureSessionStatusHook(JsonNode root, string exePath)
    {
        var wanted = $"\"{exePath}\" status --hook";
        var ss = GetOrCreateArray(root, "hooks", "SessionStart");
        var ours = ss.Where(IsOurSessionEntry).ToList();

        if (ours.Count == 1 && FirstCommand(ours[0]) == wanted)
            return false;

        foreach (var o in ours) ss.Remove(o);
        ss.Add(new JsonObject
        {
            ["matcher"] = "startup|resume",
            ["hooks"] = new JsonArray(new JsonObject
            {
                ["type"] = "command",
                ["command"] = wanted,
                ["timeout"] = 30,
                ["statusMessage"] = "Checking token stack (Headroom/RTK/Semble)...",
            }),
        });
        return true;
    }

    public static bool RemoveSessionStatusHook(JsonNode root)
    {
        var ss = root["hooks"]?["SessionStart"]?.AsArray();
        if (ss is null) return false;
        var ours = ss.Where(IsOurSessionEntry).ToList();
        foreach (var o in ours) ss.Remove(o);
        return ours.Count > 0;
    }

    private static bool IsOurSessionEntry(JsonNode? entry)
    {
        var cmd = FirstCommand(entry);
        return cmd is not null && LegacySessionMarkers.Any(m =>
            cmd.Contains(m, StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("token-stack.exe", StringComparison.OrdinalIgnoreCase));
    }

    // ---------- env.ANTHROPIC_BASE_URL ----------

    public static bool SetEnvBaseUrl(JsonNode root, string url)
    {
        var env = root["env"]?.AsObject();
        if (env is null) { env = new JsonObject(); root.AsObject()["env"] = env; }
        if (env["ANTHROPIC_BASE_URL"]?.GetValue<string>() == url) return false;
        env["ANTHROPIC_BASE_URL"] = url;
        return true;
    }

    public static bool RemoveEnvBaseUrl(JsonNode root)
    {
        var env = root["env"]?.AsObject();
        if (env is null || !env.ContainsKey("ANTHROPIC_BASE_URL")) return false;
        env.Remove("ANTHROPIC_BASE_URL");
        if (env.Count == 0) root.AsObject().Remove("env");
        return true;
    }

    // ---------- mcpServers.semble ----------

    public static bool EnsureSembleMcp(JsonNode root, string sembleExePath)
    {
        var servers = root["mcpServers"]?.AsObject();
        if (servers is null) { servers = new JsonObject(); root.AsObject()["mcpServers"] = servers; }

        var existing = servers["semble"];
        if (existing?["command"]?.GetValue<string>() == sembleExePath
            && existing["type"]?.GetValue<string>() == "stdio")
            return false;

        servers["semble"] = new JsonObject
        {
            ["type"] = "stdio",
            ["command"] = sembleExePath,
            ["args"] = new JsonArray(),
            ["env"] = new JsonObject(),
        };
        return true;
    }

    public static bool RemoveSembleMcp(JsonNode root)
    {
        var servers = root["mcpServers"]?.AsObject();
        if (servers is null || !servers.ContainsKey("semble")) return false;
        servers.Remove("semble");
        return true;
    }

    // ---------- helpers ----------

    private static string? FirstCommand(JsonNode? entry) =>
        entry?["hooks"]?.AsArray().FirstOrDefault()?["command"]?.GetValue<string>();

    private static JsonArray GetOrCreateArray(JsonNode root, string parent, string key)
    {
        var p = root[parent]?.AsObject();
        if (p is null) { p = new JsonObject(); root.AsObject()[parent] = p; }
        var arr = p[key]?.AsArray();
        if (arr is null) { arr = new JsonArray(); p[key] = arr; }
        return arr;
    }
}
```

Create `src/TokenStack.Core/Claude/ClaudeFileEditor.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TokenStack.Core.Claude;

/// <summary>Load/backup/save wrapper for a Claude config file. Backup is written
/// only when saving, as a timestamped sibling of the original content.</summary>
public sealed class ClaudeFileEditor
{
    private readonly string _path;
    private readonly Func<DateTimeOffset> _clock;
    private string? _originalText;

    public ClaudeFileEditor(string path, Func<DateTimeOffset>? clock = null)
    {
        _path = path;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public JsonNode Load()
    {
        if (!File.Exists(_path)) { _originalText = null; return new JsonObject(); }
        _originalText = File.ReadAllText(_path);
        return JsonNode.Parse(_originalText) ?? new JsonObject();
    }

    public void SaveWithBackup(JsonNode root)
    {
        if (_originalText is not null)
        {
            var stamp = _clock().ToString("yyyyMMdd-HHmmss");
            File.WriteAllText($"{_path}.token-stack-backup-{stamp}", _originalText);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
```

- [ ] **Step 3.4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~ClaudeSurgeonTests"`
Expected: PASS (13 tests).

- [ ] **Step 3.5: Commit**

```powershell
git add -A
git commit -m "feat(core): surgical Claude config editing (hooks, env, MCP) with timestamped backups"
```

---

### Task 4: Windows adapter seam (interfaces + fakes + task XML + PATH editing)

**Files:**
- Create: `src/TokenStack.Core/Windows/Interfaces.cs`, `src/TokenStack.Core/Windows/RealAdapters.cs`, `src/TokenStack.Core/Windows/TaskXml.cs`, `src/TokenStack.Core/Windows/ScheduledTaskManager.cs`, `src/TokenStack.Core/Windows/UserPath.cs`
- Test: `src/TokenStack.Tests/Fakes.cs`, `src/TokenStack.Tests/TaskXmlTests.cs`, `src/TokenStack.Tests/UserPathTests.cs`

- [ ] **Step 4.1: Write the failing tests**

Create `src/TokenStack.Tests/Fakes.cs` (shared by every later task — keep it here):
```csharp
using TokenStack.Core.Windows;

namespace TokenStack.Tests;

public sealed class FakeRunner : IProcessRunner
{
    public readonly List<string> Calls = new();
    public Func<string, string, ProcResult> Handler { get; set; } =
        (_, _) => new ProcResult(0, "", "");

    public ProcResult Run(string file, string args, int timeoutMs = 120000)
    {
        Calls.Add($"{file} {args}".Trim());
        return Handler(file, args);
    }
}

public sealed class FakeEnv : IEnvStore
{
    public readonly Dictionary<string, string> User = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, string> Process = new(StringComparer.OrdinalIgnoreCase);

    public string? GetUser(string name) => User.TryGetValue(name, out var v) ? v : null;
    public void SetUser(string name, string? value)
    {
        if (value is null) User.Remove(name); else User[name] = value;
    }
    public string? GetProcess(string name) => Process.TryGetValue(name, out var v) ? v : null;
}

public sealed class FakePort : IPortProbe
{
    public bool Listening { get; set; }
    public bool IsListening(int port) => Listening;
}

public sealed class FakeHttp : IHttpProbe
{
    public Dictionary<string, string> Responses { get; } = new(); // url → body ("" = 200 empty)
    public string? Get(string url, int timeoutMs = 5000) =>
        Responses.TryGetValue(url, out var body) ? body : null;   // null = unreachable
}
```

Create `src/TokenStack.Tests/TaskXmlTests.cs`:
```csharp
using TokenStack.Core.Windows;
using Xunit;

namespace TokenStack.Tests;

public class TaskXmlTests
{
    [Fact]
    public void Render_ContainsAllSpecMandatedSettings()
    {
        var xml = TaskXml.Render(
            pythonwPath: @"C:\ts\venv\Scripts\pythonw.exe",
            scriptPath: @"C:\ts\run_proxy.py",
            workingDir: @"C:\ts",
            userId: @"DOMAIN\user");

        Assert.Contains(@"<Command>C:\ts\venv\Scripts\pythonw.exe</Command>", xml);
        Assert.Contains(@"<Arguments>""C:\ts\run_proxy.py""</Arguments>", xml);
        Assert.Contains(@"<WorkingDirectory>C:\ts</WorkingDirectory>", xml);
        Assert.Contains("<LogonTrigger>", xml);                          // at logon
        Assert.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>", xml); // no time limit
        Assert.Contains("<RestartOnFailure>", xml);
        Assert.Contains("<Count>3</Count>", xml);                        // RestartCount=3
        Assert.Contains("<Interval>PT1M</Interval>", xml);               // RestartInterval=1m
        Assert.Contains("<MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>", xml);
        Assert.Contains("<Hidden>true</Hidden>", xml);
        Assert.Contains(@"<UserId>DOMAIN\user</UserId>", xml);
        Assert.Contains("<RunLevel>LeastPrivilege</RunLevel>", xml);     // no admin
    }

    [Fact]
    public void Render_EscapesXmlSpecials()
    {
        var xml = TaskXml.Render(@"C:\a&b\pythonw.exe", @"C:\a&b\run.py", @"C:\a&b", "u");
        Assert.Contains("a&amp;b", xml);
        Assert.DoesNotContain("a&b\\pythonw", xml);
    }
}
```

Create `src/TokenStack.Tests/UserPathTests.cs`:
```csharp
using TokenStack.Core.Windows;
using Xunit;

namespace TokenStack.Tests;

public class UserPathTests
{
    [Fact]
    public void EnsureContains_Appends_WhenMissing()
    {
        var env = new FakeEnv();
        env.User["Path"] = @"C:\one;C:\two";
        Assert.True(UserPath.EnsureContains(env, @"C:\ts\rtk"));
        Assert.Equal(@"C:\one;C:\two;C:\ts\rtk", env.User["Path"]);
    }

    [Fact]
    public void EnsureContains_NoDuplicate_CaseAndTrailingSlashInsensitive()
    {
        var env = new FakeEnv();
        env.User["Path"] = @"C:\TS\RTK\;C:\two";
        Assert.False(UserPath.EnsureContains(env, @"C:\ts\rtk"));
        Assert.Equal(@"C:\TS\RTK\;C:\two", env.User["Path"]);
    }

    [Fact]
    public void EnsureContains_CreatesPath_WhenAbsent()
    {
        var env = new FakeEnv();
        Assert.True(UserPath.EnsureContains(env, @"C:\ts\rtk"));
        Assert.Equal(@"C:\ts\rtk", env.User["Path"]);
    }

    [Fact]
    public void Remove_StripsEntry_PreservingOthers()
    {
        var env = new FakeEnv();
        env.User["Path"] = @"C:\one;C:\ts\rtk;C:\two";
        Assert.True(UserPath.Remove(env, @"C:\ts\rtk\"));
        Assert.Equal(@"C:\one;C:\two", env.User["Path"]);
    }
}
```

- [ ] **Step 4.2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~TaskXmlTests|FullyQualifiedName~UserPathTests"`
Expected: FAIL — compile errors (`IProcessRunner` not defined).

- [ ] **Step 4.3: Implement**

Create `src/TokenStack.Core/Windows/Interfaces.cs`:
```csharp
namespace TokenStack.Core.Windows;

public sealed record ProcResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;
}

public interface IProcessRunner
{
    ProcResult Run(string file, string args, int timeoutMs = 120000);
}

public interface IEnvStore
{
    string? GetUser(string name);
    void SetUser(string name, string? value);   // null = delete
    string? GetProcess(string name);
}

public interface IPortProbe
{
    bool IsListening(int port);
}

public interface IHttpProbe
{
    /// <summary>GET url. Returns the body on 2xx, null on any error/timeout.</summary>
    string? Get(string url, int timeoutMs = 5000);
}
```

Create `src/TokenStack.Core/Windows/RealAdapters.cs`:
```csharp
using System.Diagnostics;
using System.Net.Http;
using System.Net.NetworkInformation;

namespace TokenStack.Core.Windows;

public sealed class RealProcessRunner : IProcessRunner
{
    public ProcResult Run(string file, string args, int timeoutMs = 120000)
    {
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        p.Start();
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeoutMs))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* already gone */ }
            return new ProcResult(-1, "", $"timeout after {timeoutMs}ms: {file} {args}");
        }
        return new ProcResult(p.ExitCode, stdout.Result, stderr.Result);
    }
}

public sealed class RealEnvStore : IEnvStore
{
    public string? GetUser(string name) =>
        Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
    public void SetUser(string name, string? value) =>
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
    public string? GetProcess(string name) =>
        Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
}

public sealed class RealPortProbe : IPortProbe
{
    public bool IsListening(int port) =>
        IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpListeners().Any(ep => ep.Port == port);
}

public sealed class RealHttpProbe : IHttpProbe
{
    public string? Get(string url, int timeoutMs = 5000)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
            using var resp = http.GetAsync(url).GetAwaiter().GetResult();
            return resp.IsSuccessStatusCode
                ? resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                : null;
        }
        catch { return null; }
    }
}
```

Create `src/TokenStack.Core/Windows/TaskXml.cs`:
```csharp
using System.Security;

namespace TokenStack.Core.Windows;

/// <summary>Renders the HeadroomProxy scheduled-task definition. Full XML (not schtasks
/// flags) is the only way to express RestartOnFailure + IgnoreNew + Hidden with fidelity.</summary>
public static class TaskXml
{
    public static string Render(string pythonwPath, string scriptPath, string workingDir, string userId)
    {
        string E(string s) => SecurityElement.Escape(s);
        return $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Description>token-stack: Headroom API-compression proxy (managed by token-stack.exe)</Description>
          </RegistrationInfo>
          <Triggers>
            <LogonTrigger>
              <Enabled>true</Enabled>
              <UserId>{E(userId)}</UserId>
            </LogonTrigger>
          </Triggers>
          <Principals>
            <Principal id="Author">
              <UserId>{E(userId)}</UserId>
              <LogonType>InteractiveToken</LogonType>
              <RunLevel>LeastPrivilege</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <AllowHardTerminate>true</AllowHardTerminate>
            <StartWhenAvailable>true</StartWhenAvailable>
            <AllowStartOnDemand>true</AllowStartOnDemand>
            <Enabled>true</Enabled>
            <Hidden>true</Hidden>
            <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
            <RestartOnFailure>
              <Interval>PT1M</Interval>
              <Count>3</Count>
            </RestartOnFailure>
          </Settings>
          <Actions Context="Author">
            <Exec>
              <Command>{E(pythonwPath)}</Command>
              <Arguments>"{E(scriptPath)}"</Arguments>
              <WorkingDirectory>{E(workingDir)}</WorkingDirectory>
            </Exec>
          </Actions>
        </Task>
        """;
    }
}
```

Create `src/TokenStack.Core/Windows/ScheduledTaskManager.cs`:
```csharp
namespace TokenStack.Core.Windows;

/// <summary>schtasks.exe wrapper for the HeadroomProxy task. Uses /xml registration
/// (full fidelity) and /query CSV for state.</summary>
public sealed class ScheduledTaskManager(IProcessRunner runner)
{
    public const string TaskName = "HeadroomProxy";

    public bool Exists() =>
        runner.Run("schtasks", $"/query /tn {TaskName}").Ok;

    public bool IsRunning()
    {
        var r = runner.Run("schtasks", $"/query /tn {TaskName} /fo csv /nh");
        return r.Ok && r.StdOut.Contains("\"Running\"", StringComparison.OrdinalIgnoreCase);
    }

    public void RegisterOrUpdate(string xml, string tempDir)
    {
        Directory.CreateDirectory(tempDir);
        var xmlPath = Path.Combine(tempDir, "HeadroomProxy.task.xml");
        File.WriteAllText(xmlPath, xml, System.Text.Encoding.Unicode); // schtasks expects UTF-16
        var r = runner.Run("schtasks", $"/create /tn {TaskName} /xml \"{xmlPath}\" /f");
        if (!r.Ok) throw new InvalidOperationException($"schtasks /create failed: {r.StdErr}{r.StdOut}");
    }

    public void Start() => runner.Run("schtasks", $"/run /tn {TaskName}");
    public void Stop() => runner.Run("schtasks", $"/end /tn {TaskName}");
    public void Unregister() => runner.Run("schtasks", $"/delete /tn {TaskName} /f");

    /// <summary>Zombie recovery: stop task, kill orphan pythonw running run_proxy, restart.</summary>
    public void RestartWithZombieKill()
    {
        Stop();
        runner.Run("powershell",
            "-NoProfile -Command \"Get-CimInstance Win32_Process -Filter \\\"Name='pythonw.exe'\\\" | " +
            "Where-Object { $_.CommandLine -match 'run_proxy|headroom' } | " +
            "ForEach-Object { Stop-Process -Id $_.ProcessId -Force }\"");
        Thread.Sleep(2000);
        Start();
    }
}
```

Create `src/TokenStack.Core/Windows/UserPath.cs`:
```csharp
namespace TokenStack.Core.Windows;

public static class UserPath
{
    public static bool EnsureContains(IEnvStore env, string dir)
    {
        var entries = Split(env.GetUser("Path"));
        if (entries.Any(e => Same(e, dir))) return false;
        entries.Add(dir.TrimEnd('\\'));
        env.SetUser("Path", string.Join(';', entries));
        return true;
    }

    public static bool Remove(IEnvStore env, string dir)
    {
        var entries = Split(env.GetUser("Path"));
        var kept = entries.Where(e => !Same(e, dir)).ToList();
        if (kept.Count == entries.Count) return false;
        env.SetUser("Path", kept.Count == 0 ? null : string.Join(';', kept));
        return true;
    }

    private static List<string> Split(string? path) =>
        (path ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();

    private static bool Same(string a, string b) =>
        string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 4.4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~TaskXmlTests|FullyQualifiedName~UserPathTests"`
Expected: PASS (6 tests). Also run the full suite to confirm nothing broke: `dotnet test`.

- [ ] **Step 4.5: Commit**

```powershell
git add -A
git commit -m "feat(core): windows adapter seam, scheduled-task XML/manager, user PATH editing"
```

---

### Task 5: Bootstrap (uv) + Headroom component

**Files:**
- Create: `src/TokenStack.Core/Components/Bootstrap.cs`, `src/TokenStack.Core/Components/HeadroomComponent.cs`, `src/TokenStack.Core/Resources/run_proxy.py.tmpl`
- Modify: `src/TokenStack.Core/TokenStack.Core.csproj` (embed the template)
- Test: `src/TokenStack.Tests/HeadroomTests.cs`

- [ ] **Step 5.1: Embed the template**

Create `src/TokenStack.Core/Resources/run_proxy.py.tmpl` (the pythonw stdout-redirect
lesson lives here — redirect BEFORE importing headroom or the banner crashes it):
```python
"""Windowless launcher for the Headroom proxy. GENERATED by token-stack.exe - do not edit.
pythonw.exe has no console: sys.stdout/sys.stderr are None and the Headroom CLI's startup
banner would crash it (exit 1, nothing logged). Redirect BEFORE importing headroom."""
import os
import sys

_here = os.path.dirname(os.path.abspath(__file__))
_log = open(os.path.join(_here, "proxy.log"), "a", encoding="utf-8", buffering=1)
sys.stdout = _log
sys.stderr = _log
try:
    sys.stdin = open(os.devnull, "r")
except OSError:
    pass

from headroom.cli import main

if __name__ == "__main__":
    sys.argv = {{ARGV}}
    main()
```

Add to `src/TokenStack.Core/TokenStack.Core.csproj` inside `<Project>`:
```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\run_proxy.py.tmpl" />
</ItemGroup>
```

- [ ] **Step 5.2: Write the failing tests**

Create `src/TokenStack.Tests/HeadroomTests.cs`:
```csharp
using TokenStack.Core.Components;
using TokenStack.Core.Config;
using Xunit;

namespace TokenStack.Tests;

public class HeadroomTests
{
    [Theory] // NOTE: System.Text.Json emits compact arrays — no spaces after commas
    [InlineData("token", 8787, "[\"headroom\",\"proxy\",\"--port\",\"8787\"]")]
    [InlineData("cache", 8787, "[\"headroom\",\"proxy\",\"--port\",\"8787\",\"--mode\",\"cache\"]")]
    [InlineData("passthrough", 9000, "[\"headroom\",\"proxy\",\"--port\",\"9000\",\"--no-optimize\"]")]
    public void BuildArgv_MapsModeAndPort(string mode, int port, string expected)
    {
        var cfg = new HeadroomConfig { Mode = mode, Port = port };
        Assert.Equal(expected, HeadroomComponent.BuildArgv(cfg));
    }

    [Fact]
    public void BuildArgv_AppendsExtraArgs()
    {
        var cfg = new HeadroomConfig { ExtraArgs = { "--verbose" } };
        Assert.EndsWith("\"--verbose\"]", HeadroomComponent.BuildArgv(cfg));
    }

    [Fact]
    public void RenderLauncher_SubstitutesArgv_AndKeepsRedirectBeforeImport()
    {
        var cfg = new HeadroomConfig { Port = 8123 };
        var py = HeadroomComponent.RenderLauncher(cfg);
        Assert.Contains("\"8123\"", py);
        Assert.DoesNotContain("{{ARGV}}", py);
        // the load-bearing ordering: redirect must precede the import
        var redirect = py.IndexOf("sys.stdout = _log", StringComparison.Ordinal);
        var import_ = py.IndexOf("from headroom.cli import main", StringComparison.Ordinal);
        Assert.True(redirect >= 0 && import_ > redirect);
    }

    [Fact]
    public void InstallCommands_UseUvNativePipAgainstVenvPython()
    {
        var runner = new FakeRunner();
        var cfg = StackConfig.CreateDefault(@"C:\ts");
        var hc = new HeadroomComponent(runner, new FakePort(), new FakeHttp());
        var cmds = hc.PlanInstallCommands(cfg, uvPath: "uv");

        Assert.Contains(cmds, c => c == @"uv venv --python 3.12 C:\ts\venv");
        // uv venvs have NO pip inside — uv pip install with --python is the correct form
        Assert.Contains(cmds, c =>
            c == @"uv pip install --python C:\ts\venv\Scripts\python.exe headroom-ai[proxy]==0.24.0");
    }

    [Fact]
    public void WaitReady_PollsReadyz_UntilBody()
    {
        var http = new FakeHttp();
        http.Responses["http://127.0.0.1:8787/readyz"] = "ok";
        var hc = new HeadroomComponent(new FakeRunner(), new FakePort { Listening = true }, http);
        Assert.True(hc.WaitReady(8787, maxWaitMs: 100, pollMs: 10));
    }

    [Fact]
    public void WaitReady_TimesOut_WhenNeverReady()
    {
        var hc = new HeadroomComponent(new FakeRunner(), new FakePort(), new FakeHttp());
        Assert.False(hc.WaitReady(8787, maxWaitMs: 50, pollMs: 10));
    }
}
```

- [ ] **Step 5.3: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~HeadroomTests"`
Expected: FAIL — compile errors (`HeadroomComponent` not defined).

- [ ] **Step 5.4: Implement**

Create `src/TokenStack.Core/Components/Bootstrap.cs`:
```csharp
using TokenStack.Core.Windows;

namespace TokenStack.Core.Components;

/// <summary>Locates or installs uv (the only bootstrap dependency; it then provisions
/// Python for Headroom and the tool install for Semble).</summary>
public sealed class Bootstrap(IProcessRunner runner)
{
    /// <summary>Returns the uv invocation path, installing uv if needed.
    /// Order: PATH → known install dir → winget → standalone installer.</summary>
    public string EnsureUv()
    {
        if (runner.Run("uv", "--version", 15000).Ok) return "uv";

        var local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "uv.exe");
        if (File.Exists(local)) return local;

        var winget = runner.Run("winget",
            "install --id astral-sh.uv -e --accept-source-agreements --accept-package-agreements",
            300000);
        if (!winget.Ok)
        {
            var standalone = runner.Run("powershell",
                "-NoProfile -ExecutionPolicy Bypass -Command \"irm https://astral.sh/uv/install.ps1 | iex\"",
                300000);
            if (!standalone.Ok)
                throw new InvalidOperationException(
                    "Could not install uv via winget or the standalone installer. " +
                    "Install manually from https://docs.astral.sh/uv/ then re-run. " +
                    $"winget: {winget.StdErr} | standalone: {standalone.StdErr}");
        }

        if (runner.Run("uv", "--version", 15000).Ok) return "uv";
        if (File.Exists(local)) return local;
        throw new InvalidOperationException(
            "uv was installed but is not yet resolvable in this process. " +
            "Open a new terminal and re-run `token-stack install`.");
    }
}
```

Create `src/TokenStack.Core/Components/HeadroomComponent.cs`:
```csharp
using System.Reflection;
using System.Text.Json;
using TokenStack.Core.Config;
using TokenStack.Core.Windows;

namespace TokenStack.Core.Components;

public sealed class HeadroomComponent(IProcessRunner runner, IPortProbe port, IHttpProbe http)
{
    /// <summary>argv for `headroom proxy` as a Python list literal for the launcher template.
    /// Mode map: token = default (no flag), cache = --mode cache, passthrough = --no-optimize.
    /// NOTE: verify flag names against `headroom proxy --help` during integration (Task 12);
    /// they come from the reference setup doc.</summary>
    public static string BuildArgv(HeadroomConfig cfg)
    {
        var argv = new List<string> { "headroom", "proxy", "--port", cfg.Port.ToString() };
        if (cfg.Mode == "cache") argv.AddRange(new[] { "--mode", "cache" });
        if (cfg.Mode == "passthrough") argv.Add("--no-optimize");
        argv.AddRange(cfg.ExtraArgs);
        return JsonSerializer.Serialize(argv);
    }

    public static string RenderLauncher(HeadroomConfig cfg)
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("TokenStack.Core.Resources.run_proxy.py.tmpl")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("{{ARGV}}", BuildArgv(cfg));
    }

    /// <summary>The exact install commands, exposed for tests and --verbose tracing.</summary>
    public List<string> PlanInstallCommands(StackConfig cfg, string uvPath)
    {
        var venvPython = Path.Combine(cfg.InstallRoot, "venv", "Scripts", "python.exe");
        return new List<string>
        {
            $"{uvPath} venv --python {cfg.Headroom.PythonVersion} {Path.Combine(cfg.InstallRoot, "venv")}",
            $"{uvPath} pip install --python {venvPython} headroom-ai[proxy]=={cfg.Headroom.Version}",
        };
    }

    public void Install(StackConfig cfg, string uvPath)
    {
        foreach (var cmd in PlanInstallCommands(cfg, uvPath))
        {
            var space = cmd.IndexOf(' ');
            var r = runner.Run(cmd[..space], cmd[(space + 1)..], 600000);
            if (!r.Ok) throw new InvalidOperationException($"Failed: {cmd}\n{r.StdErr}\n{r.StdOut}");
        }
        File.WriteAllText(Path.Combine(cfg.InstallRoot, "run_proxy.py"), RenderLauncher(cfg.Headroom));

        var tasks = new ScheduledTaskManager(runner);
        var xml = TaskXml.Render(
            Path.Combine(cfg.InstallRoot, "venv", "Scripts", "pythonw.exe"),
            Path.Combine(cfg.InstallRoot, "run_proxy.py"),
            cfg.InstallRoot,
            Environment.UserDomainName + "\\" + Environment.UserName);
        tasks.RegisterOrUpdate(xml, Path.Combine(cfg.InstallRoot, "tmp"));
        tasks.Start();
    }

    /// <summary>Cold load is 25–105s; poll /readyz then fall back to a bound port.</summary>
    public bool WaitReady(int proxyPort, int maxWaitMs = 120000, int pollMs = 2000)
    {
        var deadline = Environment.TickCount64 + maxWaitMs;
        while (Environment.TickCount64 < deadline)
        {
            if (http.Get($"http://127.0.0.1:{proxyPort}/readyz", 3000) is not null) return true;
            if (port.IsListening(proxyPort)) return true;
            Thread.Sleep(pollMs);
        }
        return false;
    }

    /// <summary>summary.api_requests from /stats, or null when unreachable (it is slow — ~20s
    /// under load; hook mode uses a 3s budget and simply omits reqs on miss).</summary>
    public long? ReadApiRequests(int proxyPort, int timeoutMs = 3000)
    {
        var body = http.Get($"http://127.0.0.1:{proxyPort}/stats", timeoutMs);
        if (body is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.GetProperty("summary").GetProperty("api_requests").GetInt64();
        }
        catch { return null; }
    }

    public void Unwire(StackConfig cfg)
    {
        var tasks = new ScheduledTaskManager(runner);
        tasks.Stop();
        tasks.Unregister();
    }
}
```

- [ ] **Step 5.5: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~HeadroomTests"`
Expected: PASS (7 tests).

- [ ] **Step 5.6: Commit**

```powershell
git add -A
git commit -m "feat(core): uv bootstrap + headroom component (venv, launcher render, task, readyz/stats)"
```

---

### Task 6: RTK component (download, extract, PATH, hook)

**Files:**
- Create: `src/TokenStack.Core/Components/RtkComponent.cs`
- Test: `src/TokenStack.Tests/RtkTests.cs`

- [ ] **Step 6.1: Write the failing tests**

Create `src/TokenStack.Tests/RtkTests.cs`:
```csharp
using System.IO.Compression;
using TokenStack.Core.Components;
using TokenStack.Core.Config;
using Xunit;

namespace TokenStack.Tests;

public class RtkTests
{
    [Fact]
    public void ReleaseUrl_BuiltFromConfigSourceAndVersion()
    {
        var cfg = new RtkConfig { Version = "0.42.3", Source = "github:rtk-ai/rtk" };
        Assert.Equal(
            "https://github.com/rtk-ai/rtk/releases/download/v0.42.3/rtk-x86_64-pc-windows-msvc.zip",
            RtkComponent.ReleaseUrl(cfg));
    }

    [Fact]
    public void ReleaseUrl_RejectsNonGithubSource()
    {
        var cfg = new RtkConfig { Source = "https://evil.example/rtk" };
        Assert.Throws<ArgumentException>(() => RtkComponent.ReleaseUrl(cfg));
    }

    [Fact]
    public void ExtractRtkExe_FindsExeAnywhereInZip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ts-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var zipPath = Path.Combine(dir, "rtk.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("rtk-x86_64-pc-windows-msvc/rtk.exe");
            using var s = entry.Open();
            s.Write("MZfake"u8);
        }

        var dest = Path.Combine(dir, "out");
        RtkComponent.ExtractRtkExe(zipPath, dest);
        Assert.True(File.Exists(Path.Combine(dest, "rtk.exe")));
    }

    [Fact]
    public void ExtractRtkExe_Throws_WhenNoExeInZip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ts-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var zipPath = Path.Combine(dir, "bad.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            zip.CreateEntry("readme.txt");
        Assert.Throws<InvalidDataException>(() =>
            RtkComponent.ExtractRtkExe(zipPath, Path.Combine(dir, "out")));
    }
}
```

- [ ] **Step 6.2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~RtkTests"`
Expected: FAIL — compile errors (`RtkComponent` not defined).

- [ ] **Step 6.3: Implement**

Create `src/TokenStack.Core/Components/RtkComponent.cs`:
```csharp
using System.IO.Compression;
using System.Net.Http;
using TokenStack.Core.Config;
using TokenStack.Core.Windows;

namespace TokenStack.Core.Components;

public sealed class RtkComponent(IProcessRunner runner, IEnvStore env)
{
    public static string ReleaseUrl(RtkConfig cfg)
    {
        const string prefix = "github:";
        if (!cfg.Source.StartsWith(prefix, StringComparison.Ordinal))
            throw new ArgumentException(
                $"rtk.source must be 'github:<owner>/<repo>' (got '{cfg.Source}')");
        var repo = cfg.Source[prefix.Length..];
        return $"https://github.com/{repo}/releases/download/v{cfg.Version}/rtk-x86_64-pc-windows-msvc.zip";
    }

    public static void ExtractRtkExe(string zipPath, string destDir)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.Entries.FirstOrDefault(e =>
                e.Name.Equals("rtk.exe", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"rtk.exe not found inside {zipPath}");
        Directory.CreateDirectory(destDir);
        entry.ExtractToFile(Path.Combine(destDir, "rtk.exe"), overwrite: true);
    }

    public string RtkDir(StackConfig cfg) => Path.Combine(cfg.InstallRoot, "rtk");
    public string RtkExe(StackConfig cfg) => Path.Combine(RtkDir(cfg), "rtk.exe");

    public void Install(StackConfig cfg)
    {
        var url = ReleaseUrl(cfg.Rtk);
        var tmpZip = Path.Combine(cfg.InstallRoot, "tmp", "rtk.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(tmpZip)!);

        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
        using (var stream = http.GetStreamAsync(url).GetAwaiter().GetResult())
        using (var file = File.Create(tmpZip))
            stream.CopyTo(file);

        ExtractRtkExe(tmpZip, RtkDir(cfg));
        File.Delete(tmpZip);
        UserPath.EnsureContains(env, RtkDir(cfg));

        var verify = runner.Run(RtkExe(cfg), "--version", 15000);
        if (!verify.Ok)
            throw new InvalidOperationException($"rtk.exe extracted but failed to run: {verify.StdErr}");
    }

    /// <summary>Hook wiring is done by the pipeline via ClaudeSurgeon.EnsureRtkHook —
    /// kept out of this class so all settings.json writes share one editor/backup path.</summary>
    public bool IsOnPath() => runner.Run("rtk", "--version", 10000).Ok;

    public void Unwire(StackConfig cfg) => UserPath.Remove(env, RtkDir(cfg));
}
```

- [ ] **Step 6.4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~RtkTests"`
Expected: PASS (4 tests).

- [ ] **Step 6.5: Commit**

```powershell
git add -A
git commit -m "feat(core): rtk component (pinned GitHub download, zip extract, PATH, verify)"
```

---

### Task 7: Semble component (uv tool install + exe resolution)

**Files:**
- Create: `src/TokenStack.Core/Components/SembleComponent.cs`
- Test: `src/TokenStack.Tests/SembleTests.cs`

- [ ] **Step 7.1: Write the failing tests**

Create `src/TokenStack.Tests/SembleTests.cs`:
```csharp
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
```

- [ ] **Step 7.2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~SembleTests"`
Expected: FAIL — compile errors (`SembleComponent` not defined).

- [ ] **Step 7.3: Implement**

Create `src/TokenStack.Core/Components/SembleComponent.cs`:
```csharp
using TokenStack.Core.Config;
using TokenStack.Core.Windows;

namespace TokenStack.Core.Components;

public sealed class SembleComponent(IProcessRunner runner)
{
    /// <summary>The [mcp] extra is mandatory — without it the exe cannot serve MCP.</summary>
    public static string InstallSpec(SembleConfig cfg) =>
        cfg.Version == "latest" ? "semble[mcp]" : $"semble[mcp]=={cfg.Version}";

    /// <summary>uv tool exes land in %USERPROFILE%\.local\bin — often NOT on PATH, which is
    /// exactly why the MCP registration must use this absolute path (spec §5.5).</summary>
    public static string ExePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".local", "bin", "semble.exe");

    public void Install(SembleConfig cfg, string uvPath, bool verifyExe = true)
    {
        var r = runner.Run(uvPath, $"tool install --force {InstallSpec(cfg)}", 600000);
        if (!r.Ok)
            throw new InvalidOperationException($"uv tool install semble failed: {r.StdErr}{r.StdOut}");
        if (verifyExe && !File.Exists(ExePath()))
            throw new InvalidOperationException(
                $"semble installed but exe not found at {ExePath()} — " +
                "check `uv tool list` and `uv tool dir` for a non-default location.");
    }

    /// <summary>Smoke test against a throwaway folder (first run builds an index; tiny repo
    /// keeps it fast). UTF-8 forced — some semble commands crash on cp1252 consoles.</summary>
    public bool SmokeTest()
    {
        var dir = Path.Combine(Path.GetTempPath(), "token-stack-semble-smoke");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "hello.py"), "def authenticate(user):\n    return True\n");
        var r = runner.Run("cmd",
            $"/c set PYTHONIOENCODING=utf-8&& \"{ExePath()}\" search \"authentication\" \"{dir}\"",
            120000);
        return r.Ok;
    }

    public void Unwire(string uvPath) => runner.Run(uvPath, "tool uninstall semble", 120000);
}
```

- [ ] **Step 7.4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~SembleTests"`
Expected: PASS (4 tests).

- [ ] **Step 7.5: Commit**

```powershell
git add -A
git commit -m "feat(core): semble component (uv tool install, absolute exe path, smoke test)"
```

---

### Task 8: Routing manager (the BYPASSED killer)

**Files:**
- Create: `src/TokenStack.Core/Components/RoutingManager.cs`
- Test: `src/TokenStack.Tests/RoutingTests.cs`

- [ ] **Step 8.1: Write the failing tests**

Create `src/TokenStack.Tests/RoutingTests.cs`:
```csharp
using TokenStack.Core.Components;
using Xunit;

namespace TokenStack.Tests;

public class RoutingTests
{
    [Fact]
    public void Apply_SetsUserEnvVar_WhenDesktopRouting()
    {
        var env = new FakeEnv();
        var rm = new RoutingManager(env);
        rm.ApplyDesktop(8787);
        Assert.Equal("http://127.0.0.1:8787", env.User["ANTHROPIC_BASE_URL"]);
    }

    [Fact]
    public void RemoveDesktop_DeletesUserEnvVar()
    {
        var env = new FakeEnv();
        env.User["ANTHROPIC_BASE_URL"] = "http://127.0.0.1:8787";
        new RoutingManager(env).RemoveDesktop();
        Assert.False(env.User.ContainsKey("ANTHROPIC_BASE_URL"));
    }

    [Theory]
    [InlineData("http://127.0.0.1:8787", 8787, true)]
    [InlineData("http://localhost:8787", 8787, true)]
    [InlineData("http://127.0.0.1:8787/", 8787, true)]
    [InlineData("https://api.anthropic.com", 8787, false)]  // today's BYPASSED case
    [InlineData(null, 8787, false)]
    [InlineData("http://127.0.0.1:9000", 8787, false)]      // wrong port = not routed
    public void IsSessionRouted_JudgesProcessEnv(string? processVal, int port, bool expected)
    {
        var env = new FakeEnv();
        if (processVal is not null) env.Process["ANTHROPIC_BASE_URL"] = processVal;
        Assert.Equal(expected, new RoutingManager(env).IsSessionRouted(port));
    }

    [Fact]
    public void Diagnose_FlagsConflict_BetweenProcessAndUserScope()
    {
        var env = new FakeEnv();
        env.User["ANTHROPIC_BASE_URL"] = "http://127.0.0.1:8787";
        env.Process["ANTHROPIC_BASE_URL"] = "https://api.anthropic.com";
        var d = new RoutingManager(env).Diagnose(8787);
        Assert.False(d.SessionRouted);
        Assert.True(d.UserScopeCorrect);
        Assert.True(d.Conflict);   // → "fully quit Claude from the tray and relaunch"
    }

    [Fact]
    public void Diagnose_FlagsModelPinLeftover()
    {
        var env = new FakeEnv();
        env.User["ANTHROPIC_MODEL"] = "claude-opus-4-6";
        Assert.True(new RoutingManager(env).Diagnose(8787).ModelPinPresent);
    }
}
```

- [ ] **Step 8.2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~RoutingTests"`
Expected: FAIL — compile errors (`RoutingManager` not defined).

- [ ] **Step 8.3: Implement**

Create `src/TokenStack.Core/Components/RoutingManager.cs`:
```csharp
using System.Text.RegularExpressions;
using TokenStack.Core.Windows;

namespace TokenStack.Core.Components;

public sealed record RoutingDiagnosis(
    bool SessionRouted, bool UserScopeCorrect, bool Conflict, bool ModelPinPresent);

/// <summary>Owns ANTHROPIC_BASE_URL routing. CLI routing (settings.json env) is applied
/// by the pipeline via ClaudeSurgeon.SetEnvBaseUrl; this class owns the User-scope
/// variable (Desktop ignores settings.json env) and the conflict diagnosis.</summary>
public sealed class RoutingManager(IEnvStore env)
{
    public const string BaseUrlVar = "ANTHROPIC_BASE_URL";

    public static string ProxyUrl(int port) => $"http://127.0.0.1:{port}";

    public void ApplyDesktop(int port) => env.SetUser(BaseUrlVar, ProxyUrl(port));
    public void RemoveDesktop() => env.SetUser(BaseUrlVar, null);

    /// <summary>True when THIS process inherited a base URL pointing at the local proxy —
    /// the only honest routing signal (config presence proves nothing).</summary>
    public bool IsSessionRouted(int port) =>
        env.GetProcess(BaseUrlVar) is { } v
        && Regex.IsMatch(v, $@"^https?://(127\.0\.0\.1|localhost):{port}/?$");

    public RoutingDiagnosis Diagnose(int port)
    {
        var sessionRouted = IsSessionRouted(port);
        var userVal = env.GetUser(BaseUrlVar);
        var userCorrect = userVal == ProxyUrl(port);
        return new RoutingDiagnosis(
            SessionRouted: sessionRouted,
            UserScopeCorrect: userCorrect,
            Conflict: userCorrect && !sessionRouted,
            ModelPinPresent: env.GetUser("ANTHROPIC_MODEL") is not null);
    }
}
```

- [ ] **Step 8.4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~RoutingTests"`
Expected: PASS (9 tests). Full suite: `dotnet test` → all green.

- [ ] **Step 8.5: Commit**

```powershell
git add -A
git commit -m "feat(core): routing manager (desktop env var, honest session-routed check, conflict diagnosis)"
```

---

### Task 9: Doctor check registry (10 checks from spec §6)

**Files:**
- Create: `src/TokenStack.Core/Doctor/DoctorChecks.cs`
- Test: `src/TokenStack.Tests/DoctorTests.cs`

- [ ] **Step 9.1: Write the failing tests**

Create `src/TokenStack.Tests/DoctorTests.cs`:
```csharp
using System.Text.Json.Nodes;
using TokenStack.Core.Config;
using TokenStack.Core.Doctor;
using Xunit;

namespace TokenStack.Tests;

public class DoctorTests
{
    private static DoctorContext Ctx(
        Action<FakeEnv>? env = null, bool portListening = true,
        string settings = "{}", string claudeJson = "{}",
        StackConfig? cfg = null, FakeRunner? runner = null)
    {
        var e = new FakeEnv();
        env?.Invoke(e);
        return new DoctorContext(
            cfg ?? StackConfig.CreateDefault(@"C:\ts"),
            JsonNode.Parse(settings)!, JsonNode.Parse(claudeJson)!,
            e, new FakePort { Listening = portListening },
            runner ?? new FakeRunner(), new FakeHttp());
    }

    [Fact]
    public void RoutingBypassed_Detected_AndFixed()
    {
        var ctx = Ctx(e =>
        {
            e.User["ANTHROPIC_BASE_URL"] = "https://api.anthropic.com"; // wrong scope value
            e.Process["ANTHROPIC_BASE_URL"] = "https://api.anthropic.com";
        });
        var check = new RoutingBypassedCheck();
        Assert.False(check.Detect(ctx).Ok);
        Assert.True(check.Fix(ctx));
        Assert.Equal("http://127.0.0.1:8787",
            ((FakeEnv)ctx.Env).User["ANTHROPIC_BASE_URL"]);
    }

    [Fact]
    public void ProxyZombie_Detected_WhenTaskRunningPortDead()
    {
        var runner = new FakeRunner
        {
            Handler = (f, a) => a.Contains("/query") && a.Contains("/fo csv")
                ? new(0, "\"HeadroomProxy\",\"N/A\",\"Running\"", "")
                : new(0, "", ""),
        };
        var ctx = Ctx(portListening: false, runner: runner);
        Assert.False(new ProxyZombieCheck().Detect(ctx).Ok);
    }

    [Fact]
    public void SembleUvx_Detected_WhenCommandIsUvx()
    {
        var ctx = Ctx(claudeJson:
            """{ "mcpServers": { "semble": { "command": "uvx", "args": ["--from","semble[mcp]","semble"] } } }""");
        Assert.False(new SembleUvxCheck().Detect(ctx).Ok);
    }

    [Fact]
    public void RtkHookMissing_Detected_OnEmptySettings()
    {
        Assert.False(new RtkHookMissingCheck().Detect(Ctx()).Ok);
    }

    [Fact]
    public void RtkHookPowershell_Detected()
    {
        var ctx = Ctx(settings:
            """{ "hooks": { "PreToolUse": [ { "matcher": "PowerShell", "hooks": [ { "type":"command","command":"\"C:\\ts\\rtk\\rtk.exe\" hook claude" } ] } ] } }""");
        Assert.False(new RtkHookPowershellCheck().Detect(ctx).Ok);
    }

    [Fact]
    public void ModelPin_DetectedAndFixed()
    {
        var ctx = Ctx(e => e.User["ANTHROPIC_MODEL"] = "claude-opus-4-6");
        var check = new ModelPinLeftoverCheck();
        Assert.False(check.Detect(ctx).Ok);
        Assert.True(check.Fix(ctx));
        Assert.False(((FakeEnv)ctx.Env).User.ContainsKey("ANTHROPIC_MODEL"));
    }

    [Fact]
    public void PathSpaces_Detected()
    {
        var ctx = Ctx(cfg: StackConfig.CreateDefault(@"C:\proxy tokens"));
        var r = new PathSpacesCheck().Detect(ctx);
        Assert.False(r.Ok);
        Assert.False(r.CanFix); // guided reinstall, not auto-fix
    }

    [Fact]
    public void DisabledDrift_Detected_WhenRtkDisabledButHookPresent()
    {
        var cfg = StackConfig.CreateDefault(@"C:\ts");
        cfg.Rtk.Enabled = false;
        var ctx = Ctx(cfg: cfg, settings:
            """{ "hooks": { "PreToolUse": [ { "matcher": "Bash", "hooks": [ { "type":"command","command":"\"C:\\ts\\rtk\\rtk.exe\" hook claude" } ] } ] } }""");
        Assert.False(new DisabledDriftCheck().Detect(ctx).Ok);
    }

    [Fact]
    public void Registry_ContainsAllTenChecks()
    {
        Assert.Equal(10, DoctorRegistry.All.Count);
        Assert.Equal(new[]
        {
            "routing-bypassed", "proxy-zombie", "proxy-extra-missing", "semble-uvx",
            "rtk-hook-missing", "rtk-hook-powershell", "model-pin-leftover",
            "path-spaces", "task-misconfigured", "stack-disabled-drift",
        }, DoctorRegistry.All.Select(c => c.Id).ToArray());
    }
}
```

- [ ] **Step 9.2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~DoctorTests"`
Expected: FAIL — compile errors (`DoctorContext` not defined).

- [ ] **Step 9.3: Implement**

Create `src/TokenStack.Core/Doctor/DoctorChecks.cs` (all checks in one file — they are
each tiny and share the context; split later only if it grows):
```csharp
using System.Text.Json.Nodes;
using TokenStack.Core.Claude;
using TokenStack.Core.Components;
using TokenStack.Core.Config;
using TokenStack.Core.Windows;

namespace TokenStack.Core.Doctor;

public sealed record DoctorContext(
    StackConfig Config,
    JsonNode Settings,      // loaded %USERPROFILE%\.claude\settings.json
    JsonNode ClaudeJson,    // loaded %USERPROFILE%\.claude.json
    IEnvStore Env,
    IPortProbe Port,
    IProcessRunner Runner,
    IHttpProbe Http)
{
    public bool SettingsChanged { get; set; }
    public bool ClaudeJsonChanged { get; set; }
}

public sealed record CheckResult(string Id, bool Ok, string Detail, bool CanFix);

public interface IDoctorCheck
{
    string Id { get; }
    CheckResult Detect(DoctorContext ctx);
    /// <summary>Apply the remediation. Returns true when something was changed.</summary>
    bool Fix(DoctorContext ctx);
}

public static class DoctorRegistry
{
    public static IReadOnlyList<IDoctorCheck> All { get; } = new IDoctorCheck[]
    {
        new RoutingBypassedCheck(), new ProxyZombieCheck(), new ProxyExtraMissingCheck(),
        new SembleUvxCheck(), new RtkHookMissingCheck(), new RtkHookPowershellCheck(),
        new ModelPinLeftoverCheck(), new PathSpacesCheck(), new TaskMisconfiguredCheck(),
        new DisabledDriftCheck(),
    };
}

public sealed class RoutingBypassedCheck : IDoctorCheck
{
    public string Id => "routing-bypassed";
    public CheckResult Detect(DoctorContext ctx)
    {
        if (!ctx.Config.Routing.Desktop && !ctx.Config.Routing.Cli)
            return new(Id, true, "routing disabled in config", false);
        var d = new RoutingManager(ctx.Env).Diagnose(ctx.Config.Headroom.Port);
        if (d.SessionRouted) return new(Id, true, "session is ROUTED", false);
        var why = d.Conflict
            ? "User-scope var is correct but THIS session inherited a different value — fully quit Claude (tray) and relaunch"
            : "ANTHROPIC_BASE_URL does not point at the proxy";
        return new(Id, false, why, true);
    }
    public bool Fix(DoctorContext ctx)
    {
        var rm = new RoutingManager(ctx.Env);
        if (ctx.Config.Routing.Desktop) rm.ApplyDesktop(ctx.Config.Headroom.Port);
        if (ctx.Config.Routing.Cli)
            ctx.SettingsChanged |= ClaudeSurgeon.SetEnvBaseUrl(
                ctx.Settings, RoutingManager.ProxyUrl(ctx.Config.Headroom.Port));
        return true;
    }
}

public sealed class ProxyZombieCheck : IDoctorCheck
{
    public string Id => "proxy-zombie";
    public CheckResult Detect(DoctorContext ctx)
    {
        if (!ctx.Config.Headroom.Enabled) return new(Id, true, "headroom disabled", false);
        var tasks = new ScheduledTaskManager(ctx.Runner);
        var zombie = tasks.IsRunning() && !ctx.Port.IsListening(ctx.Config.Headroom.Port);
        return zombie
            ? new(Id, false, "task Running but port dead (zombie listener)", true)
            : new(Id, true, "proxy lifecycle healthy", false);
    }
    public bool Fix(DoctorContext ctx)
    {
        new ScheduledTaskManager(ctx.Runner).RestartWithZombieKill();
        return true;
    }
}

public sealed class ProxyExtraMissingCheck : IDoctorCheck
{
    public string Id => "proxy-extra-missing";
    public CheckResult Detect(DoctorContext ctx)
    {
        if (!ctx.Config.Headroom.Enabled) return new(Id, true, "headroom disabled", false);
        var sitePackages = Path.Combine(ctx.Config.InstallRoot, "venv", "Lib", "site-packages");
        if (!Directory.Exists(sitePackages)) return new(Id, true, "venv not installed yet", false);
        var ok = Directory.Exists(Path.Combine(sitePackages, "fastapi"));
        return ok
            ? new(Id, true, "[proxy] extra present (fastapi found)", false)
            : new(Id, false, "venv lacks fastapi — installed without the [proxy] extra", true);
    }
    public bool Fix(DoctorContext ctx)
    {
        var uv = new Bootstrap(ctx.Runner).EnsureUv();
        var py = Path.Combine(ctx.Config.InstallRoot, "venv", "Scripts", "python.exe");
        return ctx.Runner.Run(uv,
            $"pip install --python {py} headroom-ai[proxy]=={ctx.Config.Headroom.Version}", 600000).Ok;
    }
}

public sealed class SembleUvxCheck : IDoctorCheck
{
    public string Id => "semble-uvx";
    public CheckResult Detect(DoctorContext ctx)
    {
        if (!ctx.Config.Semble.Enabled) return new(Id, true, "semble disabled", false);
        var cmd = ctx.ClaudeJson["mcpServers"]?["semble"]?["command"]?.GetValue<string>();
        if (cmd is null) return new(Id, false, "semble MCP not registered", true);
        if (cmd.Contains("uvx", StringComparison.OrdinalIgnoreCase) || !Path.IsPathRooted(cmd))
            return new(Id, false, "semble MCP uses uvx/relative command — handshake will silently fail", true);
        if (!File.Exists(cmd)) return new(Id, false, $"semble exe missing at {cmd}", true);
        return new(Id, true, "semble MCP wired to installed exe", false);
    }
    public bool Fix(DoctorContext ctx)
    {
        ctx.ClaudeJsonChanged |= ClaudeSurgeon.EnsureSembleMcp(ctx.ClaudeJson, SembleComponent.ExePath());
        return true;
    }
}

public sealed class RtkHookMissingCheck : IDoctorCheck
{
    public string Id => "rtk-hook-missing";
    public CheckResult Detect(DoctorContext ctx)
    {
        if (!ctx.Config.Rtk.Enabled) return new(Id, true, "rtk disabled", false);
        var pre = ctx.Settings["hooks"]?["PreToolUse"]?.AsArray();
        var entry = pre?.FirstOrDefault(e =>
            e?["hooks"]?[0]?["command"]?.GetValue<string>()
                ?.Contains("rtk.exe", StringComparison.OrdinalIgnoreCase) == true);
        if (entry is null) return new(Id, false, "PreToolUse rtk hook absent", true);
        var exe = Path.Combine(ctx.Config.InstallRoot, "rtk", "rtk.exe");
        var cmd = entry["hooks"]![0]!["command"]!.GetValue<string>();
        return cmd.Contains(exe, StringComparison.OrdinalIgnoreCase)
            ? new(Id, true, "rtk hook wired", false)
            : new(Id, false, $"rtk hook points at a stale path: {cmd}", true);
    }
    public bool Fix(DoctorContext ctx)
    {
        ctx.SettingsChanged |= ClaudeSurgeon.EnsureRtkHook(ctx.Settings,
            Path.Combine(ctx.Config.InstallRoot, "rtk", "rtk.exe"), ctx.Config.Rtk.HookMatcher);
        return true;
    }
}

public sealed class RtkHookPowershellCheck : IDoctorCheck
{
    public string Id => "rtk-hook-powershell";
    public CheckResult Detect(DoctorContext ctx)
    {
        var pre = ctx.Settings["hooks"]?["PreToolUse"]?.AsArray();
        var bad = pre?.Any(e =>
            e?["matcher"]?.GetValue<string>()?.Contains("PowerShell", StringComparison.OrdinalIgnoreCase) == true
            && e["hooks"]?[0]?["command"]?.GetValue<string>()
                ?.Contains("rtk", StringComparison.OrdinalIgnoreCase) == true) == true;
        return bad
            ? new(Id, false, "rtk hook has a PowerShell matcher — rtk cannot wrap PS aliases", true)
            : new(Id, true, "no PowerShell rtk matcher", false);
    }
    public bool Fix(DoctorContext ctx)
    {
        var pre = ctx.Settings["hooks"]?["PreToolUse"]?.AsArray();
        if (pre is null) return false;
        var bad = pre.Where(e =>
            e?["matcher"]?.GetValue<string>()?.Contains("PowerShell", StringComparison.OrdinalIgnoreCase) == true
            && e["hooks"]?[0]?["command"]?.GetValue<string>()
                ?.Contains("rtk", StringComparison.OrdinalIgnoreCase) == true).ToList();
        foreach (var b in bad) pre.Remove(b);
        ctx.SettingsChanged |= bad.Count > 0;
        return bad.Count > 0;
    }
}

public sealed class ModelPinLeftoverCheck : IDoctorCheck
{
    public string Id => "model-pin-leftover";
    public CheckResult Detect(DoctorContext ctx) =>
        ctx.Env.GetUser("ANTHROPIC_MODEL") is { } v
            ? new(Id, false, $"stray User-scope ANTHROPIC_MODEL={v} pins every session's model", true)
            : new(Id, true, "no model pin", false);
    public bool Fix(DoctorContext ctx) { ctx.Env.SetUser("ANTHROPIC_MODEL", null); return true; }
}

public sealed class PathSpacesCheck : IDoctorCheck
{
    public string Id => "path-spaces";
    public CheckResult Detect(DoctorContext ctx) =>
        ctx.Config.InstallRoot.Contains(' ')
            ? new(Id, false, "installRoot contains spaces — reinstall to a space-free root " +
                  "(token-stack uninstall, edit config, token-stack install)", false)
            : new(Id, true, "installRoot is space-free", false);
    public bool Fix(DoctorContext ctx) => false; // guided, never automatic
}

public sealed class TaskMisconfiguredCheck : IDoctorCheck
{
    public string Id => "task-misconfigured";
    public CheckResult Detect(DoctorContext ctx)
    {
        if (!ctx.Config.Headroom.Enabled) return new(Id, true, "headroom disabled", false);
        var r = ctx.Runner.Run("schtasks", $"/query /tn {ScheduledTaskManager.TaskName} /xml", 15000);
        if (!r.Ok) return new(Id, false, "HeadroomProxy task not registered", true);
        var expectedCmd = Path.Combine(ctx.Config.InstallRoot, "venv", "Scripts", "pythonw.exe");
        return r.StdOut.Contains(expectedCmd, StringComparison.OrdinalIgnoreCase)
            ? new(Id, true, "task action matches managed layout", false)
            : new(Id, false, "task action drifted from managed layout", true);
    }
    public bool Fix(DoctorContext ctx)
    {
        var xml = TaskXml.Render(
            Path.Combine(ctx.Config.InstallRoot, "venv", "Scripts", "pythonw.exe"),
            Path.Combine(ctx.Config.InstallRoot, "run_proxy.py"),
            ctx.Config.InstallRoot,
            Environment.UserDomainName + "\\" + Environment.UserName);
        new ScheduledTaskManager(ctx.Runner)
            .RegisterOrUpdate(xml, Path.Combine(ctx.Config.InstallRoot, "tmp"));
        return true;
    }
}

public sealed class DisabledDriftCheck : IDoctorCheck
{
    public string Id => "stack-disabled-drift";
    public CheckResult Detect(DoctorContext ctx)
    {
        var drift = new List<string>();
        var rtkWired = ctx.Settings["hooks"]?["PreToolUse"]?.AsArray()?.Any(e =>
            e?["hooks"]?[0]?["command"]?.GetValue<string>()
                ?.Contains("rtk.exe", StringComparison.OrdinalIgnoreCase) == true) == true;
        if (!ctx.Config.Rtk.Enabled && rtkWired) drift.Add("rtk disabled but hook wired");
        var sembleWired = ctx.ClaudeJson["mcpServers"]?["semble"] is not null;
        if (!ctx.Config.Semble.Enabled && sembleWired) drift.Add("semble disabled but MCP wired");
        return drift.Count == 0
            ? new(Id, true, "wiring matches config", false)
            : new(Id, false, string.Join("; ", drift), true);
    }
    public bool Fix(DoctorContext ctx)
    {
        var changed = false;
        if (!ctx.Config.Rtk.Enabled)
        { changed |= ClaudeSurgeon.RemoveRtkHook(ctx.Settings); ctx.SettingsChanged |= changed; }
        if (!ctx.Config.Semble.Enabled)
        {
            var c2 = ClaudeSurgeon.RemoveSembleMcp(ctx.ClaudeJson);
            ctx.ClaudeJsonChanged |= c2; changed |= c2;
        }
        return changed;
    }
}
```

- [ ] **Step 9.4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~DoctorTests"`
Expected: PASS (9 tests). Full suite: `dotnet test` → all green.

- [ ] **Step 9.5: Commit**

```powershell
git add -A
git commit -m "feat(core): doctor registry — all 10 spec checks with safe auto-fixes"
```

---

### Task 10: Status probe, install pipeline, and the CLI surface

**Files:**
- Create: `src/TokenStack.Core/Status/StatusProbe.cs`, `src/TokenStack.Core/Install/InstallPipeline.cs`
- Create: `src/TokenStack.Cli/Commands/InstallCommand.cs`, `StatusCommand.cs`, `ServiceCommands.cs`, `ConfigCommand.cs`, `DoctorCommand.cs`, `UpdateCommand.cs`, `GainCommand.cs`, `UninstallCommand.cs`
- Modify: `src/TokenStack.Cli/Program.cs`
- Test: `src/TokenStack.Tests/PipelineTests.cs`

- [ ] **Step 10.1: Write the failing tests**

Create `src/TokenStack.Tests/PipelineTests.cs`:
```csharp
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
```

- [ ] **Step 10.2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~PipelineTests"`
Expected: FAIL — compile errors (`InstallPipeline` not defined).

- [ ] **Step 10.3: Implement Core pieces**

Create `src/TokenStack.Core/Status/StatusProbe.cs`:
```csharp
using System.Text.Json.Nodes;
using TokenStack.Core.Components;
using TokenStack.Core.Config;
using TokenStack.Core.Windows;

namespace TokenStack.Core.Status;

/// <summary>Gathers a live StackStatus. statsTimeoutMs: hook mode uses 3000 (never slow a
/// session start); interactive status uses 25000 (/stats can take ~20s under load).</summary>
public sealed class StatusProbe(
    IProcessRunner runner, IEnvStore env, IPortProbe port, IHttpProbe http)
{
    public StackStatus Gather(StackConfig cfg, string claudeJsonPath, int statsTimeoutMs = 3000)
    {
        var tasks = new ScheduledTaskManager(runner);
        var headroom = new HeadroomComponent(runner, port, http);
        var routing = new RoutingManager(env);

        var taskRunning = cfg.Headroom.Enabled && tasks.IsRunning();
        var listening = cfg.Headroom.Enabled && port.IsListening(cfg.Headroom.Port);
        var reqs = listening ? headroom.ReadApiRequests(cfg.Headroom.Port, statsTimeoutMs) : null;

        var sembleWired = false;
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(claudeJsonPath));
            var cmd = root?["mcpServers"]?["semble"]?["command"]?.GetValue<string>();
            sembleWired = cmd is not null && File.Exists(cmd);
        }
        catch { /* missing/corrupt file = not wired */ }

        return new StackStatus(
            TaskRunning: taskRunning,
            PortListening: listening,
            Routed: routing.IsSessionRouted(cfg.Headroom.Port),
            Reqs: reqs,
            Port: cfg.Headroom.Port,
            RtkOnPath: runner.Run("rtk", "--version", 10000).Ok,
            SembleWired: sembleWired);
    }

    /// <summary>Hook-mode side effects: start the task if down + zombie-recover, then report.</summary>
    public StackStatus GatherForHook(StackConfig cfg, string claudeJsonPath)
    {
        try
        {
            var tasks = new ScheduledTaskManager(runner);
            if (cfg.Headroom.Enabled && tasks.Exists() && !tasks.IsRunning()) tasks.Start();
            if (cfg.Headroom.Enabled && tasks.IsRunning() && !port.IsListening(cfg.Headroom.Port))
            {
                // zombie window: only after the cold-load grace (3 min from task start is not
                // queryable cheaply via schtasks; approximate with a marker file age)
                var marker = Path.Combine(cfg.InstallRoot, "tmp", "first-seen-dead.marker");
                Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
                if (!File.Exists(marker)) File.WriteAllText(marker, DateTimeOffset.UtcNow.ToString("O"));
                else if (DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(marker) > TimeSpan.FromMinutes(3))
                { tasks.RestartWithZombieKill(); File.Delete(marker); }
            }
            else
            {
                var marker = Path.Combine(cfg.InstallRoot, "tmp", "first-seen-dead.marker");
                if (File.Exists(marker)) File.Delete(marker);
            }
        }
        catch { /* the hook must never throw */ }
        return Gather(cfg, claudeJsonPath, statsTimeoutMs: 3000);
    }
}
```

Create `src/TokenStack.Core/Install/InstallPipeline.cs`:
```csharp
using TokenStack.Core.Claude;
using TokenStack.Core.Components;
using TokenStack.Core.Config;
using TokenStack.Core.Windows;

namespace TokenStack.Core.Install;

public sealed record InstallStep(string Name, Action Run);

public sealed class InstallPipeline(
    IProcessRunner runner, IEnvStore env, IPortProbe port, IHttpProbe http,
    Action<string> log)
{
    public string SettingsPath { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");
    public string ClaudeJsonPath { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");

    public static void Preflight(StackConfig cfg)
    {
        var errors = ConfigValidator.Validate(cfg);
        if (errors.Count > 0)
            throw new InvalidOperationException("Config invalid: " + string.Join("; ", errors)
                + (cfg.InstallRoot.Contains(' ') ? " — choose a root without spaces." : ""));
        Directory.CreateDirectory(cfg.InstallRoot);
    }

    /// <summary>Pure step plan (testable). Run() bodies close over the instance services.</summary>
    public static List<InstallStep> PlanSteps(StackConfig cfg)
    {
        var steps = new List<InstallStep>
        {
            new("preflight", () => { }),
            new("bootstrap-uv", () => { }),
        };
        if (cfg.Headroom.Enabled) steps.Add(new("headroom", () => { }));
        if (cfg.Rtk.Enabled) steps.Add(new("rtk", () => { }));
        if (cfg.Semble.Enabled) steps.Add(new("semble", () => { }));
        steps.Add(new("routing", () => { }));
        steps.Add(new("hooks", () => { }));
        steps.Add(new("save-config", () => { }));
        return steps;
    }

    /// <summary>persistConfig=false for `install --component X` repairs — the in-memory
    /// enabled-flag narrowing must never be written back to config.json.</summary>
    public void Run(StackConfig cfg, bool persistConfig = true)
    {
        log("[1/8] preflight");
        Preflight(cfg);
        DetectLegacyInstall();

        log("[2/8] bootstrap: uv");
        var uv = new Bootstrap(runner).EnsureUv();

        var headroom = new HeadroomComponent(runner, port, http);
        if (cfg.Headroom.Enabled)
        {
            log($"[3/8] headroom {cfg.Headroom.Version} (venv + [proxy] + task; cold load 25-105s)");
            headroom.Install(cfg, uv);
            if (!headroom.WaitReady(cfg.Headroom.Port))
                throw new InvalidOperationException(
                    $"Headroom did not become ready on :{cfg.Headroom.Port} within 120s. " +
                    $"Log: {Path.Combine(cfg.InstallRoot, "proxy.log")}");
            log($"      ready on :{cfg.Headroom.Port}");
        }

        var rtk = new RtkComponent(runner, env);
        if (cfg.Rtk.Enabled)
        {
            log($"[4/8] rtk {cfg.Rtk.Version} (download + PATH)");
            rtk.Install(cfg);
        }

        var semble = new SembleComponent(runner);
        if (cfg.Semble.Enabled)
        {
            log("[5/8] semble (uv tool install + smoke search)");
            semble.Install(cfg.Semble, uv);
            if (!semble.SmokeTest())
                throw new InvalidOperationException(
                    "semble installed but the smoke search failed — run with --verbose for output.");
        }

        log("[6/8] routing");
        if (cfg.Routing.Desktop)
        {
            new RoutingManager(env).ApplyDesktop(cfg.Headroom.Port);
            log("      User-scope ANTHROPIC_BASE_URL set (Desktop ignores settings.json env)");
        }

        log("[7/8] claude wiring (settings.json + .claude.json, one backup each)");
        ApplyClaudeWiring(cfg);

        log("[8/8] save config");
        if (persistConfig) ConfigStore.Save(cfg, ConfigStore.DefaultPath);
        else log("      (component-scoped run — config.json left untouched)");

        log("DONE. Fully quit Claude Desktop from the system tray and relaunch " +
            "(env inheritance happens at process creation).");
    }

    /// <summary>All Claude-file edits in one editor session per file = one backup per run.
    /// Also the convergence target for doctor's drift fix.</summary>
    public void ApplyClaudeWiring(StackConfig cfg)
    {
        var settingsEditor = new ClaudeFileEditor(SettingsPath);
        var settings = settingsEditor.Load();
        var changed = false;

        if (cfg.Rtk.Enabled)
            changed |= ClaudeSurgeon.EnsureRtkHook(settings,
                Path.Combine(cfg.InstallRoot, "rtk", "rtk.exe"), cfg.Rtk.HookMatcher);
        else
            changed |= ClaudeSurgeon.RemoveRtkHook(settings);

        if (cfg.Hooks.SessionStatusLine)
            changed |= ClaudeSurgeon.EnsureSessionStatusHook(settings,
                Path.Combine(cfg.InstallRoot, "token-stack.exe"));
        else
            changed |= ClaudeSurgeon.RemoveSessionStatusHook(settings);

        if (cfg.Routing.Cli)
            changed |= ClaudeSurgeon.SetEnvBaseUrl(settings, RoutingManager.ProxyUrl(cfg.Headroom.Port));
        else
            changed |= ClaudeSurgeon.RemoveEnvBaseUrl(settings);

        if (changed) settingsEditor.SaveWithBackup(settings);

        var claudeJsonEditor = new ClaudeFileEditor(ClaudeJsonPath);
        var claudeJson = claudeJsonEditor.Load();
        var changed2 = cfg.Semble.Enabled
            ? ClaudeSurgeon.EnsureSembleMcp(claudeJson, SembleComponent.ExePath())
            : ClaudeSurgeon.RemoveSembleMcp(claudeJson);
        if (changed2) claudeJsonEditor.SaveWithBackup(claudeJson);
    }

    /// <summary>Recognize the hand-built reference layout and announce adoption (spec §5.0):
    /// our Ensure* calls rewrite the legacy entries; the old root is left for manual delete.</summary>
    private void DetectLegacyInstall()
    {
        if (!File.Exists(SettingsPath)) return;
        var text = File.ReadAllText(SettingsPath);
        if (text.Contains("ensure-stack.ps1") || text.Contains("proxy tokens"))
            log("      detected a hand-built token stack — adopting (hooks/task/MCP will be " +
                "rewired to the managed layout; the old folder is left for you to delete).");
    }

    public void Uninstall(StackConfig cfg, bool keepConfig)
    {
        log("stopping + unregistering HeadroomProxy task");
        new HeadroomComponent(runner, port, http).Unwire(cfg);

        log("removing routing env vars");
        new RoutingManager(env).RemoveDesktop();

        log("unwiring claude files (hooks, env, MCP)");
        var settingsEditor = new ClaudeFileEditor(SettingsPath);
        var settings = settingsEditor.Load();
        var changed = ClaudeSurgeon.RemoveRtkHook(settings);
        changed |= ClaudeSurgeon.RemoveSessionStatusHook(settings);
        changed |= ClaudeSurgeon.RemoveEnvBaseUrl(settings);
        if (changed) settingsEditor.SaveWithBackup(settings);

        var cjEditor = new ClaudeFileEditor(ClaudeJsonPath);
        var cj = cjEditor.Load();
        if (ClaudeSurgeon.RemoveSembleMcp(cj)) cjEditor.SaveWithBackup(cj);

        log("uninstalling semble tool + removing rtk from PATH");
        try { new SembleComponent(runner).Unwire(new Bootstrap(runner).EnsureUv()); }
        catch { log("  (uv unavailable — skipped `uv tool uninstall semble`)"); }
        new RtkComponent(runner, env).Unwire(cfg);

        if (!keepConfig && File.Exists(ConfigStore.DefaultPath))
            File.Delete(ConfigStore.DefaultPath);
        log($"NOTE: {cfg.InstallRoot} (venv/rtk/launcher) left on disk — delete manually " +
            "after closing any process using it.");
    }
}
```

- [ ] **Step 10.4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~PipelineTests"`
Expected: PASS (3 tests). Full suite: `dotnet test` → all green.

- [ ] **Step 10.5: Implement the CLI**

Replace `src/TokenStack.Cli/Program.cs`:
```csharp
using Spectre.Console.Cli;
using TokenStack.Cli.Commands;

var app = new CommandApp();
app.Configure(c =>
{
    c.SetApplicationName("token-stack");
    c.AddCommand<InstallCommand>("install")
        .WithDescription("Install + wire the full stack (idempotent)");
    c.AddCommand<StatusCommand>("status")
        .WithDescription("Live stack status (--hook = one-line session mode)");
    c.AddCommand<StartCommand>("start").WithDescription("Start the Headroom proxy task");
    c.AddCommand<StopCommand>("stop").WithDescription("Stop the Headroom proxy task");
    c.AddCommand<RestartCommand>("restart").WithDescription("Restart with zombie recovery");
    c.AddCommand<ConfigCommand>("config")
        .WithDescription("config list | get <key> | set <key> <value> | open");
    c.AddCommand<DoctorCommand>("doctor").WithDescription("Diagnose (and --fix) known failure modes");
    c.AddCommand<UpdateCommand>("update").WithDescription("Update a component to a pinned version");
    c.AddCommand<GainCommand>("gain").WithDescription("Unified savings report (rtk + headroom)");
    c.AddCommand<UninstallCommand>("uninstall").WithDescription("Full rollback");
});
return app.Run(args);
```

Create `src/TokenStack.Cli/Commands/Services.cs` (composition root, shared by commands):
```csharp
using TokenStack.Core.Config;
using TokenStack.Core.Windows;

namespace TokenStack.Cli.Commands;

internal static class Services
{
    public static readonly IProcessRunner Runner = new RealProcessRunner();
    public static readonly IEnvStore Env = new RealEnvStore();
    public static readonly IPortProbe Port = new RealPortProbe();
    public static readonly IHttpProbe Http = new RealHttpProbe();

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");
    public static string ClaudeJsonPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");

    public static StackConfig LoadConfigOrDefault()
    {
        var path = ConfigStore.DefaultPath;
        if (File.Exists(path)) return ConfigStore.Load(path);
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "token-stack");
        return StackConfig.CreateDefault(root);
    }
}
```

Create `src/TokenStack.Cli/Commands/InstallCommand.cs`:
```csharp
using Spectre.Console;
using Spectre.Console.Cli;
using TokenStack.Core.Install;

namespace TokenStack.Cli.Commands;

public sealed class InstallCommand : Command<InstallCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--component <NAME>")]
        public string? Component { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var cfg = Services.LoadConfigOrDefault();
        if (settings.Component is { } only)
        {
            // narrow THIS RUN only — persistConfig:false below keeps config.json intact
            cfg.Headroom.Enabled = cfg.Headroom.Enabled && only == "headroom";
            cfg.Rtk.Enabled = cfg.Rtk.Enabled && only == "rtk";
            cfg.Semble.Enabled = cfg.Semble.Enabled && only == "semble";
        }
        var pipeline = new InstallPipeline(
            Services.Runner, Services.Env, Services.Port, Services.Http,
            msg => AnsiConsole.MarkupLineInterpolated($"[grey]{msg}[/]"));
        try
        {
            pipeline.Run(cfg, persistConfig: settings.Component is null);
            AnsiConsole.MarkupLine("[green]Install complete.[/] Run [bold]token-stack status[/] after restarting Claude.");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Install failed:[/] {ex.Message}");
            AnsiConsole.MarkupLine("[yellow]Re-running `token-stack install` resumes from the failed step.[/]");
            return 1;
        }
    }
}
```

Create `src/TokenStack.Cli/Commands/StatusCommand.cs`:
```csharp
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using TokenStack.Core.Status;

namespace TokenStack.Cli.Commands;

public sealed class StatusCommand : Command<StatusCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--hook")] public bool Hook { get; init; }
        [CommandOption("--json")] public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var cfg = Services.LoadConfigOrDefault();
        var probe = new StatusProbe(Services.Runner, Services.Env, Services.Port, Services.Http);

        if (settings.Hook)
        {
            // Session hook: must be fast, must never throw, exactly one plain line.
            var s = probe.GatherForHook(cfg, Services.ClaudeJsonPath);
            Console.WriteLine(StatusLine.Build(s));
            return 0;
        }

        var status = probe.Gather(cfg, Services.ClaudeJsonPath, statsTimeoutMs: 25000);
        if (settings.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(status));
            return 0;
        }

        var t = new Table().AddColumns("Layer", "State", "Detail");
        t.AddRow("Headroom",
            !status.TaskRunning ? "[red]DOWN[/]" : status.PortListening ? "[green]up[/]" : "[yellow]starting[/]",
            $"port {status.Port}, {(status.Routed ? "[green]ROUTED[/]" : "[red]BYPASSED[/]")}" +
            (status.Reqs is { } n ? $", reqs={n}" : ""));
        t.AddRow("RTK", status.RtkOnPath ? "[green]up[/]" : "[red]MISSING[/]", "PreToolUse Bash filter");
        t.AddRow("Semble", status.SembleWired ? "[green]up[/]" : "[red]MISSING[/]", "stdio MCP (absolute exe)");
        AnsiConsole.Write(t);
        Console.WriteLine();
        Console.WriteLine(StatusLine.Build(status));
        return status is { TaskRunning: true, PortListening: true, RtkOnPath: true, SembleWired: true } ? 0 : 1;
    }
}
```

Create `src/TokenStack.Cli/Commands/ServiceCommands.cs`:
```csharp
using Spectre.Console;
using Spectre.Console.Cli;
using TokenStack.Core.Windows;

namespace TokenStack.Cli.Commands;

public sealed class StartCommand : Command
{
    public override int Execute(CommandContext context)
    {
        new ScheduledTaskManager(Services.Runner).Start();
        AnsiConsole.MarkupLine("[green]HeadroomProxy task started[/] (cold load 25-105s before the port binds).");
        return 0;
    }
}

public sealed class StopCommand : Command
{
    public override int Execute(CommandContext context)
    {
        new ScheduledTaskManager(Services.Runner).Stop();
        AnsiConsole.MarkupLine("[yellow]HeadroomProxy task stopped.[/] Routed Claude sessions will fail until start.");
        return 0;
    }
}

public sealed class RestartCommand : Command
{
    public override int Execute(CommandContext context)
    {
        new ScheduledTaskManager(Services.Runner).RestartWithZombieKill();
        AnsiConsole.MarkupLine("[green]Restarted with zombie recovery.[/]");
        return 0;
    }
}
```

Create `src/TokenStack.Cli/Commands/ConfigCommand.cs`:
```csharp
using System.Diagnostics;
using Spectre.Console;
using Spectre.Console.Cli;
using TokenStack.Core.Config;
using TokenStack.Core.Install;

namespace TokenStack.Cli.Commands;

public sealed class ConfigCommand : Command<ConfigCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<action>")] public string Action { get; init; } = "";
        [CommandArgument(1, "[key]")] public string? Key { get; init; }
        [CommandArgument(2, "[value]")] public string? Value { get; init; }
    }

    public override int Execute(CommandContext context, Settings s)
    {
        var path = ConfigStore.DefaultPath;
        if (!File.Exists(path))
        {
            AnsiConsole.MarkupLine("[red]No config yet[/] — run [bold]token-stack install[/] first.");
            return 1;
        }
        switch (s.Action)
        {
            case "list":
                Console.WriteLine(File.ReadAllText(path));
                return 0;
            case "get" when s.Key is not null:
                Console.WriteLine(ConfigStore.GetValue(path, s.Key));
                return 0;
            case "set" when s.Key is not null && s.Value is not null:
                ConfigStore.SetValue(path, s.Key, s.Value);
                // re-apply wiring so the change takes effect (port/hooks/routing/launcher)
                var cfg = ConfigStore.Load(path);
                var pipeline = new InstallPipeline(
                    Services.Runner, Services.Env, Services.Port, Services.Http,
                    m => AnsiConsole.MarkupLineInterpolated($"[grey]{m}[/]"));
                pipeline.ApplyClaudeWiring(cfg);
                if (s.Key.StartsWith("headroom.", StringComparison.Ordinal))
                    AnsiConsole.MarkupLine("[yellow]Headroom setting changed — run `token-stack install --component headroom` to re-render the launcher/task, then `token-stack restart`.[/]");
                AnsiConsole.MarkupLine($"[green]{s.Key} = {s.Value}[/]");
                return 0;
            case "open":
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return 0;
            default:
                AnsiConsole.MarkupLine("usage: token-stack config list | get <key> | set <key> <value> | open");
                return 1;
        }
    }
}
```

Create `src/TokenStack.Cli/Commands/DoctorCommand.cs`:
```csharp
using System.Text.Json.Nodes;
using Spectre.Console;
using Spectre.Console.Cli;
using TokenStack.Core.Claude;
using TokenStack.Core.Doctor;

namespace TokenStack.Cli.Commands;

public sealed class DoctorCommand : Command<DoctorCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--fix")] public bool Fix { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var cfg = Services.LoadConfigOrDefault();
        var settingsEditor = new ClaudeFileEditor(Services.SettingsPath);
        var claudeJsonEditor = new ClaudeFileEditor(Services.ClaudeJsonPath);
        var ctx = new DoctorContext(cfg, settingsEditor.Load(), claudeJsonEditor.Load(),
            Services.Env, Services.Port, Services.Runner, Services.Http);

        var table = new Table().AddColumns("Check", "Result", "Detail");
        var failures = 0;
        foreach (var check in DoctorRegistry.All)
        {
            var r = check.Detect(ctx);
            if (!r.Ok && settings.Fix && check.Fix(ctx))
            {
                var after = check.Detect(ctx);
                table.AddRow(check.Id,
                    after.Ok ? "[green]FIXED[/]" : "[red]STILL FAILING[/]", r.Detail);
                if (!after.Ok) failures++;
            }
            else
            {
                table.AddRow(check.Id,
                    r.Ok ? "[green]ok[/]" : r.CanFix ? "[yellow]FAIL (fixable)[/]" : "[red]FAIL[/]",
                    r.Detail);
                if (!r.Ok) failures++;
            }
        }
        if (ctx.SettingsChanged) settingsEditor.SaveWithBackup(ctx.Settings);
        if (ctx.ClaudeJsonChanged) claudeJsonEditor.SaveWithBackup(ctx.ClaudeJson);

        AnsiConsole.Write(table);
        if (failures > 0 && !settings.Fix)
            AnsiConsole.MarkupLine("[yellow]Run `token-stack doctor --fix` to apply the safe remediations.[/]");
        return failures == 0 ? 0 : 1;
    }
}
```

Create `src/TokenStack.Cli/Commands/UpdateCommand.cs`:
```csharp
using Spectre.Console;
using Spectre.Console.Cli;
using TokenStack.Core.Components;
using TokenStack.Core.Config;

namespace TokenStack.Cli.Commands;

public sealed class UpdateCommand : Command<UpdateCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--component <NAME>")] public string Component { get; init; } = "";
        [CommandOption("--version <VER>")] public string? Version { get; init; }
    }

    public override int Execute(CommandContext context, Settings s)
    {
        var path = ConfigStore.DefaultPath;
        var cfg = ConfigStore.Load(path);
        var uv = new Bootstrap(Services.Runner).EnsureUv();
        switch (s.Component)
        {
            case "headroom":
                if (s.Version is not null) { cfg.Headroom.Version = s.Version; ConfigStore.Save(cfg, path); }
                var py = Path.Combine(cfg.InstallRoot, "venv", "Scripts", "python.exe");
                Run(uv, $"pip install --python {py} headroom-ai[proxy]=={cfg.Headroom.Version}");
                AnsiConsole.MarkupLine("[yellow]Run `token-stack restart` to load the new version.[/]");
                return 0;
            case "rtk":
                if (s.Version is not null) { cfg.Rtk.Version = s.Version; ConfigStore.Save(cfg, path); }
                new RtkComponent(Services.Runner, Services.Env).Install(cfg);
                return 0;
            case "semble":
                if (s.Version is not null) { cfg.Semble.Version = s.Version; ConfigStore.Save(cfg, path); }
                new SembleComponent(Services.Runner).Install(cfg.Semble, uv);
                return 0;
            default:
                AnsiConsole.MarkupLine("usage: token-stack update --component headroom|rtk|semble [--version <v>]");
                return 1;
        }

        void Run(string file, string args)
        {
            var r = Services.Runner.Run(file, args, 600000);
            if (!r.Ok) throw new InvalidOperationException($"{file} {args}\n{r.StdErr}");
            AnsiConsole.MarkupLine($"[green]updated {s.Component}[/]");
        }
    }
}
```

Create `src/TokenStack.Cli/Commands/GainCommand.cs`:
```csharp
using Spectre.Console;
using Spectre.Console.Cli;
using TokenStack.Core.Components;

namespace TokenStack.Cli.Commands;

public sealed class GainCommand : Command
{
    public override int Execute(CommandContext context)
    {
        var cfg = Services.LoadConfigOrDefault();

        AnsiConsole.MarkupLine("[bold]RTK[/] (command-output filter):");
        var rtk = Services.Runner.Run("rtk", "gain", 30000);
        Console.WriteLine(rtk.Ok ? rtk.StdOut : "  rtk unreachable (not on PATH?)");

        AnsiConsole.MarkupLine($"[bold]Headroom[/] (/stats on :{cfg.Headroom.Port} — can take ~20s):");
        var headroom = new HeadroomComponent(Services.Runner, Services.Port, Services.Http);
        var reqs = headroom.ReadApiRequests(cfg.Headroom.Port, timeoutMs: 25000);
        Console.WriteLine(reqs is { } n
            ? $"  api_requests served through the proxy: {n}"
            : "  proxy unreachable (down or cold-loading)");
        Console.WriteLine("  full dashboard: http://127.0.0.1:" + cfg.Headroom.Port + "/stats");
        return 0;
    }
}
```

Create `src/TokenStack.Cli/Commands/UninstallCommand.cs`:
```csharp
using Spectre.Console;
using Spectre.Console.Cli;
using TokenStack.Core.Install;

namespace TokenStack.Cli.Commands;

public sealed class UninstallCommand : Command<UninstallCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--keep-config")] public bool KeepConfig { get; init; }
        [CommandOption("--yes|-y")] public bool Yes { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        if (!settings.Yes &&
            !AnsiConsole.Confirm("Remove the token stack (task, env vars, hooks, MCP entry)?", false))
            return 1;
        var cfg = Services.LoadConfigOrDefault();
        new InstallPipeline(Services.Runner, Services.Env, Services.Port, Services.Http,
                m => AnsiConsole.MarkupLineInterpolated($"[grey]{m}[/]"))
            .Uninstall(cfg, settings.KeepConfig);
        AnsiConsole.MarkupLine("[green]Uninstalled.[/] Claude config backups (*.token-stack-backup-*) were kept.");
        return 0;
    }
}
```

- [ ] **Step 10.6: Build + smoke the CLI**

Run: `dotnet build TokenStack.sln`
Expected: 0 errors.

Run: `dotnet run --project src/TokenStack.Cli -- status`
Expected: a status table reflecting THIS machine (Headroom up since the hand-built stack
runs here; RTK up; Semble up). Exit code 0.

Run: `dotnet run --project src/TokenStack.Cli -- status --hook`
Expected: one line starting `[token-stack] Headroom:` — format identical to ensure-stack.ps1.

- [ ] **Step 10.7: Commit**

```powershell
git add -A
git commit -m "feat(cli): full command surface (install/status/start/stop/restart/config/doctor/update/gain/uninstall)"
```

---

### Task 11: publish.ps1 + README (the distributable)

**Files:**
- Create: `publish.ps1`, `README.md`
- Modify: `.gitignore` already excludes `dist/`

- [ ] **Step 11.1: Create publish.ps1**

```powershell
# publish.ps1 — builds the distributable zip: dist/token-stack-v<version>.zip
param([string]$Version = "1.0.0")
$ErrorActionPreference = 'Stop'
dotnet test TokenStack.sln
if ($LASTEXITCODE -ne 0) { throw "tests failed - not publishing" }
dotnet publish src/TokenStack.Cli -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:Version=$Version -o publish-out
if ($LASTEXITCODE -ne 0) { throw "publish failed" }
New-Item -ItemType Directory -Force dist | Out-Null
Compress-Archive -Force -DestinationPath "dist/token-stack-v$Version.zip" `
  -Path "publish-out/token-stack.exe", "README.md"
Remove-Item -Recurse -Force publish-out
Write-Host "dist/token-stack-v$Version.zip ready" -ForegroundColor Green
```

- [ ] **Step 11.2: Create README.md**

```markdown
# token-stack

One executable that installs and manages the 3-layer Claude Code token-optimization
stack on Windows: **Headroom** (API-payload compression proxy), **RTK** (Bash
command-output filter), **Semble** (semantic code-search MCP). Measured combined
savings: ~74-95% depending on workload.

## Quick start (any Windows 10/11 machine, no admin)

1. Unzip anywhere, open a terminal next to `token-stack.exe`.
2. `.\token-stack.exe install`   (downloads pinned components; Headroom cold-loads 25-105s)
3. Fully quit Claude (Desktop: tray icon → Quit) and relaunch.
4. Every session now starts with:
   `[token-stack] Headroom: up (:8787, ROUTED, reqs=N) | RTK: up | Semble: up (MCP)`

## Commands

| Command | What |
|---|---|
| `install` | full install/repair (idempotent; `--component headroom\|rtk\|semble`) |
| `status` | live table; `--hook` one-line; `--json` |
| `start` / `stop` / `restart` | proxy lifecycle (restart = zombie recovery) |
| `config list/get/set/open` | edit `%LOCALAPPDATA%\token-stack\config.json` |
| `doctor [--fix]` | detect + repair the known failure modes |
| `update --component X [--version v]` | move a component pin |
| `gain` | unified savings report |
| `uninstall [--keep-config] [-y]` | full rollback (Claude-file backups kept) |

## Config keys (full control)

`installRoot` (no spaces!) · `headroom.enabled/port/mode(token|cache|passthrough)/version/pythonVersion/extraArgs`
· `rtk.enabled/version/source/hookMatcher(Bash only)` · `semble.enabled/version`
· `routing.cli/desktop` · `hooks.sessionStatusLine` · `bootstrap.uvInstaller`

## Trust & safety

- No admin: user-scope scheduled task, HKCU env vars, %LOCALAPPDATA% files.
- Every edit to `~/.claude/settings.json` / `~/.claude.json` first writes a
  timestamped `*.token-stack-backup-*` sibling.
- Worried about compression fidelity? `config set headroom.mode cache` (zero
  rewriting) or `passthrough` (pure passthrough).
- If the proxy dies with desktop routing on, sessions fail until `token-stack start`
  (the task auto-restarts; `doctor --fix` repairs the rest).

Knowledge base / gotcha history: `docs/reference/TOKEN-STACK-SETUP-PROMPT.md`.
```

- [ ] **Step 11.3: Archive the original setup prompt as reference**

```powershell
New-Item -ItemType Directory -Force docs/reference | Out-Null
Copy-Item "D:\proxy tokens\TOKEN-STACK-SETUP-PROMPT.md" docs/reference/TOKEN-STACK-SETUP-PROMPT.md
```

- [ ] **Step 11.4: Run publish end-to-end**

Run: `./publish.ps1`
Expected: tests pass → `dist/token-stack-v1.0.0.zip` created; zip contains
`token-stack.exe` (~60-80 MB) + `README.md`.

- [ ] **Step 11.5: Commit**

```powershell
git add -A
git commit -m "feat: publish script + README + archived setup-prompt reference"
```

---

### Task 12: Integration on this machine + clean-VM acceptance (release gate)

No new code — evidence gathering. **Do not skip: every claim needs live output.**

- [ ] **Step 12.1: Doctor must reproduce this machine's known state**

Run: `dotnet run --project src/TokenStack.Cli -- doctor`
Expected findings on the reference machine (hand-built stack):
`rtk-hook-missing` FAIL (hook points at `D:\proxy tokens\rtk\rtk.exe`, not the managed root),
`model-pin-leftover` FAIL (`ANTHROPIC_MODEL` is set at User scope — discovered during design),
`task-misconfigured` FAIL (task action points at `D:\proxy tokens`), others ok.
If `routing-bypassed` fails, the session env conflict is live — expected when running
from a BYPASSED session.

- [ ] **Step 12.2: Verify headroom CLI flags before first managed install**

Run: `& 'D:\proxy tokens\venv\Scripts\python.exe' -m headroom proxy --help` (or
`...\Scripts\headroom.exe proxy --help`)
Confirm: `--port` exists; mode flags match `--mode cache` / `--no-optimize`.
If they differ → fix `HeadroomComponent.BuildArgv` + its tests NOW, commit
`fix(core): align headroom proxy flags with --help`.

- [ ] **Step 12.3: Adopt the hand-built install (the migration path)**

Run: `dotnet run --project src/TokenStack.Cli -- install`
Expected: "detected a hand-built token stack — adopting" log; completes all 8 steps;
`%USERPROFILE%\.claude\settings.json` now has ONE SessionStart entry
(`token-stack.exe status --hook`), the rtk hook points at
`%LOCALAPPDATA%\token-stack\rtk\rtk.exe`, backups `settings.json.token-stack-backup-*`
exist; task action points at the managed venv.

- [ ] **Step 12.4: Live status + doctor green**

Run: `dotnet run --project src/TokenStack.Cli -- status` → all green, exit 0.
Run: `dotnet run --project src/TokenStack.Cli -- doctor --fix` → everything `ok`/`FIXED`, exit 0.
Restart Claude fully (tray quit) → session status line shows `ROUTED`; chat a bit →
`token-stack gain` shows `api_requests` increasing.

- [ ] **Step 12.5: Config round-trip**

Run: `dotnet run --project src/TokenStack.Cli -- config set routing.desktop false`
→ User var removed (`[Environment]::GetEnvironmentVariable('ANTHROPIC_BASE_URL','User')` = null... note: removal happens on next `doctor --fix`/wiring apply for env; verify behavior and document).
Run: `config set routing.desktop true` then `doctor --fix` → var back. Then
`config set headroom.mode cache` + `install --component headroom` + `restart` →
`proxy.log` shows cache mode startup.

- [ ] **Step 12.6: Clean VM acceptance (the v1.0 release gate)**

On a fresh Windows 11 VM (no Python, no uv, no .NET):
1. Copy `dist/token-stack-v1.0.0.zip`, unzip, `.\token-stack.exe install`.
2. Install Claude Code; restart; verify the four spec acceptance signals:
   status line `ROUTED` · `reqs` increases while chatting · `rtk gain` grows after
   Bash commands · `mcp__semble__search` returns chunks on a real repo.
3. `.\token-stack.exe uninstall -y` → env vars gone, task gone, hooks gone,
   MCP entry gone, backups present; Claude works directly against api.anthropic.com.

- [ ] **Step 12.7: Tag the release**

```powershell
git tag v1.0.0
git log --oneline
```

---

## Plan self-review record

Checked against spec sections §1–§10: every requirement maps to a task (G1→T0/T10/T11,
G2→T1/T10-config, G3→T5/T10/T12, G4→T9/T10-hook, G5→T10-uninstall/T12.6; §6's ten checks
→ T9 registry test pins all ten ids). Known intentional deviations: none. Items
deliberately deferred per spec non-goals: AOT, offline bundle, self-update, cross-platform.






