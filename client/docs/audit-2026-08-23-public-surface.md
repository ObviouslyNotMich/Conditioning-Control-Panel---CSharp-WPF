# Client audit — public surface and Win32 chrome duplication (2026-08-23)

Scheduled cloud YAGNI/efficiency pass over `client/src` (~74,500 lines, 318 files), run from a
Linux sandbox per `client/docs/cloud-audit-brief.md` (see process note at the bottom — that file
no longer exists on this branch as of this run). Nothing here was changed in product code; this
is the report only. No gates were run (cannot run them on Linux; see brief).

Four sub-passes covered the whole of `client/src/CcpClient.Desktop` split by directory. Findings
below are consolidated and cross-checked against `client/tests/` (every candidate name was
grepped there before being listed) so nothing here would break test compilation if actually
applied.

## Cut-list, biggest cut first

**1. `shrink:` Win32 window-chrome logic is triplicated across three capability seams.**
`ReadZOrder`/`ZOrderPosition`, `ApplyClickThroughStyle`, `EnsureWindow`, and related hit-test
scaffolding appear near-verbatim in:
- `Glyph/Win32GlyphSurface.cs` (968 lines) — `ReadZOrder`/`ZOrderPosition` at 865–903, `EnsureWindow` at 905–953
- `Input/Win32InputPresence.cs` (1010 lines) — same shapes at 905–947, 948–995
- `Overlay/Win32OverlayPresence.cs` (1020 lines) — same shapes at 915–954, 955–1005

A shared `Win32WindowChrome` helper parameterized on the handful of differing interop symbols
would collapse three ~60-line blocks into one. `net: -100 to -150 lines possible.`

Caveat: each file's comments frame the triplication as a deliberate per-capability-seam boundary
(Glyph/Input/Overlay are independent platform seams by design per `docs/constitution.md`'s
boundary rules). This is a design conversation, not a blind extract — flagging, not proposing.

**2. `yagni:` ~30 public types are reachable only from inside `CcpClient.Desktop` itself.**
There is no `InternalsVisibleTo` anywhere in the repo, so `client/tests` compiling against a type
is the only thing that can force it public. Each name below was individually grepped against
`client/tests/` and has zero references there — narrowing to `internal` is a same-assembly,
zero-behavior-change encapsulation win. `net: 0 lines` (visibility-only), but meaningfully
tightens ~30 declarations' exposed surface in one low-risk pass:

