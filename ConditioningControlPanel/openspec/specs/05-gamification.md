# 05 — Gamification & Game Logic

Cluster recon of the **gamification / game-logic** subsystem of the Conditioning Control Panel:
progression math, achievements, quests, the skill tree, season recap, roadmap, the lock-card /
pop-quiz / quiz / bubble-count / focus-game minigame rules, and the Chaos roguelite economy.

**Scope note:** this cluster covers the *rules / math / state* of these systems. The *rendering*
of overlays, bubbles, HUDs, and result cards (the `*Window.xaml`, `*Overlay.cs`, and the
`ChaosModeService` orchestrator) belongs to the rendering cluster and is OS-SPECIFIC; here it is
treated only as the downstream consumer of the game-logic seam.

## Cluster-wide findings

- **The math is portable.** Every XP curve, level threshold, quest target, achievement
  predicate, skill multiplier, streak calculation, Chaos economy/boon/rank table, and quiz
  scoring rule is plain `System` / `System.Linq` / `Newtonsoft.Json` C#. None of it depends on
  Windows.
- **The persistence is portable.** All state saves to JSON under `%APPDATA%` /
  `%LOCALAPPDATA%` via `System.IO` + `JsonSerializer` / `Newtonsoft.Json` (atomic `.tmp` rename
  in several). `Environment.SpecialFolder.ApplicationData` resolves on every OS; only the path
  string differs.
- **The recurring seam is the timer, not the logic.** Nearly every *service* (as opposed to
  *model*) drives its tick/auto-save with `System.Windows.Threading.DispatcherTimer` and marshals
  notifications through `DispatcherHelper.RunOnUI`. That is the single, mechanical coupling: swap
  `DispatcherTimer` for `System.Threading.Timer`/`PeriodicTimer` and inject an `ISynchronizationContext`
  and the service body is portable. This makes most *services* **MIXED** even though their
  *models* are **PORTABLE**.
- **A second seam is "trigger an effect / show a popup."** Services that *render* a result
  (achievement popup, quest chime via `System.Media.SystemSounds`, lock-card window, pop-quiz
  window, Chaos bubbles/cards) call straight into WPF. Extract an `IGameFeedback` /
  `IEffectSink` port and the rule body lifts out.
- **Global static bridges (`App.X`)** are pervasive (`App.Settings`, `App.Logger`,
  `App.Progression`, `App.SkillTree`, `App.Achievements`, `App.Quests`, `App.Patreon`,
  `App.Haptics`, `App.Bubbles`). These are *architectural* coupling, not *OS* coupling — they
  resolve to portable services and do not themselves block a non-Windows build. They are noted
  but do not change a PORTABLE classification.

---

## Capability: XP & Leveling (Progression)
**Files:** Services/ProgressionService.cs
**Class:** PORTABLE
**Blocking deps:** none. Pure arithmetic over `App.Settings.Current`. No WPF, no timer, no I/O.
Fires `LevelUp` / `XPChanged` plain `EventHandler`s and calls portable sibling services
(Haptics/Discord/ProfileSync are fire-and-forget and behind null-checks).

### Requirement: The system SHALL award XP and advance levels on a defined curve.
The system SHALL apply the skill-tree multiplier, suppress passive XP while idle (anti-AFK),
roll XP into levels using a piecewise level curve (linear bands to L150, 3% compound beyond),
and raise a level-up event per level crossed.

#### Scenario: XP award crosses a level boundary
- WHEN `AddXP(amount)` pushes `PlayerXP` past `GetXPForLevel(level)`
- THEN the level increments, the overflow carries to the next level, `HighestLevelEver` updates,
  and `LevelUp` fires once per level gained.

#### Scenario: Passive XP while idle
- WHEN `ActivityTracker.IsIdle` and the source is Flash/Subliminal/BouncingText
- THEN no XP is awarded.

---

