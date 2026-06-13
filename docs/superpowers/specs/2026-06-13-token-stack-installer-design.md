# token-stack — Design Spec

**Date:** 2026-06-13 · **Status:** Approved (design review with owner) · **Target:** v1.0

A single self-contained Windows executable, `token-stack.exe`, that installs, configures,
supervises, diagnoses, and uninstalls the 3-layer Claude Code token-optimization stack
(**Headroom** API-payload proxy, **RTK** command-output filter, **Semble** semantic
code-search MCP) on any Windows machine, for any user, working across all projects.

It productizes a working hand-built reference install (see Appendix A) and the hard-won
operational lessons in `TOKEN-STACK-SETUP-PROMPT.md`, replacing "paste a 14 KB prompt into
an AI agent" with a deterministic program.

---

## 1. Goals

- **G1 — Portable:** hand anyone one `.exe` (no .NET runtime, no Python, no admin rights
  required on the target machine); `token-stack install` produces a fully working stack.
- **G2 — Configurable (full control):** every meaningful knob lives in one JSON config file
  and is editable post-install via `token-stack config`; changes are re-applied to the live
  wiring automatically.
- **G3 — Self-verifying:** every install step proves itself with live evidence (port bound,
  endpoint ready, tool resolves) before the next step runs; `status`/`doctor` report live
  truth, not config presence.
- **G4 — Self-healing:** the known failure modes (zombie listener, BYPASSED routing, broken
  MCP handshake) are detected and auto-fixed by `doctor --fix` and the session status hook.
- **G5 — Reversible:** `uninstall` returns the machine to its pre-install state (env vars,
  scheduled task, hooks, MCP registration, files), with timestamped backups of every Claude
  config file it ever touched.

### Non-goals (v1)

- macOS / Linux support (Windows 10/11 only — decided in design review).
- Offline/air-gapped bundle (components are downloaded at install time from PyPI/GitHub —
  decided in design review; an offline cache mode may come later).
- NativeAOT publish (deferred optimization; v1 ships self-contained single-file CoreCLR).
- Self-update of `token-stack.exe` itself (`update` covers the three components only).
- Linux-style service management UX (Windows Scheduled Task is the only supervisor).

---

## 2. The three components (what gets installed)

| Layer | What it is | Public source | Install mechanism |
|---|---|---|---|
| Headroom | Local reverse proxy on `127.0.0.1:<port>` compressing every Anthropic API request payload | PyPI `headroom-ai[proxy]` (ref: 0.24.0) | dedicated venv created by `uv venv --python <ver>` (uv provisions CPython itself — no system Python needed) |
| RTK | Rust CLI that filters Bash tool output (`ls`/`find`/`git` 60–90% smaller) | GitHub releases `rtk-ai/rtk` (`rtk-x86_64-pc-windows-msvc.zip`, ref: 0.42.3) | download pinned release, extract `rtk.exe`, add to User PATH |
| Semble | Semantic code-search MCP server (top-k chunks instead of grep+read) | PyPI `semble[mcp]` | `uv tool install "semble[mcp]"` → absolute exe path registered as stdio MCP |

The only bootstrap dependency is **uv** (detected → else `winget install astral-sh.uv` →
else the standalone installer from astral.sh). uv then provides Python for Headroom and the
tool install for Semble.

---

## 3. Architecture

### 3.1 Solution layout (new standalone repo: `D:\token-stack`)

```
token-stack/
├── src/
│   ├── TokenStack.Cli/        # entry point, Spectre.Console.Cli command surface
│   └── TokenStack.Core/       # all logic, no console concerns
│       ├── Config/            # schema, load/save/validate, key-path get/set
│       ├── Components/        # HeadroomComponent, RtkComponent, SembleComponent
│       │                      # (shared IComponent: Install/Verify/Unwire/Status)
│       ├── Claude/            # surgical JsonNode merge of settings.json / .claude.json
│       │                      # + timestamped backups + rollback
│       ├── Windows/           # schtasks XML generation+invocation, User env vars,
│       │                      # PATH editing, process/port probes
│       ├── Net/               # GitHub release download, PyPI via uv/pip, checksums
│       └── Doctor/            # check registry: one class per known failure mode
└── src/TokenStack.Tests/      # xUnit: merge logic, config, task XML, status lines
```

### 3.2 Technology choices

