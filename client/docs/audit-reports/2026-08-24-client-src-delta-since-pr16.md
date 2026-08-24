# Cloud audit — client/src delta since PR #16

Scheduled cloud audit routine, run from a Linux sandbox against `feat/crossplatform` at
`1650615ccb37b43fe7ef20558a1365a38896a3f0`. Base for this delta: PR #16's base,
`5c342f84fa065ca4dd411648ccafd57fb2f2caaa`.

## Process note: the standing brief is still missing, and the PR backlog is growing

`client/docs/cloud-audit-brief.md` still does not exist on `feat/crossplatform`. It was added at
`7e9d3b377` and deleted three commits later at `1bdf998e4` ("Refactor tests and documentation for
clarity and accuracy") as an apparent side effect of unrelated documentation cleanup — that commit's
message does not mention the brief at all, only `port-audit-prompt.md`. PRs #1–#16 have all flagged
this same gap and recovered the brief's content from git history; this pass did the same and used
that recovered content as the operative brief. Restoring the file itself is out of scope for this
pass (it is not `client/src`, and this routine cannot land anything), but it is a five-minute fix
for whoever next touches `feat/crossplatform` directly.

Separately: **16 audit PRs (#1–#16) are open and unmerged** as of this run, opened roughly every two
hours since 2026-08-22 with no PR closed, merged, or superseded in between. This pass's own delta is
1 commit / 4 files — genuinely small, like most recent deltas — so the backlog is now growing faster
than any single pass's findings could justify reviewing. This routine has no ability to merge, close,
or triage past PRs; that is an owner decision, not something fixable from here.

## The one hard limit

**Nothing was landed.** No push to `feat/crossplatform`, no merge, no test/pin/gate weakened. This
sandbox has no Windows/WSLg display, so `check-floor.mjs`, `check-warnings.mjs`, and every headed
verification command are unrunnable and are not claimed.

## Scope

Restricted to the delta since PR #16's base (`5c342f84f`..`1650615cc`): 1 commit
(`381e7acca`, "fix(motion): pin the hosted flag against the OS, and stop the picker
over-claiming"), 4 files, 130 added / 34 removed lines:

- `Motion/MotionSettings.cs` — `HostedMotion` gained `OverridesOsPreference` (the pure half of the
  OS-disagreement rule) and `BrowserArgument` grew an injectable `readOsClientAreaAnimation` seam so
  the rule is pinned against every OS answer, not just whatever the sandbox happens to read.
- `Views/Pages/MotionNotices.cs` (new file) — the Motion module's user-facing sentences, moved out of
  `SystemPage.axaml.cs` into a `*Notices.cs`-convention static class so a unit fact can check the
  exact wording instead of a headless window mount.
- `Views/Pages/SystemPage.axaml` / `SystemPage.axaml.cs` — the blurb `TextBlock` and the
  `DescribeMotion()` method now read from `MotionNotices.Blurb` / `MotionNotices.Describe(...)`
  instead of holding literal strings inline.

All four files are a single coherent extract-and-test refactor: it centralizes wording that used to
live only in code-behind (unprovable except via a headless window mount) into a plain-data class
covered by unit facts, and adds a seam (`readOsClientAreaAnimation`) that the new tests actually use
to pin behaviour against both OS states on one machine. Correctness, security, and performance are
out of scope for this pass.

## Ranked cut-list

Lean already. Ship.

No `delete:`/`stdlib:`/`native:`/`yagni:`/`shrink:` candidate survived review in this delta.
Specifically checked and found justified, not speculative:

- **`MotionNotices.cs` as a new file** rather than inlining the strings back into
  `SystemPage.axaml.cs`: the port already has this exact convention (`PhraseBackupNotices`, cited in
  the file's own remarks), and the extraction is what let `MotionLevelTests.cs` add wording-level
  assertions (`Assert.Contains`/`DoesNotContain` on `Describe(...)` output at
  `client/tests/CcpClient.Tests/MotionLevelTests.cs:342-400`) that a code-behind string could not
  carry without a headless mount.
- **`BrowserArgument`'s `readOsClientAreaAnimation` seam** (`Func<bool?>? = null`): not a speculative
  parameter — `MotionLevelTests.cs:127` calls it with an explicit closure to pin the rule against an
  OS answer the sandbox itself cannot produce, which is exactly the justification the seam's own doc
  comment gives.
- **`OverridesOsPreference` as a separate pure function** rather than inlined into `BrowserArgument`
  and `Describe`: it has two real callers (`HostedMotion.BrowserArgument` for the diagnostic-log
  path, `MotionNotices.Describe` for the UI-sentence path) that need the identical boolean, so
  splitting it is the one-function-one-truth move, not premature abstraction.

## What was checked and found clean

- **No duplicate wording left behind**: grepped for the removed string literals
  ("How much movement this app is allowed to show", "Pages this app hosts are told to") across
  `client/src` and `client/tests` — no remaining copy outside `MotionNotices.cs` and its tests.
- **No unused surface added**: `MotionNotices.Blurb`, `.NoStore`, and `.Describe` are each called
  from `SystemPage.axaml.cs` and asserted on directly in both `client/tests/CcpClient.Tests` and
  `client/tests/CcpClient.HeadlessTests`.
- **`HostedMotion.BrowserArgument` has five real call sites** (`DtrhHostWindow`, `DtrhLoomWindow`,
  `IntakeHostWindow`, `GoonHostWindow`, `ChaosTunnelWindow`), matching the "five hosted surfaces, one
  answer" claim in the type's own remarks — not a currently-unused choke point.
- **No unused `using` left in `SystemPage.axaml.cs`** after the extraction; every import in the file
  still resolves a type used below it.

## Not done, and why

No code changes were made anywhere. This sandbox cannot build the Windows-targeted solution or
produce headed evidence, so a change proposed here would be unverified work per
`docs/constitution.md`. The brief-file restoration and PR-backlog triage noted above are named for an
owner to act on, not fixed here.
