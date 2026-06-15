# Chaos Mode — Narrative Layer + Zone Backdrops: Findings & Build Plan

> Exploration findings (2026-06-14) for adding (1) a reactive narrator/voiceline layer
> (the "Madam", Hades-style) and (2) per-zone backdrop plates to Chaos Mode.
> Companion to `CHAOS_DESIGN.md`. Line numbers are approximate — verify before editing.

## TL;DR

- Both features feasible, low-to-medium risk; most plumbing already exists.
- **Trigger bus already exists** — extend the `ChaosLessonHooks` direct-call pattern, do
  NOT build a parallel bus.
- **Persistence already fits** — reuse `ChaosMetaState`/`chaos_meta.json`, add ~3 fields,
  no migration needed (additive, lenient load). Bump `SchemaVersion` to 2.
- **Voice delivery is the biggest gap** — no priority queue, no per-line cooldown, no
  ducking that unifies voiced clip + text. Bark system has the *pieces* to copy.
- **Backdrops** — CONFIRMED by the Step-1 spike: bubbles/FX/HUD/overlay are each their own
  **Topmost** window (`BubbleService.Bubble` pools `Topmost` windows; not a main-window Canvas).
  So "under the bubbles" is a topmost-band z-order question. A **non-topmost** dedicated
  fullscreen window sits deterministically below all of them — implemented as
  `ChaosBackdropService` (semi-opaque, **click-absorbing** per user direction), art via
  `ChaosArt`, swapped on the act/depth border.
- **Zones map 1:1 to the existing 5 depths.** No separate band.

## A. In-run event surface (trigger bus)

**Verdict: EXTEND the existing hook pattern.** No central bus; `ChaosModeService` makes
one-line direct calls into static dispatch (`ChaosLessonHooks`, `App.Bark?.NotifyChaos*`,
`ChaosFirstTimes`, announcer overlays). Wiring entry point: `ChaosModeService.BeginRun()`
(~296-319) registers bubble-event delegates, torn down in `EndRun()`. Existing events:
`ChaosLessons.LessonCompleted`, `ChaosFirstTimes.Awarded`.

| Moment | Fires at (~) | Exposure | Consumers |
|---|---|---|---|
| Run start | `BeginRun()` 296 | direct | Bark |
| Run end | `EndRun()` 2792 | direct | LessonHooks, Meta, Bark, Reveal |
| Wave advance (zone border) | `BeginWaveTransition()` | direct | LessonHooks, Bubbles, Bark |
| Act/Depth change | `FireActChangedIfCrossed()` 2710 | edge-detected direct | Bark, Announcer |
| First treat / defuse / golden / tease-denied | OnBenignPopped/OnDefused/OnTeaseDenied | FirstTimes.TryAward + Bark | Meta, Bark |
| First darter | `OnDarterCaught()` 1963 | inline + Bark | Bark |
| Combo 25/50/100 | `CheckComboMilestone()` 2664 | direct | Bark |
| Channel started/broken | CanChannelDefuse 1654 / OnChannelBroken 1664 | hook + Bark | LessonHooks, Bark |
| Brink defuse (<0.8s) | `OnDefused()` 1701 | OnDefuseCompleted | LessonHooks, Bark |
| Detonation bare/absorbed/collar | `OnDetonated()` 1835/1803/1815 | NotifyChaos* | Bark |
| Draft opened / sin accepted / skipped | BeginWaveTransition / OnBoonChosen 1264/1309 | hook + Bark | LessonHooks, Bark |
| Reveal flips | RevealService.Sync/MarkSeen | event Action<string> | hub UI |

**Gaps (emit nothing today):** boss spawn/during/win (confirm a boss concept exists vs.
just depth V), brink-*window-entry* (only completion fires), first-defuse-*attempt*,
per-point combo ticks.

## B. Voice/line delivery — have vs missing

`ChaosTips` is **text-only** (hover tooltips), not audio.

