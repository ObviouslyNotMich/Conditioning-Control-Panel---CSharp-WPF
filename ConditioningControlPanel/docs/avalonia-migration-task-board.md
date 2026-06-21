# Avalonia Migration Task Board

Master task list derived from `docs/crossplatform-rebuild-plan.md` and the detailed breakdowns produced by planning sub-agents.

> **Swarm execution:** this board is the live work queue for a multi-agent swarm. **Read
> `docs/crossplatform-rebuild-plan.md` §20 (Multi-Agent Swarm Execution & Context Discipline) before
> claiming or assigning anything.** Claim work via the **Active Claims Ledger** below — do **not** hand-edit
> the same file as another agent, and never touch a §20.1 chokepoint from a porter lane (route those changes
> through the Hand-off Queue to the orchestrator).

## Legend

- **Priority:** `P0` blocker · `P1` critical path · `P2` parallelizable · `P3` polish/final parity.
- **Status markers** (used in the ledger and, optionally, inline on a phase item):
  - `⬜ todo` — unclaimed, available to pick up.
  - `🚧 wip @agentN` — claimed / in progress by `agentN`.
  - `🔵 review` — complete in a worktree, awaiting orchestrator integration.
  - `✅ done` — merged and the integration build is green.
  - `🚫 blocked` — see the ledger Notes column.

---

## Swarm Coordination

> Single source of truth for *who is doing what right now*. Mirrors plan §20.4. Prefer **appending rows**
> over editing scattered list items — appends rarely conflict; in-place edits across the board do.

### Active Claims Ledger

Append a row to **claim**; the orchestrator marks `✅ done` and removes the row after a clean integration build.

