# Cloud audit — client/src YAGNI/efficiency pass, 2026-08-24 (third independent pass)

Scheduled Linux cloud sandbox run, same routine and scope (`client/src`) as the two passes already
on this PR. Nine parallel read-only reviewers each covered a disjoint subsystem; every candidate
was cross-checked against `client/tests` for a test double before being called dead, per this
routine's own baseline. **No files under `client/src` were changed.** This repo's gates cannot run
meaningfully from a Linux sandbox, so a cut applied here would be unverified work — report only,
per the (now-deleted, already covered by both earlier passes on this PR) standing brief.

Found a third, largely non-overlapping set of candidates — expected for a non-exhaustive read-only
pass. One exception: finding 1 below independently reproduces pass 2's top finding
(`Persistence/SecretStores.cs`) via an unprompted, separately-run reviewer plus my own follow-up
grep against `CompositionRoot.cs`. Reported as reconfirmation rather than re-counted in this pass's
total, since it's already the top item in the PR's cut-list.

## Independently reconfirmed (not re-counted)

**`Persistence/SecretStores.cs` has zero production callers.** A dedicated reviewer for
`Ai/Companion/Camera/Lifecycle/Persistence/Scheduling` flagged the same file pass 2 already
reported, with the same reasoning (interface's own doc says "no implementation exists in this
slice," yet a full 3-platform implementation exists; `HostDpapi.cs` documents using its own
separate crypt32 binding specifically because it doesn't use this one). I re-verified independently
via grep: `PlatformSecretStore.ForCurrentPlatform`, the only entry point, has no call site in
`CompositionRoot.cs` or anywhere else in `client/src` outside `SecretStores.cs` itself. Three
independent passes now agree on this one — worth treating as high-confidence.

## Findings, ranked biggest cut first

**1. `shrink` `Win32InputInterop.cs` and `Win32PointerInterop.cs` duplicate ~30 identical P/Invoke
signatures and 6 identical structs.**
`RegisterClassExW`, `CreateWindowExW`, `DestroyWindow`, `IsWindow(Visible)`, `ShowWindow`,
`SetWindowPos`, `GetWindowLongPtrW`, `GetWindowRect`, `WindowFromPoint`, `GetTopWindow`,
`GetWindow`, `GetForegroundWindow`, `GetSystemMetrics`, `GetClassNameW`,
`PeekMessageW`/`TranslateMessage`/`DispatchMessageW`, `GetProcessWindowStation`,
`GetThreadDesktop`, `GetUserObjectInformationW`, `GetCurrentThreadId`, `GetModuleHandleW`,
`GetDC`/`ReleaseDC`, `BeginPaint`/`EndPaint`, `FillRect`, `CreateSolidBrush`, `DeleteObject`, plus
structs `WndClassExW`, `Rect`, `Point`, `Msg`, `UserObjectFlags`, `PaintStruct`. Neither file states
an isolation rationale (unlike `Glyph`, which explicitly argues against sharing with Overlay for a
call-hazard reason that doesn't apply here — both Input and Pointer are ordinary
activatable/non-activating windows).
Replacement: a shared `Win32WindowNative` holding the common declarations; each capability keeps
only its unique/risky calls (`AttachThreadInput`/`SetForegroundWindow` for Input;
`WM_MOUSEACTIVATE`/`Ellipse`/`GetStockObject` for Pointer).
`client/src/CcpClient.Desktop/Input/Win32InputInterop.cs:85-307`,
`client/src/CcpClient.Desktop/Pointer/Win32PointerInterop.cs:90-266`
`net: -140 lines possible.`

**2. `delete` Dtrh Loom studio's single-developer forensics harness.**
The `scroll-probe`/`shot-rack`/`shot-fetch`/`report`/`scroll-rack`/`scroll-end`/`zoom:` scripts and
`PersistRackShot` name a specific, one-off investigation in their own comments ("this laptop",
"run-11 mystery") rather than a reusable capability — unlike the neighboring
`--dtrh-fx-drive`/`--dtrh-kill-renderers` harness surface, which drives real product paths through
the real dispatch path and was not flagged.
`client/src/CcpClient.Desktop/Features/Dtrh/DtrhLoomWindow.axaml.cs:405-533`
`net: -120 lines possible.`

**3. `yagni`/`shrink` `ZOrderPosition` struct + `ReadZOrder` method duplicated byte-for-byte
(only comments differ) in three Win32 surface classes.**
No stated rationale for the triplication, unlike other deliberate per-module duplication elsewhere
in this codebase which is consistently justified in comments.
`client/src/CcpClient.Desktop/Input/Win32InputPresence.cs:1029-1070`,
`client/src/CcpClient.Desktop/Pointer/Win32PointerSurface.cs:559-600`,
`client/src/CcpClient.Desktop/Glyph/Win32GlyphSurface.cs:862-903`
`net: -70 lines possible.`

**4. `shrink` Five near-identical `CapabilityState` refusal-description switches inside
`StudioPage.axaml.cs`, plus the same shape duplicated in `BubbleCountPanelNotices`.**
`DescribeSurface`, `DescribeSubliminalSurface`, `DescribePinkFilterSurface`,
`DescribeBouncingTextSurface`, `DescribeSpiralSurface` (same class) repeat the same 6 switch arms
verbatim except for one word ("is"/"was"). `BubbleCountPanelNotices.DescribeBothCapabilities`
builds two near-identical switches for the same reason.
Replacement: one private helper taking the state, the "available" text, and the tense; each
`Describe*` becomes a short wrapper.
`client/src/CcpClient.Desktop/Views/Pages/StudioPage.axaml.cs:2871-2921,3029-3042,3093-3105`,
`client/src/CcpClient.Desktop/Views/Pages/BubbleCountPanelNotices.cs:159-184`
`net: -42 lines possible (-30 StudioPage, -12 BubbleCountPanelNotices).`

**5. `shrink` The same capability-unavailable description block duplicated across
`BubbleCountEffect`, `LockCardEffect`, `PopQuizEffect`, and `MandatoryVideoEffect`.**
Three near-identical shapes (`DescribeStation`, the "no user reachable" block, the "surface
unavailable" block) repeated across these four Effects files.
`client/src/CcpClient.Desktop/Effects/BubbleCountEffect.cs:828-846`,
`client/src/CcpClient.Desktop/Effects/LockCardEffect.cs:523-559`,
`client/src/CcpClient.Desktop/Effects/PopQuizEffect.cs:653-689`,
`client/src/CcpClient.Desktop/Effects/MandatoryVideoEffect.cs:360-368`
`net: -35 lines possible.`

**6. `delete` Dead `GetMenuItemInfoW` P/Invoke, struct, and constants in production
`Win32TrayInterop.cs`.**
Zero call sites in `client/src` — the only reader of this shape,
`client/tests/CcpClient.Tests/TrayShellProbe.cs`, already re-declares its own private copy rather
than using the production one, so the production declarations are pure dead weight, not a test
seam.
`client/src/CcpClient.Desktop/Tray/Win32TrayInterop.cs:72-76,128-146,213-214`
`net: -27 lines possible.`

**7. `delete` `IntakeQuizAnswer` carries 12 properties nothing reads.**
`BeatId`, `Depth`, `Mechanic`, `PromptId`, `Score`, `LatencyMs`, `Steered`, `RewardFired`,
`RewardDecoupled`, `ChosenLabel`, `SteerIntensity`, `TimeoutMs` — each appears exactly once in
`client/src`+`client/tests`, its own declaration. `IntakeProfiler`, the only consumer, only ever
touches `Band`, `Correct`, `Tags`, `ChosenIndex`, `OptionCount`, `PromptHeat`, `IsTrick`,
`IsFreeChoice`.
Replacement: drop the 12 properties and add `[JsonExtensionData]` — the pattern this codebase
already uses on every other persisted/wire model to keep unknown fields round-tripping.
`client/src/CcpClient.Desktop/Features/Intake/IntakeQuizRun.cs:73-120`
`net: -24 lines possible.`

**8. `shrink` `GoonHostWindow.SendToPage` and `ChaosTunnelWindow.SendToPage` duplicate the same
webview dispatch glue.**
Both build the identical
`window.chrome.webview.dispatchEvent(new MessageEvent('message',{data:...}))` string and wrap
`InvokeScript(...).ContinueWith(...OnlyOnFaulted...)` with the same fault-log shape. The same
duplicate also exists in `Dtrh/DtrhHostWindow.axaml.cs:1341` and `DtrhLoomWindow.axaml.cs:326`
(outside this pair, foldable into the same fix). Distinct from pass 2's `LoopbackServer` finding —
that one is TCP ephemeral-port bind-retry logic; this is JS message-dispatch glue.
Replacement: one extension on the shared native webview wrapper, e.g.
`web.DispatchMessage(json, log, prefix)`, called from all four sites.
`client/src/CcpClient.Desktop/Features/Goon/GoonHostWindow.axaml.cs:615-633`,
`client/src/CcpClient.Desktop/Features/Chaos/ChaosTunnelWindow.cs:121-132`
`net: -20 lines possible (more if the Dtrh duplicates are folded in).`

**9. `yagni` `AiMemoryDocument.Disabled`, `RetentionMaxPairs`, `DormantSinceUtc` are documented
placeholders nothing reads.**
Each is commented in place as "placeholder from v1; c4 mechanics never consult it" — confirmed
unread anywhere else in the tree. Canonical config-nobody-set shape.
`client/src/CcpClient.Desktop/Ai/AiMemoryStore.cs:34-41`
`net: -20 lines possible.`

**10. `shrink` `SessionClock.Schedule`/`.Run` and `ScriptedClock.Schedule`/`.Run` duplicate the
same `Timer`-wrapping and fault-containment logic.**
The class docs even say so explicitly. The two *interfaces* stay separate deliberately (documented
reasoning about differing consumer counts) — only the implementation classes can share an internal
helper without touching either interface.
`client/src/CcpClient.Desktop/Session/SessionClock.cs:52-71`,
`client/src/CcpClient.Desktop/Session/ScriptedClock.cs:76-96`
`net: -18 lines possible.`

**11. `delete` `IAudioPlayer.Pause()`, `.PositionSec`, `.Volume` are never invoked through the
interface.**
Declared and implemented everywhere, including ~12 test fakes, but the one production consumer
(`SoundArbitration`) only ever calls `.Play()`/`.Stop()`/`.Dispose()`. `AudioPlayerState.Paused` is
consequently unreachable too.
`client/src/CcpClient.Desktop/Audio/AudioSeams.cs:26-35`,
`client/src/CcpClient.Desktop/Audio/SoundFlowAudioBackend.cs:164,166,170`
`net: -15 lines possible (production only, test fakes not counted).`

**12. `shrink` `DtrhSlotPickerWindow.RankName` reimplements `DtrhRanks.For` + `DtrhRanks.Name`
verbatim.**
Same thresholds array, same names array, same loop, instead of calling the existing static helper
already in the same feature.
`client/src/CcpClient.Desktop/Features/Dtrh/DtrhSlotPickerWindow.axaml.cs:294-310`
`net: -15 lines possible.`

**13. `yagni` `AiEndpointClassifier.ClassifyProviderEndpoint` and `FirstPartyProxyHost` have no
production caller.**
The one site that needs this (`CompanionParticipant.cs:87`) hardcodes the classification directly
instead of calling it. Sibling `ClassifyOllamaHost`, which *is* used by `LoopbackOllamaProvider`,
is unaffected and should stay.
`client/src/CcpClient.Desktop/Ai/AiOperationVocabulary.cs:41-59`
`net: -15 lines possible.`

**14. `shrink` `IntakeServingRoots.Probe` and `ArcademyServingRoots.Probe` are byte-for-byte
identical control flow.**
Missing → Incomplete-if-required-file-absent → Present, with file count — differing only in the
record/enum type names.
`client/src/CcpClient.Desktop/Features/Intake/IntakeServingRoots.cs:37-55`,
`client/src/CcpClient.Desktop/Features/Arcademy/ArcademyServingRoots.cs:76-94`
`net: -15 lines possible.`

**15. `shrink` `IntakeProtocol` and `ArcademyProtocol`'s `GetString`/`GetBool`/`GetInt`/
`GetDouble` `JsonElement` field readers are line-for-line identical.**
`client/src/CcpClient.Desktop/Features/Intake/IntakeProtocol.cs:248-264`,
`client/src/CcpClient.Desktop/Features/Arcademy/ArcademyProtocol.cs:509-520`
`net: -12 lines possible.`

**16. `shrink` `ChaosTunnelService.Describe(CapabilityState?)` reimplements the shared formatter
with a different template.**
The shared formatter (`DtrhHostWindow.DescribeState`/`IntakeHostWindow.DescribeState`) is
explicitly commented "one formatter, never two" — `GoonHostWindow` already reuses it instead of
rolling its own; `ChaosTunnelService` should too.
`client/src/CcpClient.Desktop/Features/Chaos/ChaosTunnelService.cs:344-354`
`net: -9 lines possible.`

**17. `delete` Three unused public constants.**
`ArcademyServingRoots.BorrowedSpiralDirectory`, `SyntheticAvatarPacks.CircuitDefPath`,
`SyntheticAvatarPacks.PulseDefPath` — the def-file names these were meant to back are already
computed inline elsewhere (`SyntheticAvatarPacks.WriteAll`).
`client/src/CcpClient.Desktop/Features/Arcademy/ArcademyServingRoots.cs:60`,
`client/src/CcpClient.Desktop/Features/AvatarTube/SyntheticAvatarPacks.cs:37,39`
`net: -8 lines possible.`

**18. `delete` `Win32VideoInterop.SetStretchBltMode` P/Invoke + `Halftone` constant, never called.**
`Win32VideoPresence.Blit` scales at a fixed 1:1 only — by the comment's own admission a
deliberately abandoned approach (`VideoLetterbox` does all scaling in managed code instead,
specifically to avoid GDI stretch blending). Distinct from pass 2's `MediaFoundationClipSource`
finding (different file, different pattern).
`client/src/CcpClient.Desktop/Video/Win32VideoInterop.cs:233-239`
`net: -7 lines possible.`

**19. `yagni` `DtrhHostWindow.PostLoomList()` is a one-line private wrapper with exactly one call
site.**
`client/src/CcpClient.Desktop/Features/Dtrh/DtrhHostWindow.axaml.cs:1249-1252,1313`
`net: -6 lines possible.`

**20. `delete` `DtrhProtocol.BuildMeta` and `DtrhProtocol.BuildPing` have no production caller.**
`DtrhMeta.SnapshotMessage()` builds its own anonymous object instead, and nothing sends a `"ping"`
message. Only their own round-trip unit tests call them.
`client/src/CcpClient.Desktop/Features/Dtrh/DtrhProtocol.cs:278,307`
`net: -2 lines possible.`

**21. `delete` `ScriptedSessionDials.DocumentCount` property has no caller anywhere in
`client/src` or `client/tests`.**
`client/src/CcpClient.Desktop/Session/ScriptedSessionDials.cs:124-125`
`net: -2 lines possible.`

**Total this pass: ~622 lines across 21 new findings, plus one independent reconfirmation not
recounted.**

## What was checked and explicitly not flagged

Every reviewer cross-referenced `client/tests` before calling anything dead. Ruled out as
legitimate, not YAGNI: the Arcademy feature tree (built ahead of an unannounced upstream flag flip,
per `ArcademyDoor.Available = false`, not speculative flexibility), the Camera capability's
currently-unreachable DirectShow/V4L2 enumeration (the codebase already argues for this at length
elsewhere), `[ComImport]` interop declarations (zero C# implementations by construction), and a
hand-rolled backward Fisher-Yates shuffle in `Effects/VideoClipPool.cs`/`PopQuizAsk.cs` that looks
like a `stdlib:` candidate but produces a different permutation than `Random.Shuffle<T>` for the
same seed — likely a deliberate parity pin, not an oversight.

## Out of scope (correctness/perf, not analyzed further)

- `DtrhSaveSlots.DeleteSlot` runs `StopAsync()`/`StartAsync()` synchronously via
  `.GetAwaiter().GetResult()`, possibly from a UI-thread path — potential deadlock risk.
- `Win32InputPresence.Escalate` couples the UI thread to a foreign process's input queue via
  `AttachThreadInput` with no timeout; the class's own doc admits no fixture can construct the
  hang case. `client/src/CcpClient.Desktop/Input/Win32InputPresence.cs:663-669`

## Scope not covered this pass

`client/tests` (~88,000 lines) and `spine-tasks/` remain uncovered by all three passes on this PR.

## On the missing brief and the weakened floor guard

Both already covered in detail by the PR description and the second-pass comment on this PR — not
repeated here. This pass's own reviewers independently rediscovered the brief's absence and were
handed its recovered content directly rather than re-deriving it from git history a third time.
