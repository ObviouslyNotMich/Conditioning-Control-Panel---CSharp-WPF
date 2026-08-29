# Cloud audit — client/src YAGNI delta since PR #27

Scheduled cloud YAGNI/efficiency audit pass, run from a Linux sandbox per
`client/docs/cloud-audit-brief.md` (recovered from history — see below). Commit audited:
`1d033c85f`, identical to the base commit of PR #27, meaning **zero commits have landed on
`feat/crossplatform` since the last pass**. This pass exists to find anything the prior 27 passes
missed, or to say honestly that nothing new exists.

**Zero product code changed.** This PR adds only this findings doc. No push to `feat/crossplatform`,
no merge, nothing weakened.

## Brief status (unresolved since 2026-08-22)

`client/docs/cloud-audit-brief.md` is still absent from `feat/crossplatform`'s tree — deleted in
`1bdf998e4` alongside unrelated doc cleanup, flagged by every audit pass since (#6, #13, #18, #19,
#21, #26, #27). This run again recovered its text via `git show 7e9d3b377:client/docs/cloud-audit-brief.md`
and followed it, including its hard limit: this sandbox has no `.NET` build tooling capable of running
`client/tests/floor/check-warnings.mjs` / `check-floor.mjs`, confirming the brief's premise that this
repo's gates cannot run from Linux.

## Backlog status

27 prior `audit:` PRs (#1–#27) are open against `feat/crossplatform`, none merged or closed. This is
#28. Not this pass's call to consolidate or close the backlog, but the pile continuing to grow with a
same-commit, near-duplicate pass is itself worth the owner's attention.

## Method

Read PR #1, #5, #6, #10, #13, #18, #19, #21, #26, #27 in full to map what is already covered, then
independently re-scanned `client/src` (~74,500 lines, 319 files) for anything outside that map,
focused on axes least likely to have been exhausted: unused private methods (re-confirmed closed —
this axis has now been re-verified clean six times: #1, #6, #10, #18, #21, #27) and CA1515
public-surface narrowing, using a member-access-level cross-check against `client/tests` (not just
type-name grep, since several tests reach into a record's properties via `var`/lambda without ever
spelling the type name) to avoid the false positives a naive scan produces.

## Already covered (not repeated here)

- Win32 interop/chrome duplication (largest item, ~350–400 lines): #1, #5, #6, #19 — still open,
  unapplied.
- `JsonElement` field-reader helper duplication (8 sites): #1, #5, #26.
- Payload-root probe triplication, DPAPI interop, `SessionParticipant` field-listing, `StudioPage`
  slider/switch handlers, preset-doc `JsonExtensionData`, gate-decision shape duplication,
  `VideoFrame`/Letterbox array duplication: #1, #5, #10, #19.
- Dead-class findings (`AiCommandEnvelope`/`AiCommandExecutor`, `MainWindowViewModel`,
  `LevelUnlocks`, `HapticReasonCodes.HapticNotEntitled`, `HapticGate.DeniedTitle`,
  `BubblePopSurfacePresenter.SpawnOnce`, `StudioPickerNotices.SpiralLibraryEmpty`,
  `BubbleCountGame.PicturesPainted`, `SyntheticAvatarPacks.FileSha256`): #5, #10, #19.
- `yagni:` single-impl interfaces (`IBubblePopSurface`, `ILockCardPhrasePool`,
  `IAiDiagnosticsSink`, `IAiEndpointAdmissionPolicy`, `IDuckHandle`): #5, #13, #19.
- `stdlib:` candidates (`Regex.Replace` whitespace collapse, `BinaryPrimitives`, `Math.Clamp`,
  `.Distinct()`, Base64Url, `SanitizeId` dedup, `char.IsLetterOrDigit`): #5, #13, #21.
- CA1515 public-surface narrowing: #6 named ~30 candidates; #27 applied the 3 safest
  (`RuntimeSessionEnvironment`, `HostTokenReaders`, `BarkRuleSet`).

## Cut-list (new this pass)

| # | Tag | What to cut | Replacement | Path | net |
|---|---|---|---|---|---|
| 1 | `yagni:` | `ArcademyAttendOutcome.AlreadyOpen` — constructed only in-file, zero external/test references; `ArcademyDoor.Available` is a hardcoded `false`, so this case is unreachable in a shipped build | `internal sealed record` | `client/src/CcpClient.Desktop/Features/Arcademy/ArcademyLaunch.cs:87` (constructed at `:109`) | visibility-only |
| 2 | `yagni:` | `AvatarEvidence.SampleLine` / `AvatarEvidence.TraceLine` — used only inside the declaring file for JSON write/read of the evidence-harness log; the one test on this path (`AvatarPackTests.cs`) parses the JSON text, never references the record type | `internal sealed record` (both) | `client/src/CcpClient.Desktop/Features/AvatarTube/AvatarEvidence.cs:25` and `:28` | visibility-only |

**Caveat inherited from every prior pass:** unverified against a real compile (no .NET SDK in this
sandbox). `AlreadyOpen` is nested three deep (`ArcademyLaunch.ArcademyAttendOutcome.AlreadyOpen`) —
confirm the outer pattern-match in `PlayPage.axaml.cs` still compiles with an internal case type.
`TraceLine`'s `JsonSerializer.Deserialize<TraceLine>` (line ~179) should work with an internal type
in the same assembly, but is reflection-based and worth confirming. A Windows-capable reviewer
applying either row still owes `check-warnings.mjs --cold` then `check-floor.mjs`.

No line-count reduction from either item — visibility-only edits.

## Also checked, no findings

- `Gaze/GazePreprocess.cs` (added 2026-08-25) has zero production callers, matching the shape of a
  "delete: unwired" finding, but `client/docs/onnxruntime-package-admission.md` documents it as a
  deliberately staged port awaiting a named, multi-step admission decision (model licensing,
  third-party notices, native-binary trimming). Correctly excluded, not flagged.
- A heuristic CA1515 scan (public types with ~0 references outside their declaring file) initially
  returned 8 hits; member-access-level cross-checking against `client/tests` eliminated 6 as unsafe
  (`AiEscalationState`, `DuckAttempt`, `DtrhPayloadProbe`, `AvatarLayerState`,
  `AssetVerificationFailure` are all touched cross-assembly by tests via property access without a
  literal type-name hit; `AiCommandExecution` is subsumed by the already-flagged dead
  `AiCommandExecutor` subsystem, #5). Only the two rows above survived.
- No new `stdlib:`, `native:`, `delete:`, or `shrink:` material beyond #1–#27.

## Correctness findings

None new. Two previously-recorded observations (`BarkRules.cs:208` vs `DtrhBarkRouting.cs:109`
`ToValue` cast divergence, #26; `LoopbackServer.BindWithRetry` HttpListener churn and
`OverlayDisplays.Enumerate` vs Avalonia `Screens`, #21) were spot-checked and are still present,
unchanged, on the unchanged tree. Not fixed here — out of scope for this pass.

## Verification

Nothing above has been applied, built, or tested. No test, pin, or gate was touched or weakened.
