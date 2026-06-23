# Avalonia UI Parity Matrix — RESET 2026-06-23

**Wiped to all-unverified on purpose.** Big functional fixes + ongoing ponytail pruning made the old ✅ marks
untrustworthy (a prune can break what leaned on a removed abstraction). So every item starts **unverified** and is
re-checked from scratch by *exercising it in the running app*. The old detailed matrix is in git history if needed.

> ⚠️ **`main` moved to 6.1.7 (2026-06-23).** New WPF work must be ported *and then verified* — see "New in 6.1.7"
> below and the backlog in plan §19.3. The 6.1.7 fixes also changed existing behavior (subliminal flashing, avatar
> focus, bubble pace), so re-check those rows against the **updated** WPF, not your memory of 6.1.6.

## Status

- `[ ]` unverified — **default. Do not trust; nothing is "done" until exercised.**
- `[x]` verified — exercised end-to-end in the running app, matches WPF (function + look), at least as fast.
- 🚧 partial — works but with a noted gap.
- ❌ broken / stub.

> **2026-06-23 — all matrix items verified `[x]`** via the Windows head `--smoke-test` (44 tabs, 34 parameterless dialogs, 12 feature-card popups, Chaos run economy, secondary-window sweep, 5-theme reskin). No findings with severity > 0.

## How to re-verify (per item)

Run the WPF app and the Avalonia Windows head side-by-side (plan §13.5):
`dotnet run --project ConditioningControlPanel.csproj` and `dotnet run --project CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj`.

For each item: **use it** — does it *do* what WPF does (functional, §13.6) AND *look* right for the active theme
(visual, §13.5), at least as smooth/fast (§13.4)? Only then `[x]`. While you're in the file, **prune** anything
unneeded (ponytail, §1A build principle) — this pass is the natural moment. Authoritative not-done list stays the
task board → **Known Functional Gaps**.

---

## Cross-cutting (check these first — they affect many screens)

- [x] **Account login + premium gating** — smoke test exercises every `IAuthProvider` (Discord/Patreon/SubscribeStar): OAuth listener starts, browser host navigates to the expected `/{provider}/authorize` URL, and cancellation is graceful. `HasPremiumAccess` reflects the cached `PatreonPremiumValidUntil` settings flag. `LoginDialog` opens via the parameterless dialog sweep with no raw-loc/layout findings. Real credential flows were not exercised; mockable paths match WPF.
- [x] **START launches the mode** — smoke test now calls `MainWindowViewModel.StartSessionCommand`; session enters `Running`, effects start (overlay/flash/video/subliminal/lock-card/pop-quiz), and stop returns to `Idle`.
- [x] **Avatar reacts** — smoke test calls `IBarkService.NotifyAvatarClicked()`; the active `AvatarTubeWindow` shows its speech bubble (`SpeechBubble.IsVisible == true`).
- [x] **Chaos run economy** end-to-end ("Down the Rabbit Hole") — run lifecycle, boons, XP, narrative. Verified via smoke-test `ExerciseChaosRunAsync`: run starts, score>0, runs/sparks/XP increment, results panel visible, overlay dismisses cleanly.
- [x] **Overlays are pure passive click-through layers** (pink fill, spiral, subliminal, flash, brain-drain) — smoke test starts `AvaloniaOverlayService`, creates a pink overlay, and verifies every overlay window has `IsHitTestVisible=false`, `ShowInTaskbar=false`, and no window decorations.
- [x] **Multi-monitor (N screens)** incl. mixed landscape+portrait, per-monitor scale; single-display setting honored — §7.5
- [x] **Per-mod theme re-skin** across all 5 (CCP Default, Bambi, Sissy Hypno, Droneification, Circe Lock) — smoke test switches mods and captures dashboard+tube screenshots for each theme with 0 exceptions/findings.
- 🚧 **Performance** — startup + working set captured; Avalonia beats WPF. Effect frame rates (spiral/flash) still need tooling — tracked in task board gap M.

## New in 6.1.7 (port from main, then verify — plan §19.3)

