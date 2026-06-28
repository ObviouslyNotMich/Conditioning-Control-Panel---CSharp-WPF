---
name: unified-compositor-engine
description: "Build a unified Skia compositor engine for Avalonia v12 that replaces the multi-window topmost overlay architecture with a single render surface, eliminating z-fighting, flickering, and race conditions. Integrates video decoding, flash images, subliminals, bouncing text, spiral, brain drain, pink tint, bubbles, and chaos overlays into one 60fps compositor window with proper z-layer ordering."
---

# unified-compositor-engine

Use this skill when asked to build or port a unified compositor engine that merges multiple separate topmost overlay windows into a single Avalonia render surface with Skia-based compositing, or when the existing multi-window overlay architecture causes z-fighting, flickering, race conditions, or lag.

This is a **multi-phase architectural rewrite**. Do not attempt to execute all phases in one session. Each phase produces a working build and should be committed before the next phase begins.

## Scope

Applies to the entire Avalonia overlay and video rendering subsystem of Conditioning Control Panel:
- Video playback (currently `LibVLC` via `VideoView` in `VideoOverlayWindow`)
- Flash image overlays (`FlashOverlayWindow`)
- Subliminal text overlays (`SubliminalWindow`)
- Bouncing text overlays (`BouncingTextWindow`)
- Brain drain overlay (`BrainDrainOverlayWindow`)
- Spiral overlay (`SpiralOverlayWindow`)
- Pink tint / color overlay (`OverlayWindow`)
- Lock card overlay
- Chaos system overlays (flash, bubble host, field FX, cursor glow, etc.)
- All topmost `Window` instances with `TransparencyLevelHint.Transparent` and `Topmost = true`

## The Problem

The current Avalonia head creates **a separate topmost `Window` per overlay layer**:

| Service | Window Class | File |
|---|---|---|
| Video | `VideoOverlayWindow` | `Services/Video/AvaloniaVideoService.cs` |
| Flash | `FlashOverlayWindow` | `Services/Flash/AvaloniaFlashService.cs` |
| Subliminal | `SubliminalWindow` | `Services/Subliminal/AvaloniaSubliminalService.cs` |
| Bouncing Text | `BouncingTextWindow` | `Services/BouncingText/AvaloniaBouncingTextService.cs` |
| Pink Tint | `OverlayWindow` | `Services/Overlays/AvaloniaOverlayService.cs` |
| Brain Drain | `BrainDrainOverlayWindow` | `Services/Overlays/AvaloniaOverlayService.cs` |
| Spiral | `SpiralOverlayWindow` | `Services/Overlays/AvaloniaOverlayService.cs` |
| Chaos Flash | `ChaosFlashOverlay` | `Chaos/ChaosFlashOverlay.axaml.cs` |
| Chaos Bubbles | `ChaosBubbleHostOverlay` | `Chaos/ChaosBubbleHostOverlay.cs` |
| Chaos Field FX | `ChaosFieldFxOverlay` | `Chaos/ChaosFieldFxOverlay.axaml.cs` |
| Chaos Skia FX | `ChaosSkiaFxOverlay` | `Chaos/ChaosSkiaFxOverlay.cs` |
| ... | 15+ more | `Chaos/` |

Each window has `Topmost = true`, `WindowTransparencyLevel.Transparent`, and its own `DispatcherTimer` or `CompositionTarget.Rendering` invalidation loop. They fight for z-order via `SetWindowPos` P/Invoke (`AvaloniaOverlayService.cs`, `AvaloniaSubliminalService.cs`). This causes:
- **Z-fighting**: Windows overlap unpredictably; flash appears behind video or spiral flickers above brain drain
- **Flickering**: 15+ separate invalidation timers at different frequencies cause frame tearing
- **Lag**: Window manager compositor overhead for 15+ transparent windows; each frame the OS must blend 15+ surfaces
- **Race conditions**: `Chaos` overlays and `Effect` services compete for `Topmost` re-assertion
- **LibVLC `VideoView` airspace**: `VideoView` is a native control host; it cannot be layered under transparent Avalonia controls cleanly

