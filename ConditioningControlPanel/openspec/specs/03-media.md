# Cluster 03 — MEDIA: Video & Audio Playback

Generated 2026-06-15. Cluster recon of the CCP media subsystem: video playback
(LibVLCSharp), system-audio ducking and SFX (NAudio/WASAPI), the audio-synced
haptics pipeline (FFT analysis), the bubble-count video minigame, and the
ancillary player windows (mini-player, help-video).

## Cluster verdict (summary)

The media cluster splits cleanly along a **decode/orchestrate vs. render/sink**
seam:

- **Decode + orchestration is largely portable.** LibVLCSharp.**Shared** (the
  `LibVLC` / `MediaPlayer` / `Media` core) is genuinely cross-platform, as are the
  FFT haptic analyzer, the metadata cache, the HTTP video downloader, and the
  playlist/queue/attention-check state machines.
- **Every actual output sink is Windows-bound.** Video is rendered through
  `LibVLCSharp.WPF.VideoView` (a WPF `HwndHost`); audio playback uses NAudio
  `WaveOutEvent` (WinMM) and audio *ducking* uses NAudio.CoreAudioApi
  (**WASAPI** session enumeration), both Windows-only. Window placement uses
  WinForms `Screen` and `user32!SetWindowPos`. Audio extraction for haptics uses
  `MediaFoundationReader` (Windows Media Foundation).

So nothing in this cluster runs unchanged off-Windows today, but the rendering
surfaces are isolated enough that a Linux/macOS re-platform would re-skin the
sinks rather than rewrite the logic.

---

## Capability: Mandatory Video Playback (VideoService)
**Files:** Services/VideoService.cs (~3309 LOC)
**Class:** MIXED
**Blocking deps:** `LibVLCSharp.WPF.VideoView` (WPF HwndHost), WPF `Window`/`Dispatcher`,
WinForms `Screen` (multi-monitor layout), `System.Windows.Input` key capture, `user32` interop.
**Seam (if MIXED):** Extract an `IVideoSurface` (attach/detach a `MediaPlayer`, set
bounds on a screen) + `IScreenLayout` (enumerate displays). The LibVLC `MediaPlayer`
lifecycle, the playlist `Queue<string>`, attention-check timers, segment-arming, and
duration logic are all portable; only the on-screen surface and screen enumeration bind to Windows.

### Requirement: The system SHALL play a mandatory video to completion with attention checks.
The system SHALL pull from a shuffled video queue, render full-screen on one or more
monitors, fire `VideoAboutToStart`/`VideoStarted`/`VideoEnded`, enforce an attention
mini-game (floating targets), and apply a max-duration safety fallback if `LengthChanged` never fires.

#### Scenario: Trigger a video
- WHEN `TriggerVideo` is called with a non-empty queue
- THEN one LibVLC `MediaPlayer` is created per target monitor, the audio-bearing window
  is marked primary (`PrimaryMediaPlayer`/`PrimaryVideoWindow`), and playback begins.

#### Scenario: Chaos random-segment
- WHEN `ArmRandomSegment(segmentSec)` is called before `TriggerVideo`
- THEN the next video seeks to a shared random fraction leaving ≥ `segmentSec` of runway,
  keeping dual-monitor mirrors in sync.

#### Scenario: Codec-independent decode
- WHEN a video uses a codec the OS MediaElement can't decode
- THEN LibVLC decodes it regardless (codec warning shown at most once per session).

---

## Capability: Dual-Monitor Mirrored Video (DualMonitorVideoService)
**Files:** Services/DualMonitorVideoService.cs (~628 LOC)
**Class:** MIXED
**Blocking deps:** WPF `WriteableBitmap`/`Image`/`Window`, `user32!SetWindowPos`,
`kernel32!RtlMoveMemory` (CopyMemory), WinForms `Screen`.
**Seam (if MIXED):** The single-decoder/shared-frame-buffer design is the portable core
(LibVLC memory-render callbacks → one `IntPtr` frame buffer). Replace the WPF
`WriteableBitmap` blit + `SetWindowPos` topmost placement with a platform surface
to port. LibVLC `MediaPlayer` + frame-buffer marshalling are portable.

### Requirement: The system SHALL mirror one decoded video stream across all monitors.
The system SHALL use a single LibVLC decoder rendering into one shared memory buffer that
each monitor's `WriteableBitmap` copies, to keep CPU/GPU/audio to a single track.

#### Scenario: Play across N monitors
- WHEN `Play(url, width, height)` is called
- THEN one decoder fills a shared frame buffer and each monitor window blits the same frame; one audio track plays.

---

