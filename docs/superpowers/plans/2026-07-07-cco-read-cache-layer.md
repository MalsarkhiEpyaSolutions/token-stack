# CCO read-cache layer — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the read-cache (redundant-file-read blocker) slice of `egorfedorov/claude-context-optimizer` as a 4th managed layer alongside Headroom/RTK/Semble.

**Architecture:** Vendor the pinned CCO source into the exe as an embedded zip; extract to `C:\token-stack\cco\` on install; wire three Windows-safe `node` hook commands into `~/.claude/settings.json` (PreToolUse[Read], PostToolUse[Edit|Write], PreCompact) via `ClaudeSurgeon`, following the RTK layer pattern end-to-end (config flag → surgeon → toggle → status → doctor → offline). Only read-cache is wired — no tracker/prompt-coach/context-shield.

**Tech Stack:** C# / .NET 10, xUnit, System.Text.Json.Nodes, Spectre.Console.Cli, MSBuild `ZipDirectory`, Node ≥18 (runtime prerequisite on the user's machine).

**Spec:** `docs/superpowers/specs/2026-07-07-cco-read-cache-layer-design.md`

## Global Constraints

- Pinned CCO source: commit `e7ab49e14568a03783a9a450fa5db200939ce9d5` (v4.6.0), MIT license — keep `LICENSE`, attribute in README.
- Identify "our" hook entries by content signature `read-cache.js` — never by array position (mirrors `IsRtkEntry`).
- Hook commands are **bare** `node "<abs path>"` — no `test -f`, `printf`, `payload=$(cat)`, `&&`, `||` (must run under cmd.exe AND Git Bash).
- `bigFileDigest` MUST be disabled (read-cache stays pure dedup, no map-then-load).
- Node absent ⇒ install is **non-fatal**: log a warning, set `cfg.Cco.Enabled = false`, continue.
- Every `settings.json` write goes through `ClaudeFileEditor.SaveWithBackup` (automatic timestamped backup) — do not write settings.json any other way.
- Default `cco.enabled = true`.
- Test project is xUnit (`[Fact]`, `Assert.*`); fakes live in `src/TokenStack.Tests/Fakes.cs` (`FakeRunner`, `FakeEnv`, `FakePort`, `FakeHttp`).
- Build: `dotnet build TokenStack.sln`. Test: `dotnet test src/TokenStack.Tests/TokenStack.Tests.csproj`.

---

### Task 1: Vendor the pinned CCO snapshot + embed it in the exe

**Files:**
- Create: `assets/cco/**` (vendored `src/`, `package.json`, `LICENSE`, `PIN.txt`)
- Modify: `src/TokenStack.Core/TokenStack.Core.csproj`
- Create: `src/TokenStack.Core/Components/CcoComponent.cs` (extraction only for this task)
- Test: `src/TokenStack.Tests/CcoComponentTests.cs`

**Interfaces:**
- Produces: `CcoComponent.ExtractSnapshot(string destDir)` → extracts the embedded `TokenStack.Core.cco.zip` into `destDir` (so `destDir/src/read-cache.js` and `destDir/package.json` exist).

- [ ] **Step 1: Vendor the pinned snapshot**

Run (Git Bash):
```bash
cd /d/token-stack
tmp=$(mktemp -d)
git clone https://github.com/egorfedorov/claude-context-optimizer "$tmp/cco"
git -C "$tmp/cco" checkout e7ab49e14568a03783a9a450fa5db200939ce9d5
mkdir -p assets/cco
cp -r "$tmp/cco/src" assets/cco/src
cp "$tmp/cco/package.json" assets/cco/package.json
cp "$tmp/cco/LICENSE" assets/cco/LICENSE
printf 'egorfedorov/claude-context-optimizer\ncommit e7ab49e14568a03783a9a450fa5db200939ce9d5\nversion 4.6.0\nvendored 2026-07-07 — read-cache slice only\n' > assets/cco/PIN.txt
rm -rf "$tmp"
ls assets/cco assets/cco/src
```
Expected: `assets/cco/{src,package.json,LICENSE,PIN.txt}` and `assets/cco/src/read-cache.js` present. `package.json` MUST contain `"type": "module"` (required for Node ESM).

- [ ] **Step 2: Embed the snapshot as a build-time zip**

In `src/TokenStack.Core/TokenStack.Core.csproj`, add inside `<Project>` (after the existing `<ItemGroup>`):
```xml
  <Target Name="ZipCcoSnapshot" BeforeTargets="AssignTargetPaths">
    <ZipDirectory SourceDirectory="$(MSBuildProjectDirectory)\..\..\assets\cco"
                  DestinationFile="$(IntermediateOutputPath)cco.zip"
                  Overwrite="true" />
    <ItemGroup>
      <EmbeddedResource Include="$(IntermediateOutputPath)cco.zip" LogicalName="TokenStack.Core.cco.zip" />
    </ItemGroup>
  </Target>
```
(`BeforeTargets="AssignTargetPaths"` ensures the generated zip is added before resources are collected.)

- [ ] **Step 3: Write the failing extraction test**

Create `src/TokenStack.Tests/CcoComponentTests.cs`:
```csharp
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
}
```

- [ ] **Step 4: Run it to confirm it fails**

Run: `dotnet test src/TokenStack.Tests/TokenStack.Tests.csproj --filter FullyQualifiedName~CcoComponentTests`
Expected: FAIL — `CcoComponent` does not exist.

- [ ] **Step 5: Implement extraction**

Create `src/TokenStack.Core/Components/CcoComponent.cs`:
```csharp
using System.IO.Compression;
using System.Reflection;

namespace TokenStack.Core.Components;

/// <summary>The read-cache layer: extracts the pinned CCO snapshot embedded in this assembly
/// and (Task 6) writes its config + smoke-tests Node. Only read-cache.js is ever wired.</summary>
public sealed class CcoComponent
{
    public const string ResourceName = "TokenStack.Core.cco.zip";

    /// <summary>Unpack the embedded CCO snapshot into destDir (creates destDir/src/... etc.).</summary>
    public static void ExtractSnapshot(string destDir)
    {
        Directory.CreateDirectory(destDir);
        using var stream = typeof(CcoComponent).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"embedded resource '{ResourceName}' not found");
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.ExtractToDirectory(destDir, overwriteFiles: true);
    }
}
```

- [ ] **Step 6: Run to confirm it passes**

Run: `dotnet test src/TokenStack.Tests/TokenStack.Tests.csproj --filter FullyQualifiedName~CcoComponentTests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add assets/cco src/TokenStack.Core/TokenStack.Core.csproj src/TokenStack.Core/Components/CcoComponent.cs src/TokenStack.Tests/CcoComponentTests.cs
git commit -m "feat(cco): vendor pinned read-cache snapshot, embed + extract"
```

---

### Task 2: `CcoConfig` in the config model

**Files:**
- Modify: `src/TokenStack.Core/Config/StackConfig.cs`
- Test: `src/TokenStack.Tests/ConfigTests.cs` (add a test)

**Interfaces:**
- Produces: `StackConfig.Cco` → `CcoConfig { bool Enabled=true; string Version="4.6.0"; }`

- [ ] **Step 1: Write the failing test**

Add to `src/TokenStack.Tests/ConfigTests.cs`:
```csharp
[Fact]
public void CcoConfig_DefaultsEnabledTrue_AndRoundTrips()
{
    var cfg = StackConfig.CreateDefault(@"C:\ts");
    Assert.True(cfg.Cco.Enabled);
    Assert.Equal("4.6.0", cfg.Cco.Version);

    var json = System.Text.Json.JsonSerializer.Serialize(cfg);
    Assert.Contains("\"cco\"", json);
    var back = System.Text.Json.JsonSerializer.Deserialize<StackConfig>(json)!;
    Assert.True(back.Cco.Enabled);
}
```
(If `ConfigTests` lacks a `using TokenStack.Core.Config;`, it is already present — this file tests `StackConfig`.)

- [ ] **Step 2: Run to confirm it fails**

Run: `dotnet test src/TokenStack.Tests/TokenStack.Tests.csproj --filter FullyQualifiedName~ConfigTests`
Expected: FAIL — `StackConfig` has no `Cco`.

- [ ] **Step 3: Add the config**

In `src/TokenStack.Core/Config/StackConfig.cs`, add the property to `StackConfig` (after the `Semble` line):
```csharp
    [JsonPropertyName("cco")] public CcoConfig Cco { get; set; } = new();
```
And add the class (after `SembleConfig`):
```csharp
public sealed class CcoConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("version")] public string Version { get; set; } = "4.6.0";
}
```

- [ ] **Step 4: Run to confirm it passes**

Run: `dotnet test src/TokenStack.Tests/TokenStack.Tests.csproj --filter FullyQualifiedName~ConfigTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/TokenStack.Core/Config/StackConfig.cs src/TokenStack.Tests/ConfigTests.cs
git commit -m "feat(cco): add CcoConfig (enabled default true, version 4.6.0)"
```

---

### Task 3: `EnsureCcoHooks` / `RemoveCcoHooks` in ClaudeSurgeon (core)

**Files:**
- Modify: `src/TokenStack.Core/Claude/ClaudeSurgeon.cs`
- Test: `src/TokenStack.Tests/ClaudeSurgeonTests.cs` (add a region)

**Interfaces:**
- Produces:
  - `ClaudeSurgeon.EnsureCcoHooks(JsonNode root, string readCacheJsPath)` → bool (true if changed). Ensures exactly one "our" entry in each of `hooks.PreToolUse` (matcher `"Read"`), `hooks.PostToolUse` (matcher `"Edit|Write"`), and `hooks.PreCompact` (no matcher), each command `node "<readCacheJsPath>"`.
  - `ClaudeSurgeon.RemoveCcoHooks(JsonNode root)` → bool. Removes all "our" entries (command contains `read-cache.js`) from those three arrays.

- [ ] **Step 1: Write the failing tests**

Add to `src/TokenStack.Tests/ClaudeSurgeonTests.cs` (before the final `}`):
```csharp
    // ---------- CCO read-cache hooks ----------

    private const string CcoJs = @"C:\token-stack\cco\src\read-cache.js";

    [Fact]
    public void EnsureCcoHooks_AddsThreeEntries_AcrossEvents()
    {
        var root = Parse(RealisticSettings);
        var changed = ClaudeSurgeon.EnsureCcoHooks(root, CcoJs);
        Assert.True(changed);

        var pre = root["hooks"]!["PreToolUse"]!.AsArray();
        Assert.Contains(pre, e => e!["matcher"]?.GetValue<string>() == "Read"
            && e["hooks"]![0]!["command"]!.GetValue<string>() == $"node \"{CcoJs}\"");
        var post = root["hooks"]!["PostToolUse"]!.AsArray();
        Assert.Contains(post, e => e!["matcher"]?.GetValue<string>() == "Edit|Write"
            && e["hooks"]![0]!["command"]!.GetValue<string>() == $"node \"{CcoJs}\"");
        var pc = root["hooks"]!["PreCompact"]!.AsArray();
        Assert.Single(pc);
        Assert.Equal($"node \"{CcoJs}\"", pc[0]!["hooks"]![0]!["command"]!.GetValue<string>());

        // existing RTK Bash hook is untouched
        Assert.Contains(root["hooks"]!["PreToolUse"]!.AsArray(),
            e => e!["hooks"]![0]!["command"]!.GetValue<string>().Contains("rtk.exe"));
    }

    [Fact]
    public void EnsureCcoHooks_Idempotent()
    {
        var root = Parse(RealisticSettings);
        ClaudeSurgeon.EnsureCcoHooks(root, CcoJs);
        var changed = ClaudeSurgeon.EnsureCcoHooks(root, CcoJs);
        Assert.False(changed);
        Assert.Single(root["hooks"]!["PreCompact"]!.AsArray());
        Assert.Single(root["hooks"]!["PreToolUse"]!.AsArray().Where(
            e => e!["hooks"]![0]!["command"]!.GetValue<string>().Contains("read-cache.js")));
    }

    [Fact]
    public void EnsureCcoHooks_RewritesStalePath_NoDuplicate()
    {
        var root = Parse(RealisticSettings);
        ClaudeSurgeon.EnsureCcoHooks(root, @"C:\old\read-cache.js");
        var changed = ClaudeSurgeon.EnsureCcoHooks(root, CcoJs);
        Assert.True(changed);
        Assert.Single(root["hooks"]!["PreCompact"]!.AsArray());
        Assert.Equal($"node \"{CcoJs}\"",
            root["hooks"]!["PreCompact"]![0]!["hooks"]![0]!["command"]!.GetValue<string>());
    }

    [Fact]
    public void RemoveCcoHooks_RemovesOnlyOurs()
    {
        var root = Parse(RealisticSettings);
        ClaudeSurgeon.EnsureCcoHooks(root, CcoJs);
        var changed = ClaudeSurgeon.RemoveCcoHooks(root);
        Assert.True(changed);
        Assert.DoesNotContain(root["hooks"]!["PreToolUse"]!.AsArray(),
            e => e!["hooks"]![0]!["command"]!.GetValue<string>().Contains("read-cache.js"));
        Assert.Empty(root["hooks"]!["PreCompact"]!.AsArray());
        // RTK Bash hook survived
        Assert.Contains(root["hooks"]!["PreToolUse"]!.AsArray(),
            e => e!["hooks"]![0]!["command"]!.GetValue<string>().Contains("rtk.exe"));
    }

    [Fact]
    public void EnsureThenRemoveCco_RoundTripsToOriginalHookShape()
    {
        var root = Parse(RealisticSettings);
        ClaudeSurgeon.EnsureCcoHooks(root, CcoJs);
        ClaudeSurgeon.RemoveCcoHooks(root);
        // PreToolUse back to just the RTK entry; PostToolUse/PreCompact empty arrays
        Assert.Single(root["hooks"]!["PreToolUse"]!.AsArray());
        Assert.Empty(root["hooks"]!["PostToolUse"]!.AsArray());
        Assert.Empty(root["hooks"]!["PreCompact"]!.AsArray());
    }
```

- [ ] **Step 2: Run to confirm they fail**

Run: `dotnet test src/TokenStack.Tests/TokenStack.Tests.csproj --filter FullyQualifiedName~ClaudeSurgeonTests`
Expected: FAIL — `EnsureCcoHooks`/`RemoveCcoHooks` not defined.

- [ ] **Step 3: Implement the surgeon methods**

In `src/TokenStack.Core/Claude/ClaudeSurgeon.cs`, add after the RTK region (before `// ---------- SessionStart status hook ----------`):
```csharp
    // ---------- CCO read-cache hooks (PreToolUse[Read] + PostToolUse[Edit|Write] + PreCompact) ----------

    public static bool EnsureCcoHooks(JsonNode root, string readCacheJsPath)
    {
        var wanted = $"node \"{readCacheJsPath}\"";
        var changed = false;
        changed |= EnsureCcoEntry(GetOrCreateArray(root, "hooks", "PreToolUse"), wanted, "Read");
        changed |= EnsureCcoEntry(GetOrCreateArray(root, "hooks", "PostToolUse"), wanted, "Edit|Write");
        changed |= EnsureCcoEntry(GetOrCreateArray(root, "hooks", "PreCompact"), wanted, matcher: null);
        return changed;
    }

    public static bool RemoveCcoHooks(JsonNode root)
    {
        var changed = false;
        foreach (var evt in new[] { "PreToolUse", "PostToolUse", "PreCompact" })
        {
            var arr = root["hooks"]?[evt]?.AsArray();
            if (arr is null) continue;
            var ours = arr.Where(IsCcoEntry).ToList();
            foreach (var o in ours) arr.Remove(o);
            changed |= ours.Count > 0;
        }
        return changed;
    }

    private static bool EnsureCcoEntry(JsonArray arr, string wanted, string? matcher)
    {
        var ours = arr.Where(IsCcoEntry).ToList();
        if (ours.Count == 1
            && ours[0]!["matcher"]?.GetValue<string>() == matcher
            && FirstCommand(ours[0]) == wanted)
            return false;

        foreach (var o in ours) arr.Remove(o);
        var entry = new JsonObject
        {
            ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = wanted }),
        };
        if (matcher is not null) entry["matcher"] = matcher;
        arr.Add(entry);
        return true;
    }

    private static bool IsCcoEntry(JsonNode? entry) =>
        FirstCommand(entry)?.Contains("read-cache.js", StringComparison.OrdinalIgnoreCase) == true;
```

- [ ] **Step 4: Run to confirm they pass**

Run: `dotnet test src/TokenStack.Tests/TokenStack.Tests.csproj --filter FullyQualifiedName~ClaudeSurgeonTests`
Expected: PASS (all, including pre-existing RTK/Semble tests).

- [ ] **Step 5: Commit**

```bash
git add src/TokenStack.Core/Claude/ClaudeSurgeon.cs src/TokenStack.Tests/ClaudeSurgeonTests.cs
git commit -m "feat(cco): EnsureCcoHooks/RemoveCcoHooks (3 events, signature-matched)"
```

---

### Task 4: `StackLayer.Cco` in ToggleLogic

**Files:**
- Modify: `src/TokenStack.Core/Components/ToggleLogic.cs`
- Test: `src/TokenStack.Tests/ToggleTests.cs` (add tests)

**Interfaces:**
- Produces: `StackLayer.Cco`; `ParseLayer("cco") == StackLayer.Cco`; `CurrentlyOn`/`SetFlag` handle Cco; `All` includes Cco.

- [ ] **Step 1: Write the failing tests**

Add to `src/TokenStack.Tests/ToggleTests.cs`:
```csharp
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
```
(`ToggleTests` already `using TokenStack.Core.Components;` and `TokenStack.Core.Config;`.)

- [ ] **Step 2: Run to confirm they fail**

Run: `dotnet test src/TokenStack.Tests/TokenStack.Tests.csproj --filter FullyQualifiedName~ToggleTests`
Expected: FAIL — `StackLayer.Cco` not defined.

- [ ] **Step 3: Implement**

In `src/TokenStack.Core/Components/ToggleLogic.cs`:
- Change the enum: `public enum StackLayer { All, Headroom, Rtk, Semble, Cco }`
- In `ParseLayer`, add before the `_ =>` arm: `"cco" => StackLayer.Cco,`
  and update the error message to `"— use headroom | rtk | semble | cco | all"`.
- In `CurrentlyOn`, add before the `_ =>` arm: `StackLayer.Cco => c.Cco.Enabled,`
  and change the `_ =>` arm to `_ => c.Headroom.Enabled || c.Rtk.Enabled || c.Semble.Enabled || c.Cco.Enabled,`
- In `SetFlag`, add: `if (l is StackLayer.All or StackLayer.Cco) c.Cco.Enabled = on;`

- [ ] **Step 4: Run to confirm they pass**

Run: `dotnet test src/TokenStack.Tests/TokenStack.Tests.csproj --filter FullyQualifiedName~ToggleTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/TokenStack.Core/Components/ToggleLogic.cs src/TokenStack.Tests/ToggleTests.cs
git commit -m "feat(cco): StackLayer.Cco in toggle flag logic"
```

---

### Task 5: `LayerState.Cco` + ToggleService wiring + CLI printing

**Files:**
- Modify: `src/TokenStack.Core/Components/ToggleService.cs`
- Modify: `src/TokenStack.Cli/Commands/ToggleCommands.cs`
- Test: `src/TokenStack.Tests/ToggleServiceTests.cs` (add tests)

**Interfaces:**
- Consumes: `ClaudeSurgeon.EnsureCcoHooks/RemoveCcoHooks` (Task 3), `StackConfig.Cco` (Task 2).
- Produces: `LayerState(bool Headroom, bool Rtk, bool Semble, bool Cco)`; `ToggleService.ApplyWiring` adds/removes the cco hooks in settings.json based on `cfg.Cco.Enabled`.

- [ ] **Step 1: Write the failing tests**

Add to `src/TokenStack.Tests/ToggleServiceTests.cs`:
```csharp
[Fact]
public void ApplyWiring_CcoOn_AddsReadCacheHook()
{
    var (sp, cj) = TempFiles("{}", "{}");
    var cfg = StackConfig.CreateDefault(@"C:\ts"); // cco enabled by default

    var st = new ToggleService(new FakeRunner(), new FakeEnv(), sp, cj).ApplyWiring(cfg);

    Assert.True(st.Cco);
    var s = File.ReadAllText(sp);
    Assert.Contains("read-cache.js", s);
    Assert.Contains("node \"C:\\\\ts\\\\cco\\\\src\\\\read-cache.js\"", s); // bare node command, escaped in JSON
}

[Fact]
public void ApplyWiring_CcoOff_RemovesReadCacheHook_LeavesRtk()
{
    var (sp, cj) = TempFiles(
        """{ "hooks": { "PreToolUse": [ { "matcher":"Read","hooks":[{"type":"command","command":"node \"C:\\ts\\cco\\src\\read-cache.js\""}] }, { "matcher":"Bash","hooks":[{"type":"command","command":"\"C:\\ts\\rtk\\rtk.exe\" hook claude"}] } ] } }""",
        "{}");
    var cfg = StackConfig.CreateDefault(@"C:\ts");
    cfg.Cco.Enabled = false;

    var st = new ToggleService(new FakeRunner(), new FakeEnv(), sp, cj).ApplyWiring(cfg);

    Assert.False(st.Cco);
    var s = File.ReadAllText(sp);
    Assert.DoesNotContain("read-cache.js", s);
    Assert.Contains("rtk.exe", s); // RTK survived
}
```

- [ ] **Step 2: Run to confirm they fail**

Run: `dotnet test src/TokenStack.Tests/TokenStack.Tests.csproj --filter FullyQualifiedName~ToggleServiceTests`
Expected: FAIL — `LayerState` has no `Cco`; `ApplyWiring` doesn't wire cco.

- [ ] **Step 3: Wire cco into ToggleService**

In `src/TokenStack.Core/Components/ToggleService.cs`:
- Change the record: `public sealed record LayerState(bool Headroom, bool Rtk, bool Semble, bool Cco);`
- In `ApplyWiring`, inside the settings.json section (after the rtk `changed = …` line, before the routing `changed |= …` line), add:
```csharp
        changed |= cfg.Cco.Enabled
            ? ClaudeSurgeon.EnsureCcoHooks(settings,
                Path.Combine(cfg.InstallRoot, "cco", "src", "read-cache.js"))
            : ClaudeSurgeon.RemoveCcoHooks(settings);
```
- Change the return: `return new LayerState(cfg.Headroom.Enabled, cfg.Rtk.Enabled, cfg.Semble.Enabled, cfg.Cco.Enabled);`

- [ ] **Step 4: Fix the CLI printers for the new record shape**

In `src/TokenStack.Cli/Commands/ToggleCommands.cs`:
- In `ToggleRunner.Run`, change the status line to include CCO:
```csharp
        AnsiConsole.MarkupLine($"Headroom: {On(state.Headroom)}  RTK: {On(state.Rtk)}  Semble: {On(state.Semble)}  CCO: {On(state.Cco)}");
```
- In `ShowNotification`, update the message + `anyOn`:
```csharp
        var msg = $"Headroom: {Onoff(s.Headroom)}  |  RTK: {Onoff(s.Rtk)}  |  Semble: {Onoff(s.Semble)}  |  CCO: {Onoff(s.Cco)}" +
                  "      (restart Claude to apply)";
        var anyOn = s.Headroom || s.Rtk || s.Semble || s.Cco;
```

- [ ] **Step 5: Run to confirm they pass + solution builds**

Run: `dotnet build TokenStack.sln`
Expected: build succeeds (no other `LayerState` call sites broke).
Run: `dotnet test src/TokenStack.Tests/TokenStack.Tests.csproj --filter FullyQualifiedName~ToggleServiceTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/TokenStack.Core/Components/ToggleService.cs src/TokenStack.Cli/Commands/ToggleCommands.cs src/TokenStack.Tests/ToggleServiceTests.cs
git commit -m "feat(cco): wire read-cache hooks in ToggleService + LayerState.Cco"
```

---

### Task 6: `CcoComponent` install (config + Node check)

**Files:**
- Modify: `src/TokenStack.Core/Components/CcoComponent.cs`
- Test: `src/TokenStack.Tests/CcoComponentTests.cs` (add tests)

**Interfaces:**
- Consumes: `CcoComponent.ExtractSnapshot` (Task 1), `IProcessRunner` (`src/TokenStack.Core/Windows`).
- Produces:
  - `CcoComponent.DisableBigFileDigest(string? existingJson)` → string (JSON with `"bigFileDigest": false`, preserving other keys).
  - `new CcoComponent(IProcessRunner runner).NodePresent()` → bool.
  - `CcoComponent.CcoDir(StackConfig)` / `ReadCacheJs(StackConfig)` path helpers.
  - `Install(StackConfig cfg)` → extract + write config + smoke (throws only on a real extract/smoke failure; assumes Node present — caller checks `NodePresent()` first).

- [ ] **Step 1: Write the failing tests**

Add to `src/TokenStack.Tests/CcoComponentTests.cs`:
```csharp
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
    var absent  = new CcoComponent(new FakeRunner { Handler = (f, a) => new(1, "", "not found") });
    Assert.True(present.NodePresent());
    Assert.False(absent.NodePresent());
}
```

- [ ] **Step 2: Run to confirm they fail**

Run: `dotnet test src/TokenStack.Tests/TokenStack.Tests.csproj --filter FullyQualifiedName~CcoComponentTests`
Expected: FAIL — members not defined.

- [ ] **Step 3: Implement the install logic**

Replace `src/TokenStack.Core/Components/CcoComponent.cs` with:
```csharp
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using TokenStack.Core.Config;
using TokenStack.Core.Windows;

namespace TokenStack.Core.Components;

/// <summary>The read-cache layer: extracts the pinned CCO snapshot embedded in this assembly,
/// disables the lossy big-file "map-then-load" nudge, and smoke-tests that Node can load the
/// ESM module graph. Only read-cache.js is ever wired (see ClaudeSurgeon.EnsureCcoHooks).</summary>
public sealed class CcoComponent(IProcessRunner runner)
{
    public const string ResourceName = "TokenStack.Core.cco.zip";

    public static string CcoDir(StackConfig cfg) => Path.Combine(cfg.InstallRoot, "cco");
    public static string ReadCacheJs(StackConfig cfg) => Path.Combine(CcoDir(cfg), "src", "read-cache.js");

    private static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude-context-optimizer");

    public static void ExtractSnapshot(string destDir)
    {
        Directory.CreateDirectory(destDir);
        using var stream = typeof(CcoComponent).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"embedded resource '{ResourceName}' not found");
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.ExtractToDirectory(destDir, overwriteFiles: true);
    }

    /// <summary>Merge {"bigFileDigest": false} into CCO's config JSON, preserving other keys.</summary>
    public static string DisableBigFileDigest(string? existingJson)
    {
        JsonObject obj;
        try { obj = (JsonNode.Parse(string.IsNullOrWhiteSpace(existingJson) ? "{}" : existingJson) as JsonObject) ?? new(); }
        catch { obj = new(); }
        obj["bigFileDigest"] = false;
        return obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public bool NodePresent() => runner.Run("node", "--version", 15000).Ok;

    /// <summary>Extract the snapshot, disable the nudge, smoke-test the ESM graph. Caller must
    /// have confirmed NodePresent() first (this throws if the smoke fails).</summary>
    public void Install(StackConfig cfg)
    {
        ExtractSnapshot(CcoDir(cfg));

        Directory.CreateDirectory(DataDir);
        var configPath = Path.Combine(DataDir, "config.json");
        var existing = File.Exists(configPath) ? File.ReadAllText(configPath) : null;
        File.WriteAllText(configPath, DisableBigFileDigest(existing));

        // Smoke: importing read-cache.js (not as main) loads utils/contextignore/file-digest
        // without running the hook body — proves the vendored ESM graph resolves under this Node.
        var smoke = Path.Combine(CcoDir(cfg), "_smoke.mjs");
        File.WriteAllText(smoke, "import './src/read-cache.js';\n");
        try
        {
            var r = runner.Run("node", $"\"{smoke}\"", 20000);
            if (!r.Ok)
                throw new InvalidOperationException($"cco smoke failed (node could not load read-cache.js): {r.StdErr}");
        }
        finally { try { File.Delete(smoke); } catch { /* best effort */ } }
    }
}
```

- [ ] **Step 4: Run to confirm they pass**

Run: `dotnet test src/TokenStack.Tests/TokenStack.Tests.csproj --filter FullyQualifiedName~CcoComponentTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/TokenStack.Core/Components/CcoComponent.cs src/TokenStack.Tests/CcoComponentTests.cs
git commit -m "feat(cco): CcoComponent install — disable bigFileDigest + Node smoke"
```

---

### Task 7: InstallPipeline — install step, wiring, uninstall

**Files:**
- Modify: `src/TokenStack.Core/Install/InstallPipeline.cs`
- Test: `src/TokenStack.Tests/PipelineTests.cs` (add a test)

**Interfaces:**
- Consumes: `CcoComponent` (Task 6), `ClaudeSurgeon.EnsureCcoHooks/RemoveCcoHooks` (Task 3).
- Produces: `PlanSteps` includes a `"cco"` step iff `cfg.Cco.Enabled`; `ApplyClaudeWiring` ensures/removes the cco hooks; `Run` installs cco (Node-absent = warn + disable); `Uninstall` removes the cco hooks.

- [ ] **Step 1: Write the failing test**

Add to `src/TokenStack.Tests/PipelineTests.cs`:
```csharp
[Fact]
public void PlanSteps_IncludesCco_WhenEnabled_AndOmits_WhenDisabled()
{
    var on = StackConfig.CreateDefault(@"C:\ts");
    Assert.Contains(InstallPipeline.PlanSteps(on), s => s.Name == "cco");

    var off = StackConfig.CreateDefault(@"C:\ts");
    off.Cco.Enabled = false;
    Assert.DoesNotContain(InstallPipeline.PlanSteps(off), s => s.Name == "cco");
}
```
(`PipelineTests` already `using TokenStack.Core.Install;` and `TokenStack.Core.Config;`.)

- [ ] **Step 2: Run to confirm it fails**

Run: `dotnet test src/TokenStack.Tests/TokenStack.Tests.csproj --filter FullyQualifiedName~PipelineTests`
Expected: FAIL — no `"cco"` step.

- [ ] **Step 3: Add the cco step to PlanSteps**

In `src/TokenStack.Core/Install/InstallPipeline.cs`, in `PlanSteps`, after the `semble` line:
```csharp
        if (cfg.Cco.Enabled) steps.Add(new("cco", () => { }));
