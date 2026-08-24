# Cloud audit — client/src YAGNI/efficiency pass, 2026-08-24 (fourth independent pass)

Scheduled Linux cloud sandbox run, same routine and scope (`client/src`) as the three passes
already on this PR. Seven parallel read-only reviewers each covered a disjoint subsystem; every
candidate was cross-checked against `client/tests` for a test double, and against the rest of
`client/src` for a cross-directory reference, before being reported. **No files under `client/src`
were changed.** Correctness, security, and performance stayed out of scope.

Deduped against passes 1-3 before writing this up. Two of this pass's raw findings were already
reported: `IBubblePopSurface`/`ILockCardPhrasePool` (pass 1). One raw finding was already
investigated and explicitly *rejected* by pass 3 — the apparent hand-rolled Fisher-Yates shuffle in
`Effects/VideoClipPool.cs`/`PopQuizAsk.cs` produces a different permutation than `Random.Shuffle<T>`
for the same seed, which pass 3 judged a deliberate parity pin rather than an oversight. Not
re-raised here; that call stands.

## Findings, ranked biggest cut first

**1. `delete` A "not a product feature" demo timer, shipped live in every session.**
`HeartbeatParticipant`'s own doc comment calls it a "[d]emonstrator participant... It is not a
product feature" — yet it is registered in the real composition root and its tick text is rendered
on the shipped System page, so every real launch runs a 250ms-forever background timer and posts to
the UI for a feature that documents itself as not meant to ship.
`client/src/CcpClient.Desktop/Lifecycle/Participants.cs:26-114` (class),
`client/src/CcpClient.Desktop/Lifecycle/CompositionRoot.cs:298` (production registration),
`client/src/CcpClient.Desktop/Views/Pages/SystemPage.axaml.cs:77-79` (System page wiring).
Caveat: `SchedulerModuleTests.cs`, `IntegrationProofTests.cs`, `CompositionRootValidationTests.cs`,
and `AsyncLifecycleTests.cs` construct this class directly to exercise the async-lifecycle-fault
contract end-to-end — a legitimate use as a test fixture. The cut is the **production wiring**
(composition-root registration + System-page UI plumbing), not necessarily the class itself; moving
it to test-only code vs. keeping it as a documented demo is an owner call.
`net: -95 lines possible.`

**2. `shrink` Identical "spread ~N samples over an area" stride formula hand-duplicated three times.**
`Math.Max(floor, (int)Math.Sqrt((double)width*height/target))` — only the floor/target constants
differ. Distinct from pass 3's `ZOrderPosition`/`ReadZOrder` triplication (different method,
different purpose).
`client/src/CcpClient.Desktop/Input/Win32InputPresence.cs:920-929`,
`client/src/CcpClient.Desktop/Overlay/Win32OverlayPresence.cs:719-720`,
`client/src/CcpClient.Desktop/Pointer/Win32PointerSurface.cs:718-719`
`net: -10 lines possible.`

**3. `shrink` Three hand-rolled loops in `OverlaySurfaceSet` are one-liners with LINQ already used
elsewhere in the same file.**
`_slots.Count(s => s.Live)`, `_slots.Where(s => s.Live).Select(s => s.Bounds).ToList()`,
`_slots.FirstOrDefault(s => !s.Live)` (reference-type `Slot`, so null-on-miss matches the existing
contract).
`client/src/CcpClient.Desktop/Effects/OverlaySurfaceSet.cs:165-239`
`net: -15 lines possible.`

**4. `shrink` `Intake`/`Arcademy`/`Goon` triplicate three separate helper shapes across their
feature trees, not just the one pass 3 already flagged.**
Pass 3 already flagged `IntakeServingRoots.Probe`/`ArcademyServingRoots.Probe` and
`IntakeProtocol`/`ArcademyProtocol`'s JSON field readers as pairwise duplicates — both also exist
identically in `Goon/GoonServingRoots.cs:13-66` and `Goon/GoonProtocol.cs:376-397` (Goon's version
additionally has `GetLong`), making each a triplicate rather than a pair. New, not previously
flagged: an identical private `LogSinkAdapter : ILogSink` nested type in three launch/context types
(each doc comment cross-references the other two as "same shape"), and an identical
`SafeAdapterInfo()` try/catch in two host windows.
`client/src/CcpClient.Desktop/Features/Intake/IntakeHostContext.cs:242-245`,
`client/src/CcpClient.Desktop/Features/Arcademy/ArcademyLaunch.cs:198-201`,
`client/src/CcpClient.Desktop/Features/Goon/GoonLaunch.cs:116-119`,
`client/src/CcpClient.Desktop/Features/Intake/IntakeHostWindow.axaml.cs:253-257`,
`client/src/CcpClient.Desktop/Features/Goon/GoonHostWindow.axaml.cs:319-323`
`net: -25 lines possible (new items only; the Goon extension to pass 3's two findings is additional
on top of pass 3's own totals).`

