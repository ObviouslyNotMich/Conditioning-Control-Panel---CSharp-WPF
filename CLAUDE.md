# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Conditioning Control Panel (CCP) — a .NET 8 desktop conditioning/hypnosis app (flash images, mandatory videos, subliminals, screen overlays, an AI companion avatar, gamification, sessions). Two UI stacks coexist in one repo:

- **Legacy WPF head** (`ConditioningControlPanel/ConditioningControlPanel.csproj`, `net8.0-windows10.0.19041.0`) — current Windows production app, kept alive during migration.
- **Cross-platform Avalonia heads** (active work) — Avalonia UI **v12** + LibVLCSharp, targeting Windows/Linux/macOS/Android.

The active migration branch is `feat/crossplatform`. **The authoritative migration status, goals, and gotchas live in [`ConditioningControlPanel/docs/crossplatform-rebuild-plan.md`](ConditioningControlPanel/docs/crossplatform-rebuild-plan.md) (§1A is the ground-truth status snapshot).** Read it before touching the port.

> Two existing docs matter: [`AGENTS.md`](AGENTS.md) is accurate and current — trust it. The nested [`ConditioningControlPanel/CLAUDE.md`](ConditioningControlPanel/CLAUDE.md) is **stale** (WPF-only, references a `/release` command and `RELEASE_WORKFLOW.md` that don't exist). Use this file + AGENTS.md + the csproj/slnx for structure.

## Project layout

| Project | Role |
|---|---|
| `CCP.Core/CCP.Core.csproj` (`net8.0`) | **Portable core** — single source of truth for models + platform-agnostic services. Referenced by both WPF and Avalonia. |
| `CCP.Avalonia/CCP.Avalonia.csproj` | Shared Avalonia UI: views, viewmodels, Avalonia service implementations, DI registration (`ServiceCollectionExtensions.cs`). |
| `CCP.Avalonia.Desktop/` | Shared desktop DI seam overrides (`DesktopServiceCollectionExtensions.cs`) used by all three desktop heads. |
| `CCP.Avalonia.Desktop.{Windows,Linux,macOS}/` | Per-OS heads — `Program.cs` entry point + that OS's `Platform/` seam implementations. |
| `CCP.Avalonia.Android/` | Android head (mobile, post-desktop-parity). |
| `CCP.WindowsOnly/` | WPF/WinForms-specific adapters for the legacy head. |
| `ConditioningControlPanel.csproj` | Legacy WPF head. |
| `tests/CCP.Core.Tests/` | xUnit v3 tests for Core (+ Avalonia.Headless). |

**Solution files** (root is `E:\Code\Conditioning-Control-Panel`, projects live under `ConditioningControlPanel/`):
- `ConditioningControlPanel.slnx` — full solution (new XML format).
- `CCP.Desktop.slnf` — solution **filter** for the cross-platform desktop set; this is what you build for the port.
- `ConditioningControlPanel.sln` — legacy; **only includes the WPF project**. `dotnet build` on it builds neither Avalonia nor Core tests.

## Build, run, test

All commands run from the repo root unless noted.

```bash
# Cross-platform desktop solution (the migration target) — builds Core + all desktop heads
dotnet build ConditioningControlPanel/CCP.Desktop.slnf -c Debug

# Avalonia Windows head (run this for the port)
dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj

# Linux head — use the script (installs deps, copies to native build dir, smoke-runs)
./ConditioningControlPanel/build-linux.sh

# Legacy WPF app (Windows only)
dotnet run --project ConditioningControlPanel/ConditioningControlPanel.csproj

# Core unit tests (xUnit v3)
dotnet test ConditioningControlPanel/tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release
# Single test:
dotnet test ConditioningControlPanel/tests/CCP.Core.Tests/CCP.Core.Tests.csproj --filter "FullyQualifiedName~YourTestName"
```

There is **no CI / GitHub Actions** — local build + manual feature exercise is the source of truth. The bar for the port is **1:1 behavioral parity with WPF, and at least as fast** (Skia composition, async startup, off-UI-thread work); never slower or degraded on Windows. Re-exercise affected features end-to-end after pruning abstractions — the parity matrix (`docs/avalonia-ui-parity-matrix.md`) was reset to all-unverified.

VS Code `F5` builds the WPF Debug head via `.vscode/tasks.json`.

## Architecture: how the two stacks share code

1. **`CCP.Core` holds everything portable** — models, AI orchestration, gamification, sessions, settings, networking. WPF csproj uses `DefaultItemExcludes` to ignore `CCP.Core/**`, `CCP.Avalonia/**`, `tests/**`. **Do not drop shared source inside the WPF folder expecting it to be shared — put it in `CCP.Core` and reference it.**

2. **Platform seams live in `CCP.Core/Platform/`** — interfaces like `ISecretStore`, `IScreenInfo`, `IVideoSurface`, `IAudioPlayer`, `IWallpaperProvider`, `IOverlaySurface`, `IInputHook`, `IUpdateInstaller`, `ITrayIcon`. `CCP.Avalonia.ServiceCollectionExtensions.ConfigureCoreServices` registers **no-op / in-memory fallbacks**; each desktop head **overrides** the ones it can implement natively (see `DesktopServiceCollectionExtensions` and per-head `Windows*.cs` / `Platform/` files). A seam earns its place only for a real cross-platform divergence with a real implementation — not a one-line wrapper over a framework API (YAGNI / "ponytail" principle, enforced in the plan §3.3).

3. **Service access differs by stack:**
   - **WPF** reaches services via static properties on `App` (`App.Flash`, `App.Video`, `App.Audio`, `App.Patreon`, …), settings via `App.Settings.Current`.
   - **Avalonia** uses `Microsoft.Extensions.DependencyInjection` (constructor injection); composition starts in each head's `Program.cs` → `ConfigureCoreServices` → head-specific overrides.

4. **Premium gating:** `HasPremiumAccess` / `HasAiAccess` (cached ~24h) gate AI chat, window awareness, cloud sync, content packs.

## Runtime data & assets

- **Settings:** `%APPDATA%/ConditioningControlPanel/settings.json`.
- **Assets** (images/videos/packs): user-chosen folder read at runtime via `App.EffectiveAssetsPath`; default `%APPDATA%/ConditioningControlPanel/assets` with `images/` and `videos/` subfolders.
- **Crash logs:** `logs/crash.log` — check first for runtime crashes.
- **Localization:** language JSON in `CCP.Core/Localization/Languages/*.json` (and a duplicate set under `ConditioningControlPanel/Localization/Languages/`). Copied to output — edits require a rebuild to take effect.

## Hard constraints (don't break these)

- **Webcam privacy contract** (`Services/Webcam/*`): camera frames and per-frame derived data must **never** be written to disk or sent over the network — only calibration coefficients persist. Broadening camera data usage must bump `ConsentVersion`.
- **Enhancement files** (`.ccpenh.json`, `EnhancementValidator.cs`): reject NaN/Infinity, UNC paths, absolute asset paths, control chars in subliminal text, out-of-range bounds.
- **File-open CLI args** (`--play`, `--edit`): reject UNC / extended-length paths; only local rooted files with allowed media extensions.

## External endpoints (see AGENTS.md §"AI / network endpoints" for the full list)

- Cloud AI proxy: `https://codebambi-proxy.vercel.app/v2/ai/chat`
- Catalogue / auth: `https://app.cclabs.app/api/...`
- Local AI: Ollama `http://localhost:11434/api/chat` (effects gated by `AllowAiToControlEffects`)
- Updates: GitHub Releases API; install via Inno Setup silent installer (Velopack retired). Server code is in the private `CC-Labs-llc/CCP-Server` repo.

## Releases

Conventional Commits; branch prefixes `feature/` `fix/` `docs/` `refactor/`. On version bump, update **all** locations listed in [`AGENTS.md`](AGENTS.md) §"Version bumps" (every head's `<Version>`, `UpdateService.cs`, `build-installer.bat`, `installer.iss`, and the localization "vX.Y.Z is out" strings). Windows installer is built with Inno Setup 6 via `build-installer.bat`; output to `installer-output/`.
