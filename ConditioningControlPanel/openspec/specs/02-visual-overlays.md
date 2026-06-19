# Cluster 02 — Visual Effects, Overlays & On-Screen Rendering

Generated 2026-06-15. Classifies every capability in the **visual effects / overlays / on-screen
rendering** cluster of the Conditioning Control Panel (WPF + WinForms, .NET 8, Windows-only).
Excludes the Chaos *game logic* — only the Chaos rendering/overlay surfaces are covered here.

**Classification key:** PORTABLE / OS-SPECIFIC / MIXED per `openspec/PORTABILITY_RUBRIC.md`.

## Cluster-wide observation

The dominant pattern across this cluster is a **borderless, transparent, click-through, topmost
WPF `Window`** positioned per-monitor and stamped with Win32 ex-styles
(`WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`) via `user32`
`GetWindowLong/SetWindowLong/SetWindowPos`, with `System.Windows.Forms.Screen` enumeration and
`shcore`/`dwmapi`/`gdi32` for DPI + capture + composition. **The on-screen rendering is
irreducibly OS-specific.** What *is* portable in many of these services is the **effect
orchestration core**: scheduling, fade/pulse/physics math, delta-time motion, anti-exploit XP
gating, state machines, frame-step bookkeeping, and pool management. Those cores are written in
plain C# but currently live inline in the same files as the WPF window code; portability requires
extracting an `IOverlaySurface` / renderer shim and a screen/DPI provider behind an interface.

---

## Capability: Flash Image Display
**Files:** Services/FlashService.cs, FlashWindow (internal)
**Class:** MIXED
**Blocking deps:** WPF (`Window`, `Image`, `DropShadowEffect`, `BitmapImage`/WIC, `CompositionTarget.Rendering`, `Storyboard`/`DoubleAnimation`); `System.Drawing`/GDI+ for GIF frame decode; `System.Windows.Forms.Screen`; Win32 topmost via `ForceTopmost`; layered window pool.
**Seam (if MIXED):** A `IFlashSurface` (show image at geometry, set opacity, set frame, close) + an `IImageDecoder` (path → frame list, currently split WIC for stills / GDI+ for GIF) + an `IScreenProvider`. The scheduler (`ScheduleNextFlash` freq→interval with ±30% variance), the per-window lifetime/fade heartbeat (delta-time `FADE_PER_SEC`), the hydra multiplication tree, the LRU decode cache (`MAX_IMAGE_CACHE_BYTES`), lucky-flash/XP math, and the concurrency cap are all portable domain logic.

### Requirement: The system SHALL display flash images on a schedule.
The system SHALL pick random images from the configured assets folder and show them as transient on-screen windows at a frequency derived from the user's flashes-per-hour setting (±30% variance, ≥3s floor).

#### Scenario: Scheduled flash fires
- WHEN the service is running and the scheduler timer ticks while not busy
- THEN one flash event loads N images, displays each in its own window with an independent lifetime, fades them in to `FlashOpacity`, animates GIF frames, then fades them out.

#### Scenario: Clicked flash multiplies (hydra)
- WHEN Corruption Mode is on and the user clicks (or gaze-pops) a live flash window
- THEN that window closes and up to 2 child windows spawn (generation+1), with linked or independent timing and reduced XP per generation, capped by `HydraLimit`/`MAX_CONCURRENT_FLASH`.

#### Scenario: Decode is bounded
- WHEN an image is loaded
- THEN it is decoded no larger than the performance-tier/`ImageScale` cap and cached in an LRU keyed by path+cap, evicting once entry-count or byte-cap limits are exceeded.

---

## Capability: Subliminal Text Flash
**Files:** Services/SubliminalService.cs
**Class:** MIXED
**Blocking deps:** WPF (`Window`, `Grid`/`Canvas`/`TextBlock`, `Storyboard`); per-screen keep-alive layered windows; Win32 `user32` `GetWindowLong/SetWindowLong/SetWindowPos`, `SetWindowDisplayAffinity` (capture affinity), `MonitorFromPoint` + `shcore` `GetDpiForMonitor`; `System.Windows.Forms.Screen`; NAudio whisper playback.
**Seam (if MIXED):** An `ISubliminalSurface` (set text/colors, animate opacity hold) + `IScreenProvider`. The text selection from the active pool, frequency scheduling (msgs/min ±30%), Bambi Freeze/Reset chaining + deferred-reset gating, audio linking/normalization, and the OCR self-exclusion rect math are portable. The full-screen keep-alive window, ex-style stamping, and `GetActiveTextScreenRects` physical-pixel projection are OS-specific.

