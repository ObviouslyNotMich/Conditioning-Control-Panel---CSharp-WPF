# Avalonia UI Parity Matrix

Audit of the Avalonia UI versus the legacy WPF implementation.
Status key:
- ✅ Parity achieved / functionally complete
- 🚧 Partially ported, known gaps
- ❌ Not started / missing
- ⚠️ Blocked by platform seam, no direct counterpart, or external dependency

Last updated: 2026-06-21

---

## Executive Summary

- **Build health:** All desktop heads compile with 0 warnings / 0 errors. Windows head starts, loads settings, and runs a background update check without crashing in a 20-second smoke test.
- **Main-merge sync (§19.3):** `AppSettings` drift, `Fredoka.ttf`, service-deltas, `UpdateService` rework, `ChaosCrashSentinel`, `ChaosBoonColors`, shared-host overlays, `ChaosSkiaFxOverlay`, and the `BubbleService` overhaul (ambient + chaos variants + field hazards + shared-host + global mouse hook) are done. §19.3 backlog is complete at the service/port level. `AvaloniaChaosService` is now a minimal functional wrapper that exercises `IBubbleService`; full run-state/boon logic is not ported.
- **Biggest remaining gaps:**
  1. **Dialogs & windows** — structure and commands are wired; all audited dialogs are now localized. Remaining hard-coded content is limited to a few WPF-only message strings and symbol-only affordances. `FeatureSettingsPopup` editor is fully ported.
  2. **Feature controls** — XAML and settings binding are solid; `ISessionEffectOrchestrator` starts/stops Flash, Video, Subliminal, MindWipe, BouncingText, Bubbles, BubbleCount, LockCard, and Overlay services. The Flash, Video (including attention checks, strict mode, and post-play penalties), BouncingText, Subliminal, MindWipe, LockCard, pink-filter/spiral/brain-drain, and ad-hoc timed/sustained overlay engines are real implementations.
  3. **Chaos overlays** — cursor-glow scale pulse, effect-banner throb, and field-FX radial shards/scale-shrink are now implemented. `AvaloniaChaosWindowZ` uses Windows `SetWindowPos` for focus-free topmost re-assert. `AvaloniaChaosService` now runs a full countdown → descent → spawn loop → scoring/heat/combo → wave-end draft → results lifecycle. The Hub has seeded catalogues and richer habit/boon/mantra/loadout rows. Localization remains parity-only (unlocalized like WPF).
  4. **AvatarTube** — window shell and core behavior restored (speech phrase system, AI chat, Circe emote engine, reaction hooks, drag/scale/floating/z-order); a few menu/fullscreen items remain stubbed.
  5. **MainWindow chrome** — custom window chrome, resize grips, title-bar drag/maximize, and cross-platform drag-drop import are implemented. A few notification strings and virtual-key labels remain unlocalized.

---

## Tab Views (`CCP.Avalonia/Views/Tabs`)

### Primary / Rich-Card Tab Views