## The Solution

A single **Unified Compositor Window** (`CompositorWindow`) that owns one `SKCanvasView` (or `Image` with `WriteableBitmap`). All layers render into a single Skia surface via a `DrawingContext`-like compositor pipeline. One 60Hz `DispatcherTimer` drives the entire frame. No separate topmost windows. No z-fighting. No airspace problem.

### Z-Layer Constants (Authoritative)

```csharp
public static class CompositorLayers
{
    public const int Video          = 10;  // Video decoder frame (bottom-most content)
    public const int MandatoryVideo = 15;  // Mandatory video attention-check layer
    public const int LockCard       = 20;  // Lock card overlay
    public const int Flash          = 30;  // Flash image popups
    public const int Subliminal     = 40;  // Subliminal text flashes
    public const int Bubbles        = 45;  // Chaos/clickable bubbles
    public const int BouncingText   = 50;  // Bouncing text phrases
    public const int BrainDrain     = 55;  // Brain drain blur overlay
    public const int Spiral         = 60;  // Spiral animation overlay
    public const int PinkTint       = 70;  // Full-screen pink color tint (top-most)
}
```

> **Rule:** These constants override any z-ordering found in the current Avalonia codebase. The old `AvaloniaChaosWindowZ` and per-service `Topmost` toggles are replaced by this single layer stack.

## Target Architecture

```
┌─────────────────────────────────────────────────────────┐
│  CompositorWindow (1 Window, Topmost, Fullscreen)       │
│  ┌─────────────────────────────────────────────────────┐ │
│  │  SKCanvasView / Image (single render surface)        │ │
│  │  ┌───────────────────────────────────────────────┐   │ │
│  │  │  PinkTint (tint, opacity 0..1)               │   │ │  z=70
│  │  │  ┌─────────────────────────────────────────┐ │   │ │
│  │  │  │  Spiral (animated GIF / pre-rendered)  │ │   │ │  z=60
│  │  │  │  ┌─────────────────────────────────────┐ │ │   │ │
│  │  │  │  │  BrainDrain (blur shader)          │ │ │   │ │  z=55
│  │  │  │  │  ┌─────────────────────────────────┐ │ │ │   │ │
│  │  │  │  │  │  BouncingText (drifting text) │ │ │ │   │ │  z=50
│  │  │  │  │  │  ┌─────────────────────────────┐ │ │ │ │   │ │
│  │  │  │  │  │  │  Bubbles (clickable rects) │ │ │ │ │   │ │  z=45
│  │  │  │  │  │  │  ┌─────────────────────────┐ │ │ │ │ │   │ │
│  │  │  │  │  │  │  │  Subliminal (text)     │ │ │ │ │ │   │ │  z=40
│  │  │  │  │  │  │  │  ┌─────────────────────┐ │ │ │ │ │ │   │ │
│  │  │  │  │  │  │  │  │  Flash (images)    │ │ │ │ │ │ │   │ │  z=30
│  │  │  │  │  │  │  │  │  ┌─────────────────┐ │ │ │ │ │ │ │   │ │
│  │  │  │  │  │  │  │  │  │  LockCard       │ │ │ │ │ │ │ │   │ │  z=20
│  │  │  │  │  │  │  │  │  │  ┌─────────────┐ │ │ │ │ │ │ │ │   │ │
│  │  │  │  │  │  │  │  │  │  │  Video Frame│ │ │ │ │ │ │ │ │   │ │  z=10
│  │  │  │  │  │  │  │  │  │  └─────────────┘ │ │ │ │ │ │ │ │   │ │
│  │  │  │  │  │  │  │  │  └─────────────────┘ │ │ │ │ │ │ │   │ │
│  │  │  │  │  │  │  │  └─────────────────────┘ │ │ │ │ │ │   │ │
│  │  │  │  │  │  │  └─────────────────────────┘ │ │ │ │ │   │ │
│  │  │  │  │  │  └─────────────────────────────────┘ │ │ │ │   │ │
│  │  │  │  │  └─────────────────────────────────────┘ │ │ │ │   │ │
│  │  │  │  └─────────────────────────────────────────┘ │ │ │   │ │
│  │  │  └─────────────────────────────────────────────┘ │ │   │ │
│  │  └───────────────────────────────────────────────────┘   │ │
│  └─────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
```

