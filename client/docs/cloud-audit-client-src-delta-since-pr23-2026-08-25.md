# Cloud audit — client/src delta since PR #23 (2026-08-25)

Scheduled cloud YAGNI/efficiency pass, per `client/docs/cloud-audit-brief.md`. That file is **absent
from `feat/crossplatform`'s current tree** — deleted in `1bdf998e4` (2026-08-22) alongside unrelated
doc cleanup, first noted by PR #15 and repeated by every pass since. Its text is recoverable via
`git show 7e9d3b377:client/docs/cloud-audit-brief.md`, and this run follows it from that recovered
copy.

This sandbox has no `dotnet`/Windows toolchain, confirming the brief's premise: gates cannot run from
Linux. `check-floor.mjs`/`check-warnings.mjs` were not run. **No code changed. Nothing pushed to
`feat/crossplatform`. Nothing weakened.** This PR adds only this doc.

## Scope of this pass

PR #23 (base `ce663833a`) is the newest prior audit. Since that base, 7 commits landed on
`feat/crossplatform` touching `client/src` — all folded into one merge,
`fix(safety,overlay): close both panic hazards, rescope the video pin, and solve the flash-size
mystery — it was one mouse click`. Net: 20 files changed, ~1183 insertions / 56 deletions. This pass
reads that delta in full: an off-UI-thread panic key with its own message pump and a watchdog that
escalates to process termination when the UI thread will not answer (`Input/PanicWatchdog.cs`,
`Input/Win32PanicInterop.cs`, `Input/Win32PanicKey.cs`), a per-module below-video yield for the pink
filter and spiral (`Overlay/VideoTopmostAnchor.cs`, `Overlay/IOverlayPresence.cs`,
`Overlay/Win32OverlayPresence.cs`, `Effects/OverlaySurfaceSet.cs`, `Effects/PinkFilterSurfacePresenter.cs`,
`Effects/SpiralSurfacePresenter.cs`), per-module arm/disarm failure isolation in the session engine
(`Session/SessionEngine.cs`, `Session/OwnedSessionEffect.cs`, `Session/EffectReasonCodes.cs`), and a
new System-page notice explaining that settings are not imported from the shipping WPF app
(`Views/Pages/SettingsHandoverNotices.cs`, `Views/Pages/SystemPage.axaml`, `.axaml.cs`,
`Session/SessionParticipant.cs`).

## Ranked cut-list

1. **`yagni:`** `Input/Win32PanicInterop.cs`, new in this delta, declares a **third** copy of the
   native `MSG` struct (`Pointer/Win32PointerInterop.cs:153`, `Input/Win32InputInterop.cs:119` already
   have one apiece) and the delta's own message pump — `GetMessageW`, `TranslateMessage`,
   `DispatchMessageW`, `PostMessageW`, `PostQuitMessage` — duplicates `TranslateMessage`/
   `DispatchMessageW` signatures already declared twice (same two files) and is the **first**
   standalone `GetMessageW`/`PostMessageW`/`PostQuitMessage` triplet in the tree. This is not a new
   category of debt: PR #15's still-open finding #1 already named the five Win32 interop files'
   shared `Rect`/`Point`/`WndClassExW`/`PaintStruct`/pump plumbing and proposed one shared
   `Win32/NativeWindowInterop.cs` (`net: -250 to -300` lines, at that count). This delta grew exactly
   that duplication — one more `Msg` copy, three more never-before-duplicated pump P/Invokes — so
   landing PR #15's fix now saves more than it did when PR #15 wrote it. Replacement: fold
   `Win32PanicInterop.cs`'s `Msg` struct and `GetMessageW`/`TranslateMessage`/`DispatchMessageW`/
   `PostMessageW`/`PostQuitMessage` into the shared file PR #15 already proposed, alongside the other
   two `Msg` copies. `net: -20 lines` in this delta alone, more once merged with PR #15's finding.
   `[client/src/CcpClient.Desktop/Input/Win32PanicInterop.cs:112-141]`

