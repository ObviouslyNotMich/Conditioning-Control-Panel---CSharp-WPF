# Cloud audit — `client/src` delta since PR #22 (2026-08-25)

Scheduled cloud audit run, per `client/docs/cloud-audit-brief.md`. That file is absent from
`feat/crossplatform`'s current tree — deleted in `1bdf998e4` alongside unrelated doc cleanup — but its
text is recoverable via `git show 7e9d3b377:client/docs/cloud-audit-brief.md`, and this run follows it
from that recovered copy.

## Base and delta

PR #22 (base `13aab940b`) is the newest prior audit. Twenty commits landed on `feat/crossplatform`
since that base, of which one touches `client/src`:
`ce663833a "fix(safety,bubbles): an emergency stop the owner can reach, a bubble that is a bubble, and
one fix refused at the merge"` — 13 files, ~792 insertions / 43 deletions. This pass reads that delta
in full:

- `Input/Win32PanicInterop.cs` (new) and `Input/Win32PanicKey.cs` (new) — a `RegisterHotKey`-based
  system-wide emergency stop.
- `App.axaml.cs`, `Views/MainWindow.axaml.cs` — wiring the panic chord to session stop / app exit.
- `Effects/OverlaySurfaceSet.cs`, `Effects/FlashSurfacePresenter.cs`, `Overlay/OverlayReasonCodes.cs` —
  a named, counted "pool full" state where a bare `return` used to drop images silently.
- `Overlay/Win32OverlayPresence.cs`, `Overlay/IOverlayPresence.cs` — `Withdraw()` now frees the
  retained frame surfaces instead of holding them until `Dispose()`.
- `Pointer/Win32PointerInterop.cs`, `Pointer/Win32PointerSurface.cs`, `Effects/BubblePopSurfacePresenter.cs`
  — `WS_EX_LAYERED` + `SetLayeredWindowAttributes` so the bubble target composites instead of painting
  an opaque box, plus a concentric-ellipse radial-gradient approximation of upstream's bubble ramp.
- `Views/SessionEditorWindow.axaml` — comment-only, correcting an earlier record of a headless-harness
  hang's trigger (blank line, not length).

This sandbox has no `dotnet`/Windows toolchain, confirming the brief's premise: gates cannot run from
Linux. `check-floor.mjs`/`check-warnings.mjs` were not run. **No code changed. Nothing pushed to
`feat/crossplatform`. Nothing weakened.**

## Ranked cut-list

**None.** Every new and changed member in this delta was read against the brief's five tags
(`delete:`, `stdlib:`, `native:`, `yagni:`, `shrink:`) and checked for call sites with grep across
`client/src` and `client/tests`.

## Checked and rejected

- **`Win32PanicKey.OwnerWindow` / `.IsArmed` / `HotkeyRefused` / `HotkeyUnsupported`** look like an
  over-exposed surface on a first read (a public window handle, a public armed flag) — but
  `PanicKeyTests.cs:61-75` asserts on `IsArmed` and reads `OwnerWindow` back through `IsWindow()` to
  prove the registration is real rather than trusting this object's own bookkeeping. Not YAGNI.
- **`Win32PanicInterop.cs` as a separate file from `Win32PanicKey.cs`** looks like it could collapse
  into the one class that uses it — but the file's own doc comment names why it can't: a native-window
  census (`client/docs/window-behavior-manifest.md` §8.4) re-derives creation sites from the
  `Type.CreateWindowExW(` shape, and the port's one-interop-file-per-surface convention (already
  followed by `Tray/Win32TrayInterop.cs`, `Overlay/Win32OverlayInterop.cs`) is what keeps a new native
  window visible to that guard. Consolidating would silently drop this surface from the census.
- **`Win32PointerSurface.Blend(uint,uint,double)`** (new, COLORREF channel lerp) looks like the fourth
  near-identical blend/lerp helper in `client/src` — `Gaze/GazePreprocess.Blend` (float),
  `Features/Mantra/MantraIntensity.MantraColour.Lerp` (byte, truncating cast), `Session/ScriptedSessionRamp.Lerp`
  (double), `Glyph/GlyphFrame.Blend` (int, premultiplied-alpha composite), `Effects/BubbleCountGame.Blend`
  (byte, straight alpha composite). Each carries a distinct, deliberately-preserved rounding or
  compositing law reproducing a specific upstream behaviour (e.g. `MantraColour.Lerp`'s doc comment
  calls out upstream's *truncating* cast by name, as a behaviour and not a shortcut). Collapsing them
  into one shared helper would risk changing which channel truncates vs. rounds, and verifying parity
  needs headed evidence this sandbox cannot produce. Named as a lower-confidence observation only, not
  a ranked cut — consistent with PR #22 rejecting the same shape of finding (`GazePreprocess.FillDetector`
  / `.FillCrop`) for the same reason.
- **`OverlaySurfaceSet.PlacementsRefusedWhileFull` / `FlashSurfacePresenter.ImagesDroppedWhilePoolFull`**
  read as two names for one counter — but the first is the primitive on the pool, the second is the
  presenter's own public surface reading it (`ImagesDroppedWhilePoolFull => _surfaces.PlacementsRefusedWhileFull`),
  matching every other diagnostic property in `FlashSurfacePresenter.cs` (`SurfacesShown`,
  `UndecodableImages`) that forwards from `_surfaces`. One name, one owner, one forwarding property —
  the established shape in this file, not a new one.

## Out of scope, not re-litigated

PR #21's findings (`SanitizeTitleForWire` regex, `SanitizeId` duplication, `AiTitleAllowList`
letter/digit check) are untouched by this delta and still open in that PR.

## Observation, not a finding

**Twenty-two `audit:` PRs (#1–#22) are open against `feat/crossplatform`, none merged**, spanning
2026-08-22 through today; this is the twenty-third. Restated because the count has not moved and is
now the more useful signal than any single delta.

## Verification

Nothing in this pass was applied, built, or tested — there is nothing to apply. This sandbox cannot
run the standard client gates meaningfully and cannot build the `net8.0-windows` shipping tree at all.
Per `docs/constitution.md`, a failed-or-unrunnable check is never accepted to keep work moving, so this
pass stops at a reviewed, empty cut-list.
