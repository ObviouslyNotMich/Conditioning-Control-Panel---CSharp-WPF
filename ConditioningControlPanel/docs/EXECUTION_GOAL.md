# Execution Goal — Finish the Avalonia v12 Cross-Platform Rebuild

> **Run with:**
> ```
> /goal Read @ConditioningControlPanel/docs/EXECUTION_GOAL.md and execute it autonomously until its "DEFINITION OF DONE" fully holds
> ```
> `/goal` keeps the session working until that condition is true (it blocks stopping and starts immediately). Stop
> early with `/goal clear`. — One line, then walk away.
>
> **Single entry point.** Reference only this file to execute. Everything you need is inline. The `(plan §X)`
> pointers are *optional* deeper detail in `docs/crossplatform-rebuild-plan.md` — you don't have to open it. You DO
> open the two **living trackers** and the reference images as you work (see "Files you work from" below) — those
> are data, not rules.

---

## GOAL

Drive the cross-platform rebuild of **Conditioning Control Panel** to completion. End state: the **Avalonia UI v12**
app at **full parity with today's Windows WPF app** — it *does* everything the WPF app does, on the cross-platform
stack (LibVLCSharp.Avalonia video, cross-platform audio/secret/browser/tray seams; no WPF-only paths in shared UI).
Work autonomously until the Definition of Done holds.

## THE BAR (non-negotiable)

- **Every feature ports 1:1 — identical behavior to the WPF app — AND should be smoother and faster.** The WPF
  baseline is the **floor, not the ceiling**: every function feels at least as snappy on Avalonia/Skia, ideally
  better. Never slower, never janky.
- **No permanent stubs, no simplified/approximate versions, no degraded behavior on Windows.** If WPF does X, the
  port does X, at least as fast (beat it via Skia composition, async startup, pooling, off-UI-thread work).
- "Better" = *faster/smoother*, not *changed*. Match WPF behavior first, then beat its performance.
- Only acceptable degradation: Linux/macOS/Android features that are inherently platform-limited (global hooks,
  wallpaper, etc.) — gate those; **never degrade on Windows**.

## PERFORMANCE IS THE POINT — lightweight & fast on the lowest-end systems

This is the **#1 reason the rebuild exists.** Moving to Avalonia v12 and its Skia/LibVLC media stack was chosen
specifically to make CCP **lighter and faster** and to run **smoothly on low-end / older machines**. So treat a low
CPU/GPU/RAM footprint and zero lag as a **hard requirement, equal in weight to functional parity** — not a
nice-to-have. For every feature: minimize allocations and per-frame work, keep decode/IO off the UI thread, pool and
reuse surfaces, gate concurrent heavy effects, and prefer the cheapest approach that still matches WPF behavior. If a
port is heavier, laggier, or needs more horsepower than WPF, that is a **defect even if it "works."** Optimize for the
weakest target machine, not your dev box.

## BUILD PRINCIPLE — lazy senior dev / YAGNI ("ponytail")

Build the **simplest thing that works**. Framework/stdlib first, no unrequested abstractions, delete over add,
shortest working diff. **A platform seam earns its place only with a real cross-platform divergence + a real
implementation — never a one-line wrapper over a framework API.** Use `Dispatcher.UIThread`, `DispatcherTimer`,
`ILogger<T>`, LibVLCSharp's own native discovery, and `AssetLoader` directly (the ponytail-audit already deleted
the `IUiDispatcher`/`IScheduler`/`IAppLogger`/`LibVLCNativeDiscovery`/frame-source/mobile wrappers — don't recreate
them; record is `docs/avalonia-ponytail-audit-queue.md`). **Keep pruning unneeded code as you go — it makes the app
faster — but each prune is a refactor:** build, then re-exercise the affected features, since a removed wrapper may
have been load-bearing.

## ACTIVELY HUNT FOR FASTER SOLUTIONS — research the web, adopt better tools

This is a **standing, proactive behavior, not a last resort.** For **every** feature you port — even one that
already "works" — actively ask "is there a faster / lighter way to do this?" and **search the web** to find out
(Avalonia docs, LibVLCSharp, SkiaSharp, GitHub issues/discussions, release notes, benchmarks, blog posts — verified
doc links in plan §23). You are **not limited to what you already know**; default to checking for the idiomatic,
modern, performant approach before settling on your first idea, and again whenever a path feels laggy or heavy. Don't
ship a slow hand-rolled version when a clean, fast one is documented.