## Architecture Components

### 1. `CompositorWindow` (Avalonia `Window`)
- One `Window` per monitor (multi-monitor support via `IEnumerable<CompositorWindow>`)
- `WindowDecorations.None`, `Topmost=true`, `ShowInTaskbar=false`, `ShowActivated=false`
- `TransparencyLevelHint` NOT needed — single surface, no window transparency
- Background: solid black (video covers it)
- Content: one `Image` control with `Stretch.UniformToFill`
- The `Image.Source` is a `WriteableBitmap` that the compositor writes to each frame

### 2. `CompositorEngine` (core service, lives in `CCP.Avalonia`)
- Owns the `WriteableBitmap` and `Image` control
- Runs one `DispatcherTimer` at 60Hz (16ms interval)
- Each tick: `RenderFrame()` → acquires `SKSurface` → calls each layer's `Render(SKCanvas)` → blits final `SKImage` to `WriteableBitmap` → `InvalidateVisual()` on the `Image`
- Manages `ILayer` instances in a `SortedList<int, ILayer>` keyed by z-index

### 3. `ILayer` interface (portable, `CCP.Core`)
```csharp
public interface ILayer
{
    int ZIndex { get; }
    bool IsActive { get; }
    void Render(SKCanvas canvas, PixelRect bounds, TimeSpan deltaTime);
    void OnActivated();
    void OnDeactivated();
}
```

### 4. Layer implementations (in `CCP.Avalonia`)
| Layer | Z | Description |
|---|---|---|
| `VideoLayer` | 10 | Receives decoded video frames (from future decoder or LibVLC interim) |
| `LockCardLayer` | 20 | Renders lock card image/text |
| `FlashLayer` | 30 | Renders flash images (bitmap blits, optional GIF via `AvaloniaAnimatedGif`) |
| `SubliminalLayer` | 40 | Renders centered text with configurable font/color |
| `BubbleLayer` | 45 | Renders clickable bubble rectangles with text/emoji; handles hit testing |
| `BouncingTextLayer` | 50 | Renders drifting text blocks with position animation |
| `BrainDrainLayer` | 55 | Renders blur/gradient shader via `SKShader` |
| `SpiralLayer` | 60 | Renders pre-rendered spiral frames or animated GIF |
| `PinkTintLayer` | 70 | Fills entire surface with configurable pink color + opacity |

### 5. Video decoder pipeline (phased — see Phase 3)
- Interim: Keep LibVLC but render its frames into the `VideoLayer` via `WriteableBitmap` (use `port-video-render` skill)
- Target: Replace LibVLC with cross-platform decoder (FFmpeg / MediaFoundation / AVFoundation / VAAPI) feeding raw `SKImage` frames into `VideoLayer`

### 6. Audio pipeline (phased — see Phase 4)
- Interim: Keep existing audio (NAudio / Avalonia audio service)
- Target: Unified audio mixer synchronized to compositor frame clock (optional, advanced)

## Phased Implementation Plan

### Phase 1: Compositor Shell (Session 1-2)

**Goal**: Create `CompositorWindow` and `CompositorEngine` with the layer infrastructure. No video/audio yet. One placeholder layer that fills the screen with a color to prove the pipeline works.

**Files to create:**
- `CCP.Avalonia/Compositor/CompositorWindow.axaml` — single `Image` control
- `CCP.Avalonia/Compositor/CompositorWindow.axaml.cs` — window setup, multi-monitor placement
- `CCP.Avalonia/Compositor/CompositorEngine.cs` — 60Hz timer, `WriteableBitmap` management, `SKSurface` acquisition
- `CCP.Avalonia/Compositor/CompositorLayers.cs` — z-index constants
- `CCP.Core/Services/Compositor/ILayer.cs` — portable layer interface
- `CCP.Avalonia/Compositor/Layers/BaseLayer.cs` — abstract base with common helpers (bounds, opacity, timer)
- `CCP.Avalonia/Compositor/Layers/PlaceholderLayer.cs` — fills screen with test color, proves pipeline

