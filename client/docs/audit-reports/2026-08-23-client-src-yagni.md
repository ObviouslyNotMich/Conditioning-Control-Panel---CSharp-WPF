# Cloud audit — client/src YAGNI and efficiency pass, 2026-08-23

Scheduled cloud audit routine, run from a Linux sandbox against `feat/crossplatform`
(`ae17514592a07ca7814ee9e2783ad27de2dbb822`). Nothing in this pass was landed — see
"Process note" below.

## Process note: the standing brief is missing from this branch

`client/docs/cloud-audit-brief.md` — the file this routine is told to read — does not exist on
`feat/crossplatform`. It was added in `7e9d3b377` and deleted two commits later in `1bdf998e4`
("Refactor tests and documentation for clarity and accuracy"), which also deleted
`client/tools/port-audit-prompt.md` and rewrote `docs/constitution.md`. This pass proceeded
using the brief's content recovered from git history (`git show 7e9d3b377^..7e9d3b377`), since it
matches the hard limit already embedded in this routine's own scheduled prompt word for word. If
the deletion was intentional, the routine's scheduled prompt should be updated to stop pointing at
a path that no longer exists; if it wasn't, the file is one `git checkout 7e9d3b377 --
client/docs/cloud-audit-brief.md` away from being restored.

## The one hard limit (from the recovered brief)

**Nothing was landed.** No push to `feat/crossplatform`, no merge, no test/pin/gate weakened. This
pass ran with no .NET SDK available at all (`dotnet` is not on PATH in this sandbox) and no
Windows/WSLg display, so `check-floor.mjs`, `check-warnings.mjs`, and every headed-verification
command were unrunnable and are not claimed. All findings below come from reading the code and
cross-checking call sites against `client/tests/`, not from an analyzer run.

## Scope

`client/src` (~74,500 lines of C#/XAML, 319 files, `Assets/assets.manifest.json` excluded as
generated). Four parallel read-only passes covered: `Features/`; `Effects/` + `Views/`;
`Session/`, `Haptics/`, `Ai/`, `Audio/`, `Camera/`, `Overlay/`; and the remaining fourteen smaller
directories (`Companion/`, `Entitlement/`, `Glyph/`, `Input/`, `Lifecycle/`, `Manifest/`,
`Motion/`, `Navigation/`, `Persistence/`, `Pointer/`, `Scheduling/`, `Tray/`, `Video/`,
`Capabilities/`). Correctness, security, and performance are out of scope for this pass; none were
noticed worth naming separately.

## Ranked cut-list (biggest cut first)

**1.** `yagni: the entire ISecretStore subsystem has zero production callers.`
`WindowsDpapiSecretStore`, `SecretToolSecretStore`, `UnsupportedSecretStore`, `DpapiNative`, and
`PlatformSecretStore.ForCurrentPlatform` (the only construction path for any of them) are referenced
only by `client/tests/CcpClient.Tests/SecretStoreTests.cs`. `Lifecycle/CompositionRoot.cs` — the
single place production objects get built — has zero references to `Secret`. Entitlement's actual
DPAPI use (`Entitlement/HostDpapi.cs`) is a separate, independent crypt32 binding, with its own doc
comment explaining it deliberately does *not* reuse `SecretStores.cs`'s binding (a TFM constraint).
So this isn't a redundant seam beside a used one — it's a fully-built, fully-tested, per-OS
(Windows DPAPI / Linux secret-tool) secret store, complete with a subprocess-driving CLI wrapper and
a capability probe, that nothing in the shipped product ever reaches. Verified independently by this
routine (not just the sub-audit that found it): `grep -rn "Secret" client/src/**/CompositionRoot.cs`
returns no hits.
Replacement: nothing — delete `Persistence/SecretStores.cs` in full and the `ISecretStore`
interface declaration in `Persistence/PersistenceStore.cs:538-545`.
`[client/src/CcpClient.Desktop/Persistence/SecretStores.cs:1-423, PersistenceStore.cs:538-545]`
`net: -430 lines`

**2.** `shrink: ~25 near-identical slider-change handlers in StudioPage.axaml.cs repeat the same
"if (_syncing) return;" guard before one line of work and a Refresh() call.` The file already has
the equivalent pattern for checkboxes (`OnRampSwitch`, line 1399) but sliders never got it. A
`Commit(Action apply)` helper (`if (_syncing) return; apply(); Refresh();`) collapses each handler
from 5 lines to 1.
`[client/src/CcpClient.Desktop/Views/Pages/StudioPage.axaml.cs:496-534, 1145-1938, 1399-1415]`
`net: -90 lines`

**3.** `delete: LevelUnlocks static class is read only by doc comments and one parity test, never
called.` `IsUnlocked()` unconditionally returns `true`; its `requiredLevel` parameter is, by the
class's own doc comment, "read by nothing, upstream or here." The four constants exist to be quoted
in doc comments across four `Effects/*.cs` files and asserted against WPF's doc-comment prose in one
test, never to gate anything at runtime.
Replacement: nothing (fold the historical numbers into a plain comment if worth keeping).
`[client/src/CcpClient.Desktop/Features/Progression/XpCurve.cs:164-221]`
`net: -58 lines`

**4.** `shrink: SessionParticipant.StartAsync/StopAsync hand-list 14 sequential PersistenceStore<T>
calls instead of looping over the shared IBackgroundParticipant interface they all implement.`
`[client/src/CcpClient.Desktop/Session/SessionParticipant.cs:726-768 (StartAsync), 787-832
(StopAsync's Task.WhenAll)]`
Replacement: build `private readonly IBackgroundParticipant[] _stores = [...]` once, then
`foreach (var store in _stores) await store.StartAsync(...)` and
`Task.WhenAll(_stores.Select(s => s.StopAsync()))`. Typed fields stay for the per-module public
accessors; `LogIfDegraded`/`FlushAsync` can't join the loop without a small interface addition, left
as-is.
`net: -20 lines`

**5.** `shrink: three pointer-released handlers in StudioPage.axaml.cs repeat the same right-click
guard / Handled / LoadDialsFromPreset / Refresh shape around one differing middle line.`
`OnRowPointerReleased`, `OnSchedulerRowPointerReleased`, `OnHapticsRowPointerReleased` differ only
in whether they call `_session.Engine.QuickToggle(effectId)`, `_scheduler.SetEnabled(...)`, or
`_haptics.RequestEnable(...)`.
`[client/src/CcpClient.Desktop/Views/Pages/StudioPage.axaml.cs:964-975, 984-995, 1008-1019]`
Replacement: one `OnRowPointerReleased(PointerReleasedEventArgs e, Action toggle)` taking the
differing line as a delegate.
`net: -12 lines`

**6.** `delete: HapticReasonCodes.HapticNotEntitled is an unused reason-code constant.` An 11-line
doc comment plus constant for an entitlement-refusal code that nothing produces or matches; the
actual gate (`HapticGate.Decide`) returns typed `HapticGateDecision.RefusedNotEntitled` /
`RefusedUnverified` records instead.
`[client/src/CcpClient.Desktop/Haptics/HapticReasonCodes.cs:73-83]`
Replacement: nothing.
`net: -11 lines`

**7.** `shrink: identical "remember last capability state" latch duplicated across three Haptics
sinks.` `ButtplugHapticSink`, `LovenseHapticSink`, and `CompositeHapticSink` each independently carry
a `_lastOutcome` field, a locked getter, and a `Remember(CapabilityState)` method with the same
6-line body.
`[client/src/CcpClient.Desktop/Haptics/ButtplugHapticSink.cs:40,55,226-234,
LovenseHapticSink.cs:61,93,397-403, CompositeHapticSink.cs:74,117,512-518]`
Replacement: one small shared `LatestCapability` value type (`Read()` / `Remember(state)` under a
lock) used as a single field in each sink in place of the two fields plus method each currently
carries. (`Audio/WasapiAudioPresence.cs` has a superficially similar `Remember` but with extra reset
logic that doesn't fit this shape — left out.)
`net: -10 lines`

**8.** `shrink: FlashGeometry.Place hand-rolls a "find any match" loop with a flag variable instead
of IReadOnlyList<T>.Any(...).`
`[client/src/CcpClient.Desktop/Effects/FlashGeometry.cs:126-141]`
Replacement: `if (!occupied.Any(o => Overlaps(candidate, o))) { return candidate; }`
`net: -8 lines`

**9.** `yagni: DtrhHostWindow.PostLoomList() is a private one-line wrapper with one call site.`
`[client/src/CcpClient.Desktop/Features/Dtrh/DtrhHostWindow.axaml.cs:1215-1222]`
Replacement: call `_loomDispatch?.PostList();` directly at the call site (line 1283); delete the
wrapper and its doc comment.
`net: -8 lines`

**10.** `shrink: byte-for-byte identical 9-point fraction table duplicated between VideoFrame and
VideoLetterbox.` Same nine `(x, y)` pairs, each carrying its own paragraph of rationale.
`[client/src/CcpClient.Desktop/Video/VideoFrame.cs:129-134, Video/VideoLetterbox.cs:170-175]`
Replacement: one shared internal constant (e.g. on `VideoLetterbox`), used by both.
`net: -6 lines`

**11.** `yagni: AvatarBitmapCache.MarshalCopyRow(...) is a private one-line wrapper with one call
site.`
`[client/src/CcpClient.Desktop/Features/AvatarTube/AvatarBitmapCache.cs:50-53]`
Replacement: inline the single `Marshal.Copy(...)` call at the loop site (line 39); delete the
wrapper.
`net: -4 lines`

**12.** `delete: HapticGate.DeniedTitle is an unused modal-dialog title string.` A comment claims it's
kept because "the port's notice is not a modal and the words have to carry what the window chrome
used to," but nothing reads it — the string actually surfaced (`TierRefusalMessage`) is built from
`DeniedMessage` + `UpgradeRoute` only.
`[client/src/CcpClient.Desktop/Haptics/HapticGate.cs:93-96]`
Replacement: nothing.
`net: -4 lines`

**Total: ~-661 lines out of ~74,500 (well under 1%).**

## What was checked and found clean

This codebase is unusually resistant to this kind of audit: nearly every file that looks like a
YAGNI candidate on the surface carries an inline doc comment explaining a real, cited reason it
exists (a WPF-parity divergence, a per-OS seam, a test double, a harness entry point). Specifically
verified clean, not just assumed:

- **Unused private methods** (the brief's named best lead, since nothing like `IDE0051` has ever
  run here): a full occurrence scan across every `.cs` file in scope, including method-group
  references that a naive grep misses, found **zero** genuinely unused private/internal methods.
- 58/66 single-implementation interfaces checked against `client/tests/` all have a real test
  double; none were flagged as YAGNI on that basis alone.
- Five duplicated-looking "presence/surface" subsystems (`Glyph`, `Input`, `Pointer`, `Tray`,
  `Video`) each have distinct production callers — deliberate per-capability isolation, not one
  abstraction copied five times.
- GDI+ interop, bounce physics, and the four pool classes (audio cue / flash image / video clip /
  lock-card phrase) look repetitive but each carries a documented, individually-cited behavioral
  divergence from the WPF original; merging them risks losing that divergence silently.
- `Camera/`, `Ai/AiCommandEnvelope.cs`'s strict hand-rolled JSON validators, and
  `Ai/AiPrivacyFilters.cs` were read and left alone as consent/capability-boundary or
  untrusted-input-boundary code, per the constitution's instruction not to propose broadening those
  boundaries — not because they were exempted from scrutiny.

## Not done, and why

No code changes were made to `client/src` or anywhere else. This sandbox cannot build the
solution (no `dotnet` on PATH) or run `check-warnings.mjs` / `check-floor.mjs`, so a change proposed
here would be unverified work — which is exactly what the constitution and the recovered brief both
exist to prevent. Every line above is a proposal for a human, or a future session with working
gates, to act on and verify — not a change already made.
