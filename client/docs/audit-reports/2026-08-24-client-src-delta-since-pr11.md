# Cloud audit — client/src delta since PR #11

Scheduled cloud audit routine, run from a Linux sandbox against `feat/crossplatform` at
`e18b4019ec00375371fb84db343fb4d58cde7033`. Base for this delta: PR #11's base,
`74268631be8e0ab0a9ece24b0835052e7eb4b977`.

## Process note: the standing brief is still missing

`client/docs/cloud-audit-brief.md` still does not exist on `feat/crossplatform`. It was added in
`7e9d3b377` and deleted in `1bdf998e4` ("Refactor tests and documentation for clarity and accuracy")
on 2026-08-22. PRs #2 through #11 already flagged this exact gap and recovered the brief's content
from git history; nothing has changed since, so this pass did the same recovery and used that
content as the operative brief. Restoring the file, or inlining its content into the routine's
stored prompt, would remove the need for every future run to repeat this recovery.

Separately: **PRs #1 through #11**, opened by this routine between 2026-08-22 and 2026-08-24, are
all still open and unmerged — none can land without a Windows/WSLg checkout to run the client gates.
This routine has no ability to close, merge, or triage them.

## The one hard limit

**Nothing was landed.** No push to `feat/crossplatform`, no merge, no test/pin/gate weakened. This
sandbox has no Windows/WSLg display, so `check-floor.mjs`, `check-warnings.mjs`, and every headed
verification command are unrunnable and are not claimed.

## Scope

Restricted to the delta since PR #11's base commit (`7426863`..`e18b401`): 29 files, ~3,636 added
lines, almost entirely one feature slice — the app-wide audio lift (`Audio/AudioParticipant.cs`,
`AudioSettingsDocument.cs`, `Effects/EffectSounds.cs`), the typed mantra minigame port
(`Features/Mantra/*`), the Trainer Card level display (`Features/Progression/TrainerCard.cs`), and
the audio-dials Studio UI (`Views/Pages/AudioDialsNotices.cs`, `StudioPage.axaml(.cs)`). PR #11
already covered everything before this delta and found it clean. Correctness, security, and
performance are out of scope.

This pass split the delta four ways (audio subsystem; the Mantra feature; the touched Effects/Dtrh/
Intake hunks; progression + views + composition wiring) and read each file's full diff and current
content against `client/src` and `client/tests` for callers and test doubles before flagging
anything.

## Ranked cut-list (biggest cut first)

**1.** `yagni: MantraLaunch.Random property is a dead pass-through with no caller or test.` None of
the sibling launchers (`GoonLaunch.cs`, `IntakeLaunch.cs`, `DtrhLaunch.cs`) expose an analogous
`Random` property, the real caller (`MainWindow.axaml.cs`) never sets it, and no test in
`client/tests` sets `launch.Random` — only `MantraSession`'s own `random:` constructor parameter is
exercised directly (`MantraSessionTests.cs:504`). Drop the property; `MantraSession` still takes its
own `random:` param for tests.
`[client/src/CcpClient.Desktop/Features/Mantra/MantraLaunch.cs:83,128]`
`net: -2 lines possible.`

**2.** `delete: TrainerCardLevel.Heading const ("Level") is unused since introduction.` Confirmed by
exact-word grep across `client/src` and `client/tests` — no production caller and no reflection-based
census test (`TrainerCardCensusTests.cs`, `TrainerCardLevelTests.cs`,
`TrainerCardLevelPresentationTests.cs`) references it. It was added fresh in this delta, not carried
over from before.
`[client/src/CcpClient.Desktop/Features/Progression/TrainerCard.cs:444]`
`net: -1 line possible.`

**Total: ~-3 lines out of ~3,636 new lines in scope (well under 1%).**

## What was checked and found clean

- **Audio subsystem** (`AudioParticipant.cs`, `AudioSettingsDocument.cs`, `AudioCuePool.cs`'s new
  without-replacement path, `EffectSounds.cs`): every public member traced to a live production
  caller (`StudioPage.axaml.cs`, `MainWindow.axaml.cs`, `DtrhHostWindow.axaml.cs`,
  `SessionParticipant.cs`) and a direct test assertion. The new shuffle-and-deal-without-replacement
  path in `AudioCuePool` is deterministically tested end-to-end (fixed-seed full-bag exhaustion and
  refill).
- **Mantra feature**, otherwise: `MantraIntensity.DroneGain` looks unused at a glance but
  `MantraSessionTests.cs:719-740` explicitly asserts no file consumes it — a documented, deliberate
  audio-seam gap, not dead code. `Show`, `DataDirectory`, `Pool`, `Clock` are genuine test seams
  exercised in `MantraWindowHeadlessTests.cs`/`PlayPageHeadlessTests.cs`. `MantraColour.Lerp` is a
  byte-truncating lerp matching upstream bit-for-bit by design (commented), not a `Color.Lerp`
  swap-in. `Match`/`StateOf` are a distinct per-character highlight algorithm from
  `LockCardTyping`'s whole-string accept/reject rules — not a duplicate.
- **Effects/Dtrh/Intake hunks**: `onPop` callback threading, `FlashImagesEffect`'s `_sounds`/
  `Deliver()` wiring, and the `DtrhBarkRouting`/`DtrhHostWindow` `masterVolume` plumbing are all live,
  tested wiring, not speculative surface. `FlashImagesEffect.LastSound`/`ClipFolder` mirror an
  established sibling API shape (other effects' `ClipFolder`) and have test coverage, even though no
  Studio panel reads them yet — noted, not flagged, since the shape match and tests argue seam over
  YAGNI.
- **Progression/views/composition**: `AudioDialsNotices.cs` (new, 246 lines) does not duplicate the
  existing `AudioPanelNotices.cs` (130 lines) despite the similar name — the two describe different
  domains (per-module cue/clip state vs. app-wide volume/endpoint/device state) with no overlapping
  methods. `CompositionRoot.cs`'s new `AudioParticipant`/`MantraLaunch` wiring is single concrete
  app-lifetime ownership, not a speculative interface. Every other new const/property/method in
  `TrainerCard.cs`, `IntakePage`, `PlayPage` resolved to a real call site and a direct test reference.

## Correctness note, out of scope, not fixed here

None found worth naming this pass (PR #11 already named the one open `StudioPage.axaml` tag-nesting
item from the prior delta; not re-checked here since it predates this delta).

## Not done, and why

No code changes were made anywhere. This sandbox cannot build the Windows-targeted solution or
produce headed evidence, so a change proposed here would be unverified work. Both items above are
proposals for a human, or a future session with working gates, to act on and verify.