```

- [ ] **Step 4: Install cco in Run() (Node-absent = non-fatal)**

In `Run`, after the rtk block (the `if (cfg.Rtk.Enabled) { … rtk.Install … }`), add:
```csharp
        var cco = new CcoComponent(runner);
        if (cfg.Cco.Enabled)
        {
            if (cco.NodePresent())
            {
                log($"[cco] read-cache {cfg.Cco.Version} (extract + disable big-file nudge + node smoke)");
                cco.Install(cfg);
            }
            else
            {
                log("[cco] Node (>=18) not found on PATH — skipping read-cache layer " +
                    "(install Node and run `token-saver on cco` to enable it later).");
                cfg.Cco.Enabled = false;
            }
        }
```

- [ ] **Step 5: Wire cco in ApplyClaudeWiring**

In `ApplyClaudeWiring`, after the RTK `if/else` block (before the SessionStatusLine block), add:
```csharp
        if (cfg.Cco.Enabled)
            changed |= ClaudeSurgeon.EnsureCcoHooks(settings, CcoComponent.ReadCacheJs(cfg));
        else
            changed |= ClaudeSurgeon.RemoveCcoHooks(settings);
```

- [ ] **Step 6: Remove cco hooks in Uninstall**

In `Uninstall`, in the settings edit block, after `var changed = ClaudeSurgeon.RemoveRtkHook(settings);` add:
```csharp
        changed |= ClaudeSurgeon.RemoveCcoHooks(settings);
