# Chaos Mode — Design Reference

A roguelite minigame layered over the **live desktop**: effect bubbles drift over your
real screen (still clickable), each carrying a conditioning payload. Pop the good ones,
defuse the threats before their fuse runs out, draft boons between waves, and bank
**Sparks** toward permanent upgrades.

> Source of truth: `Services/Chaos/`. This doc is generated from that code — if a number
> here disagrees with the code, the code wins. Last synced against the freeze/darter rework.

---

## 1. Core gameplay loop

```
Lab ▸ START CHAOS
   │
   ├─ StartRun: build config (settings + owned upgrades), pause ambient bubbles,
   │            show HUD strip + overlay
   ├─ Countdown ........... 3 · 2 · 1 · GO   (click-through; desktop stays usable)
   │
   ├─ BeginRun (GO):
   │     • install bubble callbacks, warm spiral cache
   │     • apply equipped Loadout start-boon (before wave 1)
   │     • run timer @250ms + spawn timer @~800ms
   │
   ├─ RUN LOOP  (per wave, escalating with run progress)
   │     SpawnTick → pick a weighted variant + roll a darter → spawn over desktop
   │     You interact:
   │        ○ benign bubble  → pop = treat (fires payload) + score + combo + heat
   │        ✔ live bubble    → click in time = DEFUSE (reward, no payload)
   │        💥 live bubble    → fuse-out / leaves screen = DETONATE (payload fires)
   │        ⏳ darter         → catch = SLOW-MO power-up
   │        ❄ freeze bubble  → catch = FREEZE power-up (good)
   │
   ├─ WAVE BOUNDARY
   │     pop the field (+ wave-clear sound) → BOON DRAFT (1-of-N, may include a curse)
   │     pick one → apply → next wave   (skip a draft = +1 shield)
   │
   └─ RUN END (timer elapses)
         XP payout (capped) + Sparks meta payout → RESULTS → resume ambient bubble game
```

**Escalation.** `RunIntensity = elapsed / duration` (0→1) drives everything:

| Knob | Formula |
|------|---------|
| Spawn interval | `(1300 − intensity·850) / difficultyMult` ms (min 280; ÷ slow-mo factor while slowed) |
| Max concurrent | `round((4 + intensity·7)·√difficultyMult) + maxBubblesBonus` |
| Bubble size | random in the variant band, nudged up by intensity |
| Live fuse length | `baseFuse · (1 − intensity·0.25) · fuseTimeMult` (min 1200 ms) |

**Acts/waves.** `WaveIndex` advances on `elapsed / (duration / waveCount)`. `ActIndex = 1 + (wave−1)/5` — every 5 waves is a new Act (shown in roman numerals on the HUD).

---

## 2. Scoring & the multiplier stack

**Base points per bubble:** `BasePoints(strength) = 40 + strength·1.6` → **40–200** (strength 0–100 scales with bubble size).

| Action | Award |
|--------|-------|
| Benign pop | `BasePoints · 0.4 · TotalMult` (×0.6 with *Golden Touch*) |
| Defuse | `BasePoints · 1.0 · TotalMult` |
| Darter catch | `(120 + 90 if quick) · TotalMult` |
| Freeze catch | `140 · TotalMult` |

**TotalMult = BaseMult × ComboMult × DifficultyMult × HeatMult × BoonMult × (Double-or-Nothing ? ×2 : 1)**

| Factor | Value |
|--------|-------|
| BaseMult | 1.0 (→ 1.2 with *Base Mult* upgrade) |
| ComboMult | `min(1 + combo·0.08, 6.0)` |
| DifficultyMult | Easy 1.0 · Medium 1.3 · Hard 1.7 · Extreme 2.2 |
| HeatMult | `1 + heat` (up to ×2 at full heat) |
| BoonMult | `1 + Σ(boon RunMultBonus)` |

**Combo** +1 per pop/defuse/catch; **resets to 0** on an unshielded detonation. Milestone fanfare every 10.

**Heat** (0–1, chaos-local): +0.04 benign, +0.07 defuse, +0.05 darter/freeze; −0.2 on a shielded detonation, **→0** on an unshielded one; passive decay −0.0015/tick.

---

## 3. Shields & detonation