- **.NET 10**, C#, `TokenStack.Cli` console app.
- **Spectre.Console.Cli** for commands; **Spectre.Console** for tables/status output.
- **Publish:** `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true`
  (≈70 MB, zero runtime prerequisites). `publish.ps1` builds `dist/token-stack-vX.Y.zip`
  (exe + README).
- **Scheduled Task:** generate full task XML (logon trigger, `RestartCount=3`,
  `RestartInterval=PT1M`, `ExecutionTimeLimit=PT0S`, `MultipleInstances=IgnoreNew`, Hidden)
  and register via `schtasks /create /tn HeadroomProxy /xml <file>` — full fidelity, no COM
  interop, AOT-safe for the future.
- **Env vars:** `Environment.SetEnvironmentVariable(name, value, User)` (writes HKCU +
  broadcasts `WM_SETTINGCHANGE`).
- **Claude config edits:** `System.Text.Json` `JsonNode` read-modify-write that preserves
  all unknown properties; every write is preceded by a copy to
  `<file>.token-stack-backup-<yyyyMMdd-HHmmss>`.
- **Embedded resources:** `run_proxy.py` template (with `{{ROOT}}`, `{{ARGS}}`
  placeholders), task XML template, README snippets.

### 3.3 One source of truth for status

The PowerShell `ensure-stack.ps1` hook is **retired**. Both SessionStart hooks become a
single call to `"<installRoot>\token-stack.exe" status --hook`, which:

1. starts the scheduled task if stopped;
2. runs zombie recovery (task `Running` + port dead + last-run > 3 min → stop task, kill
   orphaned `pythonw` running `run_proxy`, restart task);
3. prints exactly one line, never throws, exits 0:
   `[token-stack] Headroom: up (:8787, ROUTED, reqs=N) | RTK: up | Semble: up (MCP)`.

`ROUTED`/`BYPASSED` is judged from the hook's **inherited session env** (what the API
client actually got), never from config presence. Cold start (< startup window) reports
`starting (cold-load ~50s)` rather than a false `DOWN`.

---

## 4. CLI surface

| Command | Behavior |
|---|---|
| `token-stack install` | Full pipeline (§5). Idempotent: re-running converges, never duplicates hooks/tasks/PATH entries. `--component headroom\|rtk\|semble` installs a subset honoring `enabled` flags. |
| `token-stack status [--hook] [--json]` | Live status table (port, routing, reqs served, task state, rtk on PATH + hook wired, semble exe + MCP registered). `--hook` = one-line mode (§3.3). `--json` for scripting. |
| `token-stack start / stop / restart` | Scheduled-task control + the zombie-recovery sequence on `restart`. |
| `token-stack config list / get <key> / set <key> <value> / open` | Dot-path access into config.json (`config set headroom.port 9000`). `set` validates, saves, then **re-applies**: re-renders `run_proxy.py` args, re-registers the task, rewrites env vars/hook paths as needed, and tells the user what must restart. `open` launches the file in the default editor. |
| `token-stack doctor [--fix]` | Runs the full check registry (§6); `--fix` applies the safe remediations and re-verifies. Non-zero exit if unfixed problems remain. |
| `token-stack update [--component <c>] [--version <v>]` | Re-resolves a component to the requested (or config-pinned) version: pip upgrade in the venv / re-download rtk / `uv tool upgrade semble`. Verifies after. |
| `token-stack gain` | Unified savings table: RTK's `rtk gain` summary + Headroom `/stats` (`api_requests`, compression totals). Degrades gracefully per layer when one is unreachable. |
| `token-stack uninstall [--keep-config]` | Full rollback (§7): stop+unregister task, remove env vars, surgically remove our hooks + MCP entry (restoring from structure, not blind delete), `uv tool uninstall semble`, remove PATH entry, delete installRoot. Prints what was removed. |

Global flags: `--verbose` (step-by-step trace), `--yes` (no confirmation prompts).

---

## 5. Install pipeline (ordered, each step gated on live verification)

0. **Preflight:** Windows version, `installRoot` has **no spaces** (hard error otherwise),
   disk space, write access to `%USERPROFILE%\.claude*`; detect an existing hand-built
   install (recognize the reference layout) and offer to **adopt** it — adoption means:
   rewire hooks/task/MCP entries to the managed locations, retire `ensure-stack.ps1`, and
   leave the old root untouched for manual deletion (no data migration).
1. **uv:** `where uv` → else `winget install astral-sh.uv` → else standalone installer
   (`https://astral.sh/uv/install.ps1`). Verify: `uv --version`.