| Tab View | XAML | Code-Behind | Rich Cards/Images | Localization | Design-Time Data | Notes |
|----------|------|-------------|-------------------|--------------|------------------|-------|
| SettingsTabView (Dashboard) | ✅ | ✅ | ✅ | ✅ | ✅ | Dashboard feature cards now open their feature popups and reflect active state; bottom helper buttons (webcam/app-info/scheduler-ramp/catalogue) wired. WebView2 browser host wired on Windows; feature-card right-click quick-toggles settings and starts/stops running services. Center logo loads mod-aware `logo.png`/`logo2.png`. Quick Links show login-state panel with display name + logout. Master-volume slider drives `IAudioPlayer.SetVolume`. Audio output device picker populates from `IAudioDeviceService` and sets `IAudioPlayer` output device; Test Audio plays the system test sound. Browser toolbar "Enhance if possible" binds to `BrowserTabViewModel.EnhanceIfPossible`. Deeper auto-bind badge and Haptic Audio Sync latency/intensity controls visible. HypnoTube/BambiCloud radio toggle fixed. Background update check runs from `MainWindowViewModel`. |
| LevelFeaturesTabView | ✅ | ✅ | ✅ | ✅ | ⚠️ | `FeatureCard` grid at top; detail card layout present. |
| QuestsTabView | ✅ | ✅ | ✅ | ✅ | ⚠️ | `QuestCard` + `RoadmapNodeCard` in place; roadmap interactions need smoke test. |
| EnhancementsTabView | ✅ | ✅ | ✅ | ✅ | ⚠️ | `SkillNodeCard` + skill images; connection lines present. |
| AssetsTabView | ✅ | ✅ | ✅ | ✅ | ⚠️ | `ContentPackCard` + tree browser; drag-drop and context menus need verification. |
| PresetsTabView | ✅ | ✅ | ✅ | ✅ | ⚠️ | Two-column layout with `PresetCard`; session editor and drag-drop need verification. |
| DeeperTabView | ✅ | ✅ | ✅ | ✅ | ⚠️ | Hub layout with filter pills; player/editor integration pending. |

### Secondary Tab Views

| Tab View | XAML Parity | Rich Cards/Images | Code-Behind/Commands | Localization | Design-Time Data | Notes |
|----------|-------------|-------------------|----------------------|--------------|------------------|-------|
| AchievementsTabView | ✅ | ✅ | ✅ | ✅ | ✅ | Hero banner, free/patron summary cards, achievement icon tiles with locked overlay, and season-recap action added. |
| ProfileTabView | ✅ | ✅ | ✅ | ✅ | ✅ | Banner gradient, avatar initials, stats row, badge pills, linked providers, and gallery placeholder added. |
| HapticsTabView | 🚧 | ✅ | ✅ | ✅ | ⚠️ | Hero card, connection card, algorithm cards, premium gate added. Still missing some WPF polish (tooltip guide, per-feature two-column layout). |
| AppInfoTabView | ✅ | ✅ | ✅ | ✅ | ✅ | Hero banner, version hero card, account/language/backup/legal/smoke-test cards, dynamic theme resources added. |
| BlinkTrainerTabView | 🚧 | ✅ | ✅ | ✅ | ✅ | Hero banner, blinking eye animation, stage frame, asset packs, session/webcam cards, premium gate added. Engine remains stubbed. |
| PatreonTabView | 🚧 | ✅ | ✅ | ✅ | ✅ | Brand-colored account cards, tier badge visuals, support-development card, cloud backup/privacy sections added. |
| DeeperHubTabView | ✅ | ✅ | ✅ | ✅ | ✅ | Hero banner, media-type glyph/brush converters, richer row cards, filter/sort panel, and empty state added. |
| DeeperSubmissionsTabView | ⚠️ | ❌ | 🚧 | ✅ | ✅ | No standalone WPF XAML; logic lived in code-behind; localization complete. |
| CompanionHubTabView | ✅ | ✅ | ✅ | ✅ | ✅ | Hero banner, status card with robot icon, action buttons, pose/audio cards, and settings link added. |
| CompanionTabView | ✅ | ✅ | ✅ | ✅ | ✅ | Hero banner, active-companion hero card, settings panel, prompt panel, companion roster cards with active badges, and installed-prompts list added. |
| PresetIOTabView | ✅ | ✅ | ✅ | ✅ | ✅ | Hero banner, preset list cards, drag-drop import zone, and action buttons added. |
| LeaderboardTabView | ✅ | ✅ | ✅ | ✅ | ✅ | Hero banner, mode toggle buttons, sort card, rank medal/number badges, online/OG badges, and richer row cards added. |
| LockdownTabView | ✅ | ✅ | ✅ | ✅ | ✅ | Lockdown icon header, hero image with pink glow, pulsing active border, and premium gate image added; remaining hard-coded VM strings localized. |
| RemoteControlTabView | ✅ | ✅ | ✅ | ✅ | ✅ | Hero banner, tier cards, opt-in tags, pairing panel with live QR code generation, emote picker, privacy toggles, premium gate added. |
| AvailableSubjectsTabView | 🚧 | 🚧 | ✅ | ✅ | ✅ | Horizontal card list okay; header emoji is text glyph; all text comes from ViewModel bindings (localization complete). |
| LabTabView | 🚧 | ✅ | ✅ | ✅ | ✅ | Hero banner, how-to-play expander, MIND/EYES zones/cards, webcam engine bar, wallpaper card, smokescreen overlay added. Webcam engine bar extracted to shared `WebcamEngineView`; `IWebcamService` seam injected into `LabTabViewModel`. Engines remain stubbed. |
| AwarenessTabView | ✅ | ✅ | ✅ | ✅ | ✅ | Hero banner, header with master switch/status, live pulse feed, presets placeholder, signal sources + safety section with color swatches, advanced link, and premium gate added; new settings-bound properties wired in VM. |
| BambiTakeoverTabView | ✅ | ✅ | ✅ | ✅ | ✅ | Description image, guide sidebar, Start/Stop + Test controls, and premium gate overlay added; autonomy consent dialog strings localized. |
| MarqueeTabView | ✅ | ✅ | ✅ | ✅ | ✅ | Hero banner and debug message/welcome/banner cards added; no legacy WPF counterpart. |
| AnimationsTabView | ✅ | ✅ | ✅ | ✅ | ✅ | Hero banner and debug animation control cards added; no legacy WPF counterpart. |
| CatalogueSubmissionsTabView | ✅ | ✅ | ✅ | ✅ | ✅ | Hero banner, status action buttons, and preset/session submission cards added; no legacy WPF counterpart. |
| PlaceholderTabView | ⚠️ | ❌ | ⚠️ | ✅ | ✅ | Avalonia-only placeholder; no WPF counterpart; localization complete. |

