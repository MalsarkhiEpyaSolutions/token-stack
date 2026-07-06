# TokenSaver

One executable that installs and manages the 3-layer Claude Code token-optimization
stack on Windows: **Headroom** (API-payload compression proxy), **RTK** (Bash
command-output filter), **Semble** (semantic code-search MCP). Measured combined
savings: ~74-95% depending on workload. Also saves tokens on Claude Code pointed at
GLM (Z.ai), Kimi (Moonshot), or MiniMax — see below.

## Quick start — online (any Windows 10/11 machine, no admin)

1. Download the latest `token-saver-v*.zip` from
   [Releases](https://github.com/MalsarkhiEpyaSolutions/token-saver/releases),
   unzip anywhere, open a terminal next to `token-saver.exe`.
   *The exe is not code-signed, so SmartScreen may warn on first run — click
   "More info" → "Run anyway" (or `Unblock-File .\token-saver.exe`).*
2. `.\token-saver.exe install`   (downloads pinned components; Headroom cold-loads 25-105s)
3. Fully quit Claude (Desktop: tray icon → Quit) and relaunch.
4. Every session now starts with:
   `[TokenSaver] Headroom: up (:8787, ROUTED, reqs=N) | RTK: up | Semble: up (MCP)`

The installer copies itself to `C:\token-stack\token-saver.exe` — you can delete the
unzipped download afterwards. (The install directory stays `C:\token-stack` for
seamless upgrades from older versions.)

## GLM / Kimi / MiniMax / OpenRouter (Claude Code with a vendor endpoint)

If your Claude Code is already pointed at a vendor Anthropic-compatible endpoint
(`ANTHROPIC_BASE_URL` = `api.z.ai/api/anthropic`, `api.moonshot.ai/anthropic`,
`api.minimax.io/anthropic`, or `openrouter.ai/api`), `install` **detects and adopts
it**: it inserts the Headroom proxy in front and forwards to that vendor. Your vendor
API key and model are never touched — they pass straight through. The status line
then reads `ROUTED→Kimi` (or GLM / MiniMax / OpenRouter). Plain Claude Code is
unaffected.

**Don't have the vendor set up yet?** Run `.\token-saver.exe setup` — an interactive
screen that picks the provider, takes your API key (masked), and writes it into
Claude's `settings.json` (your key lives there, never in TokenSaver). Then
`.\token-saver.exe install` routes it through the proxy.

## Offline / air-gapped machines

The same `install` works with **zero network** when an offline bundle is present — it
**auto-detects** a `vendor\` folder next to `token-saver.exe`.

**Build the bundle once on an online machine** (after a normal online install, so rtk + the
HuggingFace models exist locally):
```powershell
.\token-saver.exe pack --out token-saver-offline-v1.1.0.zip   # ~550 MB
```
The bundle contains `uv.exe` + a portable Python + a pip **wheelhouse** (wheels are *built*
on the online machine via `pip wheel`, so the air-gapped install is pure-wheel — some
headroom-ai versions ship sdist-only and can't be built offline) + `rtk.exe` + the
HuggingFace model cache Headroom needs at runtime.

**On the air-gapped machine:** copy the zip → unzip → `.\token-saver.exe install`. It detects
`vendor\`, installs everything from the bundle, and pins HuggingFace to offline mode
(`HF_HUB_OFFLINE=1`) so the proxy never reaches the internet. Force with `--offline` /
`--online` if needed.

## Turn it on/off (one press)

The whole stack — or any single layer — pauses and resumes instantly (no reinstall):

```powershell
.\token-saver.exe off              # whole stack off  → Claude talks DIRECT to Anthropic (still works)
.\token-saver.exe on               # whole stack back on
.\token-saver.exe toggle           # flip whichever way
.\token-saver.exe off rtk          # just one layer: headroom | rtk | semble
```

**OFF is safe:** it removes the routing too, so Claude keeps working directly against
`api.anthropic.com` — it doesn't just kill the proxy and strand you. OFF also disables
the proxy's logon autostart, so it stays off across reboots; ON re-enables it. Restart
Claude after toggling for it to take effect.

**The buttons:** `install` drops these on your Desktop (re-create anytime with
`.\token-saver.exe shortcut`):
- **Token Stack** (loose icon) — double-click toggles the *whole* stack on/off.
- **Token Stack Controls** (folder) — one toggle per layer: *Headroom*, *RTK*, *Semble* — so you
  can flip any single feature with a click.

Each shows a popup confirming the new state.

## Commands

| Command | What |
|---|---|
| `install` | full install/repair (idempotent; `--component headroom\|rtk\|semble`; `--offline`/`--online`) |
| `setup` | interactive: add a vendor API key (GLM/Kimi/MiniMax/OpenRouter) into Claude's settings |
| `on` / `off` / `toggle` `[layer]` | pause/resume whole stack or one layer (no reinstall) |
| `shortcut` | (re)create the desktop toggle button |
| `pack` | build an offline bundle (run on an online machine) |
| `status` | live table (shows OFF for paused layers); `--hook` one-line; `--json` |
| `start` / `stop` / `restart` | proxy lifecycle (restart = zombie recovery) |
| `config list/get/set/open` | edit `%LOCALAPPDATA%\token-stack\config.json` |
| `doctor [--fix]` | detect + repair the known failure modes |
| `update --component X [--version v]` | move a component pin |
| `gain` | unified savings report |
| `uninstall [--keep-config] [-y]` | full rollback (Claude-file backups kept) |

## Config keys (full control)

`installRoot` (no spaces!) · `headroom.enabled/port/mode(token|cache|passthrough)/version/pythonVersion/extraArgs/upstreamUrl(vendor endpoint, auto-detected)`
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