## Capability: System Audio Ducking + SFX Playback (AudioService)
**Files:** Services/AudioService.cs (~1254 LOC)
**Class:** OS-SPECIFIC
**Blocking deps:** NAudio `WaveOutEvent` (WinMM `waveOutOpen`), **NAudio.CoreAudioApi**
(`MMDeviceEnumerator`, `AudioSessionManager`, `SimpleAudioVolume`, `AudioEndpointVolume`)
= **WASAPI** per-session volume control, `System.Diagnostics.Process` for PID→app matching.
**Seam (if MIXED):** Not meaningfully mixed — ducking *is* per-app session volume
manipulation, which has no cross-platform equivalent. The reference-counted duck/unduck
bookkeeping and device-fallback policy are portable wrappers around an irreducibly
Windows mechanism. A port would stub ducking to a no-op (or pipe-to-PulseAudio shim) and
re-target SFX to a portable output (e.g. an `IAudioOutput`).

### Requirement: The system SHALL duck other apps' audio during media events and restore it after.
The system SHALL reference-count duck requests, enumerate active render sessions (rescanning
to catch late-arriving sessions), lower their `SimpleAudioVolume` to the duck amount, and restore
original volumes on the last release — with a watchdog force-unduck and a pending-restore retry
window for sessions that were silent at unduck time.

#### Scenario: Duck during a flash/video
- WHEN `Duck()` is called and ducking is enabled
- THEN all active render sessions except this app are reduced to the duck level; a periodic
  rescan ducks sessions that start mid-window.

#### Scenario: Restore on completion
- WHEN the last `Unduck()` releases the ref-count
- THEN original per-session volumes are restored; PIDs that are gone are queued for retry within a 3-minute window.

#### Scenario: WaveOut device fallback (bug #181)
- WHEN the default `WAVE_MAPPER` returns `BadDeviceId`
- THEN the service scans explicit device numbers, honoring `Settings.AudioOutputDeviceId`, and caches the first that opens.

---

## Capability: Calibration Sound Cues (CalibrationSoundService)
**Files:** Services/CalibrationSoundService.cs (~79 LOC)
**Class:** OS-SPECIFIC
**Blocking deps:** NAudio `WaveOutEvent` + `AudioFileReader` (WinMM output).
**Seam (if MIXED):** Trivial — fire-and-forget mp3 playback with a master-volume curve.
The volume math + asset resolution are portable; only the `WaveOutEvent` sink binds to Windows.
Swapping in an `IAudioOutput` abstraction makes it portable.

### Requirement: The system SHALL play short audio cues for each webcam-calibration moment.
The system SHALL map each calibration step to one asset + volume multiplier, apply a 1.5-power
volume curve over master volume, and play it fire-and-forget through the user's preferred device.

#### Scenario: Calibration verified
- WHEN `CalibrationVerified()` is called
- THEN `result.mp3` plays at 0.6 × curved master volume and self-disposes.

---

## Capability: Audio-Synced Haptics Orchestration (AudioSyncService)
**Files:** Services/AudioSyncService.cs (~380 LOC)
**Class:** PORTABLE
**Blocking deps:** none (coordinates `ChunkManager` + `HapticService` over events; uses
`System.Windows` only for the `Application.Current.Dispatcher` marshal — easily abstracted).
**Seam (if MIXED):** n/a — orchestration/state-machine over portable inputs.

### Requirement: The system SHALL drive haptic intensity in sync with web-video playback.
The system SHALL detect the playing video URL, progressively process audio in chunks, force a
resync every 5 seconds, pause/resume the haptic stream when waiting on a chunk, and surface
processing progress/errors via events.

#### Scenario: Progressive chunk loading
- WHEN playback reaches a not-yet-ready chunk
- THEN `ChunkLoadingRequired` fires (pause + loading UI) and `ChunkLoadingCompleted` fires when ready (resume).

---

## Capability: Audio Feature Analysis for Haptics (AudioAnalyzer)
**Files:** Services/Audio/AudioAnalyzer.cs (~430 LOC)
**Class:** PORTABLE
**Blocking deps:** none — pure `System.Numerics` FFT math.
**Seam (if MIXED):** n/a.

### Requirement: The system SHALL extract per-frame intensity features from PCM audio.
The system SHALL run a windowed FFT (size 2048, hop 512 @ 44.1 kHz), compute RMS / bass / mid /
high-band energy, perform spectral-flux onset and bass-drop detection with adaptive baseline
subtraction, and normalize to an intensity stream (~86 fps).

#### Scenario: Transient detection
- WHEN spectral flux exceeds 2× the rolling average
- THEN a full-intensity transient pulse (~70 ms) is emitted.

---