### Requirement: The system SHALL flash subliminal text across monitors.
The system SHALL display short outlined-text messages from the active subliminal pool, optionally with a linked whisper audio clip and haptic anticipation, fading in/hold/out over a configurable duration.

#### Scenario: Subliminal with linked audio
- WHEN a subliminal fires and a matching audio file exists and whispers are enabled
- THEN other audio is ducked, the whisper plays, a haptic pattern triggers ~250ms before the visual, and the text fades in on each monitor's keep-alive window.

#### Scenario: Own-text OCR exclusion
- WHEN a subliminal is on screen (opacity > 0)
- THEN its physical-pixel text rect is reported so the avatar's awareness OCR skips the app's own words.

---

## Capability: Screen Overlays — Pink Filter & Spiral
**Files:** Services/OverlayService.cs
**Class:** MIXED (rendering OS-SPECIFIC; sync/ramp/topmost-reassert logic portable)
**Blocking deps:** WPF (`Window`, `Border`, `Image`, `MediaElement` for video spiral, `BlurEffect`, GIF frame decode via `System.Drawing`); Win32 `user32`/`gdi32`/`dwmapi` P/Invoke (`SetWindowLong`, `SetWindowPos`, `DwmExtendFrameIntoClientArea`, `SetWindowCompositionAttribute` acrylic/blur, `BitBlt`/`StretchBlt` for capture); `System.Windows.Forms.Screen`.
**Seam (if MIXED):** `IOverlaySurface` per kind (pink fill brush, spiral frame, blur radius) + `IScreenProvider`. The 500ms settings-sync loop, opacity-ramp override bookkeeping (`_rampPinkOpacity`/`_rampSpiralOpacity`), pulse-then-restore, ad-hoc/sustained Deeper overlay API, GIF frame caching/warming, and z-order loss counting are portable; everything that touches an `hwnd` or a `Window` is OS-specific.

### Requirement: The system SHALL render full-screen pink filter and spiral overlays.
The system SHALL show topmost click-through overlay windows on each (optionally dual) monitor at configurable opacity, kept above other topmost windows via periodic z-order re-assertion.

#### Scenario: Spiral plays as GIF or video
- WHEN the configured spiral path is a GIF
- THEN frames are decoded once (cached) and cycled on a render-priority timer at 90%-reduced opacity; WHEN it is a video, a looping muted `MediaElement` is used instead.

#### Scenario: Deeper opacity ramp owns the overlay
- WHEN a Deeper enhancement sets a sustained ramp opacity
- THEN the 500ms settings-sync leaves that overlay's opacity untouched until the ramp clears.

---

## Capability: Brain Drain Blur (screen-capture blur)
**Files:** Services/OverlayService.cs (BrainDrain region), Services/BrainDrainService.cs (audio only)
**Class:** OS-SPECIFIC (blur); PORTABLE (the audio-trigger service)
**Blocking deps (blur):** Win32 `gdi32` `BitBlt`/`StretchBlt`/`CreateCompatibleBitmap`/`CreateCompatibleDC` + `user32` `GetDC` desktop capture; WPF `BlurEffect`; `System.Drawing.Bitmap` downscale; per-monitor capture windows; `System.Windows.Forms.Screen`.
**Blocking deps (audio service):** none — `BrainDrainService.cs` is NAudio + timer + probability math.
**Seam:** The visual Brain Drain (continuous desktop BitBlt → downscale → WPF blur → upscale) is fundamentally a Windows screen-grab; not portable without a platform capture API. `BrainDrainService.cs` (random audio playback gated by intensity/probability) is portable except for the NAudio output device.

### Requirement: The system SHALL blur a downscaled live capture of the desktop.
The system SHALL periodically BitBlt each monitor at a tier-selected downscale factor, apply a proportional blur, and upscale it over the screen, refreshing at a tier-capped FPS.

#### Scenario: Tier-gated capture rate
- WHEN Brain Drain runs under load
- THEN the capture FPS and downscale factor are taken from the active performance tier so cost stays bounded while the blur masks the lower rate.

---