```
And after the settings save block, add a note line:
```csharp
        log("NOTE: %USERPROFILE%\\.claude-context-optimizer (read-cache data) left on disk — delete manually if unwanted.");
```

- [ ] **Step 7: Run to confirm it passes + builds**

Run: `dotnet build TokenStack.sln`
Expected: build succeeds.
Run: `dotnet test src/TokenStack.Tests/TokenStack.Tests.csproj --filter FullyQualifiedName~PipelineTests`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/TokenStack.Core/Install/InstallPipeline.cs src/TokenStack.Tests/PipelineTests.cs
git commit -m "feat(cco): install step + claude wiring + uninstall (node-absent non-fatal)"
```

---

### Task 8: Status — StackStatus, StatusLine, StatusProbe, StatusCommand

**Files:**
- Modify: `src/TokenStack.Core/Status/StackStatus.cs`
- Modify: `src/TokenStack.Core/Status/StatusLine.cs`
- Modify: `src/TokenStack.Core/Status/StatusProbe.cs`
- Modify: `src/TokenStack.Cli/Commands/StatusCommand.cs`
- Test: `src/TokenStack.Tests/StatusLineTests.cs` (update Healthy + exact-line asserts, add cco tests)

**Interfaces:**
- Produces: `StackStatus.CcoEnabled` + `StackStatus.CcoWired`; status line ends with ` | CCO: <off|up|MISSING>`.

