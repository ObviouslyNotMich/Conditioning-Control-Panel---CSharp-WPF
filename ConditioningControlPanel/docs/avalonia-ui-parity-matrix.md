# Avalonia UI Parity Matrix

Audit of the Avalonia UI versus the legacy WPF implementation.
Status key:
- ✅ Parity achieved / functionally complete
- 🚧 Partially ported, known gaps
- ❌ Not started / missing
- ⚠️ Blocked by platform seam, no direct counterpart, or external dependency

Last updated: 2026-06-19

---

## Executive Summary

- **Build health:** All desktop heads compile with 0 warnings / 0 errors. Windows head starts and loads settings without crashing in a 20-second smoke test.
- **Biggest remaining gaps:**
  1. **Secondary tab view richness** — ported as flat, text-only cards; most lost WPF hero images, tier cards, premium gates, and rich animations. **Localization is now complete.**
  2. **Dialogs & windows** — structure and commands are wired; **localization is now complete** for the audited surfaces (except a few symbol-only affordances). `FeatureSettingsPopup` remains a major functionality stub.
  3. **Feature controls** — XAML and settings binding are solid; live engine integration (video, flash, bubbles, subliminal, lock card, etc.) is mostly stubbed pending `App.*` service ports.
  4. **Chaos overlays** — shells ported but animation/z-order TODOs remain and nothing is localized.
  5. **AvatarTube** — window shell ported; speech/chat/reactions/emotes/windowing behavior is heavily reduced/stubbed.

---

## Tab Views (`CCP.Avalonia/Views/Tabs`)

### Primary / Rich-Card Tab Views

| Tab View | XAML | Code-Behind | Rich Cards/Images | Localization | Design-Time Data | Notes |
|----------|------|-------------|-------------------|--------------|------------------|-------|
| SettingsTabView (Dashboard) | ✅ | ✅ | ✅ | ✅ | ✅ | WebView2 browser host wired on Windows; audio sliders bound but not yet driving real `IAudioPlayer`. |
| LevelFeaturesTabView | ✅ | ✅ | ✅ | ✅ | ⚠️ | `FeatureCard` grid at top; detail card layout present. |
| QuestsTabView | ✅ | ✅ | ✅ | ✅ | ⚠️ | `QuestCard` + `RoadmapNodeCard` in place; roadmap interactions need smoke test. |
| EnhancementsTabView | ✅ | ✅ | ✅ | ✅ | ⚠️ | `SkillNodeCard` + skill images; connection lines present. |
| AssetsTabView | ✅ | ✅ | ✅ | ✅ | ⚠️ | `ContentPackCard` + tree browser; drag-drop and context menus need verification. |
| PresetsTabView | ✅ | ✅ | ✅ | ✅ | ⚠️ | Two-column layout with `PresetCard`; session editor and drag-drop need verification. |
| DeeperTabView | ✅ | ✅ | ✅ | ✅ | ⚠️ | Hub layout with filter pills; player/editor integration pending. |

### Secondary Tab Views

