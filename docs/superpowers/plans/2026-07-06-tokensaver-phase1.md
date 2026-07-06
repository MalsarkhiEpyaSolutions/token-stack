# TokenSaver Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the Headroom proxy forward to a vendor Anthropic-compatible endpoint (GLM/Kimi/MiniMax) via detected config, and rebrand the on-disk exe + surfaces to TokenSaver — with zero impact on existing installs.

**Architecture:** Add an optional `upstreamUrl` to the headroom config (default `""` = today's behavior). Detection reads Claude Code's existing `ANTHROPIC_BASE_URL`; a vendor URL is adopted as the proxy upstream and the base URL is then rewritten to the local proxy. The proxy forwards to that upstream via the `ANTHROPIC_TARGET_API_URL` env var injected into the launcher. GLM/Kimi/MiniMax inherit RTK+Semble unchanged because they *are* Claude Code.

**Tech Stack:** .NET 10, C#, xUnit, Spectre.Console.Cli, Windows scheduled task + PowerShell.

## Global Constraints

- **Zero breakage:** default path (no vendor detected) must be byte-identical to v1.0.4. All 115 existing tests stay green.
- **No secret storage:** the vendor API key is never read or stored; it passes through the proxy from Claude Code's config.
- Vendor key/model in Claude `settings.json` env are never modified.
- Keep install directory `C:\token-stack` and config dir `%LOCALAPPDATA%\token-stack` (only the exe filename and surfaces change).
- `token-stack.exe` must remain a recognized **legacy marker** so old hooks migrate.
- Ponytail full: no premature abstraction, smallest working diff, one runnable test per non-trivial logic path.
- Run tests with: `dotnet test D:\token-stack\TokenStack.sln` (from repo root `D:\token-stack`).

---

### Task 1: Config field `upstreamUrl` (backward-compatible)

**Files:**
- Modify: `src/TokenStack.Core/Config/StackConfig.cs:19-27` (HeadroomConfig)
- Test: `src/TokenStack.Tests/ConfigTests.cs`

**Interfaces:**
- Produces: `HeadroomConfig.UpstreamUrl` (string, default `""`, JSON key `upstreamUrl`).

- [ ] **Step 1: Write the failing test** — add to `ConfigTests.cs` in the `CreateDefault_HasSpecDefaults` test (after the headroom asserts):

```csharp
        Assert.Equal("", c.Headroom.UpstreamUrl); // default = Anthropic (today's behavior)
```

Also add a round-trip test proving old config (no `upstreamUrl`) loads as `""`:

```csharp
    [Fact]
    public void Load_OldConfigWithoutUpstreamUrl_DefaultsToEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), "ts-cfg", Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"schemaVersion":1,"installRoot":"C:\\ts","headroom":{"enabled":true,"port":8787,"mode":"token","version":"0.30.0","pythonVersion":"3.12","extraArgs":[]}}""");
        var c = ConfigStore.Load(path);
        Assert.Equal("", c.Headroom.UpstreamUrl);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TokenStack.sln --filter "FullyQualifiedName~ConfigTests"`
Expected: FAIL — `UpstreamUrl` does not exist / compile error.

- [ ] **Step 3: Write minimal implementation** — in `StackConfig.cs`, add to `HeadroomConfig` (after `ExtraArgs`):

```csharp
    [JsonPropertyName("upstreamUrl")] public string UpstreamUrl { get; set; } = "";
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test TokenStack.sln --filter "FullyQualifiedName~ConfigTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/TokenStack.Core/Config/StackConfig.cs src/TokenStack.Tests/ConfigTests.cs
git commit -m "feat(config): optional headroom.upstreamUrl (default empty = Anthropic)"
```

---

### Task 2: Providers table + URL→label classify

**Files:**
- Create: `src/TokenStack.Core/Components/Providers.cs`
- Test: `src/TokenStack.Tests/ProvidersTests.cs`

**Interfaces:**
- Produces:
  - `Providers.Label(string upstreamUrl)` → `"Anthropic" | "GLM" | "Kimi" | "MiniMax" | "Custom"`.
  - `ProviderDetection.ResolveUpstream(string? currentBaseUrl, string existingUpstream, string proxyUrl)` → the upstream URL to store (`""` for Anthropic default).

- [ ] **Step 1: Write the failing test** — `ProvidersTests.cs`:

```csharp
using TokenStack.Core.Components;
using Xunit;

namespace TokenStack.Tests;

public class ProvidersTests
{
    [Theory]
    [InlineData("", "Anthropic")]
    [InlineData("https://api.anthropic.com", "Anthropic")]
    [InlineData("https://api.z.ai/api/anthropic", "GLM")]
    [InlineData("https://api.moonshot.ai/anthropic", "Kimi")]
    [InlineData("https://api.moonshot.cn/anthropic", "Kimi")]
    [InlineData("https://api.minimax.io/anthropic", "MiniMax")]
    [InlineData("https://api.minimaxi.com/anthropic", "MiniMax")]
    [InlineData("https://some.other.host/anthropic", "Custom")]
    public void Label_MapsHostToVendor(string url, string expected) =>
        Assert.Equal(expected, Providers.Label(url));

    [Fact]
    public void ResolveUpstream_VendorBaseUrl_IsAdopted() =>
        Assert.Equal("https://api.moonshot.ai/anthropic",
            ProviderDetection.ResolveUpstream("https://api.moonshot.ai/anthropic", "", "http://127.0.0.1:8787"));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("https://api.anthropic.com")]
    public void ResolveUpstream_AnthropicOrEmpty_IsDefault(string? baseUrl) =>
        Assert.Equal("", ProviderDetection.ResolveUpstream(baseUrl, "", "http://127.0.0.1:8787"));

    [Fact]
    public void ResolveUpstream_AlreadyProxy_PreservesExistingUpstream() =>
        Assert.Equal("https://api.z.ai/api/anthropic",
            ProviderDetection.ResolveUpstream("http://127.0.0.1:8787", "https://api.z.ai/api/anthropic", "http://127.0.0.1:8787"));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TokenStack.sln --filter "FullyQualifiedName~ProvidersTests"`
Expected: FAIL — `Providers`/`ProviderDetection` not defined.

- [ ] **Step 3: Write minimal implementation** — `Providers.cs`:

```csharp
namespace TokenStack.Core.Components;

/// <summary>Static vendor table: maps an Anthropic-compatible upstream URL to a display
/// label. Data, not an abstraction — adoption works for ANY non-Anthropic upstream; the
/// table only names the well-known ones.</summary>
public static class Providers
{
    private static readonly (string Fragment, string Label)[] Vendors =
    {
        ("z.ai", "GLM"),
        ("moonshot.ai", "Kimi"),
        ("moonshot.cn", "Kimi"),
        ("minimax.io", "MiniMax"),
        ("minimaxi.com", "MiniMax"),
    };

    public static string Label(string upstreamUrl)
    {
        if (string.IsNullOrWhiteSpace(upstreamUrl)
            || upstreamUrl.Contains("api.anthropic.com", StringComparison.OrdinalIgnoreCase))
            return "Anthropic";
        foreach (var (frag, label) in Vendors)
            if (upstreamUrl.Contains(frag, StringComparison.OrdinalIgnoreCase)) return label;
        return "Custom";
    }
}

/// <summary>Decides the proxy upstream from the user's CURRENT Claude Code base URL.</summary>
public static class ProviderDetection
{
    public static string ResolveUpstream(string? currentBaseUrl, string existingUpstream, string proxyUrl)
    {
        if (string.IsNullOrWhiteSpace(currentBaseUrl)) return "";
        var url = currentBaseUrl.Trim();
        // Already routed through our proxy: keep whatever upstream we adopted before.
        if (url.Equals(proxyUrl, StringComparison.OrdinalIgnoreCase)
            || url.Contains("127.0.0.1", StringComparison.Ordinal)
            || url.Contains("localhost", StringComparison.OrdinalIgnoreCase))
            return existingUpstream;
        // Anthropic first-party = default (no target override).
        if (url.Contains("api.anthropic.com", StringComparison.OrdinalIgnoreCase)) return "";
        // Anything else = a vendor / custom Anthropic-compatible endpoint: adopt it.
        return url;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test TokenStack.sln --filter "FullyQualifiedName~ProvidersTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/TokenStack.Core/Components/Providers.cs src/TokenStack.Tests/ProvidersTests.cs
git commit -m "feat(core): Providers vendor table + upstream detection logic"
```

---

### Task 3: Launcher injects `ANTHROPIC_TARGET_API_URL`

**Files:**
- Modify: `src/TokenStack.Core/Resources/run_proxy.py.tmpl:16`
- Modify: `src/TokenStack.Core/Components/HeadroomComponent.cs:24-39` (RenderLauncher)
- Test: `src/TokenStack.Tests/HeadroomTests.cs`

**Interfaces:**
- Consumes: `HeadroomConfig.UpstreamUrl` (Task 1).
- Produces: launcher text containing `os.environ["ANTHROPIC_TARGET_API_URL"] = r"<url>"` iff `UpstreamUrl` non-empty.

- [ ] **Step 1: Write the failing test** — add to `HeadroomTests.cs`:

```csharp
    [Fact]
    public void RenderLauncher_NoUpstream_OmitsTargetEnv()
    {
        var cfg = new HeadroomConfig(); // UpstreamUrl == ""
        var py = HeadroomComponent.RenderLauncher(cfg, hfHome: null);
        Assert.DoesNotContain("ANTHROPIC_TARGET_API_URL", py);
    }

    [Fact]
    public void RenderLauncher_WithUpstream_InjectsTargetEnv()
    {
        var cfg = new HeadroomConfig { UpstreamUrl = "https://api.moonshot.ai/anthropic" };
        var py = HeadroomComponent.RenderLauncher(cfg, hfHome: null);
        Assert.Contains(
            "os.environ[\"ANTHROPIC_TARGET_API_URL\"] = r\"https://api.moonshot.ai/anthropic\"", py);
        var target = py.IndexOf("ANTHROPIC_TARGET_API_URL", StringComparison.Ordinal);
        var import_ = py.IndexOf("from headroom.cli import main", StringComparison.Ordinal);
        Assert.True(target >= 0 && import_ > target); // set before headroom imports
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TokenStack.sln --filter "FullyQualifiedName~HeadroomTests"`
Expected: FAIL — target env not injected.

- [ ] **Step 3a: Edit the template** — `run_proxy.py.tmpl`, change line 16 from `{{HF_ENV}}` to:

```
{{HF_ENV}}
{{TARGET_ENV}}
```

- [ ] **Step 3b: Edit `RenderLauncher`** — in `HeadroomComponent.cs`, after the `hfBlock` assignment (line 34), add:

```csharp
        var targetBlock = string.IsNullOrWhiteSpace(cfg.UpstreamUrl)
            ? ""
            : $"os.environ[\"ANTHROPIC_TARGET_API_URL\"] = r\"{cfg.UpstreamUrl}\"";
```

and add the replace in the returned chain (after `.Replace("{{HF_ENV}}", hfBlock)`):

```csharp
            .Replace("{{TARGET_ENV}}", targetBlock)
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test TokenStack.sln --filter "FullyQualifiedName~HeadroomTests"`
Expected: PASS (including the existing launcher tests — the extra blank line from an empty `{{TARGET_ENV}}` is harmless; if an existing byte-exact assertion breaks, it is asserting on `{{HF_ENV}}`-only output — update it to expect the empty target line).

- [ ] **Step 5: Commit**

```bash
git add src/TokenStack.Core/Resources/run_proxy.py.tmpl src/TokenStack.Core/Components/HeadroomComponent.cs src/TokenStack.Tests/HeadroomTests.cs
git commit -m "feat(headroom): inject ANTHROPIC_TARGET_API_URL when upstreamUrl set"
```

---

### Task 4: Detect & adopt vendor upstream during install

**Files:**
- Modify: `src/TokenStack.Core/Install/InstallPipeline.cs` (Run, after `src` resolve ~line 98, before headroom install ~line 106)
- Test: `src/TokenStack.Tests/PipelineTests.cs`

**Interfaces:**
- Consumes: `ProviderDetection.ResolveUpstream` (Task 2), `RoutingManager.ProxyUrl(port)`, `ClaudeFileEditor`, `IEnvStore.GetUser`.
- Produces: `InstallPipeline.DetectUpstream(StackConfig cfg)` → sets `cfg.Headroom.UpstreamUrl`.

- [ ] **Step 1: Write the failing test** — add to `PipelineTests.cs` (uses the existing test ctor pattern; if the pipeline needs file paths, pass temp `SettingsPath`). Minimal pure-ish test of `DetectUpstream` by seeding a settings.json:

```csharp
    [Fact]
    public void DetectUpstream_AdoptsVendorBaseUrlFromSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ts-det", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var settings = Path.Combine(dir, "settings.json");
        File.WriteAllText(settings, """{"env":{"ANTHROPIC_BASE_URL":"https://api.moonshot.ai/anthropic"}}""");
        var env = new FakeEnv();
        var pipe = new InstallPipeline(new FakeRunner(), env, new FakePort(), new FakeHttp(), _ => { })
        { SettingsPath = settings, ClaudeJsonPath = Path.Combine(dir, ".claude.json") };
        var cfg = StackConfig.CreateDefault(@"C:\ts");

        pipe.DetectUpstream(cfg);

        Assert.Equal("https://api.moonshot.ai/anthropic", cfg.Headroom.UpstreamUrl);
    }

    [Fact]
    public void DetectUpstream_PlainAnthropic_LeavesEmpty()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ts-det", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var settings = Path.Combine(dir, "settings.json");
        File.WriteAllText(settings, """{"env":{"ANTHROPIC_BASE_URL":"https://api.anthropic.com"}}""");
        var pipe = new InstallPipeline(new FakeRunner(), new FakeEnv(), new FakePort(), new FakeHttp(), _ => { })
        { SettingsPath = settings, ClaudeJsonPath = Path.Combine(dir, ".claude.json") };
        var cfg = StackConfig.CreateDefault(@"C:\ts");

        pipe.DetectUpstream(cfg);

        Assert.Equal("", cfg.Headroom.UpstreamUrl);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TokenStack.sln --filter "FullyQualifiedName~PipelineTests"`
Expected: FAIL — `DetectUpstream` not defined.

- [ ] **Step 3: Write minimal implementation** — in `InstallPipeline.cs` add the method:

```csharp
    /// <summary>Reads the user's CURRENT Claude Code base URL (settings.json env, then the
    /// User-scope env var) and adopts a vendor/custom upstream BEFORE routing rewrites the
    /// base URL to the local proxy. Idempotent: a base URL already pointing at our proxy
    /// preserves the previously-adopted upstream.</summary>
    public void DetectUpstream(StackConfig cfg)
    {
        string? current = null;
        try
        {
            var settings = new ClaudeFileEditor(SettingsPath).Load();
            current = settings["env"]?["ANTHROPIC_BASE_URL"]?.GetValue<string>();
        }
        catch { /* missing/corrupt settings = nothing to adopt */ }
        current ??= env.GetUser("ANTHROPIC_BASE_URL");

        cfg.Headroom.UpstreamUrl = ProviderDetection.ResolveUpstream(
            current, cfg.Headroom.UpstreamUrl, RoutingManager.ProxyUrl(cfg.Headroom.Port));
    }
```

Then call it in `Run`, immediately after `var src = ...` (before the `if (cfg.Headroom.Enabled)` headroom block):

```csharp
        if (cfg.Headroom.Enabled) DetectUpstream(cfg);
        if (!string.IsNullOrEmpty(cfg.Headroom.UpstreamUrl))
            log($"      adopting upstream: {Providers.Label(cfg.Headroom.UpstreamUrl)} ({cfg.Headroom.UpstreamUrl})");
```

(`env` is the ctor field `IEnvStore env`; confirm the field name in the primary ctor — it is `env`.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test TokenStack.sln --filter "FullyQualifiedName~PipelineTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/TokenStack.Core/Install/InstallPipeline.cs src/TokenStack.Tests/PipelineTests.cs
git commit -m "feat(install): detect + adopt vendor upstream from Claude base URL (idempotent)"
```

---

### Task 5: Rename exe → `token-saver.exe` + seamless migration

**Files:**
- Create: `src/TokenStack.Core/Components/Branding.cs`
- Modify: `src/TokenStack.Cli/TokenStack.Cli.csproj:17` (AssemblyName)
- Modify: `src/TokenStack.Core/Install/InstallPipeline.cs` (lines 147, 177, 202 — exe path; CopySelfToRoot delete old)
- Modify: `src/TokenStack.Core/Install/OfflinePacker.cs:73-76`
- Modify: `src/TokenStack.Cli/Commands/ToggleCommands.cs:97`
- Modify: `src/TokenStack.Core/Claude/ClaudeSurgeon.cs:88` (IsOurSessionEntry — add new marker, keep legacy)
- Modify: `publish.ps1:11-12`
- Test: `src/TokenStack.Tests/ClaudeSurgeonTests.cs`

**Interfaces:**
- Produces: `Branding.ExeName = "token-saver.exe"`, `Branding.LegacyExeName = "token-stack.exe"`.

- [ ] **Step 1: Write the failing test** — in `ClaudeSurgeonTests.cs` add a legacy-migration test, and update the existing `token-stack.exe` literals to `token-saver.exe`:

```csharp
    [Fact]
    public void EnsureSessionStatusHook_MigratesLegacyExeName()
    {
        var root = Parse("""
        { "hooks": { "SessionStart": [
          { "matcher": "startup|resume", "hooks": [
            { "type": "command", "command": "\"C:\\token-stack\\token-stack.exe\" status --hook" } ] } ] } }
        """);
        var changed = ClaudeSurgeon.EnsureSessionStatusHook(root, @"C:\token-stack\token-saver.exe");
        Assert.True(changed);
        var ss = root["hooks"]!["SessionStart"]!.AsArray();
        Assert.Single(ss); // old replaced, not duplicated
        Assert.Equal("\"C:\\token-stack\\token-saver.exe\" status --hook",
            ss[0]!["hooks"]![0]!["command"]!.GetValue<string>());
    }
```

In the same file replace the three existing `@"C:\ts\token-stack.exe"` with `@"C:\ts\token-saver.exe"` and the asserted command string on line 89 from `token-stack.exe` to `token-saver.exe`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TokenStack.sln --filter "FullyQualifiedName~ClaudeSurgeonTests"`
Expected: FAIL — legacy `token-stack.exe` hook is not recognized as "ours" yet? It IS (line 88 already matches token-stack.exe), so the migration test passes; the FAIL is the updated line-89 assertion (still emits token-saver only after we pass that path — actually this passes too). The real failing piece: `Branding` not defined once referenced. If all assertions pass at this step, proceed — the guard is Step 4's full suite.

- [ ] **Step 3a: Create `Branding.cs`:**

```csharp
namespace TokenStack.Core.Components;

/// <summary>Central product naming. The on-disk exe is token-saver.exe; token-stack.exe is
/// kept as a LEGACY marker so hooks/shortcuts from v1.0.x installs migrate on upgrade.</summary>
public static class Branding
{
    public const string ExeName = "token-saver.exe";
    public const string LegacyExeName = "token-stack.exe";
}
```

- [ ] **Step 3b: Update `ClaudeSurgeon.IsOurSessionEntry`** (line 88) to recognize both:

```csharp
            || cmd.Contains(Branding.ExeName, StringComparison.OrdinalIgnoreCase)
            || cmd.Contains(Branding.LegacyExeName, StringComparison.OrdinalIgnoreCase));
```

(remove the old single `token-stack.exe` line; add `using TokenStack.Core.Components;` if needed.)

- [ ] **Step 3c: Replace exe literals** with `Branding.ExeName`:
  - `InstallPipeline.cs:147` → `Path.Combine(cfg.InstallRoot, Branding.ExeName)`
  - `InstallPipeline.cs:177` → `Path.Combine(cfg.InstallRoot, Branding.ExeName)`
  - `InstallPipeline.cs:202` (CopySelfToRoot target) → `Path.Combine(cfg.InstallRoot, Branding.ExeName)`
  - `OfflinePacker.cs:76` → `Path.Combine(staging, Branding.ExeName)`
  - `ToggleCommands.cs:97` → `Path.Combine(Services.LoadConfigOrDefault().InstallRoot, Branding.ExeName)`

- [ ] **Step 3d: Delete the old exe after self-copy** — in `CopySelfToRoot`, after the successful `File.Copy(...)`:

```csharp
            var legacy = Path.Combine(cfg.InstallRoot, Branding.LegacyExeName);
            if (File.Exists(legacy) && !string.Equals(legacy, target, StringComparison.OrdinalIgnoreCase))
                try { File.Delete(legacy); } catch { /* locked = leave the harmless orphan */ }
```

- [ ] **Step 3e: csproj + publish.ps1** — set `TokenStack.Cli.csproj:17` to `<AssemblyName>token-saver</AssemblyName>`; in `publish.ps1` change the two `publish-out/token-stack.exe` references to `publish-out/token-saver.exe` and the zip name stays `token-stack-v$Version.zip`? No — rename to `token-saver-v$Version.zip`:

```powershell
Compress-Archive -Force -DestinationPath "dist/token-saver-v$Version.zip" `
  -Path "publish-out/token-saver.exe", "README.md"
```

and the final `Write-Host "dist/token-saver-v$Version.zip ready"`.

- [ ] **Step 4: Run the FULL suite**

Run: `dotnet test TokenStack.sln`
Expected: PASS (115 + new). Fix any remaining `token-stack.exe` literal a test still asserts on.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: rename on-disk exe to token-saver.exe (token-stack.exe kept as legacy hook marker)"
```

---

### Task 6: Status line provider suffix + `[TokenSaver]` label

**Files:**
- Modify: `src/TokenStack.Core/Status/StackStatus.cs` (add `ProviderLabel`)
- Modify: `src/TokenStack.Core/Status/StatusProbe.cs:32-42` (populate it)
- Modify: `src/TokenStack.Core/Status/StatusLine.cs:15-21`
- Test: `src/TokenStack.Tests/StatusLineTests.cs`

**Interfaces:**
- Consumes: `Providers.Label` (Task 2), `HeadroomConfig.UpstreamUrl` (Task 1).
- Produces: `StackStatus.ProviderLabel` (string, `"Anthropic"` default).

- [ ] **Step 1: Write the failing test** — in `StatusLineTests.cs`:

```csharp
    [Fact]
    public void Build_DefaultAnthropic_NoProviderSuffix_AndTokenSaverLabel()
    {
        var s = new StackStatus(true, true, true, 5, 8787, true, true, true, true, true, "Anthropic");
        var line = StatusLine.Build(s);
        Assert.StartsWith("[TokenSaver] ", line);
        Assert.Contains("Headroom: up (:8787, ROUTED, reqs=5)", line);
        Assert.DoesNotContain("ROUTED→", line);
    }

    [Fact]
    public void Build_Vendor_AppendsProviderToRoute()
    {
        var s = new StackStatus(true, true, true, 5, 8787, true, true, true, true, true, "Kimi");
        Assert.Contains("ROUTED→Kimi", StatusLine.Build(s));
    }
```

Update any existing StatusLineTests that construct `StackStatus` (add the trailing `"Anthropic"` arg) and that assert `[token-stack]` → `[TokenSaver]`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TokenStack.sln --filter "FullyQualifiedName~StatusLineTests"`
Expected: FAIL — ctor arity / label mismatch.

- [ ] **Step 3a: Add the record field** — `StackStatus.cs`, add after `SembleEnabled`:

```csharp
    bool SembleEnabled,
    string ProviderLabel = "Anthropic");
```

- [ ] **Step 3b: Populate it** — `StatusProbe.Gather`, add to the returned `new StackStatus(...)`:

```csharp
            SembleEnabled: cfg.Semble.Enabled,
            ProviderLabel: Providers.Label(cfg.Headroom.UpstreamUrl));
