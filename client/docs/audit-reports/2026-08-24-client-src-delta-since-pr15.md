# Cloud audit — client/src delta since PR #15

Scheduled cloud audit routine, run from a Linux sandbox against `feat/crossplatform` at
`5c342f84fa065ca4dd411648ccafd57fb2f2caaa`. Base for this delta: PR #15's base,
`b23ea0f37c2690586e8a3a304d5c1f761c5fe81f`.

## Process note: the standing brief is still missing

`client/docs/cloud-audit-brief.md` still does not exist on `feat/crossplatform`. PR #15 (and PRs
#1–#11 before it) already flagged this exact gap and recovered the brief's content from git history
(`7e9d3b377`); nothing has changed since, so this pass did the same recovery and used that content
as the operative brief.

PR #15 itself, opened four hours before this pass by the same routine, is still open and unmerged —
this routine has no ability to close, merge, or triage it. **Duplicate PR risk noted:** this pass's
scope barely overlaps PR #15's (that pass covered all of `client/src` as of `b23ea0f3`; this one
covers only what changed since). If an owner would rather these delta passes fold into one running
PR than accumulate as `audit/client-src-delta-since-prN` siblings, that's a process call for them,
not something this routine can decide.

## The one hard limit

**Nothing was landed.** No push to `feat/crossplatform`, no merge, no test/pin/gate weakened. This
sandbox has no Windows/WSLg display, so `check-floor.mjs`, `check-warnings.mjs`, and every headed
verification command are unrunnable and are not claimed.

## Scope

Restricted to the delta since PR #15's base (`b23ea0f3`..`5c342f84`): 2 files, 79 added / 10 removed
lines, both platform-fix work on the same two X11 window-placement/diagnostics seams:

- `Features/FeaturePopupWindow.axaml.cs` — a documented fix for a WSLg/X11 race where the window
  manager silently overrides the popup's initial `Position` write; the window now re-asserts the
  same computed position one dispatcher turn later.
- `Views/MainWindow.axaml.cs` — the rail-door layout probe now logs whenever its text changes
  (previously: only once, ever, before the real geometry had landed on X11) and reports each door's
  window-relative offset alongside its screen offset, because `@ screen` alone was proven to switch
  coordinate spaces mid-startup on WSLg.

Both hunks carry inline evidence (measured coordinates, dates, WSLg scale factors) for the bug they
fix. Correctness, security, and performance are out of scope for this pass.

## Ranked cut-list

Lean already. Ship.

No `delete:`/`stdlib:`/`native:`/`yagni:`/`shrink:` candidate survived review in this delta. Both
files add narrowly-scoped platform workarounds with no unused surface, no speculative parameter, and
no config nobody sets.

## Performance note, out of scope, not fixed here

`MainWindow.axaml.cs`'s `ProbeLine` (`client/src/CcpClient.Desktop/Views/MainWindow.axaml.cs:397`)
calls `this.PointToScreen(new Point(0, 0))` to compute `clientOrigin` — a value that does not vary
per route. It is called from `ShellRoutes.Declared.Select(ProbeLine)`
(`MainWindow.axaml.cs:247`), so the window's screen origin is recomputed once per rail door (5
routes today) on every `LayoutUpdated` pass instead of once per pass. `PointToScreen` is a real
platform round-trip (X11/Win32), not a pure computation, so this is a live redundant syscall on a
handler that already fires on every layout pass — worth hoisting above the `Select` for a future
session to verify under a real display, not fixed here per the brief's performance-is-out-of-scope
rule for this pass.

## What was checked and found clean

- **`FeaturePopupWindow.axaml.cs`'s re-assert fix**: checked for a pre-existing, more general
  "reassert after WM race" helper elsewhere in `client/src` that this could have reused instead of a
  local `Dispatcher.UIThread.Post` — none exists; `CompanionWindow.axaml.cs` and every other window in
  `client/src/CcpClient.Desktop/Features` position themselves once and do not fight a WM override, so
  this is the only site with the problem, not a duplicated fix.
- **`MainWindow.axaml.cs`'s dedup-by-text logic** (`_loggedLayoutProbe`, replacing the old
  `_layoutProbeLogged` bool): the string comparison and single extra field are the minimum needed to
  turn "log once" into "log on change" without a wall-clock poll or a new timer — consistent with the
  repo's no-wall-clock-waits rule, not overbuilt.
- **New XML doc comment on `ProbeLine`** (~20 lines): verbose, but every claim in it cites a measured
  WSLg run: not filler.

## Not done, and why

No code changes were made anywhere, including the performance note above. This sandbox cannot build
the Windows-targeted solution or produce headed evidence, so a change proposed here would be
unverified work per `docs/constitution.md`. The performance note is a proposal for a human, or a
future session with working gates, to act on and verify.
