# 07 — Sensors & System I/O

Scope: webcam / gaze tracking, haptics (Buttplug/Lovense), screen capture + OCR, display
topology, window/activity awareness, blink training, calibration audio.

Cluster summary: roughly **10,000 LOC**. The cluster splits cleanly along its I/O seams:
the *math and orchestration* (gaze filtering, calibration regression, haptic command
sequencing, activity classification by string) is portable C#; the *device acquisition and
screen access* layers (WinRT/DirectShow camera enumeration, GDI screen grab, WinRT OCR,
CCD display-topology, Win32 foreground-window/idle, WPF overlay rendering) are Windows-bound.

> **Key correction vs. brief:** the csproj references `SharpDX` / `SharpDX.DXGI` /
> `SharpDX.Direct3D11` "for Desktop Duplication", but **no `.cs` file references any SharpDX
> or DXGI Desktop Duplication type** — those packages are vestigial/dead. Screen capture is
> actually done with GDI `Graphics.CopyFromScreen` (`ScreenOcrService`), and "screen mirror"
> is `SetDisplayConfig` (the CCD topology API), **not** Desktop Duplication. Both are still
> Windows-only, just via different APIs than stated.

---

## Capability: Webcam Eye/Gaze Tracking Pipeline
**Files:** Services/WebcamTrackingService.cs (3028), Services/WebcamCalibrationData.cs (246)
**Class:** MIXED
**Blocking deps:** `OpenCvSharp4.runtime.win` (native MSMF/DirectShow capture backend);
`OpenCvSharp4.runtime.win` ships Windows-x64 native binaries even though `OpenCvSharp4` and
`Microsoft.ML.OnnxRuntime` are cross-platform managed APIs; `System.Windows.Forms.Screen`
(monitor bounds / `GetCalibratedScreen`); `System.Windows.Application.Current.Dispatcher`
(marshals gaze/blink/state events to the WPF UI thread); emits gaze points in WPF DIPs.
**Seam (if MIXED):** Three seams. (1) Frame source — wrap `VideoCapture` behind an
`IFrameSource` so the native backend can be swapped (`runtime.linux`/`runtime.osx` exist for
OpenCvSharp). (2) Output marshalling — replace `Dispatcher.BeginInvoke` (`Dispatch`) with a
`SynchronizationContext`/event abstraction. (3) Monitor geometry — replace `Forms.Screen`
with an `IScreenProvider`. The detection itself (BlazeFace + FaceMesh + Iris **ONNX** models,
EAR/MAR blink & mouth math, HSV tongue heuristic, SolvePnP head-pose, iris-vector extraction)
is fully portable — pure OpenCvSharp managed + ONNX Runtime.

### Requirement: Local offline eye/gaze tracking
The system SHALL detect face, eye-state (blink), mouth-open, gaze direction, and a calibrated
on-screen gaze point from a local webcam, running entirely offline, never persisting or
transmitting frames or per-frame derived values.

#### Scenario: Gaze stream drives Lab features
- WHEN a calibration exists and the camera is granted
- THEN the service SHALL emit a smoothed gaze point (WPF DIPs of the calibrated monitor) plus
  blink and face-lost events to subscribers (GazeFocusService, BlinkTrainerService, debug cursor)

#### Scenario: Privacy contract
- WHEN any frame is processed
- THEN it SHALL live only in RAM, be disposed after processing, and the only persisted artifact
  SHALL be the numbers-only calibration JSON; broadening observation SHALL bump `ConsentVersion`

---

## Capability: Gaze Calibration & Smoothing Math
**Files:** Services/WebcamTrackingService.cs (OneEuroFilter, polynomial/homography projection,
Cerrolaza fit), Services/WebcamCalibrationData.cs
**Class:** PORTABLE
**Blocking deps:** none — `OneEuroFilter` (Casiez 2012), 2nd-order Cerrolaza polynomial fit,
3×3 homography, iris-vector → screen mapping, percentile/hysteresis baselines are pure math
over `double[]`/`float[]`. Persisted via Newtonsoft JSON (portable).
**Seam (if MIXED):** n/a. (Note: `GetCalibratedScreen` and DIP conversion that *consume* this
math live in the MIXED pipeline above; the math types themselves are clean.)

### Requirement: Iris-to-screen projection
The system SHALL map a raw iris vector to a screen point via a calibrated polynomial (falling
back to homography on legacy calibrations) and SHALL smooth the result with chained One-Euro
filters in iris-space and screen-space.

#### Scenario: Edge accuracy
- WHEN the gaze approaches a screen edge
- THEN the Cerrolaza asymmetric polynomial SHALL be applied so edge/corner accuracy approximates
  center accuracy without runtime edge-pull heuristics

---

