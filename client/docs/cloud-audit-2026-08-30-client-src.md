# Cloud audit — client/src YAGNI/efficiency pass, 2026-08-30

**Run:** scheduled cloud audit routine, Linux sandbox, zero prior context. **Scope:** `client/src`
only (per `client/docs/cloud-audit-brief.md` priority order). **Gates run:** none — no `dotnet` SDK
is installed in this sandbox, so `check-warnings.mjs`/`check-floor.mjs` could not be invoked even if
their counts would apply. This document and its cut-list are unverified findings, not landed changes.

## Housekeeping note

`client/docs/cloud-audit-brief.md`, which this routine is instructed to read and follow, no longer
exists on `feat/crossplatform` — it was deleted in `1bdf998e4` ("Refactor tests and documentation for
clarity and accuracy", 2026-08-22) alongside a legitimate, properly-declared floor-count bump for
retired workflow-archive guards. Nothing suggests the deletion was anything but an unrelated file
swept into that cleanup. Its full original text is still recoverable at `git show
7e9d3b377:client/docs/cloud-audit-brief.md` and is what this pass followed. Flagging this only so a
future run of this routine (or an owner) can decide whether to restore the brief under version control
— no fix applied here, since that's outside this pass's scope.

Corpus has grown since the brief's baseline measurement: **108,539 lines across 418 `.cs`/`.axaml`
files** in `client/src` today, versus the brief's ~74,500 lines / 319 files. The "already measured"
section of the brief (507 dead-zero public types, CA1823=0, 58/66 interfaces with a second
implementation or test double) is now stale by file count and was not re-verified here (no analyzer
run available on Linux); treat it as directional, not current.

## Method

Three independent read-only passes over `client/src`, each cross-checking findings against
`client/tests/CcpClient.Tests` and `client/tests/CcpClient.HeadlessTests` before reporting, and
declining to flag anything involving P/Invoke, COM interop, or platform-conditional code (unverifiable
from this sandbox):

1. Dead code / single-caller wrapper bloat in `Effects/`, `Features/*`, `Capabilities/`, `Views/`.
2. Hand-rolled code duplicating BCL/LINQ/Avalonia functionality.
3. Speculative abstraction — single-implementation interfaces with no test double, dead configurability.

## Ranked cut-list (biggest cut first)

`stdlib: manual foreach+return-true/false loop reimplementing Any(). Replace with urls.Any(m => index >= m.Index && index < m.Index + m.Length). [client/src/CcpClient.Desktop/Ai/AiPrivacyFilters.cs:392-403]`
net: -10 lines possible.

`yagni: BarkPipelineOptions — 9 of 11 properties (GlobalMinGap, SafetyHold, PriorityThreshold, RecencyMemory, GlobalMinGapExemptTriggers, BarkVolumeScale, MinSpeechDelaySeconds, AiSpeechBonusSeconds, LongTextThreshold, PerCharDelaySeconds) are read throughout BarkPipeline.cs but never constructed with a non-default value anywhere in src/ or tests/ (only Clock/Rng/LiveFields are ever overridden). Replace with internal const/static readonly fields; keep the options record only for the fields actually varied. [client/src/CcpClient.Desktop/Companion/BarkPipeline.cs:126-159]`
net: -10 lines possible.

`stdlib: manual foreach+return-true/false loop reimplementing Any(). Replace with IncognitoMarkers.Any(m => lower.Contains(m, StringComparison.Ordinal)). [client/src/CcpClient.Desktop/Ai/AiPrivacyFilters.cs:97-106]`
net: -8 lines possible.

`stdlib: manual foreach+return-true/false loop reimplementing Any(). Replace with RoleMarkers.Any(m => lower.StartsWith(m, StringComparison.Ordinal)). [client/src/CcpClient.Desktop/Ai/AiPrivacyFilters.cs:241-250]`
net: -8 lines possible.

`stdlib: manual foreach+return-true/false loop reimplementing Any(). Replace with _dials.Any(d => IsLinked(preset, d.Id) && StandsDownDuringSession(d.Id)). [client/src/CcpClient.Desktop/Effects/IntensityRampEffect.cs:630-641]`
net: -8 lines possible.

`stdlib: manual foreach+return-true/false loop reimplementing Any(). Replace with _fades.Values.Any(f => f.Out || f.Opacity < f.Target). [client/src/CcpClient.Desktop/Effects/FlashSurfacePresenter.cs:733-744]`
net: -8 lines possible.

`yagni: SoundArbitrationOptions.RecoveryCooldown and RecoveryFailureThreshold — read at SoundArbitration.cs:370,375,377 but never set to a non-default value anywhere, unlike the other options in the same class (MaxSfxVoices/TeardownBudget/DuckWatchdog/VoicePacingDelay) which tests do override. Replace with consts. [client/src/CcpClient.Desktop/Audio/SoundArbitration.cs:73,76]`
net: -2 lines possible.

`yagni: private static string Raw(TimeSpan value) => Clock(value); is a pure alias of Clock(TimeSpan) with no differing behavior, called only twice. Replace both call sites with Clock(...) and delete Raw. [client/src/CcpClient.Desktop/Views/Pages/SchedulerPanelNotices.cs:165]`
net: -1 line possible.

**Total: -55 lines possible across 8 sites, 5 files.**

The `Any()` replacements all need `using System.Linq;` added per file if not already present (already
idiomatic elsewhere: `AssetManifest.cs`, `AvatarSequenceEvaluator.cs`, `CompositeHapticSink.cs`, 20+
other files) — net counts above already treat that as a wash against the lines removed.

## Checked and found clean (not cuts — recorded so a future pass doesn't re-derive these)

- 682 private methods surveyed in `Effects/`, `Features/*`, `Capabilities/`, `Views/`; 345 called
  exactly once. Nearly all single-call-site methods are legitimate event-delegate signature adapters
  (`OnFired` → `Sync()`, `OnWebMessage` → `HandleWebMessageBody`, etc.) or string-keyed dispatch tables
  (`DtrhMeta.cs` `Op*` methods) — not wrapper bloat. Only one true no-value alias found (see cut-list).
- Manual byte/pixel-buffer loops and the documented manual string parsers (e.g.
  `AiPrivacyFilters.SplitSentences`) are deliberate perf/clarity choices, not stdlib gaps — not flagged.
- All 72 interfaces in `client/src/CcpClient.Desktop` surveyed against test doubles in both test
  projects. Every single-implementation interface without a literal fake resolved to either a
  documented OS-capability seam (`*Factory` classes switching on `OperatingSystem.Is*()`), documented
  owner-pending scaffolding (`IAiEndpointAdmissionPolicy`), or a type exercised directly across many
  tests with no fake needed (`IBubblePopSurface`, `ILockCardPhrasePool`, `IPopup`). None are safe cuts.
  `PersistenceStore<T>` has 30+ distinct document-type instantiations — load-bearing, not speculative.
- A closed-hierarchy switch's `_ => ... // unreachable today` catch-all arm in
  `VisualsPanelNotices.DescribeSurface` looks dead but is a repeated, deliberate codebase convention
  for exhaustiveness-proofing against future enum growth — not flagged.

## Out of scope for this pass

No correctness, security, or performance issues were found or looked for beyond what the above
findings already note. `client/tests` and `spine-tasks/` (brief priorities 2 and 3) were not audited
this run — this pass stayed focused on `client/src` per the brief's own "one focused pass per run"
instruction.

## Disposition

Per the brief's hard limit, nothing above has been applied to product code. All eight findings are
small, mechanical, and low-risk on their face (pure predicate loops, dead configurability), but none
have been verified against this repository's test floor or a Windows build from this sandbox, so they
are left for a maintainer with the ability to run `client/tests/floor/check-warnings.mjs` and
`check-floor.mjs` to apply and verify.
