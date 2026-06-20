# AvatarTube Avalonia port TODO

## Completed
- [x] Read source WPF AvatarTube files and existing Avalonia examples.
- [x] Create target directory `CCP.Avalonia/AvatarTube/`.
- [x] Move pending files from `_pending/AvatarTube/` to `CCP.Avalonia/AvatarTube/`.
- [x] Fix Avalonia API mismatches:
  - `Panel.ZIndex` -> `ZIndex`
  - Removed `x:Name` from non-`StyledElement` transforms and `MenuFlyout`
  - Removed unsupported `ItemsControl.HorizontalContentAlignment`
  - Removed `Closed="OnClosed"` from XAML (using override `OnClosed` instead)
  - Extracted converters to public top-level `AvatarTubeConverters.cs`
  - Added public parameterless constructor and made `_parentWindow` nullable
  - `DispatcherPriority.Loaded` -> `DispatcherPriority.Normal`
  - Rewrote `AvatarRandomBubble` to use `Ellipse` shape instead of WPF `DrawingGroup`/`DrawingImage`
  - Fixed nullability warnings in speech-bubble helpers
- [x] Build Release: AvatarTube files compile with only stub-related warnings.

## Build status
- AvatarTube port: **compiles cleanly** (no errors; only stub/ unused-field warnings).
- Full `CCP.Avalonia` Release clean build: **succeeds** with 0 errors, 65 warnings (mostly AvatarTube stub fields and a few unused events in existing Platform files).

## Remaining (post-port stubs)
- [ ] Windows-specific HWND/z-order logic remains stubbed for cross-platform.
- [ ] Audio playback remains stubbed pending `IAudioPlayer` integration.
- [ ] Animated GIF/WebP avatar pipeline remains stubbed.
- [ ] AI chat and moderation counter wiring remain stubbed.
- [ ] Parent MainWindow coupling (`EngineStopped`, remote emotes, etc.) remains stubbed.