| Tab View | XAML Parity | Rich Cards/Images | Code-Behind/Commands | Localization | Design-Time Data | Notes |
|----------|-------------|-------------------|----------------------|--------------|------------------|-------|
| AchievementsTabView | 🚧 | ❌ | ✅ | ✅ | ✅ | Text-only tiles; no achievement images or blur-locked visuals. |
| ProfileTabView | 🚧 | ❌ | ✅ | ✅ | ⚠️ | Minimal identity/logout card only; no avatar, stats, banner, badges, or gallery. |
| HapticsTabView | 🚧 | ✅ | ✅ | ✅ | ⚠️ | Hero card, connection card, algorithm cards, premium gate added. Still missing some WPF polish (tooltip guide, per-feature two-column layout). |
| AppInfoTabView | 🚧 | ❌ | ✅ | ✅ | ⚠️ | Standalone account/language/backup tab; drops version header, update-check, bug-report buttons. |
| BlinkTrainerTabView | 🚧 | ❌ | ✅ | ✅ | ⚠️ | Missing hero banner, animated eye, stage frame, asset-pack cards, duration/opacity/mix-mode UI, consent card, premium gate. |
| PatreonTabView | 🚧 | 🚧 | ✅ | ✅ | ⚠️ | Moved keyword triggers out, dropped support-development card; localization complete. |
| DeeperHubTabView | 🚧 | ❌ | 🚧 | ✅ | ✅ | Flat cards; no hero images; localization complete. |
| DeeperSubmissionsTabView | ⚠️ | ❌ | 🚧 | ✅ | ✅ | No standalone WPF XAML; logic lived in code-behind; localization complete. |
| CompanionHubTabView | 🚧 | ❌ | 🚧 | ✅ | ✅ | Only avatar/hero slice of full Companion view; localization complete. |
| CompanionTabView | 🚧 | ❌ | 🚧 | ✅ | ✅ | Flat cards; missing OG glow, hover actions, accordion sections; localization complete. |
| PresetIOTabView | 🚧 | ❌ | 🚧 | ✅ | ✅ | Reduced import/export slice; localization complete. |
| LeaderboardTabView | 🚧 | ❌ | 🚧 | ✅ | ✅ | Flat list; missing gradients, badges; localization complete. |
| LockdownTabView | 🚧 | 🚧 | ✅ | ✅ | ✅ | Card + gate layout but missing lockdown icon, pulse border, emoji-to-image header; localization complete. |
| RemoteControlTabView | 🚧 | ❌ | ✅ | ✅ | ✅ | Missing hero banner, tier cards, QR code, opt-in tags, emote editor, privacy toggles, premium gate; localization complete. |
| AvailableSubjectsTabView | 🚧 | 🚧 | ✅ | ✅ | ✅ | Horizontal card list okay; header emoji is text glyph; all text comes from ViewModel bindings (localization complete). |
| LabTabView | 🚧 | 🚧 | ✅ | ✅ | ✅ | Much simpler stack; missing hero banner, how-to-play, MIND/EYES zones, webcam bar, AI panel, wallpaper card, smokescreen; localization complete. |
| AwarenessTabView | 🚧 | 🚧 | ✅ | ✅ | ✅ | Settings cards present; missing hero banner, live pulse feed, preset cards, safety section, color swatches, advanced link, premium gate; localization complete. |
| BambiTakeoverTabView | 🚧 | 🚧 | ✅ | ✅ | ✅ | Missing description image, guide sidebar, start/stop button, gated overlay; localization complete. |
| MarqueeTabView | ⚠️ | ❌ | ✅ | ✅ | ✅ | No legacy tab XAML; only debug panel in Avalonia; localization complete. |
| AnimationsTabView | ⚠️ | ❌ | ✅ | ✅ | ✅ | No legacy tab XAML; only debug buttons in Avalonia; localization complete. |
| CatalogueSubmissionsTabView | ⚠️ | ❌ | ✅ | ✅ | ✅ | No legacy tab XAML; only simple status list in Avalonia; localization complete. |
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
| VideoFeatureControl | ✅ | ✅ | 🚧 | ⚠️ (`App.Video`) | ✅ | 🚧 |
| FlashFeatureControl | ✅ | ✅ | 🚧 | ⚠️ (`App.Flash`) | ✅ | 🚧 |
| BubbleCountFeatureControl | ✅ | ✅ | 🚧 | ⚠️ (`App.BubbleCount`) | ✅ | 🚧 |
| MindWipeFeatureControl | ✅ | ✅ | 🚧 | 🚧 (audio picker OK, engine N/A) | ✅ | 🚧 |
| SubliminalFeatureControl | ✅ | ✅ | 🚧 | ⚠️ (`App.Subliminal`) | ✅ | 🚧 |
| BouncingTextFeatureControl | ✅ | ✅ | 🚧 | ⚠️ (`App.BouncingText`) | ✅ | 🚧 |
| PinkFilterFeatureControl | ✅ | ✅ | 🚧 | ⚠️ (overlay service) | ✅ | 🚧 |
| BubblePopFeatureControl | ✅ | ✅ | 🚧 | ⚠️ (`App.Bubbles`) | ✅ | 🚧 |
| SpiralFeatureControl | ✅ | ✅ | 🚧 | 🚧 (file picker OK, overlay blocked) | ✅ | 🚧 |
| SystemFeatureControl | ✅ | ✅ | 🚧 | 🚧 (capabilities + dialogs OK, startup/panic blocked) | ✅ | 🚧 |
| WebcamFeatureControl | ✅ | ✅ | 🚧 | ⚠️ (`SupportsScreenCapture`/engine) | ✅ | 🚧 |
| LockCardFeatureControl | ✅ | ✅ | 🚧 | 🚧 (capabilities OK, lockdown service blocked) | ✅ | 🚧 |
| AppInfoFeatureControl | ✅ | ✅ | ⚠️ | ⚠️ | ⚠️ | 🚧 |