**Exists (reusable):**
- `ChaosSfx` — one-shot NAudio `WaveOutEvent`, fallback path resolution, master-volume. No queue/cooldown/dedupe/ducking.
- `ChaosAnnouncerOverlay` — upper-third text, **FIFO queue**, kept-alive window, fade in/dwell/out. No priority/dedupe/audio.
- `ChaosEffectBannerOverlay` / `ChaosPopText` — effect labels / pooled floating text. Text only.
- **`BarkService`** — priority rule selection, per-rule cooldown (`CooldownMs`), global min-gap (4000ms), per-rule + global no-repeat (`_usedVariantKeys`, `_recentlySpoken`), **3-tier audio resolver** (`ResolveBarkAudio`: packaged mod → embedded mod → shared), mp3-or-TTS+text speak path. `BubbleService` has a 4-deep `WaveOutEvent` device pool worth copying.

**Missing (build):**
- Unified cue model (text + audio + priority + cooldown + lineId + lastFired).
- Priority queue with cooldown gate for a separate narrator track.
- **Audio ducking / "narrator is speaking" gate** — NO mixer exists; all `WaveOutEvent`s overlap. Biggest net-new piece.
- Dedicated narrator overlay (or upgrade `ChaosAnnouncerOverlay` FIFO → band priority).

## C. Persistence / once-flags / accretion

**Verdict: reuse `ChaosMetaState` + `ChaosMetaStore` (`chaos_meta.json`). No migration required.**
- `ChaosMetaState` is explicitly additive-only; carries `SeenReveals`/`PendingReveals`,
  `DiscoveredCodexIds`, `FirstTimesAwarded`, `LessonsComplete`, `LastRankSeen`,
  `SchemaVersion` (=1, declared but unused).
- `ChaosMetaStore`: Newtonsoft, lenient load (missing→defaults, never throws), atomic save (`.tmp`→move).
- `ChaosRevealService` is the exact hidden→pending→flash→seen "seen once ever" machine; codex `MarkDiscovered`/`IsDiscovered` keyed-set is the model.

Add: `HashSet<string> SeenNarrativeLines`, `Dictionary<string,long> NarrativeCooldownEnds`
(unix ms — the one new shape), optional `Dictionary<string,int> NarrativeUnlockRanks`.
Bump `SchemaVersion` to 2.

## D. Backdrop compositing (highest risk)

