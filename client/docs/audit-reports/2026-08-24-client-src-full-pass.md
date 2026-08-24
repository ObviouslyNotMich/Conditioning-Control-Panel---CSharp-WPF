# Cloud audit report — client/src full pass — 2026-08-24

Scheduled cloud YAGNI/efficiency audit routine. Linux sandbox, no build/test gates available.
Base commit: `e1d263619` on `feat/crossplatform`. **Zero product code changed.**

## Brief status

`client/docs/cloud-audit-brief.md` — the file this routine is configured to read and follow —
does not exist on `feat/crossplatform`. It was added at `7e9d3b377` and deleted three commits
later at `1bdf998e4` ("Refactor tests and documentation for clarity and accuracy"); that commit's
own message only calls out deleting `port-audit-prompt.md`, so the brief looks like it was swept
up unintentionally rather than deliberately retired. PRs #1–#17 already flagged this same gap and
none has restored the file. This pass again recovered the brief's content from git history
(`git show 1bdf998e4^:client/docs/cloud-audit-brief.md`) and followed it, including its one hard
limit: this repo's gates (the exact unit/headless floor count, headed Windows/WSLg capture,
`net8.0-windows` build) cannot run from Linux, so nothing gets landed here.

## Backlog status

17 prior audit PRs (#1–#17) are open and unmerged as of this run, opened roughly every two hours
since 2026-08-22 with none closed or merged in between. This PR makes 18. The routine has no way
to triage its own backlog — that's an owner call, not something a future firing can resolve by
itself.

## Scope and method

Full pass over `client/src/CcpClient.Desktop/*` (~74,500 lines, 319 files, all 24 subsystems),
not a delta — this supersedes the delta scope PR #17 would have covered (the client/src-touching
commits landed since PR #17's base: `47256702d`, `d68ae1134`, `66fbb804b`, `ecdbcea00`,
`14a3e3bc2`, `db9fe065a`, `f84f95413`, `fe3012abe`, `8dc8f0b4f`, `a0fa2898a`), since those are
included in the full-repo sweep below.

Checked and confirmed still clean/justified, per the brief's pre-measured baseline:

- 0 dead public types, 0 unused private fields (already measured; not re-run).
- 58/66 interfaces have a second implementation or a test double; the remaining 8 are load-bearing
  (`ISecretStore` as the per-OS seam, the `*Clock` interfaces for the no-wall-clock-waits rule).
- Unused private *methods* (`IDE0051`-class, previously unmeasured): regex-scanned every `private`
  method in every `.cs` file under the target tree for in-file reference counts, twice (strict
  brace-only, then a looser multiline/expression-body variant). **0 candidates**, both passes.
- `CA1515` (1656 public-could-be-internal hits): no `InternalsVisibleTo` exists anywhere; test
  projects reach the Desktop assembly via plain `ProjectReference`, which is why so much is public.
  Real architectural lead, but it's a visibility-keyword change, not a line-count cut, and the
  brief already flags it as risky to change wholesale — not reported as a cut here.

Additional checks: hand-rolled retry/poll loops, manual JSON/Base64 construction, accumulator
patterns replaceable by LINQ, duplicate `Clamp`/`Lerp` helpers across files (four exist, each with
distinct domain semantics — not duplicates), hand-rolled `INotifyPropertyChanged` (only 3 files,
too few to justify a source-generator dependency), unused `using` directives, and structurally
similar sibling classes (`Effects/*SurfacePresenter.cs` family) for collapsible duplication.

## Ranked cut-list

Lean already. Ship.

Nothing survived review as a genuine, safe, line-reducing cut. Two near-misses, reported only
because they're exactly the shape this pass looks for and were explicitly checked and rejected as
too small to matter:

- `stdlib:` `Video/VideoLetterbox.cs:69-70` and `Features/PopupPlacement.cs:31` hand-roll
  `Math.Max(lo, Math.Min(hi, x))` instead of `Math.Clamp(x, lo, hi)` (available on net10.0).
  **Net: 0 lines** — same length either way, marginally more readable. Not worth a diff alone.

No correctness bugs or security issues surfaced during the pass.

## Test plan

- [ ] N/A — documentation-only change, no product code touched. No gates run: this Linux sandbox
      cannot run the Windows-only floor/warning gates or produce a headed capture.