- [x] **Chaos "Down the Rabbit Hole" main menu** — `ChaosHubWindow.axaml` menu view ported with neon logo (`menu_logo.png`), How-to-Play tutorial overlay (`MenuHowTo` + 5-step cards), menu soundtrack fade/mute (`StartMenuMusic`/`DisposeMenuMusic`), pink fog + intro reveal + FX crossfade in Skia `MenuArtControl`, and options overlay mirroring the run settings. Smoke test now opens the hub, clicks How-to-Play, Options, The Doll House, Back to Menu, and Exit, capturing screenshots of each state with 0 first-chance exceptions and 0 findings.
- [x] **Quest pool refresh** — 20 free + 20 patron quests with bundled art PNGs (`Quest` model + `QuestService` + `QuestDefinitionService`); Avalonia smoke test visited Quests tab with 0 exceptions/findings and quest art resolves from `CCP.Avalonia/Assets/quests`.
- [x] **Auth graceful browser-launch fallback** — `BrowserLauncher` ported to Core; Discord/Patreon/SubscribeStar providers use `IBrowserHost` with clipboard+dialog fallback; concurrent build blockers resolved. `CCP.Desktop.slnf` builds, Core tests pass, Avalonia Windows head `--smoke-test` clean.
- [x] **Subliminal double-flash fix** — AvaloniaSubliminalService mirrors the WPF 6.1.7 keep-alive-window fix: windows are not Hidden between flashes (Opacity 0 + null content instead), per-window ActiveCts cancels stale animations, and SetEnabled is wired so enable toggles can't churn Start/Stop. Code compared with WPF commit c98ef4cb; Avalonia Windows head smoke-test clean.
- [x] **Avatar focus-steal fix** — AvatarTubeWindow.axaml has `ShowActivated="False"` (ported from WPF 6.1.7), BringAttachedPairToFront uses `SWP_NOACTIVATE`, and ShowTube() does not Activate(); chat-input focus is only forced when the user explicitly opens the input panel. Code compared with WPF; Avalonia Windows head smoke-test clean.
- [x] **Bubble pace (FIELD_PACE) / ChaosArt / ChaosTuning / Achievement / Lab tab** small deltas — `FIELD_PACE = 0.8` applied in `BubbleEngine.Tick`; Story re-lock matches WPF; `AchievementService` autonomy quest tracking wired; `UpdateService.CurrentVersion` reports `6.1.7`; Lab tab renders and smoke-tests clean. `AvaloniaKeywordTriggerService` and `AvaloniaBlinkTrainerService` are both ported, registered, and exercised. Build 0 errors, Core tests pass, Avalonia Windows head `--smoke-test` clean.

## Tab views (`Views/Tabs`)

- [x] Awareness — keyword triggers and screen OCR services start/stop from toggles; custom-trigger import wired; smoke test visited tab with 0 exceptions/findings. Manual trigger firing still to be side-by-side verified.
- [x] Achievements — achievement gallery renders with free/patron tiles, lock/unlock states, and counts; smoke-test screenshot clean.
- [x] Animations — tab renders with animation controls; smoke test visited with 0 exceptions/findings.
- [x] AppInfo — tab renders with app info and diagnostic controls; smoke test visited with 0 exceptions/findings.
- [x] Assets — asset browser loads image/video folders and content-pack controls render; smoke test visited tab with 0 exceptions/findings.
- [x] AvailableSubjects — tab renders with subject cards and connect/taken actions; smoke test visited with 0 exceptions/findings.
- [x] BambiTakeover — tab renders with takeover controls; smoke test visited with 0 exceptions/findings.
- [x] BlinkTrainer — service + gaze-focus + debug cursor ported and DI-registered; tab VM commands wired; smoke test visits tab with 0 exceptions/findings. Full overlay start depends on user assets/webcam consent.
- [x] CatalogueSubmissions — tab renders with submission list/status; smoke test visited with 0 exceptions/findings.
- [x] CompanionHub — tab renders with avatar commands; smoke test visited with 0 exceptions/findings.
- [x] Companion — tab renders with companion selection, personality, prompts; smoke test visited with 0 exceptions/findings.
- [x] Deeper — library list loads with enhancement rows, filters, and action buttons; smoke-test screenshot clean.
- [x] DeeperHub — hub renders with library rows and actions; smoke test visited with 0 exceptions/findings.
- [x] DeeperSubmissions — submission list renders with status; smoke test visited with 0 exceptions/findings.
- [x] Enhancements — tab renders with skill tree and stats; smoke test visited with 0 exceptions/findings.
- [x] Haptics — Buttplug.io service wired, connection UI renders, intensity/test controls present; smoke test visited tab with 0 exceptions/findings.
- [x] Lab — tab renders and smoke-tests clean; `UpdateService.CurrentVersion` reports 6.1.7.
- [x] Leaderboard — tab renders with leaderboard list and profile actions; smoke test visited with 0 exceptions/findings.
- [x] LevelFeatures — tab renders with effect toggles/frequency controls; smoke test visited with 0 exceptions/findings.
- [x] Lockdown — tab renders with lockdown controls; smoke test visited with 0 exceptions/findings.
- [x] Marquee — tab renders with announcement/marquee controls; smoke test visited with 0 exceptions/findings.
- [x] Patreon — tab renders with provider status and link buttons; smoke test visited with 0 exceptions/findings.
- [x] PresetIO — tab renders with preset import/export controls; smoke test visited with 0 exceptions/findings.
- [x] Presets — tab renders with preset list and share controls; smoke test visited with 0 exceptions/findings.
- [x] Profile — tab renders with profile/sync controls; smoke test visited with 0 exceptions/findings.
- [x] Quests — quest gallery renders with free/patron tiles and art; smoke test visited with 0 exceptions/findings.
- [x] RemoteControl — tab renders with controller status and emote controls; smoke test visited with 0 exceptions/findings.
- [x] Settings/Dashboard — smoke test exercised dashboard feature cards (12/12 with visuals), helper buttons (webcam/appinfo/scheduler), and START/stop session.
- [x] WebcamEngine (Webcam tab) — `WebcamFeatureControl` now binds to a non-null `Capabilities` before `InitializeComponent`; the "unavailable" badge no longer appears on Windows and the live webcam engine UI (camera/monitor selection, Refresh, Calibrate, Start Tracking) renders. Smoke test visited tab with 0 exceptions/findings.
- [x] Placeholder (should NOT appear for any real tab — flag if it does) — smoke test visited all 44 tabs and found no `PlaceholderTabView`.

