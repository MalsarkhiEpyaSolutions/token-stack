# Design: CCO read-cache as a 4th managed layer

**Date:** 2026-07-07
**Branch:** `feat/cco-read-cache-layer`
**Status:** approved (design), pending implementation plan

## Summary

Add a fourth token-optimization layer to the TokenSaver stack: **read-cache**, the
redundant-file-read blocker from `egorfedorov/claude-context-optimizer` (CCO). It runs as
Claude Code hooks that block a `Read` when the same file+range is already in context,
unchanged, this session. It is orthogonal to the existing three layers (Headroom wire
compression, RTK bash filter, Semble MCP) and safe on Opus 4.8.

Only the **read-cache** slice of CCO is integrated — NOT the tracker / prompt-coach /
context-shield / dashboard features (those collide with our SessionStart status line and
inject into prompts). We inject our own hook entries via `ClaudeSurgeon`, so we control
exactly which CCO scripts run.

Pinned CCO source: commit `e7ab49e14568a03783a9a450fa5db200939ce9d5` (v4.6.0).

## Goals

- Eliminate duplicate file-read tokens (the #1 waste in long coding sessions).
- Zero conflict with Headroom (any mode), RTK, Semble, or the status line.
- Lossless / fail-safe: never break a `Read`; never lose exact bytes.
- Managed like every other layer: install, `on/off/toggle cco`, status, doctor, offline,
  automatic timestamped backups, full rollback.

## Non-goals

- No tracker/budget/prompt-coach/context-shield/dashboard, no `/cco-*` commands.
- No `.contextignore` automation, no big-file "map-then-load" (disabled — see Safety).
- No change to Headroom/RTK/Semble behavior.

## Why read-cache is safe (from static code audit of `src/read-cache.js`)

- **Fail-safe:** every error path is `process.exit(0)` (= allow); `main().catch(()=>exit 0)`.
  A crash/garbage output can never block a read — worst case is "no saving".
- **Conservative block:** blocks ONLY when the file is cached this session AND `mtime`
  unchanged AND the requested range is already covered AND same ppid AND not stale
  (≥10% of budget / ≥8 files / ≥10 min displaced → allowed). Modified files, new ranges,
  subagent reads, and post-compaction reads all pass. `PreCompact` wipes the cache.
- **Opus-4.8 aware:** reads the model id from the transcript and scales staleness to the
  1M window. Never converts content to a lossy channel (unlike pxpipe).

## Architecture

Layer sits at the Claude Code **hook** layer, before the request is built:

```
Claude Code --Read--> [read-cache hook: block redundant] --build /v1/messages--> [Headroom proxy] --> API
```

CCO source is vendored (see Delivery) to `C:\token-stack\cco\` with the whole `src/` tree
plus `package.json` (required: `"type":"module"` so Node treats the `.js` files as ESM).
Only three scripts' worth of behavior is wired, all pointing at the single `read-cache.js`:

```
PreToolUse   matcher "Read"        command: node "C:\token-stack\cco\src\read-cache.js"
PostToolUse  matcher "Edit|Write"  command: node "C:\token-stack\cco\src\read-cache.js"
PreCompact   (no matcher)          command: node "C:\token-stack\cco\src\read-cache.js"
```

Commands are **bare `node "<path>"`** — no `test -f`, `printf`, or `payload=$(cat)`
(POSIX-only) so they run identically under cmd.exe and Git Bash on Windows. This is the
fix for CCO's own `hooks.json`, which is POSIX-only and would fail silently under cmd.

## Component behavior (`CcoComponent`, mirrors `RtkComponent`)

- `CcoDir(cfg)` = `Path.Combine(cfg.InstallRoot, "cco")`;
  `ReadCacheJs(cfg)` = `…\cco\src\read-cache.js`.
- `Install(cfg)` — takes no `InstallSource` (the snapshot is embedded in the assembly, not
  read from the offline `vendor\` bundle):
  1. Extract the embedded CCO snapshot (zip resource declared in `TokenStack.Core`) into `CcoDir`.
  2. Ensure Node ≥18 is present (`node --version`). **If absent: warn + skip cco, do NOT
     fail the install** (offline/air-gapped machines may lack Node; the bundle ships no Node).
  3. Write/merge `%USERPROFILE%\.claude-context-optimizer\config.json` with
     `{"bigFileDigest": false}` (load existing first, set the key, save — don't clobber).
  4. Smoke test: pipe empty stdin to `read-cache.js`, expect exit 0 (proves Node loads the
     ESM module graph from the vendored files).
- No PATH changes (unlike RTK). No scheduled task (unlike Headroom).

## Config (`StackConfig.cs`)

```csharp
[JsonPropertyName("cco")] public CcoConfig Cco { get; set; } = new();

public sealed class CcoConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;   // default-on
    [JsonPropertyName("version")] public string Version { get; set; } = "4.6.0";
}
```

Default `enabled = true` (consistent with the other layers; the user asked for it). On a
machine with no Node, install flips it off with a warning rather than failing.

## Surgeon (`ClaudeSurgeon.cs`)

Add `EnsureCcoHooks(root, readCacheJsPath)` and `RemoveCcoHooks(root)`, identifying "our"
entries by `command.Contains("read-cache.js")` (mirrors `IsRtkEntry` → `"rtk.exe"`).
`EnsureCcoHooks` ensures the three entries above across `PreToolUse` / `PostToolUse` /
`PreCompact`, returns true if any array changed; `RemoveCcoHooks` removes them from all
three. Reuses existing `GetOrCreateArray` / `FirstCommand` helpers. User-owned entries in
those arrays are preserved (removal is by content signature, never by position).

## Toggle (`ToggleService.cs`, `ToggleLogic.cs`)

- `ToggleService.ApplyWiring`: in the settings.json block, add
  `changed |= cfg.Cco.Enabled ? EnsureCcoHooks(settings, ccoJs) : RemoveCcoHooks(settings);`
- `LayerState` record gains a `Cco` bool (updates the record + its callers/printers).
- `ToggleLogic`: `StackLayer.Cco`; `ParseLayer("cco")`; `CurrentlyOn`/`SetFlag` include Cco;
  `All` includes Cco in the OR. `on/off/toggle cco` then works with no reinstall.

## Install pipeline (`InstallPipeline.cs`)

- New install step (after rtk): `if (cfg.Cco.Enabled) new CcoComponent(runner).Install(cfg);`
- `ApplyClaudeWiring`: `if (cfg.Cco.Enabled) changed |= EnsureCcoHooks(settings, ccoJs); else changed |= RemoveCcoHooks(settings);`
- Uninstall: `RemoveCcoHooks` + delete `CcoDir` (leave the `.claude-context-optimizer`
  data dir; it's the user's cache — same courtesy as keeping Claude backups).

## Status (`StackStatus`, `StatusLine`, `StatusProbe`, `StatusCommand`)

- `StackStatus` gains `CcoEnabled` + `CcoWired` (hook present).
- Status line becomes: `[TokenSaver] Headroom: … | RTK: … | Semble: … | CCO: <off|up|MISSING>`.
  `off` = toggled off; `up` = hook wired + read-cache.js present; `MISSING` = enabled but hook/file gone.
- `StatusCommand` table gains a CCO row. **Note:** this changes the exact status string —
  update README examples and any test asserting the line.

## Doctor (`DoctorChecks.cs`)

- `CcoHookMissingCheck` (mirrors `RtkHookMissingCheck`): if `cfg.Cco.Enabled` and the hook
  entry or `read-cache.js` is missing → FAIL (fixable). Fix = re-`EnsureCcoHooks` (+ re-extract
  if the file is gone). Register in `DoctorRegistry.All`.

## Offline / Node prerequisite

- CCO source is **embedded in `token-saver.exe`** (a zip resource, ~100 KB) and extracted at
  install → the offline `vendor\` bundle needs **no change** (the snapshot rides in the exe).
- **Node is a new external prerequisite** and is NOT in the bundle. On machines without Node,
  cco self-skips (component step 2). Document this in the offline section.

## Delivery of CCO source (embed)

Vendor the pinned snapshot as loose files in the repo (`assets/cco/` containing `src/**`,
`package.json`, `LICENSE`) — keeps git diffs readable, no committed binary. An MSBuild target
in `TokenStack.Core` zips `assets/cco` → `$(IntermediateOutputPath)cco.zip` at build time and
declares it `<EmbeddedResource>`, so the snapshot travels inside `token-saver.exe`
(single-file publish requires embedded resources, not loose content files). `CcoComponent`
reads it via `typeof(CcoComponent).Assembly.GetManifestResourceStream(...)`. MIT license —
keep `LICENSE` and attribute in README. Record the pin (`e7ab49e…`, v4.6.0) in
`assets/cco/` + README.

## Rollback (the hard requirement)

1. `token-saver off cco` — instant; removes the three hooks (bits stay on disk).
2. `settings.json.token-stack-backup-<yyyyMMdd-HHmmss>` — written automatically before every
   edit by `ClaudeFileEditor.SaveWithBackup`; restore for a full file revert.
3. `token-saver uninstall` — removes hooks + task + env + MCP; Claude backups kept.
4. Code itself: developed on `feat/cco-read-cache-layer` with incremental commits; revert the
   branch/commits to undo the installer change.

## Testing strategy (TDD-friendly — surgeon is pure)

- `ClaudeSurgeon`: `EnsureCcoHooks` adds exactly the 3 entries; idempotent (second call =
  no change); `RemoveCcoHooks` removes them; ensure→remove round-trips to the original tree;
  user-owned entries in those arrays are preserved; identification only matches `read-cache.js`.
- `ToggleLogic`: parse/flag/flip/`CurrentlyOn` for `cco` and `all`.
- `StatusLine`: renders the CCO segment for off/up/MISSING.
- Component: `bigFileDigest:false` merge preserves an existing config; Node-absent path skips
  without throwing.

## Open decisions — resolved

- Source delivery: **embed in exe** (no download, version-pinned, offline-free).
- Default state on upgrade: **default-on** (consistent; user asked for it), auto-skips when
  Node is absent.

## Risks / notes

- Status-line string change ripples to README + tests (tracked above).
- Without CCO's tracker, `getSessionModel` returns null → staleness uses the conservative
  200K tuning (re-reads allowed a bit more eagerly). Acceptable; safest-side behavior.
- Node version drift: pin behavior is the vendored JS; Node itself is whatever the user has
  (≥18). Smoke test at install catches an incompatible Node.