**Actively look for, and adopt, new libraries that make the app faster or lighter** (lower CPU/GPU/RAM, less lag) or
that replace a heavy hand-rolled path — this is **encouraged, expected, not a deviation**. Guardrails: prefer
well-maintained, cross-platform, permissively-licensed, actively-released packages; pin the version; and keep the
dependency set **lean** — a lib must *earn its weight* (same ponytail bar: don't pull a dependency to save a few
lines, but do adopt one that removes a slow/fragile path or measurably cuts the footprint). Never regress Windows
behavior. Record each new/changed dependency **and the reason** (what it speeds up or replaces, with a before/after
number where measurable) in the task board.

## Files you work from

| File | What it is | How to use it |
|---|---|---|
| `docs/avalonia-migration-task-board.md` | **Live work queue.** Has the Active Claims Ledger, the per-lane history, and **"Known Functional Gaps"** (the authoritative not-done list: reported #1–#6 + audit groups A–M). | Claim work here (ledger row, commit first). Start from Known Functional Gaps. Log new gaps you find. |
| `docs/avalonia-ui-parity-matrix.md` | **Per-screen checklist, RESET 2026-06-23 to all-unverified.** ~127 items, all `[ ]`. | Mark `[x]` ONLY after you exercise the item in the running app. Don't trust old marks (pruning/fixes made them stale). |
| `img state/` (repo root) | **Reference screenshots.** 5 dashboard "good view" images, one per theme, + `bad view*.png` (anti-patterns). | Compare your port to the reference for the *active theme*. Filenames have spaces — quote them. |
| `docs/crossplatform-rebuild-plan.md` | Deep detail behind the `(plan §X)` pointers. | Optional. Open only if a pointer's inline summary isn't enough. |

Theme → reference image: CCP Default = `default good view.jpg` · Sissy Hypno = `good view.png` ·
Bambi = `bambi sleep good view.jpg` · Droneification = `drone good view.jpg` · Circe Lock = `circe lock good view.jpg`.

## Commands (run from the `ConditioningControlPanel/` dir)

- Build (desktop): `dotnet build CCP.Desktop.slnf -clp:ErrorsOnly`  → must be 0 errors.
- Core tests: `dotnet test tests/CCP.Core.Tests/CCP.Core.Tests.csproj`  → must pass.
- Run Avalonia (Windows): `dotnet run --project CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj`
- Run legacy WPF (source of truth): `dotnet run --project ConditioningControlPanel.csproj`
- Render smoke test: `dotnet run --project CCP.Avalonia.Desktop.Windows -- --smoke-test` (catches crashes / raw
  `{loc:Str}` / placeholder tabs — it does **not** prove behavior).
- Add loc keys: append to `tools/new-localization-keys.json`, then `python tools/merge-localization-keys.py`.

---

## STEP 0 — reconcile the `main` 6.1.7 update (BEFORE porting anything new)

`main` **6.1.7 is merged** (branch caught up); the on-branch WPF 6.1.7 code now needs porting into Core/Avalonia.
⚠️ The merge **dropped** main's `Models/Quest.cs` / `Models/AppSettings.cs` edits (modify/delete → kept deleted —
confirmed not in Core), so the quest-pool refresh + new settings must be **re-applied to `CCP.Core/Models/` by
hand**. Full grounded backlog is plan **§19.3**. Bucketed:

- **Models → `CCP.Core/Models`:** `Quest.cs` (+114, quest-pool refresh: 20 free + 20 patron), `AppSettings.cs` (+7, incl. Story re-lock / FIELD_PACE).
- **Portable services → `CCP.Core`:** Quest pool (`QuestService` +88, `QuestDefinitionService`), `AchievementService` (+17), `UpdateService` rework (+89), `KeywordTriggerService`, `ChaosArt`/`ChaosTuning`. **Auth browser-launch fallback** — new `Helpers/BrowserLauncher.cs` (+101) + Discord/Patreon/SubscribeStar service deltas (graceful OAuth launch — **ties to the dead-auth gap A**; do via `IBrowserHost`/system-open cross-platform).
- **UI → `CCP.Avalonia`:** **Chaos "Down the Rabbit Hole" main menu** — biggest item: `ChaosHubWindow.xaml` (+321) + `.xaml.cs` (+1036) → port into the existing `CCP.Avalonia/Chaos/ChaosHubWindow.*` (neon logo, How-to-Play tutorial overlay, menu soundtrack, pink fog, intro reveal, FX crossfade). `ChaosBackdropService` (+405, authored glint FX on depth backdrops). **Subliminal double-flash fixes** (`SubliminalService` +47 + `SubliminalFeatureControl`). **Avatar focus-steal fix** (`AvatarTubeWindow.xaml`). `BubbleService` FIELD_PACE knob. `LabTabView.xaml` (+108). `MainWindow.UiUpdates`/`MainWindow.xaml`.
- **Assets → add as `AvaloniaResource`/Content:** ~70 new Chaos assets (`assets/Chaos/backdrops/*`, `menu*.png`, `menu_fx.json`/`backdrop_fx.json`, `sounds/chaos/menu_theme.mp3`) + 20 quest art PNGs in `Resources/quests/` (now bundled in-app, no CDN).
- **Localization → auto-synced** (Core links the language JSON; +2 keys each). No manual port — just reference new keys in ported views.