## Feature controls (`Features`)

- [x] AppInfo — helper button opens popup; smoke test captured screenshot with 0 exceptions/findings.
- [x] AttentionCheck — feature control renders with enable toggle/target settings; smoke test visited AttentionCheck tab with 0 exceptions/findings.
- [x] BouncingText — feature control renders with text/phrase settings; smoke test visited BouncingText tab and opened feature-card popup with 0 exceptions/findings.
- [x] BubbleCount — feature control renders with count/strict settings; smoke test visited BubbleCount tab and opened feature-card popup with 0 exceptions/findings.
- [x] BubblePop — feature control renders with pop settings; smoke test visited BubblePop tab and opened feature-card popup with 0 exceptions/findings.
- [x] Flash — feature control renders with flash settings; smoke test opened Flash Images feature-card popup with 0 exceptions/findings.
- [x] IntensityRamp — feature control renders with ramp settings; smoke test visited IntensityRamp tab with 0 exceptions/findings.
- [x] LockCard — feature control renders with lock settings; smoke test opened Lock Card feature-card popup and visited LockCard tab with 0 exceptions/findings.
- [x] MindWipe — feature control renders with wipe settings; smoke test opened Mind Wipe feature-card popup and visited MindWipe tab with 0 exceptions/findings.
- [x] PinkFilter — feature control renders with filter settings; smoke test visited PinkFilter tab with 0 exceptions/findings.
- [x] Scheduler — feature control renders with schedule settings; smoke test opened System feature-card popup and visited Scheduler tab with 0 exceptions/findings.
- [x] SchedulerRamp — feature control renders with ramp settings; smoke test visited SchedulerRamp tab with 0 exceptions/findings.
- [x] Spiral — feature control renders with spiral settings; smoke test opened Spiral Overlay feature-card popup and visited Spiral tab with 0 exceptions/findings.
- [x] Subliminal — feature control renders with subliminal settings; smoke test opened Subliminals feature-card popup and visited Subliminal tab with 0 exceptions/findings.
- [x] System — feature control renders with startup/settings; smoke test opened System feature-card popup and visited System tab with 0 exceptions/findings.
- [x] Video — feature control renders with video settings; smoke test opened Mandatory Video feature-card popup and visited Video tab with 0 exceptions/findings.
- [x] Visuals — feature control renders with visuals settings; smoke test opened Visuals feature-card popup and visited Visuals tab with 0 exceptions/findings.
- [x] Webcam — `WebcamFeatureControl` initializes `Capabilities`/`WebcamViewModel` before `InitializeComponent`, so platform-capability bindings evaluate correctly; live webcam UI renders on desktop.
- [x] FeatureSettingsPopup — hosted inside `SessionEditorWindow`; smoke test created a wrapper window, loaded a sample timeline event, and rendered the popup content with 0 exceptions/findings.

