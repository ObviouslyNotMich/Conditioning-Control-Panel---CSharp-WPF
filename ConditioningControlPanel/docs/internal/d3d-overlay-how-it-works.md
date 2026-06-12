# D3D Native Overlay — How It Works (current `feature/openaicomp`)

> Snapshot of the D3D overlay subsystem before it is split into its own branch.

## 1. Goal

Provide a second overlay backend that can render the app’s overlays (pink filter, spiral, brain-drain blur) via Direct3D 11 instead of only through WPF windows. The intent is to improve compatibility with fullscreen games and multi-monitor setups where WPF topmost windows can be clipped or fail to stay on top.

## 2. Architecture overview

```
App initialization
    │
    ▼
OverlayBackendFactory.Create()
    │
    ├── CCP_OVERLAY_BACKEND=native ──► NativeOverlayService
    │                                    │
    │                                    ├── NativeOverlayBootstrap.Probe()
    │                                    ├── NativeOverlayRuntimeHost
    │                                    │   ├── NativeOverlayTargetProcessTracker
    │                                    │   ├── NativeOverlayHookSessionMachine
    │                                    │   ├── NativeOverlayD3DRendererBridge
    │                                    │   └── NativeOverlayCaptureSessionBridge
    │                                    └──
    └── default / wpf ──► OverlayService (original WPF backend)
```

`IOverlayService` is the shared abstraction. `App.Overlay` is now typed as `IOverlayService`, and the factory selects the concrete backend.

## 3. New files and their roles

| File | Responsibility |
|------|----------------|
| `IOverlayService.cs` | Shared interface for both backends (`Start`, `Stop`, `ShowOverlayTimed`, `ShowOverlaySustained`, `HideOverlaySustained`, `PulseOverlays`, `RefreshOverlays`, `RefreshForDualMonitorChange`, `NotifyTopWindowClosed`, `StopPinkFilter`, `StopSpiral`, `UpdateBrainDrainBlurOpacity`). |
| `OverlayBackendFactory.cs` | Reads `CCP_OVERLAY_BACKEND` env var and returns `NativeOverlayService` or `OverlayService`. |
| `NativeOverlayBootstrap.cs` | Probes the system for native-backend readiness: Windows x64, `d3d11.dll`/`dxgi.dll` loadable, DWM composition, and ability to query the foreground process. Returns a `NativeOverlayProbeResult`. |
| `NativeOverlayService.cs` | Implements `IOverlayService`. Wraps `NativeOverlayRuntimeHost` and falls back to `OverlayService` for all actual visual output in the current implementation. |
| `NativeOverlayRuntimeHost.cs` | Orchestrates the native overlay lifecycle: starts/stops the tracker, session, renderer, and capture; forwards desired overlay state; runs a 250 ms frame tick when attached. |
| `NativeOverlayTargetProcessTracker.cs` | Monitors the foreground window, resolves its process/screen, and raises `TargetChanged` events. |
| `NativeOverlayHookSessionMachine.cs` | Manages the attach state machine (`Uninitialized` → `TrackingTarget` → `AttachPending` → `Attached` / `Fallback` / `Faulted`). |
| `NativeOverlayD3DRendererBridge.cs` | Initializes a D3D11 device and creates/manages a topmost layered overlay window for the attached target. |
| `NativeOverlayCaptureSessionBridge.cs` | Captures screen frames via DXGI desktop duplication to feed the renderer. |
| `NativeOverlayRuntimeContracts.cs` | Shared records/enums: `NativeOverlayTargetSnapshot`, `NativeOverlayDesiredState`, `NativeHookSessionState`. |

## 4. Integration points

### `App.xaml.cs`

- `App.Overlay` changed from `OverlayService` to `IOverlayService`.
- Initialization changed from `Overlay = new OverlayService()` to `Overlay = OverlayBackendFactory.Create()`.

### `OverlayService.cs`

- Now implements `IOverlayService`.
- `StopPinkFilter()` and `StopSpiral()` changed from `internal` to `public`.
- Added more aggressive topmost re-pinning while pink filter is active (`SWP_SHOWWINDOW` flag, per-tick forced re-pin).

### `FlashService.cs`

- Static image loading refactored into `TryLoadStaticImage()` using `BitmapImage` instead of `System.Drawing.Bitmap`. D3D rendering paths prefer WPF bitmap sources over GDI+ bitmaps.

### `ScreenMirrorService.cs`

- Added mixed-orientation guard: if monitors are a mix of portrait and landscape, cloning is skipped to prevent Windows from rotating one display incorrectly.

### `MainWindow.xaml.cs`

- Browser fullscreen popout now sets `Owner = this` and uses `WindowStartupLocation.CenterOwner`.
- Added `ApplyWindowToVirtualFullscreen()` helper that spans the entire virtual desktop instead of maximizing, intended to support fullscreen overlays across multiple monitors.

## 5. How the native backend runs

1. **Probe** at construction time. If anything is missing (wrong OS, missing DLLs, no DWM), `_fallbackOnly` is set.
2. **Start** opens the runtime host and starts tracking the foreground window.
3. **Target changes** → session moves to `AttachPending`; renderer tries to attach to the target process/screen; capture starts.
4. **Frame tick** (every 250 ms while attached) checks renderer/capture health, acquires a frame, and asks the renderer to render the current desired state.
5. If anything fails, the session transitions to `Fallback`.

In the current code, `NativeOverlayService` still routes all visible overlay calls (`ShowOverlayTimed`, etc.) through the WPF `_fallback`, so the D3D renderer is initialized and tracked but the visible output is still produced by `OverlayService`.

## 6. Env-var toggle

Set `CCP_OVERLAY_BACKEND=native` before launch to select the native backend. Any other value (or unset) selects the original WPF backend.

## 7. Later dependencies (why it’s hard to remove)

Commits after `77fd55aa` added calls that assume the `IOverlayService` API:

- `Services/Chaos/ChaosModeService.cs` → `App.Overlay.WarmSpiralCache()`
- `Services/Deeper/IActionDispatcher.cs` → `App.Overlay.SetSustainedOverlayOpacity(...)`

Removing D3D from `feature/openaicomp` without breaking those callers requires either keeping the expanded `IOverlayService` interface (and stubbing the new methods on `OverlayService`) or refactoring those callers to not use the D3D-only methods.