---

## Dialogs (`CCP.Avalonia/Dialogs`)

| Dialog | WPF Original | Structure Parity | Localization | Events/Commands | Notes |
|--------|--------------|------------------|--------------|-----------------|-------|
| UpdateNotificationDialog | ✅ | ✅ | ✅ | ✅ | — |
| SessionEditDialog | ❌ | 🚧 | ✅ | ✅ | No WPF original; all labels/buttons hard-coded English. |
| WebcamConsentDialog | ✅ | ✅ | ✅ | ✅ | Privacy-critical; all step text and consent check hard-coded. |
| OpenAiCompatibleSamplerSettingsDialog | ✅ | ✅ | ✅ | ✅ | — |
| ExplicitContentAcknowledgementDialog | ✅ | ✅ | ✅ | ✅ | — |
| CompanionPromptEditorDialog | ✅ | ✅ | ✅ | ✅ | Section description paragraphs hard-coded. |
| ContentPolicyWarningDialog | ✅ | ✅ | ✅ | ✅ | — |
| CompanionPhraseEditorDialog | ✅ | 🚧 | ✅ | ✅ | Plain `ItemsControl`; "No Audio", "Browse", "On" hard-coded. |
| LockCardColorDialog | ✅ | ✅ | ✅ | ✅ | — |
| ChatShortcutCaptureDialog | ✅ | ✅ | ✅ | ✅ | — |
| InputDialog | ✅ | ✅ | ✅ | ✅ | "OK" button hard-coded. |
| UpdateProgressDialog | ✅ | ✅ | 🚧 | ✅ | "0%" hard-coded initial text. |
| RoadmapDiaryDialog | ✅ | ✅ | ✅ | ✅ | — |
| RoadmapStepPopup | ✅ | 🚧 | ✅ | ✅ | Emoji-to-image missing; "STEP COMPLETE!", placeholders hard-coded. |
| RoadmapConfirmDialog | ✅ | 🚧 | ✅ | ✅ | Emoji icon hard-coded. |
| RoadmapStartDialog | ✅ | 🚧 | 🚧 | ✅ | Same emoji-icon gap. |
| WelcomeDialog | ✅ | 🚧 | ✅ | ✅ | Emoji icon hard-coded. |
| AssetSubmitDialog | ✅ | ✅ | 🚧 | ✅ | 📤 icon hard-coded. |
| CatalogueSubmitDialog | ✅ | ✅ | ✅ | ✅ | "Submitting..." spinner text hard-coded. |
| CataloguePickerDialog | ✅ | ✅ | 🚧 | ✅ | 📥/▶ icons hard-coded. |
| ModManagerDialog | ✅ | 🚧 | ✅ | ✅ | "No mods available" and "X" hard-coded. |
| KnowledgeLinkEditorDialog | ✅ | ✅ | ✅ | ✅ | — |
| AwarenessPresetDetailDialog | ✅ | 🚧 | ✅ | ✅ | Large blocks of policy, trigger, footer text hard-coded; code-behind builds rows with English labels. |
| LoginDialog | ✅ | 🚧 | ✅ | ✅ | Device-code panel entirely hard-coded; uses `TextBox.PasswordChar` instead of `PasswordBox`. |
| UsernamePickerDialog | ✅ | ✅ | ✅ | ✅ | — |
| DisplayNameDialog | ✅ | ✅ | ✅ | ✅ | — |
| AttentionCheckSettingsDialog | ✅ | ✅ | ✅ | ✅ | "Test now" button + tooltip hard-coded. |
| WarningDialog | ✅ | 🚧 | ✅ | ✅ | ToggleSwitch instead of CheckBox; default title/message hard-coded. |
| OfflineUsernameDialog | ❌ | 🚧 | ✅ | ✅ | No WPF original; "0 / 30" char count hard-coded. |
| AttentionTargetEditorDialog | ✅ | 🚧 | ✅ | ✅ | ToggleSwitch instead of CheckBox. |
| ColorEditorDialog | ✅ | 🚧 | ✅ | ✅ | ToggleSwitch instead of CheckBox. |
| TextEditorDialog | ✅ | 🚧 | ✅ | ✅ | Title "Text Manager" hard-coded. |

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
| ChaosCursorGlowOverlay.cs | ChaosCursorGlowOverlay.axaml.cs | ✅ | Scale pulse animation TODO. |
| ChaosDvdOverlay.cs | ChaosDvdOverlay.axaml.cs | ✅ | Bouncing text overlay. |
| ChaosEffectBannerOverlay.cs | ChaosEffectBannerOverlay.axaml.cs | ✅ | Throb animation TODO. |
| ChaosEStimOverlay.cs | ChaosEStimOverlay.axaml.cs | ✅ | Cursor-centered glow. |
| ChaosFieldFxOverlay.cs | ChaosFieldFxOverlay.axaml.cs | ✅ | Radial shards / scale-shrink TODO. |
| ChaosFlashOverlay.cs | ChaosFlashOverlay.axaml.cs | ✅ | Full-screen flash. |
| ChaosFxWindow.cs | ChaosFxWindow.cs | ✅ | Effect host window. |
| ChaosGifCascadeOverlay.cs | ChaosGifCascadeOverlay.axaml.cs | ✅ | GIF-rain overlay. |
| ChaosHubWindow.* | ChaosHubWindow.axaml + .axaml.cs + .Partial.cs | ⚠️ | Lessons/Reveals/Bench/Debug collapsed; incomplete vs WPF. |
| ChaosHudWindow.* | ChaosHudWindow.axaml + .axaml.cs + VM | ✅ | DropShadow TODOs. |
| ChaosIntroWindow.cs | ChaosIntroWindow.cs | ✅ | — |
| ChaosOverlayWindow.* | ChaosOverlayWindow.axaml + .axaml.cs | ✅ | Countdown/draft/results/story cards. |
| ChaosPopText.cs | ChaosPopText.axaml.cs | ✅ | — |
| ChaosToyButtonWindow.cs | ChaosToyButtonWindow.cs | ✅ | — |
| ChaosUnlockCardOverlay.cs | ChaosUnlockCardOverlay.axaml.cs + helper | ✅ | — |
| ChaosVibeTrailOverlay.cs | ChaosVibeTrailOverlay.axaml.cs | ✅ | — |
| ChaosWaveTimerOverlay.cs | ChaosWaveTimerOverlay.axaml.cs | ✅ | — |
| ChaosWindowZ.cs | AvaloniaChaosWindowZ.cs | ⚠️ | Z-order helper mostly TODO. |