**5. `shrink` `CurrentPlatform()` duplication (pass 2's finding) extends to a fifth factory.**
Pass 2 flagged this in Camera/Audio/Glyph/Input factories. The same shape also exists in
`Video/VideoPresenceFactory.cs:164-182`, not on pass 2's list.
`net: -19 lines possible (this file only; folds into pass 2's proposed shared helper).`

**6. `delete` `HapticShapes.SpanMs` has zero production callers.**
Only `HapticEnvelopeTests.cs` calls it; `Render`/`Append`/the rest of the file are genuinely used by
`HapticEnvelope.cs` itself.
`client/src/CcpClient.Desktop/Haptics/HapticEnvelope.cs:296-311`
`net: -16 lines possible.`

**7. `stdlib` Hand-rolled base64url token encoding.**
`Convert.ToBase64String(...).Replace('+','-').Replace('/','_').TrimEnd('=')` →
`System.Buffers.Text.Base64Url.EncodeToString(...)` (net10.0 target has this since .NET 9).
`client/src/CcpClient.Desktop/Features/Dtrh/DtrhParticipant.cs:59-60`
`net: -2 lines possible (mechanical swap, small).`

**8. `shrink` `PrimaryDisplayPlacement` hand-rolls an indexed scan for the primary display with a
fallback that `List<T>.FirstOrDefault(predicate, default)` (.NET 6+) already does in one line.**
`displays.FirstOrDefault(d => d.IsPrimary, displays[0]).Bounds`
`client/src/CcpClient.Desktop/Effects/PrimaryDisplayPlacement.cs:43-53`
`net: -8 lines possible.`

**9. `yagni` `IAiEndpointAdmissionPolicy` — one production implementation
(`LoopbackOnlyAdmissionPolicy`, itself a private-constructor singleton), no test double; every test
constructs `AiOperationPipeline` with the real instance. Distinct from pass 2's `IAiDiagnosticsSink`
(different interface, different file).**
Replacement: a plain static `AiEndpointAdmission.IsAdmitted(AiEndpointClass)` check inlined at the
one call site in `AiOperationPipeline`.
`client/src/CcpClient.Desktop/Ai/AiEndpointAdmission.cs:1-28`
`net: -8 lines possible.`

**10. `yagni` (visibility only, no line-count cut) Public types with zero references outside their
own directory in `client/src`/`client/tests` — narrow to `internal`.**
- Effects: `AudioCueFiring`, `BubbleCountFiring`, `LockCardFiring`, `MandatoryVideoFiring`,
  `PopQuizFiring`, `SubliminalFiring`, `AnimatedImageProfile`, `BouncingLogo`, `CountedBubble`,
  `IntervalLaw`.
- Features: `IntakeHarness`, `GoonCaps`, `AvatarBitmapCache`, `SystemAvatarClock`,
  `AvatarEngineOptions`, `AvatarLayerState`, `AvatarFrameEventArgs`, `AvatarTraceEventArgs`,
  `ArcademyLocalAssets`, `CompanionPermissionRow`.
- Haptics/Audio/Camera/Video: `HapticMoment`, `DuckAttempt`, `IDuckHandle`,
  `MediaFoundationCameraCapture`, `UnsupportedCameraCaptureSource`, `Win32VideoInterop`.
- Dtrh: `DtrhAssetStat`, `DtrhAssetStatsDocument`, `DtrhRanks`, `ChaosSfxChain`, `DtrhRunConfig`,
  `DtrhSlotIndex`, `DtrhSlotSummary`, `LibVlcDtrhVideo`.

**Flagged for visibility only, not recommended (low confidence):** `Progression/XpCurve.cs:192-221`
`LevelUnlocks` has zero production callers, but its own doc comment says the numbers are "kept as a
record rather than enforced" — a legitimate reason to keep it as documentation. Not counted below.

**Total this pass: ~198 lines across 9 line-count findings, plus a ~38-type visibility batch (no
line-count impact) and one Goon extension to two of pass 3's existing findings.**

## Scope covered

`Effects/`, `Views/Navigation/Manifest/Motion` (no findings — lean already), `Features/{Dtrh,
Mantra,Chaos,Progression}`, `Features/{Intake,Arcademy,AvatarTube,Goon,Companion}`,
`Session/Ai/Persistence/Storage/Scheduling`, `Haptics/Audio/Camera/Video`,
`Input/Overlay/Glyph/Pointer/Tray/Lifecycle/Companion/Entitlement/Capabilities` — all of
`client/src/CcpClient.Desktop` again, independently of the three prior passes' split.

## On the missing brief and the weakened floor guard

Already covered in detail by the PR description and the second- and third-pass comments on this
PR — not repeated here. Still unresolved as of this pass; no action visible on `feat/crossplatform`
or `main` addressing the floor/guard-test change in commit `1bdf998e4`.

## Out of scope this pass

`client/tests` and `spine-tasks/` remain uncovered by all four passes on this PR.
