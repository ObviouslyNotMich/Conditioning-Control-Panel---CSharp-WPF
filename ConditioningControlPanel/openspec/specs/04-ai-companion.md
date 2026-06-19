# 04 — AI, Companion, Sessions & Personality

Cluster recon, 2026-06-15. Scope: the "brains" of the companion — AI orchestration over
HTTP, session/phase state machines, personality/prompt data, the bark rule engine, and the
autonomy decision loop. **Not** in scope: avatar window rendering, NAudio playback internals
(those live in the AvatarTube / Audio clusters; this cluster only *produces* lines + audio
paths and hands them off).

## Portability headline

The intellectual core of this cluster is **portable**: every AI call is plain
`System.Net.Http.HttpClient` + `System.Text.Json`/Newtonsoft, every prompt/personality/preset
is a POCO, the bark rule engine is data-driven matching logic, and the session/autonomy
*algorithms* (phase transitions, ramping, XP, weighted action selection, mood/intensity
scaling) are pure math + state. The Windows binding is concentrated at two seams:

1. **Scheduling** — services use WPF `DispatcherTimer` and `Application.Current.Dispatcher`
   to tick and to marshal onto the UI thread.
2. **Output** — the orchestration dispatches into WPF feature services (`App.Flash`,
   `App.Video`, `App.AvatarWindow.Giggle`, overlays) and, in one service, plays audio via
   `NAudio.Wave.WaveOutEvent` directly.

Replacing `DispatcherTimer`→`System.Threading.Timer`/`Task.Delay`, the UI-service dispatch
with an `IEffectSink` interface, and NAudio with an `IAudioPlayer` would make the bulk of the
cluster `net8.0`-clean.

> **NuGet note:** `OpenAI-DotNet` is declared in the csproj but is **not referenced anywhere
> in this cluster** (no `using OpenAI`). `OllamaSharp` is referenced only inside
> `LocalAiService.cs`, and a code comment there says OllamaSharp 5.4.16 was returning 404 so
> the service was rewritten to call the Ollama HTTP API by hand — i.e. both packages are
> effectively dead weight for portability purposes. The real AI transport is raw HttpClient.

---

## Capability: Cloud AI chat & reactions
**Files:** Services/AiService.cs, Services/AIService/IAiService.cs, Services/AIService/AiServiceStrategy.cs
**Class:** PORTABLE
**Blocking deps:** none — `HttpClient` POST to a hosted proxy (`codebambi-proxy.vercel.app`),
`System.Text.Json` request/response, client-side daily-limit circuit breaker, regex
sanitization. Reads `App.*` statics (settings, Patreon tier, moderation guard) but no
`System.Windows`/Win32/NAudio.
**Seam (if MIXED):** n/a

### Requirement: The system SHALL obtain in-character AI replies over HTTP and degrade gracefully.
The system SHALL POST a system+user message pair to the AI proxy, enforce a per-tier daily
request cap and a hard max-token cap, sanitize leaked metadata tags from the reply, run input
and output through the moderation guard, and fall back to a canned phrase (never an error) when
offline, unavailable, rate-limited, or on any HTTP/parse failure.

#### Scenario: Successful reply
- WHEN a chat/awareness/keyword/lockscreen/video-done request is made and the user has a cloud identity or Patreon AI access
- THEN the proxy reply is sanitized, moderation-checked, returned as an AI-generated result, and the daily counter is reconciled from the server's `RequestsRemaining`.

#### Scenario: Unavailable or blocked
- WHEN offline mode is on, the daily limit is hit, the HTTP call fails, or moderation blocks the input/output
- THEN a canned fallback phrase or typed refusal sentinel is returned and no exception propagates to the caller.

---