---

## Feature Controls (`CCP.Avalonia/Features`)

| Control | WPF Original | UI Controls | Code-Behind / Commands | Platform Seams | Localization | Overall |
|---------|--------------|-------------|------------------------|------------------|--------------|---------|
| FeatureCard | ✅ | ✅ | ✅ | ✅ | N/A | ✅ |
| QuestCard | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| RoadmapNodeCard | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| PresetCard | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| ContentPackCard | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| SkillNodeCard | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| AttentionCheckFeatureControl | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| VisualsFeatureControl | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| SchedulerFeatureControl | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| VideoFeatureControl | ✅ | ✅ | ✅ | ✅ (`IVideoService` seam + real `AvaloniaVideoService`; scheduled/random full-screen `VideoView` playback on primary + muted secondary windows when `DualMonitorEnabled`, `PlaySpecificVideo`, `PlayUrl`, strict-mode window, attention-check floating targets with dual-monitor spawn/expire, post-play pass/fail XP + achievement tracking, penalty retry loop, mercy message after 3 failures; participates in interaction queue) | ✅ | ✅ |
| FlashFeatureControl | ✅ | ✅ | ✅ | ✅ (`IFlashService` seam + real `AvaloniaFlashService`; topmost transparent overlay windows, scheduler, image loading, click-to-close, hydra multiplication) | ✅ | ✅ |
| BubbleCountFeatureControl | ✅ | ✅ | ✅ | ✅ (`IBubbleCountService`) | ✅ | ✅ |
| MindWipeFeatureControl | ✅ | ✅ | ✅ | ✅ (`IMindWipeService` seam + real `AvaloniaMindWipeService`; scheduled/loop playback via LibVLC, custom audio path, test trigger) | ✅ | ✅ |
| SubliminalFeatureControl | ✅ | ✅ | ✅ | ✅ (`ISubliminalService` seam + real `AvaloniaSubliminalService`; topmost transparent full-screen flashes, phrase pool, duration/opacity settings) | ✅ | ✅ |
| BouncingTextFeatureControl | ✅ | ✅ | ✅ | ✅ (`IBouncingTextService` seam + real `AvaloniaBouncingTextService`; per-screen transparent overlays, drifting text, bounce XP, corner-hit achievements) | ✅ | ✅ |
| PinkFilterFeatureControl | ✅ | ✅ | ✅ | ✅ (`IOverlayService` seam + real pink-filter overlay; start/stop/refresh/pulse wired) | ✅ | ✅ |
| BubblePopFeatureControl | ✅ | ✅ | ✅ | ✅ (`IBubbleService`) | ✅ | ✅ |
| SpiralFeatureControl | ✅ | ✅ | ✅ | ✅ (`IOverlayService` seam + real pink-filter, spiral, and brain-drain overlays; ad-hoc timed/sustained overlays implemented) | ✅ | ✅ |
| SystemFeatureControl | ✅ | ✅ | ✅ | ✅ (capabilities + dialogs OK; startup wired via `IStartupRegistration`; panic-key capture + global panic handling wired) | ✅ | ✅ |
| WebcamFeatureControl | ✅ | ✅ | ✅ | ✅ (`IWebcamService` seam + `AvaloniaWebcamService` stub; Lab webcam bar extracted to shared `WebcamEngineView` and hosted in popup; commands delegated to service) | ✅ | ✅ |
| LockCardFeatureControl | ✅ | ✅ | ✅ | ✅ (`ILockCardService` seam + real `AvaloniaLockCardService`; scheduled lock-card popups via `LockCardWindow`, multi-monitor sync, strict mode, completion events; participates in interaction queue) | ✅ | ✅ |
| AppInfoFeatureControl | ✅ | ✅ | ✅ | ✅ (Check Updates triggers `IUpdateService`; Report Bug opens `BugReportWindow`; account-section host preserved for future reparenting) | ✅ | ✅ |