| Lane (owned subtree) | Item | Owner | Status | Branch / worktree | Updated |
|---|---|---|---|---|---|
| `CCP.Core/Models/` + Avalonia/tests | Align model namespace to `ConditioningControlPanel.Models`; port `AppSettings` drift from main | orchestrator | `✅ done` | `main` | 2026-06-21 |
| `CCP.Core/App.cs` + Core `App.X` call sites | Remove/rename Core `App` stub so WPF can reference Core and delete model copies | — | `🚫 blocked` | see plan §19.4 | 2026-06-21 |
| `Services/Chaos/ChaosCrashSentinel.cs` → Core + Avalonia startup | Crash-instrumentation sentinel | orchestrator | `✅ done` | `main` | 2026-06-21 |
| `Chaos/ChaosBubbleHostOverlay.cs` + `ChaosDvdHostOverlay.cs` + `ChaosBoonColors.cs` → `CCP.Avalonia/Chaos` | Shared-host perf overlays + color map | orchestrator | `✅ done` | `main` | 2026-06-21 |
| `Chaos/ChaosSkiaFxOverlay.cs` → `CCP.Avalonia/Chaos` | Skia GPU-style FX (pop bursts, ripples, rim-shine, multiplier HUD) | orchestrator | `✅ done` | `main` | 2026-06-21 |
| `Services/BubbleService.cs` → `CCP.Avalonia` | Port bubble-popping minigame overhaul — Stages 1, 2a, 2b & 2c done | orchestrator | `✅ done` | `main` | 2026-06-21 |
| `Services/Update/UpdateService.cs` → Core + heads | Rework update service against `IUpdateInstaller` seam | orchestrator | `✅ done` | `main` | 2026-06-21 |
| `Services/*.cs` deltas | Verify `ModService`/`FlashService`/`GlobalMouseHook` changes | orchestrator | `✅ done` | `main` | 2026-06-21 |
| `Fonts/Fredoka.ttf` → `CCP.Avalonia` | Register Fredoka as AvaloniaResource and wire usages | orchestrator | `✅ done` | `main` | 2026-06-21 |
| `Views/Tabs/LockdownTabView` | Visual parity: header/hero/premium gate images, pulsing active border, VM localization | orchestrator | `✅ done` | `main` | 2026-06-19 |
| `Views/Tabs/BambiTakeoverTabView` | Visual parity: description image, guide sidebar, Start/Stop controls, premium gate, VM localization | orchestrator | `✅ done` | `main` | 2026-06-19 |
| `Views/Tabs/AwarenessTabView` | Visual parity: hero banner, header/pulse/presets, signal sources + safety + color swatches, advanced link, premium gate, VM wiring | orchestrator | `✅ done` | `main` | 2026-06-19 |
| `Views/Tabs/RemoteControlTabView` | Real QR code generation for pairing URL (QRCoder) | orchestrator | `✅ done` | `main` | 2026-06-19 |
| `Views/Tabs/ProfileTabView` | Visual parity: banner gradient, avatar initials, stats row, badges, gallery placeholder, VM stats/badges | orchestrator | `✅ done` | `main` | 2026-06-19 |
| `Views/Tabs/DeeperHubTabView` | Visual parity: hero banner, media-type glyph/brush converters, richer row cards, filter/sort panel, empty state | orchestrator | `✅ done` | `main` | 2026-06-19 |
| `Views/Tabs/CompanionHubTabView` | Visual parity: hero banner, status card, action buttons, pose/audio cards, settings link | orchestrator | `✅ done` | `main` | 2026-06-19 |
| `Views/Tabs/CompanionTabView` | Visual parity: hero banner, active-companion card, settings/prompt panels, companion roster, installed prompts | orchestrator | `✅ done` | `main` | 2026-06-19 |
| `Views/Tabs/PresetIOTabView` | Visual parity: hero banner, preset list cards, drag-drop import zone, action buttons | orchestrator | `✅ done` | `main` | 2026-06-19 |
| `Views/Tabs/LeaderboardTabView` | Visual parity: hero banner, mode toggles, sort card, rank badges, online/OG badges, row cards | orchestrator | `✅ done` | `main` | 2026-06-19 |
| `Views/Tabs/AppInfoTabView` | Visual parity: hero banner, version hero card, account/language/backup/legal/smoke-test cards | orchestrator | `✅ done` | `main` | 2026-06-19 |
| `Views/Tabs/AchievementsTabView` | Visual parity: hero banner, summary cards, achievement icon tiles with locked overlay, season-recap action | orchestrator | `✅ done` | `main` | 2026-06-19 |
| `Views/Tabs/MarqueeTabView` | Visual parity: hero banner, debug message/welcome/banner cards | orchestrator | `✅ done` | `main` | 2026-06-19 |
| `Views/Tabs/AnimationsTabView` | Visual parity: hero banner, debug animation control cards | orchestrator | `✅ done` | `main` | 2026-06-19 |
| `Views/Tabs/CatalogueSubmissionsTabView` | Visual parity: hero banner, status buttons, preset/session submission cards | orchestrator | `✅ done` | `main` | 2026-06-19 |
| `Dialogs/RoadmapStepPopup`, `RoadmapConfirmDialog`, `RoadmapStartDialog`, `WelcomeDialog`, `AssetSubmitDialog`, `UpdateProgressDialog` | Localized decorative icons and hard-coded progress text | orchestrator | `✅ done` | `main` | 2026-06-19 |
| `Services/Flash/FlashService.cs` → `CCP.Avalonia/Services/Flash/AvaloniaFlashService.cs` | Real overlay-window flash engine: scheduler, image loading, topmost transparent windows, click-to-close, hydra multiplication, cache invalidation | orchestrator | `✅ done` | `main` | 2026-06-19 |
| `Services/Subliminal/BouncingTextService.cs` → `CCP.Avalonia/Services/BouncingText/AvaloniaBouncingTextService.cs` | Real bouncing-text engine: per-screen transparent overlays, drifting text, bounce XP, corner-hit achievements | orchestrator | `✅ done` | `main` | 2026-06-19 |
| `Services/Subliminal/SubliminalService.cs` → `CCP.Avalonia/Services/Subliminal/AvaloniaSubliminalService.cs` | Real subliminal engine: topmost transparent full-screen flashes, phrase pool, duration/opacity, XP | orchestrator | `✅ done` | `main` | 2026-06-19 |
| `Services/LockCard/MindWipeService.cs` → `CCP.Avalonia/Services/MindWipe/AvaloniaMindWipeService.cs` | Real mind-wipe engine: scheduled/loop LibVLC playback, custom audio path, test trigger, duration achievement | orchestrator | `✅ done` | `main` | 2026-06-19 |
| `Services/Video/VideoService.cs` → `CCP.Avalonia/Services/Video/AvaloniaVideoService.cs` | Real mandatory-video engine: scheduler, full-screen `VideoView` overlay, local file + URL playback, strict-mode shell; attention checks and post-play penalties not yet ported | orchestrator | `✅ done` | `main` | 2026-06-19 |
| `Services/Notifications/OverlayService.cs` → `CCP.Avalonia/Services/Overlays/AvaloniaOverlayService.cs` | Real brain-drain overlay + ad-hoc sustained/timed overlays: full-screen dark violet pulsing distortion, `pink`/`spiral`/`braindrain`/`blur` kinds, topmost click-through windows | orchestrator | `✅ done` | `main` | 2026-06-21 |
| `Services/LockCard/LockCardService.cs` → `CCP.Avalonia/Services/LockCard/AvaloniaLockCardService.cs` | Real lock-card engine: scheduled lock-card popups via existing `LockCardWindow`, multi-monitor sync, strict mode, completion events | orchestrator | `✅ done` | `main` | 2026-06-19 |
| `Services/UI/InteractionQueueService.cs` → `CCP.Avalonia/Services/InteractionQueue/AvaloniaInteractionQueueService.cs` | Real interaction queue: coordinates fullscreen interactions, queues duplicates, stuck-detection recovery; integrated with lock-card and video | orchestrator | `✅ done` | `main` | 2026-06-19 |
| `Views/Tabs/SettingsTabViewModel` | Wire master-volume slider to `IAudioPlayer.SetVolume` | orchestrator | `✅ done` | `main` | 2026-06-19 |
| `Views/Tabs/SettingsTabView.axaml` + `.axaml.cs` | Dashboard parity: load mod-aware center logo (`logo.png`/`logo2.png`) as AvaloniaResource; wire feature-card right-click quick-toggle | orchestrator | `✅ done` | `main` | 2026-06-19 |
| `Views/Tabs/SettingsTabViewModel` | Dashboard quick-links login state: `IsLoggedIn`/`LoggedInDisplayName`, `UnifiedLoginCommand`, `LogoutCommand`, `OpenDiscordCommand` | orchestrator | `✅ done` | `main` | 2026-06-19 |
| `Views/Tabs/SettingsTabView.axaml` + `.axaml.cs` + `ViewModel` | Dashboard parity: audio output device picker + Test Audio; browser Enhance toggle, Deeper badge, Webcam Tracking button, Haptic Audio Sync panel, fixed site-toggle two-way binding | agent | `✅ done` | `main` | 2026-06-21 |
| `Services/Video/AvaloniaVideoService.cs` | Port attention checks, strict mode, and post-play penalty loop/mercy from WPF `VideoService` | orchestrator | `✅ done` | `main` | 2026-06-21 |
| `CCP.Avalonia/AvatarTube/` | Restore AvatarTube behavior: speech phrase system, AI chat replies, Circe emote playback/scheduling, reactions, and windowing behavior | agent | `✅ done` | `main` | 2026-06-21 |
| `CCP.Avalonia/Chaos/` | Chaos overlay animation/z-order/boon logic parity: port remaining WPF animations, finish `AvaloniaChaosWindowZ`, wire run-state/boon logic | agent | `✅ done` | `main` | 2026-06-21 |

