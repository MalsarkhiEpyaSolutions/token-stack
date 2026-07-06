# TokenSaver — Phase 1: multi-provider proxy routing (GLM / Kimi / MiniMax)

**Date:** 2026-07-06
**Status:** design approved, awaiting spec review
**Supersedes/extends:** `2026-06-13-token-stack-installer-design.md` (the v1.0.x installer)

## Overview

Extend the existing token-stack (Claude Code only) so the Headroom compression
proxy can forward to a **vendor Anthropic-compatible endpoint** instead of
`api.anthropic.com`, unlocking token savings for GLM (Z.ai), Kimi (Moonshot),
and MiniMax. These three are not separate agents — they are Claude Code pointed
at a vendor endpoint via `ANTHROPIC_BASE_URL` + `ANTHROPIC_AUTH_TOKEN` +
`ANTHROPIC_MODEL`. So RTK (hook) and Semble (MCP) already work for them
unchanged; Phase 1 only adds the **proxy-target** piece.

This is "Approach A": a small `provider`/upstream concept on the existing Claude
Code path. **No general AgentAdapter abstraction** (YAGNI — there is still only
one real agent; the abstraction earns its place in Phase 2 when a genuinely
different agent such as Codex is added).

Also folds in a **surface-only rebrand** to TokenSaver.

## Top priority: zero impact on the working system

