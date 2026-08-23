# client/src YAGNI and efficiency audit — 2026-08-23

Scheduled cloud audit pass, run from a Linux sandbox against `feat/crossplatform` per the standing
brief (see note below). Scope: `client/src` only, per the brief's priority order. No gates run, no
code changed — report only.

## Note: the brief this routine follows is missing from this branch

`client/docs/cloud-audit-brief.md` — the standing brief this scheduled routine is supposed to read
and follow — was deleted from `feat/crossplatform` in commit `1bdf998e4` ("Refactor tests and
documentation for clarity and accuracy"), a broad doc-cleanup commit that touched dozens of files.
`docs/constitution.md` does not carry an equivalent of the brief's specific content (the hard "cannot
run this repo's gates from Linux" limit, the PR title format, the five-tag cut scheme, the
already-measured baseline). This pass recovered the brief's content from git history (commit
`7e9d3b377`) and followed it as written, but every future firing of this routine will hit the same
gap unless the file is restored or the routine's prompt is updated to point elsewhere. Flagging for
an owner decision; not restoring it here since that's outside this pass's scope (client/src only).

## Hard limit acknowledged

This pass ran with no `dotnet` SDK available and no repo gates executed. `check-floor.mjs` and
`check-warnings.mjs` were not run — their counts don't apply on Linux and a result here would prove
nothing either way. Nothing in this PR touches test pins, gates, or `.editorconfig` severities.
**Nothing is landed**: this branch is not pushed to `feat/crossplatform`, and this PR is not merged.

## Method

Six read-only passes covered all 320 `.cs`/`.axaml` files (~75,300 lines) under `client/src`, split
by subsystem. Every single-implementation-interface candidate was checked against `client/tests` for
a fake/stub/double before being called `yagni` — a real test double makes it a testing seam, not
speculative abstraction. Every `delete` claim was grepped repo-wide (src, tests, XAML bindings) for
any reference before being called dead. Correctness, security, and performance issues are out of
scope for this pass and are not included below even where noticed in passing.

## Ranked cut-list (biggest cut first)

1. `delete:` Unwired AI command-envelope/executor system — `AiCommandEnvelope.cs` (580 lines, an
   11-command-kind strict-schema validator) and `AiCommandExecutor.cs` (147 lines, a dispatch
   executor) are exercised only by tests. `CompanionParticipant.cs:87` constructs
   `AiCommandExecutor()` with an empty handler dictionary; the `.Executor` field it's assigned to has
   zero readers anywhere in `client/src`. `IAiEffectHandler`, the dispatch-target interface, has zero
   production implementations — only a test `Canary`. Confirmed by grep: `.Execute(` on this type
   appears only in `AiCommandExecutorTests.cs`. Replacement: nothing — rebuild against real
   requirements when an effect-handler backend actually exists.
   `[client/src/CcpClient.Desktop/Ai/AiCommandEnvelope.cs, AiCommandExecutor.cs]`
   `net: -727 lines possible.`

2. `delete:` `MainWindowViewModel` has no production caller. Neither `MainWindow.axaml` nor
   `MainWindow.axaml.cs` construct it, set it as a `DataContext`, or bind to it. Confirmed by grep:
   every `new MainWindowViewModel(...)` site is in `client/tests/CcpClient.Tests/` (`StatusTickerSliceTests.cs`,
   `QuickToggleDispatchTests.cs`), driving it directly as the unit under test, not through a fake. Its
   own doc comment ties it to a retired "Demo: Status Ticker" card; unlike the sibling demonstrator
   infrastructure `MainWindow.axaml.cs` keeps on purpose, no comment justifies keeping this one wired.
   Replacement: nothing (would need its two dedicated test files removed alongside it).
   `[client/src/CcpClient.Desktop/Views/MainWindowViewModel.cs]`
   `net: -125 lines possible.`

3. `shrink:` `StudioPage.axaml.cs` repeats the same slider/toggle-handler shape roughly 26 times:
   `if (_syncing) return; <module>.Set(...); _ = <preset>.Save(); Refresh();`. One generic
   `WireSlider(Slider, Action<int> set, Func<Task> save, Action? postWrite)` helper replaces all 26
   bodies with one line each.
   `[client/src/CcpClient.Desktop/Views/Pages/StudioPage.axaml.cs:457-1516]`
   `net: -180 to -220 lines possible.`

4. `shrink:` The same ~19-21 Win32 P/Invoke declarations (`RegisterClassExW`, `CreateWindowExW`,
   `GetWindowRect`, `WindowFromPoint`, etc.) plus matching `WndClassExW`/`Rect`/`Point` struct layouts
   are copy-pasted across four interop classes (`Win32OverlayInterop.cs`, `Win32InputInterop.cs`,
   `Win32PointerInterop.cs`, `Win32TrayInterop.cs`). One shared internal interop class holding the
   common declarations, with each presence-specific class keeping only its unique calls, removes three
   of the four copies.
   `[client/src/CcpClient.Desktop/{Overlay,Input,Pointer,Tray}/Win32*Interop.cs]`
   `net: -120 to -150 lines possible.`