### Hand-off Queue (porter → orchestrator)

When a lane reaches `🔵 review`, append an entry here. The orchestrator applies the chokepoint changes
(DI, csproj assets, loc merge), merges, builds, then deletes the entry.

### Claim protocol (mirror of plan §20.4)

1. **Claim:** add your ledger row with `🚧 wip @agentN` and **commit that row first** (cheap "claim commit")
   so concurrent agents see it. Don't start an item that already has a `🚧`/`🔵` row.
2. **Work:** stay inside your lane's subtree (plan §20.1). If you need a chokepoint change, **do not edit it** —
   write it into the Hand-off Queue.
3. **Review → integrate:** set the row to `🔵 review`, fill the Hand-off Queue, hand the worktree to the
   orchestrator. The orchestrator integrates, sets `✅ done`, and clears the row.

---

## Phase 0 — Cleanup (P0)

1. ✅ Remove dead packages (`SharpDX.*`, `OpenAI-DotNet`, `OllamaSharp`) — done; removed from all projects (0 references).
2. Verify and remove unused `MahApps.Metro` / `IconPacks` references.
3. ⚠️ **Corrected — do NOT remove `Microsoft.WindowsAppSDK`.** It is a required transitive of LibVLCSharp; pin it with `ExcludeAssets="all" PrivateAssets="all"` to avoid a WebView2 `NU1605` downgrade (see plan §5.1).
4. Delete `CopyLibVLCAfterPublish` and `IncludeWebView2LoaderInPublish` from shared project; move to Windows head later.
5. Add platform analyzers and remove `CA1416` `NoWarn` once cross-platform seams are in place.
6. Document feature matrix (portable vs. Windows-only) in `docs/platform-feature-matrix.md`.

