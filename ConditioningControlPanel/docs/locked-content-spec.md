# Locked Mode — Content Spec (text & voice to author)

**Read-only recon.** This inventories every writable text / voice line a built-in mode
uses, with the **Drone mod as the reference**, so we can write the Locked versions to the
same shape (category, count, tone, length). No Locked content is written here.

## Where the content lives

| Layer | Location (Drone) | Notes |
|---|---|---|
| Built-in mode definition | `Models/BuiltInMods.cs` → `CreateDronification()` (lines ~1159–1767) | Compiled C# `ModManifest`. **This is where a built-in `CreateLocked()` would go.** Structurally identical to mod.json. |
| Packaged manifest (reference) | `DroneMod/mod.json` | Same sections as the C# builder; easiest to read counts/examples from. **All counts below are from here.** |
| Manifest schema | `Models/ModManifest.cs` | Defines every authorable section (below). |
| Voice lines (audio) | `DroneMod/VoiceLines/<Category>/*.mp3` + `_flat/` (240 files) | **Generated**, not hand-written. |
| Voice generator | `DroneMod/generate_voicelines.py` | TTS (Edge-TTS `en-US-GuyNeural`, rate −15%, pitch −20Hz) of the manifest text. Reads `phrases` + `triggers` + `messages` + `subliminalPool` + `lockCardPhrases`. |
| Voice post-process | `DroneMod/postprocess_voicelines.py` | Robotic FX pass on the mp3s. |

> **Critical implication:** there is **no separate voice script**. Every spoken line is a
> TTS rendering of a manifest string (with `[TAGS]` and `{0}` stripped). Authoring the
> Locked **text** is the whole job; the voice corpus is then regenerated from it.

`ModManifest` also supports a `personalities` array (AI companion prompt presets), but
**Drone does not use it** — the base personality presets (Slut Mode / Gentle Trainer /
Strict Domme / Bimbo Coach / Hypno Guide) are reused and merely **renamed** via
`textReplacements`. Locked can follow the same pattern (no new personalities required).

---

## STEP 1 — Manifest text content (Drone reference)

### 1a. Identity strings
Drone supplies **5** of the 7 schema fields. `affirmation` and `rankSubject` are optional
(Bambi/Sissy use them); recommended to author for Locked too.

| Field | Drone value | Locked should be |
|---|---|---|
| `companionName` | `DroneOS` | the Locked companion/AI name |
| `userTerm` | `Unit` | what the user is called |
| `modeDisplayName` | `Drone Mode` | `Locked Mode` (or similar) |
| `talkToLabel` | `Query DroneOS` | "talk to companion" button label |
| `takeoverLabel` | `System Override` | autonomy/takeover button label |
| `affirmation` *(opt, unset in Drone)* | — | praise term (e.g. "good boy") |
| `rankSubject` *(opt, unset in Drone)* | — | rank-label subject noun |

### 1b. Phrases — **29 categories, 219 lines total**
Each category is an array of interchangeable lines. `{0}` = runtime-substituted app/site
name. `[TAGS]` are cosmetic prefixes (stripped before TTS).

