# Cloud audit — client/src YAGNI/efficiency pass, 2026-08-24 (second independent pass)

Scheduled Linux cloud sandbox run. No `.NET` SDK is present on this machine, `client/` targets
`net10.0`/Avalonia 12.1.1 but nothing built or ran here — every finding below is from static
reading only, cross-checked against `client/tests` for test doubles before anything was called
speculative. **No files under `client/src` were changed by this pass.**

This is a second, independent run of the same scheduled routine that opened this PR. It covers
the same scope (`client/src`) and found a different, non-overlapping set of candidates than the
PR description's own list — expected, since this kind of pass is inherently non-exhaustive.
Appended here rather than opening a second competing PR for the same day/scope.

## Before the audit: a process finding that isn't in scope to fix, but needs a human look

This routine is supposed to start by reading `client/docs/cloud-audit-brief.md`. That file does
not exist on `feat/crossplatform` — it was deleted by commit `1bdf998e4` ("Refactor tests and
documentation for clarity and accuracy", 2026-08-22 22:48 JST), on this branch's first-parent
line. The PR description already flags the brief's deletion; this pass adds detail on what else
that same commit changed, bundled under that one broad message:

- Removed a residue-check gate (`assertNoCommittedResidue`) from `client/tests/floor/check-floor.mjs`.
- Lowered the pinned test floor in `client/tests/floor/floor.json` from 2625 to 2608 (-17), logged
  as "removed retired workflow-archive guards and their seventeen archive-only unit tests."
- Deleted `WarningGateGuardTests.TheGateIsNamedInTheWorkflowTheHarnessAndTheAuditorPrompt` —
  the one test whose specific job was to fail if the warning gate stopped being referenced from
  `port-workflow.md`, `verification-harness.md`, and the (now also deleted) auditor prompt.
  It also deleted `client/tools/port-audit-prompt.md`, one of the three files that test checked.

This may all be legitimate cleanup — the three items above could be exactly what the commit
message says. But this pass has no way to verify that from a Linux sandbox with no SDK, and it is
the exact shape of thing `docs/constitution.md`'s change-discipline rule ("never weaken a guard to
make a step pass") and the deleted brief itself were written to catch. Named here, not fixed —
per this pass's own scope, correctness/process findings are reported, not corrected. The owner
has been notified separately (push notification from this run).

The brief's actual rules (recovered from `git show 7e9d3b377:client/docs/cloud-audit-brief.md`)
are what this pass followed: no landing, no gate/test weakening, `client/src` in scope, tag-and-
rank cuts, "changing nothing and reporting accurately is a good outcome."

## Findings, ranked biggest cut first

**1. `yagni` Delete the secret-storage platform abstraction — zero production callers.**
`Persistence/SecretStores.cs` (423 lines: `PlatformSecretStore`, `WindowsDpapiSecretStore`,
`SecretToolSecretStore` + its `secret-tool` subprocess plumbing, `UnsupportedSecretStore`,
`DpapiNative` P/Invoke) plus `ISecretStore` in `Persistence/PersistenceStore.cs:538-545`.
`PlatformSecretStore.ForCurrentPlatform(...)` is called nowhere in `client/src` — confirmed by
grep: every non-declaration hit is a doc-comment mentioning `ISecretStore` by name, not a call
site. `Lifecycle/CompositionRoot.cs` never references it. `client/docs/task-board.md` row P2 marks
the tier/entitlement consumer that would need this as **owner-blocked, undecided** — this is
infrastructure staged ahead of an unapproved feature, not simply forgotten code.
Replacement: nothing, unless/until the P2 decision lands, at which point this is the seam to keep.
`net: -437 lines possible.`

**2. `shrink` Four capability factories duplicate the same host-platform enum and switch.**
`Camera/CameraDeviceSourceFactory.cs:5-11,58-76`, `Audio/AudioPresenceFactory.cs:4-14,103-121`,
`Glyph/GlyphSurfaceFactory.cs:4-14,104-122`, `Input/InputPresenceFactory.cs:4-14,111-129` each
redeclare an identical `{Windows, Linux, MacOs, Unknown}` enum and an identical 19-line
`CurrentPlatform()` (`OperatingSystem.IsWindows()/IsLinux()/IsMacOS()` cascade). The per-capability
`For(...)`/`CreateFor(...)` arms carry real, distinct unsupported-reason text and should stay;
only the enum + platform-detection method are pure duplication.
Replacement: one shared `Capabilities/HostPlatform.cs` referenced from all four factories.
`net: -70 to -90 lines possible.`

**3. `shrink` Program.cs and App.axaml.cs each thread 24 positional demo/harness flags.**
`Program.Main` builds 24 independent locals (`popupDemo`, `avatarDemo`, `dtrhDemo`, `loomDemo`,
`intakeDemo`, `tunnelDemo`, `goonDemo`, and their per-feature sub-flags), passes them positionally
through `BuildAvaloniaApp(...)` into `new App(...)`, which redeclares 24 matching fields and 24
one-line constructor assignments. `Program.cs:266-270,334-352`, `App.axaml.cs:13-37,40-77`.
Replacement: one `sealed record DemoLaunchOptions(...)` built once in `Main`, threaded as a single
parameter through both signatures — same values reach `App` unchanged.
`net: -45 to -55 lines possible.`

**4. `shrink` Ephemeral-port bind-retry loop is duplicated between DTRH and Chaos loopback servers.**
`Features/Dtrh/LoopbackServer.cs:119-148` (`BindWithRetry`, 28 lines: try 60 random ports in
49152–65536, fresh `HttpListener` per attempt, throw on exhaustion) is copied inline — not called
— into `Features/Chaos/ChaosTunnelLoopback.cs:56-87`; the Chaos file's own comment cites the
`LoopbackServer.cs` lines it copied from. The repo's "DTRH `LoopbackServer` is not reused" ruling
covers route/security logic (`TryResolve`, MIME sniffing, refusal codes), which genuinely differs
between the two servers and should stay separate — the port-binding loop has nothing to do with
routes and is safe to share.
Replacement: one shared static helper, called from both.
`net: -25 to -28 lines possible.`

**5. `delete` Orphaned reason-code constant `EffectReasonCodes.PointerRewardChainAbsent`.**
`Session/EffectReasonCodes.cs:295-310`. Fully doc-commented, never referenced by any producer or
consumer in `client/src` or `client/tests` outside its own declaration. Every sibling constant in
the same file has at least one real consumer. Possible this is a stub waiting on Bubble Pop's
reward-chain wiring (out of this pass's scope, in `Effects/`) rather than truly dead — check there
before deleting.
Replacement: nothing.
`net: -16 lines possible.`

**6. `yagni` `IAiDiagnosticsSink` has one implementation, used identically in production and every test.**
`Ai/AiDiagnostics.cs:1-31`. Unlike the repo's real seams (`IUiDispatch`/`ILogSink`/`IAudioBackend`,
which have distinct alternate test doubles), `CollectingAiDiagnosticsSink` is the only class
implementing the interface anywhere, including in all ~15 test files that touch it — nothing ever
varies what "diagnostics" means here.
Replacement: delete the interface, take the concrete type directly at the two constructor sites
(`AiAwarenessService.cs:449`, `AiOperationPipeline.cs:54`).
`net: -12 to -15 lines possible.`

**7. `stdlib` Manual 24-byte zero-fill loop instead of `Marshal.Copy`.**
`Video/MediaFoundationClipSource.cs:332-338`. `ReadDuration` zero-fills an unmanaged
`PROPVARIANT` buffer with 24 individual `Marshal.WriteByte` calls immediately before a COM call
overwrites it. `Marshal.Copy(new byte[24], 0, buffer, 24)` is the same effect in one line.
`net: -3 lines possible.`

**Total: roughly -628 to -663 lines possible against ~93,200 lines in `client/src` (~0.7%).**

## Scope checked and found lean (no findings)

Two independent passes covered all of `client/src/CcpClient.Desktop`: `Ai`, `Assets` (no `.cs`),
`Audio`, `Camera`, `Capabilities`, `Companion`, `Entitlement`, `Features/*`, `Glyph`, `Haptics`,
`Input`, `Lifecycle`, `Manifest`, `Motion`, `Navigation`, `Overlay`, `Persistence` (besides #1),
`Pointer`, `Scheduling`, `Session` (besides #5), `Storage`, `Tray`, `Video` (besides #7), `Views`
(incl. `Views/Pages`). The codebase's doc-comment culture pre-empts most of what a YAGNI pass
usually finds: single-implementation interfaces almost all carry a real `Recording*`/`Stub*`/
`Fake*` test double, per-module preset documents are deliberately un-consolidated (a corrupt file
should only reset its own module), and `[ComImport]` COM vtables were excluded per the pass's own
false-positive list. No unused private methods were found in the largest files checked
(`Win32OverlayPresence.cs`, `Win32PointerSurface.cs`, `Win32TrayPresence.cs`,
`Win32VideoPresence.cs`, `StudioPage.axaml.cs`).

## Noted, not a finding (per scope: correctness/doc issues are out of scope for this pass)

`Audio/UnsupportedAudioPresence.cs` has two stacked `<summary>` doc-comment blocks above
`IsSounding(string slot)` — a doc artifact, not a behavior issue.

## Out of scope for this pass, not audited

`client/tests` (~88,000 lines) and `spine-tasks/` were named in scope by the recovered brief but
not covered this run given the size of `client/src` alone; a future pass should pick those up.