- `ChaosOverlayWindow`: `WindowStyle=None`, `AllowsTransparency=True`, `Background=Transparent`, `Topmost`. Click-through is **not** opacity-driven — it's `WS_EX_TRANSPARENT | WS_EX_NOACTIVATE` toggled in `ApplyExStyles()` (~890): on during countdown/play, off during draft/results. Already has a hidden `<Rectangle x:Name="Backdrop" Opacity=0.72 Collapsed/>`.
- Click-through math: `GlobalMouseHook` (sync low-level hook) on `WM_RBUTTONDOWN` calls `RightDown?.Invoke(point)` → return 1 to swallow / `CallNextHookEx` to pass; reads `BubbleService.ChaosBubbleCentersSnapshot` (immutable `Point[]`, physical px, atomic reassign). Backdrop must not touch this.
- **CONFIRMED (Step-1 spike + code):** bubbles are NOT in a main-window Canvas — each `Bubble` rents its own `Topmost` `AllowsTransparency` window (`BubbleService` pool, `WINDOW_POOL_MAX=64`). FX/HUD/overlay are likewise topmost. So a backdrop must be **non-topmost** to sit under them; that is deterministic (no z-order bookkeeping, no RaiseAboveVideo fights). Built as `ChaosBackdropService` (non-topmost, click-absorbing — NOT click-through, since it's the play surface).
- Opacity is cosmetic only; never changes hit-testing. Semi-opaque (~40-50%) preserves desktop-bleed premise.
- Art: `ChaosArt.TryLoad` already caches (`ConcurrentDictionary`, `BitmapCacheOption.OnLoad`, `Freeze()`, keyed by path) — cured the documented OOM. Backdrops → `assets/Chaos/backdrops/{id}.png` via `ChaosArt.Resolve("backdrops", id)`. Load at act border only, small working set, ~1920x1080 cap.
- Depth: `_state.WaveIndex`/`_state.ActIndex`, `ActIndex = 1 + (WaveIndex-1)/5`, HUD "DEPTH {roman}".

**Recommended (pending Step 1): Option A** — semi-opaque `<Image>` as the bottom child of
whatever layer the spike proves sits under the bubbles, source set by `ChaosBackdropService`
on the act border, art via `ChaosArt`, opt-in. Defer full-cover story window (Option B).

## E. Config / setup

`Models/AppSettings.cs` ~2045-2153 (`[JsonProperty]` + `INotifyPropertyChanged` + clamp +
auto-save). Add `NarrativeModeEnabled` (copy `ChaosModeEnabled`), `BackdropEnabled` (copy
`ChaosColorFlashesEnabled`), `BackdropOpacity` (copy `ChaosEffectIntensity`, clamp 0-1),
register params (string like `ChaosMotionMode`). Per-run config = `ChaosRunConfig`
(`ChaosModels.cs`); tuning constants → `ChaosTuning.cs`. UI wires via
`ChaosHubWindow.LoadFromSettings()` (~1498) / `SaveToSettings()` (~1586).

**Zone↔depth: 1:1 with the 5 depths.** Formula caps at V; ~36s/wave (180s/5), ~3min/depth.

**ChaosHappyPath collision:** mostly orthogonal. Run 1 stays at DEPTH I (zone-border
narrative naturally won't fire). Scripted beats run inside the same field-pause as reactive
drafts (no queue collision). **One guard:** run-4 draft rigging announces during the table
pause; gate narrative announce on not-paused/not-spawning (mirror lesson-card pause ~113).

## Minimal new surface

- `Services/Chaos/ChaosNarrativeHooks.cs` — static dispatch parallel to `ChaosLessonHooks`.
- `Services/Chaos/ChaosNarrativeDirector.cs` — priority queue + cooldown + register/gate eval + line catalog.
- Narrator cue model + line catalog (rank/depth/first-time/item/run-stat gates, registers).
- Narrator audio (reuse Bark resolver + device-pool play) + ducking/`IsPlaying` gate (net-new).
- Narrator text overlay (new, or `ChaosAnnouncerOverlay` FIFO→priority).
- `Services/Chaos/ChaosBackdropService.cs` + image layer + `ChaosArt` backdrop key.
- `ChaosMetaState` +3 fields, `SchemaVersion`→2.
- `AppSettings` + hub UI: 3-5 toggles/sliders.

## Risk list

1. Audio overlap/ducking — no mixer; design first.
2. Backdrop "under the bubbles" — bubbles in main Canvas, backdrop in overlay; **verify via Step 1 spike**.
3. Memory — large bitmaps; load at act border, small working set, respect OOM history.
4. Save migration — low; additive + lenient load + SchemaVersion bump.
5. ChaosHappyPath double-announce — gate on not-paused (run 4).
6. Click-through regression — never touch `WS_EX_TRANSPARENT` / `ChaosBubbleCentersSnapshot`.
7. Boss events may not exist — confirm before scripting boss lines.

## Vertical slice (build order)

0. **Step 1 GATE** — backdrop layering spike (throwaway image): prove it renders UNDER the
   bubbles on a real screen; report which window/layer; STOP for go/no-go before art/feature.
1. Cue schema + director rules (see build brief).
2. Ducking design (report the "bed" in code, then minimal build).
3. `ChaosNarrativeHooks` + 5 moments (run_start, zone_border, first_bare_deto,
   brink_defuse, sin_accepted), gated not-paused/not-spawning.
4. `ChaosNarrativeDirector` + ~3 placeholder lines/moment.
5. Playback via Bark resolver + pooled `WaveOutEvent` + `IsPlaying` ducking; text via
   `ChaosAnnouncerOverlay` (FIFO→band priority).
6. `ChaosBackdropService` swapping ONE test backdrop on DEPTH I→II, layer per Step 1.
7. Settings: `NarrativeModeEnabled` + `BackdropEnabled` + `BackdropOpacity`, default on, in hub.