## Capability: Achievements
**Files:** Services/AchievementService.cs, Models/Achievement.cs, Models/AchievementProgress.cs
**Class:** MIXED
**Blocking deps:** `System.Windows.Threading.DispatcherTimer` (30s autosave + 1s time-tracker),
`AchievementPopup` window + `DispatcherHelper.RunOnUI` for the unlock toast. The catalog
(`Achievement.All`), the unlock predicates, the counters, the streak math, and JSON persistence
in `AchievementProgress` are all PORTABLE (the agent confirmed both model files are pure .NET).
**Seam (if MIXED):** replace the two `DispatcherTimer`s with a portable scheduler; route the
unlock event through an `IAchievementFeedback` port instead of constructing `AchievementPopup`.

### Requirement: The system SHALL track stats and unlock achievements at thresholds.
The system SHALL accumulate counters (flashes, bubbles, video minutes, spiral/pink-filter
minutes, sessions, lock cards, etc.), evaluate level/time/session/combo predicates, and unlock
each achievement at most once, persisting progress to `achievements.json`.

#### Scenario: Threshold crossed
- WHEN a tracked counter reaches an achievement's threshold (e.g. 1000 bubbles → `pop_the_thought`)
- THEN `TryUnlock` records the unlock, marks dirty, saves immediately, and (unless suppressed)
  raises `AchievementUnlocked`.

#### Scenario: Patron-exclusive earn vs cloud restore
- WHEN a patron-exclusive achievement is *earned* via `TryUnlockExclusive`
- THEN it unlocks only if `Patreon.HasPremiumAccess`; cloud restore via plain `TryUnlock` stays
  ungated so a downgraded user keeps prior earns.

---

## Capability: Daily / Weekly Quests
**Files:** Services/QuestService.cs, Services/QuestDefinitionService.cs, Models/Quest.cs, Models/QuestProgress.cs
**Class:** MIXED (QuestService) / PORTABLE (QuestDefinitionService + both models)
**Blocking deps:** QuestService uses `DispatcherTimer` (autosave + 1-min rollover check) and
`System.Media.SystemSounds.Exclamation.Play()` for the completion chime. QuestDefinitionService is
pure HTTP (`HttpClient`) + JSON file cache — fully PORTABLE. The quest pool, category gating,
reroll math, streak/shield recalculation, and level-scaled XP reward are PORTABLE.
**Seam (if MIXED):** portable scheduler for the two timers; move `SystemSounds` behind the
`IGameFeedback` port.

### Requirement: The system SHALL generate, track, complete, and roll over quests.
The system SHALL pick daily/weekly quests from a remote-or-embedded pool, advance progress per
category event, complete a quest at its target (awarding level- and streak-scaled XP), enforce
3 dailies/day, support level/Patreon-gated rerolls, and regenerate on day/week rollover or when a
definition disappears.

#### Scenario: Quest reaches target
- WHEN category progress reaches `TargetValue`
- THEN the quest completes, scaled XP is awarded via `Progression.AddXP`, the streak calendar
  updates (with shield fill for a missed yesterday), and the next daily generates if under the cap.

#### Scenario: Day rollover while running
- WHEN the 1-minute timer detects `IsDailyExpired()`/`IsWeeklyExpired()`
- THEN expired quests regenerate and `QuestsRefreshed` fires.

---

## Capability: Skill Tree (Enhancements)
**Files:** Services/SkillTreeService.cs, Models/SkillTree.cs
**Class:** MIXED
**Blocking deps:** two `DispatcherTimer`s (Pink Rush window end + 10-min random-proc check) and
`DispatcherHelper.RunOnUI` for proc events. `SkillDefinition.All`, purchase/prereq validation,
all multiplier math (XP, reroll, perfect-week bonus, streak shield, conditioning-time accrual) is
PORTABLE.
**Seam (if MIXED):** portable timer + event sink for the Pink Rush proc/expiry.

### Requirement: The system SHALL manage skill purchases and apply skill bonuses.
The system SHALL gate purchases on points/prereqs, persist unlocked skills, expose aggregate
multipliers (`GetTotalXpMultiplier`, reroll/perfect-week bonuses), manage streak-shield charges,
and time-box the Pink Rush buff.

