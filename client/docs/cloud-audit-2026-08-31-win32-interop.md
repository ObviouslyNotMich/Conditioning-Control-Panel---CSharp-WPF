# Cloud audit — client/src Win32 interop duplication

**Date:** 2026-08-31 · **Run:** scheduled cloud YAGNI/efficiency pass, Linux sandbox · **Shape:** report only —
zero product code changed, per the standing hard limit.

## Housekeeping note

`client/docs/cloud-audit-brief.md`, which this routine is supposed to read, does not exist on
`feat/crossplatform` — it was deleted in `1bdf998e4` ("Refactor tests and documentation for clarity
and accuracy", 2026-08-22), nine days before this run, by the same author who owns this repo. Its
content was recovered from git history (`git show 7e9d3b377^^{/cloud-audit-brief}`... concretely
`git show 1bdf998e4^:client/docs/cloud-audit-brief.md`) and followed as the operating spec, since its
hard limit and scope are still consistent with the current `CLAUDE.md` and `docs/constitution.md`.
Flagging the deletion here rather than silently reconstructing it, in case it was intentional (folded
into the two files above) and this doc is redundant going forward — worth a human call, not an
audit-routine one.

Also stronger than the brief anticipated: this sandbox has **no `dotnet` SDK installed at all** —
not even `client/`'s own `net10.0` Avalonia tree can be attempted-built here, let alone the
`net8.0-windows` shipping product. Every finding below is verified by manual reading and `grep` only;
none of it has seen a compiler.

## Scope covered this pass

`client/src` only (396 `.cs` files, 22 `.axaml`, ~108.5k lines currently — grown since the brief's
~74,500/319-file baseline). `client/tests` and `spine-tasks/` were not covered this run.

## Ranked cut-list

**1.** `shrink:` Seven Win32 P/Invoke modules (`Overlay/Win32OverlayInterop.cs`,
`Tray/Win32TrayInterop.cs`, `Pointer/Win32PointerInterop.cs`, `Glyph/Win32GlyphInterop.cs`,
`Video/Win32VideoInterop.cs`, `Input/Win32InputInterop.cs`, `Input/Win32PanicInterop.cs`) each
redeclare their own copy of the same `user32.dll`/`gdi32.dll`/`kernel32.dll` signatures instead of
sharing one native module. Verified by counting `[DllImport(...)]` per file:

| File | user32 | gdi32 | kernel32 | other |
|---|---|---|---|---|
| `Overlay/Win32OverlayInterop.cs` | 27 | 6 | 1 | — |
| `Tray/Win32TrayInterop.cs` | 21 | — | 1 | shell32 ×2 |
| `Pointer/Win32PointerInterop.cs` | 29 | 6 | 2 | — |
| `Glyph/Win32GlyphInterop.cs` | 23 | 5 | 1 | — |
| `Video/Win32VideoInterop.cs` | 21 | 5 | 1 | dwmapi ×1 |
| `Input/Win32InputInterop.cs` | 36 | 5 | 2 | — |
| `Input/Win32PanicInterop.cs` | 12 | — | 1 | — |

The names overlap heavily (`RegisterClassExW`, `CreateWindowExW`, `DefWindowProcW`, `DestroyWindow`,
`IsWindow`, `IsWindowVisible`, `GetModuleHandleW`, `ShowWindow`, `SetWindowPos`,
`GetWindowLongPtrW`, `GetWindowRect`, `WindowFromPoint`, `GetTopWindow`, `GetWindow`,
`GetSystemMetrics`, `GetDC`, `ReleaseDC`, `DeleteObject`, and more), each declared independently in
most or all of the seven files, along with shared structs (`WndClassExW`, `Rect`, `Point`,
`PaintStruct`, `UserObjectFlags`). On top of the raw signatures, one *behavioral* helper —
`ReadZOrder`/`ZOrderPosition` — is duplicated near-verbatim (only the surrounding class differs) in:
- `Overlay/Win32OverlayPresence.cs:941` (+ record at `:975`)
- `Pointer/Win32PointerSurface.cs:562` (+ record at `:596`)
- `Glyph/Win32GlyphSurface.cs:865` (+ record at `:899`)
- `Input/Win32InputPresence.cs:1038` (+ record at `:1029`)
- `Video/Win32VideoPresence.cs:687` carries a narrower two-field `ZOrderPosition` without a
  `ReadZOrder` method — a fifth partial echo of the same shape.

Replacement: one shared `internal static class Win32Native` (e.g. under a new
`CcpClient.Desktop/Interop/` folder) holding the common signatures/structs, plus a single
`Win32ZOrder.Read(nint)` helper; each surface-specific interop file keeps only what's genuinely
unique to it (`NotifyIconDataW`, `BitmapInfo`, `Msg`, `GuiThreadInfo`, etc.).
`net: -350 lines possible` (conservative: ~92 redundant single-line declarations + duplicated structs
+ 4 redundant `ReadZOrder` copies, minus the one retained copy).

**Not done here.** This is a real, multi-file P/Invoke consolidation. Getting a struct layout,
marshaling attribute, or calling convention wrong in this kind of change is exactly the class of
mistake a compiler catches and eyeballing doesn't — and this sandbox has no compiler. Landing it
unverified would trade a ~350-line win for a plausible native-interop regression nobody could see
from here. Left for a session with Windows build + headed capture.

**2.** `yagni:` (low confidence, reported per the brief's criteria, not asserted as removable) — six
interfaces have exactly one production implementation and zero test doubles anywhere in
`client/tests` (this repo uses no mocking framework — `grep -rl "Moq\|NSubstitute" client/tests` → 0
hits — so a plain-text `class X : IFoo` search is a complete census, not an approximation):

| Interface | Sole impl | File |
|---|---|---|
| `IAiDiagnosticsSink` | `CollectingAiDiagnosticsSink` | `Ai/AiDiagnostics.cs:11` |
| `IAiEndpointAdmissionPolicy` | `LoopbackOnlyAdmissionPolicy` | `Ai/AiEndpointAdmission.cs:10` |
| `IBubblePopSurface` | — | `Effects/BubblePopSurfacePresenter.cs:14` |
| `IDocumentMigration` | — | `Persistence/DemoSettings.cs:11` |
| `IDuckHandle` | `DuckHandle` | `Audio/SoundArbitration.cs:60` |
| `ILockCardPhrasePool` | — | `Effects/LockCardPhrasePool.cs:7` |

Every one of these carries an explicit doc-comment cross-reference to a contract or spike (e.g.
`IAiEndpointAdmissionPolicy` names a second, allow-list policy as "owner-pending"), which reads as a
deliberately-scoped extension point tracked by a governing doc rather than accidental speculative
flexibility. A 7th candidate, `IButtplugSession`, was checked and excluded: its test file explicitly
documents that it deliberately has no fake, since "a fact that only ever drove a fake session would
prove the policy and nothing about whether this port calls the library correctly." Listed for
completeness; a maintainer who owns the referenced contracts should judge, not this pass.
`net: -15 lines possible` (small — collapsing each to its concrete class removes only the interface
block).

## Checked hard and found clean (do not re-run these)

- **Unused private methods** (the brief's first "genuinely unmeasured" lead): all 1023 `private`
  method declarations in `client/src` scanned for zero other call sites of their name within the
  declaring file. Result: **zero** hits. Tool verified against `Views/Pages/StudioPage.axaml.cs` (90
  private methods) by spot-checking several matches.
- **Over-exposed public surface** (the brief's second lead): no `InternalsVisibleTo` exists anywhere
  in the solution, and both test projects reach `CcpClient.Desktop` only via ordinary
  `ProjectReference` — so essentially every type the test suite touches is required to be `public`
  already. No high-confidence `internal`-candidate surface found beyond the interfaces in finding 2.
- Dead-code markers (`#if false`, `[Obsolete]`, `// DEAD`, `// UNUSED`, `// TODO: remove`): 0 hits.
- Hand-rolled BCL/native reimplementations: no busy-wait sleeps, no custom JSON parser, no manual
  debounce/throttle/retry outside correct `System.Threading.Timer` usage, no manual DI container.

## Out of scope, noted only

No correctness, security, or performance issues were noticed in passing during this pass.

## Bottom line

Two real findings, one genuinely actionable (`shrink:`, ~350 lines, needs a Windows build to land
safely) and one low-confidence/informational (`yagni:`, ~15 lines, needs an owner call on the
referenced contracts). Nothing was changed in `client/src` this run.