- [ ] **Step 1: Update the failing tests**

In `src/TokenStack.Tests/StatusLineTests.cs`:
- Update `Healthy()` to include the two new fields:
```csharp
    private static StackStatus Healthy() => new(
        TaskRunning: true, PortListening: true, Routed: true, Reqs: 42,
        Port: 8787, RtkOnPath: true, SembleWired: true,
        HeadroomEnabled: true, RtkEnabled: true, SembleEnabled: true,
        CcoEnabled: true, CcoWired: true);
```
- Update the two exact-equality expected strings to append ` | CCO: up`:
  - `AllUp_Routed_WithReqs`: `"[TokenSaver] Headroom: up (:8787, ROUTED, reqs=42) | RTK: up | Semble: up (MCP) | CCO: up"`
  - `ReqsOmitted_WhenStatsUnreachable`: `"[TokenSaver] Headroom: up (:8787, ROUTED) | RTK: up | Semble: up (MCP) | CCO: up"`
- Add:
```csharp
[Fact]
public void CcoOff_And_Missing_Reported()
{
    Assert.Contains("CCO: OFF", StatusLine.Build(Healthy() with { CcoEnabled = false }));
    Assert.Contains("CCO: MISSING", StatusLine.Build(Healthy() with { CcoWired = false }));
}
```

