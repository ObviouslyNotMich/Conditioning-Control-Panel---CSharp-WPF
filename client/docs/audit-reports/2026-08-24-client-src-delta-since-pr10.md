# Cloud audit — client/src delta since PR #10, 2026-08-24

Scheduled cloud audit routine, run from a Linux sandbox against `feat/crossplatform`
(`74268631be8e0ab0a9ece24b0835052e7eb4b977`). Nothing in this pass was landed — see
"Process note" below.

## Process note: the standing brief is still missing, and PRs #1–#10 are still unmerged

`client/docs/cloud-audit-brief.md` — the file this routine is told to read — still does not exist
on `feat/crossplatform`. It was added in `7e9d3b377` and deleted in `1bdf998e4` ("Refactor tests
and documentation for clarity and accuracy") on 2026-08-22. PR #10 (2026-08-23) already flagged
this exact gap and recovered the brief's content from git history; nothing has changed since. This
pass did the same recovery (`git show 7e9d3b377^..7e9d3b377 -- client/docs/cloud-audit-brief.md`)
and used that content as the operative brief, since it matches this routine's own scheduled prompt
word for word on the one hard limit.

Separately: **PRs #1 through #10**, opened by this routine between 2026-08-22 and 2026-08-23, are
all still open and unmerged — none can land without a Windows/WSLg checkout to run
`check-floor.mjs`, `check-warnings.mjs`, and headed verification. This routine has no ability to
close, merge, or triage them; a human needs to decide which findings to act on and either merge,
supersede, or close the rest before they accumulate further.

## The one hard limit (from the recovered brief)

**Nothing was landed.** No push to `feat/crossplatform`, no merge, no test/pin/gate weakened. This
sandbox has no Windows/WSLg display and cannot build the `net8.0-windows`/Avalonia-on-Windows
targets that matter for headed verification, so `check-floor.mjs`, `check-warnings.mjs`, and every
headed-verification command were unrunnable and are not claimed. All findings below come from
reading the code and cross-checking call sites against `client/tests/`, not from an analyzer run.

## Scope

`client/src`, restricted to the delta since PR #10's base commit
(`ae17514592a07ca7814ee9e2783ad27de2dbb822`..`74268631be8e0ab0a9ece24b0835052e7eb4b977`): 65 files,
~8,400 added lines across `Ai/`, `Effects/`, `Features/Companion/`, `Input/`, `Lifecycle/`,
`Navigation/`, `Session/`, `Storage/`, and `Views/`. PR #10 already covered everything before this
delta and found it clean; re-reviewing that code was out of scope here. Four parallel read-only
passes covered: `Ai/` + `Effects/`; `Features/Companion/` + `Input/` + `Lifecycle/` +
`Navigation/`; `Session/` + `Storage/`; `Views/` (including the six new `*Notices.cs` files).
Correctness, security, and performance are out of scope for this pass; nothing alarming enough to
name separately was noticed.

## Ranked cut-list (biggest cut first)

**1.** `shrink: four identical catch(IOException)/catch(UnauthorizedAccessException) block pairs
in ScriptedSessionLog.cs, each with the same body duplicated per exception type.` Collapse each
pair into one `catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)`,
matching the pattern the same changeset already uses in `AvaloniaUserFilePicker.cs` and
`UserFileTransfer.IsStorageFault`.
`[client/src/CcpClient.Desktop/Session/ScriptedSessionLog.cs:362-371 (Files), 388-395 (Read),
411-420 (Persist), 444-451 (Prune)]`
`net: -17 lines`

**2.** `shrink: CompanionTranscriptWindow's Escape-to-close is a KeyBinding plus a private 4-member
ICommand implementation for one Close() call.` The file's own comment defends the choice ("so the
gesture and the action stay in one place"), and this is lower confidence than the other findings
here for that reason — but `FeaturePopupWindow.axaml.cs:68-89` solves the identical problem
(Escape closes a window) with a 9-line `OnKeyDown` override and no ICommand type, and its own doc
comment records that a window-level `KeyBinding` was tried there first and silently failed to fire
(compiled `CloseCommand` binding never resolved because `KeyBinding` doesn't inherit
`DataContext`). `CompanionTranscriptWindow` sidesteps that specific failure mode by constructing
the `ICommand` directly in code rather than via a compiled binding, so it may not share
`FeaturePopupWindow`'s bug — but it still spends a 20-line private class shape on what the rest of
this codebase does in an `OnKeyDown` override, and does not reuse `FeaturePopupWindow`'s existing
pattern for the same window shape (W-04).
`[client/src/CcpClient.Desktop/Features/Companion/CompanionTranscriptWindow.cs:133-138, 168-181]`
`net: -14 lines`

**3.** `delete: SessionRecapLaunch.HistoryCount is a counter property incremented on every
ShowHistory() call and read by nothing.` Verified by grep across `client/src` and `client/tests`:
the only other `HistoryCount` symbols are the unrelated `SessionRecapNotices.HistoryCount(int)`
static formatter and the `"HistoryCount"` TextBlock automation id, neither of which reads this
property.
`[client/src/CcpClient.Desktop/Navigation/SessionRecapLaunch.cs:99-100, 169]`
`net: -3 lines`

**4.** `delete: PhraseBackupFile.KnownPools is a public static property with zero references.`
Verified by grep across `client/src` and `client/tests`: only its own declaration matches; the
internally-used name is the differently-spelled `KnownPoolOrder`.
`[client/src/CcpClient.Desktop/Session/PhraseBackupFile.cs:89-91]`
`net: -3 lines`

**5.** `shrink: PopQuizEffect.DescribeStation reintroduces, verbatim, a 3-line station-formatter
that already exists identically in LockCardEffect.cs and BubbleCountEffect.cs (both out of this
delta's scope).` Lower confidence: this codebase's effect modules elsewhere explicitly argue
*against* sharing helpers between effects ("a shared helper would make a later change to one
silently change the other" — `PopQuizSchedule.cs:14`, `BubbleCountSchedule.cs:34`), but that stated
rationale is about tunable behavior/constants, not pure diagnostic-string formatting, so it may not
apply here.
`[client/src/CcpClient.Desktop/Effects/PopQuizEffect.cs:687-689]`
`net: -3 lines` (in this file only; the other two copies are outside this delta)

**6.** `shrink: SessionRecapWindow.axaml.cs computes
ordinal.ToString(CultureInfo.InvariantCulture) twice in BuildRow for the same row-suffix string.`
Hoist into one local `var suffix = ...` and reuse for both `Name` and
`AutomationIdProperty` — the pattern `SessionHistoryWindow.axaml.cs:101` already uses one file
over.
`[client/src/CcpClient.Desktop/Views/SessionRecapWindow.axaml.cs:140, 146]`
`net: -1 line`

**Total: ~-41 lines out of ~8,400 new lines in scope (well under 1%).**

## What was checked and found clean

Consistent with PR #10's finding, this delta is unusually resistant to this kind of audit: nearly
every new public member, dial, and branch carries an inline doc comment citing a real reason it
exists (an upstream-parity divergence, a consent/privacy-boundary rationale, a test double). Files
confirmed clean in their new/changed lines, not just skipped: all of `Ai/` (8 files) except the one
marginal `Effects/` catch above; all of `Effects/` except finding 5; `Features/Companion/`'s
consent machinery (privacy dial, three forget scopes, per-app title allow-list, effect permission
grid — deliberately not scrutinized for "redundancy" per the constitution's boundary rule);
`Input/IInputPresence.cs`, `InputReasonCodes.cs`, `Win32InputPresence.cs`; `Lifecycle/
CompositionRoot.cs`'s new wiring (every new constructor parameter traced to a live call site);
`Storage/`'s new `IUserFilePicker` family (confirmed test double in
`client/tests/CcpClient.Tests/UserFilePickerTests.cs`); `Session/PhraseBackup.cs`,
`ScriptedSessionRun.cs`, `SessionParticipant.cs`'s new PopQuiz wiring; and all of `Views/` apart
from finding 6, including the six new `*Notices.cs` files, which share a naming convention but not
an implementation shape worth collapsing (each pulls distinct constants from a distinct upstream
source, by design per their own doc comments).

## Correctness note, out of scope, not fixed here

One of the four sub-passes noticed `StudioPage.axaml`'s new `<DockPanel>` wrapping the detail host
(around the SessionLockBanner) looked worth a glance for a matching close tag. Not chased further
— correctness is out of scope for this pass — but naming it per the brief's instruction to report
rather than fix.

## Not done, and why

No code changes were made to `client/src` or anywhere else. This sandbox cannot build the
Windows-targeted solution or produce headed evidence, so a change proposed here would be unverified
work — exactly what the constitution and the recovered brief both exist to prevent. Every line
above is a proposal for a human, or a future session with working gates, to act on and verify — not
a change already made.