#### Scenario: Purchase a skill
- WHEN `CanPurchaseSkill` holds (owns prereq, enough points, not owned)
- THEN points are spent, the skill is added to `UnlockedSkills`, and `SkillUnlocked` fires.

---

## Capability: Season Recap & Roadmap
**Files:** Services/SeasonRecapService.cs, Models/SeasonRecap.cs, Services/RoadmapService.cs, Models/RoadmapDefinition.cs, Models/RoadmapProgress.cs
**Class:** PORTABLE (SeasonRecap) / MIXED (RoadmapService)
**Blocking deps:** SeasonRecapService is static, file+JSON only — PORTABLE (its only Windows-ish
touch is `pack://application` background URIs in `SeasonRecap.cs`, which are inert strings, a
trivial seam). RoadmapService uses a `DispatcherTimer` for 30s autosave (otherwise pure
track/step/progress logic).
**Seam (if MIXED):** portable autosave timer in RoadmapService.

### Requirement: The system SHALL capture per-season stats and track roadmap progress.
The system SHALL accumulate monthly counters, snapshot-then-clear them at season rollover
(snapshot written before clear), assign a season title tier, and advance roadmap steps per track.

#### Scenario: Season rollover
- WHEN startup detects a new `yyyy-MM` UTC season vs the stored bucket
- THEN the prior season's snapshot is written to `season-recaps/<key>.json` before live counters reset.

---

## Capability: Lock Card minigame (rules + scheduling)
**Files:** Services/LockCardService.cs, Models/Quest.cs (phrases via sessions)
**Class:** MIXED
**Blocking deps:** `DispatcherTimer` scheduling + `DispatcherHelper.RunOnUISync` to show the
typing window. Phrase selection, timing, accuracy/speed scoring (fed to
`Achievements.TrackLockCardCompletion`) is PORTABLE.
**Seam (if MIXED):** portable scheduler + an `ILockCardPresenter` port.

### Requirement: The system SHALL schedule lock-card typing challenges and score completions.
The system SHALL present phrases on an interval and report duration/chars/errors/phrases for
achievement + quest tracking.

#### Scenario: Perfect lock card
- WHEN a card completes with zero errors
- THEN `typing_tutor` is flagged and the quest counter advances.

---

## Capability: Pop-Quiz & Quiz (reinforcement + assessment)
**Files:** Services/PopQuizService.cs, Services/QuizService.cs, Services/QuizSessionGenerator.cs
**Class:** MIXED (PopQuiz) / PORTABLE (QuizService, QuizSessionGenerator)
**Blocking deps:** PopQuizService uses `DispatcherTimer` + `DispatcherHelper.RunOnUISync` to show
quiz popups (the question pool + "all answers correct" logic is portable). QuizService is pure
HTTP + JSON + AI-moderation orchestration with archetype/category scoring — PORTABLE.
QuizSessionGenerator is pure: maps a score% to a difficulty and builds a `Session` — PORTABLE.
**Seam (if MIXED):** portable scheduler + popup presenter for PopQuizService.

### Requirement: The system SHALL score quizzes and derive a tailored session.
The system SHALL evaluate quiz answers into a category percentage, map it to an archetype and a
session difficulty (≤25 Easy … >75 Extreme), and generate a `Session` with matching phases/text.

#### Scenario: High-score quiz
- WHEN a quiz scores >75%
- THEN `QuizSessionGenerator` emits an Extreme-difficulty session.

---

## Capability: Bubble minigames (pop + count)
**Files:** Services/BubbleService.cs, Services/BubbleCountService.cs, Models/FocusGameBucket.cs, Services/FocusGameService.cs
**Class:** OS-SPECIFIC (BubbleService) / MIXED (BubbleCountService) / PORTABLE (FocusGameService rules)
**Blocking deps:** **BubbleService is the renderer** — `System.Windows.*`, `System.Windows.Media`,
per-bubble `Window`s, `DispatcherTimer` animation loop, `System.Windows.Forms.Screen` enumeration
and DPI. It is OS-SPECIFIC by nature; only the spawn-rate/scoring constants are portable, and they
are entangled with rendering. BubbleCountService is the count-the-bubbles round logic (MIXED — rules
portable, presentation via Dispatcher). FocusGameService is pure filesystem bucket enumeration +
persisted Include/IsTarget merge — PORTABLE (skeleton; round loop lands in a window later).
**Seam:** BubbleService would need a full render/IScreen abstraction to port — treat as rendering
cluster. FocusGameBucket model is pure JSON data.