## Capability: Screen Shake
**Files:** Services/ScreenShakeService.cs
**Class:** MIXED
**Blocking deps:** WPF (`Application.Current.Windows`, `RenderTransform`/`TranslateTransform`, `VisualTreeHelper`, `DispatcherTimer`); WebView2 (`Microsoft.Web.WebView2.Wpf.WebView2`) CSS-injection path for fullscreen embedded pages.
**Seam (if MIXED):** The single-flight jitter loop (symmetric random offset in [-amp, amp], delta-stable, self-expiring) is portable math; applying it via WPF `RenderTransform` and via WebView2 `ExecuteScriptAsync` CSS transforms is OS/host-specific.

### Requirement: The system SHALL jitter all visible windows together for a duration.
The system SHALL apply one shared random offset per tick to every visible top-level window's content (and inject an equivalent CSS shake into any visible WebView2) for the requested intensity and duration, then restore prior transforms.

#### Scenario: Fullscreen WebView2 also shakes
- WHEN a Deeper player/editor is fullscreen and its content is a WebView2
- THEN a self-expiring CSS `transform` shake is injected into the page so the otherwise-WPF-invisible Chromium surface shakes too.

---

## Capability: Bouncing Text (DVD screensaver)
**Files:** Services/BouncingTextService.cs, BouncingTextWindow (internal)
**Class:** MIXED
**Blocking deps:** WPF (`Window`, `TextBlock`/`Canvas`, `FormattedText` measure, `CompositionTarget.Rendering`, `DropShadowEffect`); Win32 `user32` click-through + topmost; `System.Drawing.Graphics.FromHwnd` DPI; `System.Windows.Forms.Screen`; checks `App.Video.IsPlaying`.
**Seam (if MIXED):** The DVD bounce physics (delta-time velocity, wall reflection, exact + near-corner detection with tolerance), XP rate-limiting (per-minute cap + cooldown), color/text rotation, and OCR self-exclusion rect are portable; text measurement, the per-screen transparent windows, and z-order re-assertion are WPF/Win32.

### Requirement: The system SHALL bounce a text string across all monitors.
The system SHALL move a colored text string across the virtual desktop at a settings-derived speed, reflecting off edges, awarding XP on bounces (rate-limited), and firing a corner-hit event on (near-)corner collisions.

#### Scenario: Corner hit
- WHEN both axes reflect on the same frame (or the text is within corner tolerance during a single-axis bounce)
- THEN a corner-hit achievement is tracked and `OnCornerHit` fires.

#### Scenario: Hidden during mandatory video
- WHEN a mandatory video is playing
- THEN the bouncing-text windows hide and re-show when it ends.

---