5. `shrink:` `ReadZOrder`/`ZOrderPosition` and `Describe(nint)` are byte-for-byte identical across
   `Win32OverlayPresence.cs`, `Win32InputPresence.cs`, and `Win32PointerSurface.cs` (only the interop
   class name differs). One shared helper, called from all three, replaces two of the three copies.
   `[client/src/CcpClient.Desktop/{Overlay,Input,Pointer}/Win32*Presence.cs / Win32PointerSurface.cs]`
   `net: -90 to -120 lines possible.`

6. `shrink:` `App.axaml.cs` and `Program.cs` thread the same ~24 harness/demo `bool`/`string?`/`int`
   flags through four parallel parameter lists by hand (fields, constructor, `BuildAvaloniaApp`
   signature, `Configure<App>` lambda). A single `LaunchOptions` record built once in `Main` and
   passed through all three call sites collapses three of the four lists to one type reference each.
   `[client/src/CcpClient.Desktop/App.axaml.cs:13-76, Program.cs:334-361]`
   `net: -60 to -90 lines possible.`

7. `yagni:` `IBubblePopSurface` has exactly one implementation (`BubblePopSurfacePresenter`) and no
   test double — `BubblePopModuleTests.cs` constructs the concrete presenter directly. Drop the
   interface, use the concrete type in `SessionParticipant` and `BubblePopEffect`.
   `[client/src/CcpClient.Desktop/Effects/BubblePopSurfacePresenter.cs:14-90]`
   `net: -75 lines possible.`

8. `shrink:` Identical private `JsonElement` field-extraction helpers (`GetString`/`GetBool`/`GetInt`/
   `GetLong`/`GetDouble`) are copy-pasted across five protocol files instead of one shared
   `JsonElement` extension class.
   `[client/src/CcpClient.Desktop/Features/{Dtrh/DtrhProtocol.cs, Dtrh/DtrhMeta.cs, Dtrh/DtrhAssetStats.cs, Intake/IntakeProtocol.cs, Goon/GoonProtocol.cs}]`
   `net: -55 to -60 lines possible.`

9. `shrink:` `SessionParticipant.cs` enumerates the same 13 preset stores by name in four separate
   places (`StartAsync`, the `StopAsync`/`FlushAsync` `Task.WhenAll` calls, and 13 `LogIfDegraded`
   calls). The class's own comment documents this duplication already caused a real bug (one store
   omitted from one of the four lists, silently reverting to defaults). One `AllPresetStores` list
   built once in the constructor, iterated in a loop, both shrinks this and removes the recurrence
   risk the class's own comment describes.
   `[client/src/CcpClient.Desktop/Session/SessionParticipant.cs:695-824]`
   `net: -35 to -45 lines possible.`

10. `shrink:` Five near-duplicate `CapabilityState → string` switch methods in `StudioPage.axaml.cs`
    (`DescribeSurface`, `DescribeSubliminalSurface`, `DescribePinkFilterSurface`,
    `DescribeBouncingTextSurface`, `DescribeSpiralSurface`) differ only in subject noun and verb
    tense. One parameterized `DescribeDrawnSurface(state, subject, verbNow, verbLast)` replaces all
    five.
    `[client/src/CcpClient.Desktop/Views/Pages/StudioPage.axaml.cs:1594-1828]`
    `net: -40 lines possible.`

11. `shrink:` The payload-root presence probe (directory exists → file count → required-file check →
    typed Present/Missing/Incomplete) is re-implemented near-identically three times, once per
    feature.
    `[client/src/CcpClient.Desktop/Features/{Dtrh/DtrhParticipant.cs:29-40, Goon/GoonServingRoots.cs:46-65, Intake/IntakeServingRoots.cs:36-55}]`
    `net: -25 to -30 lines possible.`

12. `shrink:` `PlayPage.axaml.cs` and `IntakePage.axaml.cs` each carry a structurally identical
    `RenderFault`/`ClearFault`/`Show` fault-band trio, differing only in which named controls they
    touch.
    `[client/src/CcpClient.Desktop/Views/Pages/{PlayPage.axaml.cs:100-123, IntakePage.axaml.cs:90-113}]`
    `net: -15 to -20 lines possible.`

13. `delete:` `EffectReasonCodes.PointerRewardChainAbsent` is declared and documented but never
    constructed or returned anywhere (its three sibling "always-present" codes each have 2-4 live
    references; this one has zero, confirmed by repo-wide grep, including a read of
    `BubblePopEffect.Ready()`, the method its own comment says should emit it).
    `[client/src/CcpClient.Desktop/Session/EffectReasonCodes.cs:289-303]`
    `net: -15 lines possible.`

14. `shrink:` `AiPrivacyFilters.SanitizeTitleForWire` hand-rolls whitespace-run collapsing with an
    18-line `StringBuilder` loop; `Regex.Replace(stripped, @"\s+", " ")` does the same thing in one
    call, matching the pattern the same subsystem already uses elsewhere (`AiTextHygiene.cs:115`).
    `[client/src/CcpClient.Desktop/Ai/AiPrivacyFilters.cs:168-185]`
    `net: -15 lines possible.`