**Files to modify:**
- `CCP.Avalonia/ServiceCollectionExtensions.cs` — register `CompositorEngine` as singleton
- `CCP.Avalonia/App.axaml.cs` (or `Program.cs`) — start `CompositorEngine` on app launch, create one `CompositorWindow` per monitor

**Validation:**
- Build succeeds
- Run app: see a full-screen colored window on each monitor (no flickering, no separate windows)
- `CompositorWindow` is `Topmost` and covers the screen without any other windows visible

**Do NOT:**
- Remove any existing overlay services yet
- Integrate video or audio
- Worry about click-through or input handling

---

### Phase 2: Layer Adapters — Migrate Existing Services (Sessions 2-5)

**Goal**: Port each existing overlay service to push into the `CompositorEngine` instead of creating its own `Window`. Each service creates its `ILayer` and registers it with the engine. Existing `Window`-based code stays but is deprecated (not deleted yet).

**Order of migration (bottom-up, by z-index):**

#### 2a. Pink Tint Layer (Session 2)
- `PinkTintLayer` implements `ILayer`
- `AvaloniaOverlayService` (pink tint path) registers/unregisters `PinkTintLayer` with `CompositorEngine` instead of creating `OverlayWindow`
- `PinkTintLayer.Render()` fills canvas with configured color at configured opacity

#### 2b. Spiral + Brain Drain Layers (Session 2-3)
- `SpiralLayer` renders pre-loaded spiral frames (GIF or pre-decoded `SKImage` sequence)
- `BrainDrainLayer` renders a blur shader using `SKShader` or pre-computed texture
- `AvaloniaOverlayService` registers both layers instead of creating `SpiralOverlayWindow` and `BrainDrainOverlayWindow`

#### 2c. Bouncing Text Layer (Session 3)
- `BouncingTextLayer` manages a list of `BouncingTextItem` objects with position, velocity, text, color
- `AvaloniaBouncingTextService` registers the layer and adds/removes items instead of creating `BouncingTextWindow`
- Position animation runs in `Update()` (called by compositor timer), not in a separate `DispatcherTimer`

#### 2d. Subliminal Layer (Session 3)
- `SubliminalLayer` renders centered text with configurable font size, color, background transparency
- `AvaloniaSubliminalService` registers the layer and triggers text display instead of creating `SubliminalWindow`

#### 2e. Flash Layer (Session 4)
- `FlashLayer` renders a list of `FlashItem` objects with bitmap, position, opacity, lifetime
- `AvaloniaFlashService` registers the layer and spawns items instead of creating `FlashOverlayWindow`
- Supports click-through detection: `FlashLayer.HitTest(x, y)` returns the clicked item
- GIF support via `AvaloniaAnimatedGif` or direct `SKCodec` frame decoding into `SKImage`

#### 2f. Lock Card Layer (Session 4)
- `LockCardLayer` renders lock card image + text
- Triggered by session engine or lock service

#### 2g. Video Layer placeholder (Session 4)
- `VideoLayer` exists but renders a black screen (video integration in Phase 3)
- `AvaloniaVideoService` registers the layer

**Files to modify per service:**
- `Services/Overlays/AvaloniaOverlayService.cs`
- `Services/BouncingText/AvaloniaBouncingTextService.cs`
- `Services/Subliminal/AvaloniaSubliminalService.cs`
- `Services/Flash/AvaloniaFlashService.cs`
- `Services/Video/AvaloniaVideoService.cs` (placeholder registration only)

**Validation:**
- Each service works end-to-end when triggered manually
- No separate overlay windows appear (only `CompositorWindow` is visible)
- Layers stack correctly: pink tint on top, spiral below it, etc.

---

### Phase 3: Video Decoder Integration (Sessions 5-8)

**Goal**: Replace `LibVLC VideoView` with a frame source that feeds into `VideoLayer`. This is the most complex phase.