## Capability: Webcam Device Enumeration (DirectShow + WinRT fallback)
**Files:** Services/WebcamDeviceEnumerator.cs (138), Services/WebcamWinRtEnumerator.cs (61)
**Class:** OS-SPECIFIC
**Blocking deps:** DirectShow COM (`SystemDeviceEnum` / `VideoInputDeviceCategory` /
`IPropertyBag` via `ComImport` P/Invoke); WinRT `Windows.Devices.Enumeration.DeviceInformation`
(fallback for MF-only / 32-bit-filter cameras). Both are Windows Media Foundation / COM.
**Seam (if MIXED):** n/a — both implementations are wholly Windows; a portable build needs a
platform-specific enumerator (e.g. V4L2 on Linux) behind a shared `WebcamDevice` record + an
`IWebcamEnumerator` interface (the record itself is portable).

### Requirement: List capture devices with index/name
The system SHALL return ordered `(Index, Name)` capture devices, preferring DirectShow and
falling back to WinRT enumeration when DirectShow returns empty (64-bit / MF-only cameras).

#### Scenario: DirectShow empty
- WHEN DirectShow enumeration returns no devices but a camera is present
- THEN WinRT `FindAllAsync(VideoCapture)` SHALL be tried (5s timeout) and its devices returned

---

## Capability: Gaze-Reactive Lab Interactions (Focus / Dwell / Blink-pop)
**Files:** Services/GazeFocusService.cs (555), Services/GazeContentScreenPolicy.cs (26),
Services/GazeDebugCursorService.cs (213)
**Class:** MIXED
**Blocking deps:** WPF (`System.Windows.Point`, `DispatcherTimer`, `Shapes`/`Brush`/`Media`
for the debug cursor overlay, `Window.Left/Top` hit-testing); `System.Windows.Forms.Screen`
(GazeContentScreenPolicy screen resolution). Operates on WPF window rects.
**Seam (if MIXED):** The dwell/scoring logic is portable (Gaussian target scoring, sticky
bonus, cooldown, dwell timing over abstract rects). The seams are: WPF hit-test geometry
(needs an `IHitTarget` rect abstraction), `DispatcherTimer` (needs a portable timer), and the
debug cursor's WPF `Shape` rendering (UI-layer, replace per toolkit).

### Requirement: Dwell-to-activate targets
The system SHALL let the user pop bubbles / dismiss flashes by gazing at them ~1s (or blinking
near them), choosing the best target via Gaussian distance scoring with a sticky bias.

#### Scenario: Soft lock
- WHEN gaze jitters near a target boundary
- THEN the Gaussian falloff + sticky bonus SHALL prevent lock ping-pong between equidistant targets

---

## Capability: Blink Trainer Overlay
**Files:** Services/BlinkTrainerService.cs (734), Services/BlinkTrainerAssetPool.cs (104)
**Class:** MIXED
**Blocking deps:** WPF (transparent topmost click-through overlay windows, `DispatcherTimer`,
`BitmapImage`, `XamlAnimatedGif`, WPF `MediaElement` for video assets); subscribes to the
WebcamTrackingService blink stream. The asset-pool selection/shuffle (BlinkTrainerAssetPool)
is portable.
**Seam (if MIXED):** Overlay rendering is the toolkit-bound seam; the swap-on-blink scheduling
and pool management are portable behind an `IOverlaySurface`.

### Requirement: Swap overlay asset on blink
The system SHALL display a random image/GIF/video from a user pool on a click-through overlay
and swap to a new random asset on each blink, auto-stopping after a configured duration.

#### Scenario: Blink advances asset
- WHEN the webcam blink event fires while the trainer is active
- THEN a different random asset (avoiding immediate repeat) SHALL replace the current one

---

## Capability: Haptic Device Control (Buttplug / Lovense / Mock)
**Files:** Services/HapticService.cs (923), Services/Haptics/IHapticProvider.cs (37),
Services/Haptics/ButtplugProvider.cs (282), Services/Haptics/LovenseProvider.cs (355),
Services/Haptics/MockHapticProvider.cs (149)
**Class:** PORTABLE
**Blocking deps:** none. ButtplugProvider talks to Intiface over WebSocket (`Buttplug` 3.0.1,
cross-platform managed); LovenseProvider uses `HttpClient` against `127.0.0.1:20010` (Lovense
Connect/Remote HTTP API); coordination uses `INotifyPropertyChanged`, `System.Threading.Timer`,
events. No Windows APIs. (Latency tuning, ping-failure debouncing, event-type gating all pure.)
**Seam (if MIXED):** n/a.

### Requirement: Provider-abstracted haptic playback
The system SHALL drive vibration intensity/duration through a selected `IHapticProvider`,
debounce transient ping failures (≥3 consecutive before drop), and stop on feature/master toggle.

#### Scenario: Buttplug anticipation offset
- WHEN a haptic is requested for a subliminal under the Buttplug provider
- THEN the trigger SHALL be advanced ~1.3s to compensate for Intiface round-trip latency

---

