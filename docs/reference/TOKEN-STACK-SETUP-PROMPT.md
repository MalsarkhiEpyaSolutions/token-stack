# PROMPT — Set up the 3-layer Claude Code token-saving stack on Windows

> Copy everything below this line and paste it as the first message to Claude Code
> (or any capable AI agent) on the target Windows machine.

---

You are going to install and wire a **3-layer token-optimization stack** for Claude Code on this Windows machine. The three layers are complementary (they intercept at different points, they never fight, and their savings don't double-count):

1. **RTK** — a Rust binary that filters Bash command output at execution time (60–90% off `ls`/`find`/`git`; `grep`/`cat` pass through).
2. **Semble** — a semantic code-search MCP server that replaces grep+read-whole-files exploration with top-k relevant chunks (~70–76% smaller).
3. **Headroom** — a local reverse proxy on `127.0.0.1:8787` that compresses every API request payload (the conversation history that is re-sent on EVERY request). Responses stream back untouched.

Measured combined savings on real workloads: **~74–95%** depending on command mix. Work step by step. Verify each layer with live evidence before moving to the next. Never claim success without running the verification command and seeing the output.

## Prerequisites (check first, install what's missing)

- Windows 10/11 with PowerShell.
- Python 3.11+ on PATH.
- `uv` (for Semble): `winget install astral-sh.uv` or https://docs.astral.sh/uv/
- Git Bash (ships with Git for Windows) — RTK filtering works in the Bash tool, not PowerShell.
- Claude Code (CLI and/or Desktop app).
- Choose an install root **WITHOUT spaces in the path**, e.g. `D:\token-stack`. (A reference install used `D:\proxy tokens` and every hook command needed painful nested quoting — avoid that.) Call it `<ROOT>` below.

## Step 1 — Headroom proxy (the API-payload compressor)

1. Create a dedicated venv:
   ```powershell
   python -m venv "<ROOT>\venv"
   ```
2. Install **with the `[proxy]` extra** — REQUIRED. Without it the proxy crashes with `ModuleNotFoundError: fastapi` (base `headroom-ai` does not pull fastapi/uvicorn):
   ```powershell
   & "<ROOT>\venv\Scripts\pip.exe" install "headroom-ai[proxy]"
   ```
   (Reference version: 0.24.0. No torch needed — it uses onnxruntime; a `[transformers] PyTorch was not found` warning at startup is harmless.)
3. Create the launcher `<ROOT>\run_proxy.py`. **CRITICAL gotcha:** the task will run under `pythonw.exe`, which has NO console, so `sys.stdout`/`sys.stderr` are `None` — and the Headroom CLI prints a startup banner, which makes it **crash instantly with exit code 1 and nothing logged**. Redirect BEFORE importing headroom:
   ```python
   import sys, os
   log = open(r"<ROOT>\proxy-task.out.log", "a", buffering=1, encoding="utf-8")
   sys.stdout = log
   sys.stderr = log
   sys.stdin = open(os.devnull)
   from headroom.cli import main   # headroom has no __main__.py; entry point is headroom.cli:main
   sys.argv = ["headroom", "proxy"]
   main()
   ```
4. Register a Scheduled Task so the proxy runs 24/7, survives reboot, and is decoupled from Claude's lifecycle:
   ```powershell
   $action   = New-ScheduledTaskAction -Execute "<ROOT>\venv\Scripts\pythonw.exe" -Argument '"<ROOT>\run_proxy.py"'
   $trigger  = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
   $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) `
               -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) `
               -MultipleInstances IgnoreNew -Hidden
   Register-ScheduledTask -TaskName HeadroomProxy -Action $action -Trigger $trigger -Settings $settings
   Start-ScheduledTask -TaskName HeadroomProxy
   ```
5. Verify (first start cold-loads onnx compressor models — **expect 25–105 seconds** before the port binds):
   ```powershell
   Get-NetTCPConnection -LocalPort 8787 -State Listen        # must show a listener
   Invoke-WebRequest http://127.0.0.1:8787/readyz            # must report ready
   ```