- [ ] **Step 2: Run to confirm they fail**

Run: `dotnet test src/TokenStack.Tests/TokenStack.Tests.csproj --filter FullyQualifiedName~StatusLineTests`
Expected: FAIL — `StackStatus` has no `CcoEnabled`/`CcoWired`.

- [ ] **Step 3: Add fields to StackStatus**

In `src/TokenStack.Core/Status/StackStatus.cs`, append two params to the record (after `ProviderLabel`):
```csharp
    string ProviderLabel = "Anthropic",
    bool CcoEnabled = false,
    bool CcoWired = false);
```
(Replace the existing closing `);` of the record accordingly.)

- [ ] **Step 4: Render the CCO segment**

In `src/TokenStack.Core/Status/StatusLine.cs`, before the final `return`:
```csharp
        var cco = !s.CcoEnabled ? "OFF" : s.CcoWired ? "up" : "MISSING";
```
And change the return to append it:
```csharp
        return $"[TokenSaver] Headroom: {headroom} | RTK: {rtk} | Semble: {semble} | CCO: {cco}";
```

- [ ] **Step 5: Populate the fields in StatusProbe**

In `src/TokenStack.Core/Status/StatusProbe.cs`, in `Gather`, add before the `return`:
```csharp
        var ccoWired = cfg.Cco.Enabled
            && File.Exists(Path.Combine(cfg.InstallRoot, "cco", "src", "read-cache.js"));
```
And add to the `new StackStatus(...)` argument list (after `ProviderLabel: …`):
```csharp
            CcoEnabled: cfg.Cco.Enabled,
            CcoWired: ccoWired);
```
(Move the closing `)` onto the last argument.)

