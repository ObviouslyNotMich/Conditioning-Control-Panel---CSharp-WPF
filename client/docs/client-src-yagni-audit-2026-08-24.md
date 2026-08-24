# client/src YAGNI + efficiency audit — 2026-08-24

**Shape:** scheduled cloud audit pass, zero product-code changes — findings only, per the standing
brief's hard limit (this run cannot execute the repository's gates from Linux, so nothing lands).
**Scope:** `client/src/CcpClient.Desktop` (~74,500 lines, 377 files), priority 1 of the standing
brief's three-tier scope. `client/tests` and `spine-tasks/` were out of scope for this pass.

## Note on the brief itself

`client/docs/cloud-audit-brief.md` — the file this routine is instructed to read — no longer exists
on `feat/crossplatform`. It was deleted in commit `1bdf998e4` ("Refactor tests and documentation for
clarity and accuracy", 2026-08-22), alongside several other now-removed docs
(`port-audit-prompt.md`, `architecture-proposal.md`, `avalonia-mcp-admission.md`,
`main-sync-2026-08-04.md`). No replacement with equivalent content exists in the current
`CLAUDE.md` or `docs/constitution.md`. This pass proceeded using the brief's content recovered from
git history (commit `7e9d3b377`, the commit that introduced it) since nothing in the deletion diff
suggested its guidance had changed, only that the file was removed. Worth an owner decision on
whether to restore the file or fold its content into `CLAUDE.md`/`docs/constitution.md` — otherwise
every future firing of this routine hits the same missing file.

## What was already measured (from the recovered brief, not re-run here)

- 507 public types in `client/src`: zero dead.
- `CA1823` unused-private-field: 0, under a full CA analyzer run.
- 58 of 66 interfaces have a second implementation or test double; the remaining 8 are load-bearing
  OS seams (`ISecretStore`, `*Clock` interfaces, etc).

This pass targeted the brief's named unmeasured axes instead: unused private methods, hand-rolled
reimplementations of BCL/Avalonia functionality, dead-flexibility parameters, newly-single-impl
abstractions, duplicated logic blocks, and verbose non-idiomatic code.

## Ranked cut-list

**1. `yagni:` Five Win32 native-window interop files each redeclare identical P/Invoke plumbing.**
`Overlay/Win32OverlayInterop.cs`, `Pointer/Win32PointerInterop.cs`, `Glyph/Win32GlyphInterop.cs`,
`Input/Win32InputInterop.cs`, `Tray/Win32TrayInterop.cs` (287/271/262/308/239 lines) each
independently declare byte-identical structs (`Rect`, `Point`, `WndClassExW`, `PaintStruct`) and
~20 shared `DllImport` signatures (`CreateWindowExW`, `RegisterClassExW`, `DefWindowProcW`,
`GetWindowRect`, `SetWindowPos`, `BeginPaint`/`EndPaint`, etc — verified via `comm -12` across all
five files). Each file is a legitimate separate seam (overlay surfaces, pointer field, glyph text,
input capture, tray icon) with different message-loop bodies, but the shared native-window
creation/GDI declarations don't need five copies. Replacement: a shared `Win32/NativeWindowInterop.cs`
holding the common structs and DllImports, consumed by all five.
`net: -250 to -300 lines possible.`

**2. `shrink:` Device-matching/re-enumeration logic duplicated between the two SoundFlow audio
backends.** `Audio/SoundFlowAudioBackend.cs:74-138` and `Features/Dtrh/SoundFlowDtrhAudio.cs:61-131`
implement near-line-for-line identical MiniAudio device init (`TryInit`'s re-enumerate-before-init,
match-by-name, never-a-stale-`DeviceInfo` logic) and teardown. The two backends themselves are a
**documented, deliberate** ownership split (`SoundFlowAudioBackend.cs:15-16` cites the DTRH-local
seam boundary in the packet record.md) — do not merge the backend classes. But the init/teardown
arithmetic inside each has no reason to be duplicated; a shared internal helper both backends compose
would preserve the boundary while cutting the duplicate logic. Replacement: extract the `TryInit`
device-matching loop and the `Dispose` teardown block into one shared internal helper.
`net: -80 to -100 lines possible.`

**3. `shrink:` Identical ephemeral-port bind-retry loop in two loopback servers.**
`Features/Chaos/ChaosTunnelLoopback.cs:56-87` (`Start()`) and `Features/Dtrh/LoopbackServer.cs:127-150`
(`BindWithRetry()`) both implement "random port in 49152–65535, fresh `HttpListener` per attempt
(a failed `Start()` disposes the instance), up to 60 tries" with identical range, retry count, and
exception-tracking. The servers themselves are a cited, deliberate divergence
(`ChaosTunnelLoopback.cs:8-11`, pre-approach consult ruling 1 — about routing tables, not bind
mechanics). Replacement: a shared `EphemeralPortBinder.BindWithRetry(Func<int, HttpListener> factory)`
extracted from just the bind loop.
`net: -25 lines possible.`

**4. `shrink:` Identical three-arm outcome-resolution method in two Effects presenters.**
`Effects/PinkFilterSurfacePresenter.cs:222-245` and `Effects/SpiralSurfacePresenter.cs:412-432`
both implement the same "what did the OS really say" logic over `_surfaces.LastPresent`/`LastPaint`
(present-confirmed → paint-refusal-after-a-good-present → present-refusal → unreachable-fallback),
differing only in the fallback message string. `SpiralSurfacePresenter.cs:406-410` already comments
that it's "the same three-arm rule `PinkFilterSurfacePresenter` uses" — the duplication is noticed,
not hidden, but never extracted. Replacement: a static helper on `OverlaySurfaceSet`, e.g.
`ResolveOutcome(bool placed, CapabilityReason noOutcomeReason)`.
`net: -20 lines possible.`

**5. `shrink:` Scattered single-statement `foreach { list.Add(...) }` loops.**
`Haptics/HapticEnvelope.cs:323`, `Haptics/CompositeHapticSink.cs:198-205`,
`Persistence/PersistenceStore.cs:518`, `Persistence/SecretStores.cs:243`,
`Features/Arcademy/ArcademyLocalAssets.cs:72`, `Features/Companion/CompanionTranscriptWindow.cs:89`,
`Manifest/AssetManifest.cs:278-281,428` — each collapses to `list.AddRange(source.Select(...))` or a
direct LINQ projection. Individually trivial; only worth doing as a batch or via an analyzer rule.
`net: -15 to -20 lines possible.`

**Not recommended — flagged and rejected:** `Session/*PresetDocument.cs` (12 files) repeat ~4 lines
of boilerplate each (`FileName`, `CurrentSchemaVersion`, `ExtensionData`). This is the documented
"one document per module" isolation contract (cited D71/D80 in `BubbleCountPresetDocument.cs:11-13`):
a shared base class would let one hand-broken value reset every module. Leave as-is.

`Total estimated cut across items 1-5: roughly 390-465 lines, all in seam-internal plumbing, zero
public-surface or behavioral change.`

## Axes checked and found clean

- **Unused private methods** — swept all ~954 `private`/`private static` method declarations in
  scope for zero in-file references. Zero hits. Combined with the prior pass's 0 unused private
  fields, this axis is genuinely clean.
- **Dead-flexibility parameters** — spot-checked the most plausible always-same-value candidates
  (`IOverlayPresence.SetClickThrough(bool)`, `BarkPipeline.Raise(..., bool guaranteed)`,
  `ScriptedSessionRun.Stop(bool completed)`); all have live call sites on both branches. Not a full
  sweep of all 161 public `bool`-parameter methods — under-covered rather than confirmed clean.
- **New single-implementation abstractions beyond the known 8/66** — none found; the only abstract
  classes in scope (`OwnedSessionEffect`, `PacedSessionEffect<T>`, `AudioCueEffect`) each have
  multiple concrete subclasses, and the `*Factory` classes resolve real-vs-`Unsupported` OS seams
  already ruled load-bearing by the prior pass.
- **Verbose/non-idiomatic C#** — zero hits for `x != null ? x : y` (not already `??`), zero double
  materialization (`.ToList().ToList()` etc), zero `if (cond) return true; else return false;`.
- **Views/Pages `*Notices.cs` (16 files)** and **`Effects/*Schedule.cs` (8 files)** — both looked
  like duplication targets on the surface; both turned out to be bespoke content or an
  already-centralized facade (`Effects/EffectSchedule.cs`) respectively. No action.
- **Camera, Scheduling, Persistence, Ai/AiTextHygiene, AiPrivacyFilters** — read in full or in large
  part; JSON parsing goes through `JsonNode`/`JsonObject` properly, text hygiene uses compiled
  `Regex`. No hand-rolled BCL reimplementations found.

## Findings out of scope for this pass (per brief, correctness/security/perf are not audited here)

None surfaced incidentally during this pass.
