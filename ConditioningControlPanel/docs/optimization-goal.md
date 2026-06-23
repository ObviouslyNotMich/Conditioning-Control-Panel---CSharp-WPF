# Optimization Goal — Avalonia v12 Smoothness & Resource Reduction

Optimize the Avalonia v12 Conditioning Control Panel so it uses fewer resources and runs visibly smoother than it does now, while keeping full functional and visual parity with the WPF baseline.

## Done when

- Baseline measurements are recorded for Avalonia startup time, 10-second working set, effect/overlay FPS (spiral, flash, bubbles, video), and peak CPU/GPU during an active session + Chaos run.
- Startup time improves by at least **10%** and 10-second working set drops by at least **10%** versus baseline.
- Effect/overlay animations hold at least **60 fps** where feasible, with **30 fps** as the floor.
- Peak CPU/GPU during session/Chaos run is lower than baseline.
- A 3-minute max-intensity benchmark is defined and recorded where all effect modules run simultaneously (GIFs, images, full-screen video across all screens, pop-ups, spirals, chaos bubbles, flashes, subliminals, etc.); the run reports average/min FPS, peak working set, and peak CPU/GPU.
- `dotnet build CCP.Desktop.slnf -clp:ErrorsOnly`, `dotnet test tests/CCP.Core.Tests/CCP.Core.Tests.csproj`, and the Avalonia Windows head `--smoke-test` all stay green, and no parity-matrix item regresses.
- Any new dependency is cross-platform, permissively licensed, well-maintained, and recorded with the reason it was added plus before/after numbers.

## Scope

Focus on media/video/LibVLC, animations/overlays, and UI responsiveness/async in:

- `ConditioningControlPanel/CCP.Avalonia`
- `ConditioningControlPanel/CCP.Core`
- `ConditioningControlPanel/CCP.Avalonia.Desktop.Windows`

The agent may simplify/remove wrappers, pool/reuse resources, move work off the UI thread, and adopt new libraries that clearly earn their weight. Do not change WPF behavior or remove features.

## Loop

1. Measure baseline.
2. Identify the highest-impact bottleneck.
3. Implement the smallest change that helps.
4. Re-measure and rerun build/tests/smoke.
5. Record the result.
6. Repeat.

## Stop rule

If a metric cannot be improved after several attempts and the bottleneck is external/platform-limited, report the best measured result and why, then move on. Stay within roughly a **400M-token** budget and report progress before it is exhausted.