- [ ] **Step 6: Add the CCO row to the status table**

In `src/TokenStack.Cli/Commands/StatusCommand.cs`, after the `Semble` `t.AddRow(...)`:
```csharp
        t.AddRow("CCO",
            !status.CcoEnabled ? "[grey]OFF[/]" : status.CcoWired ? "[green]up[/]" : "[red]MISSING[/]",
            "PreToolUse Read dedup");
```

- [ ] **Step 7: Run to confirm they pass + builds**

Run: `dotnet build TokenStack.sln`
Expected: build succeeds.
Run: `dotnet test src/TokenStack.Tests/TokenStack.Tests.csproj --filter FullyQualifiedName~StatusLineTests`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/TokenStack.Core/Status/StackStatus.cs src/TokenStack.Core/Status/StatusLine.cs src/TokenStack.Core/Status/StatusProbe.cs src/TokenStack.Cli/Commands/StatusCommand.cs src/TokenStack.Tests/StatusLineTests.cs
git commit -m "feat(cco): status line + table segment (off/up/MISSING)"
```

---

### Task 9: Doctor — CcoHookMissingCheck

**Files:**
- Modify: `src/TokenStack.Core/Doctor/DoctorChecks.cs`
- Test: `src/TokenStack.Tests/DoctorTests.cs` (add a test)

**Interfaces:**
- Consumes: `DoctorContext` (`.Config`, `.Settings`, `.SettingsChanged`), `ClaudeSurgeon.EnsureCcoHooks`, `CcoComponent.ReadCacheJs`.
- Produces: `CcoHookMissingCheck` registered in `DoctorRegistry.All`.

- [ ] **Step 1: Write the failing test**

Add to `src/TokenStack.Tests/DoctorTests.cs` (mirror the existing rtk-hook check test in that file for context/fakes):
```csharp
[Fact]
public void CcoHookMissingCheck_FailsThenFixes_WhenEnabledButUnwired()
{
    var cfg = StackConfig.CreateDefault(@"C:\ts"); // cco enabled
    var ctx = new DoctorContext(cfg, JsonNode.Parse("{}")!, JsonNode.Parse("{}")!,
        new FakeEnv(), new FakePort(), new FakeRunner(), new FakeHttp());

    var check = new CcoHookMissingCheck();
    Assert.False(check.Detect(ctx).Ok);      // enabled but no hook present
    Assert.True(check.Fix(ctx));             // adds it
    Assert.True(ctx.SettingsChanged);
    Assert.True(check.Detect(ctx).Ok);       // now present
}
```
(If `DoctorTests.cs` lacks `using System.Text.Json.Nodes;`, add it.)

- [ ] **Step 2: Run to confirm it fails**

Run: `dotnet test src/TokenStack.Tests/TokenStack.Tests.csproj --filter FullyQualifiedName~DoctorTests`
Expected: FAIL — `CcoHookMissingCheck` not defined.

- [ ] **Step 3: Implement the check + register it**

In `src/TokenStack.Core/Doctor/DoctorChecks.cs`, add the class (near `RtkHookMissingCheck`):
```csharp
public sealed class CcoHookMissingCheck : IDoctorCheck
{
    public string Id => "cco-hook-missing";
    public CheckResult Detect(DoctorContext ctx)
    {
        if (!ctx.Config.Cco.Enabled) return new(Id, true, "cco disabled", false);
        var pre = ctx.Settings["hooks"]?["PreToolUse"]?.AsArray();
        var wired = pre?.Any(e => e?["hooks"]?[0]?["command"]?.GetValue<string>()
            ?.Contains("read-cache.js", StringComparison.OrdinalIgnoreCase) == true) == true;
        return wired
            ? new(Id, true, "cco read-cache hook present", false)
            : new(Id, false, "cco enabled but read-cache hook missing", true);
    }
    public bool Fix(DoctorContext ctx)
    {
        var changed = Claude.ClaudeSurgeon.EnsureCcoHooks(ctx.Settings,
            Components.CcoComponent.ReadCacheJs(ctx.Config));
        ctx.SettingsChanged |= changed;
        return changed;
    }
}
```
Then add `new CcoHookMissingCheck(),` to the `DoctorRegistry.All` array.

- [ ] **Step 4: Run to confirm it passes**

Run: `dotnet test src/TokenStack.Tests/TokenStack.Tests.csproj --filter FullyQualifiedName~DoctorTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/TokenStack.Core/Doctor/DoctorChecks.cs src/TokenStack.Tests/DoctorTests.cs
git commit -m "feat(cco): doctor check for a missing read-cache hook"
```

---

### Task 10: CLI descriptions + README + docs

**Files:**
- Modify: `src/TokenStack.Cli/Program.cs`
- Modify: `README.md`

**Interfaces:** none (docs/help text only).

- [ ] **Step 1: Update on/off/toggle descriptions**

In `src/TokenStack.Cli/Program.cs`, change the three descriptions to include cco:
```csharp
    c.AddCommand<OnCommand>("on").WithDescription("Turn a layer (or all) ON: on (headroom|rtk|semble|cco)");
    c.AddCommand<OffCommand>("off").WithDescription("Turn a layer (or all) OFF: off (headroom|rtk|semble|cco)");
    c.AddCommand<ToggleCommand>("toggle").WithDescription("Flip a layer (or all) on/off");