- **Shields** start at `StartingShields` (default 3; settings 0–5; +1 per *Start Shield* upgrade). Shown as ♥/♡.
- A **detonation** costs **1 shield** (**2** during *Double or Nothing*):
  - **Absorbed** (shield available): heat −0.2, blue pulse + light shake. Combo kept.
  - **No shield**: combo & heat reset to 0, red pulse + hard shake, payload fires.
- **Skipping a boon draft** grants **+1 shield**.

---

## 4. In-run power-ups (special "good" bubbles)

| Power-up | Trigger | Effect |
|----------|---------|--------|
| **Slow-mo** | catch a **Darter** (fast bouncing pink orb; 3 bounces then flees) | Time scale **0.20× for 5s** — bubbles, fuses, spawn cadence all slow; on-screen payloads linger (×5). Refreshes on re-catch. |
| **Freeze** | catch a **Freeze** bubble (❄) | Field **holds 3.5s**: motion + fuses paused, spawns halt. Icy burst + 0.2s shudder + held white-blue edges + pulsing blue auras. Payloads linger (×2.5). **Bubbles stay poppable** — a free window to clear/defuse. Refreshes on re-catch. |

Both are *good* pickups (no conditioning jolt). Darter slow-mo and the Freeze hold are independent and can overlap.

---

## 5. Bubble variants

Benign = pop for a treat (no fuse). Live = defuse for reward or it detonates. Two "good" pickups (darter, freeze).

| Variant | Kind | Label | Size band | Default motion | Payload | Weight · MinIntensity · Fuse(ms) |
|---------|------|-------|-----------|----------------|---------|----------------------------------|
| **Flash** | benign | — | 150–210 | FloatUp | flash burst | 3.0 · 0.00 · — |
| **Subliminal** | benign | ♥ | 170–220 | FloatUp | subliminal flash | 3.0 · 0.00 · — |
| **Pink Filter** | live | ◑ | 180–240 | RainDown | `pink_filter` overlay | 2.0 · 0.10 · 3500–5000 |
| **Spiral** | live | ◎ | 180–240 | RoamBounce | `spiral` overlay | 2.0 · 0.15 · 3500–5000 |
| **BrainDrain** | live | ☁ | 240–320 | RoamBounce | random flash-pool image, fullscreen ~10s @ 10% | 1.4 · 0.25 · 4500–6500 |
| **Freeze** | **good pickup** | ❄ | 190–250 | FloatUp | freeze power-up (see §4) | 1.0 · 0.15 · — |
| **Video** | live (rare) | ▶ | 240–300 | RainDown | mandatory video | 0.5 · 0.50 · 5000–7000 |
| **HT Link** | live (rare) | HT | 200–280 | FloatUp | HypnoTube link, fullscreen | 0.45 · 0.60 · 4500–6500 |
| **Darter** | **good pickup** | — | 72–96 | bounce (own physics) | slow-mo power-up (see §4) | intensity-rolled, separate from the cap |

A per-variant sprite at `assets/Chaos/bubbles/{id}.png` replaces the tinted `bubble.png` when present.

---

## 6. Boons & curses (drafted at wave boundaries)

Draft of **3 options** (→ **4** with the *4-Boon Draft* upgrade). ~50% chance one option is a **curse** (if curses are allowed). Options reveal one at a time — a "dling" for rares, a "thud" otherwise — then pick one (others dissolve) and Continue. **Skip = +1 shield.**

### Boons
| Boon | Rarity | RunMult | Effect |
|------|--------|---------|--------|
| **Slow Fuses** | Common | — | +30% fuse time on live bubbles |
| **Extra Shield** | Common | — | +2 shields |
| **Defuse Chain** | Uncommon | +0.10 | each defuse grants ~900ms invulnerability |
| **Golden Touch** | Uncommon | +0.15 | +15% run multiplier outright |
| **Magnet** | Uncommon | +0.10 | near-clicks still defuse a live bubble |

### Curses (risk/reward — bigger RunMult, nastier knob)
| Curse | Rarity | RunMult | Effect |
|-------|--------|---------|--------|
| **Hair Trigger** | Rare | +0.40 | −25% fuse time |
| **Live Wire** | Rare | +0.50 | next wave: every bubble is live |
| **Double or Nothing** | Rare | — | next wave pays ×2, but detonations cost an extra shield |

`RunMultBonus` accumulates into **BoonMult**. Active boons/curses show in the expanded HUD.