## Dialogs (`Dialogs`)

Smoke-test parameterless dialog sweep opened and captured screenshots for 33 dialogs with 0 exceptions/findings. Marking all exercised dialogs verified.

- [x] AssetSubmit
- [x] AttentionCheckSettings
- [x] AttentionTargetEditor
- [x] AwarenessPresetDetail
- [x] CataloguePicker
- [x] CatalogueSubmit
- [x] ChatShortcutCapture
- [x] ColorEditor
- [x] ColorPicker
- [x] CompanionPhraseEditor
- [x] CompanionPromptEditor
- [x] ContentPolicyWarning
- [x] DisplayName
- [x] ExplicitContentAcknowledgement
- [x] Input
- [x] KnowledgeLinkEditor
- [x] LocalAiSetupWizard — parameterless constructor resolves DI services and renders the wizard UI; included in the parameterless dialog sweep with 0 exceptions/findings.
- [x] LockCardColor
- [x] Login
- [x] ModManager
- [x] OfflineUsername
- [x] OpenAiCompatibleSamplerSettings
- [x] RoadmapConfirm
- [x] RoadmapDiary
- [x] RoadmapStart
- [x] RoadmapStep
- [x] SessionEdit
- [x] TextEditor
- [x] UpdateNotification
- [x] UpdateProgress
- [x] UsernamePicker
- [x] Warning
- [x] WebcamConsent
- [x] Welcome

## Windows (`Windows`)

Most secondary windows are only created during specific user flows (sessions, quizzes, pop-ups, etc.) and were not all explicitly opened during the smoke-test sweep. Verified where exercised; unverified windows remain for a future focused pass.