Steps: (1) merge is **done** — re-apply the dropped `Quest.cs`/`AppSettings.cs` deltas to Core by hand; (2) port the
buckets above; (3) refresh §19.3 to done/remaining; (4) **re-verify** anything the WPF fixes touched (subliminal,
avatar focus, bubble pace) against the new WPF behavior — those existing matrix rows may now be wrong.

## WORKFLOW

Act as the **orchestrator**. Own the chokepoints yourself: `CCP.Avalonia/ServiceCollectionExtensions.cs` (DI),
`App.axaml`, the `MainWindow` shell, all `*.csproj`, and localization merges. Do the shell/infra backbone first;
everything else hangs off it. You MAY spawn worktree-isolated sub-agents per lane (plan §20) if available; else run
lanes sequentially.

Lanes map to the original project: one `MainWindow/MainWindow.<Feature>.cs` partial → one
`Views/Tabs/<Feature>TabView.*` + `ViewModels/Tabs/<Feature>TabViewModel.cs` (port its `Services/<Area>` logic into
Core in the same lane). Sub-split the oversized Chaos lane.

**PER-LANE LOOP:** claim (ledger row, commit first) → targeted reads of just that subtree → port → build
(`-clp:ErrorsOnly`) + Core tests → **functionally verify (exercise it)** → **visually verify** (screenshot vs the
active-theme reference + the same tab in the running WPF app) → prune anything needless while you're in the file →
update task board + flip the matrix item to `[x]` → commit → **compact context**.

**Compact aggressively:** after every finished item, after each green build, after any large file read, and at
~50–60% of the window. Keep only the trackers + the outcome; never carry one lane's source into the next.

## FUNCTIONAL PARITY — exercise it, don't just render it

A clean build + correct screenshot does **not** mean it works. The first port shipped inert UI (START did nothing,
avatar inert, overlays blocked input). Wire every control to a live `ICommand` → Core/platform service (no
stubs/`NotImplemented`), then **run the feature and confirm it does what WPF does**. Start from the task board's
**Known Functional Gaps** — includes high-impact ones like **account login being entirely no-op (premium/Patreon
gating dead)**, Chaos run economy placeholder, content packs, feature-card editors, webcam tracking. Find more with
`grep -rinE "TODO|stub|not ported|not wired|placeholder|NotImplemented|No-?op" CCP.Avalonia --include=*.cs` and by
exercising every feature — the markers are a floor, not a ceiling.

## VISUAL PARITY

Screenshot each ported tab and compare to (a) the `img state/` reference **for the active theme** and (b) the same
tab+theme in the running WPF app. Watch for: no raw `{loc:Str}` keys shown as text; all cards/controls/emblem/
avatar present; correct grid/spacing/fonts (incl. Fredoka); accents correct for the active theme. Avoid the defects
in `img state/bad view*.png`. **Don't guess** — if you're unsure how any view/state should look, launch the WPF app,
switch to the relevant theme, open that exact view, and screenshot it; save keepers into `img state/`.

**Overlays are pure passive paint-only layers — tinted glass over the monitor:** you *see* it rendered smoothly, but
you can click/type/use the whole PC normally underneath. Pink fill, spiral, subliminal, flash, brain-drain must NOT
affect the CCP window or any other app behind them — input passes straight through, no focus steal, no activation,
not in Alt-Tab/taskbar, no interference, and the overlay's own animation (e.g. spiral) stays smooth. Implement with
`IsHitTestVisible=false` AND, on Windows, `WS_EX_TRANSPARENT|WS_EX_LAYERED|WS_EX_NOACTIVATE` applied after the
handle exists (plan §7.4). Verify: with an overlay up, click the app's buttons through it AND fully use a second app
behind it.

## MULTI-MONITOR — N screens, not "dual" (plan §7.5)

Every screen-spanning feature (video, flash, subliminal, spiral, pink fill, brain-drain, bouncing text, bubbles,
Chaos overlays) renders correctly across **all** monitors at once — **unless set to a single display** (respect it).
Iterate `Screens.All` (never `[0]`/`[1]`); size each surface to its own monitor's `Bounds` and scale by that
monitor's own `Scaling`, so a mix of landscape + portrait screens each look right (no stretching). Generalize
`AvaloniaDualMonitorVideoService` to N; spawn a surface only where needed, pool/reuse them, react to
`Screens.Changed` (hotplug/resolution/orientation). Verify on a 3-monitor mixed-orientation layout (1 landscape + 2
portrait).

