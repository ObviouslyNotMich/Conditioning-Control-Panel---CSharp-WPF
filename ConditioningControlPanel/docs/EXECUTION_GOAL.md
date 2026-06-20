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
  - `CCP.Avalonia.Desktop.Windows` launches and every tab / feature / dialog / window behaves the same as today's
    WPF app — every row in `docs/avalonia-ui-parity-matrix.md` is ✅.
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
    update task board + parity matrix → commit → **COMPACT CONTEXT** and move on.
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

CADENCE: Work until the Definition of Done holds, updating the trackers and compacting after each lane. If you hit
a genuine decision, record it in the task board and proceed with the best reasonable default; only stop for truly
irreversible or ambiguous choices that need the user.