**Interim approach (recommended for Phase 3a):**
- Keep `LibVLC` for **decoding only** (no `VideoView`)
- Use `LibVLC` memory callbacks (`SetVideoCallbacks` with `LockCallback`/`DisplayCallback`) to get raw `RV32` frames
- Copy frames into `VideoLayer`'s `SKImage` or `WriteableBitmap` via `Buffer.MemoryCopy`
- See `port-video-render` skill for exact `WriteableBitmap` blitting pattern
- `VideoLayer` is now a first-class citizen of the compositor

**Target approach (Phase 3b, future milestone):**
- Replace LibVLC with a cross-platform decoder:
  - Windows: MediaFoundation via `FFmpeg.AutoGen` or `MediaPlayer` COM
  - macOS: `AVFoundation` via `AVPlayer` + `MTLTexture`
  - Linux: `VAAPI` + `FFmpeg`
- Decode into `SKImage` directly (zero-copy where possible)
- Synchronize audio to compositor frame clock

**Files to create:**
- `CCP.Avalonia/Compositor/Layers/VideoLayer.cs` — receives `SKImage` frames, renders at z=10
- `CCP.Avalonia/Compositor/VideoFrameQueue.cs` — thread-safe queue for decoded frames (producer/consumer)
- `CCP.Avalonia/Compositor/VideoDecoder.cs` (interim) — LibVLC memory callback wrapper

**Files to modify:**
- `Services/Video/AvaloniaVideoService.cs` — stop creating `VideoOverlayWindow`; instead start `VideoDecoder` and feed frames to `VideoLayer`
- `CCP.Avalonia/ServiceCollectionExtensions.cs` — register `VideoDecoder`

**Validation:**
- Video plays full-screen within `CompositorWindow` at z=10
- All overlay layers render correctly on top of video (no airspace problems)
- Flash, subliminal, bouncing text appear over video seamlessly
- `AvaloniaVideoService` events (`VideoAboutToStart`, `VideoStarted`, `VideoEnded`) still fire

**See also:** `port-video-render` skill for the `WriteableBitmap` frame blitting pattern.

---

### Phase 4: Chaos Overlay Integration (Sessions 8-10)

**Goal**: Migrate all Chaos system overlays from separate `Window` instances into `CompositorEngine` layers.

**Chaos layers to migrate:**
- `ChaosFlashOverlay` → `ChaosFlashLayer` (z=30, but within Chaos subsystem)
- `ChaosBubbleHostOverlay` → `ChaosBubbleLayer` (z=45, but within Chaos subsystem)
- `ChaosFieldFxOverlay` → `ChaosFieldFxLayer` (custom shader/animation)
- `ChaosSkiaFxOverlay` → `ChaosSkiaFxLayer` (already uses Skia, easiest migration)
- `ChaosCursorGlowOverlay` → `ChaosCursorGlowLayer`
- `ChaosDvdOverlay` → `ChaosDvdLayer`
- `ChaosVibeTrailOverlay` → `ChaosVibeTrailLayer`
- `ChaosEStimOverlay` → `ChaosEStimLayer`
- `ChaosAnnouncerOverlay` → `ChaosAnnouncerLayer`
- `ChaosBoonBarOverlay` → `ChaosBoonBarLayer`
- `ChaosWaveTimerOverlay` → `ChaosWaveTimerLayer`
- `ChaosEffectBannerOverlay` → `ChaosEffectBannerLayer`
- `ChaosUnlockCardOverlay` → `ChaosUnlockCardLayer`
- `ChaosGifCascadeOverlay` → `ChaosGifCascadeLayer`

**Key insight:** Many Chaos overlays already use `SKCanvas` or `WriteableBitmap`. Moving them into the compositor means:
- Their `Render()` methods write to the shared `SKCanvas` instead of their own surface
- Their animation state updates happen in `Update(deltaTime)` instead of separate timers
- Click/tap handling is delegated to the `CompositorWindow` which routes to the active layer

**Files to create:**
- `CCP.Avalonia/Compositor/Layers/Chaos/` — one file per Chaos layer

