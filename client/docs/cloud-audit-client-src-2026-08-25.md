# Cloud audit — `client/src` YAGNI/efficiency pass, 2026-08-25

Run from an Anthropic cloud Linux sandbox per `client/docs/cloud-audit-brief.md` (note: that file is
absent from this branch's working tree as of this run — deleted in `1bdf998e4` alongside unrelated
doc cleanup; its full text is still readable via `git show 7e9d3b377:client/docs/cloud-audit-brief.md`
and this pass follows it from that recovered copy). `dotnet` is not installed on this sandbox at all,
confirming the brief's premise — no build, no analyzer, no test run backs any finding below. Every
line is a static-reading, grep-verified claim, not a compiled or tested one.

**No code changed in this pass.** Four parallel readers each covered a disjoint slice of
`client/src/CcpClient.Desktop` (~97,900 lines total) and verified every dead-code claim with a
repo-wide grep (`client/src`, `client/tests`, `.axaml` bindings, `client/docs`) before including it.
Findings below are candidates for a Windows-capable reviewer who can actually build and run the floor
gate — not applied changes.

## Ranked cut-list (biggest first)

| # | Tag | What to cut | Replacement | Path | net lines |
|---|---|---|---|---|---|
| 1 | `shrink:` | `WndClassExW`/`Rect`/`Point`/`PaintStruct`/`UserObjectFlags`/`Msg`/`BitmapInfo*` structs and ~19 `[DllImport]` signatures (`RegisterClassExW`, `CreateWindowExW`, `GetWindowRect`, `SetWindowPos`, …) redeclared byte-identical across 6 files | one shared `internal static class Win32Common` (new `Interop/` folder); each feature file keeps only its feature-specific P/Invokes (`Shell_NotifyIconW`, `UpdateLayeredWindow`, `EnumDisplayMonitors`, …) | `Video/Win32VideoInterop.cs`, `Overlay/Win32OverlayInterop.cs`, `Glyph/Win32GlyphInterop.cs`, `Pointer/Win32PointerInterop.cs`, `Tray/Win32TrayInterop.cs`, `Input/Win32InputInterop.cs` | **-350 to -400** |
| 2 | `shrink:` | `DataBlob` struct + `CryptProtectData`/`CryptUnprotectData`/`LocalFree` P/Invoke + marshaling boilerplate, duplicated between the two DPAPI call sites | shared `internal static class DpapiInterop` with a parameterized `Protect(bytes, entropy?)`/`Unprotect(bytes, entropy?)` pair both sites wrap | `Entitlement/HostDpapi.cs:42-115`, `Persistence/SecretStores.cs:331-423` | **-50 to -70** |
| 3 | `yagni:` | `IBubblePopSurface` interface — one production implementation, no test double anywhere in `client/tests` | delete interface; reference `BubblePopSurfacePresenter` concretely | `Effects/BubblePopSurfacePresenter.cs:13-60` | **-48** |
| 4 | `shrink:` | Identical `Probe(string? payloadRoot)` payload-presence algorithm (Directory.Exists → EnumerateFiles → RequiredFiles loop) hand-rolled 3x | one shared `internal static class PayloadRootProbe`, three thin typed wrappers keep their own enum/record | `Features/Arcademy/ArcademyServingRoots.cs:75-91`, `Features/Intake/IntakeServingRoots.cs:36-56`, `Features/Goon/GoonServingRoots.cs:46-66` | **-35 to -45** |
| 5 | `shrink:` | 15 near-identical `new PersistenceStore<T>(infra.OwnerFor("Name"), infra.Log, Path.Combine(dataDirectory, T.FileName), T.CurrentSchemaVersion)` call sites in one constructor | private `MakeStore<T>(ownerName, fileName, schemaVersion)` helper; each site becomes one line | `Session/SessionParticipant.cs:101-211` | **-30 to -35** |
| 6 | `yagni:` | `{Feature}GateDecision` 3-case record (`Proceed`/`RefusedNotEntitled`/`RefusedUnverified`) + `Decide()` guard logic, same shape in 3 places (2 in scope, 1 in `Haptics/`) — *lower confidence, coordinate with the Haptics owner before cutting* | shared `TierGateDecision` + `Decide(EntitlementOutcome, EntitlementTier)`; per-feature message constants stay | `Features/Arcademy/ArcademyGate.cs:13-27,90-110`, `Features/Dtrh/DtrhGate.cs:15-33,132-153`, `Haptics/HapticGate.cs` (third instance, outside this pass's read) | **-20 to -30** |
| 7 | `shrink:` | `[JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }` (persistence contract §6) repeated verbatim across 13 preset-document classes | shared `abstract class PresetDocumentBase` each document inherits | `Session/*PresetDocument.cs` (13 files), `Session/ScriptedSession.cs` | **-20 to -26** |
| 8 | `yagni:` | `ILockCardPhrasePool` interface — one production implementation, no test double | delete interface; reference `LockCardPhrasePool` concretely | `Effects/LockCardPhrasePool.cs:5-19` | **-15** |
| 9 | `stdlib:` | `AiPrivacyFilters.SanitizeTitleForWire` hand-rolled whitespace-collapse loop (`StringBuilder` + `char.IsWhiteSpace`) — *lowest confidence: file uses `Regex` elsewhere, but no note rules out a deliberate single-pass-perf choice here* | `Regex.Replace(stripped, @"\s+", " ")` | `Ai/AiPrivacyFilters.cs:168-189` | **-13** |
| 10 | `delete:` | `HapticReasonCodes.HapticNotEntitled` constant — only its own declaration repo-wide; actual refusal path is the typed `HapticGateDecision.RefusedNotEntitled` record | nothing | `Haptics/HapticReasonCodes.cs:73-83` | **-11** |
| 11 | `delete:` | `BubblePopSurfacePresenter.SpawnOnce()` — public wrapper, zero callers anywhere (its sibling `StepOnce()` is genuinely wired and stays) | nothing | `Effects/BubblePopSurfacePresenter.cs:414-421` | **-8** |
| 12 | `delete:` | `StudioPickerNotices.SpiralLibraryEmpty(string)` — dead; the live empty-spiral-folder message is composed independently inline in `StudioPage.axaml.cs` | nothing | `Views/Pages/StudioPickerNotices.cs:24-29` | **-5** |
| 13 | `delete:` | `HapticGate.DeniedTitle` constant — WPF-modal archaeology; the port's actual notice path carries no title field | nothing | `Haptics/HapticGate.cs:93-96` | **-4** |
| 14 | `delete:` | `BubbleCountGame.PicturesPainted` property — incremented every frame, read nowhere | nothing | `Effects/BubbleCountGame.cs:261-262,315` | **-3** |

**Total: roughly -610 to -670 lines** against ~97,900 lines in scope (well under 1%). Finding 1
(Win32 interop) accounts for the large majority of the total; everything else is small and mechanical.

## Reading the result

This codebase is unusually YAGNI-disciplined already. All four readers independently reported the
same pattern: nearly every interface, placeholder, and per-module facade carries an explicit in-code
citation defending its existence (a WPF source-line parity requirement, a contract section, or an
explicit "considered and rejected" note), which is exactly the kind of justification a YAGNI pass is
supposed to smoke out the absence of. That discipline is consistent with what the (recovered) brief
said was already measured before this run: 0 dead public types out of 507, `CA1823` unused-private-field
at 0 under a 2063-warning analyzer run. This pass adds one more data point in the same direction —
this is not a codebase carrying speculative weight.

Every interface census in every reader's scope came back clean except the two flagged above
(#3, #8): every other interface checked had either ≥1 production implementation with test-double
coverage in `client/tests`, or was a documented per-OS/contract seam (`ISecretStore`, `*Clock`
interfaces, `Unsupported*` refusal classes) explicitly excluded by this pass's own ground rules.

## Out of scope, noted only (not fixed here)

No correctness, security, or performance issue surfaced during this pass that rose to the level of
worth naming; nothing was found in passing, and none of the four readers went looking (that is a
different pass).

## Verification note

**Nothing in this list has been applied, built, or tested.** This sandbox cannot run
`node client/tests/floor/check-warnings.mjs` / `check-floor.mjs` meaningfully (Windows-only and
headed-desktop tests are in the pinned 2622/152 floor count) and cannot build the `net8.0-windows`
shipping tree at all. Per `docs/constitution.md`, a failed-or-unrunnable check is never accepted to
keep work moving — so this pass stops at a reviewed cut-list rather than landing any of it. A
Windows-capable reviewer applying any row here still owes the standard client gates
(`check-warnings.mjs --cold` after touching any of the moved P/Invoke declarations, then
`check-floor.mjs`) before it counts as done.