---

## Dialogs (`CCP.Avalonia/Dialogs`)

| Dialog | WPF Original | Structure Parity | Localization | Events/Commands | Notes |
|--------|--------------|------------------|--------------|-----------------|-------|
| UpdateNotificationDialog | ✅ | ✅ | ✅ | ✅ | — |
| SessionEditDialog | ❌ | ✅ | ✅ | ✅ | No WPF original; all labels/buttons now localized. |
| WebcamConsentDialog | ✅ | ✅ | ✅ | ✅ | XAML and code-behind button/hint strings are localized. |
| OpenAiCompatibleSamplerSettingsDialog | ✅ | ✅ | ✅ | ✅ | — |
| ExplicitContentAcknowledgementDialog | ✅ | ✅ | ✅ | ✅ | — |
| CompanionPromptEditorDialog | ✅ | ✅ | ✅ | ✅ | Section description paragraphs hard-coded. |
| ContentPolicyWarningDialog | ✅ | ✅ | ✅ | ✅ | — |
| CompanionPhraseEditorDialog | ✅ | ✅ | ✅ | ✅ | Audited labels localized; inline add-phrase placeholder remains (no WPF counterpart). |
| LockCardColorDialog | ✅ | ✅ | ✅ | ✅ | — |
| ChatShortcutCaptureDialog | ✅ | ✅ | ✅ | ✅ | — |
| InputDialog | ✅ | ✅ | ✅ | ✅ | "OK" button already localized. |
| UpdateProgressDialog | ✅ | ✅ | ✅ | ✅ | Initial "0%" moved to localization. |
| RoadmapDiaryDialog | ✅ | ✅ | ✅ | ✅ | — |
| RoadmapStepPopup | ✅ | ✅ | ✅ | ✅ | Decorative icons moved to localization; image assets can replace glyphs later. |
| RoadmapConfirmDialog | ✅ | ✅ | ✅ | ✅ | Decorative camera icon moved to localization. |
| RoadmapStartDialog | ✅ | ✅ | ✅ | ✅ | Decorative camera icon moved to localization. |
| WelcomeDialog | ✅ | ✅ | ✅ | ✅ | Decorative heart icon moved to localization. |
| AssetSubmitDialog | ✅ | ✅ | ✅ | ✅ | Decorative upload icon moved to localization. |
| CatalogueSubmitDialog | ✅ | ✅ | ✅ | ✅ | — |
| CataloguePickerDialog | ✅ | ✅ | ✅ | ✅ | Download/play icons moved to localization keys. |
| ModManagerDialog | ✅ | ✅ | ✅ | ✅ | Code-behind user-facing strings now localized. |
| KnowledgeLinkEditorDialog | ✅ | ✅ | ✅ | ✅ | — |
| AwarenessPresetDetailDialog | ✅ | ✅ | ✅ | ✅ | Policy, trigger, footer, and all code-behind row labels localized. |
| LoginDialog | ✅ | ✅ | ✅ | ✅ | Device-code status/error strings now localized; uses `TextBox.PasswordChar` instead of `PasswordBox`. |
| UsernamePickerDialog | ✅ | ✅ | ✅ | ✅ | — |
| DisplayNameDialog | ✅ | ✅ | ✅ | ✅ | — |
| AttentionCheckSettingsDialog | ✅ | ✅ | ✅ | ✅ | "Test now" button + tooltip localized. |
| WarningDialog | ✅ | ✅ | ✅ | ✅ | Default confirm text falls back to localized key. |
| OfflineUsernameDialog | ❌ | ✅ | ✅ | ✅ | No WPF original; char count now localized. |
| AttentionTargetEditorDialog | ✅ | ✅ | ✅ | ✅ | Uses CheckBox for parity with WPF. |
| ColorEditorDialog | ✅ | ✅ | ✅ | ✅ | Uses CheckBox for parity with WPF. |
| TextEditorDialog | ✅ | ✅ | ✅ | ✅ | Title localized via `title_manager`. |