**Files to modify:**
- `Chaos/AvaloniaChaosService.cs` — register Chaos layers with `CompositorEngine` instead of creating separate windows
- `Chaos/ChaosHubWindow.axaml.cs` — update any references to Chaos overlay windows

**Validation:**
- Full Chaos descent runs with all visual effects in the compositor
- No separate Chaos windows appear
- Frame rate stays stable during heavy Chaos activity (many bubbles, effects, cascades)

---

### Phase 5: Audio Pipeline (Optional, Sessions 10-12)

**Goal**: Synchronize audio playback with the compositor frame clock, or at least ensure audio doesn't drift.

**Interim:** Keep existing audio services (NAudio, Avalonia audio). No changes needed.

**Target:** If drift is observed:
- `IAudioPlayer` interface already exists in `CCP.Core/Platform/`
- Implement a sample-accurate audio mixer that runs on its own high-priority thread
- Provide `AudioMixer.GetCurrentPresentationTime()` for A/V sync
- `VideoLayer` uses presentation time to select which frame to display

**This is advanced and should only be tackled if Phase 3b (custom decoder) is also implemented.**

---

### Phase 6: Cleanup (Sessions 12-13)

**Goal**: Remove all old separate-window overlay code once the compositor is fully operational.

**Files to delete (in `CCP.Avalonia`):**
- `Services/Video/AvaloniaVideoService.cs` — `VideoOverlayWindow` class
- `Services/Flash/AvaloniaFlashService.cs` — `FlashOverlayWindow` class
- `Services/Subliminal/AvaloniaSubliminalService.cs` — `SubliminalWindow` class, P/Invoke `SetWindowPos`
- `Services/BouncingText/AvaloniaBouncingTextService.cs` — `BouncingTextWindow` class
- `Services/Overlays/AvaloniaOverlayService.cs` — `OverlayWindow`, `BrainDrainOverlayWindow`, `SpiralOverlayWindow` classes, P/Invoke
- `Chaos/ChaosFlashOverlay.axaml` + `.cs`
- `Chaos/ChaosBubbleHostOverlay.cs`
- `Chaos/ChaosFieldFxOverlay.axaml` + `.cs`
- `Chaos/ChaosSkiaFxOverlay.cs`
- `Chaos/ChaosCursorGlowOverlay.axaml` + `.cs`
- `Chaos/ChaosDvdOverlay.axaml` + `.cs`
- `Chaos/ChaosDvdHostOverlay.cs`
- `Chaos/ChaosEStimOverlay.axaml` + `.cs`
- `Chaos/ChaosEStimGlowOverlay.axaml` + `.cs`
- `Chaos/ChaosVibeTrailOverlay.axaml` + `.cs`
- `Chaos/ChaosAnnouncerOverlay.axaml` + `.cs`
- `Chaos/ChaosBoonBarOverlay.axaml` + `.cs`
- `Chaos/ChaosWaveTimerOverlay.axaml` + `.cs`
- `Chaos/ChaosEffectBannerOverlay.axaml` + `.cs`
- `Chaos/ChaosUnlockCardOverlay.axaml` + `.cs`
- `Chaos/ChaosGifCascadeOverlay.axaml` + `.cs`
- `Platform/AvaloniaOverlaySurface.cs` — if no longer needed

**Files to modify:**
- Remove `using` directives for deleted types
- Update `ServiceCollectionExtensions.cs` to remove registrations for deleted services
- Ensure all references to old window classes are gone

**Validation:**
- `dotnet build` succeeds with 0 errors
- No compile-time references to old window classes
- Runtime: all features work through compositor only

---

### Phase 7: Performance Optimization (Sessions 13-15)

**Goal**: Ensure 60fps under all load conditions.

**Techniques:**
- **Dirty rectangles**: Only re-render layers that changed since last frame. `ILayer` exposes `bool NeedsRedraw` and `Rect? DirtyRect`.
- **Layer culling**: If a layer is fully opaque, layers below it don't need to render.
- **Pre-rendered caches**: Brain drain blur, spiral frames, pink tint are pre-rendered to `SKImage` and blitted.
- **GPU rendering**: Use `SKImage` backed by GPU texture instead of CPU `WriteableBitmap` (requires Skia GPU backend).
- **Frame skipping**: If a frame takes >16ms, skip the next update to catch up.