## Capability: Local AI provider (Ollama) & live provider switching
**Files:** Services/AIService/LocalAiService.cs, Services/AIService/AiServiceStrategy.cs
**Class:** PORTABLE
**Blocking deps:** none — `HttpClient` to `http://localhost:11434` (`/api/chat`, `/api/generate`,
`/api/tags`), `System.Text.Json`, chat history persisted to disk via `System.IO`
(`Path.Combine`, `File.Read/WriteAllText`). `OllamaSharp` is imported but bypassed in favor of
manual HTTP (per in-file comment). `AiServiceStrategy` routes between cloud and local providers
behind the same `IAiService` interface based on a settings flag, with lazy construction and a
warm-up path.
**Seam (if MIXED):** n/a

### Requirement: The system SHALL provide a swappable local-LLM provider with persistent memory.
The system SHALL call a local Ollama server over HTTP, persist and reload conversation history
to disk, warm the model at startup, list installed models, and expose the same `IAiService`
surface as the cloud provider so the active provider can switch live without restart.

#### Scenario: Local reply with memory
- WHEN local AI is selected and the user chats
- THEN history is loaded, the prompt is sent to Ollama, the response is moderation-checked, and the exchange is appended to on-disk history.

#### Scenario: Provider switch
- WHEN `CompanionPrompt.UseLocalAi` toggles
- THEN the strategy lazily constructs and routes to the other provider on the next call, with no app restart.

---

