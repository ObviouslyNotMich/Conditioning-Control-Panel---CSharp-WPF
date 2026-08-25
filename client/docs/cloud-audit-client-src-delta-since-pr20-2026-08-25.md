# Cloud audit — `client/src` delta since PR #20, 2026-08-25

Run from an Anthropic cloud Linux sandbox per `client/docs/cloud-audit-brief.md` (note: that file is
absent from this branch's working tree — deleted in `1bdf998e4` alongside unrelated doc cleanup; its
full text is still readable via `git show 7e9d3b377:client/docs/cloud-audit-brief.md`, and this pass
follows it from that recovered copy). `dotnet` is not installed on this sandbox at all, confirming the
brief's premise — no build, no analyzer, no test run backs any finding below. Every line is a
static-reading, grep-verified claim, not a compiled or tested one.

**PR #19 (14-item full pass) and PR #20 (4-item delta) are both still open at the same base scope.**
Seven commits touched `client/src` since PR #20's base (camera, toast/DPI, licence-notices work); this
pass re-read the current tree in full and only reports items neither #19 nor #20 already covers. One
item below (`ILockCardPhrasePool`) was independently re-surfaced here — it duplicates PR #19's item #8
and is **not** re-added to the count.

**No code changed. Nothing pushed to `feat/crossplatform`.** This PR adds one doc with an unapplied
cut-list for a Windows-capable reviewer to validate against the real gates
(`check-warnings.mjs --cold` then `check-floor.mjs`).

## Ranked cut-list (biggest first)

| # | Tag | What to cut | Replacement | Path | net lines |
|---|---|---|---|---|---|
| 1 | `stdlib:` | Manual whitespace-collapse loop (`StringBuilder` + `lastWasSpace` char scan) in `SanitizeTitleForWire` | `Regex.Replace(stripped, @"\s+", " ").Trim()` — the file already uses `Regex` two lines above for `EmailPattern`/`LongDigitsPattern`, so this is consistent with existing style | `Ai/AiPrivacyFilters.cs:168-185` | **~-17** |
| 2 | `shrink:` | `SanitizeId` (empty→`"session"`, else replace `PortablePath.InvalidFileNameChars` with `_`) duplicated byte-for-byte across two files — the second file's own doc comment says it sanitises "the same reason `ScriptedSessionLogStore` sanitises the same way" | move one copy into `PortablePath.cs` (which already owns `InvalidFileNameChars`); both call sites delegate | `Session/ScriptedSessionLog.cs:326-344`, `Session/CustomSessionStore.cs:240-258` | **~-15** |
| 3 | `stdlib:` | 7-line foreach-return loop checking "has any letter or digit" in `SanitizeEntry` | `if (!entry.Any(char.IsLetterOrDigit)) return null;` | `Ai/AiTitleAllowList.cs:102-108` | **~-6** |

**Total: roughly -38 lines**, on top of PR #19's -610 to -670 and PR #20's -58 to -68.

## Checked and rejected

- `ILockCardPhrasePool` — single implementation, no test double (`LockCardModuleTests.cs` constructs
  the concrete class directly at all 3 sites) — **already PR #19 item #8**, not re-added here.
- `IDocumentMigration`, `IDuckHandle` (structurally load-bearing, latter already covered by PR #20),
  `SoundArbitrationOptions.RecoveryCooldown`/`RecoveryFailureThreshold` (never overridden but part of
  an actively-tested tuning surface) — all single-impl but not YAGNI on inspection.
- Full `IDE0051`-class unused-private-method sweep across `client/src/CcpClient.Desktop` (998 private
  method declarations checked): zero with fewer than one real call site beyond their own declaration.
  Consistent with PR #20's own finding on the same axis — this codebase does not carry dead private
  methods.

## Out of scope, noted only (not fixed here)

`LoopbackServer.BindWithRetry` (`Features/Dtrh/LoopbackServer.cs:127-150`) allocates and discards up to
60 `HttpListener` instances in a tight loop on port collision. Performance, not YAGNI — named only, not
fixed, per this pass's scope.

`OverlayDisplays.Enumerate()` (Win32 monitor enumeration) has no "why not Avalonia's `Screens`" doc
comment, unlike its sibling `Win32TrayPresence`, even though `Screens` is used elsewhere in the
codebase (`IntakeHostWindow.axaml.cs:88`). Lower-confidence note, not a ranked finding: `Screens` is an
instance property of a `TopLevel` and `Enumerate()` is a static call usable before any window exists,
so the two may not be interchangeable — flagged for a reviewer to judge, not cut here.

## Observation, not a finding

Twenty `audit:` PRs (#1–#20) are open against `feat/crossplatform`, none merged, spanning
2026-08-22 through today — this routine has been producing a new PR roughly every 2 hours with no
action taken on prior ones. Not this pass's call to consolidate or close any of them, but worth an
owner's attention if the backlog itself is the more useful signal than any single delta.

## Verification

**Nothing in this list has been applied, built, or tested.** This sandbox cannot run
`node client/tests/floor/check-warnings.mjs` / `check-floor.mjs` meaningfully (Windows-only and
headed-desktop tests are in the pinned floor count) and cannot build the `net8.0-windows` shipping tree
at all. Per `docs/constitution.md`, a failed-or-unrunnable check is never accepted to keep work moving —
so this pass stops at a reviewed cut-list rather than landing any of it. A Windows-capable reviewer
applying any row here still owes the standard client gates before it counts as done.
