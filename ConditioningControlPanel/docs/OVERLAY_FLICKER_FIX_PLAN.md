# Overlay Flicker Fix Plan — Avalonia v12 Port

## Problem

Transparent click-through overlays (pink tint, spiral, braindrain) flicker /
disappear when a fullscreen mandatory video plays across all screens. The user
confirmed this is a **z-order / layering** issue, not a media-ordering issue.

## Root cause (not bitmaps / not memory)

The flicker is a **z-order race**, confirmed from the code:

- `VideoOverlayWindow` is constructed with `Topmost = true` +
  `WindowState.FullScreen` (`CCP.Avalonia\Services\Video\AvaloniaVideoService.cs:814,818`),
  shown, then `OverlayZ.Register(... Video)` demotes it to `HWND_NOTOPMOST`
  **asynchronously on the `Opened` event** (`CCP.Avalonia\Services\Overlays\OverlayZ.cs:59-65`).
  During that async gap the opaque fullscreen video sits *above* the topmost
  tint/spiral/braindrain on every monitor at once → they blink out.
- `OverlayZ` deliberately **never re-pins** (`OverlayZ.cs:7-22`). The comment
  claims this avoids blink, but it also means nothing restores the stack after
  the video disturbs it.
- Unlike WPF, there is **no `NotifyTopWindowClosed` / `ReassertZOrder`** hook
  from the video service into the overlay service. WPF re-pins every 500 ms +
  on video close (`Services\Notifications\OverlayService.cs:403, 1770`); the
  port dropped all of that.

### Why no cross-platform lib fixes this

Multi-window z-order of transparent click-through windows is OS
window-manager territory (Win32 on Windows, X11/Wayland on Linux, NSWindow on
macOS). Avalonia 12's `Topmost` property is the only cross-platform lever and
it is already in use. The fix is local: eliminate the open-race and add a
lightweight re-assert.

## WPF reference behavior (the target parity)

| Aspect | WPF original | Avalonia port (current) |
|---|---|---|
| Overlay window props | `WindowStyle.None`, `AllowsTransparency=true`, `Topmost=true`, `ShowInTaskbar=false`, `ShowActivated=false`, `Focusable=false`, `IsHitTestVisible=false` | matches (`AvaloniaOverlayService.cs:825-848`) |
| Click-through | `WS_EX_TRANSPARENT \| WS_EX_LAYERED \| WS_EX_NOACTIVATE \| WS_EX_TOOLWINDOW` | matches (`ApplyWindowStylesCore:770-780`) |
| Per-screen windows | one window per `Screen` | matches (`GetScreens:722-742`) |
| Z-order model | all overlays topmost, no enum, re-pin every 5 s + on `NotifyTopWindowClosed` | explicit `OverlayZ.Layer` enum, two-band (topmost/non-topmost), placed once, **no re-pin** |
| Video window | `WindowStyle.None`, `Topmost=true`, `WS_EX_NOACTIVATE` only during chaos, one LibVLC window per screen | `WindowDecorations.None`, `WindowState.FullScreen`, `Topmost=true` then demoted by `OverlayZ.Register(Video)` to non-topmost |
| Re-assert on video start | none | none |
| Re-assert on video close | `App.Overlay?.NotifyTopWindowClosed()` → `ReassertZOrder(force:true)` | **none — missing** |
| Keep-alive heartbeat | 500 ms timer → `ReassertZOrder` every 5 s + `RecreateOverlays` after 3 s loss | 500 ms timer → `RefreshOverlays` only, **no re-pin** |

## The plan

### 1. Eliminate the video-window topmost race

File: `CCP.Avalonia\Services\Video\AvaloniaVideoService.cs` (lines 802-833)

- Construct `VideoOverlayWindow` with `Topmost = false` from the start (it is
  a non-topmost `Layer.Video` anyway).
- Drop `WindowState.FullScreen` (which forces a mode switch that recomposites
  and blinks overlays); use `WindowState.Normal` + `WindowDecorations.None` +
  `ConstrainToScreen(screen)` to cover the screen, matching how the overlays
  themselves are sized. This matches WPF, which uses `WindowStyle.None` +
  bounds, not `WindowState.FullScreen`.

