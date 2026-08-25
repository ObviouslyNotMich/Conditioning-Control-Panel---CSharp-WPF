# Cloud audit — `client/src` delta since PR #25 (2026-08-25)

Scheduled cloud audit run (recurring, per `client/docs/cloud-audit-brief.md` — that file is absent from
`feat/crossplatform`'s current tree, deleted in `1bdf998e4` alongside unrelated doc cleanup, but its
text is recoverable via `git show 7e9d3b377:client/docs/cloud-audit-brief.md` and this run follows it
from that recovered copy; PR #15 first noted the deletion and every pass since has repeated it —
`CLAUDE.md` and `docs/constitution.md` were re-checked this pass and still carry the same no-landing,
headed-evidence, and "never accept a failed/unrunnable check" rules the brief restated, so nothing about
the deletion changes what this pass is allowed to do).

## Delta since PR #25

PR #25 (base `70301af72`) is the newest prior audit. Since that base, 18 non-merge commits (plus 6
merge commits) landed on `feat/crossplatform`, and unlike PR #25's zero-delta window this one is
substantial: a new theme/token layer (`Themes/Ccp.axaml`, `App.axaml`), the subliminal effect's colour
settings and frame source, a pointer ink-sweep rewrite (`Pointer/PointerInkSweep.cs`, replacing
per-pixel `GetPixel` calls), the Linux AT-SPI element route, an accessible-name collision fix, and
matching test coverage. 60 files changed, ~5000 lines added. This pass read that new and changed
surface in `client/src` in addition to sampling the rest of the tree.

## Ranked cut-list

1. **`shrink:`** Seven files each hand-roll the same small family of `JsonElement` safe-accessor helpers
   (`GetString`/`GetStr`, `GetBool`, `GetInt`, `GetLong`, `GetDouble`) — same `TryGetProperty` +
   `ValueKind` + `TryGetX` pattern, byte-for-byte, at every site. No shared helper exists today; all
   seven live in the same `CcpClient.Desktop.csproj`, so sharing needs no new project reference.
   - `client/src/CcpClient.Desktop/Companion/BarkRules.cs:214-226` — `GetStr`, `GetInt`, `GetDouble`,
     `GetBool` (nullable returns)
   - `client/src/CcpClient.Desktop/Features/Arcademy/ArcademyProtocol.cs:551-562` — `GetString`,
     `GetBool`, `GetInt` (nullable returns)
   - `client/src/CcpClient.Desktop/Features/Dtrh/DtrhMeta.cs:1040-1061` — `GetString`, `GetBool`,
     `GetInt`, `GetLong`, `GetDouble` (nullable returns)
   - `client/src/CcpClient.Desktop/Features/Dtrh/DtrhProtocol.cs:311-327` — `GetString`, `GetBool`,
     `GetInt`, `GetDouble` (nullable returns)
   - `client/src/CcpClient.Desktop/Features/Intake/IntakeProtocol.cs:248-264` — `GetString`, `GetBool`,
     `GetInt`, `GetDouble` (nullable returns)
   - `client/src/CcpClient.Desktop/Features/Goon/GoonProtocol.cs:376-397` — `GetString`, `GetBool`,
     `GetInt`, `GetLong`, `GetDouble` (nullable returns)
   - `client/src/CcpClient.Desktop/Features/Dtrh/DtrhAssetStats.cs:167-171` — `GetDouble`, `GetLong`
     (non-nullable, 0-default variant — same shape, different fallback convention)

   Also duplicated in the same family: an identical `ToValue(JsonElement)` switch expression in
   `client/src/CcpClient.Desktop/Companion/BarkRules.cs:205-212` and
   `client/src/CcpClient.Desktop/Features/Dtrh/DtrhBarkRouting.cs:106-113` — fold into the same shared
   helper.

   Replacement: one shared `internal static class` (e.g. `Json`) exposing the five nullable accessors
   plus `ToValue`, called from all eight sites in place of the ~130 duplicated lines. The one
   non-nullable variant (`DtrhAssetStats.cs`) can call the shared nullable accessor with `?? 0` at its
   two call sites rather than keeping its own copy.

   `net: -85 to -95 lines possible.`

Nothing else surfaced this pass. Sampled the new theme/token surface (`Themes/Ccp.axaml`, 52 `x:Key`
entries — all either consumed by Avalonia's FluentTheme accent system or referenced via
`DynamicResource` in the same file, none dead) and the new `Pointer/PointerInkSweep.cs` — no YAGNI or
duplication found in either.

## Out of scope, not re-litigated

PR #15's still-open finding #1 (Win32 interop consolidation — a third copy of `MSG`,
`TranslateMessage`/`DispatchMessageW` duplicated across `Pointer/Win32PointerInterop.cs`,
`Input/Win32InputInterop.cs`, and `Input/Win32PanicInterop.cs`) and PR #21's findings are untouched by
this pass and still open in those PRs.

## Non-audit finding (correctness, not chased further)

The two `ToValue(JsonElement)` copies named above diverge on the `Number` branch:
`DtrhBarkRouting.cs:109` explicitly casts `(double)l` before returning; `BarkRules.cs:208` returns the
raw `long` `l` with no cast. One boxes a `double`, the other a `long`, for what looks like the same
input shape — worth a look, out of scope for this pass.

## Observation, not a finding

Twenty-five `audit:` PRs (#1–#25) are open against `feat/crossplatform`, none merged, spanning
2026-08-22 through today; this is the twenty-sixth. Every pass since PR #15 has flagged this and it has
not moved. PR #15's fully-costed Win32-interop consolidation (~390–465 lines) is still the largest
sitting fix in that backlog, now over 30 hours old.

## Verification

Nothing in this pass was applied, built, or tested — there is nothing to apply; this doc is the only
change. This sandbox has no `dotnet`/Roslyn and cannot run the standard client gates
(`check-warnings.mjs`, `check-floor.mjs`) or build the `net8.0-windows` shipping tree at all. Per
`docs/constitution.md`, a failed-or-unrunnable check is never accepted to keep work moving, so this pass
stops at a reviewed, single-item cut-list rather than claiming verification it cannot produce.