15. `yagni:` `IAiDiagnosticsSink` has exactly one implementation (`CollectingAiDiagnosticsSink`) and no
    test double — production and every test construct the same concrete type. Drop the interface.
    `[client/src/CcpClient.Desktop/Ai/AiDiagnostics.cs:11-31]`
    `net: -15 to -20 lines possible.`

16. `yagni:` `ILockCardPhrasePool` has exactly one implementation and no test double —
    `LockCardModuleTests.cs` constructs the concrete `LockCardPhrasePool` directly.
    `[client/src/CcpClient.Desktop/Effects/LockCardPhrasePool.cs:7-18]`
    `net: -12 lines possible.`

17. `yagni:` `IAiEndpointAdmissionPolicy` has exactly one implementation
    (`LoopbackOnlyAdmissionPolicy`) and no test double — every test uses the same singleton instance.
    `[client/src/CcpClient.Desktop/Ai/AiEndpointAdmission.cs:10-28]`
    `net: -10 to -15 lines possible.`

18. `shrink:` `HarnessEntryPoints.HarnessFlagsIn` manually dedupes with a `List<string>.Contains` loop
    instead of `args.Where(...).Distinct(StringComparer.Ordinal)`.
    `[client/src/CcpClient.Desktop/Lifecycle/HarnessEntryPoints.cs:106-120]`
    `net: -8 lines possible.`

19. `stdlib:` Per-session bridge-token generation hand-rolls Base64URL encoding
    (`Convert.ToBase64String` + two `.Replace` calls + `.TrimEnd('=')`) identically in three
    constructors; `System.Buffers.Text.Base64Url.EncodeToString` (available on `net10.0`, the
    project's target) does this in one call.
    `[client/src/CcpClient.Desktop/Features/{Dtrh/DtrhParticipant.cs:59-60, Goon/GoonParticipant.cs:88-89, Intake/IntakeParticipant.cs:44-45}]`
    `net: -6 lines possible.`

20. `yagni:` `IDuckHandle` has exactly one implementation, a private nested class already scoped to
    the same file, and no test double.
    `[client/src/CcpClient.Desktop/Audio/SoundArbitration.cs:57-64]`
    `net: -6 lines possible.`

21. `delete:` `SyntheticAvatarPacks.FileSha256` has no caller anywhere in `client/src` or
    `client/tests`.
    `[client/src/CcpClient.Desktop/Features/AvatarTube/SyntheticAvatarPacks.cs:251-253]`
    `net: -3 lines possible.`

22. `stdlib:` `VideoLetterbox` nests `Math.Max(1, Math.Min(...))` where `Math.Clamp` reads more
    directly. Cosmetic only — same line count.
    `[client/src/CcpClient.Desktop/Video/VideoLetterbox.cs:69-70]`
    `net: 0 lines, readability only.`

**Total estimated cut: ~1,700 lines (~2.3% of the ~75,300 lines in scope), across 22 items.**

## What was checked and explicitly ruled out

Each pass verified single-implementation-interface candidates against `client/tests` before flagging;
the following all have real fakes/stubs/doubles and were excluded as testing seams, not yagni:
`IIntensityDial`, `ISubliminalFrameSource`, `IBouncingTextSurface`, `IGlyphTextSource`,
`ISubliminalPhrasePool`, `IGlyphSurface`, `IMountTable`, `ISessionEnvironment`, `IDocumentMigration`,
`ISecretStore` (genuine per-OS seam, 3 real implementations), `IUiDispatch`, the four `I*Presence`
capability interfaces, `IAiMemoryStore`, `IAiProvider`, `IBarkAudioResolver`, `IEntitlementTierSource`,
`IHostBlobDecryptor`, `IAvatarClock`, `IDtrhAudioPlayer`/`Backend`, `IDtrhVideoBackend`,
`IIntakeEntitlementSource`. `[ComImport]` COM interop declarations (`IMFSample`, `IMFSourceReader`,
`IMFAttributes`, `IMFMediaBuffer`, `ICoreWebView2*Native`) are zero-impl by construction and out of
scope by the brief's own note. No further dead public types or unused private fields turned up,
consistent with the previously-measured 0-dead-types / CA1823=0 baseline. Per-module `*PanelNotices.cs`
files and the demo-flag plumbing itself were read and are deliberate/tested, not bloat.

## Out of scope, not fixed here

- `client/src/CcpClient.Desktop/Features/{Dtrh,Goon,Intake}` "sibling host" window code-behind files
  (`DtrhHostWindow.axaml.cs` 1320 lines, `DtrhNativeEffects.cs` 770, `IntakeHostWindow.axaml.cs` 777,
  `LoopbackServer.cs` 548, `DtrhLoomWindow.axaml.cs` 553) were scanned for structure and cross-referenced
  but not read line-by-line; the duplication pattern in items 8 and 11 above suggests a deeper pass
  there could turn up more of the same.
- `client/tests` (~88,000 lines) and `spine-tasks/` were out of scope for this pass per the brief's
  priority order; the brief separately notes `spine-tasks/` carries ~139 MB of committed binary/log
  residue as of 2026-08-22 that a local session, not this routine, should own deleting.