## THEMING — each mod is a theme (plan §15.11)

The top-left mod switcher re-skins the whole app. Five themes, distinct looks: **CCP Default, Bambi, Sissy Hypno,
Droneification, Circe Lock**. Layout is shared; palette/avatar/card art differ per theme. **Accent colors must come
from theme resources via `DynamicResource` (never hard-coded hex)**, and the per-mod re-skin path (WPF's
`RefreshThemeAwareElements` reading `App.Mods` accents → rewriting `Application.Current.Resources`) must be ported.
Smoke-test across themes (at least CCP Default + one non-pink, e.g. Droneification) to confirm the whole UI re-skins.

## KNOWN CRITICAL BUGS (fix early)

- **P0 data-loss — user-data path split (task board #L):** `AvaloniaAppEnvironment.ApplicationDataPath` returns
  **Roaming** (`%APPDATA%`) while `UserDataPath` + the legacy WPF app use **Local**
  (`%LOCALAPPDATA%\ConditioningControlPanel`). Core services keyed on `ApplicationDataPath` (`SessionLogService`,
  `SessionFileService`, `ModerationCounter`) write to the wrong folder → existing users see empty session history /
  lost saved sessions. **Collapse to one Local path** (ponytail: make `ApplicationDataPath` == `UserDataPath` or drop
  it). Verify settings/tokens/logs/custom-sessions/progress all land in the legacy folder.
- **No WPF perf baseline captured (#M):** capture WPF startup/working-set/a couple of effect frame rates **now**
  (before more pruning) so "match or beat" is measurable.

## AVALONIA v12 GOTCHAS (don't trip on these)

- `Microsoft.WindowsAppSDK` stays **PINNED** (`ExcludeAssets="all" PrivateAssets="all"`) — do NOT remove (it's a
  required transitive of LibVLCSharp; removing it causes a WebView2 `NU1605` downgrade).
- Compiled bindings are on by default → every `.axaml` needs `x:DataType`; `{ReflectionBinding}` only for dynamic
  paths.
- `WindowDecorations` (not `SystemDecorations`); `TransparencyLevelHint` is a list in code.
- WPF mouse/`Preview` events → pointer/tunnel events; **re-add the left-button check** (it moves into the pointer
  args). `ElementName`/`RelativeSource` → `{Binding #name}` / `{Binding $parent[T]}`; `DependencyProperty` →
  `StyledProperty`/`DirectProperty`.
- Per-head DI override via `App.ConfigurePlatformServices`; register new seams in shared `ConfigureCoreServices`
  with a safe fallback. Don't create phantom seams (`ICaptureService`/`IImageDecoder`/`IUiTimer`/`IThumbnailProvider`
  — folded into `IFrameSource`/`IAssetLoader`/`IScheduler`).
- `IVideoSurface` is intentionally not DI-registered (constructed with a `VideoView`) — don't "fix" it.
- Localization auto-syncs into Core; add keys via `tools/new-localization-keys.json` + the merge script (never
  hand-merge per-language JSON in parallel).

## DEFINITION OF DONE

- `CCP.Avalonia.Desktop.Windows` launches; every tab / feature / dialog / window **actually works** AND looks like
  today's WPF app — **"builds + looks right" is NOT done.** START launches the mode, the avatar reacts,
  progression/marquee run, feature cards activate their services, overlays are tinted-glass click-through.
- The task board's **Known Functional Gaps** list is cleared.
- Every item in `docs/avalonia-ui-parity-matrix.md` is `[x]` — verified by exercising it in the running app (it was
  reset to all-unverified; don't trust old marks).
- Each tab visually matches the `img state/` reference for the active theme; UI re-skins across all 5 themes.
- All media/audio/video on `LibVLCSharp.Avalonia` + the cross-platform seams (WebView2 only behind the Windows head).
- `dotnet build CCP.Desktop.slnf -clp:ErrorsOnly` clean + `CCP.Core.Tests` pass; the sync backlog is empty.
- The P0 data-path bug is fixed (user data in the legacy Local folder).
- Linux/macOS/Android heads still build (CI green). **Windows parity is the primary bar.** The legacy WPF app stays
  runnable throughout (no big-bang cutover).

## CADENCE

Work until Done, updating trackers and compacting after each lane. Hit a genuine decision? Record it in the task
board and proceed with the best reasonable default; only stop for truly irreversible/ambiguous choices that need the
user.
