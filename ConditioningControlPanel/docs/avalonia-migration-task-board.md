# Avalonia Migration Task Board

Master task list derived from `docs/crossplatform-rebuild-plan.md` and the detailed breakdowns produced by planning sub-agents.

## Legend

- `P0` — Blocker for other work
- `P1` — High value / on the critical path
- `P2` — Important but can be parallelized
- `P3` — Polish / final parity
- `🚧` — Partially done
- `✅` — Done

---

## Phase 0 — Cleanup (P0)

1. Remove dead packages (`SharpDX.*`, `OpenAI-DotNet`, `OllamaSharp`) from WPF head.
2. Verify and remove unused `MahApps.Metro` / `IconPacks` references.
3. Remove `Microsoft.WindowsAppSDK` exclusion hack from shared projects.
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

Dashboard, image parity, and core localization/extension infrastructure are now in place. The immediate priority is **full desktop UI parity smoke-testing** (Phase 4 task 18) and **closing remaining gaps**:

1. ✅ Port tab views that lack rich cards/images (Presets, Quests, Enhancements, Assets, Deeper, Level Features).
2. ✅ Replace hard-coded English strings in feature controls/tab views with `{loc:Str ...}` bindings (dialogs, windows, Chaos overlays, AvatarTube, Deeper editor/player remain).
3. Run the Avalonia desktop app and document UI parity matrix (missing icons, layout overflows, missing interactions).
4. Implement remaining missing feature control code-behind (file pickers, audio dispatch, feature activation/toggle flow).
5. Port remaining WPF-only dialogs and utility windows.
3. Port the Avalonia `{loc:Str}` markup extension or replace usages with explicit bindings.
