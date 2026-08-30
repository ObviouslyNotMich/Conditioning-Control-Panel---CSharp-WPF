# Cloud audit report — 2026-08-30

Scheduled cloud YAGNI/efficiency run against `feat/crossplatform`. Per the standing brief this
routine follows, **nothing was pushed to `feat/crossplatform`, no gate or pin was touched, and
this PR carries only this report.**

## The finding this run surfaced: the standing brief and several gates are gone

`client/docs/cloud-audit-brief.md` — the file this routine is told to read and follow — **does not
exist on `feat/crossplatform`.** It was added at `7e9d3b377` and deleted 2.5 hours later at
`1bdf998e4` ("Refactor tests and documentation for clarity and accuracy"), which is on the current
branch history. That single 85-file commit also, without describing any of this in its message:

- Removed `assertNoCommittedResidue()` from `client/tests/floor/check-floor.mjs` — the check added
  specifically to catch build/run residue committed under `spine-tasks/` (its own removed comment
  cites 76 `.trx` files at 49.8 MB plus 72 `.DONE` markers as the reason it exists).
- Lowered `client/tests/floor/floor.json`'s `CcpClient.Tests` pin from **2625 to 2608** (-17),
  bundled into a commit that also deletes tests (e.g. `GoonGameCensusTests.cs` lost
  `TheTwoWalkCopies_AreByteIdentical_AndMatchTheHashTheCensusQuotes`), rather than as the "one
  task, one commit slice" the constitution requires for a floor bump.
- Deleted `WarningGateGuardTests.TheGateIsNamedInTheWorkflowTheHarnessAndTheAuditorPrompt` — the
  test that asserted the warning-gate command is actually referenced in
  `port-workflow.md`, `verification-harness.md`, `port-audit-prompt.md`, and
  `port-wave.workflow.mjs` (both issuing sites). This is the specific guard against a gate that
  exists but nothing tells anyone to run.
- Deleted `client/tools/port-audit-prompt.md`, one of the four files the now-deleted guard test
  checked.
- Rewrote `docs/constitution.md`. The prior version's hard-rules list included: *"Never modify
  `.pi/`, `.spine/`, the existing `spine-tasks/SP-*/` packets, or `CLAUDE.md`."* The new version
  drops that clause — and the same commit edits `CLAUDE.md` by ~150 lines. The one rule that would
  have named this specific edit as a violation was removed in the same edit that makes it.

None of this was flagged as a change in scope in the commit message, which only mentions ledger
entries, comments, and "clarity." Whether this was a deliberate rewrite, an agent session that
overreached, or something else, a local (non-sandboxed) session should verify intent with the repo
owner before trusting `feat/crossplatform`'s current gates, pins, or governing docs at face value.
This report does not restore any of it — restoring `check-floor.mjs`, `floor.json`, the deleted
guard test, or the constitution clause is a decision for a session that can actually run the
gates, not this one.

For continuity, the deleted brief's own content is preserved in git history at
`7e9d3b377:client/docs/cloud-audit-brief.md` and is not reproduced here.

## YAGNI/efficiency pass — `client/src`

Scope per the (now-deleted, but still legible in history) brief: `client/src` first, ~74,500 lines
across 319 files. `ConditioningControlPanel/**` is out of scope (read-only legacy evidence) and was
not touched or read for cutting purposes.

Checked, with zero survivors after verifying callers:
- Unused private methods (IDE0051-class, previously unmeasured): swept all 1,047 private method
  declarations across `client/src` + `client/tests`. None had zero callers.
- Hand-rolled BCL duplicates: no manual case-insensitive compares, clamp ternaries, linear-search
  loops, or hand-built JSON in production code (`JsonSerializer`/`JsonDocument` used throughout;
  the only literal JSON strings found are harness-only test fixtures).
- Config/feature-flag surface: all 8 `Environment.GetEnvironmentVariable` reads branch into
  distinct real logic, none frozen at one path.
- Enum members (95 enums, 542 members): no genuinely unreferenced cases.
- A ~28-handler slider-callback family in `StudioPage.axaml.cs` looked like duplication at a
  glance; each handler carries a doc comment tying it to specific preserved WPF behavior
  (some re-pace live schedules, some don't), so consolidating it is a behavior risk dressed as
  cleanup. Not flagged.

One real, small, verified finding — an over-exposed-surface (`CA1515`) cut, not a line-count win:

- `yagni:` `HapticMoment` is a public enum used only inside its own file and never appears on any
  public member of `HapticLimb`/`IHapticLimb` — only in private methods and private nested
  records. Narrow to `internal`.
  `client/src/CcpClient.Desktop/Haptics/HapticLimb.cs:12`
- `yagni:` `DtrhRanks` is a public static class used only within its declaring file
  (`DtrhMeta.cs`), returns primitives, no reflection/cross-assembly use found. Narrow to
  `internal`.
  `client/src/CcpClient.Desktop/Features/Dtrh/DtrhMeta.cs:10`

`net: -0 lines possible.` (visibility-only; two fewer public API surface members.)

14 other single-file-referenced public types were checked and rejected as candidates: they appear
as parameter/return/property types on other **public** members, so narrowing them would fail
C#'s accessibility-consistency check, not just tests.

No `NotImplementedException`, `#if false` dead branches, or bare `TODO`/`FIXME` turned up
incidentally; this pass did not specifically hunt for correctness or security issues, so that is
not a clean bill on those axes.

**Lean already. Ship.** — for `client/src` specifically. The open item is the gate/brief
regression above, not this pass's scope.
