# Cloud audit — client/src public-surface narrowing (2026-08-29)

Scheduled cloud YAGNI/efficiency pass per `client/docs/cloud-audit-brief.md`. Report only — no
product code changed by this pass.

## Brief status (still unresolved)

`client/docs/cloud-audit-brief.md` is still absent from `feat/crossplatform`'s tree — deleted in
`1bdf998e4` on 2026-08-22 and flagged as a likely-unintentional casualty by every audit pass since
(PRs #6, #13, #18, #19, #21, #26). This run again recovered its text from git history
(`git show 7e9d3b377:client/docs/cloud-audit-brief.md`) and followed it. Restoring the file (or
retargeting this routine's prompt) would stop every future run from repeating this note.

## Backlog status

As of this run, **26 prior `audit:` PRs (#1–#26) are open against `feat/crossplatform`, none merged
or closed**, spanning 2026-08-22 through 2026-08-25 (the routine then found nothing new to report for
about four days, consistent with the brief's "changing nothing and reporting accurately is a good
outcome" — no PR was opened for those runs). This is PR #27. The backlog itself, not any single
line-count, is now the largest finding this routine can surface; triaging or closing it is an owner
call this pass cannot make.

## Method

`client/src` (~74,500 lines, 319 files, current HEAD `1d033c85f`) was checked against the brief's two
named-unmeasured axes:

1. **Unused private methods** (`IDE0051`-class, no analyzer covers this). Every private method name
   in `client/src` was grepped repo-wide (`client/src`, `client/tests`, `client/spikes`, `.cs`/`.axaml`).
   **Zero** had fewer repo-wide hits than declarations — this axis stays clean.
2. **Over-exposed public surface** (`CA1515` fired 1656 times previously). Public types with zero
   references from `client/tests`/`client/spikes` were checked for leakage into another public
   member's signature elsewhere in `client/src` (a public member cannot expose a less-accessible type,
   so a leaking candidate can't be narrowed without a cascading change) before being listed. Candidates
   already named in prior open PRs (`#6`, `#13`, `#19`) were cross-checked and dropped rather than
   re-listed.

## Cut-list

| # | Tag | What to cut | Replacement | Path | net |
|---|---|---|---|---|---|
| 1 | `yagni:` | `RuntimeSessionEnvironment` can be `internal` — only production impl of `ISessionEnvironment` (tests inject their own fake per its own doc comment), constructed once in `CompositionRoot.cs` and passed only as the interface type | change `public sealed class` to `internal sealed class` | `Capabilities/SessionProbe.cs:29` | visibility-only |
| 2 | `yagni:` | `HostTokenReaders` can be `internal` — static factory with one external caller (`HostLoginEntitlement.cs:67`), returns the `IHostAuthTokenReader` interface, zero test references | change `public static class` to `internal static class` | `Entitlement/HostAuthTokenReader.cs:182` | visibility-only |
| 3 | `yagni:` | `BarkRuleSet` can be `internal` — pure aggregation type, only ever held as a private field/local inside `BarkPipeline.cs`, zero test references | change `public sealed class` to `internal sealed class` | `Companion/BarkRules.cs:235` | visibility-only |

No line-count reduction — these are visibility-only edits that shrink the CA1515-flagged public
surface without touching behavior. **Not applied**: a bulk or even single accessibility change needs
a real build to confirm no XAML/reflection/generic-constraint path depends on `public`, and this
sandbox has no `dotnet` installed at all (checked before starting) — consistent with the brief's core
premise that this repo's gates cannot run from Linux. A Windows-capable reviewer applying any row
still owes `check-warnings.mjs --cold` then `check-floor.mjs`.

## Checked and rejected (already covered by open PRs, not re-listed)

`SoundFlowDtrhAudio`, `DtrhRunConfig`, `SyntheticPopupContent` — same `internal`-narrowing shape as
the three above, but already named in PR #6's cut-list (item 2). Re-confirmed still applicable, not
re-added here to avoid re-litigating an already-open finding.

## Also checked, no findings

- `stdlib:`/`native:` — no hand-rolled string/parsing logic duplicating a BCL method found this pass.
- `shrink:` — nothing beyond what PRs #19/#21/#26 already carry (JSON accessor duplication, Win32
  P/Invoke duplication, DPAPI duplication) — not re-verified or re-listed here.
- The 18 `*PanelNotices` and 13 `Unsupported*` platform-fallback families, and the `MainWindow._pages`
  page classes, each have almost every sibling referenced by name from `client/tests` — narrowing just
  the untested outlier in each family would break that convention for uncertain benefit, so none of
  those are listed as findings (more likely a coverage gap than YAGNI).
- `ISecretStore` and the `*Clock` interfaces (`ISessionClock`, `IScheduleClock`, `IScriptedClock`) —
  confirmed load-bearing single-impl seams per the brief, left untouched.

## Correctness findings

None observed during this pass.

## Verification

Nothing in this list has been applied, built, or tested. This sandbox cannot run
`node client/tests/floor/check-warnings.mjs` / `check-floor.mjs` meaningfully and cannot build the
`net8.0-windows` shipping tree at all. No test, pin, or gate was touched or weakened.