```

- [ ] **Step 2: Update README**

In `README.md`:
- First paragraph: change "**Headroom** … **RTK** … **Semble** …" to add
  "**CCO** (read-cache: blocks redundant file re-reads)".
- Update the example status line to:
  `[TokenSaver] Headroom: up (:8787, ROUTED, reqs=N) | RTK: up | Semble: up (MCP) | CCO: up`
- Add a short subsection after the toggle section:
```markdown
## CCO read-cache (4th layer)

Blocks redundant file re-reads (same file, same range, unchanged, this session) — the #1
token waste in long coding sessions. Runs as `node` hooks (PreToolUse Read, PostToolUse
Edit|Write, PreCompact) that point at a pinned, vendored snapshot of
`egorfedorov/claude-context-optimizer` (read-cache only — no tracker/prompt-coach). Lossless
and fail-safe: any error lets the read through. The lossy "map-then-load" nudge is disabled
(`bigFileDigest:false`). **Requires Node ≥18 on PATH**; if absent, install skips it with a
warning (the offline bundle ships no Node). Toggle with `on|off|toggle cco`.
```
- In the Config-keys line, add: `· cco.enabled/version`
- In the Commands table `on / off / toggle` row, change the layer list to include `cco`.

- [ ] **Step 3: Build to confirm nothing broke**

Run: `dotnet build TokenStack.sln`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/TokenStack.Cli/Program.cs README.md
git commit -m "docs(cco): CLI help + README for the read-cache layer"
```