2. **Python + venv:** `uv venv --python <pythonVersion> <root>\venv` (uv downloads CPython).
   Verify: `<venv>\Scripts\python.exe --version`.
3. **Headroom:** `<venv> pip install "headroom-ai[proxy]==<version>"` (the `[proxy]` extra
   is mandatory — without it the proxy crashes on missing fastapi). Render `run_proxy.py`
   from the embedded template (stdout/stderr redirected to a log file **before** importing
   headroom — pythonw has no console and the CLI banner otherwise crashes it instantly).
   Register + start the `HeadroomProxy` scheduled task. Verify: port listens and
   `/readyz` responds within the cold-load window (25–105 s; progress shown).
4. **RTK:** download pinned `rtk-x86_64-pc-windows-msvc.zip` from GitHub releases, extract
   to `<root>\rtk\`, prepend to **User** PATH (dedup-safe). Merge the PreToolUse hook into
   `%USERPROFILE%\.claude\settings.json` with matcher **`"Bash"` only** (RTK cannot wrap
   PowerShell aliases). Verify: `rtk --version` resolves from a fresh environment block.
   (Known false negative: `rtk gain` forever prints "No hook installed" for manually merged
   hooks — ignored by design; the real test is rewritten commands after a Claude restart.)
5. **Semble:** `uv tool install "semble[mcp]"` (the `[mcp]` extra is mandatory). Register in
   `%USERPROFILE%\.claude.json` → `mcpServers.semble` with the **absolute exe path**
   (`%USERPROFILE%\.local\bin\semble.exe`), `args: []` — **never** a `uvx` command (uvx
   cold-start overruns Claude's MCP handshake window and the server silently never
   connects). Verify: exe exists + a smoke `semble search` against a tiny temp repo.
6. **Routing:** per `routing` config — CLI: merge `env.ANTHROPIC_BASE_URL =
   http://127.0.0.1:<port>` into `settings.json`; Desktop: set the same at **Windows User
   scope** (the desktop harness ignores the settings.json `env` block). Detect and warn
   about conflicting session-scope values (today's BYPASSED root cause) and a leftover
   `ANTHROPIC_MODEL` pin. Disclose the trade-off: with User-scope routing, a fully dead
   proxy blocks all Claude sessions until restart (mitigated by task auto-restart + the
   status hook's recovery; rollback is one `config set routing.desktop false`).
7. **Hooks:** replace/insert the two SessionStart entries with the single
   `token-stack.exe status --hook` call (timeout 30 s).
8. **Summary:** print the evidence table + "fully quit Claude Desktop from the tray and
   relaunch" reminder (env inheritance happens at process creation).

**Hard rule encoded:** no PreToolUse hook may ever "ensure the proxy" — a 15 s hook timeout
vs a ~50 s cold load previously caused a competing-instance restart loop. Lifecycle =
Scheduled Task + SessionStart only.

---

## 6. `doctor` check registry

Each check: id, detect, explain (one honest sentence), severity, optional auto-fix.

| Id | Detects | Auto-fix |
|---|---|---|
| `routing-bypassed` | session/User/`settings.json` `ANTHROPIC_BASE_URL` disagree (the BYPASSED case) | re-set User var + settings.json; instruct tray-quit |
| `proxy-zombie` | task Running + port dead > 3 min | stop task → kill orphan `pythonw run_proxy` → start task |
| `proxy-extra-missing` | venv lacks `fastapi` (installed without `[proxy]`) | reinstall with the extra |
| `semble-uvx` | MCP command is `uvx`/relative, or exe missing | rewrite to absolute installed exe |
| `rtk-hook-missing` | PreToolUse Bash hook absent or pointing at a dead path | re-merge hook |
| `rtk-hook-powershell` | a PowerShell matcher was added for rtk | remove it (Bash-only rule) |
| `model-pin-leftover` | stray `ANTHROPIC_MODEL` env var | offer removal |
| `path-spaces` | installRoot contains spaces | error + guided reinstall to a clean root |
| `task-misconfigured` | task exists but settings drift from rendered XML | re-register |
| `stack-disabled-drift` | a layer `enabled:false` in config but still wired (or inverse) | converge wiring to config |

`doctor` ends with the same one-line summary as `status --hook` plus per-check results.

---

## 7. Config schema (`%LOCALAPPDATA%\token-stack\config.json`)

```jsonc
{
  "schemaVersion": 1,
  "installRoot": "C:\\Users\\<user>\\AppData\\Local\\token-stack",  // no spaces, validated
  "headroom": {
    "enabled": true,
    "port": 8787,
    "mode": "token",                  // token | cache | passthrough → headroom proxy args
    "version": "0.24.0",              // pinned PyPI version
    "pythonVersion": "3.12",          // what uv provisions
    "extraArgs": []                   // raw extra args appended to `headroom proxy`
  },
  "rtk": {
    "enabled": true,
    "version": "0.42.3",              // pinned GitHub release tag
    "source": "github:rtk-ai/rtk",
    "hookMatcher": "Bash"             // guarded: PowerShell rejected with explanation
  },
  "semble": { "enabled": true, "version": "latest" },   // resolved once at install; then
                                                        // frozen until an explicit `update`
  "routing": { "cli": true, "desktop": true },
  "hooks":   { "sessionStatusLine": true },
  "bootstrap": { "uvInstaller": "auto" }   // auto = detect → winget → standalone
}
```

Rules: unknown keys preserved on save (forward compat); `config set` validates type +
domain per key; disabling a component unwires it cleanly (hooks/MCP/env restored) without
touching the other layers; the file itself is created by `install` with detected defaults.

---

## 8. Error handling & safety

- **Idempotent + resumable:** every step detects "already done" and converges; a failed
  step leaves a clear actionable message (what failed, the exact command, the log path) and
  a non-zero exit; re-running `install` resumes from the failed step.
- **Backups:** any modified Claude file gets a timestamped sibling backup; `uninstall`
  offers restore-from-backup when our-section removal is ambiguous.
- **Surgical merges only:** never rewrite whole `settings.json`/`.claude.json`; only our
  keys are added/removed; user content and formatting-irrelevant structure preserved.
- **No admin:** user-scope task, HKCU env, `%LOCALAPPDATA%` files; winget per-user install.
- **Network failures:** pinned downloads verified by size/zip integrity (checksums when the
  source publishes them); PyPI/GitHub unreachable → named remediation (corporate proxy hint).
- **Compression-fidelity concern (user-facing):** documented escape hatches surface in
  `config`: `headroom.mode = cache` (zero rewriting) or `passthrough`.

---

## 9. Testing

1. **Unit (xUnit, the core value):** JSON merge/unmerge on real-world `settings.json`
   fixtures (incl. the owner's actual current file shape: plugins, marketplaces, voice,
   pre-existing hooks); config get/set validation; task-XML rendering; status-line builder
   (ROUTED/BYPASSED/cold-load truth table); doctor check predicates against fabricated
   system states (via thin probe interfaces, mocked).
2. **Integration on the dev machine:** `doctor` must reproduce the known current state
   (incl. today's BYPASSED finding); `install` over the existing hand-built stack must
   adopt it without breakage; `config set headroom.port` round-trip; `uninstall` then
   `install` back to green.
3. **Acceptance on a clean Windows VM:** copy exe → `install` → after Claude restart the
   status line reads `ROUTED`, `reqs` increases while chatting, `rtk gain` grows after Bash
   commands, `mcp__semble__search` returns chunks. This is the release gate for v1.0.

---

## 10. Distribution & docs

- `publish.ps1` → `dist/token-stack-v<semver>.zip` containing `token-stack.exe` + `README.md`.
- `README.md`: 3-line quick start (unzip → `token-stack install` → restart Claude), the
  command table, config reference, and the inherited gotchas knowledge (superseding
  `TOKEN-STACK-SETUP-PROMPT.md`, which is archived in `docs/reference/`).
- Versioning: semver; component pins move only via explicit `update`/config edit.

---

## Appendix A — Reference install (the machine this was reverse-engineered from)

- Root `D:\proxy tokens` (spaces — the motivating mistake): a 5 KB `Headroom-Proxy.exe`
  shim (gives the proxy process its display name) + `run_proxy.py` + venv
  (`headroom-ai 0.24.0`), Scheduled Task `HeadroomProxy` →
  `pythonw.exe "D:\proxy tokens\run_proxy.py"`, logon trigger, Limited run level.
- `rtk.exe` 0.42.3 at `D:\proxy tokens\rtk`, PreToolUse Bash hook in user settings.
- `semble.exe` at `%USERPROFILE%\.local\bin`, stdio MCP in `~/.claude.json`.
- Routing: `settings.json` env + User env var both `http://127.0.0.1:8787`; observed
  failure mode: a session received `https://api.anthropic.com` → status `BYPASSED`.
- Status hook `ensure-stack.ps1` (zombie recovery + honest routing) — superseded by
  `token-stack status --hook`.
