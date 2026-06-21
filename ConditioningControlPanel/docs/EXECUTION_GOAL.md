# Execution Goal — Finish the Avalonia v12 Cross-Platform Rebuild

> Paste the block below into the AI as a `/goal` (or use `@ConditioningControlPanel/docs/EXECUTION_GOAL.md`).
> It is written to run autonomously to completion and stays accurate by deferring to the plan.

---

GOAL: Drive the cross-platform rebuild of Conditioning Control Panel to completion. The end state is the
**Avalonia UI v12** app at **full feature parity with the current Windows WPF app** — it does everything the WPF
app does today, on the new cross-platform stack (LibVLCSharp.Avalonia for video, the cross-platform audio/secret/
browser/tray/etc. seams, no WPF-only paths in shared UI). Work autonomously until the Definition of Done holds.

SOURCE OF TRUTH: `ConditioningControlPanel/docs/crossplatform-rebuild-plan.md`. Read it first — especially
**§1A** (current status), **§19** (mainline sync + the §19.3 backlog), **§20** (execution model, the §20.10 lane
map, and §20.6 context compaction), **§21** (v12 gotchas), and **§23** (official Avalonia v12 docs — validate API
choices there). Treat `docs/avalonia-migration-task-board.md` (live queue) and `docs/avalonia-ui-parity-matrix.md`
(parity checklist) as external memory: update them as you work; first re-verify §1A against the real tree, since
a recent merge from `main` moved things.

STEP 0 — RECONCILE THE LATEST MERGE FROM `main` (do this BEFORE porting anything new). `main` was merged into
`feat/crossplatform` and added/changed WPF code the Core/Avalonia copies don't have yet:
  1. Re-verify the §19.3 backlog against the actual tree (AppSettings fields, ChaosSkiaFxOverlay, ChaosBoonColors,
     ChaosBubbleHostOverlay, ChaosDvdHostOverlay, ChaosCrashSentinel, BubbleService overhaul, UpdateService rework,
     Fredoka.ttf, ModService/FlashService/GlobalMouseHook deltas). Mark what's already done.
  2. Detect ANY ADDITIONAL drift the merge introduced beyond §19.3 using the §19.2 triage: diff the merge range
     over `ConditioningControlPanel/`, bucket each changed file (Model→Core, portable service→Core, UI→Avalonia,
     infra/installer→ignore, localization JSON→auto-synced).
  3. Port each outstanding item into Core/Avalonia; rewrite §19.3 to reflect done vs. remaining.
  4. Prefer doing §19.4 first (make `CCP.Core` the single source of truth for models, delete the WPF `Models/`
     duplicates) — it permanently removes the drift that caused this and shrinks every later step.

DEFINITION OF DONE:
  - `CCP.Avalonia.Desktop.Windows` launches and every tab / feature / dialog / window behaves AND **looks** the
    same as today's WPF app — every row in `docs/avalonia-ui-parity-matrix.md` is ✅, and each tab visually matches
    the `img state/` reference **for the active theme** + the running WPF app (no raw `{loc:Str}` keys shown as
    text, all feature cards / controls / emblem / avatar tube present, correct per-theme palette and fonts — see
    plan §13.5, §15.11).
  - All media/audio/video run on `LibVLCSharp.Avalonia` + the cross-platform seams (no WebView2/WPF-only path in
    shared UI; WebView2 only behind the Windows head).
  - `dotnet build CCP.Desktop.slnf -clp:ErrorsOnly` is clean (0 errors) and `CCP.Core.Tests` pass.
  - The §19.3 sync backlog is empty (cross-platform behavior matches `main`).
  - Linux/macOS/Android heads still build (CI green). **Windows parity is the primary bar**; full Linux/macOS/
    Android parity is secondary.
  - The legacy WPF app still builds and runs the whole time (no big-bang cutover).

EXECUTION MODEL (per §20):
  - Act as the ORCHESTRATOR. Own the chokepoints yourself: `CCP.Avalonia/ServiceCollectionExtensions.cs` (DI),
    `App.axaml`, the `MainWindow` shell, all `*.csproj`, and localization merges. Do the shell/infra backbone
    first; everything else hangs off it.
  - Seed lanes from the §20.10 map: one `MainWindow/MainWindow.<Feature>.cs` partial → one
    `Views/Tabs/<Feature>TabView.*` + `ViewModels/Tabs/<Feature>TabViewModel.cs` lane (porting its `Services/<Area>`
    logic into Core in the same lane). Sub-split the oversized Chaos lane.
  - You MAY spawn worktree-isolated sub-agents per lane if that's available; otherwise run lanes sequentially.
  - PER-LANE LOOP: claim (append a row to the Active Claims Ledger in the task board, commit it first) →
    targeted reads of just that subtree → port → `dotnet build CCP.Desktop.slnf -clp:ErrorsOnly` + Core tests →
    **visual-parity check: screenshot the tab and compare to the `img state/` reference for the active theme + the
    same tab in the running WPF app (§13.5)** → update task board + parity matrix → commit → **COMPACT CONTEXT**.
  - COMPACT AGGRESSIVELY (§20.6): after every finished item, after each green build, after any large file read,
    and at ~50–60% of the context window. Keep only the trackers + the outcome; never carry one lane's source
    into the next.

