# token-stack

One executable that installs and manages the 3-layer Claude Code token-optimization
stack on Windows: **Headroom** (API-payload compression proxy), **RTK** (Bash
command-output filter), **Semble** (semantic code-search MCP). Measured combined
savings: ~74-95% depending on workload.

## Quick start — online (any Windows 10/11 machine, no admin)

1. Unzip anywhere, open a terminal next to `token-stack.exe`.
2. `.\token-stack.exe install`   (downloads pinned components; Headroom cold-loads 25-105s)
3. Fully quit Claude (Desktop: tray icon → Quit) and relaunch.
4. Every session now starts with:
   `[token-stack] Headroom: up (:8787, ROUTED, reqs=N) | RTK: up | Semble: up (MCP)`

The installer copies itself to `C:\token-stack\token-stack.exe` — you can delete the
unzipped download afterwards.

## Offline / air-gapped machines

The same `install` works with **zero network** when an offline bundle is present — it
**auto-detects** a `vendor\` folder next to `token-stack.exe`.

**Build the bundle once on an online machine** (after a normal online install, so rtk + the
HuggingFace models exist locally):
```powershell
.\token-stack.exe pack --out token-stack-offline-v1.0.0.zip   # ~350 MB
```
The bundle contains `uv.exe` + a portable Python + a pip wheelhouse + `rtk.exe` + the
~222 MB HuggingFace model cache Headroom needs at runtime.

**On the air-gapped machine:** copy the zip → unzip → `.\token-stack.exe install`. It detects
`vendor\`, installs everything from the bundle, and pins HuggingFace to offline mode
(`HF_HUB_OFFLINE=1`) so the proxy never reaches the internet. Force with `--offline` /
`--online` if needed.

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