### 2. Make `OverlayZ.Register` synchronous-safe for the video window

File: `CCP.Avalonia\Services\Overlays\OverlayZ.cs` (lines 48-66)

- Call `OverlayZ.Register` **before** `Show()` when a handle exists, or keep
  the `Opened` deferral only for the no-handle case. For the video window this
  removes the async gap entirely. (Overlays already have handles post-`Show`
  so they are fine.)

### 3. Add a one-shot re-assert hook (port WPF's `NotifyTopWindowClosed`)

The minimal version WPF has that the port lacks.

- Add `NotifyTopWindowClosed()` and `NotifyTopWindowOpened()` to
  `CCP.Core\Services\Overlays\IOverlayService.cs`.
- In `AvaloniaOverlayService`, implement them as a single
  `OverlayZ.Reassert()` call (not a timer — see step 4).
- Call `NotifyTopWindowOpened()` from `AvaloniaVideoService.OnVideoWindowStarted`
  (line 364) and `NotifyTopWindowClosed()` from `OnVideoWindowEnded` /
  `CleanupInternal` (lines 395, 706).

### 4. Add `OverlayZ.Reassert()` — throttled, not a heartbeat

File: `CCP.Avalonia\Services\Overlays\OverlayZ.cs`

- Keep a `WeakReference<Window>` list of registered topmost-band windows.
  `Reassert()` re-issues
  `SetWindowPos(HWND_TOPMOST, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE)` once
  across them.
- **Throttle to ~250 ms** (timestamp guard) so the flash-churn blink the
  original `OverlayZ` comment warns about does not return. This is the key
  difference from the abandoned "re-pin every tick" design: event-driven +
  throttled, not timer-driven.
- Mark with:
  `// ponytail: throttled re-pin; raise threshold or per-window lock if blink returns under heavy churn`

### 5. Stop the `FloatingText` attention-target from fighting the band

File: `CCP.Avalonia\Services\Video\AvaloniaVideoService.cs` (lines 934-1247)

- It re-pins itself `HWND_TOPMOST` every 32 ms (lines 1113-1127), reshuffling
  the overlays. Register it via `OverlayZ.Register(..., Layer.Bubbles)` (it is
  a topmost-band sibling) and drop its private `SetWindowPos` loop, or at
  minimum route its re-pin through the same throttled `Reassert`.

## Status

Implemented. Key changes:

- `OverlayZ` now keeps a weak-reference list of topmost-band windows and exposes
  `Reassert()`, throttled to 250 ms so rapid calls do not cause flash-churn blink.
- `VideoOverlayWindow` is now created with `Topmost = false` and
  `WindowState = Normal` (sized via `ConstrainToScreen`), eliminating the
  async demotion race introduced by `WindowState.FullScreen`.
- `IOverlayService` gained `NotifyTopWindowOpened()` / `NotifyTopWindowClosed()`;
  `AvaloniaOverlayService.NotifyTopWindowClosed()` calls `OverlayZ.Reassert()`.
- `AvaloniaVideoService` now reports video open/close to the overlay service and
  re-asserts z-order when cleanup runs.
- `FloatingText` attention targets no longer call `SetWindowPos(HWND_TOPMOST)`
  on a 32 ms loop; they are registered via `OverlayZ.Register(..., Layer.Bubbles)`
  so they stay in the topmost band without reshuffling the overlay stack.

## Verification

- `dotnet build ConditioningControlPanel/CCP.Core/CCP.Core.csproj -c Release` ✅
- `dotnet build ConditioningControlPanel/CCP.Avalonia/CCP.Avalonia.csproj -c Debug` ✅
- `dotnet build ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj -c Debug` ✅
- `dotnet test ConditioningControlPanel/tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release` ✅ 101 passed

Remaining manual step: run the Avalonia Windows head with `--max-benchmark` and a
video assets path to confirm tint/spiral/braindrain remain stable across the
1-minute video stress segment on all monitors.