---

## Windows (`CCP.Avalonia/Windows`)

| Window | WPF Original | Structure Parity | Localization | Events/Commands | Notes |
|--------|--------------|------------------|--------------|-----------------|-------|
| AchievementPopup | ✅ | ✅ | ✅ | ✅ | Header icon rendered as emoji text. |
| AnnouncementPopup | ✅ | ✅ | ✅ | ✅ | Download/dismiss buttons localized. |
| BubbleCountResultWindow | ✅ | ✅ | ✅ | ✅ | — |
| BubbleCountWindow | ✅ | ✅ | ✅ | ✅ | — |
| BugReportWindow | ✅ | ✅ | ✅ | ✅ | — |
| EasterEggWindow | ✅ | ✅ | ✅ | ✅ | Narrative body localized (intentionally English source). |
| FeatureSettingsPopup | ✅ | ✅ | ✅ | ✅ | Full editor ported: header, minute slider, dynamic settings (slider/toggle/dropdown/file picker), ramping, phrase management, Delete/Done. |
| HapticsSetupWindow | ✅ | ✅ | ✅ | ✅ | Step instructions localized. |
| HelpVideoWindow | ✅ | ✅ | ✅ | ✅ | Title/header localized. |
| LockCardWindow | ✅ | ✅ | ✅ | ✅ | — |
| MantraWindow | ✅ | ✅ | ✅ | ✅ | — |
| MiniPlayerWindow | ✅ | ✅ | ✅ | ✅ | — |
| ModCreatorWindow | ✅ | ✅ | ✅ | ✅ | Minimize glyph `_` hard-coded. |
| PinkRushPopup | ✅ | ✅ | ✅ | ✅ | Title/default labels localized. |
| PopQuizWindow | ✅ | 🚧 | ✅ | ✅ | Title/ESC hint/XP text localized; simplified layout remains. |
| QuestCompletePopup | ✅ | ✅ | ✅ | ✅ | Title/header localized. |
| QuizCategoryEditorWindow | ✅ | ✅ | ✅ | ✅ | — |
| QuizReportWindow | ✅ | ✅ | ✅ | ✅ | — |
| QuizWindow | ✅ | 🚧 | ✅ | ✅ | Missing WPF storyboards/animations. |
| SeasonRecapWindow | ⚠️ in Controls | ✅ | 🚧 | ✅ | WPF counterpart in `Controls/`; button/note text set in code. |
| SessionCompleteWindow | ✅ | ✅ | ✅ | ✅ | — |
| SessionEditorWindow | ✅ | ✅ | ✅ | ✅ | — |
| SessionLogHistoryWindow | ✅ | ✅ | ✅ | ✅ | — |
| SplashScreen | ✅ | ✅ | ✅ | ✅ | Brand/status/version localized; progress fill is solid instead of gradient. |
| TutorialOverlay | ✅ | ✅ | ✅ | ✅ | First-run text localized. |
| WebcamCalibrationWindow | ✅ | ✅ | ✅ | ✅ | User-visible strings localized; eye-tracking pipeline stubbed. |
| WebcamGazeTrackerWindow | ✅ | ✅ | ✅ | ✅ | Strings localized; drop shadow removed. |
| WebcamLoadingSplash | ✅ | ✅ | ✅ | ✅ | Now uses localization bindings. |
| WebcamQuickRecalWindow | ✅ | ✅ | ✅ | ✅ | Strings localized. |