**Files to modify:**
- `CompositorEngine.cs` — add dirty rectangle tracking, culling, frame skip
- Each layer — add `NeedsRedraw` and `DirtyRect` support

**Validation:**
- Profiling: `CompositorEngine` renders at 60fps with 9 layers active + video + 20 Chaos bubbles
- CPU usage < 15% on a modern desktop CPU
- No frame drops during Chaos descents

---

## Implementation Patterns

### Compositor Frame Loop (Core Pattern)

```csharp
// In CompositorEngine, 60Hz DispatcherTimer tick
private void OnFrameTick(object? sender, EventArgs e)
{
    var now = DateTime.UtcNow;
    var delta = now - _lastFrame;
    _lastFrame = now;

    // 1. Update all layers (animations, timers, state transitions)
    foreach (var layer in _layers.Values)
        if (layer.IsActive)
            layer.Update(delta);

    // 2. Render to off-screen SKSurface
    using var surface = _skSurface;
    var canvas = surface.Canvas;
    canvas.Clear(SKColors.Black);

    foreach (var layer in _layers.Values)
    {
        if (!layer.IsActive) continue;
        canvas.Save();
        layer.Render(canvas, _bounds, delta);
        canvas.Restore();
    }

    // 3. Blit to WriteableBitmap (or present directly if GPU)
    using var image = surface.Snapshot();
    // ... copy to _writeableBitmap ...

    // 4. Invalidate the Image control
    _image.InvalidateVisual();
}
```

### Video Layer Frame Receipt (LibVLC Interim)

```csharp
public class VideoLayer : ILayer
{
    private SKImage? _currentFrame;
    private readonly object _frameLock = new();

    public int ZIndex => CompositorLayers.Video;
    public bool IsActive => _currentFrame != null;

    // Called by VideoDecoder on a background thread when a new frame is ready
    public void PresentFrame(IntPtr frameBuffer, int width, int height, int stride)
    {
        unsafe
        {
            var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            using var pixmap = new SKPixmap(info, frameBuffer, stride);
            var newImage = SKImage.FromPixmap(pixmap);
            lock (_frameLock)
            {
                _currentFrame?.Dispose();
                _currentFrame = newImage;
            }
        }
    }

    public void Render(SKCanvas canvas, PixelRect bounds, TimeSpan deltaTime)
    {
        lock (_frameLock)
        {
            if (_currentFrame != null)
            {
                canvas.DrawImage(_currentFrame, bounds.ToSKRect());
            }
        }
    }
}
```

> **Note:** The above uses `SKImage.FromPixmap` which may not be zero-copy. For zero-copy, use `GRContext` and `SKImage.FromTexture` with GPU textures. See `port-video-render` skill for the `WriteableBitmap` interop pattern if staying on CPU path.

### Flash Layer (Click-Through Hit Testing)

```csharp
public class FlashLayer : ILayer
{
    private readonly List<FlashItem> _items = new();

    public int ZIndex => CompositorLayers.Flash;
    public bool IsActive => _items.Count > 0;

    public void Spawn(LoadedImageData image, PixelRect position, int lifetimeMs)
    {
        _items.Add(new FlashItem(image, position, lifetimeMs));
    }

    public void Render(SKCanvas canvas, PixelRect bounds, TimeSpan deltaTime)
    {
        foreach (var item in _items)
        {
            if (item.IsExpired) continue;
            using var paint = new SKPaint { Color = new SKColor(255, 255, 255, (byte)(item.Opacity * 255)) };
            canvas.DrawImage(item.Image, item.Position.ToSKRect(), paint);
        }
    }

    public bool HitTest(PixelPoint point, out FlashItem? hitItem)
    {
        // Iterate in reverse (topmost first)
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            if (_items[i].Position.Contains(point))
            {
                hitItem = _items[i];
                return true;
            }
        }
        hitItem = null;
        return false;
    }
}
```