---

## 7. Meta-progression (persists in `chaos_meta.json`)

### Currency — Sparks
Banked at run end (separate from XP):
```
sparks = round( (score/100 · difficultyMult  +  25 · difficultyMult) · SparkGainMult )
```

### XP payout (to the main progression system, separate from Sparks)
```
baseXp  = min(score, 250 · durationMinutes · difficultyMult)   // capped
finalXp = baseXp · skillTreeMultiplier
```

### Lifetime stats & rank
Tracked: `RunsCompleted`, `BestScore`, `BestCombo`, `TotalDefused`.

| Rank | Runs completed |
|------|----------------|
| Newcomer | 0–2 |
| Dabbler | 3–9 |
| Novice | 10–24 |
| Initiate | 25–49 |
| Adept | 50–99 |
| Paragon | 100+ |

### Hub unlocks (by lifetime runs)
Upgrades / Stats / Codex at **1 run**; Loadout at **3 runs**.

- **Codex** — records every bubble/boon you've encountered (`bubble:{id}`, `boon:{id}`).
- **Loadout** — equip one **start boon** that applies automatically before wave 1.

---

## 8. Permanent upgrades (Sparks shop — 3 branches)

`Apply` mutates the freshly-built run config, so owning an upgrade shapes **every** run. Rows marked *deferred* set their config field but the consuming runtime isn't wired yet (inert).

### 🛡 Control
| Upgrade | Cost | Effect |
|---------|------|--------|
| +1 Start Shield | 100 | `StartingShields += 1` |
| Slower Fuses | 120 | fuse time ×1.15 |
| Bigger Hitboxes | 80 | hit-test ×1.25 *(deferred)* |
| Shield Recharge | 200 | regen a shield every 45s *(deferred)* |

### 💰 Greed
| Upgrade | Cost | Effect |
|---------|------|--------|
| Base Mult x1.2 | 90 | BaseMult → 1.2 |
| Golden Touch | 130 | benign-pop scoring 0.4 → 0.6 |
| Magnet Radius | 150 | near-click defuse |
| +Sparks Gain | 180 | Spark payout ×1.2 |

### 🌀 Depth
| Upgrade | Cost | Effect |
|---------|------|--------|
| +2 Max Bubbles | 110 | concurrent cap +2 |
| 4-Boon Draft | 200 | drafts offer 4 options |
| Extreme Tier | 350 | unlock Extreme difficulty |
| Take More | 400 | detonation penalty ×0.5 *(deferred)* |

---

## 9. Per-run setup knobs (setup window / `AppSettings`)

Difficulty · Duration (60–900s) · Wave count (1–12) · Live-bubble share · Starting shields (0–5) ·
Motion override (Mixed / Float / Rain / Roam) · Enabled variants · Screen shake + intensity ·
Colour flashes · Effect intensity (0.2–1.5) · Boon draft on/off · Allow curses · Darters on/off.

Owned upgrades are layered on top of these via `ChaosMeta.ApplyTo` at run start.

---

## 10. File map

| File | Role |
|------|------|
| `ChaosModeService.cs` | Run lifecycle: countdown, run/spawn timers, scoring callbacks, waves, payout, power-ups |
| `ChaosModels.cs` | `ChaosRunConfig`, `ChaosRunState` (HUD-bound), `ChaosBoon` + `ChaosBoonPool` |
| `ChaosBubbleVariants.cs` | The variant table + weighted picker + darter/freeze build |
| `ChaosUpgrades.cs` | Permanent upgrade catalogue + `ChaosMeta` facade |
| `ChaosMetaState.cs` / `ChaosMetaStore.cs` | Persistent meta save model + JSON store |
| `EffectPayload.cs` / `EffectPayloadFactory.cs` | Payload behaviours (flash/overlay/video/htlink/…) |
| `ChaosSfx.cs` | One-shot SFX (wave-clear, boon reveal/pick) |
| `ChaosArt.cs` | Resolves per-variant sprites under `assets/Chaos/` |
| `ChaosHudWindow` · `ChaosOverlayWindow` · `ChaosFxWindow` · `ChaosFlashOverlay` | HUD strip · countdown/draft/results · juice vignette · braindrain wash |
| `BubbleService.cs` | Renders + animates every bubble (chaos extends the ambient pop game) |