**Localization:** ❌ None of the Chaos UI is localized (WPF Chaos was also unlocalized, so this is parity, not a regression).

---

## AvatarTube (`CCP.Avalonia/AvatarTube`)

| WPF File | Avalonia Equivalent | Status | Notes |
|----------|---------------------|--------|-------|
| AvatarTubeWindow.xaml | AvatarTubeWindow.axaml | ✅ | Layout and tube visual ported. |
| AvatarTubeWindow.xaml.cs | AvatarTubeWindow.axaml.cs | ⚠️ | Reduced; many menu hooks stubbed. |
| AvatarTubeWindow.Avatar.cs | AvatarTubeWindow.Avatar.cs | ⚠️ | Portrait/Circe emote modes stubbed. |
| AvatarTubeWindow.ChatInput.cs | AvatarTubeWindow.ChatInput.cs | ⚠️ | AI reply is placeholder. |
| AvatarTubeWindow.CirceEmotes.cs | AvatarTubeWindow.CirceEmotes.cs | ⚠️ | All emote playback TODO. |
| AvatarTubeWindow.Reactions.cs | AvatarTubeWindow.Reactions.cs | ⚠️ | Activity hooks mostly no-op. |
| AvatarTubeWindow.Speech.cs | AvatarTubeWindow.Speech.cs | ⚠️ | Phrase system replaced with random Bambi phrases. |
| AvatarTubeWindow.Windowing.cs | AvatarTubeWindow.Windowing.cs | ⚠️ | Fullscreen/floating/z-order stubs. |
| AvatarRandomBubble.cs | AvatarRandomBubble.cs | ⚠️ | Scaling TODO. |

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
| AvatarTube | 🚧 | Shell localized; code-driven strings not |
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

