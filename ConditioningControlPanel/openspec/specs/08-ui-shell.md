# Cluster 08 — UI Shell, Dialogs, ViewModels, Models, Localization, Helpers

Generated 2026-06-15. Presentation layer + plain data models + i18n.

## Quantification (app code only — excludes `obj/`, `bin/`, `.claude/`, `publish/`)

| Bucket | Files | ~LOC | Class |
|--------|------:|-----:|-------|
| XAML markup (all `*.xaml`) | 98 | ~30,400 | OS-SPECIFIC |
| XAML code-behind (all `*.xaml.cs`) | 93 | ~78,400 | OS-SPECIFIC |
| Non-XAML WPF partials (MainWindow.*.cs, AvatarTube.CirceEmotes.cs, Views/Deeper/*.cs, Controls/*.cs) | ~20 | ~25,000 | OS-SPECIFIC |
| Plain Models (POCO/DTO) | ~66 | ~17,800 | PORTABLE |
| Localization runtime (Loc.cs, LocalizationManager.cs) | 2 | ~180 | PORTABLE |
| Localization markup ext (LocExtension.cs) | 1 | 37 | OS-SPECIFIC |
| Language JSON (9 files) | 9 | ~22,700 (data) | PORTABLE (data) |
| Helpers | 8 | ~700 | 7 OS-SPECIFIC / 1 PORTABLE |
| ViewModels | 1 | 201 | MIXED |

**The OS-specific UI bulk dominates:** roughly **133,000+ LOC of WPF/XAML UI** (markup + code-behind + WPF partials + the one markup extension + 4 view-typed models) vs roughly **18,000 LOC of portable non-UI** (plain models + loc runtime). The 22.7k lines of language JSON are portable data, not code.

---

## Capability: Application UI Shell (MainWindow + Avatar)
**Files:** MainWindow.xaml (13,333 LOC), MainWindow.xaml.cs, MainWindow.DeeperHub.cs, MainWindow.DeeperSubmissions.cs, AvatarTubeWindow.xaml, AvatarTubeWindow.xaml.cs, AvatarTubeWindow.CirceEmotes.cs, App.xaml, SplashScreen.xaml/.cs
**Class:** OS-SPECIFIC
**Blocking deps:** WPF (`System.Windows.Window`, `FrameworkElement`, animations, data binding, custom styles), system-tray, screen-relative window positioning. MainWindow group alone is ~39,700 LOC of code-behind + 13,333 LOC XAML.

### Requirement: Tabbed control-panel shell
The system SHALL present a tabbed main window (Flashes, Videos, Overlays, Subliminals, Sessions, Progression, Settings, Deeper hub) and an attachable/detachable avatar companion window.

#### Scenario: Avatar attach
- WHEN the avatar window is attached
- THEN it positions itself relative to the main window and tracks its movement.

---

## Capability: Dialog & Popup Family (~60 root windows)
**Files:** ~60 top-level `*Dialog.xaml`, `*Window.xaml`, `*Popup.xaml`, `*Overlay.xaml` (e.g. LoginDialog, WelcomeDialog, InputDialog, WarningDialog, UpdateNotificationDialog, ModManagerDialog, ModCreatorWindow, SessionEditorWindow, QuizWindow, ChaosHubWindow/ChaosHudWindow/ChaosOverlayWindow, WebcamCalibrationWindow, etc.) plus Views/Deeper/* (DeeperEditorWindow + partials, EnhancementPlayerWindow, GazePickerWindow) and Controls/* (AttentionCheckControl, SeasonRecapCard/Window).
**Class:** OS-SPECIFIC
**Blocking deps:** WPF Window/UserControl, XAML resources, converters, animations. ~24,000 LOC root XAML + ~27,600 LOC non-MainWindow root code-behind; Views/Deeper adds ~2,700 XAML + ~11,000 code-behind; Controls ~1,500 LOC.

Categories: onboarding/consent (Welcome, ExplicitContentAcknowledgement, ContentPolicyWarning, WebcamConsent, OfflineUsername, DisplayName, UsernamePicker); auth (Login); editors (Session, Mod, Companion prompt/phrase, Quiz category, KnowledgeLink, AttentionTarget, Color, Text, Input, Roadmap); session/feedback (SessionComplete, SessionLogHistory, QuizReport, BubbleCountResult); notifications/popups (Achievement, Announcement, QuestComplete, PinkRush, RoadmapStep, FeatureSettings); chaos overlays; webcam windows; update (UpdateNotification, UpdateProgress).

### Requirement: Modal interaction surfaces
The system SHALL provide modal dialogs and transient popups for onboarding, editing, notification, and confirmation flows.

#### Scenario: Update available
- WHEN a newer version is detected
- THEN UpdateNotificationDialog is shown and on accept UpdateProgressDialog tracks the silent install.

---

## Capability: Feature Cards & Tile Controls
**Files:** Features/ — 20 `*FeatureControl.xaml`/.cs (Flash, Video, Subliminal, Spiral, BubblePop, BubbleCount, Webcam, MindWipe, PinkFilter, Scheduler, IntensityRamp, BouncingText, Visuals, System, AppInfo, AttentionCheck, LockCard) + FeatureCard + FeaturePopupWindow.
**Class:** OS-SPECIFIC
**Blocking deps:** WPF UserControl. ~2,000 LOC XAML + ~3,100 LOC code-behind. These are the per-feature settings tiles on the main panel; pure UI veneer over the Services cluster.

### Requirement: Per-feature configuration tiles
The system SHALL render one configurable card per feature, binding its controls to the corresponding service/setting.

---

## Capability: WPF Converters & UI Helpers
**Files:** Helpers/Converters.cs, EmojiImage.cs, EmojiTextBlock.cs, EmoteHelper.cs, FlashWindowHelper.cs, DispatcherHelper.cs, ShellThumbnailHelper.cs
**Class:** OS-SPECIFIC
**Blocking deps:** `System.Windows.Data.IValueConverter`, `System.Windows.Media`, `Dispatcher`, and Win32 Shell API (ShellThumbnailHelper uses `Runtime.InteropServices` P/Invoke + `System.Windows.Interop`). 7 of 8 helper files are WPF/Win32-bound.

### Requirement: Binding-time value conversion & UI utilities
The system SHALL provide value converters and UI utilities (emoji/emote rendering, dispatcher marshalling, Windows shell thumbnails) for the XAML layer.

---

## Capability: Pure Utility Helper (HtUrl)
**Files:** Helpers/HtUrlHelper.cs
**Class:** PORTABLE
**Blocking deps:** none — regex-based HypnoTube URL validation/parsing, no WPF. Must stay in lockstep with server `normalizeHtUrl`.

### Requirement: HypnoTube URL eligibility
The system SHALL validate and extract video IDs from HypnoTube URLs as a client-side pre-filter.

---

## Capability: Localization Runtime (lookup)
**Files:** Localization/Loc.cs, Localization/LocalizationManager.cs, Localization/Languages/*.json (9 langs, ~3,218 keys each in en.json)
**Class:** PORTABLE
**Blocking deps:** none — JSON dictionary load (Newtonsoft) + key lookup with English fallback; only ties to `INotifyPropertyChanged` (BCL) and Serilog. Loads from app dir or `%LOCALAPPDATA%`.

### Requirement: Key-based localized string lookup
The system SHALL load a language JSON dictionary by code and resolve keys, falling back to English then to the key itself.

#### Scenario: Missing key
- WHEN a key is absent in the active and fallback languages
- THEN the key string itself is returned (visible-untranslated behavior).

---

## Capability: Localization XAML Binding (markup extension)
**Files:** Localization/LocExtension.cs (StrExtension)
**Class:** OS-SPECIFIC
**Blocking deps:** `System.Windows.Markup.MarkupExtension`, `System.Windows.Data.Binding`. This is the only WPF-bound piece of the i18n system; it wraps the portable lookup for `{loc:Str key}` XAML usage and live language-change rebinding.

### Requirement: Localized XAML markup
The system SHALL expose `{loc:Str <key>}` markup that binds to the localization manager indexer and updates on language change.

---

## Capability: Plain Data Models (POCO / JSON DTO)
**Files:** Models/ — ~66 files (~17,800 LOC) excluding the 4 view-typed ones. Includes Achievement, Quest, Session(+Definition/Log), RoadmapDefinition/Progress, CompanionDefinition/Phrase/Progress, ModManifest/Package, PatreonModels, DiscordModels, ContentPack, SkillTree, SeasonRecap, TimelineEvent/Session, FeatureDefinition, PersonalityPreset(s), KeywordTrigger/Action, plus subfolders Models/AiEnrichment/, Models/CommandData/, Models/Deeper/.
**Class:** PORTABLE
**Blocking deps:** none — POCOs with `[JsonProperty]`, enums, and `INotifyPropertyChanged`. Serialization via Newtonsoft. (NOTE: AppSettings.cs is covered by another cluster.)

### Requirement: Serializable domain data
The system SHALL define JSON-serializable data models for all gamification, session, companion, mod, and catalogue entities.

---

## Capability: View-Typed Models (UI-leaning DTOs)
**Files:** Models/AssetFileItem.cs, Models/ContentPack.cs (`System.Windows.Media.Imaging.BitmapImage`), Models/TutorialStep.cs (`System.Windows`), Models/AppSettings.cs (other cluster)
**Class:** MIXED
**Blocking deps:** `System.Windows.Media.Imaging` (thumbnail BitmapImage), `System.Windows` (Rect/Thickness). 4 of ~70 model files leak WPF types.
**Seam:** replace the cached `BitmapImage`/geometry properties with a platform-agnostic image handle/rect; the rest of each model is portable.

### Requirement: Asset/content rows carry preview imagery
The system SHALL expose asset and content-pack rows with thumbnail and geometry properties for the UI list views.

---

## Capability: ViewModels (MVVM)
**Files:** ViewModels/SeasonRecapViewModel.cs (only one)
**Class:** MIXED
**Blocking deps:** `System.Windows.Visibility` (exposes `SupporterVisibility`/`OgVisibility`/`StatusRowVisibility` as WPF `Visibility`). Underlying recap logic is portable.
**Seam:** replace `Visibility`-typed presentation flags with `bool`s and convert at the view; then portable. Note: the project is overwhelmingly code-behind, not MVVM — there is essentially a single ViewModel.

### Requirement: Season-recap presentation state
The system SHALL expose computed recap display flags (supporter/OG/status visibility) for the recap card.

---

## Notes
- **TutorialService.cs / TutorialEventBus.cs** live in Services/ (another cluster); TutorialOverlay.xaml/.cs and TutorialStep model are in this cluster (OS-SPECIFIC / MIXED respectively).
- This cluster contains almost the entire OS-specific surface of the app. Re-platforming off Windows means rewriting ~133k LOC of WPF UI; the portable carry-over is the ~18k LOC of models + ~180 LOC loc runtime + ~22.7k lines of language data + HtUrlHelper.
