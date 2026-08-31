# Cloud audit — client/src, 2026-08-31 12:13 UTC

Scheduled cloud YAGNI/efficiency pass, run from a Linux sandbox per the standing brief
(`client/docs/cloud-audit-brief.md` — deleted from this branch, recovered from history;
see Housekeeping below). No product code changed. Report only.

## Zero code delta since PR #38

`feat/crossplatform` HEAD (`1d033c85f`) is byte-identical to the base commit of PR #38, which is
itself byte-identical to the base of #35/#36/#37. Nothing has landed in `client/src` across five
consecutive scheduled runs (#35, #36, #37, #38, this one). Re-running the sweeps those PRs already
did against unchanged source would not find anything new.

This pass spot-checked five more angles none of #35/#36/#37/#38 named:

| Angle | Result |
|---|---|
| `async void` methods outside event handlers | 0 (only hit is a code comment describing a known, out-of-scope fault-handling gap in `AiCommandExecutor.cs`) |
| `GC.Collect()` calls | 0 |
| `Task.Run(() => ...)` sites (9 total) | All 9 read — every one carries an inline comment justifying deliberate off-thread dispatch (UI-thread deadlock avoidance, teardown budget, background stop). None are trivial/needless wrapping. |
| `.ToList().Count` / `.ToArray().Length` (materialize-then-measure) | 0 |
| Hand-rolled `Clamp` helpers outside `Math`/`MathF` | 1 (`DtrhSaveSlots.cs:281`) — not a real stdlib-swap candidate: it's a validate-or-default-to-1 function, not boundary clamping, so `Math.Clamp` isn't a behavior-preserving substitute. |

All clean or already-legitimate. **Ranked cut-list: nothing new.** The valid, unlanded cuts remain
exactly the ones already sitting in #35 (`ILockCardPhrasePool`, -14 lines) and #36 (seven cuts,
-33 lines) — restating them here would duplicate, not add to, those PRs. `Lean already. Ship.`

## The actual finding: the backlog is still growing, cadence still unpaused

**38 open `audit:` PRs (#1–#38)**, spanning 2026-08-22 through today, none merged or closed. #36
flagged this at #35; #38 flagged it again six hours ago and recommended triage or pausing the
2-hour cadence. Neither has happened — the count is now one higher than #38 reported, and this is
the fifth consecutive run to find the source clean or unchanged.

Recommend (owner call, not this pass's to make — same as #38):

1. Merge or close-with-reason the two small, already-verified cuts sitting in #35 (-14 lines) and
   #36 (-33 lines).
2. Resolve the duplicate brief-restoration PRs (#34, #35, #36 each carry the identical
   `cloud-audit-brief.md` restoration).
3. Pause or slow the 2-hour cadence until the backlog is triaged — five straight zero-delta runs
   is a strong signal the current cadence is spending most of its budget re-confirming "nothing
   new" rather than surfacing anything actionable.

## Housekeeping note (repeated from #35/#36/#37/#38)

`client/docs/cloud-audit-brief.md` — the file this routine is supposed to read — still doesn't
exist on `feat/crossplatform`. It was deleted in `1bdf998e4` ("Refactor tests and documentation
for clarity and accuracy", 2026-08-22) in a commit whose message never names it. Recovered its
content from git history and followed it, since its hard limit and scope are still consistent with
current `CLAUDE.md` / `docs/constitution.md`. Not fixed here (outside this pass's `client/src`
scope, and #34/#35 already carry a restoration) — flagging again since it's unresolved across five
consecutive runs.

## Out of scope

No correctness, security, or performance issues noticed beyond the two already on record in #35
(`MediaFoundationCameraCapture.cs:876` bounded O(n²) dedup; `ScriptedSessionRamp.cs:251` hand-rolled
clamp-then-lerp, not a safe stdlib swap).
