# Avalonia Port Batch TODO

## Scope
Port WPF MainWindow partial logic into Avalonia ViewModels/commands for:
- MainWindow.Browser.cs
- MainWindow.Autonomy.cs
- MainWindow.KeywordTriggers.cs
- MainWindow.Haptics.cs
- MainWindow.Lab.cs
- MainWindow.LabTab.cs

## Status
- [x] Read source partials and existing Avalonia structure
- [x] Map WPF tabs to Avalonia ViewModels
- [x] Implement BambiTakeoverTabViewModel (Autonomy)
- [x] Implement HapticsTabViewModel
- [x] Implement AwarenessTabViewModel (Keyword Triggers)
- [x] Implement LabTabViewModel + BlinkTrainerTabViewModel (Lab / LabTab)
- [x] Extend SettingsTabViewModel (Browser shell)
- [x] Register new ViewModels in ServiceCollectionExtensions
- [x] Update MainWindow.axaml DataTemplates
- [x] Build CCP.Avalonia Release
- [x] Build CCP.Avalonia.Desktop.Windows Release
- [x] Build CCP.Avalonia.Desktop.Linux Release
- [x] Run CCP.Core.Tests

## Build Results
- CCP.Avalonia Release: **succeeded** (53 pre-existing warnings, 0 errors)
- CCP.Avalonia.Desktop.Windows Release: **succeeded** (0 warnings, 0 errors)
- CCP.Avalonia.Desktop.Linux Release: **succeeded** (0 warnings, 0 errors)
- CCP.Core.Tests: **passed** (4/4)

## What Was Done
- Added `BambiTakeoverTabViewModel` exposing autonomy enable, consent, intensity/cooldown/interval sliders, behaviour toggles, and test/force commands.
- Added `HapticsTabViewModel` exposing provider selection, connection stubs, global intensity, audio-sync toggles, and per-event enable/intensity/mode grids.
- Added `AwarenessTabViewModel` exposing keyword trigger list (add/delete/browse-audio), cooldown/multiplier sliders, screen-OCR toggles, and highlight options.
- Added `LabTabViewModel` exposing lockdown, quiz, chaos, wallpaper override, and pop-quiz commands/properties.
- Added `BlinkTrainerTabViewModel` exposing webcam tracking start/stop, calibration/tracker-test/quick-recal stubs, device/monitor combos, focus gaze toggle, and debug log.
- Extended `SettingsTabViewModel` with browser initialization/navigation stubs and legacy audio-sync latency/intensity controls.
- Registered the new ViewModels in `ServiceCollectionExtensions` and wired DI retrieval in `MainWindowViewModel.InitializeTabs`.
- Added `DataTemplate` entries in `MainWindow.axaml` for the new ViewModels (currently backed by `PlaceholderTabView`).

## What Remains
- Full XAML views for the new tabs (currently placeholders).
- Extraction of live service seams into Core (autonomy, haptics, keyword triggers, browser, lockdown, quiz, chaos, wallpaper, webcam/gaze focus) so the stub commands can invoke real logic.
- Browser view: replace stub navigation with an actual Avalonia web-view control or platform browser integration.
- Profile/Discord viewer: port the remainder of `MainWindow.Browser.cs` (Discord login/profile viewer) into `ProfileTabViewModel`.
- Lockdown theme/animation and rapid-blink recalibration service wiring.