### Requirement: The system SHALL run bubble pop/count rounds and award progress.
The system SHALL spawn bubbles, register pops (awarding bubble counters + every-100 skill point),
and for count rounds compare the user's count to the actual, tracking correct/best streaks.

#### Scenario: Bubble milestone
- WHEN total bubbles popped hits a multiple of 100
- THEN a skill point is awarded and a milestone notification shows.

---

## Capability: Lockdown & Mind-Wipe (timed coercion states)
**Files:** Services/LockdownService.cs, Services/MindWipeService.cs
**Class:** MIXED
**Blocking deps:** both use `DispatcherTimer` (Lockdown 1s countdown; MindWipe tick + crossfade).
MindWipe also drives overlay crossfades via Dispatcher. The *state machine* — forcing strict-lock
on / panic off for a duration, the recovery-file write/restore so a crash can't strand the panic
key, the elapsed/duration math — is PORTABLE.
**Seam (if MIXED):** portable countdown timer; an `ILockEnforcer` / overlay port.

### Requirement: The system SHALL hold a timed lockdown and safely restore prior settings.
The system SHALL persist pre-lockdown StrictLock/PanicKey to a recovery file, force the coercive
values for the duration, count down, and restore (from recovery file even after a crash) on expiry.

#### Scenario: Crash during lockdown
- WHEN the app restarts and finds `lockdown_recovery.json`
- THEN the user's real StrictLock/PanicKey values are restored.

---

## Capability: Gamification Bridge (event wiring)
**Files:** Services/GamificationBridge.cs
**Class:** PORTABLE (with a thread-marshaling seam)
**Blocking deps:** none structural — it only subscribes to portable feature events and calls
Track*/TryUnlock. A few handlers use `DispatcherHelper.RunOnUI` purely to hop threads (gaze/remote
events arrive on the UI thread today); that is a synchronization-context concern, not WPF.
**Seam:** inject an `ISynchronizationContext` instead of `DispatcherHelper`.

### Requirement: The system SHALL translate feature events into achievement/XP tracking centrally.
The system SHALL be the single subscriber that maps companion/keyword/quiz/lockdown/gaze/deeper
events to threshold-based achievement unlocks (free + patron-exclusive), with no XP/skill side
effects of its own beyond the achievement counters.

#### Scenario: Keyword triggers reach threshold
- WHEN 500 keyword triggers have fired
- THEN the bridge unlocks `pavlov`.

---

## Capability: Chaos roguelite — economy, boons, ranks, lessons, narrative (RULES)
**Files:** Services/Chaos/ChaosTuning.cs, ChaosUpgrades.cs, ChaosRanks.cs, ChaosLifetimeBoons.cs, ChaosMetaStore.cs, ChaosMetaState.cs, ChaosLessons.cs, ChaosHappyPath.cs, ChaosNarrativeModels.cs, ChaosNarrativeDirector.cs, ChaosNarrativeHooks.cs, ChaosStoryCards.cs, ChaosTips.cs, ChaosBubbleHints.cs, ChaosRanks.cs, EffectPayloadFactory.cs, HtLinkPool.cs, ChaosBubbleVariants.cs
**Class:** PORTABLE
**Blocking deps:** none. These are tuning constants, the upgrade/boon catalogs, rank thresholds,
the Sparks economy + purchase facade (`ChaosMeta`), lesson gates/completion events, the
happy-path scripting for the first descents, and the narrative director/cue tables. All are
`System` / `System.Linq` / `Newtonsoft.Json`. `ChaosMetaStore` is atomic JSON disk I/O.
ChaosBubbleVariants is a large data/spec table for behavioral bubble variants (portable data,
though its specs are consumed by the OS-specific spawner).