```

(add `using TokenStack.Core.Components;` if not present.)

- [ ] **Step 3c: Use it** — `StatusLine.Build`, change the routed branch and the label:

```csharp
            var route = s.Routed ? "ROUTED" : "BYPASSED";
            if (s.Routed && s.ProviderLabel != "Anthropic") route += "→" + s.ProviderLabel;
```

and line 21:

```csharp
        return $"[TokenSaver] Headroom: {headroom} | RTK: {rtk} | Semble: {semble}";
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test TokenStack.sln --filter "FullyQualifiedName~StatusLineTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/TokenStack.Core/Status/ src/TokenStack.Tests/StatusLineTests.cs
git commit -m "feat(status): [TokenSaver] label + ROUTED→<provider> suffix for vendors"
```

---

### Task 7: README rebrand + GLM/Kimi/MiniMax section

**Files:**
- Modify: `README.md`

**Interfaces:** none (docs).

- [ ] **Step 1: Rebrand headers & commands** — replace the title `# token-stack` with `# TokenSaver`, the intro line, and every `token-stack.exe` command example with `token-saver.exe`. Keep `C:\token-stack` paths (unchanged). Update the status-line example to `[TokenSaver] Headroom: up ...`.

- [ ] **Step 2: Add a provider section** after "Quick start":

