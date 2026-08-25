# Cloud audit — `client/src` delta since PR #24 (2026-08-25)

Scheduled cloud audit run (recurring, per `client/docs/cloud-audit-brief.md` — that file is absent
from `feat/crossplatform`'s current tree, deleted in `1bdf998e4` alongside unrelated doc cleanup, but
its text is recoverable via `git show 7e9d3b377:client/docs/cloud-audit-brief.md` and this run follows
it from that recovered copy; PR #15 first noted the deletion and every pass since has repeated it —
`CLAUDE.md` and `docs/constitution.md` were re-checked this pass and still carry the same no-landing,
headed-evidence, and "never accept a failed/unrunnable check" rules the brief restated, so nothing about
the deletion changes what this pass is allowed to do).

## Delta since PR #24

PR #24 (base `180ddb2a1`) is the newest prior audit. Since that base, exactly two commits landed on
`feat/crossplatform` — `4db3a0696` ("docs(board): bank the white-screen elimination list, and two
parity defects found while hunting it") and `70301af72` ("docs(goal): the UI must look like the WPF
product's, not merely do the same things") — and both are documentation-only (`client/docs/task-board.md`,
`client/port.txt`). **`client/src` has zero changes since PR #24.** There is no code delta for this pass
to read.

## Supplementary pass: unused-private-method sweep

With no delta to audit, this pass used the gap to work one of the brief's called-out "genuinely
unmeasured" leads instead of re-walking ground four prior full passes (#1, #5, #10, #13/#18/#19) and
eleven delta passes already covered: **`IDE0051`-class unused private methods**. No IDE-family analyzer
has run in this repo (per the brief), so this axis had no prior measurement to build on.

This sandbox has no `dotnet`/Roslyn, so the sweep is a regex-based approximation, not a real analyzer
run:

- Parsed every `private` method declaration in `client/src` (block-bodied and expression-bodied
  separately), **999 in total** (793 block-bodied + 206 expression-bodied) across 393 `.cs` files.
- For each, grouped the declaring file with any sibling files sharing the same `partial class`/`struct`/
  `record` name (so a method called only from another part of the same partial class doesn't read as
  dead), then counted whole-word occurrences of the method name across that group.
- A method whose name appears exactly once in its group (the declaration itself, nothing else) is a
  candidate for dead code.

**Result: 0 candidates.** Every private method found at least one call site beyond its own declaration.

This is a real if narrow result — it doesn't catch a method that's called only via reflection with a
literal string (rare in this codebase; `nameof()` calls still match) or a genuinely dead overload
shadowed by name collision with an unrelated same-named private method in a different, unrelated class
(the sibling-scoping above specifically guards against that second case). It does **not** need a Roslyn
run to trust for what it claims: no unused private method, by name, in `client/src`.

`CA1515` (public types that could be `internal`, 1656 hits per the brief's last full analyzer run)
remains unmeasured this pass — checking that axis needs the actual analyzer, which this sandbox cannot
run, and eyeballing 507 public types by hand for cross-project reachability from `client/tests` would be
a fresh full pass, not a focused one on a zero-delta day.

## Ranked cut-list

Nothing to cut. Lean already. Ship.

## Out of scope, not re-litigated

PR #15's still-open finding #1 (Win32 interop consolidation — a third copy of `MSG`, `TranslateMessage`/
`DispatchMessageW` duplicated across `Pointer/Win32PointerInterop.cs`, `Input/Win32InputInterop.cs`, and
`Input/Win32PanicInterop.cs`) and PR #21's findings are untouched by this delta and still open in those
PRs.

## Observation, not a finding

Twenty-four `audit:` PRs (#1–#24) are open against `feat/crossplatform`, none merged, spanning
2026-08-22 through today; this is the twenty-fifth. PR #24 flagged the same backlog and it has not
moved. PR #15's fully-costed Win32-interop consolidation is still the largest sitting fix in that
backlog.

## Verification

Nothing in this pass was applied, built, or tested — there is nothing to apply; this doc is the only
change. This sandbox cannot run the standard client gates (`check-warnings.mjs`, `check-floor.mjs`) or
build the `net8.0-windows` shipping tree at all. Per `docs/constitution.md`, a failed-or-unrunnable
check is never accepted to keep work moving, so this pass stops here rather than claiming verification
it cannot produce.