---

## Phase 1 — Carve Out `CCP.Core` (P0)

1. Create solution file wiring `ConditioningControlPanel`, `CCP.Core`, and `CCP.WindowsOnly`.
2. Audit current extraction status (file-by-file map of models/services).
3. Move remaining root POCOs into `CCP.Core/Models/` and unify namespaces.
4. Finalize platform seam interfaces in `CCP.Core/Platform/` (add `IThumbnailProvider`, `IImageDecoder`, `IEffectSink`, `IAppEnvironment`).
5. Move AI orchestration into `CCP.Core/Services/AIService/`.
6. Move auth/networking transport into `CCP.Core/Services/Account/` and `CCP.Core/Services/Auth/`.
7. Move mod system logic into `CCP.Core/Services/Mod/`.
8. Move session/gamification engine into `CCP.Core/Services/Session/` and `CCP.Core/Services/Progression/`.
9. Replace `DispatcherTimer` with `IScheduler` in Core.
10. Replace `MessageBox.Show` with `IDialogService` in Core.
11. Replace DPAPI with `ISecretStore` in Core.
12. Replace `pack://` URI loading with `IAssetLoader` in Core.
13. Wire legacy WPF app to `CCP.Core` + `CCP.WindowsOnly` via DI.
14. Migrate legacy `App` static service accessors to Core abstractions.
15. Remove duplicate portable code from WPF head.
16. Create `tests/CCP.Core.Tests` project with unit tests.
17. Validate Core builds on Linux/macOS CI.
18. WPF regression smoke test.

---

## Phase 2 — Prove Core Off-Windows (P0)

1. Run `dotnet build CCP.Core/CCP.Core.csproj` on Linux/macOS CI.
2. Run Core unit/integration tests on Linux/macOS.
3. Ensure DPAPI, screen, audio, and UI dispatch seams are the only Windows leaks.

---

## Phase 3 — Avalonia Solution Skeleton (✅ mostly done)

1. ✅ Create `CCP.Avalonia` (shared UI).
2. ✅ Create `CCP.Avalonia.Desktop`.
3. ✅ Create `CCP.Avalonia.Desktop.Windows`.
4. ✅ Create `CCP.Avalonia.Desktop.Linux`.
5. ✅ Create `CCP.Avalonia.Desktop.macOS`.
6. ✅ Create `CCP.Avalonia.Android`.
7. ✅ Wire solution and packages.

---

## Phase 4 — Migrate WPF XAML/UI to Avalonia (P1, largest phase)

### Theme & Assets

1. Migrate app-level theme ResourceDictionaries to Avalonia (`Resources/Theme/*.xaml` → `CCP.Avalonia/Resources/Theme/*.axaml`).
2. Remove all remaining `pack://` URI usage; use `avares://CCP.Avalonia/Assets/...`.
3. ✅ Port WPF localization markup extension to Avalonia (`{loc:Str Key}`) — `CCP.Avalonia/Localization/LocExtension.cs` created and wired in `App.axaml`.

### Main Shell & Dashboard

4. ✅ Port Settings/Dashboard tab view (velvet mosaic grid + right panel) — **DONE**.
5. Finish `MainWindow.axaml` code-behind parity (drag-drop, resize grips, window state, XP bar, banner).
6. Implement Avalonia custom window chrome and resizing across desktops.
7. ✅ Implement `IBrowserHost` strategy — `WebView2BrowserHost` embedded via `NativeControlHost` on Windows; system-browser fallback on Linux/macOS/mobile.