## Capability: Mantra Session (logic) + Mantra Window (surface)
**Files:** Services/MantraService.cs, MantraWindow.xaml/.cs
**Class:** MantraService = PORTABLE; MantraWindow = OS-SPECIFIC
**Blocking deps:** MantraService — none (pure C#: streak/anti-cheat/XP state machine, `Stopwatch`, random no-repeat selection). MantraWindow — WPF `Window` (presentation only, no P/Invoke).
**Seam:** Already clean — the session logic is fully decoupled from the WPF window. Only the on-screen card needs a re-platform.

### Requirement: The system SHALL run an anti-cheat mantra repetition session.
The system SHALL present mantras from a pool with no immediate repeats, accept completions only after a 1.5s minimum and within a 20/min cap, track streak/best-streak, and award scaled XP until the target rep count completes.

#### Scenario: Too-fast completion rejected
- WHEN a completion is attempted < 1.5s after the current mantra started, or the per-minute cap is reached
- THEN the completion is rejected (no streak/XP change).

---

## Capability: Keyword Highlight (OCR match overlay)
**Files:** Services/KeywordHighlightService.cs
**Class:** OS-SPECIFIC (consumes OCR + draws on-screen)
**Blocking deps:** WPF (`Window`, `Canvas`, `Rectangle` shape, `DoubleAnimation`); Win32 `user32` `SetWindowLong`/`SetWindowPos`/`SetWindowDisplayAffinity` (capture exclusion), `MonitorFromPoint`+`GetMonitorInfo`, `shcore` `GetDpiForMonitor`; `System.Windows.Forms.Screen`; input is `OcrWordHit` screen rects (itself OS-specific upstream).
**Seam:** The DPI-independent rect→canvas projection math is portable, but the capability is inherently about painting a rectangle over a live OCR'd word on the physical desktop — no useful portable core.

### Requirement: The system SHALL highlight on-screen words matched by OCR.
The system SHALL draw a fading rounded outline over each matched word's screen rect on a per-monitor click-through overlay, optionally excluded from screen capture.

#### Scenario: Highlight fades out
- WHEN a match is shown
- THEN it holds at full opacity for 60% of the configured duration, then fades over the remainder and is removed.

---

## Capability: Outlined Text primitive
**Files:** OutlinedText.cs
**Class:** OS-SPECIFIC (WPF visual primitive)
**Blocking deps:** WPF (`FrameworkElement`, `FormattedText.BuildGeometry`, `DrawingContext`, `Pen`/`Geometry`, `VisualTreeHelper.GetDpi`).
**Seam:** None worth extracting — it is a thin WPF `OnRender` stroke-then-fill text element reused by the Chaos overlays.

### Requirement: The system SHALL render readable outlined text over arbitrary backgrounds.
The system SHALL draw a single line of text as a true geometry outline (doubled stroke under fill) sized to the text, suitable for legibility over any live-desktop content.

---

## Capability: Avatar Random Bubble
**Files:** AvatarRandomBubble.cs
**Class:** MIXED
**Blocking deps:** WPF (`Window`, `Image`, `Grid`, transforms, `DispatcherTimer` ~20 FPS); Win32 `user32` hide-from-alt-tab; `System.Drawing.Graphics` DPI; `ModResourceResolver` for sprite.
**Seam (if MIXED):** The float/wobble/pop animation math (4 wobble types, scale/fade lifecycle, off-screen despawn) is portable; the per-bubble layered clickable window is OS-specific.

### Requirement: The system SHALL float a clickable bubble up from the avatar.
The system SHALL spawn a bubble sprite near the avatar that drifts upward with a wobble pattern, is clickable to pop (invoking a callback), and self-destroys on pop-fade or when off the top of the screen.

---

## Capability: Mantra / Pink-Rush popup surfaces
**Files:** MantraWindow.xaml/.cs, PinkRushPopup.xaml/.cs, TutorialOverlay.xaml
**Class:** OS-SPECIFIC (pure WPF presentation, no P/Invoke)
**Blocking deps:** WPF `Window` + XAML.
**Seam:** None — these are view layers; their backing state (e.g. MantraService) is already separate.

### Requirement: The system SHALL present modal/popup effect surfaces.
The system SHALL display the mantra entry card, the Pink Rush reward popup, and the tutorial overlay as styled WPF windows driven by their respective logic services.

---

## Capability: Chaos rendering overlays (visual surfaces only)
**Files:** ChaosAnnouncerOverlay.cs, ChaosCursorGlowOverlay.cs, ChaosDvdOverlay.cs, ChaosEStimOverlay.cs, ChaosEffectBannerOverlay.cs, ChaosFieldFxOverlay.cs, ChaosFlashOverlay.cs, ChaosFxWindow.cs, ChaosGifCascadeOverlay.cs, ChaosPopText.cs, ChaosToyButtonWindow.cs, ChaosVibeTrailOverlay.cs, ChaosWaveTimerOverlay.cs, ChaosBackdropService.cs, ChaosOverlayWindow.xaml/.cs, ChaosHudWindow.xaml/.cs
**Class:** MIXED (each surface is OS-SPECIFIC WPF; each has a separable portable core)
**Blocking deps:** Every file is a WPF `Window` with `user32` ex-style/topmost P/Invoke (`GetWindowLong`/`SetWindowLong`/`SetWindowPos`); rendering via `RadialGradientBrush`, `DropShadowEffect`, `Canvas`/`Polyline`, `CompositionTarget.Rendering`, `DoubleAnimation(UsingKeyFrames)`, `OutlinedText`. Additional: `XamlAnimatedGif` (ChaosFlashOverlay, ChaosGifCascadeOverlay) GIF decode; `user32` `GetCursorPos` (ChaosEStimOverlay, ChaosVibeTrailOverlay); `SetWindowsHookEx` global mouse/keyboard low-level hooks (ChaosOverlayWindow countdown skip). ChaosBackdropService is intentionally non-topmost (z-order design). HUD/Overlay windows are XAML + data-bound.
**Seam (if MIXED):** Per file, the timing/scheduling/physics/state-machine cores are plain C# and extractable behind an `IOverlaySurface`/renderer interface: announcer/banner/poptext fade timing queues (IN/HOLD/OUT ms); DVD + gif-cascade motion physics (bounce angle, gravity, collision, despawn) on a delta-time clock; field-fx ripple expansion + tether distance; cursor-follow + emit-distance trail sampling; toy-button cooldown/ready/active state machine; wave timer countdown/score; backdrop depth→asset lookup; overlay-window countdown + boon-draft reveal sequencing; HUD prerun/pause/live state + combo-tier coloring. External deps `XamlAnimatedGif` and the global-hook countdown skip have cross-platform substitutes (ImageSharp; platform input hooks).

### Requirement: The system SHALL render Chaos-run visual effect surfaces.
The system SHALL present full-screen and HUD overlay surfaces for a Chaos run — announcer flashes, effect banners, floating pop-text, DVD/gif-cascade motion layers, e-stim arcs, cursor glow/vibe trails, field FX, wave timer, toy button, per-zone backdrop, the modal countdown/boon-draft/recap window, and the live HUD — layered above the bubble field.

#### Scenario: Topmost re-assertion above the bubble field
- WHEN the Chaos run re-raises its bubble windows
- THEN keep-alive overlay surfaces re-assert their topmost band so effect chrome stays above bubbles while the backdrop stays beneath them.

#### Scenario: Countdown can be skipped
- WHEN the modal Chaos countdown is showing
- THEN a global low-level mouse/keyboard hook lets the user skip it (a Windows-only mechanism that needs a platform input shim off-Windows).

---

## Cluster portability summary

| Capability | Class | Blocking dep | ~LOC |
|---|---|---|---|
| Flash Image Display | MIXED | WPF window/pool, WIC+GDI+ decode, layered topmost | ~2460 |
| Subliminal Text Flash | MIXED | WPF keep-alive windows, user32 ex-style, capture affinity, DPI | ~1020 |
| Screen Overlays (Pink/Spiral) | MIXED | WPF windows, MediaElement, user32/dwmapi, GIF decode | ~900 (of 2183) |
| Brain Drain Blur | OS-SPECIFIC | gdi32 BitBlt desktop capture, WPF BlurEffect | ~700 (of 2183) |
| BrainDrainService (audio) | PORTABLE | none (NAudio device only) | ~247 |
| Screen Shake | MIXED | WPF RenderTransform, WebView2 CSS inject | ~265 |
| Bouncing Text | MIXED | WPF windows, FormattedText, user32 topmost, DPI | ~653 |
| MantraService (logic) | PORTABLE | none | ~131 |
| Mantra/PinkRush/Tutorial windows | OS-SPECIFIC | WPF Window/XAML (no P/Invoke) | ~300 |
| Keyword Highlight | OS-SPECIFIC | WPF overlay, user32+shcore, capture affinity | ~509 |
| Outlined Text primitive | OS-SPECIFIC | WPF FormattedText/DrawingContext | ~56 |
| Avatar Random Bubble | MIXED | WPF layered window, user32 alt-tab hide | ~295 |
| Chaos rendering overlays (16 files) | MIXED | WPF windows, user32 ex-style/hooks/cursor, XamlAnimatedGif | ~5885 |

**Verdict (~25-30% portable by LOC).** This cluster is, at the surface, overwhelmingly Windows-bound:
the on-screen rendering is WPF + `user32`/`gdi32`/`dwmapi`/`shcore` P/Invoke + `System.Windows.Forms`
screen enumeration + DPI, and a couple of hard-Windows capabilities (Brain Drain desktop BitBlt
capture, Keyword Highlight OCR painting, the Chaos global input hooks) have essentially no portable
core. **But the cluster is mostly MIXED, not monolithically OS-specific**: almost every overlay
service interleaves a genuinely portable orchestration core — scheduling and frequency math,
delta-time motion/physics, fade/pulse/ripple curves, anti-exploit XP gating, LRU decode caches,
pool/lifetime bookkeeping, and state machines — with the WPF window it drives. Two capabilities
(`MantraService`, `BrainDrainService` audio) are already fully PORTABLE. Realistically ~25-30% of
the cluster's LOC is portable today, and that share rises substantially (toward ~45-50%) if an
`IOverlaySurface` + `IScreenProvider` + `IImageDecoder` seam is extracted, leaving the Win32/WPF
window plumbing as the only piece needing a per-platform implementation.
