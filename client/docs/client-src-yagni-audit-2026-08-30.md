# client/src YAGNI / efficiency audit — 2026-08-30

Scheduled cloud audit pass, run from a Linux sandbox with no .NET SDK available (`dotnet` is not
on PATH here), per `client/docs/cloud-audit-brief.md`'s standing brief (recovered from git history
— see note at the bottom; the file is currently missing from `feat/crossplatform`'s tree). All
findings below come from manual source reading plus grep/glob verification, not from running any
analyzer, build, or test.

**No code changes are included in this PR.** Per the brief's hard limit, this sandbox cannot run
`client/tests/floor/check-floor.mjs` or produce headed verification evidence, so nothing is landed
against `feat/crossplatform` — this is a report only.

## Scope

`client/src/CcpClient.Desktop/` — 418 files, ~108,500 lines (`Assets/assets.manifest.json`, a
58,370-line generated file, excluded per the brief).

## What was checked, per the brief's two named "unmeasured" leads

- **Unused private methods (IDE0051-class):** extracted every `private`/`protected` method
  declaration (1,108 candidates), isolated the 1,028 true `private` non-`override` ones, and
  confirmed each is referenced at least once elsewhere in its own file. Zero unused. The
  `protected override` methods that looked unused on a first pass are virtual-dispatch overrides
  required by an abstract base in the `Effects/` family (`NextInterval`, `Stamp`, `Deliver`,
  `OnDisarmed`, `Ready`) — false positives, not real candidates.
- **CA1515-class over-exposure:** the project is a `WinExe`, not a library, with no
  `InternalsVisibleTo`, so in principle most of the public surface could be `internal` instead.
  No bulk claim is made here: without a compiler available, Avalonia's XAML/compiled-binding
  visibility requirements can't be verified, so a wholesale "make these internal" finding would be
  a volume claim this pass can't stand behind. Cross-checked all 70 public interfaces against
  production implementations and test doubles instead (see finding below and the "checked, found
  legitimate" list).

## Ranked findings

**1.** `ManualAvatarClock` (`client/src/CcpClient.Desktop/Features/AvatarTube/AvatarAnimationEngine.cs:30-70`,
~40 lines) is a test double — its own doc comment calls it "Test clock: time advances only when
the test says so" — that lives in the production assembly instead of `client/tests`. Grepped
`ManualAvatarClock` across all of `client/src` and `client/tests`: every instantiation is in
`CcpClient.Tests/AvatarAnimationEngineTests.cs` or `CcpClient.HeadlessTests/AvatarTubeHeadlessTests.cs`;
zero production callers. `SystemAvatarClock` in the same file is the real production
implementation and stays. This doesn't fit any of the five tags cleanly (it isn't dead — it's a
legitimate test seam — just shipped in the wrong project), so it's reported as a placement issue
rather than forced into `delete:`/`yagni:`. Suggested fix: move the class into
`client/tests/CcpClient.Tests/` (or a shared test-support project both test assemblies can
reference) as a plain fix, not a `delete:`.

`net: -40 lines possible` (moved out of `client/src`, not deleted — the `IAvatarClock` seam and
`SystemAvatarClock` are load-bearing per `client/docs/cloud-audit-brief.md`'s note that the
`*Clock` interfaces exist to satisfy the no-wall-clock-waits rule, and stay as-is).

No other findings survived verification. This codebase came back essentially lean on this pass.

## Checked and found legitimate (not flagged)

- 8 single-implementation interfaces without an obvious test double
  (`IButtplugSession`, `IDuckHandle`, `IIntakeEntitlementSource`, `IAiEndpointAdmissionPolicy`,
  `ILockCardPhrasePool`, `IAiDiagnosticsSink`, `IDocumentMigration`, `IPopup`) — each checked
  individually. All are either consumed in production with a documented reason
  (e.g. `IAiEndpointAdmissionPolicy` is an explicitly-labeled owner-pending governance extension
  point), a sound encapsulation boundary (`IDuckHandle`, a private nested marker interface), or do
  have a test double once nesting is accounted for (`FeaturePopupManager.IPopup` has
  `FeaturePopupWindow` in production and `FakePopup` in `FeaturePopupManagerTests.cs`).
- 6 public events with no production subscriber, only test subscribers — all explicitly documented
  as forward-compatibility hooks for an in-progress 1:1 port of the legacy WPF product (e.g.
  `ArcademySettingsEcho.cs:25` names the exact legacy hook it will attach to once it exists) —
  acknowledged migration seams, not speculative gold-plating.
- No `NotImplementedException`, `TODO`/`FIXME`/`HACK`, dead `if(true)`/`if(false)` branches, or
  commented-out code anywhere in `client/src`.
- `Math.Max(1, Math.Min(...))` clamp patterns in `Video/VideoLetterbox.cs` and
  `Features/PopupPlacement.cs` are not materially shorter as `Math.Clamp(...)` — not a real
  `shrink:`.
- A 12-line private nested `ICommand` implementation in `MainWindowViewModel.cs` — too small for a
  package dependency to be a net win.
- No `.Count() > 0`, `.ToList().ForEach()`, or manual-loop-instead-of-LINQ patterns of any size.

## Non-YAGNI findings noticed in passing

None. No correctness, security, or performance issues turned up while reading through these files;
none were chased down deliberately, per the brief's scope.

## Process note: `client/docs/cloud-audit-brief.md` is missing from `feat/crossplatform`

This run's instructions pointed at `client/docs/cloud-audit-brief.md`, but that file does not exist
in the current `feat/crossplatform` tree. It was added in `7e9d3b377` and deleted three commits
later in `1bdf998e4` ("Refactor tests and documentation for clarity and accuracy"), whose message
names the deletion of an unrelated file (`client/tools/port-audit-prompt.md`) but never mentions
`cloud-audit-brief.md` — this looks like unintentional collateral from that cleanup rather than a
deliberate decision to retire the standing brief. This run recovered the brief's exact content from
git history (`git show 7e9d3b377:client/docs/cloud-audit-brief.md`) and followed it as written. No
fix is included in this PR since restoring a docs file is outside this pass's client/src scope —
flagging it here so a human can decide whether to restore it before the next scheduled firing.

---
_Generated by [Claude Code](https://claude.ai/code)_