6. ⚠️ **NEVER add a PreToolUse hook that "ensures" the proxy is running.** A 15-second hook timeout vs a ~46-second cold load caused a documented restart-loop: every tool call spawned a competing proxy instance that killed the previous one, and the port never bound. The Scheduled Task is the ONLY lifecycle manager. A light SessionStart check (step 5 below) is fine.

## Step 2 — RTK (the command-output filter)

1. Download the latest `rtk-x86_64-pc-windows-msvc.zip` from https://github.com/rtk-ai/rtk/releases and extract `rtk.exe` to `<ROOT>\rtk\`.
2. Add `<ROOT>\rtk` to the **User** PATH and verify a fresh shell resolves `rtk --version`.
3. Wire the hook by **manually merging** into `%USERPROFILE%\.claude\settings.json` (don't rely on `rtk init -g` — in non-interactive runs it defaults to NOT patching settings; it only prints instructions):
   ```json
   "hooks": {
     "PreToolUse": [
       {
         "matcher": "Bash",
         "hooks": [
           { "type": "command", "command": "\"<ROOT>\\rtk\\rtk.exe\" hook claude" }
         ]
       }
     ]
   }
   ```
   ⚠️ The matcher must be `"Bash"` ONLY — never add PowerShell. RTK cannot wrap PowerShell aliases (`ls`, `cat`, `grep` are not real exes there); `rtk ls` fails in PowerShell but works in the Bash tool / Git Bash.
   ⚠️ If the agent doing this setup is itself blocked from editing `settings.json` hooks (Claude's permission classifier may refuse hook self-modification even with user approval), print the exact JSON and ask the human to paste it manually.
4. Verify: in a Bash shell run `rtk ls -la <some dir>` (output should be compact), then `rtk gain` (should show savings).
   ⚠️ Known false negative: `rtk gain` prints `[warn] No hook installed — run rtk init -g` forever when the hook was merged manually. **Ignore it** — the hook is judged by whether tool calls get rewritten, which you can only see after restarting Claude (hooks load at session start).

## Step 3 — Semble (the semantic code-search MCP)

1. Install as a uv tool **with the `[mcp]` extra** — REQUIRED (without it the exe physically cannot serve MCP):
   ```powershell
   uv tool install "semble[mcp]"
   ```
2. Locate the exe — usually `%USERPROFILE%\.local\bin\semble.exe`. ⚠️ That directory is often NOT on PATH, so the MCP config below MUST use the full absolute path.
3. Register in `%USERPROFILE%\.claude.json` (top-level `mcpServers`):
   ```json
   "mcpServers": {
     "semble": {
       "type": "stdio",
       "command": "C:\\Users\\<YOU>\\.local\\bin\\semble.exe",
       "args": [],
       "env": {}
     }
   }
   ```
   ⚠️ Do NOT use `uvx --from "semble[mcp]" semble` as the MCP command. uvx cold-start (resolving + downloading the package) overruns Claude's MCP handshake window, and the server **silently never connects** — config looks fine, tools never appear. The installed exe has zero cold-start.
4. Verify from CLI (first run builds the index, ~seconds to a minute):
   ```bash
   PYTHONIOENCODING=utf-8 /c/Users/<YOU>/.local/bin/semble.exe search "where is authentication handled" <repo path>
   ```
   (`PYTHONIOENCODING=utf-8` avoids a cosmetic cp1252 crash in some commands.)
   After a full Claude restart, confirm the `mcp__semble__search` tool actually registers and returns chunks — **config presence is NOT a live handshake.**

## Step 4 — Route Claude's API traffic through Headroom

- **Terminal `claude` sessions:** add to `%USERPROFILE%\.claude\settings.json`:
  ```json
  "env": { "ANTHROPIC_BASE_URL": "http://127.0.0.1:8787" }
  ```
- **Claude DESKTOP sessions:** the desktop harness IGNORES the settings.json `env` block (it's a CLI-only feature) and injects `https://api.anthropic.com` when nothing is set in its inherited environment. Fix: set the variable at **Windows User scope** so the desktop app inherits it at launch:
  ```powershell
  [Environment]::SetEnvironmentVariable('ANTHROPIC_BASE_URL','http://127.0.0.1:8787','User')
  ```
  Then **fully quit the Desktop app (system tray icon → Quit — closing the window is not enough)** and relaunch. Environment inheritance happens at process creation.
- ⚠️ Consequence to disclose to the user: with the User-scope variable set, if the proxy dies completely, ALL Claude sessions lose API access until `Start-ScheduledTask HeadroomProxy`. The task's auto-restart makes this rare.
- Rollback at any time: `reg delete HKCU\Environment /v ANTHROPIC_BASE_URL /f` (plus remove the settings.json env line).

## Step 5 — Honest per-session status hook

Create `%USERPROFILE%\.claude\hooks\ensure-stack.ps1`. Two design rules learned the hard way: (a) report **actual routing** (the hook inherits the session's env, so it can see what base URL the API client really got — config presence proves nothing); (b) never throw, print exactly one line.

```powershell
# SessionStart hook: ensure the 3 token-optimization layers are up.
$ErrorActionPreference = 'SilentlyContinue'

# 1) Headroom: start the scheduled task if it isn't already running.
$task = Get-ScheduledTask -TaskName 'HeadroomProxy'
if ($task -and $task.State -ne 'Running') { Start-ScheduledTask -TaskName 'HeadroomProxy' | Out-Null }
$hdRunning = ((Get-ScheduledTask -TaskName 'HeadroomProxy').State -eq 'Running')
$port = [bool](Get-NetTCPConnection -LocalPort 8787 -State Listen -ErrorAction SilentlyContinue)

# 1c) Zombie recovery: task Running but port dead well past the cold-load window.
#     Real failure mode: asyncio proactor accept-loop dies on WinError 64/10054 under
#     concurrent probes; process stays alive, listener gone, task still "Running".
if ($hdRunning -and -not $port) {
  $started = (Get-ScheduledTaskInfo -TaskName 'HeadroomProxy').LastRunTime
  if ($started -and ((Get-Date) - $started).TotalMinutes -gt 3) {
    Stop-ScheduledTask -TaskName 'HeadroomProxy'
    Get-CimInstance Win32_Process -Filter "Name='pythonw.exe'" |
      Where-Object { $_.CommandLine -match 'run_proxy|headroom' } |
      ForEach-Object { Stop-Process -Id $_.ProcessId -Force -Confirm:$false }
    Start-Sleep -Seconds 2
    Start-ScheduledTask -TaskName 'HeadroomProxy' | Out-Null
  }
}

# 1b) Is THIS session actually routed through the proxy?
$routed = ($env:ANTHROPIC_BASE_URL -match '(127\.0\.0\.1|localhost):8787')
$reqs = $null
try { $reqs = (Invoke-RestMethod 'http://127.0.0.1:8787/stats' -TimeoutSec 3).summary.api_requests } catch {}

# 2) RTK present on PATH?
$rtk = [bool](Get-Command rtk -ErrorAction SilentlyContinue)

# 3) Semble: registered MCP + exe exists? (still only config-presence; the real test is the tool registering)
$mcp = $false
try {
  $j = Get-Content "$env:USERPROFILE\.claude.json" -Raw | ConvertFrom-Json
  $mcp = ($null -ne $j.mcpServers.semble) -and (Test-Path $j.mcpServers.semble.command)
} catch {}

$h = if ($hdRunning) {
  if ($port) {
    $route = if ($routed) { 'ROUTED' } else { 'BYPASSED' }
    $rq = if ($null -ne $reqs) { ", reqs=$reqs" } else { '' }
    "up (:8787, $route$rq)"
  } else { 'starting (cold-load ~50s)' }
} else { 'DOWN' }
$r = if ($rtk) { 'up' } else { 'MISSING' }
$s = if ($mcp) { 'up (MCP)' } else { 'MISSING' }
Write-Output "[token-stack] Headroom: $h | RTK: $r | Semble: $s"
```

Wire it in `settings.json`:
```json
"SessionStart": [
  {
    "matcher": "startup|resume",
    "hooks": [
      {
        "type": "command",
        "command": "powershell -NoProfile -ExecutionPolicy Bypass -File \"C:\\Users\\<YOU>\\.claude\\hooks\\ensure-stack.ps1\"",
        "timeout": 30
      }
    ]
  }
]
```

## Step 6 — End-to-end verification (evidence, not vibes)

After a full restart of Claude (Desktop: tray → Quit → relaunch):

1. The session status line must read `[token-stack] Headroom: up (:8787, ROUTED, reqs=N) | RTK: up | Semble: up (MCP)`.
2. `reqs` must **increase** as you chat — that is the only real proof traffic flows through the proxy. (`/stats` can take ~20s to respond; that's normal.)
3. Run 2–3 Bash commands (`ls`, `find`), then `rtk gain` — savings counter must grow.
4. Ask the model a code question and confirm an `mcp__semble__search` call returns chunks.
5. `GET http://127.0.0.1:8787/stats` → `summary.api_requests > 0` and compression numbers accumulating.

If the status line says `BYPASSED` after a TRUE full restart, this desktop build force-overrides the env var: investigate the desktop app's own config storage for an env/baseURL setting, or fall back to terminal `claude` for routed sessions.

## Gotchas recap (read this before debugging anything)

| Symptom | Cause | Fix |
|---|---|---|
| Proxy: `ModuleNotFoundError: fastapi` | missing `[proxy]` extra | reinstall `headroom-ai[proxy]` |
| Task exits code 1 instantly, no log | pythonw has no stdout; CLI banner crashes | redirect stdout/stderr in `run_proxy.py` BEFORE importing headroom |
| Proxy never binds, keeps restarting | short-timeout ensure hook spawning competing instances during 25–105s cold load | delete the ensure hook; Scheduled Task only |
| Semble tools never appear despite config | uvx cold-start overruns MCP handshake; or missing `[mcp]` extra; or `.local\bin` not on PATH | installed exe + full path + `args: []` |
| Desktop traffic ignores the proxy | desktop ignores settings.json `env`; injects api.anthropic.com as default | User-scope env var + full tray quit + relaunch |
| `rtk gain`: "No hook installed" | false negative for manually merged hooks | ignore; verify by seeing commands rewritten |
| `rtk ls` fails | running in PowerShell | RTK is Bash-only; matcher `"Bash"` |
| Hook command breaks | spaces in install path | nested quotes, or reinstall to a space-free path |
| Agent can't edit settings.json hooks | permission classifier blocks hook self-modification | print JSON, human pastes manually |
| Process alive + task "Running" but port 8787 dead (zombie) | asyncio proactor accept-loop dies on WinError 64/10054 under concurrent connection resets | the hook's zombie-recovery block (1c) restarts it; manual: Stop-ScheduledTask → kill pythonw running run_proxy → Start-ScheduledTask |
| Worried about compression fidelity | token mode rewrites OLD turns only; newest turn + JSON preserved; session transcript never modified (no generational loss) | if ever suspicious: `--mode cache` (zero rewriting) or `--no-optimize` (passthrough) |

## Ops cheat-sheet

- Savings dashboard: `GET http://127.0.0.1:8787/stats` (slow, ~20s) · RTK: `rtk gain`
- Restart proxy: `Start-ScheduledTask HeadroomProxy` · logs: `<ROOT>\proxy-task.out.log`, `~/.headroom/logs/`
- Full rollback: delete the User env var (`reg delete HKCU\Environment /v ANTHROPIC_BASE_URL /f`), remove the settings.json `env` line + hooks, `Unregister-ScheduledTask HeadroomProxy`, `uv tool uninstall semble`, delete `<ROOT>`.