### Views, Dialogs, Feature Controls

7. Port remaining WPF-only dialogs to Avalonia (`Dialogs/*.xaml` without `.axaml` counterparts).
8. Replace `MessageBoxStub` with `IDialogService` across Avalonia windows.
9. Complete `FeatureSettingsPopup`.
10. Port feature controls code-behind to Avalonia (file pickers, audio, dispatch seams).
11. Complete AvatarTube window.
12. Complete Chaos overlay windows and animations.
13. Port WPF-only utility windows (Splash, Tutorial, Webcam, Mantra, SessionEditor).
14. Port `Lab/GazeMinigame` window.
15. Convert WPF helpers/converters to Avalonia.
16. Implement cross-platform drag-drop for session import.
17. Add design-time data contexts and accessibility labels to all views.
18. Build and smoke-test Avalonia desktop app.
19. Run UI parity matrix.
20. Clean legacy WPF references from `CCP.Avalonia.csproj`.

---

## Phase 5 — Replace Media & Audio Stack (P1)

1. Audit and extend audio abstraction seams (`IAudioPlayer`, `IAudioDeviceService`, `ISystemAudioDucker`).
2. Wire short-SFX call sites to `IAudioPlayer`.
3. Port system-audio ducking to seam implementations (Windows full, Linux/macOS graceful).
4. Port Deeper long-form audio player (`EnhancementAudioPlayer`).
5. Replace NAudio `MediaFoundationReader` in audio analysis pipeline.
6. Port mandatory `VideoService` — core playback.
7. Port mandatory `VideoService` — attention checks and strict mode.
8. Implement mobile `IVideoSurface` for Android.
9. Verify memory-render video surfaces (`AvaloniaInlineLoopVideo`, dual-monitor mirror).
10. Finalize GIF/SVG migration (`AvaloniaAnimatedGif`, `Svg.Skia`).
11. Lock down native LibVLC packaging and discovery per RID.
12. Run Phase 5 build/test matrix.

---

## Phase 6 — Replace OS-Shell Features (P1)

1. Finalize `ITrayIcon` and migrate desktop tray behavior.
2. Port global hotkeys and panic key to `IHotkeyProvider`/`IInputHook` seams.
3. Implement `IWallpaperProvider` wiring and port wallpaper override.
4. Implement `IBrowserHost` per-platform and wire browser consumers.
5. Port WebView2 parity features into `WebView2BrowserHost`.
6. Implement `IWindowChrome` per-platform title-bar handling.
7. Provide `IOverlaySurface` implementations for desktop overlay windows.
8. Port overlay consumers to `IOverlaySurface`.
9. Define `IThumbnailProvider` and replace `ShellThumbnailHelper`.
10. Implement `IFrameSource` for screen capture and wire webcam/gaze windows.
11. Update `IPlatformCapabilities` and gate OS-shell UI options.
12. Register all Phase 6 implementations in the correct head DI containers.
13. Add Phase 6 smoke tests and document platform matrix.

---

## Phase 7 — Build & Publish Pipeline (✅ done)

1. ✅ Define RuntimeIdentifiers per head.
2. ✅ Desktop single-file + self-contained publish.
3. ✅ Android standard build with trimming roots.
4. ✅ CI matrix builds.
5. Code signing and notarization setup (macOS).
6. Android keystore and app bundle publishing.

---

## Phase 8 — Mobile Feature Gating & Adaptation (✅ done structurally)

1. ✅ Hide overlay effects on Android.
2. ✅ Hide global hooks/system-key suppression on Android.
3. ✅ Hide system tray on Android.
4. ✅ Hide wallpaper override on Android.
5. ✅ Hide GDI/desktop capture on Android.
6. ✅ Hide screen OCR on Android.
7. ✅ Hide multi-monitor video mirroring on Android.
8. Design mobile-first shell (tabs/navigation).
9. Touch-optimized controls.
10. Native camera + LibVLC for Lab/webcam features.
11. Background audio/execution restrictions on Android.
12. Address Google Play content policies.

---

## Localization Parity (P1)