## Capability: Screen Capture + OCR Keyword Detection
**Files:** Services/ScreenOcrService.cs (273)
**Class:** OS-SPECIFIC
**Blocking deps:** GDI `System.Drawing.Graphics.CopyFromScreen` (screen grab),
`System.Drawing.Bitmap`/`Imaging` (GDI+), WinRT `Windows.Media.Ocr.OcrEngine` +
`Windows.Graphics.Imaging.SoftwareBitmap` (the OCR engine itself), `System.Windows.Forms.Screen`
bounds, WPF `Dispatcher`. (No DXGI Desktop Duplication despite the SharpDX csproj refs.)
**Seam (if MIXED):** n/a as written — capture *and* OCR engine are both Windows. A portable
build would need both an `IScreenCapturer` and an `IOcrEngine` (e.g. Tesseract) behind
interfaces. The downstream keyword/self-exclusion logic (rect intersection vs. own windows,
confirmation-scan streaks) is portable but is a thin layer here.

### Requirement: Periodic on-screen keyword scanning
The system SHALL periodically capture all screens, OCR them to word+rect hits, exclude hits
inside the app's own windows, and forward survivors to the keyword-trigger evaluator.

#### Scenario: Self-exclusion
- WHEN OCR words fall inside a CCP-owned window rect and `AwarenessIgnoreOwnUi` is on
- THEN those words SHALL be dropped so the app cannot react to its own rendered text

---

## Capability: Display Topology / Screen Mirror
**Files:** Services/ScreenMirrorService.cs (133)
**Class:** OS-SPECIFIC
**Blocking deps:** Win32 `user32!SetDisplayConfig` (CCD API) with `SDC_TOPOLOGY_CLONE/EXTEND`;
`System.Windows.Forms.Screen` for active-screen count. (This is the "mirror" service — it is
**display-mode switching, not frame duplication**; no SharpDX/DXGI involved.)
**Seam (if MIXED):** n/a — switching monitor topology is inherently OS-specific; no
cross-platform equivalent. Would be a no-op/stub on other OSes behind an `IDisplayTopology`.

### Requirement: Clone/extend during fullscreen video
The system SHALL switch monitors to CLONE during fullscreen playback (only when ≥2 active
screens) and restore EXTEND afterward / on dispose.

#### Scenario: Single-screen guard
- WHEN fewer than 2 active screens are present (projection "second screen only" etc.)
- THEN the service SHALL leave topology untouched rather than fight the user's projection setting

---

## Capability: Window / Activity Awareness
**Files:** Services/WindowAwarenessService.cs (740), Services/AppClusterMap.cs (136)
**Class:** MIXED
**Blocking deps:** Win32 `user32!GetForegroundWindow` + `GetWindowText` (the foreground-window
title source); `DispatcherTimer` polling. The classification engine — keyword→category
dictionaries, service/page parsing, cluster/app-id resolution (AppClusterMap, JSON-overridable,
longest-substring-wins) — is pure string processing and portable.
**Seam (if MIXED):** Extract an `IForegroundWindowProvider` returning the active window title;
everything downstream (ActivityCategory mapping, AppClusterMap) is already portable. Privacy
contract: titles are classified, never stored/logged.

### Requirement: Categorize foreground activity
The system SHALL poll the foreground window title, classify it into an ActivityCategory plus
fine-grained cluster/app id, and raise change/still-on events without persisting titles.

#### Scenario: Activity change event
- WHEN the foreground window switches to a different recognized service
- THEN an `ActivityChanged` event SHALL fire with category, service, page, and cluster/app id

---

## Capability: Idle / Input Activity Detection
**Files:** Services/ActivityTracker.cs (88)
**Class:** OS-SPECIFIC
**Blocking deps:** Win32 `user32!GetLastInputInfo` (system-wide last-input time); `DispatcherTimer`.
**Seam (if MIXED):** n/a — there is no portable system-wide idle API. Behind an `IIdleProvider`
the 3-min threshold / debounce logic is trivially portable, but the source call is Windows-only.

### Requirement: Report AFK state
The system SHALL detect when no keyboard/mouse input has occurred for ≥3 minutes and raise an
idle-state-changed event (used by anti-cheat to suppress passive XP).

#### Scenario: Tick-count wraparound
- WHEN `Environment.TickCount` has wrapped (~49-day uptime)
- THEN the idle computation SHALL correct for wraparound rather than report a negative interval

---

## Capability: Calibration Audio Cues
**Files:** Services/CalibrationSoundService.cs (79)
**Class:** PORTABLE
**Blocking deps:** none in domain terms — uses NAudio (`WaveOutEvent`/`AudioFileReader`).
NAudio's default output is WASAPI/WinMM (Windows), so this is effectively Windows-bound at the
NAudio layer, but contains no Win32/WPF of its own; classified PORTABLE per rubric (pure
fire-and-forget playback wrapper) with the NAudio output device as the only platform seam.
**Seam (if MIXED):** swap NAudio output device for a cross-platform backend (the file/volume
selection logic is fully portable).

### Requirement: Map calibration moments to cues
The system SHALL play a short (≤1.6s) self-disposing audio clip for each calibration moment at
a per-cue volume multiplier, fire-and-forget.

#### Scenario: Mid-flight cancel
- WHEN calibration is cancelled while a cue is playing
- THEN the in-flight clip SHALL finish in the background without blocking cancellation