- `Persistence/DemoSettings.cs:11` `IDocumentMigration` (single impl `DemoMigrationV0ToV1`, consumed only by `PersistenceStore`)
- `Scheduling/SessionScheduler.cs:35` `SchedulerTickOutcome`
- `Video/UnsupportedVideoPresence.cs:14,61` `UnsupportedVideoPresence`, `UnsupportedVideoClipSource`
- `Views/Pages/SchedulerPanelNotices.cs:24`, `Views/Pages/VideoPanelNotices.cs:18` (static formatters, consumed only by `StudioPage.axaml.cs`)
- `Input/UnsupportedInputPresence.cs`, `Overlay/UnsupportedOverlayPresence.cs` (constructed only inside their own factory's `switch`; consumers pattern-match against the public interface, never the concrete type)
- `Haptics/HapticLimb.cs:12` `HapticMoment` enum (used only by that file's own private methods)
- `Features/Dtrh/DtrhRunConfig.cs:17` `DtrhRunConfig` (static class, used only from `DtrhMeta.cs`)
- `Features/Dtrh/DtrhAssetStats.cs:9` `DtrhAssetStat`, `:33` `DtrhAssetStatsDocument`
  — **not** `DtrhAssetStats` itself (line 55): that class is directly `new`'d in
  `client/tests/CcpClient.Tests/DtrhM2TestFixtureTests.cs` and `DtrhMetaTests.cs`, so it must
  stay public. (One of the four sub-passes initially flagged all three names together; verifying
  against `client/tests` caught this before it went in the list.)
- `Features/Dtrh/LibVlcDtrhVideo.cs`, `SoundFlowDtrhAudio.cs` — concrete backends behind
  `IDtrhVideoBackend`/`IDtrhAudioBackend`, only ever constructed from composition wiring; keep the
  interfaces public
- `Features/AvatarTube/AvatarBitmapCache.cs`, `Features/Intake/IntakeHarness.cs`,
  `Features/SyntheticPopupContent.cs`, `Features/Dtrh/DtrhMeta.cs:10` `DtrhRanks`,
  `Features/QuickToggleDispatch.cs`
- DTO/options records with no cross-assembly touch: `DtrhHarnessOptions`, `DtrhSlotIndex`,
  `DtrhSlotSummary`, `ChaosSfxChain`, `GoonCaps`, `GoonDoorRefusal`, `GoonPayloadProbe`,
  `IntakePayloadProbe`, `IntakeHarnessOptions`, `IntakeDraftKnobs`, `DtrhProcessFailedSignal`,
  `PermissionDenySignal`
- `Progression/GradedRunAwards.cs:132` `GradedRunAwardOutcome` — **not** `GradedRunAwards` or
  `GradedRunAwardsDocument`: both are directly constructed in
  `client/tests/CcpClient.Tests/GradedRunAwardsTests.cs`, so they must stay public. Same
  correction as `DtrhAssetStats` above — flagged by the sub-pass, removed after the test-reference
  check.

This is a bulk `public` → `internal` find-and-replace across ~30 declarations, doable as one
mechanical change. **Not applied here** — this sandbox has no .NET SDK and cannot compile, so a
visibility change is unverified work until someone with a working build confirms it (constitution:
"a change that looks correct here is still unverified work").

**3. `yagni:` (unverified, lower confidence) — `Views/Pages/CompanionPage.axaml.cs:9` and
`SystemPage.axaml.cs:22`** are `UserControl`s constructed only by `MainWindow.axaml.cs`. Avalonia's
compiled-XAML loader is reported to accept `internal partial class` for `x:Class`, which would make
these internal-candidates too — but there is no existing `internal partial class` `UserControl` in
`client/src` to confirm that against, and this cannot be compiled from Linux. **Do not apply without
confirming on a real build first.**

## Directories audited and found lean (no findings)

`Ai`, `Audio`, `Camera`, `Capabilities`, `Companion`, `Entitlement`, `Pointer`, `Tray`, `Motion`,
`Manifest`, `Navigation`, most of `Persistence`, `Chaos`, `Goon`, most of `AvatarTube`. Zero unused
private methods were found anywhere in `client/src` (all ~550+ private methods checked across the
four sub-passes are called at least once in their own file) — this extends the already-measured
"507 public types, zero dead" baseline down to the private-method level. No `stdlib:`/`native:`
findings: hand-rolled-looking code (manual PATH search in `Persistence/SecretStores.cs`, manual
retry in `Ai/LoopbackOllamaProvider.cs`, manual sentence-splitting in `Ai/AiPrivacyFilters.cs`)
each carries an explicit comment justifying the departure from a one-line BCL call (WPF-parity
ports, or a capability the BCL API doesn't expose directly).

## Correctness/security notes (out of scope for this pass, not fixed)

- `Persistence/SecretStores.cs`: resolves a secret-tool-style binary by walking `PATH` manually —
  not an issue found, just naming the attack surface of resolving an executable by bare name.
- `Features/Dtrh/DtrhHostWindow.axaml.cs` (1323 lines) and `DtrhMeta.cs` (1027 lines) are large
  god-files; worth a future split, not a bug.
- `Input/Win32InputPresence.cs`: `TakeForeground` polls OS foreground state in a bounded retry
  loop — fine as-is, worth a look if timing ever needs tuning.
- `Haptics/ButtplugHapticSink.cs`, `CompositeHapticSink.cs`: `lock (_gate)` around state also read
  via `Task` continuations — not reviewed for lock-scope correctness, flagged only in passing.

## Process note

`client/docs/cloud-audit-brief.md` — the file this routine is supposed to read — was deleted from
`feat/crossplatform` in commit `1bdf998e4` ("Refactor tests and documentation for clarity and
accuracy", 2026-08-22), alongside `client/tools/port-audit-prompt.md`. Its content (recovered from
git history) is still consistent with the current `CLAUDE.md` and `docs/constitution.md`, so this
run proceeded using that recovered content. If the deletion was intentional (e.g. its guidance
folded into `docs/constitution.md`), no action needed; if it was accidental fallout of that
refactor, future scheduled runs will hit a missing file every time.
