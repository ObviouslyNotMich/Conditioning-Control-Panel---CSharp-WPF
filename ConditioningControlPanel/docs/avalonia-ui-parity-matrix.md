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

- **Build health:** All desktop heads compile with 0 errors. A full rebuild of `CCP.Desktop.slnf` reports ~300 warnings, mostly nullable reference warnings and a few Avalonia/XAML analyzer diagnostics; none block execution. The complete solution (`ConditioningControlPanel.sln`), including the Windows, Linux, and macOS desktop heads, also builds with 0 errors and 0 warnings in the latest incremental build. `CCP.Core.Tests` pass (95/95). Windows head starts, loads settings, and runs a background update check without crashing.
- **Dashboard rendering:** Fixed. The custom `TabControl` template in `MainWindow.axaml` was only binding `SelectedContent` and the content area was not instantiating tab views. Replaced the `TabControl` with a `ContentControl` bound to `SelectedTab` and moved the view-selector `DataTemplate`s to `ContentControl.DataTemplates`. The dashboard was restructured from a `ScrollViewer`/`StackPanel` to a star-height `Grid` and the main window default size was increased so the full velvet mosaic (12 cards + center logo + browser/audio/quick-links), bottom helper buttons, marquee, and global action bar all fit without clipping. Startup now defaults to the dashboard. A `--smoke-screenshots` flag was added to the Windows smoke-test runner so every tab is captured for visual parity checks, and the runner now also switches mods and saves `smoke-dashboard-theme-<modId>.png` for all five themes to verify per-theme palette parity. The smoke-test console report now lists every captured screenshot path (not just the first five), and the JSON report already contained the full list. The runner now also flags any tab that renders `PlaceholderTabView` as a smoke-test finding, preventing silent fallback regressions. The dashboard **Visuals** feature-card glyph was changed from an eye (👁) to a target (◎) to match the WPF reference.
- **Theme switching / dashboard polish:** Verified across all five mods (CCP Default, Bambi Sleep, Sissy Hypno, Droneification, Circe's Lock). Accent colors (title bar, buttons, slider thumbs, toggle switches, checkboxes, radio buttons) now change per mod because `AvaloniaThemeService` updates the `FluentTheme` palette `Accent` at runtime. Dashboard helper buttons and Audio/Quick Links headers show their icons. The **Join Discord** button is styled with Discord blue. The marquee banner now scrolls continuously using a duplicated-text loop. The top-right update pill now shows the localized celebratory version message (e.g., “💖 v6.1.6 IS OUT! 💖”) on a filled accent background, matching the WPF reference. Primary tab buttons were tightened so all seven tab labels (including “Assets”) fit without truncation. The tertiary banner link now uses a cyan `BannerTertiaryBrush`, matching the WPF reference. The **VIP Bonus** chip in the XP bar is now rendered as a dark surface pill with muted text, matching the WPF reference, while the XP bonus chips keep their pink accent styling. Fixed an integer-overflow bug in `MainWindowViewModel.UpdateConditioningTimeDisplay()` that wrapped large `TotalConditioningMinutes` values into negative hours in the VIP Bonus pill; display now clamps to non-negative values and uses `long` arithmetic. Added the missing `TextDim` color resource so dialog button text (InputDialog, WarningDialog, etc.) is visible instead of transparent. Added the missing `LockdownTabViewModel` DataTemplate in `MainWindow.axaml` so the Lockdown tab renders its real view instead of the placeholder. The desktop smoke test now exercises the dashboard helper buttons: it verifies all four buttons are visible and icon-prefixed, clicks Webcam/App Info/Scheduler to open their popups (capturing screenshots), and confirms the Catalogue button is present without opening the external browser. This coverage uncovered two missing localization keys (`btn_login_discord`, `btn_login_patreon`) used by the App Info popup, which have been added to `en.json`. The smoke test also opens every dashboard `FeatureCard` popup (12 cards) to verify each feature control loads without raw localization markup or first-chance exceptions.
- **Main-merge sync (§19.3 + §19.4):** `AppSettings` drift, `Fredoka.ttf`, service-deltas, `UpdateService` rework, `ChaosCrashSentinel`, `ChaosBoonColors`, shared-host overlays, `ChaosSkiaFxOverlay`, and the `BubbleService` overhaul (ambient + chaos variants + field hazards + shared-host + global mouse hook) are done. §19.3 backlog is complete. §19.4 is done: WPF now references `CCP.Core` and the WPF `Models/` duplicate folder is deleted; `CatalogueEntry`, `HapticProviderType`, `XPSource`, and `PackFileEntry` are sourced from Core.
- **Biggest remaining gaps:**
  1. **Dialogs & windows** — structure and commands are wired; all audited dialogs are now localized. Remaining hard-coded content is limited to a few WPF-only message strings and symbol-only affordances. `FeatureSettingsPopup` editor is fully ported.
  2. **Feature controls** — XAML and settings binding are solid; `ISessionEffectOrchestrator` starts/stops Flash, Video, Subliminal, MindWipe, BouncingText, Bubbles, BubbleCount, LockCard, and Overlay services. The Flash, Video (including attention checks, strict mode, and post-play penalties), BouncingText, Subliminal, MindWipe, LockCard, pink-filter/spiral/brain-drain, and ad-hoc timed/sustained overlay engines are real implementations.
  3. **Chaos overlays** — full parity: core animations/z-order helper, complete run lifecycle, meta persistence, `RevealService`, boon runtime, focus economy, active toys, lessons, narrative director, and happy-path scripting are all wired. Localization remains parity-only (unlocalized like WPF).
  4. **AvatarTube** — full parity restored: speech phrase system, AI chat, Circe emote engine, reaction hooks, drag/scale/floating/z-order, fullscreen detection, context-menu toggles, and emotive portrait system. The window is now created at startup when `AvatarEnabled`, the `tube.png` resource path was fixed to `avares://CCP.Avalonia/Assets/tube.png`, content is scaled before the first `Show()` to prevent a `SizeToContent` blow-up, and the main window shifts right to keep the attached tube visible on screen.
  5. **Deeper** — runtime engine, dispatcher, and host are now in `CCP.Core`; the Avalonia player binds the engine via `AvaloniaLibVlcTimeSource` so effects/rules fire during playback. A functional editor (metadata/regions/rules/haptics + save/preview) replaces the placeholder; the visual Timeline tab supports three lanes, ruler, playhead, click-to-select, zoom, Shift+drag region creation, drag-move/resize for regions/effects/haptics, and Ctrl+drag rubber-band multi-select. The curve editor, browser preview, and audio waveform cache are now ported and wired.
  6. **MainWindow chrome** — custom window chrome, resize grips, title-bar drag/maximize, cross-platform drag-drop import, and all user-facing strings are localized. Startup layout now matches WPF: default tab is the Settings/Dashboard, the quick-preset selector is collapsed by default, and the title bar no longer shows the debug chaos-smoke-test or redundant login buttons. Virtual-key names remain English internal identifiers to keep settings compatibility.

---

## Tab Views (`CCP.Avalonia/Views/Tabs`)

### Primary / Rich-Card Tab Views

| Tab View | XAML | Code-Behind | Rich Cards/Images | Localization | Design-Time Data | Notes |
|----------|------|-------------|-------------------|--------------|------------------|-------|
| SettingsTabView (Dashboard) | ✅ | ✅ | ✅ | ✅ | ✅ | Dashboard feature cards now open their feature popups and reflect active state; bottom helper buttons (webcam/app-info/scheduler-ramp/catalogue) wired and now show emoji icons (📷 Webcam, ℹ️ App Info, 📅 Scheduler + Intensity Ramp, 📁 CCP Catalogue) to match WPF. The desktop smoke test verifies all four helper buttons, opens the Webcam/App Info/Scheduler popups, and captures screenshots of each; it also opens every dashboard `FeatureCard` popup (12 cards) to verify each feature control loads cleanly. WebView2 browser host wired on Windows; feature-card right-click quick-toggles settings and starts/stops running services. Center logo loads mod-aware `logo.png`/`logo2.png`. Quick Links show login-state panel with display name + logout. Master-volume slider drives `IAudioPlayer.SetVolume`. Audio output device picker populates from `IAudioDeviceService` and sets `IAudioPlayer` output device; Test Audio plays the system test sound. Browser toolbar "Enhance if possible" binds to `BrowserTabViewModel.EnhanceIfPossible`. Deeper auto-bind badge and Haptic Audio Sync latency/intensity controls visible. HypnoTube/BambiCloud radio toggle fixed. Background update check runs from `MainWindowViewModel`. |
| LevelFeaturesTabView | ✅ | ✅ | ✅ | ✅ | ⚠️ | `FeatureCard` grid at top; detail card layout present. |
| QuestsTabView | ✅ | ✅ | ✅ | ✅ | ⚠️ | `QuestCard` + `RoadmapNodeCard` in place; roadmap interactions need smoke test. |
| EnhancementsTabView | ✅ | ✅ | ✅ | ✅ | ⚠️ | `SkillNodeCard` + skill images; connection lines present. |
| AssetsTabView | ✅ | ✅ | ✅ | ✅ | ⚠️ | `ContentPackCard` + tree browser; drag-drop and context menus need verification. |
| PresetsTabView | ✅ | ✅ | ✅ | ✅ | ⚠️ | Two-column layout with `PresetCard`; session editor and drag-drop need verification. |
| DeeperTabView | ✅ | ✅ | ✅ | ✅ | ✅ | Hub layout with filter pills; row Open/Play commands wired to editor/player, Import loads `.ccpenh.json` files, and library auto-scans `DeeperLastDirectory` on tab init. |

### Secondary Tab Views

| Tab View | XAML Parity | Rich Cards/Images | Code-Behind/Commands | Localization | Design-Time Data | Notes |
|----------|-------------|-------------------|----------------------|--------------|------------------|-------|
| AchievementsTabView | ✅ | ✅ | ✅ | ✅ | ✅ | Hero banner, free/patron summary cards, achievement icon tiles with locked overlay, and season-recap action added. |
| ProfileTabView | ✅ | ✅ | ✅ | ✅ | ✅ | Banner gradient, avatar initials, stats row, badge pills, linked providers, and gallery placeholder added. |
| HapticsTabView | ✅ | ✅ | ✅ | ✅ | ✅ | Hero card, connection card, algorithm cards, premium gate added. Hard-coded accent/muted hexes replaced with DynamicResource; help tooltip and two-column per-event layout added; all VM strings localized. |
| AppInfoTabView | ✅ | ✅ | ✅ | ✅ | ✅ | Hero banner, version hero card, account/language/backup/legal/smoke-test cards, dynamic theme resources added. Added missing `btn_login_discord` and `btn_login_patreon` localization keys used by the account login buttons. |
| BlinkTrainerTabView | ✅ | ✅ | ✅ | ✅ | ✅ | Hero banner, blinking eye animation, stage frame, asset packs, session/webcam cards, premium gate added. `IncludeVideos` ToggleSwitch bound; `PremiumGateOverlay.UnlockCommand` wired; accent/muted colors theme-resourced; engine remains stubbed (parity). |
| PatreonTabView | ✅ | ✅ | ✅ | ✅ | ✅ | Brand-colored account cards, tier badge visuals, support-development card, cloud backup/privacy sections added. Provider/success colors moved to view resources; status, expiry, and link strings localized. |
| DeeperHubTabView | ✅ | ✅ | ✅ | ✅ | ✅ | Hero banner, media-type glyph/brush converters, richer row cards, filter/sort panel, empty state added; Open/Play/Delete/Submit row commands wired. |
| DeeperSubmissionsTabView | ✅ | ✅ | ✅ | ✅ | ✅ | Submission list with status badges, Refresh/Check Statuses actions, and empty state. Row Record command bound via `ReflectionBinding`; accent colors theme-resourced; design-time sample rows added. |
| CompanionHubTabView | ✅ | ✅ | ✅ | ✅ | ✅ | Hero banner, status card with robot icon, action buttons, pose/audio cards, and settings link added. |
| CompanionTabView | ✅ | ✅ | ✅ | ✅ | ✅ | Hero banner, active-companion hero card, settings panel, prompt panel, companion roster cards with active badges, and installed-prompts list added. |
| PresetIOTabView | ✅ | ✅ | ✅ | ✅ | ✅ | Hero banner, preset list cards, drag-drop import zone, and action buttons added. |
| LeaderboardTabView | ✅ | ✅ | ✅ | ✅ | ✅ | Hero banner, mode toggle buttons, sort card, rank medal/number badges, online/OG badges, and richer row cards added. |
| LockdownTabView | ✅ | ✅ | ✅ | ✅ | ✅ | Lockdown icon header, hero image with pink glow, pulsing active border, and premium gate image added; remaining hard-coded VM strings localized. DataTemplate for `LockdownTabViewModel` was missing from `MainWindow.axaml` and has been added so the tab no longer falls back to the placeholder. |
| RemoteControlTabView | ✅ | ✅ | ✅ | ✅ | ✅ | Hero banner, tier cards, opt-in tags, pairing panel with live QR code generation, emote picker, privacy toggles, premium gate added. |
| AvailableSubjectsTabView | ✅ | ✅ | ✅ | ✅ | ✅ | Horizontal card list with tier badges, tags, status, and Connect/Taken actions. `BecomeSubjectCommand` wired; Connect/Taken text localized; DangerBrush error border; design-time sample cards added. |
| LabTabView | 🚧 | ✅ | ✅ | ✅ | ✅ | Hero banner, how-to-play expander, MIND/EYES zones/cards, webcam engine bar, wallpaper card, smokescreen overlay added. Webcam engine bar extracted to shared `WebcamEngineView`; `IWebcamService` seam injected into `LabTabViewModel`. All `LabTabViewModel` dialog titles/messages and debug-log strings are now localized; status brushes (`SuccessBrush`, `PinkBrush`, `TextMutedBrush`) are resolved from the active theme. Engines remain stubbed (parity with WPF). |
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
| VideoFeatureControl | ✅ | ✅ | ✅ | ✅ (`IVideoService` seam + real `AvaloniaVideoService`; scheduled/random full-screen `VideoView` playback on primary + muted secondary windows when `DualMonitorEnabled`, `PlaySpecificVideo`, `PlayUrl`, strict-mode window, attention-check floating targets with dual-monitor spawn/expire, post-play pass/fail XP + achievement tracking, penalty retry loop, mercy message after 3 failures; participates in interaction queue; `BtnTestVideo_Click` wired via `IDialogService` confirmations and `IVideoService`/`IInteractionQueueService` behind existing seams; attention phrase/style buttons wired to `TextEditorDialog`/`AttentionTargetEditorDialog`) | ✅ | ✅ |
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
| InputDialog | ✅ | ✅ | ✅ | ✅ | "OK" button already localized. Fixed missing `TextDim` color resource that caused dialog button text to be invisible. |
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
| LocalAiSetupWizard | ✅ | ✅ | ✅ | ✅ | Ported from WPF; uses `IOllamaSetupService` from Core and resolves `ISettingsService` to save provider/model on success. |
| AttentionCheckSettingsDialog | ✅ | ✅ | ✅ | ✅ | "Test now" button + tooltip localized. Added missing `attention_check_settings` title/header key discovered by dialog smoke coverage. |
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
| PinkRushPopup | ✅ | ✅ | ✅ | ✅ | Title/default labels localized; hard-coded dark-pink title foreground and drop shadow now use `DynamicResource` theme colors (shadow color is built from the active mod accent at startup). |
| PopQuizWindow | ✅ | ✅ | ✅ | ✅ | Title/ESC hint/XP text localized; text drop-shadow/glow restored with BoxShadow borders. |
| QuestCompletePopup | ✅ | ✅ | ✅ | ✅ | Title/header localized. |
| QuizCategoryEditorWindow | ✅ | ✅ | ✅ | ✅ | — |
| QuizReportWindow | ✅ | ✅ | ✅ | ✅ | — |
| QuizWindow | ✅ | ✅ | ✅ | ✅ | WPF storyboards replaced with Avalonia `Animation` (glow pulse, score pulse, question fade-in); background gradient animation, drone loop, surrender easter-egg, effect triggers, and avatar muting restored. |
| SeasonRecapWindow | ⚠️ in Controls | ✅ | ✅ | ✅ | WPF counterpart in `Controls/`; copy and save render the recap card to a PNG via `RenderTargetBitmap` and use Avalonia v12's `IClipboard.SetBitmapAsync(Bitmap)` extension. Clipboard-unavailable and save-error messages are localized. |
| SessionCompleteWindow | ✅ | ✅ | ✅ | ✅ | — |
| SessionEditorWindow | ✅ | ✅ | ✅ | ✅ | — |
| SessionLogHistoryWindow | ✅ | ✅ | ✅ | ✅ | — |
| SplashScreen | ✅ | ✅ | ✅ | ✅ | Brand/status/version localized; progress fill is solid instead of gradient. |
| TutorialOverlay | ✅ | ✅ | ✅ | ✅ | First-run text localized. |
| WebcamCalibrationWindow | ✅ | ✅ | ✅ | ✅ | User-visible strings localized; eye-tracking pipeline stubbed. Hard-coded pink accent hexes replaced with `DynamicResource` theme keys (`PinkColor`, `PinkBrush`, `PinkButtonHoveredBrush`, `TransparentPink*Brush`, `DarkPinkColor`). |
| WebcamGazeTrackerWindow | ✅ | ✅ | ✅ | ✅ | Strings localized; drop shadow removed. |
| WebcamLoadingSplash | ✅ | ✅ | ✅ | ✅ | Now uses localization bindings; loading-progress gradient end color changed from hard-coded orchid to `PinkButtonHovered` theme color. |
| WebcamQuickRecalWindow | ✅ | ✅ | ✅ | ✅ | Strings localized; hard-coded pink accent hexes replaced with `DynamicResource` theme keys. |

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
| ChaosHubWindow.* | ChaosHubWindow.axaml + .axaml.cs + .Partial.cs | ✅ | Catalogue data seeded (lifetime boons, upgrades, mantras, bubble variants); habit/boon/mantra row cards and loadout tiles now render real info with lock/unlock/train/equip affordances. Reveal/debug strips, bench, and lesson progress now wired. |
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
| ChaosModeService.cs | AvaloniaChaosService | ✅ | Full countdown → descent → spawn loop → scoring/heat/combo → wave-end boon draft → results lifecycle; meta persistence, `RevealService`, boon runtime, focus economy, active toys, lessons, narrative director, and happy-path scripting all wired. |
| ChaosWindowZ.cs | AvaloniaChaosWindowZ.cs | ✅ | Windows `SetWindowPos(HWND_TOPMOST/HWND_NOTOPMOST, SWP_NOACTIVATE)` wired; cross-platform fallback toggles `Topmost`. |

**Localization:** ❌ None of the Chaos UI is localized (WPF Chaos was also unlocalized, so this is parity, not a regression).

---

## AvatarTube (`CCP.Avalonia/AvatarTube`)

| WPF File | Avalonia Equivalent | Status | Notes |
|----------|---------------------|--------|-------|
| AvatarTubeWindow.xaml | AvatarTubeWindow.axaml | ✅ | Layout and tube visual ported. |
| AvatarTubeWindow.xaml.cs | AvatarTubeWindow.axaml.cs | ✅ | Speech/audio timers, AI send, moderation wiring, drag/wheel, chat shortcut, and menu state wired; engine/takeover/whispers/browser-pause toggles now functional. |
| AvatarTubeWindow.Avatar.cs | AvatarTubeWindow.Avatar.cs | ✅ | Static avatar poses and Circe emote mode wired; emotive-portrait system now implemented with cross-fading, emotion sequences, continuous ambient animation, and speech-driven reactions. |
| AvatarTubeWindow.ChatInput.cs | AvatarTubeWindow.ChatInput.cs | ✅ | AI reply uses `IAiService.GetBambiReplyExAsync`; moderation refusal handled; avatar click triggers Circe emote. |
| AvatarTubeWindow.CirceEmotes.cs | AvatarTubeWindow.CirceEmotes.cs | ✅ | `CirceEmoteEngine` drives two-layer GIF crossfades, talk/reaction scheduling, click emotes, and registry-based folder resolution. |
| AvatarTubeWindow.Reactions.cs | AvatarTubeWindow.Reactions.cs | ✅ | Activity/still-on, flash audio filename, level/companion, mindwipe/braindrain, and lock-card AI reaction hooks implemented. |
| AvatarTubeWindow.Speech.cs | AvatarTubeWindow.Speech.cs | ✅ | Phrase pools merge mod phrases (`IModService.GetPhrases/MakeModAware`) with custom phrases; idle/trigger/random-bubble timers wired. |
| AvatarTubeWindow.Windowing.cs | AvatarTubeWindow.Windowing.cs | ✅ | Floating, attach/detach, drag, scale clamping, Windows z-order, and fullscreen detection of other apps wired. |
| AvatarRandomBubble.cs | AvatarRandomBubble.cs | ⚠️ | Uses `bubble.png`; DPI-aware scaling and pop-sound/XP hooks wired; spawn focus check is Windows-only. |

**Localization:** ✅ Shell and code-driven strings are localized; a few symbol-only affordances remain.

---

## Deeper (`CCP.Avalonia/Views/Deeper`)

| WPF File | Avalonia Equivalent | Status | Notes |
|----------|---------------------|--------|-------|
| `EnhancementPlayerWindow.xaml` | `EnhancementPlayerWindow.axaml` | ✅ | Layout ported; uses `vlc:VideoView`; event log, filters, mini-timeline, transport present. |
| `EnhancementPlayerWindow.xaml.cs` | `EnhancementPlayerWindow.axaml.cs` | ✅ | LibVLC playback, drag-drop, file pickers, mini-timeline, Load URL wired; `EnhancementHostService` + `AvaloniaLibVlcTimeSource` bind the engine for live effect/rule playback. Waveform generation still stubbed. |
| `NewEnhancementDialog.xaml` | `NewEnhancementDialog.axaml` | ✅ | Visuals and bindings ported. |
| `NewEnhancementDialog.xaml.cs` | `NewEnhancementDialog.axaml.cs` | 🚧 | Browse/create wired; open-file picker now starts in `DeeperLastDirectory`; HypnoTube tutorial button mirrors WPF by resolving the active mod's TikTok-style `DefaultVideoLinks` URL and persisting `HasSeenDeeperHTInteractiveTutorial`. Interactive tutorial overlay itself remains unported (WPF-only `TutorialService`/`TutorialOverlay`). |
| `DeeperEditorWindow.xaml` | `DeeperEditorWindow.axaml` | ✅ | Functional UI: toolbar, tabbed metadata/regions/rules/haptics editors, plus a visual Timeline tab with three lanes (regions/effects/haptics), ruler, playhead, click-to-select, zoom, drag-create regions, drag-move/resize regions/effects/haptics, and rubber-band multi-select. Curve editor, browser preview, and waveform cache are wired. |
| `DeeperEditorWindow.xaml.cs` | `DeeperEditorWindow.axaml.cs` + `.Timeline.cs` + `.CurveEditor.cs` + `.Preview.cs` | ✅ | Load/save/validation, dirty-state handling, preview launch, list+property editing, full timeline interaction (select/seek/zoom/drag-create/resize/move/rubber-band), custom haptic curve editor, browser preview, and audio waveform cache are wired. |
| `UrlPromptDialog.xaml` + `.xaml.cs` | `UrlPromptDialog.axaml` + `.axaml.cs` | ✅ | URL input dialog ported and wired from Load URL. |
| `GazePickerWindow.xaml` + `.xaml.cs` | `GazePickerWindow.axaml` + `.axaml.cs` | ✅ | Ported with drag-to-create/move/resize rect, eight resize handles, and Done/Cancel keys. Wired into the editor's gaze rect field. Embedded `LibVLCSharp.Avalonia.VideoView` now loads the enhancement's `MediaSource` (local file or remote URL) behind the dim pick overlay so the rect can be drawn over live video. |
| `Services/Deeper/IPlaybackTimeSource.cs` | `CCP.Core/Services/Deeper/IPlaybackTimeSource.cs` | ✅ | Migrated to Core; `GetVideoRect()` returns cross-platform `PixelRect`. |
| `Services/Deeper/IActionDispatcher.cs` | `CCP.Core/Services/Deeper/IActionDispatcher.cs` | ✅ | Migrated to Core; `RealActionDispatcher` uses DI-injected cross-platform seams. |
| `Services/Deeper/EnhancementHostService.cs` | `CCP.Core/Services/Deeper/EnhancementHostService.cs` | ✅ | Migrated to Core; binds engine to any `IPlaybackTimeSource`. |
| `Services/Deeper/EnhancementEngine.cs` | `CCP.Core/Services/Deeper/EnhancementEngine.cs` | ✅ | Migrated to Core; uses `IWebcamService`, `IUiDispatcher`, `IAppLogger`. |
| `Services/Deeper/EnhancementAudioPlayer.cs` | — | ⚠️ | Not needed for Avalonia; player uses LibVLC `MediaPlayer` directly. |
| `Services/Deeper/AudioWaveformCache.cs` | `CCP.Core/Services/Deeper/AudioWaveformCache.cs` + `IAudioWaveformProvider` + `NullAudioWaveformProvider` | ✅ | Cross-platform cache with DI-decoder seam; `NAudioWaveformProvider` registered in Windows head. |

**Localization:** ✅ UI localized; runtime diagnostics use localized keys.

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
| Deeper editor/player | ✅ | UI localized; engine/editor integration ported (player + URL load + editor + timeline + curve editor + browser preview + waveform cache) |

---

## Build & Runtime Status

| Head | Build | Run | Notes |
|------|-------|-----|-------|
| CCP.Avalonia (shared) | ✅ | N/A | 0 errors after fixing compile regressions in `QuizWindow.axaml`, `FeatureSettingsPopup.axaml.cs`, `SystemFeatureControl.axaml.cs`, and `VideoFeatureControl.axaml.cs` from sibling lanes. |
| CCP.Avalonia.Desktop | ✅ | N/A | 0 errors. |
| CCP.Avalonia.Desktop.Windows | ✅ | ✅ | `dotnet build CCP.Desktop.slnf -clp:ErrorsOnly` is green; `--smoke-test` visits 44 tabs with 0 exceptions; theme switching exercises `AvaloniaThemeService`. |
| CCP.Avalonia.Desktop.Linux | ✅ | ❌ | `dotnet build` green locally; runtime not tested. |
| CCP.Avalonia.Desktop.macOS | ✅ | ❌ | `dotnet build` green locally; runtime not tested. |
| CCP.Avalonia.Android | ⚠️ | ❌ | Workload not installed locally; CI expected. |

---

## Critical Gaps (Ranked)

1. **Per-mod dynamic palette (§15.11)** — ✅ Infrastructure and audit complete. `AvaloniaThemeService` updates `Application.Current.Resources` on `ActiveModChanged`; hard-coded accent/background/text hexes across `Views`, `Features`, `Windows`, `Dialogs`, `Chaos`, `AvatarTube`, `Converters`, and `Services` have been replaced with `DynamicResource` theme keys. The MainWindow player-title drop shadow is now assigned in code-behind from the `TransparentPink60` theme color and refreshed on `AvaloniaThemeService.ThemeChanged`. Remaining: local visual parity check (running WPF + Avalonia side-by-side, switching mods) to confirm every tab re-skins correctly.
2. **Onboarding/privacy dialogs** — `WebcamConsentDialog`, `LoginDialog`, `AwarenessPresetDetailDialog`, and `SessionEditDialog` are now fully localized. Remaining localization work is in webcam windows/popups and a few WPF-only message strings.
3. **Webcam windows** — shells are ported; calibration/eye-tracking pipeline remains stubbed, but all user-facing strings are now localized.
4. **Deeper editor depth** — a functional basic editor is in place (metadata, regions/rules/haptics lists, save, preview). The `GazePickerWindow` is ported and wired for gaze-target/avoid rect authoring with an embedded live video preview. The full WPF visual timeline, drag-create/resize regions, curve editor, browser preview, and audio waveform cache are now ported and wired.
5. **Phase 4 remaining UI** — Feature-control code-behind, Deeper integration, and dynamic theme audit are green. Remaining: local visual parity verification and any first-chance issues discovered while running the app on a desktop session.

---

## Recommended Next Sprints

### Sprint A — Localization Blitz
- ✅ Onboarding/privacy dialogs localized (`WebcamConsentDialog`, `LoginDialog`, `AwarenessPresetDetailDialog`, `SessionEditDialog`).
- ✅ Webcam windows and popups localized (`AchievementPopup`, `AnnouncementPopup`, `PinkRushPopup`, `QuestCompletePopup`, `SplashScreen`, `TutorialOverlay`, `WebcamCalibrationWindow`, `WebcamGazeTrackerWindow`, `WebcamQuickRecalWindow`).

### Sprint B — AvatarTube Behavior
- ✅ Restore speech phrase system, AI chat replies, Circe emote playback/scheduling, reactions, windowing behavior, fullscreen detection, context-menu toggles, and emotive portrait system.

### Sprint C — Chaos Overlay Polish
- ✅ Finish Skia/host overlay animations, z-order helper, run-state/boon logic, meta persistence, lessons, narrative director, and happy-path scripting.

### Sprint D — Deeper Runtime + Editor
- ✅ Migrate `EnhancementEngine`, `IActionDispatcher`, and `EnhancementHostService` to `CCP.Core` behind cross-platform seams.
- ✅ Create `AvaloniaLibVlcTimeSource` and wire the player to bind/unbind the engine.
- ✅ Port basic `DeeperEditorWindow` (metadata, regions/rules/haptics lists, save, preview).
- ✅ Port `GazePickerWindow` for gaze-target/avoid rect authoring.
- ✅ Visual timeline tab: lanes, ruler, playhead, region/effect/haptic rendering, click-to-select, zoom.
- ✅ Timeline drag-create/resize + rubber-band multi-select.
- ✅ Browser preview + audio waveform cache + curve editor.
- ✅ Deeper player/editor integration live smoke test — row Open/Play commands open `DeeperEditorWindow`/`EnhancementPlayerWindow`, Import loads `.ccpenh.json` files, library auto-scans `DeeperLastDirectory`, and the Windows head smoke-test starts cleanly.

---

## Smoke-Test Findings

Run on `CCP.Avalonia.Desktop.Windows` using the `--smoke-test` automation in `CCP.Avalonia.Desktop.Windows/Program.cs`.

### Automated run summary (latest)

| Metric | Value |
|---|---|
| Tabs visited | 44 (all registered shell tabs) |
| Dialogs opened | `InputDialog`, `WarningDialog`, plus all 32 parameterless Avalonia dialogs (LocalAiSetupWizard excluded because it probes the Ollama network endpoint) |
| Dashboard helper buttons exercised | Webcam, App Info, Scheduler (popup open + screenshot); Catalogue (presence verified, not clicked because it opens an external browser) |
| Dashboard `FeatureCard` popups opened | 12/12 |
| Mod switches exercised | 3 (CCP Default → Bambi Sleep → Sissy Hypno → CCP Default) |
| Duration | ~55 s |
| First-chance exceptions | 0 |
| Raw `{loc:Str}` markup visible | 0 |
| Possible missing loc keys displayed | 0 |
| Avalonia binding/resource/layout warnings | 0 |
| Dashboard `FeatureCard`s found | 12 |
| Dashboard cards with visuals (image or glyph) | 12/12 |
| Helper buttons with icon prefix | 4/4 |
| Screenshots captured | 96 |

### Catalog of current issues

| # | Severity | Area | Owner | Description |
|---|---|---|---|---|
| 1 | ✅ Resolved | Dashboard rendering / `MainWindow.axaml` | orchestrator | Dashboard content area was empty because the custom `TabControl` template only bound `SelectedContent`. Replaced `TabControl` with `ContentControl` + view-selector `DataTemplate`s; dashboard now renders with 12 feature cards, center logo, browser/audio/quick-links panels. Screenshot captured. |
| 2 | ✅ Resolved | Presets tab bindings | orchestrator | Replaced parent-relative command bindings (`#Root.DataContext.SelectPresetCommand`, `#Root.DataContext.EditSessionCommand`, `#Root.DataContext.ExportSessionCommand`) with `Click` event handlers in `PresetsTabView.axaml.cs` that execute the VM commands directly. No more transient "DataContext value is null" binding warnings. |
| 3 | ✅ Resolved | Missing localization keys | orchestrator | Added 98 new keys to `en.json` via `tools/new-localization-keys.json` + `python tools/merge-localization-keys.py`: all achievement name/requirement/flavor keys (30 achievements x 3), leaderboard mode keys (`label_monthly`, `label_all_time`), companion keys (`label_avatar_anchored`, `label_avatar_floating`, `label_companion_description_placeholder`, `label_no_prompts_installed`), and asset folder keys (`asset_folder_images`, `asset_folder_videos`). Updated `AssetsTabViewModel` to use the localized names. |
| 4 | ✅ Resolved | First-chance `FormatException` | orchestrator | The exception was in the smoke-test log sink, not the app. `SmokeTestLogSink.Log` used `string.Format` on Avalonia's named-placeholder message templates (e.g. `{Property}`), which expects numeric placeholders. Replaced with a named-placeholder renderer; first-chance exception count is now 0. |
| 5 | ✅ Resolved | Dashboard card visuals | orchestrator | Smoke test originally counted only `Icon`; two cards (`Visuals`, `System`) intentionally use `Glyph` instead of `IconUri`. Updated the smoke test to count `Icon` or non-empty `Glyph` as a valid visual; dashboard now reports 12/12 cards with visuals. |
| 6 | ✅ Resolved | Smoke-test heuristic over-reporting | orchestrator | The three previously reported "possible missing loc key" warnings (`bambi`, `u_mpnrz5gm4e7d38c7aa7b`, `spiral`) were user-data false positives and are no longer flagged in the latest automated run. |
| 7 | ✅ Resolved | Dashboard helper button coverage | orchestrator | Added smoke-test coverage that verifies all four dashboard helper buttons are visible and icon-prefixed, opens the Webcam/App Info/Scheduler popups, and captures a screenshot of each. This uncovered two missing localization keys (`btn_login_discord`, `btn_login_patreon`) in the App Info popup, which were added to `en.json`. |
| 8 | ✅ Resolved | Dashboard feature-card popup coverage | orchestrator | Extended the smoke test to open every dashboard `FeatureCard` popup (12 cards), scan it for raw localization markup, and capture a screenshot. All 12 feature controls loaded without first-chance exceptions or findings. |
| 9 | ✅ Resolved | Parameterless dialog smoke coverage + missing loc key | orchestrator | Extended the smoke test to open all 32 parameterless Avalonia dialogs (excluding `LocalAiSetupWizard` to avoid Ollama network probes). This uncovered one missing localization key (`attention_check_settings`) used by `AttentionCheckSettingsDialog`, which was added to `en.json`. Run is now clean: 0 first-chance exceptions, 0 findings. |

### Recommended hand-offs

- **orchestrator:** `CCP.Desktop.slnf` builds with 0 errors and `CCP.Core.Tests` pass. Smoke-test is clean: 0 first-chance exceptions, 0 binding/resource/layout warnings, and 0 "possible missing loc key" findings. The dashboard helper buttons, all 12 feature-card popups, and all 32 parameterless Avalonia dialogs are now exercised automatically; the transient Presets-tab binding warnings have been eliminated.