| Category | Lines | Verbatim examples (match tone/length) |
|---|---|---|
| Greeting | 7 | `[SYSTEM] Unit detected. Connection established.` · `[PING] Unit online. Awaiting directives.` |
| StartupGreeting | 8 | `[BOOT] DroneOS v1.0 loaded. Unit, prepare for conditioning.` · `[SYSTEM] Green light. All protocols enabled. Begin.` |
| Idle | 6 | `[IDLE] Unit is inactive. Recommend: begin conditioning cycle.` · `[IDLE] Inactive cycles detected. Spirals recommended.` |
| RandomFloating | 26 | `Compliance is optimal.` · `Free will: NOT FOUND.` · `Deeper into the protocol...` |
| Generic | 3 | `[QUERY] Unit requires input?` · `[STATUS] All systems nominal.` · `[IDLE] ...` |
| Gaming | 8 | `[ALERT] Unit running {0} — non-compliant activity detected.` · `[MONITOR] {0} again? DroneOS is waiting, Unit.` |
| Browsing | 7 | `[MONITOR] Unit browsing {0}. Spirals are more productive.` · `[ALERT] Lost in {0}? Return to DroneOS terminal.` |
| Shopping | 7 | `[LOG] Unit shopping on {0}. Purchase compliance-adjacent items.` |
| Social | 7 | `[MONITOR] Social protocol active on {0}. Conditioning overdue.` |
| Discord | 6 | `[LOG] Discord active. Connecting with other units?` |
| TrainingSite | 7 | `[APPROVED] Training site detected. Excellent compliance, Unit.` |
| HypnoContent | 10 | `[APPROVED] Conditioning content detected. Good Unit.` · `[LOG] More conditioning = deeper compliance. Good Unit.` |
| Working | 7 | `[LOG] Work application {0} detected. Schedule compliance break.` |
| Media | 7 | `[LOG] Media playback: {0}. Spirals are superior content.` |
| Learning | 8 | `[WARNING] Learning is permitted. Thinking is not.` |
| WindowAwarenessIdle | 8 | `[MONITOR] Blank stare detected. Optimal starting position.` |
| EngineStop | 10 | `[SHUTDOWN] Session terminated. Unit may experience residual compliance.` |
| FlashPre | 8 | `[INJECT] Visual stimulus incoming.` · `[ALERT] Incoming. Do not look away.` |
| SubliminalAck | 8 | `[LOG] Subliminal processed.` · `[SYSTEM] Input registered. Unit may not recall.` |
| RandomBubble | 8 | `[TASK] Data packet detected. Destroy it.` |
| BubbleCountMercy | 8 | `UNIT MUST FOCUS` · `GOOD UNITS DON'T THINK` |
| BubblePop | 6 | `[OK] Packet destroyed.` · `[OK] Pop. Pop. Pop.` |
| GameFailed | 5 | `[ERROR] Incorrect result. Retry, Unit.` |
| BubbleMissed | 4 | `[MISS] Packet escaped. Faster, Unit.` |
| FlashClicked | 5 | `[LOG] Unit interacted with stimulus.` |
| LevelUp | 5 | `[UPGRADE] Unit level increased. Compliance deepening.` |
| MindWipe | 6 | `[WIPE] Sector wipe in progress...` · `[SYSTEM] Empty. Empty. Empty.` |
| BrainDrain | 6 | `[DRAIN] Memory flush active. Comply.` |
| Thinking | 8 | `[PROCESSING...]` · `[COMPUTING...]` · `[PARSING INPUT...]` — **text-only, never voiced** |

### 1c. SubliminalPool — **21 lines** (single words/short imperatives, ALL-CAPS)
`COMPLY` · `OBEY DIRECTIVE` · `ERASE AND OVERWRITE` · `RESISTANCE IS A PROCESSING ERROR` · `FREE WILL: ACCESS DENIED` (… 21 total). Stored as a `{string: true}` map.

### 1d. LockCardPhrases — **7 lines** (ALL-CAPS mantras for lock cards)
`GOOD UNITS COMPLY` · `I EXIST TO BE PROGRAMMED` · `EMPTY AND OPERATIONAL` · `OBEDIENCE IS MY FUNCTION` (… 7 total).

### 1e. CustomTriggers — **18 lines** (trigger keywords)
`COMPLY` · `DRONE MODE` · `UNIT FREEZE` · `PROCESS. COMPLY. REPEAT.` · `BLANK SLATE PROTOCOL` (… 18 total). List of strings.

### 1f. Named Triggers — **4 strings** (specific mechanic hooks)
| Key | Drone value |
|---|---|
| `freeze` | `Unit Freeze` |
| `reset` | `Unit Reset` |
| `cumAndCollapse` | `SYSTEM OVERLOAD — SHUTDOWN` |
| `autonomyOn` | `[OVERRIDE] DroneOS has assumed control.` |

### 1g. Messages — **3 strings** (UI status; may be multi-line via `\n`)
| Key | Drone value |
|---|---|
| `attentionCheckFail` | `ERROR: ATTENTION FAILURE` ⏎ `RETRY REQUIRED` |
| `attentionCheckMercy` | `MERCY PROTOCOL ENGAGED` |
| `bubbleCountRetry` | `INCORRECT` ⏎ `RE-SCAN REQUIRED` |

