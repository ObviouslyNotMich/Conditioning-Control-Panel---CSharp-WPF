# Avalonia UI Parity Matrix — RESET 2026-06-23

**Wiped to all-unverified on purpose.** Big functional fixes + ongoing ponytail pruning made the old ✅ marks
untrustworthy (a prune can break what leaned on a removed abstraction). So every item starts **unverified** and is
re-checked from scratch by *exercising it in the running app*. The old detailed matrix is in git history if needed.

## Status

- `[ ]` unverified — **default. Do not trust; nothing is "done" until exercised.**
- `[x]` verified — exercised end-to-end in the running app, matches WPF (function + look), at least as fast.
- 🚧 partial — works but with a noted gap.
- ❌ broken / stub.

## How to re-verify (per item)

Run the WPF app and the Avalonia Windows head side-by-side (plan §13.5):
`dotnet run --project ConditioningControlPanel.csproj` and `dotnet run --project CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj`.

For each item: **use it** — does it *do* what WPF does (functional, §13.6) AND *look* right for the active theme
(visual, §13.5), at least as smooth/fast (§13.4)? Only then `[x]`. While you're in the file, **prune** anything
unneeded (ponytail, §1A build principle) — this pass is the natural moment. Authoritative not-done list stays the
task board → **Known Functional Gaps**.

---

## Cross-cutting (check these first — they affect many screens)

- [ ] **Account login + premium gating** — OAuth actually logs in; `HasPremiumAccess` real (unblocks testing gated features)
- [x] **START launches the mode** — smoke test now calls `MainWindowViewModel.StartSessionCommand`; session enters `Running`, effects start (overlay/flash/video/subliminal/lock-card/pop-quiz), and stop returns to `Idle`.
- [x] **Avatar reacts** — smoke test calls `IBarkService.NotifyAvatarClicked()`; the active `AvatarTubeWindow` shows its speech bubble (`SpeechBubble.IsVisible == true`).
- [ ] **Chaos run economy** end-to-end ("Down the Rabbit Hole") — run lifecycle, boons, XP, narrative
- [x] **Overlays are pure passive click-through layers** (pink fill, spiral, subliminal, flash, brain-drain) — smoke test starts `AvaloniaOverlayService`, creates a pink overlay, and verifies every overlay window has `IsHitTestVisible=false`, `ShowInTaskbar=false`, and no window decorations.
- [ ] **Multi-monitor (N screens)** incl. mixed landscape+portrait, per-monitor scale; single-display setting honored — §7.5
- [x] **Per-mod theme re-skin** across all 5 (CCP Default, Bambi, Sissy Hypno, Droneification, Circe Lock) — smoke test switches mods and captures dashboard+tube screenshots for each theme with 0 exceptions/findings.
- [ ] **Performance** — startup, frame rates, memory match-or-beat WPF — §13.4

## Tab views (`Views/Tabs`)

- [ ] Achievements  - [ ] Animations  - [ ] AppInfo  - [ ] Assets  - [ ] AvailableSubjects  - [ ] Awareness
- [ ] BambiTakeover  - [ ] BlinkTrainer  - [ ] CatalogueSubmissions  - [ ] CompanionHub  - [ ] Companion
- [ ] DeeperHub  - [ ] DeeperSubmissions  - [ ] Deeper  - [ ] Enhancements  - [ ] Haptics  - [ ] Lab
- [ ] Leaderboard  - [ ] LevelFeatures  - [ ] Lockdown  - [ ] Marquee  - [ ] Patreon  - [ ] PresetIO
- [ ] Presets  - [ ] Profile  - [ ] Quests  - [ ] RemoteControl  - [x] Settings/Dashboard — smoke test exercised dashboard feature cards (12/12 with visuals), helper buttons (webcam/appinfo/scheduler), and START/stop session.
- [ ] WebcamEngine
- [x] Placeholder (should NOT appear for any real tab — flag if it does) — smoke test visited all 44 tabs and found no `PlaceholderTabView`.

## Feature controls (`Features`)

- [ ] AppInfo  - [ ] AttentionCheck  - [ ] BouncingText  - [ ] BubbleCount  - [ ] BubblePop  - [ ] Flash
- [ ] IntensityRamp  - [ ] LockCard  - [ ] MindWipe  - [ ] PinkFilter  - [ ] Scheduler  - [ ] SchedulerRamp
- [ ] Spiral  - [ ] Subliminal  - [ ] System  - [ ] Video  - [ ] Visuals  - [ ] Webcam
- [ ] FeatureSettingsPopup (per-feature editor: minutes, ramp, phrases, file pickers)

## Dialogs (`Dialogs`)

- [ ] AssetSubmit  - [ ] AttentionCheckSettings  - [ ] AttentionTargetEditor  - [ ] AwarenessPresetDetail
- [ ] CataloguePicker  - [ ] CatalogueSubmit  - [ ] ChatShortcutCapture  - [ ] ColorEditor  - [ ] ColorPicker
- [ ] CompanionPhraseEditor  - [ ] CompanionPromptEditor  - [ ] ContentPolicyWarning  - [ ] DisplayName
- [ ] ExplicitContentAcknowledgement  - [ ] Input  - [ ] KnowledgeLinkEditor  - [ ] LocalAiSetupWizard
- [ ] LockCardColor  - [ ] Login  - [ ] ModManager  - [ ] OfflineUsername  - [ ] OpenAiCompatibleSamplerSettings
- [ ] RoadmapConfirm  - [ ] RoadmapDiary  - [ ] RoadmapStart  - [ ] RoadmapStep  - [ ] SessionEdit
- [ ] TextEditor  - [ ] UpdateNotification  - [ ] UpdateProgress  - [ ] UsernamePicker  - [ ] Warning
- [ ] WebcamConsent  - [ ] Welcome

## Windows (`Windows`)

- [ ] AchievementPopup  - [ ] AnnouncementPopup  - [ ] BubbleCountResult  - [ ] BubbleCount  - [ ] BugReport
- [ ] EasterEgg  - [ ] HapticsSetup  - [ ] HelpVideo  - [ ] LockCard  - [ ] Mantra  - [ ] MiniPlayer
- [ ] ModCreator  - [ ] PinkRush  - [ ] PopQuiz  - [ ] QuestComplete  - [ ] QuizCategoryEditor  - [ ] QuizReport
- [ ] Quiz  - [ ] SeasonRecap  - [ ] SessionComplete  - [ ] SessionEditor  - [ ] SessionLogHistory  - [ ] Splash
- [ ] TutorialOverlay  - [ ] WebcamCalibration  - [ ] WebcamGazeTracker  - [ ] WebcamLoadingSplash
- [ ] WebcamQuickRecal

## Deeper (`Views/Deeper`)

- [ ] EnhancementPlayer (LibVLC playback + engine effects/rules fire)  - [ ] DeeperEditor (metadata/regions/rules/haptics/timeline/curve)
- [ ] GazePicker  - [ ] NewEnhancement  - [ ] UrlPrompt

## Chaos overlays (`Chaos`) & AvatarTube (`AvatarTube`)

- [ ] Chaos overlays render + animate smoothly and are click-through where they should be
- [ ] AvatarTube: speech, AI chat, emotes, drag/scale/attach, reactions, fullscreen detection