---

### Task 11: Full green build + activation note (user-gated)

**Files:** none (verification).

- [ ] **Step 1: Full build**

Run: `dotnet build TokenStack.sln -c Release`
Expected: build succeeds, no warnings introduced by our files.

- [ ] **Step 2: Full test run**

Run: `dotnet test src/TokenStack.Tests/TokenStack.Tests.csproj`
Expected: ALL tests pass (new + pre-existing). If any pre-existing status-line/toggle test still asserts the old 3-layer string, update its expected value to include the CCO segment.

- [ ] **Step 3: Confirm the embedded resource shipped**

Run (Git Bash):
```bash
dotnet build src/TokenStack.Core/TokenStack.Core.csproj -c Release >/dev/null
strings src/TokenStack.Core/bin/Release/net10.0/TokenStack.Core.dll | grep -c "cco.zip" || true
```
Expected: ≥1 (the resource name is present).

- [ ] **Step 4: STOP — live activation is user-gated**

Do NOT run `token-saver install` or `token-saver on cco` against the user's live machine as
part of this plan. That step edits the running `~/.claude/settings.json` (auto-backed-up) and
should be a deliberate, user-initiated action, ideally after fully quitting + relaunching
Claude. Report that the build+tests are green and hand the activation decision back to the user.

- [ ] **Step 5: Final commit / branch summary**

```bash
git log --oneline master..feat/cco-read-cache-layer
```
Report the commit list; the branch is ready to merge or to activate on the machine.

---

## Self-Review

**Spec coverage:** config (T2) ✓, surgeon hooks (T3) ✓, component/extract/config/node (T1,T6) ✓,
toggle+on/off (T4,T5) ✓, install/wiring/uninstall (T7) ✓, status line change (T8) ✓, doctor (T9) ✓,
offline=embedded/no-bundle-change (T1) ✓, node-absent non-fatal (T7) ✓, README/help (T10) ✓,
rollback via SaveWithBackup + off + uninstall + branch (T7,T11 + automatic) ✓, Windows-safe bare
`node` command (T3, asserted in T3/T5 tests) ✓, bigFileDigest off (T6) ✓.

**Placeholders:** none — every code step has complete code.

**Type consistency:** `LayerState(…, bool Cco)` defined T5, printed T5; `CcoComponent.ReadCacheJs`
defined T6, used T7/T9; `StackStatus.CcoEnabled/CcoWired` defined T8, used T8; `EnsureCcoHooks`
signature `(JsonNode, string)` consistent across T3/T5/T7/T9.

**Note (verify during execution):** `ConfigValidator.Validate` — `SembleConfig.Version` ("latest")
is not validated, so `CcoConfig.Version` needs no new rule; confirm by reading the file, add none.
