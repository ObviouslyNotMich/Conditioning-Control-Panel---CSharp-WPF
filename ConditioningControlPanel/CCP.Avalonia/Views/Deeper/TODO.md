# EnhancementPlayerWindow Avalonia Port TODO

## Goal
Port `Views/Deeper/EnhancementPlayerWindow` from WPF/WebView2 to Avalonia/LibVLC, creating only files under `CCP.Avalonia/Views/Deeper/`.

## Checklist

- [x] Read original WPF XAML + code-behind
- [x] Read existing Avalonia window/video stubs (`VideoSpikeWindow`, `AvaloniaVideoSurface`, etc.)
- [x] Read Core DI/services (`IAudioPlayer`, `IDialogService`, `LibVLC` registration)
- [x] Create `EnhancementPlayerWindow.axaml`
  - Use `LibVLCSharp.Avalonia.VideoView` for video
  - Preserve header, file context strip, media pane, mini-timeline, transport, event log layout
  - Adapt WPF resources to existing Avalonia theme resources
- [x] Create `EnhancementPlayerWindow.axaml.cs`
  - DI-resolve `IAudioPlayer`, `IDialogService`, `LibVLC`
  - LibVLC-based audio + video playback with one `MediaPlayer`
  - Event log, filter pills, status pill, mini-timeline, waveform, drag/drop, file pickers
  - Stub engine-host integration (WPF `EnhancementHostService`/`IPlaybackTimeSource` not yet in Core)
- [x] Build `CCP.Avalonia/CCP.Avalonia.csproj` (shared UI)
- [x] Build desktop heads
- [x] Final verification and report

## Notes
- The original WPF window depends on WPF-only services (`EnhancementHostService`, `EnhancementAudioPlayer`, `BrowserVideoTimeSource`, `EnhancementAudioPlayerTimeSource`, `EnhancementResolver`). These are not in `CCP.Core` and cannot be referenced from `CCP.Avalonia`. This port focuses on the view + media playback; engine/effect integration is stubbed with `TODO` markers for when those services are migrated to Core.
- `AudioWaveformCache` has been migrated to `CCP.Core` and is now wired into the player so audio waveforms render.