1. **FeatureSettingsPopup** — stub; missing the entire feature-settings editor (feature icon/name, event type, minute slider, dynamic settings panel, Done).
2. **Secondary tab views** — rich images, premium gates, hero banners, and animations missing; localization is now complete.
3. **Onboarding/privacy dialogs** — `WebcamConsentDialog`, `LoginDialog`, `AwarenessPresetDetailDialog`, `SessionEditDialog` are mostly hard-coded English.
4. **Webcam windows** — all hard-coded English; calibration pipeline stubbed.
5. **Feature control engine wiring** — video, flash, bubbles, subliminal, bouncing text, lock card, etc. are XAML-ready but not connected to live services.
6. **AvatarTube depth** — shell works; speech/chat/reactions/emotes/windowing behavior is mostly placeholder.
7. **MainWindow chrome** — custom window chrome and resize grips are unfinished.
8. **Cross-platform drag-drop** — not verified for session/preset import.

---

## Recommended Next Sprints

### Sprint A — Localization Blitz (highest ROI)
- Localize all secondary tab views.
- Localize `FeatureSettingsPopup`, `WebcamConsentDialog`, `LoginDialog`, `AwarenessPresetDetailDialog`, `RoadmapStepPopup`, `SessionEditDialog`.
- Localize all webcam windows and popups (`AchievementPopup`, `AnnouncementPopup`, `PinkRushPopup`, `PopQuizWindow`, `QuestCompletePopup`, `SplashScreen`, `TutorialOverlay`).

### Sprint B — FeatureSettingsPopup
- Rebuild the popup to match WPF: feature icon/name header, event type selector, minute slider, dynamic settings panel, Done/Cancel/Delete buttons.

### Sprint C — Secondary Tab View Richness
- Add hero images, tier cards, premium gates, and lock overlays to secondary tabs using `FeatureCard`/`ContentPackCard` patterns.

### Sprint D — Feature Control Engine Wiring
- Connect `VideoFeatureControl`, `FlashFeatureControl`, `BubbleCountFeatureControl`, `SubliminalFeatureControl`, `BouncingTextFeatureControl`, `PinkFilterFeatureControl`, `BubblePopFeatureControl`, `LockCardFeatureControl` to their Core engine services via ViewModels.

### Sprint E — AvatarTube Behavior
- Restore speech phrase system, AI chat replies, Circe emote playback/scheduling, reactions, and windowing behavior.