1. Audit hard-coded strings and define key naming convention.
2. Move language assets into `CCP.Core` and wire loading.
3. Create Avalonia `{loc:Str}` markup extension.
4. Localize main shell and dashboard chrome.
5. Localize system/config tab views.
6. Localize content & lab tab views.
7. Localize account & catalogue tab views.
8. Localize primary feature controls.
9. Localize secondary feature controls and shared feature UI.
10. Localize account & onboarding dialogs.
11. Localize editor & catalogue dialogs.
12. Localize roadmap dialogs.
13. Localize session, quiz, mod-creator & haptics windows.
14. Localize webcam & tool windows.
15. Localize popups, splash & recap surfaces.
16. Localize Chaos hub, HUD & intro.
17. Localize Chaos overlay effects.
18. Localize Deeper editor & player.
19. Localize AvatarTube.
20. Externalize fallback strings in Core models.
21. Externalize user-facing messages in Core services.
22. Externalize dynamic strings in shell/system ViewModels.
23. Externalize dynamic strings in content/social ViewModels.
24. Localize tray/platform strings.
25. Add localized accessibility labels.
26. Add localization QA gate and tests.
27. Sync skeleton translations and update docs.
28. (Optional) Legacy WPF parity shim.

---

## Stability / Performance / Maintainability (P2)

1. Replace static service locator with scoped DI container.
2. Implement `IAsyncDisposable` for long-running services.
3. Move service initialization off UI thread with async startup + splash progress.
4. Pool overlay windows instead of creating/disposing per effect.
5. Use Avalonia/Skia `CompositionCustomVisual` for particle/spiral effects.
6. Pool audio players; use LibVLC for short SFX.
7. Replace `System.Drawing` with SkiaSharp / ImageSharp.
8. Move hard-coded paths/constants to `appsettings.json` + options pattern.
9. Add structured logging with correlation IDs.
10. Add feature flags for platform/experimental features.

---

## Settings & Data Backward Compatibility (P2)

1. Read existing WPF settings format on first launch.
2. Version settings schema and add `ISettingsMigration` classes.
3. Back up old settings before migration.
4. Preserve user data path across updates.

---

## Accessibility, Telemetry, Crash Reporting (P2)

1. Set `AutomationProperties.Name`/`HelpText` on every interactive control.
2. Ensure keyboard navigation (Tab order, access keys).
3. Respect system high-contrast and reduce-motion settings.
4. Replace `dbghelp.dll` minidumps with cross-platform crash handler.
5. Add opt-in telemetry for startup time, feature usage, crash-free sessions.
6. Ensure no PII in logs.

---

## Code Signing & Distribution (P3)

1. Windows code signing certificate for EXE + installer.
2. Linux GPG sign AppImage/package.
3. macOS Apple Developer ID notarization + staple.
4. Android upload/signing keys + Play Console.
5. Provide SHA256 checksums for artifacts.
6. Host native dependencies (Linux libvlc, macOS ARM64 libvlc) on CDN/release assets.

---

## Current Sprint Focus

§19.3 main-merge backlog is complete: `AppSettings`, `Fredoka`, `UpdateService`, service-deltas, `ChaosCrashSentinel`, `ChaosBoonColors`, shared-host overlays, `ChaosSkiaFxOverlay`, and the full `BubbleService` overhaul (ambient + chaos variants + field hazards + shared-host + global mouse hook) are ported. The §19.4 project-reference collapse remains blocked; see plan §19.4.

Chaos overlay integration is complete and build/test green: cursor-glow/banner/field-FX animations, `AvaloniaChaosWindowZ` topmost re-assert, full countdown → descent → spawn → scoring → draft → results lifecycle, and seeded Hub catalogues are in `main`. AvatarTube behavior restoration is in progress by `@agent1`.

Next priorities (pick one lane at a time):

1. **AvatarTube behavior** — integrate `@agent1` output when ready; restore speech phrase system, AI chat replies, Circe emote playback/scheduling, reactions, and windowing behavior.
2. **Phase 4 UI parity smoke-test** — run the Avalonia desktop app and fill `docs/avalonia-ui-parity-matrix.md`.
3. **Phase 4 remaining work** — port WPF-only dialogs/utility windows, localize hard-coded onboarding/privacy strings, and wire feature-control code-behind.