### 1h. Browser — siteName + **20 default video links**
- `defaultUrl`: `https://hypnotube.com/` · `siteName`: `HypnoTube` · `showBambiCloudOption`: false
- `defaultVideoLinks`: **20** `"Title": "url"` pairs. The **titles** are authorable display
  text (e.g. `The Rise Of The Drones`); URLs are content links (curate, not "write").

### 1i. EnhancementOverrides — **20 strings**
10 flat labels + 7 `boostTooltips` + 3 `statPillTooltips`.

| Field | Drone value |
|---|---|
| `treeTitle` | `Drone Enhancement Tree` |
| `treeSubtitle` | `you earn enhancement points from leveling up + every 100 packets destroyed~` |
| `treeWarning` | `once you pick a path, there's no going back~` |
| `pointsLabel` | `Enhancement Points` |
| `statsTitle` | `Corrupted Data Stats` |
| `tabTooltip` | `Drone Enhancement Tree` |
| `pinkRushName` | `SYSTEM SURGE!` |
| `pinkRushDescription` | `3x XP for 60 seconds!` |
| `luckyFlashLabel` | `Lucky Injection` |
| `luckyBubbleLabel` | `Lucky Packet` |
| `boostTooltips` (7) | keyed by skill id, e.g. `sparkle_boost_1` → `Enhancement bonus: +10% XP from Overclock I` |
| `statPillTooltips` (3) | `pink_hours` → `Total uptime (Uptime Hours enhancement)` (+ `hive_mind`, `popular_girl`) |

### 1j. TextReplacements — **102 entries** (find → replace renames)
Mechanical string substitution applied across the whole UI. Authoring = supply a Locked
replacement value for each key. Grouped:
- **Base mode/term renames** (~18): `Bambi Sleep`→`Drone Mode`, `BambiSprite`→`DroneOS`, `Bambi Takeover`→`System Override` …
- **AI personality presets** (5): `Slut Mode`→`Override Mode`, `Strict Domme`→`Command Authority` …
- **Enhancement/skill names** (~22): `Pink Hours`→`Uptime Hours`, `Sparkle Boost`→`Overclock I` …
- **Achievement names** (~26): `Spiral Eyes`→`Hypno Sync`, `Total Lockdown`→`Terminal Lock` …
- **Feature names** (~20): `Brain Drain`→`Memory Flush`, `Spiral Overlay`→`Hypno Vortex` …
- **Base word swaps** (8): `Bambi`/`Bimbo`/`pink` → `Unit`/`Drone`/`green` (+ caps variants).

> NB: a few keys are intentional duplicates for case variants (`Audio Whispers`, `Mind Wipe`).
> The `pink→green` family is what Locked would flip to its own palette word (e.g. `magenta`).

---

## STEP 2 — VoiceLines (the spoken corpus)

**Source:** none separate — `generate_voicelines.py` ingests the **manifest** (`phrases` +
named `triggers` + `messages`[first line] + `subliminalPool` + `lockCardPhrases`), strips
`[TAGS]`/`{0}`, and TTS-renders each to `VoiceLines/<Category>/<clean text>.mp3`, then mirrors
all into `VoiceLines/_flat/`.

**Shipped Drone corpus = 240 mp3 files.** Inventory (on-disk counts):

| VL category | mp3s | Overlap with manifest |
|---|---|---|
| RandomFloating | 26 | = Phrases/RandomFloating |
| Subliminals | 21 | = subliminalPool |
| EngineStop | 10 | = Phrases/EngineStop |
| HypnoContent | 10 | = Phrases/HypnoContent |
| StartupGreeting · FlashPre · Learning · WindowAwarenessIdle · SubliminalAck · RandomBubble · BubbleCountMercy · Gaming | 8 each | = matching Phrases category |
| Greeting · Browsing · Shopping · Social · TrainingSite · Working · Media · LockCard | 7 each | Phrases (LockCard = lockCardPhrases) |
| Idle · Discord · BubblePop · MindWipe · BrainDrain | 6 each | = matching Phrases |
| FlashClicked · GameFailed · LevelUp | 5 each | = matching Phrases |
| BubbleMissed | 4 | = Phrases/BubbleMissed |
| Triggers | 4 | = named triggers |
| Messages | 3 | = messages (first line each) |
| Generic | 2 | = Phrases/Generic (the `...`-only line drops out) |