---

## Chaos Overlays (`CCP.Avalonia/Chaos`)

| WPF File | Avalonia Equivalent | Status | Notes |
|----------|---------------------|--------|-------|
| ChaosAnnouncerOverlay.cs | ChaosAnnouncerOverlay.axaml.cs | ✅ | Window chrome stubs remain. |
| ChaosBackdropService.cs | ChaosBackdropService.cs | ✅ | Click-absorbing backdrop. |
| ChaosBoonBarOverlay.cs | ChaosBoonBarOverlay.axaml.cs | ✅ | Hover-interactive bar. |
| ChaosCursorGlowOverlay.cs | ChaosCursorGlowOverlay.axaml.cs | ✅ | Scale pulse animation implemented. |
| ChaosDvdOverlay.cs | ChaosDvdOverlay.axaml.cs | ✅ | Bouncing text overlay. |
| ChaosEffectBannerOverlay.cs | ChaosEffectBannerOverlay.axaml.cs | ✅ | Throb animation implemented. |
| ChaosEStimOverlay.cs | ChaosEStimOverlay.axaml.cs | ✅ | Cursor-centered glow. |
| ChaosFieldFxOverlay.cs | ChaosFieldFxOverlay.axaml.cs | ✅ | Radial shards and trail-dot scale-shrink implemented; ring positioning fixed. |
| ChaosFlashOverlay.cs | ChaosFlashOverlay.axaml.cs | ✅ | Full-screen flash. |
| ChaosFxWindow.cs | ChaosFxWindow.cs | ✅ | Effect host window. |
| ChaosGifCascadeOverlay.cs | ChaosGifCascadeOverlay.axaml.cs | ✅ | GIF-rain overlay. |
| ChaosHubWindow.* | ChaosHubWindow.axaml + .axaml.cs + .Partial.cs | 🚧 | Catalogue data seeded (lifetime boons, upgrades, mantras, bubble variants); habit/boon/mantra row cards and loadout tiles now render real info with lock/unlock/train/equip affordances. Reveal/debug strips and bench already present; Lessons still use stub progress. |
| ChaosHudWindow.* | ChaosHudWindow.axaml + .axaml.cs + VM | ✅ | DropShadow TODOs. |
| ChaosIntroWindow.cs | ChaosIntroWindow.cs | ✅ | — |
| ChaosOverlayWindow.* | ChaosOverlayWindow.axaml + .axaml.cs | ✅ | Countdown/draft/results/story cards. |
| ChaosPopText.cs | ChaosPopText.axaml.cs | ✅ | — |
| ChaosToyButtonWindow.cs | ChaosToyButtonWindow.cs | ✅ | — |
| ChaosUnlockCardOverlay.cs | ChaosUnlockCardOverlay.axaml.cs + helper | ✅ | — |
| ChaosVibeTrailOverlay.cs | ChaosVibeTrailOverlay.axaml.cs | ✅ | — |
| ChaosWaveTimerOverlay.cs | ChaosWaveTimerOverlay.axaml.cs | ✅ | — |
| ChaosSkiaFxOverlay.cs | ChaosSkiaFxOverlay.cs | ✅ | Skia particle FX (trail, burst, ripple, cursor glow, lightning) ported with `ICustomDrawOperation`/`ISkiaSharpApiLease`. |
| BubbleService.cs | AvaloniaBubbleService.cs + BubbleEngine/BubbleState | ✅ | Ambient + chaos bubbles ported (variants, chain reaction, field hazards, shared-host, global mouse hook). |
| ChaosModeService.cs | AvaloniaChaosService | 🚧 | Now runs a full countdown → descent → spawn loop → scoring/heat/combo → wave-end boon draft → results lifecycle using `ChaosOverlayWindow`/`ChaosHudWindow` and `IBubbleService`. Still simplified vs WPF (no story cards, active toys, or lesson hooks). |
| ChaosWindowZ.cs | AvaloniaChaosWindowZ.cs | ✅ | Windows `SetWindowPos(HWND_TOPMOST/HWND_NOTOPMOST, SWP_NOACTIVATE)` wired; cross-platform fallback toggles `Topmost`. |