## Capability: Chunked Audio Extraction & Processing (ChunkManager)
**Files:** Services/Audio/ChunkManager.cs (~495 LOC)
**Class:** MIXED
**Blocking deps:** NAudio `MediaFoundationReader` (Windows Media Foundation decode of the
downloaded video's audio track).
**Seam (if MIXED):** The chunk scheduling, state machine, and progressive download/analyze
pipeline are portable; only the decode step (`MediaFoundationReader`) is Windows-bound.
Replace with a portable decoder (FFmpeg/`LibVLC` audio render callback / `MediaFoundationReader`
→ `IAudioDecoder`) to port.

### Requirement: The system SHALL download a video once and analyze its audio in 5-minute chunks.
The system SHALL download the video via `VideoDownloader`, then extract & FFT-analyze its audio
progressively in chunks (NotStarted→Downloading→Extracting→Analyzing→Ready/Failed), exposing
per-chunk readiness to the orchestrator.

#### Scenario: Extract a chunk
- WHEN a chunk transitions to Extracting
- THEN `MediaFoundationReader` decodes that time range's PCM and `AudioAnalyzer` produces its intensity array.

---

## Capability: Video Download (VideoDownloader)
**Files:** Services/Audio/VideoDownloader.cs (~185 LOC)
**Class:** PORTABLE
**Blocking deps:** none — `System.Net.Http.HttpClient` with retry + progress reporting.
**Seam (if MIXED):** n/a.

### Requirement: The system SHALL download remote video files with progress and retry.
The system SHALL stream a remote video to disk in 80 KB buffers, report bytes/total via
`ProgressChanged`, retry up to 3 times, and support cancellation.

#### Scenario: Download with progress
- WHEN a download runs
- THEN `ProgressChanged` reports bytes downloaded and total (when Content-Length is known).

---

## Capability: Video Duration Metadata Cache (VideoMetadataCache)
**Files:** Services/VideoMetadataCache.cs (~155 LOC)
**Class:** PORTABLE
**Blocking deps:** none of consequence — uses LibVLCSharp.**Shared** (`Media.Parse`), which is
cross-platform, plus `System.IO` + Newtonsoft.Json. Keyed by path|size|mtime.
**Seam (if MIXED):** n/a (LibVLC.Shared core is portable; no WPF reference).

### Requirement: The system SHALL cache per-video durations on disk and fall open on misses.
The system SHALL key durations by path+size+mtime (fallback path+size), parse uncached durations
via LibVLC with a 5 s timeout, never throw (misses degrade to "unknown / include video"), and persist to JSON.

#### Scenario: Cache miss
- WHEN a video's duration is not cached
- THEN it is parsed once via `Media.Parse`, stored, and the cache marked dirty for save.

---

## Capability: Bubble-Count Video Minigame (BubbleCountService)
**Files:** Services/BubbleCountService.cs (~637 LOC), BubbleCountWindow.xaml(.cs) (~1224 LOC),
BubbleCountResultWindow.xaml(.cs), Features/BubbleCountFeatureControl.xaml(.cs)
**Class:** MIXED
**Blocking deps:** WPF `Window`/`Controls`/`Media`/`Dispatcher`, WinForms `Screen`,
NAudio `WaveOut` (pop SFX). Video shown via the same LibVLC `VideoView` surface.
**Seam (if MIXED):** The minigame's scoring/spawn/timer logic and count-validation are
portable; the full-screen WPF window, screen placement, and animated bubble visuals are the Windows-bound rendering layer.

### Requirement: The system SHALL run a count-the-bubbles video challenge and score the guess.
The system SHALL play a video full-screen, overlay animated bubbles, collect the user's count,
play pop SFX, and compare the guess to the true count for a pass/fail result.

#### Scenario: Submit count
- WHEN the user submits their bubble count
- THEN the result window reports correct/incorrect against the tracked spawn count.

---

## Capability: Mini-Player & Help-Video Windows
**Files:** MiniPlayerWindow.xaml(.cs) (~365 LOC), HelpVideoWindow.xaml(.cs) (~287 LOC)
**Class:** OS-SPECIFIC
**Blocking deps:** `LibVLCSharp.WPF.VideoView` (HwndHost), WPF `Window`, shared
`VideoService.SharedLibVLC`.
**Seam (if MIXED):** Thin view wrappers — they create a `VideoView`, attach a LibVLC
`MediaPlayer`, and loop. The play/loop logic is portable; the surface is not. They share the
single app-wide `LibVLC` instance and mutually close (only one help/mini player at a time).

### Requirement: The system SHALL present looping demo/help clips in a dedicated window.
The system SHALL create a `VideoView`, play a clip via the shared LibVLC, loop on end (off the
LibVLC thread), and fail soft (hide the surface) when LibVLC or the clip is unavailable.

#### Scenario: Help clip unavailable
- WHEN `SharedLibVLC` is null or the clip is missing
- THEN the video surface is hidden and the window continues without it.

---

## Shared infrastructure notes

- **LibVLC instance** — `VideoService.SharedLibVLC` is the single app-wide `LibVLC`
  (LibVLCSharp.Shared, portable). Every video surface (`VideoService`,
  `DualMonitorVideoService`, mini/help players, `VideoMetadataCache`) borrows it.
- **The portability line is the assembly split:** `LibVLCSharp.Shared` = portable core;
  `LibVLCSharp.WPF` = the only Windows-binding video reference. Memory-render mode
  (DualMonitor) bypasses `.WPF` but substitutes WPF `WriteableBitmap` + `user32`.
- **NAudio is wholly Windows-only here:** `WaveOutEvent` (WinMM), CoreAudioApi/WASAPI
  session ducking, and `MediaFoundationReader` decode all bind to Windows.
