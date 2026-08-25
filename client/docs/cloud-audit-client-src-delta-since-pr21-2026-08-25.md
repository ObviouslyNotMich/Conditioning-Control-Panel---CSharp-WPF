# Cloud audit — client/src delta since PR #21 (2026-08-25)

Scheduled cloud audit run (recurring, per `client/docs/cloud-audit-brief.md` — that file is absent
from `feat/crossplatform`'s current tree, deleted in `1bdf998e4` alongside unrelated doc cleanup, but
its text is recoverable via `git show 7e9d3b377:client/docs/cloud-audit-brief.md` and this run follows
it from that recovered copy).

This sandbox has no Windows toolchain and no way to run `check-floor.mjs` / `check-warnings.mjs`
meaningfully, confirming the brief's premise. **No code changed. Nothing pushed to
`feat/crossplatform`. Nothing weakened.**

## Scope of this pass

PR #21 (base `5e775ee3`) is the newest prior audit. Four commits touched `client/src` since that
base — session import (`SessionImport.cs`, new, 264 lines), gaze inference preprocessing
(`GazePreprocess.cs`, new, 454 lines), and the rack/editor doc updates and UI wiring that go with
import landing — nine files, ~1021 insertions. This pass reads that delta in full.

## Ranked cut-list

**None.** Every changed or added file was read in full against the brief's five tags
(`delete:`/`stdlib:`/`native:`/`yagni:`/`shrink:`). Nothing qualified:

- `SessionImport.cs`, `SessionRackNotices.cs`'s new import messages, and the `ScriptedSession.cs` /
  `ScriptedSessionRack.cs` / `SessionEditorRules.cs` doc updates carry no new abstraction beyond one
  caller, no hand-rolled logic a stdlib call replaces, and no dead surface — every new public member
  (`SessionImport.Validate`, `.RunAsync`, `SessionRackNotices.Imported`, `.ImportRefusedFile`,
  `.ImportRefusedPicker`, `.ImportFaulted`) has exactly one call site in `StudioPage.axaml.cs`,
  checked by grep.
- `StudioPage.axaml.cs`'s two new private methods (`OnScriptedImportClickedAsync`,
  `ReportScriptedImport`) are both called once, from the wiring added in the same commit.
- `GazePreprocess.cs`'s six private helpers (`Square`, `Taps`, `Sample`, `Blend`, `Pixel`, `Require`)
  are all reached from the public surface; none is unused.

## Checked and rejected

- **`SessionRackNotices.ImportButton` / `.ImportTooltip` looked unused at first pass** — the XAML
  (`StudioPage.axaml:2400-2408`) hardcodes the button's `Content` and `ToolTip.Tip` as literal
  strings rather than setting them from the constants in code-behind, unlike `SystemPage.axaml.cs:137,139`
  wiring `PhraseBackupNotices.ImportButton`/`.ImportTooltip` at runtime. Before flagging it as dead
  code, checked `client/tests`: `SessionImportHeadlessTests.cs:203-204`
  (`TheStudioDoorCarriesAnImportButton_ArrangedAndCarryingTheRacksOwnWords`) asserts the rendered
  button's `Content` and tooltip equal those same constants, pinning the XAML text against drift the
  same way a runtime assignment would. Not a finding — it is the same author-once/verify-by-test
  pattern the codebase already uses, not an unreferenced constant.
- **`GazePreprocess.FillDetector` and `.FillCrop`** share the same nested bilinear-sample loop shape
  and could look like a `shrink:` candidate. Read both in full: `FillDetector` letterboxes into a
  pre-filled `-1`-valued canvas with independent X/Y scale factors and a `[-1, 1]` normalisation;
  `FillCrop` samples a single-scale square crop with optional horizontal flip and a `[0, 1]`
  normalisation, and never pre-fills because out-of-frame reads already resolve to 0 through `Pixel`.
  A shared helper needs five to six extra parameters (per-axis scale, canvas offset, fill value, flip,
  normalisation) to cover both, on code whose own doc comments stake out bit-for-bit parity with
  upstream's OpenCV path (`GazePreprocess.cs:60-82`, the "one named divergence" paragraph). Not raised
  as a ranked cut: the merge's net line count is uncertain and the risk is behavioral drift in code
  this audit cannot verify from Linux — headed evidence would be needed either way, per
  `docs/constitution.md`'s presentation-verification rule. Named here only as a lower-confidence
  observation, not fixed.

## Out of scope, not re-litigated

PR #21's own findings (`SanitizeTitleForWire` regex, `SanitizeId` duplication,
`AiTitleAllowList` letter/digit check) are untouched by this delta and still open in that PR; not
repeated here.

## Observation, not a finding

**Twenty-one `audit:` PRs (#1–#21) are open against `feat/crossplatform`, none merged, spanning
2026-08-22 through today (2026-08-25); this is the twenty-second.** PR #21 already flagged the
backlog itself as the more useful signal than any single delta. Restated because the count has not
moved since: no PR in the series has been merged, closed, or consolidated in this run's checking
window.

## Verification

Nothing in this pass was applied, built, or tested — there is nothing to apply. This sandbox cannot
run `node client/tests/floor/check-warnings.mjs` / `check-floor.mjs` meaningfully and cannot build the
`net8.0-windows` shipping tree at all, so a green result here would prove nothing and a red one would
prove nothing either. Per `docs/constitution.md`, a failed-or-unrunnable check is never accepted to
keep work moving — this pass stops at a reviewed, empty cut-list rather than landing anything.