### Requirement: The system SHALL define and persist the Chaos meta-economy.
The system SHALL award/spend Sparks, gate permanent upgrades and lifetime boons by cost/rank,
advance ranks at thresholds, track lesson completion once-each, and persist all of it to the
chaos meta-state JSON.

#### Scenario: Purchase a permanent upgrade
- WHEN the player has enough Sparks and meets the rank requirement
- THEN Sparks are deducted, the upgrade is recorded in meta-state, and the change is saved atomically.

#### Scenario: Lesson completes
- WHEN a lesson's predicate is first satisfied
- THEN it is marked complete once and `LessonCompleted` fires (drives an unlock card downstream).

---

## Capability: Chaos roguelite — run lifecycle & bound view-state (ORCHESTRATION)
**Files:** Services/Chaos/ChaosModeService.cs, ChaosModels.cs, EffectPayload.cs, ChaosArt.cs, ChaosNarrator.cs, ChaosSfx.cs, ChaosRevealService.cs, ChaosLessonHooks.cs
**Class:** OS-SPECIFIC (ChaosModeService) / MIXED (ChaosModels, EffectPayload, ChaosArt)
**Blocking deps:**
- **ChaosModeService** (3021 LOC) is the orchestrator: owns `ChaosHudWindow`/`ChaosOverlayWindow`/
  `ChaosFxWindow`, two `DispatcherTimer`s (run clock + spawn), `Application.Current.Dispatcher`
  topmost re-asserts, `System.Windows.Media.Color` cues. The *combo/heat/multiplier/shield/wave*
  math lives here but is welded to the live-desktop render loop → OS-SPECIFIC as written.
- **ChaosModels.cs** — run-state economy fields are portable but the file also carries WPF
  `Color`/`SolidColorBrush`/`Visibility`/`INotifyPropertyChanged` sidebar view-state → MIXED.
- **EffectPayload.cs** — uses `Application.Current.MainWindow` + `DispatcherTimer` to dispatch
  effects → MIXED (the payload *taxonomy* is portable; the *apply* is not).
- **ChaosArt.cs** — `BitmapImage`/`System.Windows.Media.Imaging` sprite cache → MIXED (asset
  loading only).
**Seam (if MIXED):** extract the run state machine (clock, waves, combo/heat/shield, XP payout) into
a UI-free engine driven by an injected ticker, emitting spawn/cue/result *intents* through an
`IChaosView` port; split ChaosModels into a portable run-state record + a WPF view-model;
put `BitmapImage`/`Color` behind asset/color abstractions.

### Requirement: The system SHALL run a timed Chaos descent with combo/heat/boon scoring.
The system SHALL run a countdown then timed waves of effect bubbles, accumulate combo/heat and a
score multiplier, apply shields against detonations, offer a boon draft between loops, and pay out
capped XP + Sparks at the end.

#### Scenario: Combo milestone crossed
- WHEN the combo count crosses a big-combo threshold (25/50/100)
- THEN the combo-big cue fires once per crossing (edge-detected).

#### Scenario: Loop boundary boon draft
- WHEN a wave loop ends
- THEN the clock and spawns pause, a boon draft is presented, and an untouched draft auto-resumes
  after the configured delay (granting a shield).

---

## Capability: Gaze Minigame (Lab) — config & pack assignment
**Files:** Lab/GazeMinigame/GazeMinigameSettings.cs, Lab/GazeMinigame/GazePackLibrary.cs, Lab/GazeMinigame/AssetPack.cs, Lab/GazeMinigame/GazeMinigameWindow.xaml.cs
**Class:** PORTABLE (settings/library/pack model) / OS-SPECIFIC (the `.xaml.cs` window)
**Blocking deps:** the settings enums (vibration mode, reward effect, pack role), pack
assignment/resolution, and library discovery are pure JSON-backed data. The game window itself is
WPF.
**Seam:** the round loop logic, if/when separated from `GazeMinigameWindow.xaml.cs`, would be
portable; the gaze input + rendering stay OS-SPECIFIC.