The overriding constraint (user's #1 requirement): **nothing that works today may
break.** Every change below is strictly additive and defaults to today's exact
behavior.

- New config field is optional and defaults to the current behavior.
- The default path (Claude Code → proxy → `api.anthropic.com`) sets **no new
  env, wires nothing new** — byte-identical to v1.0.4.
- Vendor handling only activates when a vendor endpoint is **positively
  detected**; otherwise every step is a no-op.
- **No path, directory, or filename changes.** `C:\token-stack`,
  `token-stack.exe`, and `%LOCALAPPDATA%\token-stack` stay exactly as they are,
  so no existing install (the user, malyasein, mjaraydeh) needs migration.
- All 115 existing tests must still pass; new tests assert the no-op default.

## Non-goals (explicitly deferred)

- General `AgentAdapter` interface / other agents (Codex, Cursor, opencode,
  Cline, Grok, Gemini, etc.) — Phase 2+.
- Configuring a vendor **from scratch** or prompting for / storing vendor API
  keys — decided: **detect & adopt only**. TokenSaver never holds a secret; the
  vendor key stays in the user's Claude Code config and passes through the proxy
  in the request's `Authorization` header.
- Gemini-format compression (proprietary Google format — Phase 2+, low ROI).
- Renaming the install **directory** `C:\token-stack` and config dir (deferred
  to a future major version — a dir rename would force whole-directory
  migration). Note: the on-disk **exe filename** IS renamed this phase (see
  Rebrand); only the directory names stay.

## Architecture

### Config schema (backward-compatible)

Add one optional field to `HeadroomConfig`:

- `upstreamUrl` (string, default `""`). Empty = use Headroom's built-in default
  (`api.anthropic.com`) = today's behavior. Non-empty = the vendor
  Anthropic-compatible endpoint the proxy forwards to.

Old `config.json` files have no `upstreamUrl` key → deserialize to `""` →
identical current behavior. (The store already tolerates missing/unknown keys —
see the existing `futureKey` round-trip test.)

`upstreamUrl` is the single source of truth (faithful to whatever the user had,
including regional China endpoints). A **display provider label** (GLM / Kimi /
MiniMax / Custom / Anthropic) is *derived* by matching the URL host against a
known-vendors table — it is cosmetic (for the status line and install report),
not stored.

### Provider table (static, `Providers.cs`)

A small static table of known vendors, used for (a) recognizing a vendor URL
during detection and (b) labeling the report. Not an abstraction — just data:

| label   | host fragment(s)                          | endpoint (for reference)              |
|---------|-------------------------------------------|---------------------------------------|
| GLM     | `z.ai`                                    | `https://api.z.ai/api/anthropic`      |
| Kimi    | `moonshot.ai`, `moonshot.cn`              | `https://api.moonshot.ai/anthropic`   |
| MiniMax | `minimax.io`, `minimaxi.com`              | `https://api.minimax.io/anthropic`    |
| Anthropic | `api.anthropic.com` (or empty)          | (Headroom default)                    |
| Custom  | any other non-Anthropic, non-proxy host   | (as detected)                         |

### Proxy target wiring (additive)

Headroom reads `ANTHROPIC_TARGET_API_URL` from its process env to choose the
upstream (default `api.anthropic.com`). The launcher template
(`run_proxy.py.tmpl`, rendered by `HeadroomComponent.RenderLauncher`) already
injects an env block before importing headroom (the `{{HF_ENV}}` mechanism). Add
a parallel injection:

- `upstreamUrl == ""` → inject **nothing** (default path unchanged).
- `upstreamUrl != ""` → inject `os.environ["ANTHROPIC_TARGET_API_URL"] = r"<url>"`.

The vendor `ANTHROPIC_AUTH_TOKEN` is **not** touched — it flows from Claude
Code's config through the proxy to the upstream automatically.

### Detection & adoption

New logic run during `install` (before routing is applied), reading the vendor
endpoint from where the user already has it:

1. Read `env.ANTHROPIC_BASE_URL` from Claude Code `settings.json`; if absent,
   read the User-scope `ANTHROPIC_BASE_URL` env var (the two documented setup
   locations).
2. Classify the value:
   - A **known/custom vendor URL** (not `api.anthropic.com`, not our local
     proxy) → set `cfg.Headroom.upstreamUrl` to it, then let routing rewrite the
     base URL to the local proxy. Vendor key + model are left untouched.
   - `api.anthropic.com` or empty → `upstreamUrl = ""` (default, current
     behavior).
   - **Already our local proxy** (`127.0.0.1:<port>`) → this is a re-install;
     **keep** the existing `cfg.Headroom.upstreamUrl` (do not reset it), so
     re-runs are idempotent and don't lose the previously adopted vendor.

**This also fixes a latent bug:** today, installing token-stack on a machine
configured for a vendor would silently redirect that vendor's traffic to
`api.anthropic.com` and break it. Detection prevents that.

### Install report / status line

- Install report line names the adopted upstream, e.g.
  `routing: Claude Code → proxy → Kimi (api.moonshot.ai)` or, default,
  `routing: Claude Code → proxy → Anthropic`.
- The one-line status/hook string appends the provider only when non-default,
  e.g. `Headroom: up (:8787, ROUTED→Kimi, reqs=N) | RTK: up | Semble: up`.
  Default stays exactly `Headroom: up (:8787, ROUTED, reqs=N) | ...`.

## Rebrand (surface only)

Changes (visible surfaces):
- GitHub repo `token-stack` → `token-saver` (GitHub 301-redirects old URLs and
  release assets).
- README / release titles / notes → TokenSaver.
- Status-line label `[token-stack]` → `[TokenSaver]` (display-only string; the
  SessionStart hook output is not parsed by anything).
- **On-disk exe filename `token-stack.exe` → `token-saver.exe`** (distributed
  zip, self-copy target, hook command, shortcuts, offline staging, `publish.ps1`
  output). Centralize the name in one constant instead of the ~10 scattered
  string literals.

**Migration for existing installs (must be seamless — the #1 constraint):**
- The SessionStart hook migrates automatically: `IsOurSessionEntry` already
  recognizes a command containing `token-stack.exe`, so on re-install/upgrade
  the old hook is matched and **replaced** by the `token-saver.exe` command.
  `token-stack.exe` MUST stay in the recognized markers as a **legacy marker**.
- The RTK hook is unaffected (it points at `rtk.exe`).
- Install recreates desktop shortcuts pointing at the new exe and removes the
  old-named ones (no orphaned/duplicate shortcuts).
- The old `installRoot\token-stack.exe` is deleted after the new exe is copied
  in (best-effort; skip if locked).
- A user who never upgrades is untouched.

Unchanged (to guarantee zero breakage):
- Install **directory** `C:\token-stack`, config dir `%LOCALAPPDATA%\token-stack`.
- Internal namespaces `TokenStack.*` (not user-visible).

Version: **v1.1.0** (backward-compatible feature bump). Release title
"TokenSaver v1.1.0".

## Testing

New unit tests (all with the existing `FakeRunner`/`FakeEnv`, no new frameworks):
- **Backward-compat:** a `config.json` with no `upstreamUrl` loads with
  `upstreamUrl == ""` and produces a launcher with **no** `ANTHROPIC_TARGET_API_URL`.
- **Default no-op:** `upstreamUrl == ""` → rendered launcher is byte-identical to
  the current template output (guard against accidental default drift).
- **Vendor launcher:** `upstreamUrl == "https://api.moonshot.ai/anthropic"` →
  launcher injects exactly that `ANTHROPIC_TARGET_API_URL`.
- **Provider label:** URL-host → label mapping (z.ai→GLM, moonshot→Kimi,
  minimax→MiniMax, api.anthropic.com/empty→Anthropic, other→Custom).
- **Detection:** settings.json with a vendor base URL → `upstreamUrl` adopted +
  routing rewrites base URL to proxy; with `api.anthropic.com`/absent → no
  change; with the local proxy already set → existing `upstreamUrl` preserved
  (idempotent re-install).
- **Hook migration:** a SessionStart hook whose command references the legacy
  `token-stack.exe` is recognized and replaced by the `token-saver.exe` command
  (assert `IsOurSessionEntry` still matches the legacy marker; extend
  `ClaudeSurgeonTests` which currently hard-code `token-stack.exe`).
- Full existing suite (115) stays green (update the tests that assert the old
  exe name to the new one, keeping one that proves the legacy marker still
  migrates).

Manual verification (Windows sandbox, per the existing test checklist): a Claude
Code + Kimi setup → `install` → status shows `ROUTED→Kimi`; a plain Claude Code
setup → unchanged behavior.

## Files touched (estimate)

- `Config/StackConfig.cs` — add `HeadroomConfig.UpstreamUrl`.
- `Components/Providers.cs` — **new**, static vendor table + URL→label + classify.
- `Components/HeadroomComponent.cs` — `RenderLauncher` injects
  `ANTHROPIC_TARGET_API_URL` when `upstreamUrl` set.
- `Resources/run_proxy.py.tmpl` — add the target-env placeholder.
- `Install/InstallPipeline.cs` — detection/adoption step (before routing).
- `Status/StatusLine.cs` (+ `StatusProbe`/`StackStatus` as needed) — provider
  suffix in the one-liner.
- **Exe rename:** a single `ExeName` constant consumed by `InstallPipeline`
  (self-copy target + shortcut/hook paths, ~3 sites), `OfflinePacker`,
  `ToggleCommands`, and `ClaudeSurgeon` (add `token-saver.exe` marker, keep
  `token-stack.exe` legacy); delete old exe after copy.
- `publish.ps1` — output `token-saver.exe`; `PackCommand` help text.
- `README.md` — rebrand + a short "GLM/Kimi/MiniMax" section.
- Tests: `HeadroomTests`, `ConfigTests`, `ClaudeSurgeonTests` (exe name +
  legacy-migration), a new `ProvidersTests` / detection test.

## Rollout

1. Land the feature + tests on `master` (paths unchanged → safe for existing
   installs).
2. Rebrand surfaces; rename the GitHub repo to `token-saver`.
3. Release **v1.1.0** (online + spliced offline zip), mark latest.
4. v1.0.4 remains for anyone mid-download; no forced migration.