**Localization:** ❌ None of the Chaos UI is localized (WPF Chaos was also unlocalized, so this is parity, not a regression).

---

## AvatarTube (`CCP.Avalonia/AvatarTube`)

| WPF File | Avalonia Equivalent | Status | Notes |
|----------|---------------------|--------|-------|
| AvatarTubeWindow.xaml | AvatarTubeWindow.axaml | ✅ | Layout and tube visual ported. |
| AvatarTubeWindow.xaml.cs | AvatarTubeWindow.axaml.cs | ✅ | Speech/audio timers, AI send, moderation wiring, drag/wheel, chat shortcut, and menu state wired; a few menu items (engine, takeover, whispers, browser pause) still stubbed. |
| AvatarTubeWindow.Avatar.cs | AvatarTubeWindow.Avatar.cs | ⚠️ | Static avatar poses and Circe emote mode wired; portrait/emotive system still stubbed. |
| AvatarTubeWindow.ChatInput.cs | AvatarTubeWindow.ChatInput.cs | ✅ | AI reply uses `IAiService.GetBambiReplyExAsync`; moderation refusal handled; avatar click triggers Circe emote. |
| AvatarTubeWindow.CirceEmotes.cs | AvatarTubeWindow.CirceEmotes.cs | ✅ | `CirceEmoteEngine` drives two-layer GIF crossfades, talk/reaction scheduling, click emotes, and registry-based folder resolution. |
| AvatarTubeWindow.Reactions.cs | AvatarTubeWindow.Reactions.cs | ✅ | Activity/still-on, flash audio filename, level/companion, mindwipe/braindrain, and lock-card AI reaction hooks implemented. |
| AvatarTubeWindow.Speech.cs | AvatarTubeWindow.Speech.cs | ✅ | Phrase pools merge mod phrases (`IModService.GetPhrases/MakeModAware`) with custom phrases; idle/trigger/random-bubble timers wired. |
| AvatarTubeWindow.Windowing.cs | AvatarTubeWindow.Windowing.cs | ⚠️ | Floating, attach/detach, drag, scale clamping, and Windows z-order wired; fullscreen detection of other apps remains stubbed. |
| AvatarRandomBubble.cs | AvatarRandomBubble.cs | ⚠️ | Uses `bubble.png`; DPI-aware scaling and pop-sound/XP hooks wired; spawn focus check is Windows-only. |