## Critical Rules

1. **Never create a separate `Window` for any layer.** All rendering happens inside `CompositorWindow`.
2. **Never use `Topmost` on any layer.** Z-order is controlled by `CompositorLayers` constants and the `SortedList` in `CompositorEngine`.
3. **Never use P/Invoke (`SetWindowPos`, `GetWindowLong`, `SetWindowLong`) for z-ordering.** Delete all P/Invoke from overlay services.
4. **One invalidation per frame.** The `CompositorEngine` timer is the only thing that calls `InvalidateVisual()`. Individual layers do NOT invalidate themselves.
5. **Thread safety:** Decoded video frames arrive on a background thread. Use `lock` or `ConcurrentQueue` when passing frames to the UI thread. Never touch `SKCanvas` from a background thread.
6. **Preserve WPF head.** Do not delete or modify WPF overlay code. The compositor is Avalonia-only.
7. **Layer state is service-owned.** Services (Flash, Video, Subliminal, etc.) own the state. The `ILayer` is a thin rendering adapter. The service tells the layer "show this text" or "spawn this image"; the layer only knows how to render it.

## Validation Per Phase

Run these checks after every phase:

```bash
# Build desktop heads + Core + tests
dotnet build ConditioningControlPanel/CCP.Desktop.slnf -c Debug

# Run Core tests
dotnet test ConditioningControlPanel/tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release

# Windows Avalonia smoke test
dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj -- --smoke-test
```

For Phase 3+ (video), also verify:
- Video playback is smooth at 60fps
- All overlay types render correctly over video
- No airspace artifacts (black rectangles, flickering)
- Audio stays synchronized with video (±50ms acceptable)

## Report Template

After each phase, report:

- **Phase:** which phase was completed
- **Files created:** list of new files
- **Files modified:** list of changed files
- **Files deleted:** list of removed files (if any)
- **Validation:** build/test/smoke results
- **Behavioral differences:** any intentional changes from old multi-window behavior
- **Known gaps:** what remains for next phase
- **Performance:** rough fps / CPU measurement if available

## Constraints

- Do not modify the WPF head (`ConditioningControlPanel/` without `CCP.Avalonia` or `CCP.Core` prefix).
- Do not broaden webcam tracking privacy contract. Camera frames must never be written to disk or sent over network.
- Do not weaken deeper-enhancement validation (NaN, Infinity, UNC paths, control characters, bounds).
- Do not accept UNC or extended-length paths for `--play`/`--edit` CLI arguments.
- Preserve security and privacy behavior exactly unless explicitly asked to change it.
- If LibVLC is used as interim decoder, do not remove `LibVLCSharp` package until Phase 3b is complete.

## References

- `port-video-render` skill — for the `WriteableBitmap` frame blitting pattern (Step 1-8 migration blueprint)
- `port-function` skill — for general WPF→Avalonia feature porting workflow
- `AvaloniaAnimatedGif` helper — for GIF frame decoding into `SKImage` or `WriteableBitmap`
- `AvaloniaChaosWindowZ` — old z-ordering code to be deleted (reference for understanding current behavior, not to copy)

## Example Invocation

> "Build the unified compositor engine for CCP Avalonia. Start with Phase 1: the compositor shell and placeholder layer."

This creates `CompositorWindow`, `CompositorEngine`, and a single `PlaceholderLayer` that proves the 60Hz render loop works across all monitors.

> "Migrate the pink tint and spiral overlays to the compositor. Phase 2a and 2b."

This creates `PinkTintLayer` and `SpiralLayer`, modifies `AvaloniaOverlayService` to register them with `CompositorEngine`, and removes the old `OverlayWindow` and `SpiralOverlayWindow` classes.

> "Integrate video into the compositor using LibVLC memory callbacks. Phase 3a."

This creates `VideoLayer` and `VideoDecoder`, modifies `AvaloniaVideoService` to feed decoded frames into the compositor instead of creating a `VideoOverlayWindow`, and uses the `port-video-render` skill for the `WriteableBitmap` interop pattern.