## Capability: AI response parsing & command extraction
**Files:** Services/AIService/AiResponseParser.cs, Services/AIService/IAiResponseParser.cs, Models/AiCommandData.cs, Models/CommandData/*
**Class:** PORTABLE
**Blocking deps:** none — `System.Text.Json`/`JsonDocument`, regex, custom `JsonConverter`,
brace-balancing/JSON-repair string logic, typed effect-command POCOs (FlashImage, Bubbles,
Media, Subliminal, Bounce, Haptic, etc.).
**Seam (if MIXED):** n/a

### Requirement: The system SHALL extract clean text and effect commands from messy LLM output.
The system SHALL parse standard JSON, fenced code blocks, and mixed prose+JSON; repair
unbalanced braces/brackets and trailing commas; deserialize an `effects` array into typed
`AiCommandData`; strip leaked context-metadata tags; and substitute a fallback when the result
is empty.

#### Scenario: Mixed-format reply with effects
- WHEN the model returns prose interleaved with a `{ "response": ..., "effects": [...] }` block
- THEN the prose is returned as clean text and each effect is parsed into a typed command (unknown commands map to `none`).

---

## Capability: AI enrichment (knowledge base & effect-schema prompts)
**Files:** Services/AIService/Enrichment/KnowledgeService.cs, Services/AIService/Enrichment/PromptService.cs, Models/AiEnrichment/*
**Class:** PORTABLE
**Blocking deps:** none — JSON knowledge loaded from deploy dir → assets dir → embedded
resource via `AppDomain.CurrentDomain.BaseDirectory` + reflection; prompt/schema builders are
pure string interpolation and object literals; models are POCOs/records with JSON attributes.
**Seam (if MIXED):** n/a

### Requirement: The system SHALL supply knowledge facts and an effect-command schema to the model.
The system SHALL load a knowledge base (with embedded-resource fallback) and build enrichment
instruction blocks describing the JSON effect-command schema the model may emit.

#### Scenario: Build enrichment prompt
- WHEN an AI request is assembled
- THEN the knowledge facts and effect-schema instructions are injected as system context.

---

## Capability: Personality, prompt & community-prompt data
**Files:** Services/PersonalityService.cs, Models/PersonalityPreset.cs, Models/PersonalityPresets.cs, Models/CompanionPromptSettings.cs, Models/CommunityPrompt.cs, Models/CompanionDefinition.cs
**Class:** PORTABLE
**Blocking deps:** none for the data/logic. The preset/prompt models carry `INotifyPropertyChanged`
(an interface in `System.ComponentModel`, *not* WPF — portable) for MVVM binding; all preset
selection, mod-aware resolution, cloning, and JSON (de)serialization is pure C#.
**Seam (if MIXED):** n/a

### Requirement: The system SHALL manage AI personality presets and per-personality prompt settings.
The system SHALL provide built-in and user-created personality presets (including mod-specific
personalities), select an active preset, build the effective system prompt from
`CompanionPromptSettings`, and clone/serialize presets to JSON.

#### Scenario: Active preset prompt
- WHEN the companion needs a system prompt
- THEN the active preset's prompt settings (personality, reactions, rules, effect permissions) are assembled into the prompt string.

---

## Capability: Community prompt download/install
**Files:** Services/CommunityPromptService.cs, Models/CommunityPrompt.cs
**Class:** MIXED
**Blocking deps:** `System.Windows` — a single advisory toast at line ~360 uses
`Application.Current.Dispatcher.BeginInvoke` + `MessageBox.Show`. Everything else (HTTP fetch of
the prompt directory/manifest, install/activate, file persistence, export/import) is portable
HTTP + JSON + `System.IO`.
**Seam (if MIXED):** Replace the inline `MessageBox.Show` notification with an injected
`INotificationSink`; the rest compiles `net8.0`-clean.

### Requirement: The system SHALL fetch, install, activate and export community AI prompts.
The system SHALL download a remote prompt catalog, install prompts to disk, activate one as the
current personality, and export the current settings as a shareable prompt manifest.

#### Scenario: Install and activate
- WHEN a user installs a community prompt
- THEN it is persisted locally and can be activated as the live personality.

---

## Capability: Available-subjects directory polling
**Files:** Services/AvailableSubjectsService.cs
**Class:** MIXED
**Blocking deps:** WPF — `DispatcherTimer` for the poll cadence and
`Application.Current.Dispatcher.Invoke` to push results onto an `ObservableCollection`; models
use `INotifyPropertyChanged`. The HTTP fetch (15s-timeout `HttpClient`), JSON parse, claim flow,
and entry reconciliation/diff are portable.
**Seam (if MIXED):** Swap `DispatcherTimer`→`System.Threading.Timer` and emit change events
instead of marshalling to an `ObservableCollection`; the polling/reconciliation core is portable.

### Requirement: The system SHALL poll a remote directory of available subjects and reconcile state.
The system SHALL periodically fetch the subject directory over HTTP, parse entries, reconcile
them against the current set, and support a claim operation.

#### Scenario: Poll tick
- WHEN the poll timer fires
- THEN the directory is fetched, parsed, diffed against the current list, and additions/removals are surfaced.

---

## Capability: Session orchestration (phase/ramp/XP engine)
**Files:** Services/SessionEngine.cs, Models/Session.cs, Models/SessionPhase
**Class:** OS-SPECIFIC (portable core inside)
**Blocking deps:** WPF + Win32, heavy:
- `System.Windows.Threading.DispatcherTimer` for the main + phase timers (SessionEngine.cs:37-38).
- Holds a `MainWindow` reference and drives feature services through it (Flash/Video/Pink/Spiral/BrainDrain/Bubbles).
- Corner-GIF feature creates a transparent click-through WPF `Window` + `Image` with
  `XamlAnimatedGif`, `WindowInteropHelper`, `System.Windows.Forms.Screen.PrimaryScreen`
  (SessionEngine.cs:1203,1329), `System.Drawing.Graphics.FromHwnd`, and
  **`DllImport("user32.dll")`** Get/SetWindowLong for `WS_EX_TRANSPARENT|LAYERED|TOOLWINDOW`
  (SessionEngine.cs:1418,1421).
**Seam (if MIXED):** The pure logic — phase-transition selection (`CheckPhaseTransition`),
opacity/intensity ramping, randomized feature start times, pause/resume state machine,
Stopwatch anti-cheat cross-check, and XP/penalty math — is platform-agnostic and could move to a
headless `SessionEngineCore` driven by an injected timer + `IEffectSink`. The timers, MainWindow
coupling, and the corner-GIF window are the OS-bound shell.

### Requirement: The system SHALL run a timed multi-phase conditioning session.
The system SHALL start/pause/resume/stop a session, advance phases on elapsed time, ramp feature
intensity, schedule intermittent bursts, cross-check wall-clock against a Stopwatch for
anti-cheat, save and restore settings around the session, and emit phase-change/completion
events with XP accounting.

#### Scenario: Phase advance
- WHEN elapsed time crosses a phase boundary
- THEN the current phase index updates and a `PhaseChanged` event fires with the new phase.

#### Scenario: Pause penalty
- WHEN the user pauses
- THEN pause count increments and the XP penalty (100/pause) is applied at completion.

---

## Capability: Session persistence & logging
**Files:** Services/SessionManager.cs, Services/SessionFileService.cs, Services/SessionLogService.cs, Models/SessionDefinition.cs, Models/SessionLog.cs
**Class:** PORTABLE (one trivial OS call)
**Blocking deps:** none of consequence. `SessionManager` (CRUD facade over an
`ObservableCollection`) and `SessionLogService` (rolling 20-file media-event JSON log) are pure.
`SessionFileService` is pure JSON `System.IO` except a single
`Process.Start("explorer.exe", ...)` "reveal folder" convenience.
**Seam (if MIXED):** Replace the `explorer.exe` reveal with a no-op/abstraction off-Windows.

### Requirement: The system SHALL load, import/export, persist and log sessions as JSON.
The system SHALL serialize session definitions to `.session.json`, manage the session
collection with add/remove/reload events, and persist a pruned history of in-session media
events.

#### Scenario: Import session
- WHEN a `.session.json` is imported
- THEN it is validated, de-duplicated by id, and added to the collection.

---

## Capability: Autonomy decision loop
**Files:** Services/AutonomyService.cs
**Class:** OS-SPECIFIC (portable decision core inside)
**Blocking deps:** WPF — 3 `DispatcherTimer`s, pervasive
`Application.Current.Dispatcher.BeginInvoke`/`DispatcherHelper.RunOnUI`, `MessageBox.Show`
diagnostics, and `PerformAction` dispatch into WPF feature/avatar services
(`App.Flash/Video/Subliminal/MindWipe/LockCard/Bubbles/BouncingText/Wallpaper/Overlay`).
**Seam (if MIXED):** The algorithmic subset — `SelectAction` (weighted random),
`ApplyMoodWeights`, `ApplyIntensityScaling`, `UpdateMood`/`GetTimeMultiplier` (time-of-day),
cooldown + `ScheduleNextRandomTick` interval variance, and user-activity timestamps — is pure
and exportable behind an injected timer + `IEffectSink`. Timers, dispatcher marshalling, and the
UI-service calls are the OS shell.

### Requirement: The system SHALL autonomously schedule and trigger companion actions.
The system SHALL pick autonomous actions on idle/random/context/time-of-day triggers using
weighted selection modulated by mood and intensity, respect cooldowns, and trigger the chosen
effect.

#### Scenario: Idle-triggered action
- WHEN the user has been idle past the threshold and not on cooldown
- THEN a mood/intensity-weighted action is selected and triggered, and a new cooldown + next random tick are scheduled.

---

## Capability: Bark reactive-dialogue engine
**Files:** Services/BarkService.cs, Services/Bark/* (BarkRule, BarkRuleSet, BarkRuleLoader, BarkVariant, BarkContext, BarkState)
**Class:** MIXED
**Blocking deps:** The data layer is fully portable — `BarkRule`/`BarkVariant`/`BarkRuleSet`
(POCO + Newtonsoft JSON), `BarkRuleLoader` (two-tier file/JSON merge), `BarkContext` (typed
value bag), `BarkState` (thread-safe in-memory counters/timers: blink tally, face-lost timer,
mod-switch/click windows, marathon latches — only reads bools/doubles off `SessionEngine`). The
**service** is OS-bound: a `System.Windows.Threading.DispatcherTimer` for the setup-ready
detector, `DispatcherHelper.RunOnUI`, and — the key output seam — it resolves a line + voiceline
*path* and calls `App.AvatarWindow.Giggle()/GigglePriority()` to render+play. It does **not**
play audio itself (NAudio lives in the avatar window).
**Seam (if MIXED):** Extract the matcher/gate/rotation engine (already mostly pure) over an
`IBarkSink.Speak(line, audioPath, mood, priority)` interface and swap the `DispatcherTimer`;
then the engine + all of `Services/Bark/*` is `net8.0`-clean.

### Requirement: The system SHALL select and emit reactive companion barks from data-driven rules.
The system SHALL subscribe to feature events, evaluate condition maps against per-fire context +
live state, enforce cooldowns/global-min-gap/one-shot scopes/probability and a persisted
no-repeat variant rotation, choose a class-banded priority, substitute tokens, and hand the
resulting line + optional voiceline path to the avatar for rendering.

#### Scenario: Reactive bark fires
- WHEN a subscribed trigger raises and a matching rule passes its conditions, cooldown, chance and rotation gates
- THEN an unused variant is chosen, tokens are substituted, and the line (with any voiceline path) is dispatched to the avatar (priority barks preempt; normal barks queue).

#### Scenario: Suppressed by gate
- WHEN the global min-gap, a held safety bark, or an exhausted one-shot scope blocks the rule
- THEN no bark is emitted and the decision is logged.

---

## Capability: Companion progression & phrase library
**Files:** Services/CompanionService.cs, Services/CompanionPhraseService.cs, Services/CompanionPhraseService (phrase data), Models/CompanionProgress.cs, Models/CompanionPhrase.cs
**Class:** MIXED
**Blocking deps:**
- `CompanionService` — XP routing, drain formula, level-up, active-time math are all portable;
  the only Windows tie is a `DispatcherTimer` (drain/active-time tick).
- `CompanionPhraseService` — phrase CRUD, category/enabled resolution, slugify, voiceline-path
  resolution, and audio-file copy are portable `System.IO`; **but `PlayAudioFile` calls
  `NAudio.Wave.AudioFileReader` + `WaveOutEvent` directly** (lines ~374-376) — a hard Windows
  audio binding.
- Models carry `INotifyPropertyChanged` (portable) and use `File.Exists` (portable).
**Seam (if MIXED):** Inject an `IAudioPlayer` in place of the inline NAudio block in
`PlayAudioFile`, and swap the `CompanionService` `DispatcherTimer`; the XP/level/drain math and
all phrase orchestration are then portable.

### Requirement: The system SHALL track per-companion progression and serve companion phrases.
The system SHALL route XP to the active companion, apply level-up and idle-drain mechanics over
a timer, and provide an enabled/categorized phrase library with optional voicelines, playing a
phrase's audio when requested.

#### Scenario: XP and level-up
- WHEN XP is awarded to the active companion
- THEN cumulative XP, level, and active-time update via the portable progression math.

#### Scenario: Play voiced phrase
- WHEN a phrase with a voiceline is selected
- THEN its audio path is resolved and played (current impl via NAudio — the audio seam).

---

## Capability: Interaction queue (fullscreen-effect serialization)
**Files:** Services/InteractionQueueService.cs
**Class:** MIXED
**Blocking deps:** WPF — `DispatcherTimer` (stuck-detection) + `DispatcherHelper.RunOnUI`. The
queue state machine, timeout extension, force-reset, and clear logic are pure.
**Seam (if MIXED):** Swap the `DispatcherTimer` and dispatcher routing; the queue core is
portable.

### Requirement: The system SHALL serialize mutually-exclusive fullscreen interactions.
The system SHALL queue fullscreen interactions (video, bubble-count, lock-card) so only one runs
at a time, with stuck-detection and force-reset safety.

#### Scenario: Concurrent request
- WHEN a second fullscreen interaction is requested while one is active
- THEN it is queued and started on completion of the first.