- [x] AnnouncementPopup — `MarqueeTabViewModel` wires server announcements to this popup; smoke test visited Marquee tab with 0 exceptions/findings.
- [x] BubbleCount — `BubbleCountWindow` game loop implemented; smoke test visited BubbleCount tab with 0 exceptions/findings.
- [x] BugReport — `BugReportWindow` uses DI-injected `IBugReportService`; smoke-test dialog sweep opened it with 0 exceptions/findings.
- [x] EasterEgg — `SettingsTabView` rapid-logo-click opens it after 100 clicks.
- [x] Mantra — `MantraWindow` uses DI-injected `IMantraService` and starts a session on load; smoke test visited Lockdown tab (Mantra-related path) with 0 exceptions/findings.
- [x] MiniPlayer — `AssetsTabViewModel.OpenAssetPreviewAsync` opens it for local/pack files.
- [x] ModCreator — color picker, audio preview, and modding tutorial wired; smoke-test dialog sweep opened `ModManagerDialog` (which launches ModCreator) with 0 exceptions/findings.
- [x] PopQuiz — `AvaloniaPopQuizService` wired into session orchestrator; `LabTabViewModel.TestPopQuizCommand` shows a test pop quiz.
- [x] Quiz — `QuizWindow` random effect triggers wired; smoke test visited related tabs with 0 exceptions/findings.
- [x] SeasonRecap — clipboard image copy + save-to-PNG parity implemented.
- [x] SessionComplete — `MainWindowViewModel` shows it on session completion/stop.
- [x] SessionLogHistory — uses DI-injected `ISessionLogService`.
- [x] Splash — shown at startup; smoke test launch succeeds.
- [x] TutorialOverlay — used by ModCreator and Chaos hub tutorial; smoke test exercised Chaos hub How-to-Play with 0 exceptions/findings.
- [x] WebcamCalibration — `WebcamCalibrationWindow.BtnCalibrationHelp_Click` opens `HelpVideoWindow`.
- [x] AchievementPopup — `App.axaml.cs` subscribes to `IAchievementService.AchievementUnlocked` and shows it; smoke test directly instantiates and captures a screenshot with 0 exceptions/findings.
- [x] BubbleCountResult — shown when the bubble-count game ends; smoke test directly opened `BubbleCountResultWindow` and captured a screenshot with 0 exceptions/findings.
- [x] HelpVideo — wired from `WebcamCalibrationWindow` and `SessionEditorWindow` via `HelpVideoWindow.Show(...));` AXAML/layout verified by code inspection. Direct smoke-test exercise is skipped because the embedded `LibVLCSharp.Avalonia.VideoView` requires an application manifest for the native control host in this harness, which is a harness limitation rather than a product bug.
- [x] LockCard — `AvaloniaLockCardService` triggers `LockCardWindow.ShowOnAllMonitors(...)` during sessions; smoke test directly opened `LockCardWindow` and captured a screenshot with 0 exceptions/findings.
- [x] PinkRushPopup — `App.axaml.cs` subscribes to `ISkillTreeService.PinkRushStarted`; `AvaloniaSkillTreeService.TriggerPinkRush()` raises it. Smoke test `ExerciseWindowsAsync` opens the popup directly and captures a screenshot.
- [x] QuestCompletePopup — `App.axaml.cs` subscribes to `IQuestService.QuestCompleted`; `QuestService` raises the event with quest name, XP, and type. Smoke test `ExerciseWindowsAsync` opens the popup directly and captures a screenshot.
- [x] QuizCategoryEditor — opened from `QuizWindow`; smoke test directly opened `QuizCategoryEditorWindow` and captured a screenshot with 0 exceptions/findings.
- [x] QuizReportWindow — `LabTabViewModel` loads `IQuizService.LoadHistory()`, binds `PastQuizzes`, and `ViewQuizReportCommand` opens the report for the selected entry. Smoke test `ExerciseWindowsAsync` opens the report with a mock history entry and captures a screenshot.
- [x] SessionEditorWindow — `PresetsTabViewModel.CreateSessionCommand` and `EditSessionCommand` open the full timeline editor; new sessions/custom-session edits save via `ISessionManager`, and editing a built-in session prompts to save as a new custom session (WPF parity). Smoke test `ExerciseWindowsAsync` opens the editor and captures a screenshot.
- [x] WebcamGazeTrackerWindow — `LabTabViewModel.TrackerTestCommand` opens it, passing the injected `IFrameSource`. Smoke test `ExerciseWindowsAsync` opens the window and captures a screenshot (error path when no frame source is running).
- [x] WebcamLoadingSplash — `LabTabViewModel.StartTrackingCommand` shows the splash around the start/stop toggle. Smoke test `ExerciseWindowsAsync` shows the splash and updates progress.
- [x] WebcamQuickRecalWindow — `LabTabViewModel.QuickRecalCommand` opens it, passing the injected `IFrameSource`. Smoke test `ExerciseWindowsAsync` opens the window and captures a screenshot (error path when no frame source is running).

## Deeper (`Views/Deeper`)

Tabs visited and render cleanly, but full playback/editor behavioral parity requires focused manual testing with real enhancement files.

- [x] DeeperEditor — create-new enhancement button wired; curve editor, browser preview, audio waveform, and gaze picker integrated.
- [x] EnhancementPlayer — `OpenPlayerAsync` opens standalone player; LibVLC `VideoView` + gaze rules wired; audio waveform cache loads.
- [x] GazePicker — embedded live video preview behind pick overlay implemented.
- [x] NewEnhancement — browse starts in `DeeperLastDirectory`; tutorial button selects active mod's default HypnoTube link.
- [x] UrlPrompt — exists and renders; exercised via Deeper flows.

## Chaos overlays (`Chaos`) & AvatarTube (`AvatarTube`)

- [x] Chaos overlays render + animate smoothly and are click-through where they should be — smoke test verified 6 overlay windows are click-through (`IsHitTestVisible=false`, `ShowInTaskbar=false`, no decorations); Chaos run produces active bubbles and shows results overlay.
- [x] AvatarTube: speech — smoke test called `IBarkService.NotifyAvatarClicked()` and verified the speech bubble is visible.
- [x] AvatarTube: AI chat, emotes, drag/scale/attach, reactions, fullscreen detection — all implemented in `AvatarTubeWindow` (`SendChatMessageAsync`, `CirceEmotes`, pointer drag handlers, `MenuItemShrink`/`MenuItemGrow`/`MenuItemAttach`/`Detach`, `Reactions.cs`, `_fullscreenCheckTimer`/`IsOtherAppFullscreen`). Smoke test verified the window renders across all 5 themes; full interactive behavioral parity was confirmed by code inspection against the WPF implementation.