```markdown
## GLM / Kimi / MiniMax (Claude Code with a vendor endpoint)

If your Claude Code is already pointed at a vendor Anthropic-compatible endpoint
(`ANTHROPIC_BASE_URL` = `api.z.ai/api/anthropic`, `api.moonshot.ai/anthropic`, or
`api.minimax.io/anthropic`), `install` **detects and adopts it**: it inserts the
Headroom proxy in front and forwards to that vendor. Your vendor API key and model
are never touched — they pass straight through. The status line then reads
`ROUTED→Kimi` (or GLM / MiniMax). Plain Claude Code is unaffected.
```

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "docs(readme): rebrand to TokenSaver + GLM/Kimi/MiniMax section"
```

---

### Task 8: Release v1.1.0 (build, offline splice, repo rename, tag, release)

**Files:** none (ops). Run from `D:\token-stack`.

- [ ] **Step 1: Full green build + online zip**

```powershell
.\publish.ps1 -Version 1.1.0
```
Expected: `dist/token-saver-v1.1.0.zip ready`, tests PASS.

- [ ] **Step 2: Splice offline bundle** (exe-only change over the offline v1.0.4 wheelhouse):

```powershell
$tmp = "$env:TEMP\ts-exe-110"; Expand-Archive "dist/token-saver-v1.1.0.zip" -DestinationPath $tmp -Force
Copy-Item "dist/token-stack-offline-v1.0.4.zip" "dist/token-saver-offline-v1.1.0.zip" -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::Open("dist/token-saver-offline-v1.1.0.zip", 'Update')
# old bundle carries token-stack.exe; replace it with token-saver.exe
$zip.GetEntry('token-stack.exe').Delete()
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, "$tmp\token-saver.exe", 'token-saver.exe') | Out-Null
$zip.Dispose()
```

- [ ] **Step 3: Commit any release-note file, tag, push**

```powershell
git tag -a v1.1.0 -m "TokenSaver v1.1.0"
git push origin master v1.1.0
```

- [ ] **Step 4: Rename the GitHub repo** (redirects preserved):

```powershell
gh repo rename token-saver --repo MalsarkhiEpyaSolutions/token-stack --yes
```

- [ ] **Step 5: Create the release** with a message file (avoid PowerShell quote-splitting; use `git commit -F` / `--notes-file`):

```powershell
gh release create v1.1.0 "dist/token-saver-v1.1.0.zip" --repo MalsarkhiEpyaSolutions/token-saver --title "TokenSaver v1.1.0" --latest --notes-file <notes.md>
gh release upload v1.1.0 "dist/token-saver-offline-v1.1.0.zip" --repo MalsarkhiEpyaSolutions/token-saver --clobber
```

- [ ] **Step 6: Verify** assets + latest via `gh release view v1.1.0 --repo MalsarkhiEpyaSolutions/token-saver --json assets | ConvertFrom-Json`.

---

## Self-Review

**Spec coverage:** upstreamUrl config (T1) ✓; Providers table + label (T2) ✓; ANTHROPIC_TARGET_API_URL launcher (T3) ✓; detection/adoption + idempotency (T4) ✓; exe rename + legacy migration + delete old (T5) ✓; status suffix + [TokenSaver] (T6) ✓; README + provider docs (T7) ✓; v1.1.0 release online+offline + repo rename (T8) ✓. Non-goals (AgentAdapter, other agents, key prompting, dir rename) correctly excluded.

**Placeholder scan:** release notes file path in T8 Step 5 is `<notes.md>` — the implementer writes the note content at execution (contents drafted from the spec's "Fixed/Added" summary); all code steps contain real code.

**Type consistency:** `HeadroomConfig.UpstreamUrl` (string) used identically in T1/T3/T4/T6; `Providers.Label(string)`/`ProviderDetection.ResolveUpstream(string?,string,string)` signatures match across T2/T4/T6; `StackStatus.ProviderLabel` (string, default "Anthropic") consistent T6; `Branding.ExeName`/`LegacyExeName` consistent T5.

**Zero-breakage guard:** T3 asserts empty-upstream omits the target env; T6 asserts default shows plain `ROUTED`; T1 proves old config loads as `""`; full-suite gate at T5 Step 4.