Nothing else in this delta earned a place on the cut-list. Full detail below.

## Checked and not counted

- **`SessionEngine.Start`/`.Stop`/`.SetEnabled`'s three try/catch blocks around `Arm()`/`Disarm()`**
  look like the same shape three times — catch, build a `CapabilityReason`, assign `_armOutcomes`.
  They are not a `shrink:` candidate: each catch's message is written for a different reader situation
  (session never started vs. mid-teardown vs. a live quick-toggle) and `Stop`'s also appends to
  `_stopFailures`, which the other two must not touch. A shared helper would need a message-template
  parameter and a "record as stop failure: yes/no" flag, which is the same number of call-site
  arguments as today's three inline blocks — no net line reduction, and it would blur three distinct
  narratives (`EffectArmFailed`, `EffectDisarmFailed`, `EffectDialOff`) behind one generic wrapper.
  Same shape of rejection PR #23 already applied to `OverlaySurfaceSet.PlacementsRefusedWhileFull`/
  `FlashSurfacePresenter.ImagesDroppedWhilePoolFull`.
- **`VideoTopmostAnchor`** (new, 114 lines, process-global static state) looks over-scoped for one
  producer (`Win32VideoPresence`) and two consumers (pink filter, spiral) — but its own doc comment
  answers the obvious question first: it exists because this port, unlike upstream's single
  `OverlayService` that owns every window, has one `OverlaySurfaceSet` per module on its own clock, so
  there is no existing owner to ask instead. `Resolve` is kept pure and separate from the published
  state specifically so the yes/video-up/no-video decision is unit-testable without touching the
  static field — already the shape the port's other seams use. Not a YAGNI abstraction; a documented
  substitute for an owner this port doesn't have.
- **`PanicWatchdog.TerminateThisProcess`** (`Process.GetCurrentProcess().Kill()`) is the only process
  self-termination call in `client/src` — no existing helper it duplicates or should have used instead.
- **`IOverlayPresence.ReassertBelowVideo() => Reassert()`** default-interface-method: the minimal shape
  for "every backend except one gets today's behaviour for free," not a speculative extension point —
  only `Win32OverlayPresence` overrides it, and the interface comment names why the default is the
  honest one (a backend with no z-order to resolve has nothing to yield to).
- **Five `Blend`/`Lerp` implementations** (`GazePreprocess.Blend`, `MantraColour.Lerp`,
  `ScriptedSessionRamp.Lerp`, `GlyphFrame.Blend`, `BubbleCountGame.Blend`, now joined by
  `Win32PointerSurface.Blend` from PR #23's delta) were re-checked rather than re-litigated: this
  delta didn't add a new one, so PR #23's "lower-confidence observation only" stands as written.

## Out of scope, not re-litigated

PR #21's findings (`SanitizeTitleForWire` regex, `SanitizeId` duplication, `AiTitleAllowList`
letter/digit check) and PR #15's remaining findings #2–#5 (audio backend device-matching duplication,
ephemeral-port bind-retry duplication, three-arm outcome-resolution duplication, scattered
`foreach`-to-LINQ collapses) are untouched by this delta and still open in those PRs.

## Observation, not a finding

**Twenty-three `audit:` PRs (#1–#23) are open against `feat/crossplatform`, none merged**, spanning
2026-08-22 through yesterday; this is the twenty-fourth. Restated because the count has not moved
across several passes and is the more useful signal than any single delta — the routine is
accumulating findings faster than they're being triaged, and PR #15's fully-costed, still-unapplied
Win32-interop consolidation (~250-300 lines, now larger per this pass) is sitting in that backlog.

## Verification

Nothing in this pass was applied, built, or tested — there is nothing to apply. This sandbox cannot run
the standard client gates meaningfully and cannot build the `net8.0-windows` shipping tree at all. Per
`docs/constitution.md`, a failed-or-unrunnable check is never accepted to keep work moving, so this
pass stops at a reviewed, one-item cut-list.