GUARDRAILS (from §21 / the v12 gotchas — validate against §23 docs):
  - Keep `Microsoft.WindowsAppSDK` PINNED (`ExcludeAssets="all" PrivateAssets="all"`), do NOT remove it (§5.1).
  - Compiled bindings are on by default → every `.axaml` needs `x:DataType`; use `{ReflectionBinding}` only for
    dynamic paths (§4.2).
  - Use `WindowDecorations` (not `SystemDecorations`); `TransparencyLevelHint` is a list in code (§4.4).
  - Convert WPF mouse/Preview events to pointer/tunnel events and RE-ADD the left-button check that moves into the
    pointer args (§4.8). Translate `ElementName`/`RelativeSource` bindings and `DependencyProperty` →
    `StyledProperty`/`DirectProperty` (§4.7).
  - Use the per-head DI override pattern (`App.ConfigurePlatformServices`); register new seams in the shared
    `ConfigureCoreServices` with a safe fallback. Do NOT create the phantom seams in §3.3
    (`ICaptureService`/`IImageDecoder`/`IImageSourceFactory`/`IUiTimer`/`IThumbnailProvider`) — they were folded
    into `IFrameSource`/`IAssetLoader`/`IScheduler`.
  - `IVideoSurface` is intentionally not DI-registered (constructed with a `VideoView`) — don't "fix" it.
  - Localization auto-syncs into Core (§19.1); add new keys via `tools/new-localization-keys.json` then
    `python tools/merge-localization-keys.py` (never hand-merge the per-language JSON in parallel).
  - Every change must build; never leave the tree red. Don't touch a chokepoint from a porter lane — route it
    through the task board's Hand-off Queue.
  - VISUAL PARITY (§13.5): a clean build is not "done" — earlier ports made small UI mistakes (raw loc keys shown
    as text, missing/unstyled cards, wrong spacing). At least once per tab before marking it ✅, screenshot the
    Avalonia tab and compare to the `img state/` reference for the active theme and the same tab in the running WPF
    app; avoid the defects in `img state/bad view*.png`. Doesn't need to be every iteration, but must happen before
    a tab is complete.
  - SELF-SERVE REFERENCES — DON'T GUESS: whenever you're unsure how *any* tab/dialog/popup/view looks or should
    look/behave (a missing theme reference, or just an unfamiliar screen/state), **launch the WPF app yourself**
    (from the `ConditioningControlPanel/` dir: `dotnet run --project ConditioningControlPanel.csproj`), switch the
    top-left mod selector to the relevant theme (CCP Default / Bambi / Sissy Hypno / Droneification / Circe Lock),
    open that exact view in the state you care about, and screenshot it — that's your reference. Save useful captures
    into `img state/` (e.g. `good-view-bambi.png`) so they're reusable (this is how to fill the missing
    Bambi/Droneification/Circe Lock references). Run the Avalonia head side-by-side to compare. (Local Windows step —
    needs a desktop session + screenshot tooling.)
  - THEMING (§15.11): the top-left mod switcher re-skins the app — **each mod is a theme**, and there are five with
    distinct looks: **CCP Default, Bambi, Sissy Hypno, Droneification, Circe Lock**. The dashboard *layout* is shared;
    *palette/avatar/card art* differ per theme. Reference images: `img state/default good view.jpg` (CCP Default),
    `img state/good view.png` (Sissy Hypno); for Bambi/Droneification/Circe Lock capture from the running WPF app.
    Compare each tab to the reference **for the active theme** — match layout/elements always, colors per theme.
    Accent colors must come from theme resources via `DynamicResource` (never hard-coded hex), and the per-mod
    re-skin path (WPF's `RefreshThemeAwareElements` reading `App.Mods` accents) must be ported. Smoke-test the app
    across themes (at least CCP Default + one non-pink, e.g. Droneification) to confirm the whole UI re-skins.

CADENCE: Work until the Definition of Done holds, updating the trackers and compacting after each lane. If you hit
a genuine decision, record it in the task board and proceed with the best reasonable default; only stop for truly
irreversible or ambiguous choices that need the user.