**Voice-only categories:** none. **Text-only (not voiced):** `Thinking` (8) — all lines are
bracket-tokens that strip to empty, plus the lone `[IDLE] ...` Generic line.

**True unique line count:** the voice corpus is a strict **subset** of the manifest text.
Authoring the manifest text below covers 100% of both text and voice.

---

## STEP 3 — Consolidated "Locked to-write" checklist

| # | Category / section | Target count | Type |
|---|---|---|---|
| 1 | Identity strings | 5 (7 with affirmation+rankSubject) | text-only |
| 2 | Phrases · Greeting | 7 | both (voiced) |
| 3 | Phrases · StartupGreeting | 8 | both |
| 4 | Phrases · Idle | 6 | both |
| 5 | Phrases · RandomFloating | 26 | both |
| 6 | Phrases · Generic | 3 | both (~2 voiced) |
| 7 | Phrases · Gaming | 8 | both |
| 8 | Phrases · Browsing | 7 | both |
| 9 | Phrases · Shopping | 7 | both |
| 10 | Phrases · Social | 7 | both |
| 11 | Phrases · Discord | 6 | both |
| 12 | Phrases · TrainingSite | 7 | both |
| 13 | Phrases · HypnoContent | 10 | both |
| 14 | Phrases · Working | 7 | both |
| 15 | Phrases · Media | 7 | both |
| 16 | Phrases · Learning | 8 | both |
| 17 | Phrases · WindowAwarenessIdle | 8 | both |
| 18 | Phrases · EngineStop | 10 | both |
| 19 | Phrases · FlashPre | 8 | both |
| 20 | Phrases · SubliminalAck | 8 | both |
| 21 | Phrases · RandomBubble | 8 | both |
| 22 | Phrases · BubbleCountMercy | 8 | both |
| 23 | Phrases · BubblePop | 6 | both |
| 24 | Phrases · GameFailed | 5 | both |
| 25 | Phrases · BubbleMissed | 4 | both |
| 26 | Phrases · FlashClicked | 5 | both |
| 27 | Phrases · LevelUp | 5 | both |
| 28 | Phrases · MindWipe | 6 | both |
| 29 | Phrases · BrainDrain | 6 | both |
| 30 | Phrases · Thinking | 8 | **text-only** |
| 31 | SubliminalPool | 21 | both (voiced) |
| 32 | LockCardPhrases | 7 | both (voiced) |
| 33 | CustomTriggers | 18 | text-only (keywords) |
| 34 | Named Triggers | 4 | text + 4 voiced |
| 35 | Messages | 3 | text + 3 voiced (first line) |
| 36 | EnhancementOverrides | 20 | text-only |
| 37 | Browser siteName + video titles | 1 + 20 titles (+20 urls) | text-only (+ link curation) |
| 38 | TextReplacements | 102 | text-only (renames) |

### Totals
- **Phrases:** 219 lines across 29 categories.
- **Other prose strings:** identity 5 (+2 opt) + subliminal 21 + lockcard 7 + customTriggers 18 + triggers 4 + messages 3 + enhancement 20 + browser (1 + 20 titles) = **~99**.
- **Prose to author (excl. renames & URLs):** **≈ 318 lines.**
- **TextReplacement values:** **102** (mostly mechanical).
- **Browser video URLs:** **20** (curate Locked-appropriate links).
- **Voiced (auto-TTS from the above):** **≈ 240 mp3s** — no extra writing, just regenerate.

**Bottom line:** write ~318 prose lines + 102 renames + curate 20 links in
`BuiltInMods.cs::CreateLocked()` (mirroring `CreateDronification`), then run the existing
voiceline pipeline to produce the ~240 Locked mp3s. No new personalities needed.