**Localization:** 🚧 `.axaml` shell is localized; code-driven strings are mostly hard-coded English placeholders.

---

## Localization Status

| Area | Status | Notes |
|------|--------|-------|
| `{loc:Str}` markup extension | ✅ | `CCP.Avalonia/Localization/LocExtension.cs` |
| `en.json` | ✅ | 3537 keys, strict JSON, no duplicates |
| Dashboard/Settings tab | ✅ | Fully localized |
| Feature controls (primary) | ✅ | Localized |
| Tab views (primary) | ✅ | Localized |
| Secondary tab views | ✅ | All now use `{loc:Str}` |
| Dialogs | ✅ | All audited dialogs localized; a few symbol-only affordances remain |
| Windows | ✅ | All audited windows localized; a few symbol-only affordances remain |
| Chaos overlays | ❌ | All hard-coded English (parity with WPF) |
| AvatarTube | ✅ | Shell and code-driven strings localized; a few symbol-only affordances remain |
| Deeper editor/player | ❌ | Not audited separately; assumed hard-coded |

---

## Build & Runtime Status

| Head | Build | Run | Notes |
|------|-------|-----|-------|
| CCP.Avalonia (shared) | ✅ | N/A | 0 warnings, 0 errors |
| CCP.Avalonia.Desktop | ✅ | N/A | 0 warnings, 0 errors |
| CCP.Avalonia.Desktop.Windows | ✅ | ✅ | Starts, loads settings, no crash in 20s smoke test |
| CCP.Avalonia.Desktop.Linux | ⚠️ | ❌ | CI only; not tested locally |
| CCP.Avalonia.Desktop.macOS | ⚠️ | ❌ | CI only; not tested locally |
| CCP.Avalonia.Android | ⚠️ | ❌ | Workload not installed locally; CI expected |

---

## Critical Gaps (Ranked)

1. **Onboarding/privacy dialogs** — `WebcamConsentDialog`, `LoginDialog`, `AwarenessPresetDetailDialog`, and `SessionEditDialog` are now fully localized. Remaining localization work is in webcam windows/popups and a few WPF-only message strings.
2. **Webcam windows** — shells are ported; calibration/eye-tracking pipeline remains stubbed, but all user-facing strings are now localized.
3. **AvatarTube depth** — core behavior restored: speech phrase system, AI chat replies, Circe emote playback/scheduling, reaction hooks, and windowing (drag/scale/floating/z-order). Remaining: fullscreen detection of other apps, emotive portrait system, and a few context-menu toggles.
4. **Chaos overlays** — core animations/z-order helper and a simplified full run lifecycle are now in place; remaining work is WPF-level story cards, active toys, lesson hooks, and deeper boon runtime effects. Localization matches the unlocalized WPF version.
5. **Deeper player/editor** — UI is ported; full playback/editor integration not audited.

---

## Recommended Next Sprints

### Sprint A — Localization Blitz
- ✅ Onboarding/privacy dialogs localized (`WebcamConsentDialog`, `LoginDialog`, `AwarenessPresetDetailDialog`, `SessionEditDialog`).
- ✅ Webcam windows and popups localized (`AchievementPopup`, `AnnouncementPopup`, `PinkRushPopup`, `PopQuizWindow`, `QuestCompletePopup`, `SplashScreen`, `TutorialOverlay`, `WebcamCalibrationWindow`, `WebcamGazeTrackerWindow`, `WebcamQuickRecalWindow`).

### Sprint B — AvatarTube Behavior
- Restore speech phrase system, AI chat replies, Circe emote playback/scheduling, reactions, and windowing behavior.

### Sprint C — Chaos Overlay Polish
- Finish Skia/host overlay animations, z-order helper, and run-state/boon logic.

### Sprint D — MainWindow / Deeper Polish
- Audit MainWindow chrome behavior and Deeper player/editor integration.