### Requirement: The system SHALL persist gaze-pack roles and re-resolve them on load.
The system SHALL remember Focus/Ignore/Off assignments by folder path and silently drop a pack
whose folder vanished.

---

## Summary table

| Capability | Class | Blocking dep | ~LOC |
|---|---|---|---|
| XP & Leveling (Progression) | PORTABLE | none | 265 |
| Achievements | MIXED | DispatcherTimer ×2 + AchievementPopup | 874 (svc) +1000 (models) |
| Quests | MIXED | DispatcherTimer + SystemSounds | 963 +360 (models) |
| Quest Definitions (remote) | PORTABLE | none (HTTP+JSON) | 471 |
| Skill Tree | MIXED | DispatcherTimer ×2 (Pink Rush) | 876 +403 |
| Season Recap | PORTABLE | none (pack:// strings, inert) | 250 +213 |
| Roadmap | MIXED | DispatcherTimer (autosave) | 384 +400 |
| Lock Card | MIXED | DispatcherTimer + window | 196 |
| Pop-Quiz | MIXED | DispatcherTimer + popup | 254 |
| Quiz + QuizSessionGenerator | PORTABLE | none (HTTP/JSON/AI) | 1357 +476 |
| Bubble pop (BubbleService) | OS-SPECIFIC | WPF windows + Forms.Screen + Dispatcher | 3914 |
| Bubble count | MIXED | DispatcherTimer + presentation | 637 |
| Focus Game (rules) | PORTABLE | none (filesystem) | 137 +29 |
| Lockdown | MIXED | DispatcherTimer countdown | 226 |
| Mind-Wipe | MIXED | DispatcherTimer + overlay crossfade | 603 |
| Gamification Bridge | PORTABLE | sync-context only (DispatcherHelper) | 486 |
| Chaos economy/boons/ranks/lessons/narrative (rules) | PORTABLE | none | ~4500 |
| Chaos run lifecycle (ChaosModeService) | OS-SPECIFIC | WPF windows + DispatcherTimer + Color | 3021 |
| Chaos bound view-state (Models/EffectPayload/Art) | MIXED | Color/Brush/Visibility/BitmapImage/MainWindow | ~1100 |
| Gaze Minigame config/packs | PORTABLE | none (JSON) | ~300 |
| Gaze Minigame window | OS-SPECIFIC | WPF window + gaze input | (render cluster) |

---

## Portability verdict

The gamification cluster is **rules-portable but service-coupled**. Every piece of domain
truth — XP curves, level thresholds, quest pools and streak math, the full achievement catalog
and predicates, skill multipliers, the entire Chaos meta-economy (Sparks, boons, ranks, lessons,
narrative), quiz scoring, and all JSON persistence — is pure `net8.0` C# that compiles and runs
unchanged off-Windows. **All gamification *models* are PORTABLE.** What pins the *services* to
Windows is overwhelmingly one mechanical seam, **`DispatcherTimer`** (autosave/tick/countdown),
plus a thin "show a popup / play a chime / spawn a bubble" feedback seam — both removable by
injecting a portable scheduler and an effect/feedback port. The genuinely OS-SPECIFIC code is the
*rendering* end: `BubbleService` (per-bubble WPF windows + `Forms.Screen`) and the
`ChaosModeService` orchestrator (owns three WPF windows and the live-desktop run loop), plus the
gaze-minigame window — and even there the scoring/combo/economy math is portable logic merely
welded to the render loop.

Counting by capability: of the ~22 capabilities above, ~9 are PORTABLE today, ~9 are MIXED
(timer/feedback seam only), and ~3-4 are OS-SPECIFIC renderers. By the share of game-logic LOC
that is *rules/math/state* (excluding the two big renderers BubbleService 3.9k and ChaosModeService
3.0k), roughly **70–75% of the cluster is portable as-is or behind a single timer/feedback shim**;
the irreducibly OS-bound remainder (~25-30%) is the bubble/Chaos/gaze *rendering* surface, which
belongs to the rendering cluster by design.
