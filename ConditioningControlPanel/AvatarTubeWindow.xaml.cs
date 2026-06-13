using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Moderation;
using XamlAnimatedGif;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel
{
    public partial class AvatarTubeWindow : Window
    {
        private readonly Window _parentWindow;
        private readonly DispatcherTimer _poseTimer;
        private BitmapImage[] _avatarPoses;
        private int _currentPoseIndex = 0;
        private bool _isAttached = true;
        private IntPtr _tubeHandle;
        private IntPtr _parentHandle;
        private int _currentAvatarSet = 1; // Track which avatar set is loaded
        private int _selectedAvatarSet = 1; // User's manually selected avatar (can be lower than max unlocked)
        private int _maxUnlockedSet = 1; // Highest avatar set unlocked based on level
        private bool _useAnimatedAvatar = false; // Whether to use animated GIF

        // ── Emotive portrait avatar (mod-agnostic) ───────────────────────────────────────
        // Active only when the active mod ships an avatar_manifest.json (Sissy first). When on, the
        // skins ARE the avatar-set selector's choices — each the same 20-emotion portrait collection
        // in a different outfit; barks drive a per-line emotion (pose A → linger → pose B), with
        // continuous breathing/wobble/pink-mist. Mods without a manifest keep the legacy 4-pose path.
        private Services.AvatarPortraitSet? _portraitSet;
        private bool _portraitMode = false;
        private int _skinIndex = 0;
        private string _currentEmotion = "neutral";
        private int _emotionPoseIndex = 0;
        private System.Windows.Controls.Image? _activeImg;   // portrait layer currently shown
        private System.Windows.Controls.Image? _idleImg;     // the hidden layer we crossfade INTO
        private bool _crossfadeInFlight = false;
        private DispatcherTimer? _emotionReturnTimer;        // (legacy) created but no longer scheduled
        private DispatcherTimer? _poseSeqTimer;              // drives the speak pose sequence (1s steps, last 2s)
        private int[] _seqOrder = System.Array.Empty<int>(); // bucket-index order for the current spoken line
        private int _seqStep = 0;                            // index into _seqOrder of the pose now showing
        private int _seqStepMs = 1000;                       // per-sequence step/last timing (scaled by line length)
        private int _seqLastMs = 2000;
        private readonly Dictionary<string, double> _audioDurCache = new(); // mp3 length per path (poses ∝ length)
        private double _breathPhase = 0, _wobblePhase = 0, _mistPhase = 0;
        private const double BreathAmplitude = 0.01;     // ±1% scale (breathing)
        private const double WobbleAmplitudeDeg = 0.4;   // ±0.4° rotation (wobble)
        // Speak-time pose sequence: NO idle rotation. On speech we cycle 2–5 poses of the line's emotion,
        // each PoseStepMs, with the LAST held LastPoseLingerMs, then settle on a still idle pose. Pose count
        // scales with line length (longer line → more poses).
        private const int PoseStepMs = 1000;             // each non-final pose shows ~1s
        private const int LastPoseLingerMs = 2000;       // the final pose lingers ~2s before idle
        private const int MinSpeakPoses = 2;
        private const int MaxSpeakPoses = 5;
        // Short lines (≈4–5 words) finish before the 1s/2s cadence does, so their poses flip ~2x faster
        // and keep pace with the brief audio instead of stalling on the first/second pose.
        private const double ShortLineSec = 3.5;
        private const double ShortSpeedFactor = 0.5;
        // Extra "she's talking" motion + mist, applied ONLY while a spoken clip is playing. Kept subtle and
        // INTERMITTENT (a slow envelope gates the fast carrier) so it's a barely-there occasional shimmer.
        private double _speakPhase = 0;       // fast vibration carrier
        private double _speakEnvPhase = 0;    // slow envelope → bursts separated by calm
        private const double SpeakWobbleDeg = 0.175; // extra rotation at the peak of a burst (tiny)
        private const double SpeakShakePx = 0.25;    // horizontal jitter at the peak of a burst (tiny)
        private const double PortraitSizeScale = 0.88;   // portrait size vs legacy poses (0.70 → +15% → +10% per feedback)
        private const double PortraitRaisePx = 30;       // shift the portrait avatar UP by this many px (100→50→30 per feedback)
        private const double PortraitShiftX = 10;        // shift the portrait avatar RIGHT by this many px
        private const double LegacyAvatarMaxHeight = 306; // XAML defaults for ImgAvatar/ImgAvatarB
        private const double LegacyAvatarMaxWidth = 198;
        private System.Windows.Media.Effects.Effect? _savedEffectA; // original pink DropShadow (restored for legacy)
        private System.Windows.Media.Effects.Effect? _savedEffectB;
        private bool _avatarEffectsSaved = false;

        // Avatar set titles (localization keys)
        private static readonly string[] AvatarTitleKeys = new[]
        {
            "avatar_title_basic_bimbo",          // Set 1: Level 1-19
            "avatar_title_dumb_airhead",         // Set 2: Level 20-34
            "avatar_title_synthetic_blowdoll",   // Set 3: Level 35-49
            "avatar_title_perfect_fuckpuppet",   // Set 4: Level 50-124
            "avatar_title_brainwashed_slavedoll", // Set 5: Level 125-149
            "avatar_title_platinum_puppet",      // Set 6: Level 150+
            "avatar_title_bambi_cow"             // Set 7: Level 75+ (companion-only)
        };

        // Companion speech and chat
        // emotionLineId (bark audio-filename stem) rides along so a QUEUED bark still drives the
        // portrait emotion when its bubble finally renders, not when it was enqueued. Null for
        // non-bark speech (AI/triggers/canned) → no emotion change.
        private readonly Queue<(string text, SpeechSource source, string? emotionLineId, string? mood)> _speechQueue = new();
        private bool _isGiggling = false;
        private bool _isWaitingForAi = false; // Blocks other giggles while waiting for AI
        private DispatcherTimer? _speechTimer;
        private DispatcherTimer? _speechDelayTimer; // Delay between speech instances
        private DispatcherTimer? _idleTimer;
        private DispatcherTimer? _triggerTimer; // Random trigger phrases
        private DispatcherTimer? _randomBubbleTimer; // Random bubble spawning
        private DispatcherTimer? _zOrderRefreshTimer; // Keep speech bubble on top
        private bool _chaosRunActive;       // A Chaos run owns the screen (see SetChaosRunActive)
        private bool _reattachAfterChaos;   // we auto-detached for the run and should re-attach when it ends
        private DateTime _lastClickTime = DateTime.MinValue;
        private DateTime _lastSpeechEndTime = DateTime.MinValue; // Track when last speech ended
        private SpeechSource _lastSpeechSource = SpeechSource.Preset; // Track last speech source for delay calc
        private int _lastSpeechLength = 0; // Track last speech length for delay calc
        private bool _isInputVisible = false;
        private readonly Random _random = new();

        /// <summary>
        /// Regex to match markdown-style links: [Link Text](url)
        /// Used for clickable links in speech bubbles.
        /// </summary>
        private static readonly Regex MarkdownLinkRegex = new Regex(
            @"\[([^\]]+)\]\((https?://[^\)]+)\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private int _presetGiggleCounter = 0; // Counter for 1-in-5 giggle sound on presets
        private readonly List<DateTime> _rapidClickTimestamps = new(); // Track clicks for 50-in-1-minute trigger
        private bool _isMuted = false; // Mute avatar speech and sounds
        private bool _isMouseOverSpeechBubble = false; // Track mouse over speech bubble to keep it open
        private bool _isShowingAiBubble = false; // Track when AI bubble is visible (presets get discarded)
        // While true a real recorded clip (e.g. the New Year note) owns the bubble — nothing may
        // preempt it. The clip's own bubble renders via ShowGiggle(..., bypassClipLock: true).
        private bool _isPlayingUninterruptibleClip = false;
        private const string NoteClipCaption = "♡"; // ♡ — placeholder caption while the clip plays; replace with real content
        private readonly DateTime _startupTime = DateTime.Now; // Track startup to prevent race conditions
        private const double StartupCooldownSeconds = 3.0; // Don't allow non-greeting speech for 3 seconds

        // Speech source for priority/delay calculation
        private enum SpeechSource
        {
            Preset,     // Preset phrases (click reactions, idle, etc.)
            Trigger,    // Random trigger phrases
            AI          // AI-generated responses
        }

        // Chat history: only the conversational pair (user prompts + AI replies),
        // not random preset/trigger chatter. Capped at MaxChatHistorySize entries.
        public class ChatMessage
        {
            public string Text { get; set; } = string.Empty;
            public bool IsUser { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
            public string TimeLabel => Timestamp.ToString("HH:mm");
        }
        private const int MaxChatHistorySize = 100;
        public ObservableCollection<ChatMessage> ChatHistory { get; } = new();
        private bool _isShowingChatHistory = false;

        private void AddToChatHistory(string text, bool isUser)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            ChatHistory.Add(new ChatMessage { Text = text, IsUser = isUser });
            while (ChatHistory.Count > MaxChatHistorySize)
                ChatHistory.RemoveAt(0);
        }

        // Thinking animation: rotating phrase + animated dots while waiting for AI reply.
        private DispatcherTimer? _thinkingTimer;
        private string _thinkingPhraseBase = string.Empty;
        private int _thinkingTickCount;
        private int _thinkingGeneration; // bumped on stop so stale ticks bail

        // Typewriter effect: types AI replies char-by-char, then re-renders with hyperlinks.
        private DispatcherTimer? _typewriterTimer;
        private string _typewriterFullText = string.Empty;
        private int _typewriterIndex;
        private int _typewriterGeneration;

        // Lead-in: spoken (non-AI) lines defer their audio + bubble by this much so the talking
        // animation's mouth starts BEFORE the voice instead of lagging behind it.
        private const double SpeechLeadInSeconds = 0.6;
        private DispatcherTimer? _speechLeadInTimer;

        // Speech delay constants
        private const double MinSpeechDelaySeconds = 2.0;      // Minimum delay between any speech
        private const double AiSpeechBonusSeconds = 5.0;       // Extra delay after AI responses (users need time to read)
        private const int LongTextThreshold = 100;             // Characters before adding per-char delay
        private const double PerCharDelaySeconds = 0.02;       // Delay per character over threshold (doubled for readability)
        // Note: Whispers mute state is now read from App.Settings.Current.SubAudioEnabled
        private bool _isBrowserPaused = false; // Browser audio paused state

        // Voice lines from flash audio folder (used for idle comments and 50% of triggers)
        private List<string> _voiceLineFiles = new();
        private string _voiceLinesPath = Services.CompanionPhraseService.VoiceLineFolder;

        // Unified "spoken audio" channel — ALL companion voice (barks, event lines, idle voicelines) plays
        // through one stoppable player so a new line cuts off the previous one instead of overlapping.
        private readonly object _spokenLock = new();
        private NAudio.Wave.WaveOutEvent? _spokenPlayer;
        private NAudio.Wave.AudioFileReader? _spokenReader;
        private volatile bool _isSpeakingAudio = false;   // true only while a spoken clip is actually playing

        // ============================================================
        // POSITIONING & SCALING - ADJUST THESE VALUES AS NEEDED
        // ============================================================

        // Design reference size (what the XAML is designed for)
        private const double DesignWidth = 780;
        private const double DesignHeight = 1020;

        // Gap between tube window and main window (negative = overlap)
        // This will be scaled based on actual window size
        private const double BaseOffsetFromParent = -350;

        // Vertical offset from center (positive = lower, negative = higher)
        private const double VerticalOffset = 20;

        // Floating animation settings
        private const double FloatDistance = 4;
        private const double FloatDuration = 2.0;

        // Current scale factor
        private double _scaleFactor = 1.0;

        // Current avatar scale (for Ctrl+scroll/arrow key/menu resizing when detached)
        private double _currentScale = 1.0;
        private const double MinScale = 0.5;   // 50% - can shrink twice from 100%
        private const double MaxScale = 1.5;   // 150% - can grow twice from 100%
        private const double ScaleStep = 0.25; // 25% per step

        // Fullscreen detection
        private DispatcherTimer? _fullscreenCheckTimer;
        private bool _hiddenForFullscreen = false;
        private bool _wasAttachedBeforeFullscreen = false;

        // Win32 API
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        // Used by ForceForegroundWindow to bypass Windows' focus-stealing prevention
        // on this tool window (WS_EX_TOOLWINDOW). Without this, Activate() is silently
        // ignored when the user clicks the avatar from another foreground app.
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint GW_HWNDPREV = 3;
        private const int GWL_EXSTYLE = -20;
        private const int GWL_STYLE = -16;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TOPMOST = 0x00000008;
        private const uint WS_POPUP = 0x80000000;
        private const uint WS_CAPTION = 0x00C00000;
        private static readonly IntPtr HWND_TOP = IntPtr.Zero;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        // Window message hook for maintaining topmost during drag
        private const int WM_WINDOWPOSCHANGING = 0x0046;
        private const int WM_WINDOWPOSCHANGED = 0x0047;
        private HwndSource? _hwndSource;
        // Hook on the PARENT window so we can lift the tube back above main the
        // instant main changes z-order (click, flash/overlay close, subsystem
        // re-activation) — event-driven, no polling gap.
        private HwndSource? _parentHwndSource;
        private bool _reassertingAboveParent;

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPOS
        {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x;
            public int y;
            public int cx;
            public int cy;
            public uint flags;
        }

        public AvatarTubeWindow(Window parentWindow)
        {
            InitializeComponent();

            // Apply the user-configured chat shortcut keybinding (Ctrl+T by default).
            Loaded += (_, _) => ApplyChatShortcutTo(this);

            // Bind chat history list to the rolling collection of conversational messages.
            ChatHistoryList.ItemsSource = ChatHistory;

            // Esc closes chat history mode if open.
            PreviewKeyDown += AvatarTubeWindow_PreviewKeyDown;

            _parentWindow = parentWindow;
            // Don't set Owner - it causes black window artifacts during minimize
            // We manage visibility manually via event handlers instead

            // Determine which avatar set to load based on player level
            int playerLevel = App.Settings?.Current?.PlayerLevel ?? 1;
            _maxUnlockedSet = GetAvatarSetForLevel(playerLevel);

            // Load user's saved avatar selection, or use max unlocked
            _selectedAvatarSet = App.Settings?.Current?.SelectedAvatarSet ?? _maxUnlockedSet;
            // Clamp to valid range (1 to max unlocked)
            _selectedAvatarSet = Math.Clamp(_selectedAvatarSet, 1, _maxUnlockedSet);
            _currentAvatarSet = _selectedAvatarSet;

            // Fall back if the saved set isn't supported by the active mod (e.g. a level was retired,
            // like Circe's set 5) — otherwise a stale selection would load an unsupported avatar.
            var supportedSetsInit = GetUnlockedAvatarSets(playerLevel);
            if (supportedSetsInit.Length > 0 && !supportedSetsInit.Contains(_currentAvatarSet))
            {
                _selectedAvatarSet = supportedSetsInit[supportedSetsInit.Length - 1];
                _currentAvatarSet = _selectedAvatarSet;
            }

            // Mods with a single animated emote avatar (BambiSleep, Sissy): lock to that set, ignoring
            // the saved/level-based selection — there's no picker, just the one animated avatar.
            if (IsSingleEmoteAvatarMod(out int emoteOnlySetInit))
                _currentAvatarSet = _selectedAvatarSet = emoteOnlySetInit;

            // Check if this avatar set has an animated version available
            _useAnimatedAvatar = HasAnimatedAvatar(_currentAvatarSet);

            // Load avatar poses for the appropriate set
            _avatarPoses = LoadAvatarPoses(_currentAvatarSet);

            // Set initial avatar (animated or static)
            if (_useAnimatedAvatar)
            {
                LoadAnimatedAvatar(_currentAvatarSet);
            }
            else if (_avatarPoses.Length > 0)
            {
                ImgAvatar.Source = _avatarPoses[0];
            }

            // Apply size/position adjustments for non-basic avatars
            ApplyAvatarTransform(_currentAvatarSet);

            // Initialize title box display
            UpdateTitleDisplay(playerLevel);
            UpdateNavigationArrows();

            // Setup pose switching timer (only for static avatars)
            _poseTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _poseTimer.Tick += PoseTimer_Tick;
            if (!_useAnimatedAvatar && _avatarPoses.Length > 1)
                _poseTimer.Start();

            // Emotive portrait avatar: if the active mod ships avatar_manifest.json, take over the
            // avatar with the 79-pose emotive system. No-op (keeps the legacy 4-pose path) otherwise.
            TryEnterPortraitMode();

            // Circe's Lock pose-1: take over with animated WebP emotes (overrides the above).
            TryUpdateCirceEmoteMode();

            // Subscribe to parent window events
            _parentWindow.LocationChanged += ParentWindow_PositionChanged;
            _parentWindow.SizeChanged += ParentWindow_PositionChanged;
            _parentWindow.StateChanged += ParentWindow_StateChanged;
            _parentWindow.IsVisibleChanged += ParentWindow_IsVisibleChanged;
            _parentWindow.Activated += ParentWindow_Activated;
            _parentWindow.PreviewMouseDown += ParentWindow_PreviewMouseDown;
            _parentWindow.Closed += ParentWindow_Closed;
            
            // Get handles when loaded
            Loaded += OnLoaded;

            // AllowsTransparency=True + SizeToContent=WidthAndHeight + Viewbox
            // creates a layered window whose surface is sized at Show() before
            // the content has been measured. Without a forced refresh after
            // first composition, the surface stays blank until WM_NCCALCSIZE
            // fires from a user window-move — the bug the user reported as
            // "tube doesn't render until I move main". ContentRendered fires
            // ONCE after the first paint, so it's the right place to flush
            // the layered surface via a SizeToContent toggle.
            ContentRendered += OnFirstContentRendered;

            // Refresh tube image from mod on startup (XAML hardcodes pack:// URI)
            SetTubeStyle(!_isAttached);

            // Apply tube layout offsets for current mod
            ApplyTubeLayoutOffsets();

            // Load the active mod's video links on startup (otherwise the known-video table
            // keeps its hardcoded defaults until the user switches mods, so a themed mod's
            // links wouldn't be clickable on a plain boot).
            ReloadVideoLinks();

            // Subscribe to mod changes to refresh tube, avatars, and titles
            if (App.Mods != null)
            {
                App.Mods.ModChanged += (s, mod) =>
                {
                    if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => OnModChanged()); return; }
                    OnModChanged();
                };
            }

            // Initialize context menu state
            UpdateQuickMenuState();

            // Subscribe to mouse wheel and keyboard for resizing when detached
            PreviewMouseWheel += Window_PreviewMouseWheel;
            PreviewKeyDown += Window_PreviewKeyDown;

            // Keep tube in front during position changes when attached
            LocationChanged += (s, e) => { if (_isAttached) BringAttachedPairToFront(); };

            // When tube gets activated (e.g. after topmost video closes), redirect to parent
            Activated += TubeWindow_Activated;

            // Wire up video service events for companion speech (1.3s before video)
            if (App.Video != null)
            {
                App.Video.VideoAboutToStart += OnVideoAboutToStart;
                App.Video.VideoEnded += OnVideoEnded;
            }

            // Lock-card reaction is now owned by BarkService (Fork D 50/50 coin flip): it is the
            // sole subscriber to LockCardCompleted and invokes PlayLockCardAiReactionAsync on heads.
            // This window no longer self-subscribes.

            // Wire up game completion events
            if (App.BubbleCount != null)
            {
                App.BubbleCount.GameCompleted += OnGameCompleted;
                App.BubbleCount.GameFailed += OnGameFailed;
            }

            // Wire up flash service events for pre-announcement
            if (App.Flash != null)
            {
                App.Flash.FlashAboutToDisplay += OnFlashAboutToDisplay;
                App.Flash.FlashClicked += OnFlashClicked;
                App.Flash.FlashAudioPlaying += OnFlashAudioPlaying;
            }

            // Wire up subliminal service events for acknowledgment
            if (App.Subliminal != null)
            {
                App.Subliminal.SubliminalDisplayed += OnSubliminalDisplayed;
            }

            // Wire up bubble service events for occasional pop acknowledgment
            if (App.Bubbles != null)
            {
                App.Bubbles.OnBubblePopped += OnBubblePopped;
                App.Bubbles.OnBubbleMissed += OnBubbleMissed;
            }

            // Wire up achievement events
            if (App.Achievements != null)
            {
                App.Achievements.AchievementUnlocked += OnAchievementUnlocked;
            }

            // Wire up progression events
            if (App.Progression != null)
            {
                App.Progression.LevelUp += OnLevelUp;
            }

            // Wire up companion events (v5.3 companion leveling)
            if (App.Companion != null)
            {
                App.Companion.CompanionLevelUp += OnCompanionLevelUp;
                App.Companion.CompanionSwitched += OnCompanionSwitched;
            }

            // Wire up window awareness events (opt-in feature)
            if (App.WindowAwareness != null)
            {
                App.WindowAwareness.ActivityChanged += OnActivityChanged;
                App.WindowAwareness.StillOnActivity += OnStillOnActivity;
                // Start awareness if enabled
                App.WindowAwareness.Start();
            }

            // Wire up MindWipe events (occasional reactions)
            if (App.MindWipe != null)
            {
                App.MindWipe.MindWipeTriggered += OnMindWipeTriggered;
            }

            // Wire up BrainDrain events (occasional reactions)
            if (App.BrainDrain != null)
            {
                App.BrainDrain.BrainDrainTriggered += OnBrainDrainTriggered;
            }

            // Wire up engine stop event from MainWindow
            if (_parentWindow is MainWindow mainWindow)
            {
                mainWindow.EngineStopped += OnEngineStopped;
            }

            // Show greeting after a short delay (2 seconds after window loads)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var greetingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                greetingTimer.Tick += (s, e) =>
                {
                    greetingTimer.Stop();
                    ShowGreeting();
                };
                greetingTimer.Start();
            }), System.Windows.Threading.DispatcherPriority.Loaded);

            // Start idle timer for random giggles
            StartIdleTimer();

            // Start trigger timer if enabled
            StartTriggerTimer();

            // Start random bubble timer if enabled
            StartRandomBubbleTimer();

            // Handle clicks outside the input panel to close it
            PreviewMouseDown += Window_PreviewMouseDown;

            // P1.4 — wire moderation counter for warning modal + chat cooldown.
            WireModerationCounter();

            App.Logger?.Information("AvatarTubeWindow initialized with avatar set {Set} for level {Level}",
                _currentAvatarSet, playerLevel);
        }

        // ===== P1.4 moderation counter / chat cooldown =====
        private DispatcherTimer? _cooldownTickTimer;
        private string? _normalChatPlaceholder;
        private DateTime? _cooldownEndsAt;

        private void WireModerationCounter()
        {
            var counter = App.ModerationCounter;
            if (counter == null) return;

            counter.WarningTriggered += state =>
            {
                if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => OnWarningTriggered(state)); return; }
                OnWarningTriggered(state);
            };
            counter.CooldownStarted += endsAt =>
            {
                if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => OnCooldownStarted(endsAt)); return; }
                OnCooldownStarted(endsAt);
            };
            counter.CooldownEnded += () =>
            {
                if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => OnCooldownEnded()); return; }
                OnCooldownEnded();
            };
        }

        private void OnWarningTriggered(Services.Moderation.ModerationCounterState state)
        {
            try
            {
                var dlg = new ContentPolicyWarningDialog(state.HitsInLastTenMinutes)
                {
                    Owner = _parentWindow
                };
                dlg.ShowDialog();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "AvatarTubeWindow: failed to show ContentPolicyWarningDialog");
            }
        }

        private void OnCooldownStarted(DateTime endsAt)
        {
            _cooldownEndsAt = endsAt;
            try
            {
                _normalChatPlaceholder ??= TxtUserInput?.Tag as string ?? string.Empty;
                if (TxtUserInput != null)
                {
                    TxtUserInput.IsEnabled = false;
                    TxtUserInput.Opacity = 0.5;
                    TxtUserInput.Text = string.Empty;
                }
                if (BtnSendChat != null)
                {
                    BtnSendChat.IsEnabled = false;
                    BtnSendChat.Opacity = 0.5;
                }

                _cooldownTickTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _cooldownTickTimer.Tick -= CooldownTick;
                _cooldownTickTimer.Tick += CooldownTick;
                _cooldownTickTimer.Start();
                CooldownTick(null, EventArgs.Empty); // initial paint
                App.Logger?.Information("AvatarTubeWindow: chat cooldown engaged until {End}", endsAt);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "AvatarTubeWindow: OnCooldownStarted failed");
            }
        }

        private void CooldownTick(object? sender, EventArgs e)
        {
            if (!_cooldownEndsAt.HasValue) { _cooldownTickTimer?.Stop(); return; }
            var remaining = _cooldownEndsAt.Value - DateTime.UtcNow;
            if (remaining.TotalSeconds <= 0)
            {
                // Probe state to trigger CooldownEnded event in the counter.
                _ = App.ModerationCounter?.GetState();
                return;
            }
            try
            {
                if (TxtUserInput != null)
                {
                    TxtUserInput.Text = string.Format(
                        Localization.Loc.Get("chat_cooldown_active"),
                        (int)Math.Ceiling(remaining.TotalSeconds));
                }
            }
            catch { /* best-effort painter */ }
        }

        private void OnCooldownEnded()
        {
            _cooldownEndsAt = null;
            _cooldownTickTimer?.Stop();
            try
            {
                if (TxtUserInput != null)
                {
                    TxtUserInput.IsEnabled = true;
                    TxtUserInput.Opacity = 1.0;
                    TxtUserInput.Text = string.Empty;
                }
                if (BtnSendChat != null)
                {
                    BtnSendChat.IsEnabled = true;
                    BtnSendChat.Opacity = 1.0;
                }
                App.Logger?.Information("AvatarTubeWindow: chat cooldown ended");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "AvatarTubeWindow: OnCooldownEnded failed");
            }
        }

        /// <summary>
        /// Feature level gating has been removed — every avatar set is available from level 1.
        /// The "max set" now just returns the largest base set (7) so navigation lands in the
        /// same place a level-200 user would.
        /// </summary>
        /// <param name="level">Player's current level (unused - kept for API compatibility)</param>
        /// <returns>Avatar set number</returns>
        public static int GetAvatarSetForLevel(int level)
        {
            return 7;
        }

        /// <summary>
        /// Feature level gating has been removed — every avatar set is always unlocked.
        /// </summary>
        public static bool IsAvatarSetUnlocked(int setNumber, int level)
        {
            return true;
        }

        /// <summary>
        /// Gets all unlocked avatar sets for the given level, in unlock-level order.
        /// Order: 1 (Lv1), 2 (Lv20), 3 (Lv35), 4 (Lv50), 7 (Lv75), 5 (Lv125), 6 (Lv150)
        /// </summary>
        public static int[] GetUnlockedAvatarSets(int level)
        {
            // Base sets in unlock-level order (not numerical order)
            int[] setsInOrder = { 1, 2, 3, 4, 7, 5, 6 };
            var unlocked = new System.Collections.Generic.List<int>();
            foreach (int set in setsInOrder)
            {
                if (IsAvatarSetUnlocked(set, level) && (App.Mods?.IsAvatarSetSupported(set) ?? true))
                    unlocked.Add(set);
            }

            // Append custom avatar sets (8+) sorted by unlock level
            var customSets = App.Mods?.GetCustomAvatarSets();
            if (customSets != null)
            {
                foreach (var cs in customSets.OrderBy(c => c.UnlockLevel))
                {
                    if (IsAvatarSetUnlocked(cs.SetNumber, level) && (App.Mods?.IsAvatarSetSupported(cs.SetNumber) ?? true))
                        unlocked.Add(cs.SetNumber);
                }
            }

            return unlocked.ToArray();
        }

        /// <summary>
        /// Updates the avatar to match the current player level
        /// Call this when the player levels up
        /// </summary>
        public void UpdateAvatarForLevel(int newLevel)
        {
            int newMaxSet = GetAvatarSetForLevel(newLevel);

            // Update max unlocked (user may have unlocked a new avatar)
            if (newMaxSet > _maxUnlockedSet)
            {
                App.Logger?.Information("New avatar unlocked! Set {NewSet} at level {Level}", newMaxSet, newLevel);
                _maxUnlockedSet = newMaxSet;

                // Auto-switch to newly unlocked avatar
                _selectedAvatarSet = newMaxSet;
                if (App.Settings?.Current != null)
                {
                    App.Settings.Current.SelectedAvatarSet = _selectedAvatarSet;
                    App.Settings.Save();
                }

                SwitchToAvatarSet(newMaxSet, animate: true);
            }

            // Update title display
            UpdateTitleDisplay(newLevel);
            UpdateNavigationArrows();
        }

        /// <summary>
        /// Check if an avatar set has animated GIF version available
        /// File naming: animated{set}_1.gif (e.g., animated1_1.gif for set 1)
        /// </summary>
        private bool HasAnimatedAvatar(int setNumber)
        {
            try
            {
                // Check mod override first, then embedded resource
                if (Services.ModResourceResolver.HasModOverride($"animated{setNumber}_1.gif"))
                    return true;
                var uri = new Uri($"pack://application:,,,/Resources/animated{setNumber}_1.gif", UriKind.Absolute);
                var info = Application.GetResourceStream(uri);
                return info != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Load animated GIF avatar using XamlAnimatedGif
        /// File naming: animated{set}_1.gif (e.g., animated1_1.gif for set 1)
        /// </summary>
        private void LoadAnimatedAvatar(int setNumber)
        {
            try
            {
                // An animated set always wins over the emotive-portrait system.
                LeavePortraitMode();

                // Naming pattern: animated1_1.gif, animated2_1.gif, etc.
                var gifUri = new Uri(Services.ModResourceResolver.ResolveUri($"animated{setNumber}_1.gif"), UriKind.Absolute);

                // Hide static avatar, show animated
                ImgAvatar.Visibility = Visibility.Collapsed;
                ImgAvatarAnimated.Visibility = Visibility.Visible;

                // Set the animated GIF source
                AnimationBehavior.SetSourceUri(ImgAvatarAnimated, gifUri);
                AnimationBehavior.SetAutoStart(ImgAvatarAnimated, true);
                AnimationBehavior.SetRepeatBehavior(ImgAvatarAnimated, RepeatBehavior.Forever);

                // Stop pose timer (not needed for animated)
                _poseTimer.Stop();

                App.Logger?.Information("Loaded animated avatar: animated{Set}_1.gif", setNumber);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to load animated avatar {Set}: {Error}", setNumber, ex.Message);
                // Fall back to static
                _useAnimatedAvatar = false;
                ImgAvatar.Visibility = Visibility.Visible;
                ImgAvatarAnimated.Visibility = Visibility.Collapsed;
                if (_avatarPoses.Length > 0)
                {
                    ImgAvatar.Source = _avatarPoses[0];
                }
            }
        }

        /// <summary>
        /// Refresh the avatar animation to fix stuck animations
        /// </summary>
        private void RefreshAvatarAnimation()
        {
            if (!_useAnimatedAvatar) return;

            try
            {
                // Clear and reload the animation
                AnimationBehavior.SetSourceUri(ImgAvatarAnimated, null);

                var gifUri = new Uri(Services.ModResourceResolver.ResolveUri($"animated{_currentAvatarSet}_1.gif"), UriKind.Absolute);
                AnimationBehavior.SetSourceUri(ImgAvatarAnimated, gifUri);
                AnimationBehavior.SetAutoStart(ImgAvatarAnimated, true);
                AnimationBehavior.SetRepeatBehavior(ImgAvatarAnimated, RepeatBehavior.Forever);

                App.Logger?.Debug("Refreshed avatar animation");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to refresh avatar animation: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Pause the animated GIF to reduce CPU usage when not visible
        /// </summary>
        private void PauseAvatarGif()
        {
            if (_circeEmoteMode) { CircePause(); return; }
            if (!_useAnimatedAvatar) return;
            try
            {
                var animator = AnimationBehavior.GetAnimator(ImgAvatarAnimated);
                animator?.Pause();
            }
            catch { }
        }

        /// <summary>
        /// Resume the animated GIF when becoming visible again
        /// </summary>
        private void ResumeAvatarGif()
        {
            if (_circeEmoteMode) { CirceResume(); return; }
            if (!_useAnimatedAvatar) return;
            try
            {
                var animator = AnimationBehavior.GetAnimator(ImgAvatarAnimated);
                animator?.Play();
            }
            catch { }
        }

        /// <summary>Monotonic token; a rapid burst of set-switches collapses to the latest so a stale
        /// fade-completion can't repaint an intermediate set (the "ghost" avatar bug).</summary>
        private int _avatarSwitchGen;

        /// <summary>
        /// Switch to a specific avatar set (with optional animation)
        /// </summary>
        private void SwitchToAvatarSet(int setNumber, bool animate = true)
        {
            int playerLevel = App.Settings?.Current?.PlayerLevel ?? 1;
            if (!IsAvatarSetUnlocked(setNumber, playerLevel)) return;

            int gen = ++_avatarSwitchGen;
            _currentAvatarSet = setNumber;
            _selectedAvatarSet = setNumber;
            _useAnimatedAvatar = HasAnimatedAvatar(setNumber);

            // Save selection
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.SelectedAvatarSet = setNumber;
                App.Settings.Save();
            }

            Action switchAction = () =>
            {
                // Link avatar sets 4+ to companions (v5.3). Done here (NOT in the synchronous prefix)
                // so a rapid swipe past intermediate sets doesn't fire SwitchCompanion -> repaint for
                // each one — only the settled set switches the companion (fixes the ghost avatar).
                //   Set 4: Lv50 → Perfect Fuckpuppet · Set 5: Lv125 → Brainwashed Slavedoll · Set 6: Lv150 → Platinum Puppet
                // In portrait mode the "set" picks a SKIN (outfit), not a companion — skip the coupling.
                if (!UsePortraitSystem())
                {
                    var companionId = GetCompanionForAvatarSet(setNumber);
                    if (companionId.HasValue && App.Companion != null)
                    {
                        App.Companion.SwitchCompanion(companionId.Value);
                    }
                }

                if (UsePortraitSystem())
                {
                    // Portrait mode: the selector picks the SKIN. Repoint and reload the buckets.
                    if (_portraitSet == null)
                    {
                        TryEnterPortraitMode();
                    }
                    else
                    {
                        _skinIndex = _portraitSet.ClampSkin(setNumber - 1);
                        ReloadPortraitSkin();
                    }
                }
                else if (_useAnimatedAvatar)
                {
                    LoadAnimatedAvatar(setNumber);
                }
                else
                {
                    LeavePortraitMode();

                    // Hide animated, show static
                    ImgAvatarAnimated.Visibility = Visibility.Collapsed;
                    AnimationBehavior.SetSourceUri(ImgAvatarAnimated, null);
                    ImgAvatar.Visibility = Visibility.Visible;

                    _avatarPoses = LoadAvatarPoses(setNumber);
                    _currentPoseIndex = 0;
                    if (_avatarPoses.Length > 0)
                    {
                        ImgAvatar.Source = _avatarPoses[0];
                    }

                    // Restart pose timer for static avatars (never in portrait mode — no idle rotation there)
                    if (!_portraitMode) _poseTimer.Start();
                }

                // Update UI
                UpdateTitleDisplay(App.Settings?.Current?.PlayerLevel ?? 1);
                UpdateNavigationArrows();
                ApplyAvatarTransform(setNumber);

                // Circe's Lock: engage emotes only on the base set (pose 1), leave otherwise.
                TryUpdateCirceEmoteMode();
            };

            if (animate)
            {
                // Fade transition
                var target = _useAnimatedAvatar ? (UIElement)ImgAvatarAnimated : ImgAvatar;
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
                fadeOut.Completed += (s, args) =>
                {
                    if (gen != _avatarSwitchGen)
                    {
                        // A newer swap superseded this one — its own fade restores the border opacity.
                        App.Logger?.Information("[AVATAR] swap to set {Set} superseded (gen {Gen}/{Cur})",
                            setNumber, gen, _avatarSwitchGen);
                        return;
                    }
                    switchAction();
                    var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                    AvatarBorder.BeginAnimation(OpacityProperty, fadeIn);
                };
                AvatarBorder.BeginAnimation(OpacityProperty, fadeOut);
            }
            else
            {
                switchAction();
            }

            App.Logger?.Information("Switched to avatar set {Set} (animated: {Animated})", setNumber, _useAnimatedAvatar);
        }

        /// <summary>
        /// Gets the companion ID that corresponds to an avatar set.
        /// Returns null for sets 1-2 (pre-level 35 avatars without companions).
        /// </summary>
        private static Models.CompanionId? GetCompanionForAvatarSet(int setNumber)
        {
            return setNumber switch
            {
                3 => Models.CompanionId.OGBambiSprite,      // Level 50: Synthetic Blowdoll
                4 => Models.CompanionId.CultBunny,          // Level 100: Perfect Fuckpuppet
                5 => Models.CompanionId.BrainParasite,      // Level 125: Brainwashed Slavedoll
                6 => Models.CompanionId.BambiTrainer,       // Level 150: Platinum Puppet
                7 => Models.CompanionId.BimboCow,           // Level 75: Bambi Cow
                _ => null                                    // Sets 1-2 have no companion
            };
        }

        /// <summary>
        /// Gets the avatar set that corresponds to a companion.
        /// Used when switching companions from the UI to update the avatar.
        /// </summary>
        public static int GetAvatarSetForCompanion(Models.CompanionId companionId)
        {
            return companionId switch
            {
                Models.CompanionId.OGBambiSprite => 3,   // Synthetic Blowdoll
                Models.CompanionId.CultBunny => 4,       // Perfect Fuckpuppet
                Models.CompanionId.BrainParasite => 5,   // Brainwashed Slavedoll
                Models.CompanionId.BambiTrainer => 6,    // Platinum Puppet
                Models.CompanionId.BimboCow => 7,        // Bambi Cow
                _ => 1
            };
        }

        /// <summary>
        /// Update the title and level display.
        /// Shows companion name based on current avatar set (v5.3).
        /// </summary>
        private void UpdateTitleDisplay(int level)
        {
            // Portrait mode: the avatar-set selector picks a skin (outfit) — title from the manifest skin.
            if (_portraitMode && _portraitSet != null && _portraitSet.SkinCount > 0)
            {
                int si = _portraitSet.ClampSkin(_skinIndex);
                var skin = _portraitSet.Skins[si];
                var skinTitle = string.IsNullOrWhiteSpace(skin.Title) ? skin.Id : skin.Title;
                skinTitle = App.Mods?.MakeModAware(skinTitle) ?? skinTitle;
                TxtAvatarTitle.Text = (skinTitle ?? "").ToUpperInvariant();
                TxtAvatarLevel.Visibility = Visibility.Collapsed;
                return;
            }

            // v5.3: Show companion name based on current avatar set
            var companionId = GetCompanionForAvatarSet(_currentAvatarSet);

            if (companionId.HasValue && App.Companion != null)
            {
                var companionDef = Models.CompanionDefinition.GetById(companionId.Value);
                var companionProgress = App.Companion.GetProgress(companionId.Value);
                bool isSlutMode = App.Settings?.Current?.SlutModeEnabled ?? false;

                var displayName = companionDef.GetDisplayName(isSlutMode);
                displayName = App.Mods?.MakeModAware(displayName) ?? displayName;
                TxtAvatarTitle.Text = displayName.ToUpperInvariant();
                TxtAvatarLevel.Visibility = Visibility.Visible;
                TxtAvatarLevel.Text = companionProgress.IsMaxLevel
                    ? Loc.Get("avatar_level_max")
                    : Loc.GetF("avatar_level_format", companionProgress.Level);
            }
            else
            {
                // For sets 1-3 (pre-level 50), use legacy avatar titles
                int titleIndex = Math.Clamp(_currentAvatarSet - 1, 0, AvatarTitleKeys.Length - 1);
                var title = Loc.Get(AvatarTitleKeys[titleIndex]);
                title = App.Mods?.MakeModAware(title) ?? title;
                TxtAvatarTitle.Text = title;

                // Hide level for the first 2 generic sprites (sets 1-2) to avoid confusion with persona levels
                if (_currentAvatarSet <= 2)
                {
                    TxtAvatarLevel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    TxtAvatarLevel.Visibility = Visibility.Visible;
                    TxtAvatarLevel.Text = Loc.GetF("avatar_level_format", level);
                }
            }
        }

        /// <summary>
        /// Refreshes the companion display when companion changes or levels up.
        /// Called from CompanionService events.
        /// </summary>
        public void RefreshCompanionDisplay()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(RefreshCompanionDisplay);
                return;
            }

            UpdateTitleDisplay(App.Settings?.Current?.PlayerLevel ?? 1);
        }

        /// <summary>
        /// Called when the active mod changes. Refreshes tube image, avatar poses, and titles.
        /// </summary>
        private void OnModChanged()
        {
            try
            {
                // Refresh tube frame
                SetTubeStyle(!_isAttached);

                // Apply tube layout offsets for new mod's tube glass position
                ApplyTubeLayoutOffsets();

                // Reload video links for companion speech bubbles
                ReloadVideoLinks();

                // Validate current avatar set is supported by the new mod — if not, fall back.
                int playerLevel = App.Settings?.Current?.PlayerLevel ?? 1;
                if (IsSingleEmoteAvatarMod(out int emoteOnlySet))
                {
                    // BambiSleep / Sissy: single animated avatar, no picker — lock to that set. Don't
                    // persist it, so the level selection is preserved for mods that still use the picker.
                    _currentAvatarSet = _selectedAvatarSet = emoteOnlySet;
                }
                else
                {
                    var supportedSets = GetUnlockedAvatarSets(playerLevel);
                    if (supportedSets.Length > 0 && !supportedSets.Contains(_currentAvatarSet))
                    {
                        var oldSet = _currentAvatarSet;
                        _currentAvatarSet = supportedSets[0];
                        _selectedAvatarSet = _currentAvatarSet;
                        if (App.Settings?.Current != null)
                        {
                            App.Settings.Current.SelectedAvatarSet = _selectedAvatarSet;
                        }
                        App.Logger?.Information("Avatar set {OldSet} not supported by new mod, switched to {NewSet}",
                            oldSet, _currentAvatarSet);
                    }
                }

                // Check if the new mod has an animated version for this set
                _useAnimatedAvatar = HasAnimatedAvatar(_currentAvatarSet);

                // Reload avatar from new mod. If the new mod ships a portrait manifest, switch into the
                // emotive-portrait system; otherwise tear it down and use the legacy poses/animated path.
                if (UsePortraitSystem())
                {
                    TryEnterPortraitMode();
                }
                else if (_useAnimatedAvatar)
                {
                    LoadAnimatedAvatar(_currentAvatarSet);
                }
                else
                {
                    LeavePortraitMode();

                    // Hide animated, show static
                    ImgAvatarAnimated.Visibility = Visibility.Collapsed;
                    AnimationBehavior.SetSourceUri(ImgAvatarAnimated, null);
                    ImgAvatar.Visibility = Visibility.Visible;

                    _avatarPoses = LoadAvatarPoses(_currentAvatarSet);
                    _currentPoseIndex = 0;
                    if (_avatarPoses.Length > 0)
                    {
                        ImgAvatar.Source = _avatarPoses[0];
                    }

                    if (_avatarPoses.Length > 1 && !_portraitMode)
                        _poseTimer.Start();
                }

                // Update navigation arrows for supported sets
                ApplyAvatarTransform(_currentAvatarSet);
                UpdateNavigationArrows();

                // Circe's Lock pose-1: engage/leave animated WebP emotes after the normal setup.
                TryUpdateCirceEmoteMode();

                // Refresh voice lines from new mod
                _voiceLinesPath = Services.CompanionPhraseService.VoiceLineFolder;
                RefreshVoiceLines();

                // Refresh title (applies text replacements)
                UpdateTitleDisplay(App.Settings?.Current?.PlayerLevel ?? 1);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to refresh resources after mod change");
            }
        }

        /// <summary>
        /// Applies the active mod's tube layout offsets to avatar, title, input, and speech bubble positions.
        /// Mod tube images may have the glass cylinder in a different position than the default,
        /// so the offset shifts all UI elements horizontally to align with the glass.
        /// </summary>
        private void ApplyTubeLayoutOffsets()
        {
            // Apply avatar scale (emote-set override wins over the mod's global TubeLayout).
            var scale = EffAvatarScale();
            if (Math.Abs(scale - 1.0) > 0.001)
            {
                var scaleTransform = new System.Windows.Media.ScaleTransform(scale, scale);
                ImgAvatar.LayoutTransform = scaleTransform;
                ImgAvatarAnimated.LayoutTransform = scaleTransform;
                ImgAvatarAnimatedB.LayoutTransform = scaleTransform; // Circe emote crossfade layer must match
            }
            else
            {
                ImgAvatar.LayoutTransform = null;
                ImgAvatarAnimated.LayoutTransform = null;
                ImgAvatarAnimatedB.LayoutTransform = null;
            }

            // When the mod only overrides the attached tube image, force the attached
            // layout in detached state too — otherwise the avatar lands outside the
            // chamber the mod author drew (bug report #172).
            var useAttachedLayout = _isAttached || ModOverridesAttachedTubeOnly();

            if (useAttachedLayout)
            {
                var dx = EffAvatarOffsetX();
                var dy = EffAvatarOffsetY();
                AvatarBorder.Margin = new Thickness(5, 100, 126 - dx, 210 + dy);
                TitleBox.Margin = new Thickness(0, 0, 121 - dx, 180);
                InputPanel.Margin = new Thickness(0, 0, 126 - dx, 520);
                SpeechBubble.Margin = new Thickness(0, 0, 125 - dx, 550);
            }
            else
            {
                var dx = EffAvatarDetachedOffsetX();
                var dy = EffAvatarDetachedOffsetY();
                // Detached nudge: 20px higher (bottom margin +20, bottom-aligned) and net 5px left
                // (right margin +10 — element is HorizontalAlignment=Center, so offset is (L-R)/2).
                AvatarBorder.Margin = new Thickness(5, 100, 436 - dx, 228 + dy);
                TitleBox.Margin = new Thickness(0, 0, 416 - dx, 193);
                InputPanel.Margin = new Thickness(0, 0, 426 - dx, 520);
                SpeechBubble.Margin = new Thickness(0, 0, 425 - dx, 550);
            }
        }

        /// <summary>
        /// Switches to the avatar set corresponding to a companion.
        /// Called when user clicks a companion in the Companion tab.
        /// </summary>
        public void SwitchToCompanionAvatar(Models.CompanionId companionId)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SwitchToCompanionAvatar(companionId));
                return;
            }

            int targetSet = GetAvatarSetForCompanion(companionId);

            // Only switch if set is unlocked
            int playerLevel = App.Settings?.Current?.PlayerLevel ?? 1;
            if (IsAvatarSetUnlocked(targetSet, playerLevel))
            {
                SwitchToAvatarSet(targetSet, animate: true);
            }
        }

        /// <summary>
        /// Update navigation arrow visibility based on unlocked avatars
        /// </summary>
        /// <summary>
        /// The avatar sets the selector should cycle through. In portrait mode these are the manifest
        /// skins (1..SkinCount); otherwise the mod-supported unlocked sets.
        /// </summary>
        private int[] EffectiveAvatarSets()
        {
            // Mods whose only avatar is a single animated emote (BambiSleep, Sissy): one fixed set,
            // no picker — overrides portrait skins and level-gated sets so the nav arrows hide.
            if (IsSingleEmoteAvatarMod(out int onlySet)) return new[] { onlySet };

            if (_portraitMode && _portraitSet != null && _portraitSet.SkinCount > 0)
            {
                var arr = new int[_portraitSet.SkinCount];
                for (int i = 0; i < arr.Length; i++) arr[i] = i + 1;
                return arr;
            }
            return GetUnlockedAvatarSets(App.Settings?.Current?.PlayerLevel ?? 1);
        }

        private void UpdateNavigationArrows()
        {
            var unlockedSets = EffectiveAvatarSets();
            bool hasMultiple = unlockedSets.Length > 1;
            int currentIndex = System.Array.IndexOf(unlockedSets, _currentAvatarSet);

            // Previous arrow: show if not at first unlocked set
            BtnPrevAvatar.Visibility = hasMultiple && currentIndex > 0
                ? Visibility.Visible : Visibility.Collapsed;

            // Next arrow: show if not at last unlocked set
            BtnNextAvatar.Visibility = hasMultiple && currentIndex < unlockedSets.Length - 1
                ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Apply size and position transforms for different avatar sets
        /// Sets 2, 3, 4 are 12% bigger and 10px to the right
        /// </summary>
        private void ApplyAvatarTransform(int setNumber)
        {
            // Portrait mode: all skins render at the same (already-reduced) size — skip the per-set
            // +12% border zoom so base/lingerie/beach/fishnet stay consistent, and raise the avatar.
            if (_portraitMode)
            {
                AvatarBorder.RenderTransform = new System.Windows.Media.TranslateTransform(PortraitShiftX, -PortraitRaisePx);
                AvatarBorder.RenderTransformOrigin = new Point(0.5, 0.5);
                return;
            }

            if (setNumber > 1)
            {
                // Sets 2, 3, 4: 12% bigger, 10px to the right
                var transformGroup = new TransformGroup();
                transformGroup.Children.Add(new ScaleTransform(1.12, 1.12));
                transformGroup.Children.Add(new TranslateTransform(10, 0));
                AvatarBorder.RenderTransform = transformGroup;
                AvatarBorder.RenderTransformOrigin = new Point(0.5, 0.5);
            }
            else if (App.Mods?.ActiveModId == Models.BuiltInMods.LockedId)
            {
                // Locked's set 1 ("The Lure") art reads smaller than the other stages
                // (which get the +12% above); nudge it 6% bigger to match.
                AvatarBorder.RenderTransform = new ScaleTransform(1.06, 1.06);
                AvatarBorder.RenderTransformOrigin = new Point(0.5, 0.5);
            }
            else
            {
                // Set 1 (Basic Bimbo): no transform
                AvatarBorder.RenderTransform = null;
            }
        }

        /// <summary>
        /// Navigate to previous avatar set
        /// </summary>
        private void BtnPrevAvatar_Click(object sender, MouseButtonEventArgs e)
        {
            var unlockedSets = EffectiveAvatarSets();
            int currentIndex = System.Array.IndexOf(unlockedSets, _currentAvatarSet);
            if (currentIndex > 0)
            {
                SwitchToAvatarSet(unlockedSets[currentIndex - 1]);
                // User-initiated appearance/skin change (the arrows also repoint the portrait skin).
                try { App.Bark?.NotifyAvatarChanged(); } catch { }
            }
        }

        /// <summary>
        /// Navigate to next avatar set
        /// </summary>
        private void BtnNextAvatar_Click(object sender, MouseButtonEventArgs e)
        {
            var unlockedSets = EffectiveAvatarSets();
            int currentIndex = System.Array.IndexOf(unlockedSets, _currentAvatarSet);
            if (currentIndex >= 0 && currentIndex < unlockedSets.Length - 1)
            {
                SwitchToAvatarSet(unlockedSets[currentIndex + 1]);
                // User-initiated appearance/skin change (the arrows also repoint the portrait skin).
                try { App.Bark?.NotifyAvatarChanged(); } catch { }
            }
        }

        private void OnFirstContentRendered(object? sender, EventArgs e)
        {
            // One-shot: clear the subscription so this only runs after the
            // very first composition.
            ContentRendered -= OnFirstContentRendered;
            try
            {
                // The window has now auto-sized (SizeToContent=WidthAndHeight) to the correct
                // dimensions against the composed Viewbox. Capture that size and turn auto-sizing
                // OFF permanently.
                //
                // WHY: this is a layered window (AllowsTransparency=True). While SizeToContent is
                // active, WPF hooks HwndSource.OnLayoutUpdated → Resize, and for a layered window that
                // resize runs a SYNCHRONOUS HwndTarget.OnResize → MediaContext.CompleteRender() — a
                // blocking present that waits on the render thread. When the render thread is busy
                // (e.g. the avatar emote GIF crossfade: two ~1.8 MB layers at 15 fps with blurred drop
                // shadows on a CPU-composited surface), that present deadlocks and the whole app hangs
                // ("not responding"), then Windows force-closes it. Diagnosed 2026-06-10 from live hang
                // dumps: every emote-mode mod/set switch could freeze the UI in CompleteRender.
                //
                // The window's size is driven explicitly by ContentViewbox.Width/Height (DPI × user
                // scale) — see CalculateScaleFactor()/ApplyScale() — so auto-sizing is unnecessary.
                // The toggle below also doubles as the original first-paint re-measure that flushes the
                // layered surface (it was previously toggled Manual→back; now it stays Manual).
                if (ActualWidth > 0 && ActualHeight > 0)
                {
                    Width = ActualWidth;
                    Height = ActualHeight;
                }
                SizeToContent = SizeToContent.Manual;

                // Belt-and-suspenders: also flush the Win32 frame so the
                // layered window picks up any cached style/size deltas from
                // the SetWindowLong(WS_EX_TOOLWINDOW) call in OnLoaded.
                if (_tubeHandle != IntPtr.Zero)
                {
                    SetWindowPos(_tubeHandle, IntPtr.Zero, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
                }

                InvalidateVisual();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("AvatarTube first-paint kick failed: {Error}", ex.Message);
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _tubeHandle = new WindowInteropHelper(this).Handle;
            _parentHandle = new WindowInteropHelper(_parentWindow).Handle;

            // Once auto-sizing is turned off after first paint (OnFirstContentRendered), the window
            // no longer follows its content, so keep its size pinned to the explicitly-sized Viewbox.
            // This is what lets us run SizeToContent=Manual and avoid the layered-window
            // OnResize→CompleteRender hang (see OnFirstContentRendered for the full rationale).
            ContentViewbox.SizeChanged += (_, __) =>
            {
                if (SizeToContent != SizeToContent.Manual) return; // startup auto-size phase: let WPF do it
                if (double.IsNaN(ContentViewbox.Width) || ContentViewbox.Width <= 0) return;
                if (Width != ContentViewbox.Width) Width = ContentViewbox.Width;
                if (Height != ContentViewbox.Height) Height = ContentViewbox.Height;
            };

            // Hook window messages (minimal hook, no z-order forcing)
            _hwndSource = HwndSource.FromHwnd(_tubeHandle);
            _hwndSource?.AddHook(WndProc);

            // Hook the parent window's messages too. The keep-on-top timer polls at
            // Background priority and gets starved exactly when AI speech is busy
            // (GIF animation, text streaming, effects firing) — so the bubble can sit
            // behind main for noticeably longer than the 300ms tick. Reacting to the
            // parent's own WM_WINDOWPOSCHANGED lifts the tube back the instant main
            // moves up in z-order, with no polling gap.
            if (_parentHandle != IntPtr.Zero)
            {
                _parentHwndSource = HwndSource.FromHwnd(_parentHandle);
                _parentHwndSource?.AddHook(ParentWndProc);
            }

            // Hide from Alt+Tab by adding WS_EX_TOOLWINDOW style.
            // SetWindowLong caches frame data; without a follow-up SetWindowPos
            // with SWP_FRAMECHANGED the layered window (AllowsTransparency=True)
            // doesn't get a WM_NCCALCSIZE/WM_PAINT pass and the tube stays
            // invisible until the user moves the window. Forcing the frame
            // recalc here gives it that initial paint.
            int exStyle = GetWindowLong(_tubeHandle, GWL_EXSTYLE);
            SetWindowLong(_tubeHandle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
            SetWindowPos(_tubeHandle, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

            // Ensure NOT topmost when attached (starts attached)
            Topmost = false;

            // Calculate scale factor based on screen size and DPI
            CalculateScaleFactor();

            // Defer position update to ensure layout is complete
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_parentWindow.IsVisible && _parentWindow.WindowState != WindowState.Minimized)
                {
                    UpdatePosition();
                    StartFloatingAnimation();
                    // Force on first show: at startup foreground may not have transferred
                    // to us yet, so the gated raise would bail and the tube would come up
                    // behind the main window.
                    BringAttachedPairToFront(force: true);
                }

                            // Reset bubble position to ensure correct placement after layout
                            // Anchored at bottom, grows upward. Margin = left, top, right, bottom
                            var initUseAttached = _isAttached || ModOverridesAttachedTubeOnly();
                            var initDx = initUseAttached
                                ? EffAvatarOffsetX()
                                : EffAvatarDetachedOffsetX();
                            var initRight = initUseAttached ? 125 - initDx : 425 - initDx;
                            SpeechBubble.Margin = new Thickness(0, 0, initRight, 550);
                        }), System.Windows.Threading.DispatcherPriority.Loaded);

            // Start fullscreen detection timer
            StartFullscreenDetection();
        }

        /// <summary>
        /// Start monitoring for fullscreen applications
        /// </summary>
        private void StartFullscreenDetection()
        {
            _fullscreenCheckTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500) // Check every 500ms
            };
            _fullscreenCheckTimer.Tick += FullscreenCheckTimer_Tick;
            _fullscreenCheckTimer.Start();
        }

        private void FullscreenCheckTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                bool isOtherAppFullscreen = IsOtherAppFullscreen();

                // When DETACHED, avatar should stay visible as a widget overlay
                // Only hide for fullscreen when ATTACHED
                if (_isAttached)
                {
                    if (isOtherAppFullscreen && !_hiddenForFullscreen)
                    {
                        // Another app went fullscreen - hide the avatar (attached mode only)
                        _hiddenForFullscreen = true;
                        _wasAttachedBeforeFullscreen = _isAttached;
                        Hide();
                        App.Logger?.Debug("Avatar hidden - fullscreen app detected (attached mode)");
                    }
                    else if (!isOtherAppFullscreen && _hiddenForFullscreen)
                    {
                        // Fullscreen app closed - restore the avatar.
                        // IMPORTANT: only clear the flag once we actually Show(). If the parent
                        // is momentarily minimized/hidden during the fullscreen-exit transition
                        // (common when leaving an exclusive-fullscreen game), clearing the flag
                        // here without showing would leave the avatar stuck hidden forever - the
                        // hide-branch can't re-fire (no fullscreen) and this branch can't re-fire
                        // (flag cleared). Keeping the flag set lets us retry on the next tick.
                        if (_parentWindow.IsVisible && _parentWindow.WindowState != WindowState.Minimized
                            && App.Settings?.Current?.AvatarEnabled == true)
                        {
                            _hiddenForFullscreen = false;
                            Show();
                            if (_wasAttachedBeforeFullscreen && _isAttached)
                            {
                                UpdatePosition();
                            }
                            App.Logger?.Debug("Avatar restored - fullscreen app closed");
                        }
                    }
                }
                else
                {
                    // DETACHED mode - periodically reassert topmost to stay visible as widget
                    // This handles cases where other topmost windows or focus changes demote us
                    if (_hiddenForFullscreen && App.Settings?.Current?.AvatarEnabled == true)
                    {
                        _hiddenForFullscreen = false;
                        Show();
                    }
                    ReassertTopmost();
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Error checking fullscreen state");
            }
        }

        /// <summary>
        /// Check if another application (not our app) is running in EXCLUSIVE fullscreen mode.
        /// This is conservative - only hides for true DirectX/OpenGL exclusive fullscreen,
        /// NOT for borderless windowed games or browser video fullscreen.
        /// </summary>
        private bool IsOtherAppFullscreen()
        {
            try
            {
                IntPtr foregroundWindow = GetForegroundWindow();
                if (foregroundWindow == IntPtr.Zero) return false;

                // Check if it's our own window
                if (foregroundWindow == _tubeHandle || foregroundWindow == _parentHandle)
                    return false;

                // Get the process ID of the foreground window
                GetWindowThreadProcessId(foregroundWindow, out uint foregroundPid);
                uint ourPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                if (foregroundPid == ourPid)
                    return false;

                // Get window class name to exclude known safe applications
                var className = new System.Text.StringBuilder(256);
                GetClassName(foregroundWindow, className, className.Capacity);
                string windowClass = className.ToString();

                // Exclude browsers and common media applications - these use "fake" fullscreen
                // that covers the screen but isn't exclusive DirectX/OpenGL fullscreen
                string[] safeClasses = {
                    "Chrome_WidgetWin",      // Chrome, Edge (Chromium), Brave, etc.
                    "MozillaWindowClass",    // Firefox
                    "ApplicationFrameWindow", // UWP apps (Netflix, Disney+, etc.)
                    "Windows.UI.Core",       // Modern Windows apps
                    "CabinetWClass",         // Windows Explorer
                    "Shell_TrayWnd",         // Taskbar
                    "Progman",               // Desktop
                    "WorkerW",               // Desktop worker
                    "XLMAIN",                // Excel
                    "OpusApp",               // Word
                    "PPTFrameClass",         // PowerPoint
                    "VLC",                   // VLC media player
                    "mpv",                   // mpv player
                    "MediaPlayerClassicW",   // MPC
                };

                foreach (var safeClass in safeClasses)
                {
                    if (windowClass.StartsWith(safeClass, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                // Get the window style
                int style = GetWindowLong(foregroundWindow, GWL_STYLE);
                int exStyle = GetWindowLong(foregroundWindow, GWL_EXSTYLE);

                bool hasCaption = (style & WS_CAPTION) == WS_CAPTION;
                bool isPopup = (style & WS_POPUP) == WS_POPUP;
                bool isTopmost = (exStyle & WS_EX_TOPMOST) == WS_EX_TOPMOST;

                // If the window has a caption (title bar), it's definitely not exclusive fullscreen
                if (hasCaption)
                    return false;

                // For exclusive fullscreen, we require BOTH:
                // 1. Window is popup style (no borders) AND
                // 2. Window is topmost (exclusive fullscreen apps set this)
                // This excludes borderless windowed games which usually aren't topmost
                if (!isPopup || !isTopmost)
                    return false;

                // Get the window rect
                if (!GetWindowRect(foregroundWindow, out RECT windowRect))
                    return false;

                // Get FULL screen bounds (not working area - must cover taskbar too)
                var screen = System.Windows.Forms.Screen.FromHandle(foregroundWindow);
                var screenBounds = screen.Bounds;

                // For true fullscreen, window must cover the ENTIRE screen including taskbar
                int tolerance = 5;
                bool coversFullScreen =
                    windowRect.Left <= screenBounds.Left + tolerance &&
                    windowRect.Top <= screenBounds.Top + tolerance &&
                    windowRect.Right >= screenBounds.Right - tolerance &&
                    windowRect.Bottom >= screenBounds.Bottom - tolerance;

                if (coversFullScreen)
                {
                    App.Logger?.Debug("Exclusive fullscreen detected: class={Class}, popup={Popup}, topmost={Topmost}",
                        windowClass, isPopup, isTopmost);
                }

                return coversFullScreen;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Window procedure hook (minimal - no longer forcing z-order to allow normal window switching)
        /// </summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // No longer intercepting z-order changes - let Windows handle it normally
            return IntPtr.Zero;
        }

        /// <summary>
        /// Hook on the PARENT (main) window. When main's z-order changes, lift the tube
        /// back above it immediately so the avatar/speech bubble never gets buried behind
        /// main's UI. This is the event-driven counterpart to the (Background-priority,
        /// pollable-to-starvation) keep-on-top timer — it fires synchronously the moment
        /// main moves up, closing the gap the timer leaves during busy AI speech.
        /// </summary>
        private IntPtr ParentWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_WINDOWPOSCHANGED) return IntPtr.Zero;
            if (!_isAttached || _tubeHandle == IntPtr.Zero) return IntPtr.Zero;
            if (_reassertingAboveParent) return IntPtr.Zero; // guard against re-entrancy

            try
            {
                // Only react when the z-order actually changed (ignore pure move/resize —
                // those are already handled by LocationChanged/SizeChanged).
                var wp = Marshal.PtrToStructure<WINDOWPOS>(lParam);
                if ((wp.flags & SWP_NOZORDER) != 0) return IntPtr.Zero;

                // Don't fight pop quiz (it owns HWND_TOPMOST), and don't pop over other
                // apps — only lift the tube when our own app owns the foreground.
                if (PopQuizWindow.IsOpen || QuizWindow.IsOpen) return IntPtr.Zero;
                if (!IsOurAppForeground()) return IntPtr.Zero;

                // Place the tube directly above main. Moving the tube only triggers
                // WM_WINDOWPOSCHANGED on the TUBE (its WndProc is a no-op), not on the
                // parent, so this can't loop — but guard anyway for safety.
                _reassertingAboveParent = true;
                SetWindowPos(_tubeHandle, HWND_TOP, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
            catch { /* parent may be tearing down */ }
            finally { _reassertingAboveParent = false; }

            return IntPtr.Zero;
        }

        /// <summary>
        /// True when the foreground window belongs to our process. Used to gate z-order
        /// raises so we lift the tube only when our app is actually in front — never
        /// stealing z-order from other apps (e.g. a fullscreen video player).
        /// </summary>
        private bool IsOurAppForeground()
        {
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero) return false;
            if (foreground == _parentHandle || foreground == _tubeHandle) return true;
            GetWindowThreadProcessId(foreground, out uint foregroundPid);
            uint ourPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            return foregroundPid == ourPid;
        }

        private void CalculateScaleFactor()
        {
            try
            {
                // Get DPI scaling
                var source = PresentationSource.FromVisual(this);
                double dpiScale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

                // Get primary screen working area
                var screen = System.Windows.Forms.Screen.PrimaryScreen;
                if (screen == null) return;

                double screenHeight = screen.WorkingArea.Height / dpiScale;
                double screenWidth = screen.WorkingArea.Width / dpiScale;

                // Calculate max scale that fits on screen (leave some margin)
                double maxHeightScale = (screenHeight * 0.85) / DesignHeight;
                double maxWidthScale = (screenWidth * 0.3) / DesignWidth; // Tube shouldn't be more than 30% of screen width

                _scaleFactor = Math.Min(maxHeightScale, maxWidthScale);
                _scaleFactor = Math.Max(0.4, Math.Min(1.0, _scaleFactor)); // Clamp between 40% and 100%

                // Apply scale to viewbox
                ContentViewbox.Width = DesignWidth * _scaleFactor;
                ContentViewbox.Height = DesignHeight * _scaleFactor;

                App.Logger?.Information("AvatarTube scale factor: {Scale:F2} (Screen: {W}x{H}, DPI: {DPI:F2})",
                    _scaleFactor, screenWidth, screenHeight, dpiScale);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to calculate scale factor: {Error}", ex.Message);
                _scaleFactor = 0.7; // Safe default for smaller screens
                ContentViewbox.Width = DesignWidth * _scaleFactor;
                ContentViewbox.Height = DesignHeight * _scaleFactor;
            }
        }

        /// <summary>
        /// Ensure the window is visible when detached - acts as a persistent widget
        /// </summary>
        private void EnsureVisibleWhenDetached()
        {
            if (!_isAttached)
            {
                Show();
                // Reassert topmost so avatar stays visible as a widget overlay
                ReassertTopmost();
            }
        }

        /// <summary>
        /// Toggle the WS_EX_TOOLWINDOW style (controls Alt+Tab visibility)
        /// </summary>
        private void SetToolWindowStyle(bool isToolWindow)
        {
            if (_tubeHandle == IntPtr.Zero) return;

            int exStyle = GetWindowLong(_tubeHandle, GWL_EXSTYLE);
            if (isToolWindow)
            {
                SetWindowLong(_tubeHandle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
            }
            else
            {
                SetWindowLong(_tubeHandle, GWL_EXSTYLE, exStyle & ~WS_EX_TOOLWINDOW);
            }
            // SetWindowLong frame data is cached — flush with SWP_FRAMECHANGED
            // so the new style takes effect without requiring a window move.
            SetWindowPos(_tubeHandle, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }

        private DispatcherTimer? _floatTimer;
        private double _floatPhase = 0;

        private void StartFloatingAnimation()
        {
            // Stop any existing animation first
            StopFloatingAnimation();

            // Use a timer-based approach instead of WPF animations for maximum reliability
            // This won't interfere with other animations on the element
            _floatPhase = 0;
            _floatTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16) // ~60fps
            };
            _floatTimer.Tick += (s, e) =>
            {
                // Sine wave oscillation (the existing gentle vertical bob)
                _floatPhase += 0.05; // Speed of oscillation
                var y = Math.Sin(_floatPhase) * FloatDistance;
                AvatarTranslate.Y = y;

                // Portrait mode adds breathing (subtle scale), wobble (subtle rotation), and a drifting
                // pink mist. Written every tick to BOTH portrait layers so the incoming crossfade image
                // isn't snapped back to neutral mid-pulse. Crossfade lives on Opacity (orthogonal to these
                // transform DPs), so nothing fights the 60fps writes. Legacy mods skip this block entirely.
                if (_portraitMode)
                {
                    _breathPhase += 0.013;
                    _wobblePhase += 0.017;
                    _mistPhase += 0.009;

                    double scale = 1.0 + Math.Sin(_breathPhase) * BreathAmplitude;
                    double angle = Math.Sin(_wobblePhase) * WobbleAmplitudeDeg;
                    double xJit = 0.0;

                    // While a clip is actually playing: add a faster vibration + a tiny horizontal jitter,
                    // so she visibly "talks". Stops the instant the audio ends (_isSpeakingAudio flips false).
                    bool speaking = _isSpeakingAudio;
                    if (speaking)
                    {
                        _speakPhase += 0.45;       // fast carrier
                        _speakEnvPhase += 0.035;   // slow envelope (~3s) → intermittent bursts
                        // env is ~0 most of the time and briefly swells toward 1 → occasional, barely-there shimmer.
                        double env = Math.Pow(Math.Max(0.0, Math.Sin(_speakEnvPhase)), 3);
                        double vib = Math.Sin(_speakPhase) * env;
                        angle += vib * SpeakWobbleDeg;
                        xJit = vib * SpeakShakePx;
                    }

                    AvatarScale.ScaleX = AvatarScale.ScaleY = scale;
                    AvatarRotate.Angle = angle;
                    AvatarTranslate.X = xJit;
                    AvatarScaleB.ScaleX = AvatarScaleB.ScaleY = scale;
                    AvatarRotateB.Angle = angle;
                    AvatarTranslateB.X = xJit;
                    AvatarTranslateB.Y = y; // layer B bobs in lockstep with layer A

                    if (MistOverlay.Visibility == Visibility.Visible)
                    {
                        // Pink mist drifts over the avatar; thicker + livelier while she's speaking.
                        double mistBase = speaking ? 0.26 : 0.15;
                        double mistAmp = speaking ? 0.10 : 0.06;
                        double driftSpeed = speaking ? 1.0 : 0.7;
                        MistOverlay.Opacity = mistBase + (Math.Sin(_mistPhase) + 1.0) * 0.5 * mistAmp;
                        double mistScale = 1.0 + (Math.Sin(_mistPhase * driftSpeed) + 1.0) * 0.5 * (speaking ? 0.06 : 0.04);
                        MistScale.ScaleX = MistScale.ScaleY = mistScale;
                    }
                }
            };
            _floatTimer.Start();
        }

        private void StopFloatingAnimation()
        {
            _floatTimer?.Stop();
            _floatTimer = null;
            AvatarTranslate.Y = 0;
            // Reset portrait-mode transforms so a stopped avatar isn't frozen mid-breath/wobble.
            if (_portraitMode)
            {
                AvatarScale.ScaleX = AvatarScale.ScaleY = 1.0;
                AvatarRotate.Angle = 0;
                AvatarScaleB.ScaleX = AvatarScaleB.ScaleY = 1.0;
                AvatarRotateB.Angle = 0;
                AvatarTranslateB.Y = 0;
            }
        }

        // ════════════════════════════════════════════════════════════════════════════════
        //  EMOTIVE PORTRAIT AVATAR (mod-agnostic; on only when the active mod ships a manifest)
        // ════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// True when the active mod ships an avatar_manifest.json. A portrait manifest WINS over the
        /// legacy animated GIF — a mod that ships emotive portraits wants them, not the generic GIF —
        /// so this intentionally does not check <see cref="_useAnimatedAvatar"/>.
        /// </summary>
        private bool UsePortraitSystem()
        {
            return Services.AvatarPortraitLoader.HasManifestForActiveMod();
        }

        /// <summary>
        /// Switch the avatar into the emotive-portrait system if the active mod ships a manifest;
        /// otherwise leave the legacy 4-pose path untouched. Idempotent — safe to call on ctor,
        /// mod-change, and set-switch.
        /// </summary>
        private void TryEnterPortraitMode()
        {
            try
            {
                var set = Services.AvatarPortraitLoader.Load();
                if (set == null) { LeavePortraitMode(); return; }

                _portraitSet = set;
                _portraitMode = true;
                _useAnimatedAvatar = false; // portraits replace the legacy animated GIF for this mod

                if (_poseSeqTimer == null)
                {
                    _poseSeqTimer = new DispatcherTimer();
                    _poseSeqTimer.Tick += PoseSeqTimer_Tick;
                }
                if (_emotionReturnTimer == null)
                {
                    _emotionReturnTimer = new DispatcherTimer();
                    _emotionReturnTimer.Tick += EmotionReturnTimer_Tick;
                }

                _activeImg = ImgAvatar;
                _idleImg = ImgAvatarB;
                _skinIndex = _portraitSet.ClampSkin(_selectedAvatarSet - 1);
                _currentEmotion = _portraitSet.IdleEmotion;
                _emotionPoseIndex = 0;

                // Both portrait layers visible; A opaque, B transparent. Animated hidden.
                ImgAvatarAnimated.Visibility = Visibility.Collapsed;
                AnimationBehavior.SetSourceUri(ImgAvatarAnimated, null);
                ImgAvatar.Visibility = Visibility.Visible;
                ImgAvatarB.Visibility = Visibility.Visible;
                CancelCrossfade();
                ImgAvatar.Opacity = 1.0;
                ImgAvatarB.Opacity = 0.0;
                MistOverlay.Visibility = Visibility.Visible;
                ApplyPortraitChrome();

                var bucket = _portraitSet.GetBucket(_skinIndex, _currentEmotion);
                if (bucket.Length > 0) _activeImg.Source = bucket[0];

                // No idle rotation: the avatar holds a still pose until it speaks.
                _poseTimer.Stop();

                // Refresh title (skin name) + nav arrows now that portrait mode is active — the ctor ran
                // UpdateTitleDisplay before this, so without this they'd show the stale legacy set title.
                UpdateTitleDisplay(App.Settings?.Current?.PlayerLevel ?? 1);
                UpdateNavigationArrows();

                App.Logger?.Information("Avatar portrait mode ON (skin {Skin}/{Count}, emotion '{Emo}')",
                    _skinIndex, _portraitSet.SkinCount, _currentEmotion);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "TryEnterPortraitMode failed; falling back to legacy avatar");
                LeavePortraitMode();
            }
        }

        /// <summary>
        /// Portrait-mode chrome: shrink the avatar to <see cref="PortraitSizeScale"/> and drop the pink
        /// DropShadow glow (the deliberate mist overlay already supplies pink atmosphere; the glow read as
        /// an unwanted aura on the detailed portraits). Saves the original effects once for restore. Idempotent.
        /// </summary>
        private void ApplyPortraitChrome()
        {
            if (!_avatarEffectsSaved)
            {
                _savedEffectA = ImgAvatar.Effect;
                _savedEffectB = ImgAvatarB.Effect;
                _avatarEffectsSaved = true;
            }
            ImgAvatar.Effect = null;
            ImgAvatarB.Effect = null;
            ImgAvatar.MaxHeight = ImgAvatarB.MaxHeight = LegacyAvatarMaxHeight * PortraitSizeScale;
            ImgAvatar.MaxWidth = ImgAvatarB.MaxWidth = LegacyAvatarMaxWidth * PortraitSizeScale;
            // Uniform size across skins (no per-set zoom) + nudge the portrait up/right in the tube.
            AvatarBorder.RenderTransform = new System.Windows.Media.TranslateTransform(PortraitShiftX, -PortraitRaisePx);
            AvatarBorder.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        /// <summary>Tear down the portrait system and restore the legacy single-image avatar. Idempotent.</summary>
        private void LeavePortraitMode()
        {
            _portraitMode = false;
            _portraitSet = null;
            _poseSeqTimer?.Stop();
            _emotionReturnTimer?.Stop();
            CancelCrossfade();
            try
            {
                if (ImgAvatarB != null)
                {
                    ImgAvatarB.BeginAnimation(OpacityProperty, null);
                    ImgAvatarB.Visibility = Visibility.Collapsed;
                    ImgAvatarB.Opacity = 0.0;
                    ImgAvatarB.Source = null;
                }
                if (MistOverlay != null) MistOverlay.Visibility = Visibility.Collapsed;
                if (AvatarScale != null) { AvatarScale.ScaleX = AvatarScale.ScaleY = 1.0; }
                if (AvatarRotate != null) AvatarRotate.Angle = 0;
                if (ImgAvatar != null) { ImgAvatar.BeginAnimation(OpacityProperty, null); ImgAvatar.Opacity = 1.0; }

                // Restore legacy chrome: original pink glow + full size.
                if (_avatarEffectsSaved)
                {
                    if (ImgAvatar != null) ImgAvatar.Effect = _savedEffectA;
                    if (ImgAvatarB != null) ImgAvatarB.Effect = _savedEffectB;
                }
                if (ImgAvatar != null) { ImgAvatar.MaxHeight = LegacyAvatarMaxHeight; ImgAvatar.MaxWidth = LegacyAvatarMaxWidth; }
                if (ImgAvatarB != null) { ImgAvatarB.MaxHeight = LegacyAvatarMaxHeight; ImgAvatarB.MaxWidth = LegacyAvatarMaxWidth; }
            }
            catch { /* closing/teardown — non-fatal */ }
            _activeImg = null;
            _idleImg = null;
        }

        /// <summary>Repoint the current skin (after the selector changes set) without re-parsing the manifest.</summary>
        private void ReloadPortraitSkin()
        {
            if (_portraitSet == null) return;
            CancelCrossfade();
            _activeImg = ImgAvatar;
            _idleImg = ImgAvatarB;
            ImgAvatar.Visibility = Visibility.Visible;
            ImgAvatarB.Visibility = Visibility.Visible;
            MistOverlay.Visibility = Visibility.Visible;
            ImgAvatar.Opacity = 1.0;
            ImgAvatarB.Opacity = 0.0;
            ApplyPortraitChrome();
            var bucket = _portraitSet.GetBucket(_skinIndex, _currentEmotion);
            if (bucket.Length > 0)
            {
                if (_emotionPoseIndex >= bucket.Length) _emotionPoseIndex = 0;
                _activeImg.Source = bucket[_emotionPoseIndex];
            }
        }

        /// <summary>Cancel any in-flight image crossfade animation and clear the guard.</summary>
        private void CancelCrossfade()
        {
            try
            {
                ImgAvatar?.BeginAnimation(OpacityProperty, null);
                ImgAvatarB?.BeginAnimation(OpacityProperty, null);
            }
            catch { }
            _crossfadeInFlight = false;
        }

        /// <summary>
        /// Crossfade the visible portrait to <paramref name="next"/> by fading layer A out and layer B in
        /// (then swapping their roles). Idle ticks no-op while a fade is in flight; an event with
        /// <paramref name="preempt"/> cancels the in-flight fade and switches cleanly.
        /// </summary>
        private void CrossfadeTo(BitmapImage? next, bool preempt = false)
        {
            if (next == null || _activeImg == null || _idleImg == null) return;

            if (_crossfadeInFlight)
            {
                if (!preempt) return;
                _activeImg.BeginAnimation(OpacityProperty, null);
                _idleImg.BeginAnimation(OpacityProperty, null);
                _activeImg.Opacity = 0.0;
                _idleImg.Opacity = 1.0;
                var swap = _activeImg; _activeImg = _idleImg; _idleImg = swap;
                _crossfadeInFlight = false;
            }

            if (ReferenceEquals(_activeImg.Source, next)) return; // already showing it

            var inImg = _idleImg;
            var outImg = _activeImg;
            inImg.Source = next;
            inImg.Opacity = 0.0;
            _crossfadeInFlight = true;

            int frames = _portraitSet?.Director.CrossfadeFrames ?? 4;
            var dur = TimeSpan.FromMilliseconds(Math.Max(60, frames * 38)); // 4 frames ≈ 150ms

            var fadeOut = new DoubleAnimation(1, 0, dur) { FillBehavior = FillBehavior.Stop };
            var fadeIn = new DoubleAnimation(0, 1, dur) { FillBehavior = FillBehavior.Stop };
            fadeIn.Completed += (s, e) =>
            {
                inImg.Opacity = 1.0;
                outImg.Opacity = 0.0;
                _activeImg = inImg;
                _idleImg = outImg;
                _crossfadeInFlight = false;
            };
            outImg.BeginAnimation(OpacityProperty, fadeOut);
            inImg.BeginAnimation(OpacityProperty, fadeIn);
        }

        /// <summary>
        /// A line is being spoken → drive the portrait through a short pose sequence of that line's emotion.
        /// Mapped bark lines use their manifest emotion; our event + idle affirmation lines (unmapped) get a
        /// seductive mix (mostly alluring, plus entrancing/dreamy/teasing). The number of poses scales with
        /// the line's length. No-op when the portrait system is off.
        /// </summary>
        private void PlayEmotionForLine(string? emotionLineId, string? audioPath, string? text, string? mood = null)
        {
            // Circe pose-1 animated emotes take over the spoken-line reaction (own WebP path).
            if (_circeEmoteMode) { CircePlayEmote(emotionLineId, audioPath, text, mood); return; }
            if (!_portraitMode || _portraitSet == null) return;
            var emotion = _portraitSet.EmotionForLine(emotionLineId);
            if (string.IsNullOrEmpty(emotion))
                // Bark lines carry a mood → map it to an emotion (base layer). Non-bark speech
                // (AI/trigger/canned, mood==null) keeps the alluring affirmation mix for variety.
                emotion = !string.IsNullOrWhiteSpace(mood)
                    ? _portraitSet.EmotionForMood(mood)
                    : PickAffirmationEmotion();
            double durationSec = audioPath != null ? AudioDurationSec(audioPath) : EstimateDurationSec(text);
            SetEmotionSequence(emotion!, PoseCountForDuration(durationSec), durationSec);
        }

        // Unmapped lines (our ~138 event + idle affirmation lines) read as affirmations: lean alluring,
        // mixed with entrancing/dreamy/teasing. GetBucket falls back if a mod lacks one of these.
        private static readonly string[] AffirmationEmotions =
            { "alluring", "alluring", "alluring", "entrancing", "dreamy", "teasing" };
        private string PickAffirmationEmotion() => AffirmationEmotions[_random.Next(AffirmationEmotions.Length)];

        /// <summary>Poses ∝ line length: ~1s each + a ~2s final hold should span the line. 4s ≈ 3 poses.</summary>
        private int PoseCountForDuration(double sec)
        {
            if (sec <= 0) return MinSpeakPoses;
            int n = (int)Math.Round(sec) - 1; // (n-1)*1s + 2s ≈ sec
            return Math.Clamp(n, MinSpeakPoses, MaxSpeakPoses);
        }

        /// <summary>Cached spoken-line length in seconds (NAudio), used to size the pose sequence.</summary>
        private double AudioDurationSec(string? path)
        {
            if (string.IsNullOrEmpty(path)) return 0;
            if (_audioDurCache.TryGetValue(path!, out var cached)) return cached;
            double sec = 0;
            try
            {
                if (System.IO.File.Exists(path))
                    using (var r = new NAudio.Wave.AudioFileReader(path)) sec = r.TotalTime.TotalSeconds;
            }
            catch { sec = 0; }
            _audioDurCache[path!] = sec;
            return sec;
        }

        /// <summary>Rough spoken length for text-only lines (no audio): slow, pause-heavy bimbo read.</summary>
        private double EstimateDurationSec(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 2.5;
            int words = text!.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            return Math.Clamp(0.45 * words + 0.8, 2.0, 7.0);
        }

        /// <summary>
        /// Start the spoken pose sequence for <paramref name="emotion"/>: show the first pose now, then advance
        /// every <see cref="PoseStepMs"/> through <paramref name="poseCount"/> poses (drawn from this emotion's
        /// bucket), holding the last for <see cref="LastPoseLingerMs"/> before returning to a still idle pose.
        /// </summary>
        private void SetEmotionSequence(string emotion, int poseCount, double durationSec)
        {
            if (_portraitSet == null) return;
            var bucket = _portraitSet.GetBucket(_skinIndex, emotion);
            if (bucket.Length == 0) return;

            _currentEmotion = emotion;
            poseCount = Math.Clamp(poseCount, MinSpeakPoses, MaxSpeakPoses);
            _seqOrder = BuildPoseOrder(bucket.Length, poseCount);
            _seqStep = 0;

            // Short lines flip ~2x faster so the poses keep pace with the brief audio.
            bool shortLine = durationSec > 0 && durationSec < ShortLineSec;
            _seqStepMs = shortLine ? (int)(PoseStepMs * ShortSpeedFactor) : PoseStepMs;
            _seqLastMs = shortLine ? (int)(LastPoseLingerMs * ShortSpeedFactor) : LastPoseLingerMs;

            if (_poseSeqTimer == null)
            {
                _poseSeqTimer = new DispatcherTimer();
                _poseSeqTimer.Tick += PoseSeqTimer_Tick;
            }
            _poseSeqTimer.Stop();
            _emotionReturnTimer?.Stop();
            _poseTimer.Stop(); // belt-and-suspenders: never idle-rotate during a spoken sequence

            int first = _seqOrder.Length > 0 ? _seqOrder[0] : 0;
            _emotionPoseIndex = first;
            CrossfadeTo(bucket[first], preempt: true); // preempt so rapid lines switch cleanly

            bool firstIsLast = _seqOrder.Length <= 1;
            _poseSeqTimer.Interval = TimeSpan.FromMilliseconds(firstIsLast ? _seqLastMs : _seqStepMs);
            _poseSeqTimer.Start();
        }

        /// <summary>N-long order of bucket indices: shuffled, avoiding immediate repeats; cycles if N &gt; bucket size.</summary>
        private int[] BuildPoseOrder(int bucketLen, int n)
        {
            if (bucketLen <= 0) return System.Array.Empty<int>();
            var order = new List<int>(n);
            var pool = new List<int>();
            int last = -1;
            while (order.Count < n)
            {
                if (pool.Count == 0)
                {
                    for (int i = 0; i < bucketLen; i++) pool.Add(i);
                    for (int i = pool.Count - 1; i > 0; i--) { int j = _random.Next(i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }
                    if (bucketLen > 1 && pool[0] == last) { (pool[0], pool[1]) = (pool[1], pool[0]); }
                }
                last = pool[0];
                order.Add(pool[0]);
                pool.RemoveAt(0);
            }
            return order.ToArray();
        }

        private void PoseSeqTimer_Tick(object? sender, EventArgs e)
        {
            _poseSeqTimer?.Stop();
            if (!_portraitMode || _portraitSet == null) return;

            _seqStep++;
            if (_seqStep >= _seqOrder.Length)
            {
                ReturnToIdleEmotion(); // last pose's hold elapsed
                return;
            }

            var bucket = _portraitSet.GetBucket(_skinIndex, _currentEmotion);
            if (bucket.Length == 0) { ReturnToIdleEmotion(); return; }

            int idx = _seqOrder[_seqStep] % bucket.Length;
            _emotionPoseIndex = idx;
            CrossfadeTo(bucket[idx], preempt: true);

            bool isLast = _seqStep == _seqOrder.Length - 1;
            _poseSeqTimer!.Interval = TimeSpan.FromMilliseconds(isLast ? _seqLastMs : _seqStepMs);
            _poseSeqTimer.Start();
        }

        private void EmotionReturnTimer_Tick(object? sender, EventArgs e)
        {
            _emotionReturnTimer?.Stop();
            ReturnToIdleEmotion();
        }

        /// <summary>Settle on a still idle pose (one crossfade, then NO further rotation until the next line).</summary>
        private void ReturnToIdleEmotion()
        {
            if (!_portraitMode || _portraitSet == null) return;
            _poseSeqTimer?.Stop();
            _currentEmotion = _portraitSet.IdleEmotion;
            var bucket = _portraitSet.GetBucket(_skinIndex, _currentEmotion);
            if (bucket.Length > 0)
            {
                int idx = _random.Next(bucket.Length); // tiny variety per return; not a continuous rotation
                _emotionPoseIndex = idx;
                CrossfadeTo(bucket[idx], preempt: true);
            }
        }

        /// <summary>
        /// Load avatar poses for a specific set
        /// </summary>
        /// <param name="setNumber">1 = default, 2 = level 20, 3 = level 35, 4 = level 50, 5 = level 125, 6 = level 150</param>
        private BitmapImage[] LoadAvatarPoses(int setNumber = 1)
        {
            var poses = new BitmapImage[4];

            // Determine the resource path based on set number
            // Set 1: avatar_pose1.png - avatar_pose4.png (original)
            // Set 2: avatar2_pose1.png - avatar2_pose4.png (level 20)
            // Set 3: avatar3_pose1.png - avatar3_pose4.png (level 35)
            // Set 4: avatar4_pose1.png - avatar4_pose4.png (level 50)
            // Set 5: avatar5_pose1.png - avatar5_pose4.png (level 125)
            // Set 6: avatar6_pose1.png - avatar6_pose4.png (level 150)
            string prefix = setNumber == 1 ? "avatar_pose" : $"avatar{setNumber}_pose";
            
            for (int i = 0; i < 4; i++)
            {
                try
                {
                    var resolved = Services.ModResourceResolver.ResolveImage($"{prefix}{i + 1}.png");
                    if (resolved is BitmapImage bmp)
                    {
                        poses[i] = bmp.IsFrozen ? bmp : bmp.Clone();
                        if (!poses[i].IsFrozen) poses[i].Freeze();
                    }
                    else
                    {
                        var uri = new Uri($"pack://application:,,,/Resources/{prefix}{i + 1}.png", UriKind.Absolute);
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = uri;
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        poses[i] = bitmap;
                    }
                    
                    App.Logger?.Debug("Loaded avatar pose: {Prefix}{Index}.png", prefix, i + 1);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning("Failed to load avatar pose {Prefix}{Index}: {Error}", prefix, i + 1, ex.Message);
                    
                    // Try to fall back to default avatar set if a higher set fails to load
                    if (setNumber > 1)
                    {
                        try
                        {
                            var fallbackResolved = Services.ModResourceResolver.ResolveImage($"avatar_pose{i + 1}.png");
                            if (fallbackResolved is BitmapImage fbmp)
                            {
                                poses[i] = fbmp.IsFrozen ? fbmp : fbmp.Clone();
                                if (!poses[i].IsFrozen) poses[i].Freeze();
                            }
                            else
                            {
                                var fallbackUri = new Uri($"pack://application:,,,/Resources/avatar_pose{i + 1}.png", UriKind.Absolute);
                                var fallbackBitmap = new BitmapImage();
                                fallbackBitmap.BeginInit();
                                fallbackBitmap.UriSource = fallbackUri;
                                fallbackBitmap.CacheOption = BitmapCacheOption.OnLoad;
                                fallbackBitmap.EndInit();
                                fallbackBitmap.Freeze();
                                poses[i] = fallbackBitmap;
                            }
                            App.Logger?.Debug("Fell back to default avatar pose {Index}", i + 1);
                        }
                        catch
                        {
                            poses[i] = new BitmapImage();
                        }
                    }
                    else
                    {
                        poses[i] = new BitmapImage();
                    }
                }
            }
            
            return poses;
        }

        private void PoseTimer_Tick(object? sender, EventArgs e)
        {
            if (_portraitMode) return; // portrait mode never rotates on idle (poses change only while speaking)

            if (_avatarPoses.Length == 0) return;

            _currentPoseIndex = (_currentPoseIndex + 1) % _avatarPoses.Length;

            // Use FillBehavior.Stop to prevent animations from holding onto the property
            var fadeOut = new DoubleAnimation(1, 0.3, TimeSpan.FromMilliseconds(150))
            {
                FillBehavior = FillBehavior.Stop
            };
            fadeOut.Completed += (s, args) =>
            {
                ImgAvatar.Source = _avatarPoses[_currentPoseIndex];
                ImgAvatar.Opacity = 1.0; // Reset opacity after fade out completes
            };
            ImgAvatar.BeginAnimation(OpacityProperty, fadeOut);
        }

        public void UpdatePosition()
        {
            if (!_isAttached || _parentWindow == null) return;

            // Don't update position if parent window has invalid dimensions (can happen during focus changes)
            if (_parentWindow.ActualHeight <= 0 || _parentWindow.ActualWidth <= 0) return;

            // Don't update if parent window is at origin with zero size (likely transitioning)
            if (_parentWindow.Top == 0 && _parentWindow.Left == 0 && _parentWindow.ActualHeight < 100) return;

            // Get actual window dimensions (scaled)
            double actualWidth = ActualWidth > 0 ? ActualWidth : DesignWidth * _scaleFactor;
            double actualHeight = ActualHeight > 0 ? ActualHeight : DesignHeight * _scaleFactor;

            // Scale the offset based on current scale factor
            double scaledOffset = BaseOffsetFromParent * _scaleFactor;

            // Calculate new position
            double newLeft = _parentWindow.Left - actualWidth - scaledOffset;
            double newTop = _parentWindow.Top + (_parentWindow.ActualHeight - actualHeight) / 2 + (VerticalOffset * _scaleFactor);

            // Sanity check: don't jump to extreme positions (likely invalid data)
            // This prevents the "bounce to top" issue during focus changes
            if (newTop < -500 || newTop > 5000 || newLeft < -2000 || newLeft > 5000) return;

            // Position to the LEFT of the parent window
            Left = newLeft;
            Top = newTop;
        }

        private void ParentWindow_PositionChanged(object? sender, EventArgs e)
        {
            // Skip if parent is null, window is closing, or parent is minimized
            if (_parentWindow == null) return;
            try
            {
                if (_parentWindow.WindowState == WindowState.Minimized) return;
                UpdatePosition();
                // Keep tube in front when attached, during parent move
                if (_isAttached) BringAttachedPairToFront();
            }
            catch { /* Window may be closing */ }
        }

        private void ParentWindow_StateChanged(object? sender, EventArgs e)
        {
            if (_parentWindow == null) return;
            try
            {
                switch (_parentWindow.WindowState)
                {
                    case WindowState.Minimized:
                        PauseAvatarGif();
                        if (_isAttached)
                        {
                            Hide();
                        }
                        else
                        {
                            // When detached, force visibility and topmost
                            EnsureVisibleWhenDetached();
                        }
                        break;
                    case WindowState.Normal:
                    case WindowState.Maximized:
                        ResumeAvatarGif();
                        if (_parentWindow.IsVisible && App.Settings?.Current?.AvatarEnabled == true)
                        {
                            Show();
                            if (_isAttached)
                            {
                                UpdatePosition();
                                BringAttachedPairToFront();
                            }
                            // When detached, WPF Topmost property handles it
                        }
                        break;
                }
            }
            catch { /* Window may be closing */ }
        }

        private void ParentWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_parentWindow == null) return;
            try
            {
                if ((bool)e.NewValue && _parentWindow.WindowState != WindowState.Minimized
                    && App.Settings?.Current?.AvatarEnabled == true)
                {
                    ResumeAvatarGif();
                    Show();
                    if (_isAttached)
                    {
                        UpdatePosition();
                        BringAttachedPairToFront();
                    }
                    // When detached, WPF Topmost property handles it
                }
                else
                {
                    PauseAvatarGif();
                    if (_isAttached)
                    {
                        Hide();
                    }
                    else
                    {
                        // When detached, force visibility and topmost
                        EnsureVisibleWhenDetached();
                    }
                }
            }
            catch { /* Window may be closing */ }
        }

        private void ParentWindow_Activated(object? sender, EventArgs e)
        {
            if (_parentWindow == null) return;

            // Don't do any z-order work when pop quiz is open
            if ((PopQuizWindow.IsOpen || QuizWindow.IsOpen)) return;

            try
            {
                if (_parentWindow.WindowState != WindowState.Minimized && _parentWindow.IsVisible
                    && App.Settings?.Current?.AvatarEnabled == true)
                {
                    Show();
                    UpdatePosition();

                    if (_isAttached)
                    {
                        // Delay BringToFront to ensure it happens AFTER parent activation completes
                        // Use Background priority so all window activation processing finishes first
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if ((PopQuizWindow.IsOpen || QuizWindow.IsOpen)) return;
                            if (_isAttached && _tubeHandle != IntPtr.Zero)
                            {
                                BringAttachedPairToFront();
                            }
                        }), System.Windows.Threading.DispatcherPriority.Background);
                    }
                }
            }
            catch { /* Window may be closing */ }
        }

        private void ParentWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Don't fight z-order when pop quiz is open
            if ((PopQuizWindow.IsOpen || QuizWindow.IsOpen)) return;

            // When main window is clicked (even if already active), immediately bring tube to front
            // This handles the case where Activated event doesn't fire (window already active)
            if (_isAttached && _tubeHandle != IntPtr.Zero && SpeechBubble.Visibility == Visibility.Visible)
            {
                // Use Background priority to ensure this happens after the click processing
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if ((PopQuizWindow.IsOpen || QuizWindow.IsOpen)) return;
                    if (_isAttached && _tubeHandle != IntPtr.Zero && SpeechBubble.Visibility == Visibility.Visible)
                    {
                        BringAttachedPairToFront();
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void ParentWindow_Closed(object? sender, EventArgs e)
        {
            if (_isAttached)
            {
                // Attached mode: close the tube with the main window
                try { Close(); } catch { /* Already closing */ }
            }
            else
            {
                // Detached mode: keep floating independently
                App.Logger?.Information("Main window closed while detached - tube continues floating");
                // Wrap in try-catch in case app is shutting down
                try
                {
                    if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
                    {
                        Giggle("Main window closed! Right-click to dismiss~");
                    }
                }
                catch { /* App shutting down */ }
            }
        }

        // ============================================================
        // PUBLIC METHODS
        // ============================================================

        public void ShowTube()
        {
            try
            {
                // Manual/explicit show (checkbox toggle, tray "Wake Bambi Up", session events)
                // is a deliberate user/system request to make the avatar visible, so clear the
                // fullscreen-hidden flag. Otherwise IsAvatarVisibleOnScreen and the fullscreen
                // timer would still think we're hidden and could fight this show.
                _hiddenForFullscreen = false;

                // When attached, only show if our process owns the foreground window
                if (_isAttached && _parentWindow != null)
                {
                    var foreground = GetForegroundWindow();
                    if (foreground != IntPtr.Zero)
                    {
                        GetWindowThreadProcessId(foreground, out uint foregroundPid);
                        uint ourPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                        if (foregroundPid != ourPid)
                            return; // Don't show tube if our app isn't in front
                    }
                }

                Show();

                // Only update position if parent is visible
                if (_parentWindow != null && _parentWindow.IsVisible && _parentWindow.WindowState != WindowState.Minimized)
                {
                    UpdatePosition();
                    if (_isAttached && !(PopQuizWindow.IsOpen || QuizWindow.IsOpen)) BringAttachedPairToFront();
                }

                StartFloatingAnimation();

                // Ensure TOOLWINDOW style is applied when attached
                if (_isAttached)
                {
                    SetToolWindowStyle(true);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Error showing tube: {Error}", ex.Message);
            }
        }

        public void HideTube()
        {
            Hide();
        }

        public void StartPoseAnimation() => _poseTimer.Start();
        public void StopPoseAnimation() => _poseTimer.Stop();

        public void SetPose(int poseNumber)
        {
            if (poseNumber < 1 || poseNumber > 4) return;
            if (_avatarPoses.Length == 0) return;
            _currentPoseIndex = poseNumber - 1;
            ImgAvatar.Source = _avatarPoses[_currentPoseIndex];
        }

        public void SetPoseInterval(TimeSpan interval)
        {
            _poseTimer.Interval = interval;
        }
        
        /// <summary>
        /// Gets the current avatar set number
        /// </summary>
        public int CurrentAvatarSet => _currentAvatarSet;

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                _poseTimer?.Stop();
                _fullscreenCheckTimer?.Stop();
                StopFloatingAnimation();

                // Stop companion timers
                _speechTimer?.Stop();
                _speechDelayTimer?.Stop();
                _idleTimer?.Stop();
                _triggerTimer?.Stop();
                _randomBubbleTimer?.Stop();

                // Stop voice line audio
                StopVoiceLineAudio();

                // Release GIF animation frames to prevent memory leak
                AnimationBehavior.SetSourceUri(ImgAvatarAnimated, null);

                // Remove window message hooks
                _hwndSource?.RemoveHook(WndProc);
                _hwndSource = null;
                _parentHwndSource?.RemoveHook(ParentWndProc);
                _parentHwndSource = null;

                // Unsubscribe from video service events
                if (App.Video != null)
                {
                    App.Video.VideoAboutToStart -= OnVideoAboutToStart;
                    App.Video.VideoEnded -= OnVideoEnded;
                }

                // LockCardCompleted is owned by BarkService now (no self-subscription to remove).

                // Unsubscribe from game events
                if (App.BubbleCount != null)
                {
                    App.BubbleCount.GameCompleted -= OnGameCompleted;
                    App.BubbleCount.GameFailed -= OnGameFailed;
                }

                // Unsubscribe from flash events
                if (App.Flash != null)
                {
                    App.Flash.FlashAboutToDisplay -= OnFlashAboutToDisplay;
                    App.Flash.FlashClicked -= OnFlashClicked;
                    App.Flash.FlashAudioPlaying -= OnFlashAudioPlaying;
                }

                // Unsubscribe from bubble events
                if (App.Bubbles != null)
                {
                    App.Bubbles.OnBubblePopped -= OnBubblePopped;
                    App.Bubbles.OnBubbleMissed -= OnBubbleMissed;
                }

                // Unsubscribe from achievement events
                if (App.Achievements != null)
                {
                    App.Achievements.AchievementUnlocked -= OnAchievementUnlocked;
                }

                // Unsubscribe from progression events
                if (App.Progression != null)
                {
                    App.Progression.LevelUp -= OnLevelUp;
                }

                // Unsubscribe from window awareness events
                if (App.WindowAwareness != null)
                {
                    App.WindowAwareness.ActivityChanged -= OnActivityChanged;
                    App.WindowAwareness.StillOnActivity -= OnStillOnActivity;
                }

                // Unsubscribe from MindWipe events
                if (App.MindWipe != null)
                {
                    App.MindWipe.MindWipeTriggered -= OnMindWipeTriggered;
                }

                // Unsubscribe from BrainDrain events
                if (App.BrainDrain != null)
                {
                    App.BrainDrain.BrainDrainTriggered -= OnBrainDrainTriggered;
                }

                // Unsubscribe from engine stop event
                if (_parentWindow is MainWindow mainWindow)
                {
                    mainWindow.EngineStopped -= OnEngineStopped;
                }

                if (_parentWindow != null)
                {
                    _parentWindow.LocationChanged -= ParentWindow_PositionChanged;
                    _parentWindow.SizeChanged -= ParentWindow_PositionChanged;
                    _parentWindow.StateChanged -= ParentWindow_StateChanged;
                    _parentWindow.IsVisibleChanged -= ParentWindow_IsVisibleChanged;
                    _parentWindow.Activated -= ParentWindow_Activated;
                    _parentWindow.PreviewMouseDown -= ParentWindow_PreviewMouseDown;
                    _parentWindow.Closed -= ParentWindow_Closed;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Error during tube window cleanup: {Error}", ex.Message);
            }

            base.OnClosed(e);
        }
        
        // Interaction counter for 1-in-4 logic
        private int _interactionCount = 0;
        private DateTime _lastInteractionTime = DateTime.MinValue;
        private int _animationRefreshClickCount = 0;

        private void ImgAvatar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var now = DateTime.Now;

            // Refresh animation every 4 clicks to prevent stuck animations
            _animationRefreshClickCount++;
            if (_animationRefreshClickCount >= 4)
            {
                _animationRefreshClickCount = 0;
                RefreshAvatarAnimation();
            }

            // Track rapid clicks for 50-in-1-minute "Bambi Cum and Collapse" trigger
            _rapidClickTimestamps.Add(now);
            // Remove clicks older than 1 minute
            _rapidClickTimestamps.RemoveAll(t => (now - t).TotalSeconds > 60);

            // Check if 50+ clicks in the last minute
            if (_rapidClickTimestamps.Count >= 50)
            {
                _rapidClickTimestamps.Clear(); // Reset to prevent repeat triggers
                TriggerBambiCumAndCollapse();
            }

            // Track for Neon Obsession achievement (20 rapid clicks)
            App.Achievements?.TrackAvatarClick();

            // Bark hook: rolling 60s click count drives the click-escalation eggs.
            try { App.Bark?.NotifyAvatarClicked(); } catch { }

            // Animated avatar: a click rotates to a rare affectionate emote (3s cooldown). No-op for
            // static/portrait avatars or while cooling down.
            try { CirceClickEmote(); } catch { }

            // 1 in 25 chance to play a pop sound
            if (_random.Next(25) == 0)
            {
                PlayAvatarPopSound();
            }

            // Log click count for debugging
            var clickCount = App.Achievements?.Progress.AvatarClickCount ?? 0;
            App.Logger?.Debug("Avatar clicked! Count: {Count}/20, RapidClicks: {RapidCount}/50", clickCount, _rapidClickTimestamps.Count);

            // Double-click detection — open chat input if AI available, otherwise activity comment
            if ((now - _lastClickTime).TotalMilliseconds < 300)
            {
                if (_isMuted)
                {
                    // Show brief muted indicator so user knows she's not broken
                    ShowMutedIndicator();
                }
                else if (App.Settings?.Current?.AiChatEnabled == true && App.Ai?.IsAvailable == true)
                {
                    // Open the chat input panel (same as "Talk to" menu item)
                    ShowInputPanel();
                }
                else if (_isGiggling || _isWaitingForAi)
                {
                    App.Logger?.Debug("Skipping double-click - message still showing");
                }
                else if ((now - _lastInteractionTime).TotalSeconds >= 1.5)
                {
                    _lastInteractionTime = now;
                    _ = TriggerActivityCommentAsync();
                }
            }
            _lastClickTime = now;

            // Visual feedback - glow pulse on the drop shadow effect
            // Pulse whichever avatar is currently visible
            var activeAvatar = _useAnimatedAvatar ? ImgAvatarAnimated : ImgAvatar;
            if (activeAvatar.Effect is System.Windows.Media.Effects.DropShadowEffect dropShadow)
            {
                // Pulse the blur radius for glow effect (longer duration for visibility)
                var blurPulse = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 20,
                    To = 60,
                    Duration = TimeSpan.FromMilliseconds(200),
                    AutoReverse = true,
                    FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
                };
                dropShadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, blurPulse);

                // Also pulse the opacity for a brighter flash
                var opacityPulse = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0.6,
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(200),
                    AutoReverse = true,
                    FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
                };
                dropShadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, opacityPulse);
            }

            // Bouncy squash-and-settle on every click.
            PlayClickBounce();
        }

        /// <summary>
        /// A quick springy bounce of the whole avatar on click: pop up to ~1.15x then settle back to
        /// 1.0 with an elastic ease. Runs on a dedicated ScaleTransform (AvatarBounceScale) that wraps
        /// all avatar layers, so it composes with — and never fights — the 60fps float/breathing writes.
        /// FillBehavior.Stop returns the transform to its 1.0 local value when done.
        /// </summary>
        private void PlayClickBounce()
        {
            if (AvatarBounceScale == null) return;

            var kf = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames
            {
                FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
            };
            kf.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(
                1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            kf.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(
                1.015, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80)),
                new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }));
            kf.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(
                1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300)),
                new System.Windows.Media.Animation.ElasticEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
                    Oscillations = 1,
                    Springiness = 7
                }));

            AvatarBounceScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, kf);
            AvatarBounceScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, kf.Clone());
        }

        private void ImgAvatar_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Close input panel on right-click
            HideInputPanel();
        }

        /// <summary>
        /// Trigger a comment based on current activity or random thought (Double-click action)
        /// </summary>
        private async Task TriggerActivityCommentAsync()
        {
            // 1. Trigger Mode Enabled: Always prioritize Custom Triggers
            if (App.Settings?.Current?.TriggerModeEnabled == true)
            {
                var triggers = App.Settings?.Current?.CustomTriggers;
                if (triggers != null && triggers.Count > 0)
                {
                    var trigger = triggers[_random.Next(triggers.Count)];
                    GigglePriority(trigger, aiGenerated: false);
                    return;
                }
            }

            // 2. Trigger Mode Disabled: Use 1-in-4 logic
            // 3/4 times -> Default Preset Phrase
            // 1/4 times -> Try AI/Context
            
            _interactionCount++;

            if (_interactionCount % 4 != 0)
            {
                // Show standard random Bambi phrase
                GigglePriority(GetRandomBambiPhrase(), aiGenerated: false);
                return;
            }

            // --- AI / Awareness Logic (1 in 4 chance) ---

            // Fallback defaults
            string reaction = GetRandomBambiPhrase();
            bool isAiAvailable = App.Settings?.Current?.AiChatEnabled == true && App.Ai?.IsAvailable == true;
            bool gotAiResponse = false;

            // Get current awareness context
            var awareness = App.WindowAwareness;
            var category = awareness?.CurrentActivity ?? ActivityCategory.Unknown;
            var detectedName = awareness?.CurrentDetectedName ?? "";
            var serviceName = awareness?.CurrentServiceName ?? "";
            var pageTitle = awareness?.CurrentPageTitle ?? "";

            // Decision: Comment on activity OR random thought?
            // If Unknown/Idle, do random thought.
            // If recognized, do activity comment.
            
            bool isRecognizedActivity = category != ActivityCategory.Unknown && category != ActivityCategory.Idle;

            if (isRecognizedActivity)
            {
                // Try AI Activity Comment
                if (isAiAvailable && App.Ai != null)
                {
                    try
                    {
                        // Show quick thinking indicator
                        if (!_isGiggling) Giggle("Hmm...");

                        var aiReaction = await App.Ai.GetAwarenessReactionAsync(detectedName, category.ToString(), serviceName, pageTitle);
                        if (!string.IsNullOrEmpty(aiReaction))
                        {
                            reaction = aiReaction;
                            gotAiResponse = true;
                        }
                        else
                        {
                            // Fallback to preset if AI returns empty
                            reaction = GetPhraseForCategory(category, detectedName);
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning(ex, "Failed to get AI awareness reaction on double-click");
                        reaction = GetPhraseForCategory(category, detectedName);
                    }
                }
                else
                {
                    // No AI, use preset
                    reaction = GetPhraseForCategory(category, detectedName);
                }
            }
            else
            {
                // Unrecognized/Idle/Desktop -> Random Thought
                if (isAiAvailable && App.Ai != null)
                {
                    try
                    {
                        // Show quick thinking indicator
                        if (!_isGiggling) Giggle("Hmm...");

                        // R2-NEW-H-1: migrate to typed AI API. Refusals are silently
                        // dropped on this non-chat surface (the user didn't directly
                        // prompt — a POLICY bubble out of nowhere is jarring). The
                        // downstream guard in AiService already logged via ModerationLog.
                        // IsAiGenerated propagates so canned fallbacks don't wear the badge.
                        var aiResult = await App.Ai.GetBambiReplyExAsync("Say something random and ditzy about what we're doing (or not doing) right now.");
                        if (aiResult.Refusal != null)
                        {
                            // Silent drop — fall back to preset behaviour below.
                        }
                        else if (!string.IsNullOrEmpty(aiResult.Text))
                        {
                            reaction = aiResult.Text;
                            gotAiResponse = aiResult.IsAiGenerated;
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning(ex, "Failed to get AI random thought on double-click");
                    }
                }
            }

            // Double bounce for AI responses to attract attention
            if (gotAiResponse)
            {
                PlayDoubleBounce();
            }

            // Display the result with priority. The badge only fires when we actually got an
            // AI-generated reaction — preset fallbacks are unmarked.
            GigglePriority(reaction, aiGenerated: gotAiResponse);
        }

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Report user activity to autonomy service
            App.Autonomy?.ReportUserActivity();

            // Close input panel when clicking outside of it
            if (_isInputVisible)
            {
                // Check if the click is outside the input panel
                var clickedElement = e.OriginalSource as DependencyObject;
                if (clickedElement != null && !IsDescendantOf(clickedElement, InputPanel))
                {
                    HideInputPanel();
                }
            }
        }

        private bool IsDescendantOf(DependencyObject element, DependencyObject parent)
        {
            while (element != null)
            {
                if (element == parent) return true;
                // ContentElements (e.g. Run, Hyperlink) are not part of the visual tree —
                // VisualTreeHelper.GetParent throws "X is not a Visual or Visual3D" on them.
                // Fall back to LogicalTreeHelper for those.
                element = element is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                    ? System.Windows.Media.VisualTreeHelper.GetParent(element)
                    : System.Windows.LogicalTreeHelper.GetParent(element);
            }
            return false;
        }

        private void HideInputPanel()
        {
            if (_isInputVisible)
            {
                _isInputVisible = false;
                InputPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void MenuItemDismiss_Click(object sender, RoutedEventArgs e)
        {
            // Hide the sprite and reattach to main window UI
            App.Logger?.Information("User dismissed avatar - hiding and reattaching");

            // Reattach if detached
            if (!_isAttached)
            {
                Attach();
            }

            // Hide the tube
            HideTube();
        }

        // ============================================================
        // COMPANION SPEECH & CHAT
        // ============================================================

        /// <summary>
        /// Checks if a new speech bubble can be shown (not currently showing and cooldown passed)
        /// </summary>
        private bool IsSpeechReady()
        {
            // If currently showing a bubble, not ready
            if (_isGiggling) return false;

            // Check cooldown
            double timeSinceLastSpeech = (DateTime.Now - _lastSpeechEndTime).TotalSeconds;
            double requiredDelay = CalculateRequiredDelayAfterLastSpeech();
            return timeSinceLastSpeech >= requiredDelay;
        }

        /// <summary>
        /// Calculates the required delay before showing the next speech.
        /// Delay is based on the PREVIOUS speech's properties - AI responses and long texts
        /// get more time after they end so users can read them.
        /// </summary>
        private double CalculateRequiredDelayAfterLastSpeech()
        {
            double delay = MinSpeechDelaySeconds;

            // Add bonus delay after AI responses (they're more important, give time to read)
            if (_lastSpeechSource == SpeechSource.AI)
            {
                delay += AiSpeechBonusSeconds;
            }

            // Add per-character delay for long texts (so users can read them)
            if (_lastSpeechLength > LongTextThreshold)
            {
                int extraChars = _lastSpeechLength - LongTextThreshold;
                delay += extraChars * PerCharDelaySeconds;
            }

            return delay;
        }

        /// <summary>
        /// Processes the next speech in the queue with proper delay.
        /// </summary>
        private void ProcessNextSpeech()
        {
            if (_speechQueue.Count == 0)
            {
                _isGiggling = false;
                return;
            }

            var (nextText, source, nextEmotionLineId, nextMood) = _speechQueue.Dequeue();
            App.Logger?.Debug("Dequeued speech ({Source}): {Text}", source, nextText);

            // Calculate how long since last speech ended (delay based on PREVIOUS speech properties)
            double timeSinceLastSpeech = (DateTime.Now - _lastSpeechEndTime).TotalSeconds;
            double requiredDelay = CalculateRequiredDelayAfterLastSpeech();
            double remainingDelay = Math.Max(0, requiredDelay - timeSinceLastSpeech);

            if (remainingDelay > 0)
            {
                // Wait before showing next speech
                _speechDelayTimer?.Stop();
                _speechDelayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(remainingDelay) };
                _speechDelayTimer.Tick += (s, e) =>
                {
                    _speechDelayTimer.Stop();
                    ShowSpeechBySource(nextText, source, nextEmotionLineId, nextMood);
                };
                _speechDelayTimer.Start();
                App.Logger?.Debug("Delaying speech by {Delay:F1}s", remainingDelay);
            }
            else
            {
                // Show immediately
                ShowSpeechBySource(nextText, source, nextEmotionLineId, nextMood);
            }
        }

        /// <summary>
        /// Shows speech based on its source (triggers play audio, others don't)
        /// </summary>
        private void ShowSpeechBySource(string text, SpeechSource source, string? emotionLineId = null, string? mood = null)
        {
            if (source == SpeechSource.Trigger)
            {
                // Triggers have their own display method with audio
                ShowTriggerBubbleImmediate(text);
            }
            else
            {
                // Determine sound: always for AI, 1 in 5 for presets
                bool playSound = source == SpeechSource.AI || (source == SpeechSource.Preset && ++_presetGiggleCounter % 5 == 0);
                ShowGiggle(text, playSound, source, emotionLineId: emotionLineId, mood: mood);
            }
        }

        /// <summary>
        /// Queues a speech bubble to be displayed. Bubbles are shown one at a time.
        /// Blocked while waiting for AI response or while AI bubble is visible.
        /// Plays giggle sound 1 in 5 times for preset phrases.
        /// </summary>
        public void Giggle(string text, string? phraseAudioPath = null, bool barkVoice = false, string? mood = null)
        {
            if (_isPlayingUninterruptibleClip) return; // an uninterruptible clip is speaking

            // Block if waiting for AI response
            if (_isWaitingForAi)
            {
                App.Logger?.Debug("Giggle blocked - waiting for AI: {Text}", text);
                return;
            }

            // Block (discard) preset phrases while AI bubble is visible - don't queue them
            if (_isShowingAiBubble)
            {
                App.Logger?.Debug("Giggle discarded - AI bubble visible: {Text}", text);
                return;
            }

            // A bark voiceline carries its audio path; its filename stem is the manifest lineId that
            // drives the portrait emotion. Derive it once so both the queued and immediate paths agree.
            string? emotionLineId = phraseAudioPath != null
                ? System.IO.Path.GetFileNameWithoutExtension(phraseAudioPath)
                : null;

            // Use BeginInvoke for non-blocking UI update
            DispatcherHelper.RunOnUI(() =>
            {
                // Double-check AI bubble state on UI thread
                if (_isShowingAiBubble)
                {
                    App.Logger?.Debug("Giggle discarded (UI thread) - AI bubble visible: {Text}", text);
                    return;
                }

                if (_isGiggling)
                {
                    _speechQueue.Enqueue((text, SpeechSource.Preset, emotionLineId, mood));
                    App.Logger?.Debug("Queued preset speech: {Text}", text);
                    return;
                }

                // Check if we need to delay based on last speech (delay based on PREVIOUS speech properties)
                double timeSinceLastSpeech = (DateTime.Now - _lastSpeechEndTime).TotalSeconds;
                double requiredDelay = CalculateRequiredDelayAfterLastSpeech();

                if (timeSinceLastSpeech < requiredDelay)
                {
                    // Queue it and let the delay system handle it. The queue now carries emotionLineId so
                    // the portrait emotion still fires when the bubble renders; audio is still text-only on
                    // this delayed path (barks normally fire idle, so the immediate path below is the norm).
                    _speechQueue.Enqueue((text, SpeechSource.Preset, emotionLineId, mood));
                    _isGiggling = true;
                    ProcessNextSpeech();
                }
                else
                {
                    // Determine if we should play giggle sound (1 in 5 for presets). A bark voiceline
                    // (barkVoice + phraseAudioPath) takes the audio path in ShowGiggle, so the giggle
                    // sound is suppressed automatically.
                    _presetGiggleCounter++;
                    bool playSound = _presetGiggleCounter % 5 == 0;
                    ShowGiggle(text, playSound, SpeechSource.Preset, phraseAudioPath, barkVoice: barkVoice,
                        emotionLineId: emotionLineId, mood: mood);
                }
            });
        }

        /// <summary>
        /// Shows a speech bubble immediately with priority (for AI responses).
        /// Clears any pending queue and interrupts current bubble.
        /// Also clears the AI waiting flag.
        /// </summary>
        /// <param name="text">Text to display</param>
        /// <param name="playSound">Whether to play giggle sound (default true for AI responses)</param>
        // ============================================================
        // CHAT HISTORY MODE
        // ============================================================

        private void MenuItemShowChatHistory_Click(object sender, RoutedEventArgs e)
        {
            EnterChatHistoryMode();
        }

        private void BtnCloseChatHistory_Click(object sender, RoutedEventArgs e)
        {
            ExitChatHistoryMode();
        }

        private void AvatarTubeWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _isShowingChatHistory)
            {
                ExitChatHistoryMode();
                e.Handled = true;
            }
        }

        private void EnterChatHistoryMode()
        {
            // Cancel any in-flight bubble timers — chat history takes over the bubble.
            _speechTimer?.Stop();
            _speechDelayTimer?.Stop();
            StopThinkingAnimation();
            _isWaitingForAi = false;
            _isGiggling = false;

            _isShowingChatHistory = true;

            // Show empty-state hint when there are no captured messages yet.
            TxtChatHistoryEmpty.Visibility = ChatHistory.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // Swap bubble content: hide single-message view, show chat history.
            SpeechScroller.Visibility = Visibility.Collapsed;
            ChatHistoryView.Visibility = Visibility.Visible;
            // Hide the per-message AI badge when showing the chat history list (mixed AI + user lines).
            if (AiBadge != null) AiBadge.Visibility = Visibility.Collapsed;

            // Enlarge bubble for the chat history layout.
            SpeechBubble.MaxWidth = 600;

            SpeechBubble.UpdateLayout();
            SpeechBubble.Visibility = Visibility.Visible;

            // Auto-scroll to most recent message.
            Dispatcher.BeginInvoke(new Action(() => ChatHistoryScroller.ScrollToBottom()),
                System.Windows.Threading.DispatcherPriority.Background);

            if (!(PopQuizWindow.IsOpen || QuizWindow.IsOpen))
            {
                StartZOrderRefreshTimer();
                BringAttachedPairToFront();
            }
        }

        private void ExitChatHistoryMode()
        {
            _isShowingChatHistory = false;
            ChatHistoryView.Visibility = Visibility.Collapsed;
            SpeechScroller.Visibility = Visibility.Visible;
            SpeechBubble.MaxWidth = 380; // Restore default bubble width.
            SpeechBubble.Visibility = Visibility.Collapsed;
            StopZOrderRefreshTimer();
        }

        // Timestamp of the last genuine AI reply bubble (not bark output). Lets the bark
        // system suppress barks for a window after a chat exchange. See IsCompanionBusy.
        private DateTime _lastAiBubbleUtc = DateTime.MinValue;

        /// <summary>
        /// True while the companion is mid-chat: an AI request is in flight, an AI bubble is
        /// on screen, or a genuine AI reply occurred within <paramref name="windowMs"/>.
        /// The bark system checks this to avoid talking over a conversation (Fork E).
        /// </summary>
        public bool IsCompanionBusy(int windowMs)
        {
            if (_isWaitingForAi || _isShowingAiBubble) return true;
            return windowMs > 0 && (DateTime.UtcNow - _lastAiBubbleUtc).TotalMilliseconds < windowMs;
        }

        /// <summary>
        /// True while ANY speech bubble (AI or ordinary "Preset" bark/chatter) is currently being
        /// displayed. Unlike <see cref="IsCompanionBusy"/> this also covers non-AI bubbles, so the bark
        /// system can avoid stacking ordinary barks behind one that's already on screen — otherwise they
        /// queue and, by the time the queue drains, comment on something that happened seconds ago.
        /// </summary>
        public bool IsSpeaking => _isGiggling;

        public void GigglePriority(string text, bool playSound = true, bool aiGenerated = true, string? phraseAudioPath = null, bool barkVoice = false, string? mood = null)
        {
            if (_isPlayingUninterruptibleClip) return; // an uninterruptible clip is speaking
            string? emotionLineId = phraseAudioPath != null
                ? System.IO.Path.GetFileNameWithoutExtension(phraseAudioPath)
                : null;
            DispatcherHelper.RunOnUI(() =>
            {
                // Only genuine AI replies anchor the chat-suppression window; bark output
                // (aiGenerated: false) must not suppress subsequent barks via this path.
                if (aiGenerated) _lastAiBubbleUtc = DateTime.UtcNow;

                // Stop any in-flight thinking animation before showing the reply.
                StopThinkingAnimation();

                // Clear AI waiting flag
                _isWaitingForAi = false;

                // Clear the queue - AI response takes priority
                _speechQueue.Clear();

                // Stop any current speech/delay timers
                _speechTimer?.Stop();
                _speechDelayTimer?.Stop();

                // Show immediately
                _isGiggling = false;

                // Capture AI reply for chat history
                AddToChatHistory(text, isUser: false);

                // aiGenerated: when true, the bubble shows the "AI" badge. Most call sites that route
                // through GigglePriority are AI replies, but the awareness keyword path can fall back
                // to canned phrases when the AI is unavailable — those callers pass false.
                ShowGiggle(text, playSound: playSound, source: SpeechSource.AI, aiGenerated: aiGenerated,
                    phraseAudioPath: phraseAudioPath, barkVoice: barkVoice, emotionLineId: emotionLineId, mood: mood);

                App.Logger?.Debug("Priority speech (queue cleared): {Text}", text);
            });
        }

        /// <summary>
        /// Renders the moderation-refusal bubble. Called when an LLM input or output
        /// trips <c>ModerationGuard</c>. Shows the POLICY badge (amber) and the localized
        /// refusal string. Part of P1.1 (CCBill substantive moderation).
        /// </summary>
        public void ShowModerationRefusalBubble(ModerationSource source)
        {
            DispatcherHelper.RunOnUI(() =>
            {
                try
                {
                    StopThinkingAnimation();
                    _isWaitingForAi = false;
                    _speechQueue.Clear();
                    _speechTimer?.Stop();
                    _speechDelayTimer?.Stop();
                    _isGiggling = false;

                    var locKey = source == ModerationSource.Input
                        ? "moderation_input_refusal"
                        : "moderation_output_refusal";
                    var text = Loc.Get(locKey);
                    if (string.IsNullOrEmpty(text))
                        text = source == ModerationSource.Input
                            ? "This message can't be sent under our content policy."
                            : "AI declined to respond.";

                    AddToChatHistory(text, isUser: false);

                    // Show via the normal pipeline first so sizing/animation runs,
                    // then swap the badges (POLICY visible, AI hidden).
                    ShowGiggle(text, playSound: false, source: SpeechSource.AI, aiGenerated: false);
                    if (AiBadge != null) AiBadge.Visibility = Visibility.Collapsed;
                    if (PolicyBadge != null) PolicyBadge.Visibility = Visibility.Visible;

                    App.Logger?.Information("ShowModerationRefusalBubble: source={Source}", source);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "ShowModerationRefusalBubble failed");
                }
            });
        }

        /// <summary>
        /// Displays a speech bubble with text.
        /// </summary>
        /// <param name="text">The text to display</param>
        /// <param name="playSound">Whether to play a giggle sound</param>
        /// <param name="source">The source of the speech (for delay calculation)</param>
        private void ShowGiggle(string text, bool playSound = false, SpeechSource source = SpeechSource.Preset, string? phraseAudioPath = null, bool aiGenerated = false, bool bypassClipLock = false, bool barkVoice = false, string? emotionLineId = null, string? mood = null)
        {
            // An uninterruptible recorded clip owns the bubble — only its own (bypassing) call may render.
            if (_isPlayingUninterruptibleClip && !bypassClipLock) return;

            // CCBill AI Addendum: show the "AI" badge only when this bubble's text actually came from
            // an AI inference. Canned/preset phrases (including AI-path fallbacks) leave it hidden.
            // The POLICY badge is mutually exclusive — only ShowModerationRefusalBubble shows it.
            try
            {
                if (AiBadge != null)
                    AiBadge.Visibility = aiGenerated ? Visibility.Visible : Visibility.Collapsed;
                if (PolicyBadge != null)
                    PolicyBadge.Visibility = Visibility.Collapsed;
            }
            catch { /* AiBadge not in template / closing — non-fatal */ }

            // Chat history view owns the bubble — swap back to single-message mode before showing.
            if (_isShowingChatHistory)
            {
                _isShowingChatHistory = false;
                ChatHistoryView.Visibility = Visibility.Collapsed;
                SpeechScroller.Visibility = Visibility.Visible;
                SpeechBubble.MaxWidth = 380;
            }

            // Skip if muted or avatar not visible on screen
            if (_isMuted || !IsAvatarVisibleOnScreen)
            {
                _isGiggling = false;
                // Track timing and properties even when muted/hidden (for delay calculation)
                _lastSpeechEndTime = DateTime.Now;
                _lastSpeechSource = source;
                _lastSpeechLength = text.Length;
                ProcessNextSpeech();
                return;
            }

            _isGiggling = true;
            _isShowingAiBubble = (source == SpeechSource.AI);

            // Drive the emotive portrait to this line's emotion (pose A → linger → pose B → idle after
            // the dwell). lineId is the explicit arg or the bark audio filename stem. No-ops when the
            // portrait system is off, the line is unknown, or there's no audio (canned/AI/trigger text).
            PlayEmotionForLine(
                emotionLineId ?? (phraseAudioPath != null
                    ? System.IO.Path.GetFileNameWithoutExtension(phraseAudioPath) : null),
                phraseAudioPath, text, mood);

            // Any new bubble cuts off the previous spoken line (covers the giggle-sound/fallback branches
            // below, which otherwise wouldn't stop an in-flight voiceline → overlap).
            StopSpokenAudio();

            // The spoken part — audio + bubble. For non-AI lines this is deferred behind
            // EmoteAudioLeadInSeconds: ~0 in emote mode (voice-first; the talk clips join late and bow out
            // early), else the flat SpeechLeadInSeconds so the static pose swap leads the voice.
            bool isThinking = source == SpeechSource.AI && _isWaitingForAi;
            bool slowType = source != SpeechSource.AI; // bark/preset/trigger type slower than AI replies
            Action speak = () =>
            {
                if (!_isGiggling) return; // a newer bubble took over during the lead-in

                // Play sound for the speech bubble
                if (phraseAudioPath != null)
                {
                    // Custom phrase audio overrides default sounds. Bark voicelines play through the
                    // companion-voice path (MasterVolume-gated), not the SubAudio-gated phrase path.
                    if (barkVoice)
                        PlayBarkVoice(phraseAudioPath);
                    else
                        PlayPhraseAudio(phraseAudioPath);
                }
                else if (playSound)
                {
                    // Explicitly requested giggle sound (AI responses, etc.)
                    PlayGiggleSound();
                }
                else if (source != SpeechSource.AI)
                {
                    // Fallback sound for regular bubbles (skip for AI thinking — response will play its own)
                    PlayFallbackBubbleSound();
                }

                // Type the text out char-by-char for every bubble (AI replies fast, everything else
                // slower). The AI "thinking" frame skips it — its tick updates the bubble directly.
                // PopulateSpeechBubble runs again at the end of the typewriter so links stay clickable.
                if (!isThinking)
                    StartTypewriter(text, slowType);
                else
                    PopulateSpeechBubble(text);

                // Strip markdown links for size calculation (use plain text length)
                var plainText = MarkdownLinkRegex.Replace(text ?? "", "$1");

                // Adjust bubble size based on text length
                AdjustBubbleSize(plainText);

                // Force layout update before showing to prevent flickering
                SpeechBubble.UpdateLayout();
                SpeechBubble.Visibility = Visibility.Visible;

                // Start z-order refresh to keep bubble on top of main window
                // Skip all z-order work when pop quiz is open — must not cover the quiz
                if (!(PopQuizWindow.IsOpen || QuizWindow.IsOpen))
                {
                    StartZOrderRefreshTimer();
                    BringAttachedPairToFront();
                }

                // Display duration is user-controlled via Companion tab slider (1-10s, default 2).
                // Long AI replies are still readable: hovering keeps the bubble open, and
                // "Show chat history" preserves the full conversation for re-reading.
                double userSetting = Math.Clamp(App.Settings?.Current?.BubbleDurationSeconds ?? 2.0, 1.0, 10.0);
                double displayDuration = userSetting;

                // The typewriter eats into the visible window. Add its estimated runtime (every bubble
                // types now) so the slider value reflects fully-rendered reading time, not open time.
                if (!isThinking)
                {
                    double typewriterSec = EstimateTypewriterDurationMs((text ?? "").Length, slowType) / 1000.0;
                    displayDuration += typewriterSec;

                    if (source == SpeechSource.AI)
                    {
                        // Floor for long replies: the user's slider value is sensible for the short
                        // trigger-style bubbles it was designed for, but a 200-char AI reply at
                        // userSetting=2 leaves only 2 seconds of reading time after typing (bug #193).
                        // Apply ~12 chars/sec ESL-friendly reading speed as a minimum post-typewriter
                        // dwell, capped at 30s so a runaway long reply doesn't pin the bubble forever.
                        const double charsPerSecond = 12.0;
                        const double maxPostTypeSec = 30.0;
                        double readingFloorSec = Math.Min(maxPostTypeSec, (text ?? "").Length / charsPerSecond);
                        double minTotalSec = typewriterSec + Math.Max(userSetting, readingFloorSec);
                        if (minTotalSec > displayDuration) displayDuration = minTotalSec;
                    }
                }

                // Hide after calculated duration
                _speechTimer?.Stop();
                _speechTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(displayDuration) };

                // Capture current speech properties for delay calculation
                var currentSource = source;
                var currentLength = (text ?? "").Length;

                _speechTimer.Tick += (s, e) =>
                {
                    // If mouse is over speech bubble, keep it open - recheck in 1 second
                    if (_isMouseOverSpeechBubble)
                    {
                        _speechTimer.Interval = TimeSpan.FromSeconds(1);
                        return; // Don't stop timer, keep checking
                    }

                    _speechTimer.Stop();
                    StopZOrderRefreshTimer();
                    SpeechBubble.Visibility = Visibility.Collapsed;
                    _isShowingAiBubble = false; // Clear AI bubble flag when any bubble hides

                    // Track this speech's properties for delay calculation on next speech
                    _lastSpeechEndTime = DateTime.Now;
                    _lastSpeechSource = currentSource;
                    _lastSpeechLength = currentLength;

                    // Process next speech with proper delay handling
                    ProcessNextSpeech();
                };
                _speechTimer.Start();

                // Reset idle timer when speaking
                ResetIdleTimer();

                App.Logger?.Debug("Companion says ({Source}, {Chars} chars, {Duration:F1}s): {Text}",
                    source, (text ?? "").Length, displayDuration, text);
            };

            _speechLeadInTimer?.Stop();
            _speechLeadInTimer = null;
            if (source != SpeechSource.AI)
            {
                // Spoken line: in emote mode the voice fires right away (~0 — the talk animation joins the
                // line late instead); otherwise hold audio + bubble behind the flat lead-in so the static
                // pose swap leads the voice.
                _speechLeadInTimer = new DispatcherTimer
                { Interval = TimeSpan.FromSeconds(EmoteAudioLeadInSeconds(SpeechLeadInSeconds)) };
                _speechLeadInTimer.Tick += (s, e) =>
                {
                    _speechLeadInTimer?.Stop();
                    _speechLeadInTimer = null;
                    speak();
                };
                _speechLeadInTimer.Start();
            }
            else
            {
                speak(); // AI replies are already gated by inference latency — no lead-in
            }
        }

        private DispatcherTimer? _mutedIndicatorTimer;

        /// <summary>
        /// Shows a brief "MUTED" indicator in the speech bubble when the user
        /// double-clicks the avatar while muted, so they know she's not broken.
        /// </summary>
        private void ShowMutedIndicator()
        {
            // Don't spam the indicator
            if (SpeechBubble.Visibility == Visibility.Visible)
                return;

            TxtSpeech.Inlines.Clear();
            TxtSpeech.Inlines.Add(new System.Windows.Documents.Run("MUTED \U0001F509")
            {
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(180, 180, 200))
            });
            TxtSpeech.FontSize = 20;

            SpeechBubble.Visibility = Visibility.Visible;

            _mutedIndicatorTimer?.Stop();
            _mutedIndicatorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _mutedIndicatorTimer.Tick += (s, e) =>
            {
                _mutedIndicatorTimer.Stop();
                SpeechBubble.Visibility = Visibility.Collapsed;
            };
            _mutedIndicatorTimer.Start();
        }

        /// <summary>
        /// Adjusts the speech bubble font size and position based on text length.
        /// The bubble has fixed width (380) and MaxHeight (420) - ScrollViewer handles overflow.
        /// </summary>
        private void AdjustBubbleSize(string text)
        {
            int charCount = text.Length;

            // Adjust font size for readability based on text length
            // Shorter text can use larger font, longer text uses smaller font
            double fontSize;
            if (charCount <= 50)
            {
                fontSize = 22; // Normal size for short messages
            }
            else if (charCount <= 120)
            {
                fontSize = 20; // Slightly smaller for medium messages
            }
            else if (charCount <= 250)
            {
                fontSize = 18; // Smaller for longer messages
            }
            else
            {
                fontSize = 16; // Smallest for very long AI responses
            }

            TxtSpeech.FontSize = fontSize;

            // Reset scroll position to top when new text is shown
            SpeechScroller?.ScrollToTop();

            // Position bubble next to avatar — align with tube position based on attach state.
            var bubbleUseAttached = _isAttached || ModOverridesAttachedTubeOnly();
            var bubbleDx = bubbleUseAttached
                ? EffAvatarOffsetX()
                : EffAvatarDetachedOffsetX();
            var bubbleRight = bubbleUseAttached ? 125 - bubbleDx : 425 - bubbleDx;
            SpeechBubble.Margin = new Thickness(0, 0, bubbleRight, 550);
        }

        /// <summary>
        /// Populates the speech bubble with text and clickable hyperlinks.
        /// Parses markdown-style [text](url) links and creates Hyperlink inlines.
        /// </summary>
        /// <summary>
        /// Exact HypnoTube video titles mapped to URLs.
        /// Names match exactly as shown on HypnoTube.
        /// Reloaded when the active mod changes via ReloadVideoLinks().
        /// </summary>
        internal static Dictionary<string, string> KnownVideoLinks = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Naughty Bambi", "https://hypnotube.com/video/naughty-bambi-109749.html" },
            { "Bambi Bae", "https://hypnotube.com/video/bambi-bae-113979.html" },
            { "Bambi's Naughty TikTok Collection", "https://hypnotube.com/video/bambis-naughty-tiktok-collection-117314.html" },
            { "TikTok Loop", "https://hypnotube.com/video/tiktok-loop-39245.html" },
            { "Overload", "https://hypnotube.com/video/overload-46422.html" },
            { "Bambi TikTok - In Beat", "https://hypnotube.com/video/bambi-tiktok-in-beat-52730.html" },
            { "Bambi TikTok - In Beat - Longer Version", "https://hypnotube.com/video/bambi-tiktok-in-beat-longer-version-56194.html" },
            { "Bambi TikTok - Good Girls Dont Cum", "https://hypnotube.com/video/bambi-tiktok-good-girls-dont-cum-68081.html" },
            { "Bambi Chastity Overload", "https://hypnotube.com/video/bambi-chastity-overload-75092.html" },
            { "Mommy's In Control Full", "https://hypnotube.com/video/mommys-in-control-full-76043.html" },
            { "Bambi Loves Hentai - OneeKitsune", "https://hypnotube.com/video/bambi-loves-hentai-oneekitsune-78373.html" },
            { "Bubblehead Forever - Iplaywithdolls", "https://hypnotube.com/video/bubblehead-forever-iplaywithdolls-79880.html" },
            { "Dumb Bimbo Brainwash", "https://hypnotube.com/video/dumb-bimbo-brainwash-80780.html" },
            { "Bambi TikTok Eager Slut", "https://hypnotube.com/video/bambi-tiktok-eager-slut-80971.html" },
            { "Mindlocked Cock Zombie", "https://hypnotube.com/video/mindlocked-cock-zombie-87742.html" },
            { "Bambi TikTok Good Girl Academy", "https://hypnotube.com/video/bambi-tiktok-good-girl-academy-92527.html" },
            { "Bambi TikTok Chastity Trainer", "https://hypnotube.com/video/bambi-tiktok-chastity-trainer-96290.html" },
            { "Bambi Slay", "https://hypnotube.com/video/bambi-slay-99609.html" },
            // Batch 2
            { "Yes Brain Loop", "https://hypnotube.com/video/yes-brain-loop-113736.html" },
            { "Bambi Uniform Bliss", "https://hypnotube.com/video/bambi-uniform-bliss-3553.html" },
            { "Bambi Bimbo Dreams Ep 1", "https://hypnotube.com/video/bambi-bimbo-dreams-ep-1-8050.html" },
            { "Day 1", "https://hypnotube.com/video/day-1-11009.html" },
            { "Day 2", "https://hypnotube.com/video/day-2-11011.html" },
            { "Day 4", "https://hypnotube.com/video/day-4-11179.html" },
            { "Day 5", "https://hypnotube.com/video/day-5-11228.html" },
            { "Bimbo Servitude Brainwash", "https://hypnotube.com/video/bimbo-servitude-brainwash-33041.html" },
            { "Bambi Uniform Oblivion", "https://hypnotube.com/video/bambi-uniform-oblivion-34010.html" },
            { "Bambi TikTok 7", "https://hypnotube.com/video/bambi-tiktok-7-42488.html" },
            { "Bambi Tik-Tok Mix 1 - 7 No Pauses", "https://hypnotube.com/video/bambi-tik-tok-mix-1-7-no-pauses-53860.html" },
            { "Bambi's Brain Melts TikTok", "https://hypnotube.com/video/bambi-s-brain-melts-tiktok-56183.html" },
            { "Bimbodoll Seduction - Part I", "https://hypnotube.com/video/bimbodoll-seduction-part-i-62493.html" },
            { "Toms Dangerous Tik Tok", "https://hypnotube.com/video/toms-dangerous-tik-tok-62552.html" },
            { "Bimbodoll Awakened Obedience", "https://hypnotube.com/video/bimbodoll-awakened-obedience-62614.html" },
            { "Bimbdoll Resistance Full", "https://hypnotube.com/video/bimbdoll-resistance-full-63079.html" },
            { "Bambi - I Want Your Cum", "https://hypnotube.com/video/bambi-i-want-your-cum-64715.html" },
            { "Bambi Day 7 Remix", "https://hypnotube.com/video/bambi-day-7-remix-65691.html" },
            { "Bambi Tiktok Wide Remix By Analbambi", "https://hypnotube.com/video/bambi-tiktok-wide-remix-by-analbambi-66055.html" },
            // Sissy Hypno pool
            { "Ultimate Sissy Mindfuck", "https://hypnotube.com/video/ultimate-sissy-mindfuck-106170.html" },
            { "Femboy Heaven - TS PMV", "https://hypnotube.com/video/femboy-heaven-ts-pmv-90699.html" },
            { "Wife Helps You Take Cock", "https://hypnotube.com/video/wife-helps-you-take-cock-91559.html" },
            { "Up and Down", "https://hypnotube.com/video/up-and-down-95541.html" },
            { "Neural Rewire - Mommys Fap Roulette - Devereux", "https://hypnotube.com/video/neural-rewire-mommys-fap-roulette-devereux-115970.html" },
            { "Girly Thoughts Vertical Loop", "https://hypnotube.com/video/girly-thoughts-vertical-loop-118644.html" },
            { "Splitscreen Anal Trainer 4 - By Dildoslut", "https://hypnotube.com/video/splitscreen-anal-trainer-4-by-dildoslut-111004.html" },
            { "Anal Dream - SissyGalJasmine Edition", "https://hypnotube.com/video/anal-dream-sissygaljasmine-edition-90388.html" },
            { "BBC Stoner Goon File", "https://hypnotube.com/video/bbc-stoner-goon-file-89975.html" },
            { "Sissy Desires", "https://hypnotube.com/video/sissy-desires-103899.html" },
            { "A Touch Of Femboy - TS PMV", "https://hypnotube.com/video/a-touch-of-femboy-ts-pmv-110470.html" },
            { "Say Yes To Cock Hypnosis", "https://hypnotube.com/video/say-yes-to-cock-hypnosis-112015.html" },
            { "Pegging Dreams - Full", "https://hypnotube.com/video/pegging-dreams-full-110796.html" },
            { "Hold it Down - Deepthroat Trainer By Whore Factory", "https://hypnotube.com/video/hold-it-down-deepthroat-trainer-by-whore-factory-112708.html" },
            { "Anal Slut Trainer", "https://hypnotube.com/video/anal-slut-trainer-101540.html" },
            { "You Love Cock", "https://hypnotube.com/video/you-love-cock-105890.html" },
            { "Deep Acceptance", "https://hypnotube.com/video/deep-acceptance-113157.html" },
            { "Eat Your Cum", "https://hypnotube.com/video/eat-your-cum-116026.html" },
            { "Trans Love Hypno - CrimsonPMV", "https://hypnotube.com/video/trans-love-hypno-crimsonpmv-121310.html" },

            // BambiCloud playlists (audio, extracted 2026-04-28)
            { "IQ Programming", "https://bambicloud.com/playlist/ff15f538-6e6b-433c-b68b-b4af5ee5d14d" },
            { "Attitude Programming", "https://bambicloud.com/playlist/c0effdad-6002-4269-a982-479d676c8d46" },
            { "Takeover Programming", "https://bambicloud.com/playlist/726403c2-567c-4c30-9f74-8fd750a82ef9" },
            { "Cockslut Programming", "https://bambicloud.com/playlist/10091e87-2243-4f75-85d1-912c39951bc4" },
            { "Uniform Programming", "https://bambicloud.com/playlist/39f0c016-abfb-4a53-a8d3-1c492a86635b" },
            { "Maid Programming", "https://bambicloud.com/playlist/d244e2d6-be21-4e5b-bab1-b1268ade85ce" },
            { "Deep Trance Programming", "https://bambicloud.com/playlist/648f16c8-865b-44e2-bba5-881fc499e0f7" },
            { "Personality Programming", "https://bambicloud.com/playlist/ba1cf73a-5f3e-4ef8-bbc6-67ce2dcae774" },
        };

        // Cached copy of the built-in links for restoring when switching away from custom mods
        private static Dictionary<string, string>? _builtInVideoLinks;

        /// <summary>
        /// Reloads KnownVideoLinks from the active mod's defaultVideoLinks, or restores built-in defaults.
        /// Called on mod switch.
        /// </summary>
        internal static void ReloadVideoLinks()
        {
            // Cache built-in links on first call
            _builtInVideoLinks ??= new Dictionary<string, string>(KnownVideoLinks, StringComparer.OrdinalIgnoreCase);

            var modLinks = App.Mods?.GetVideoLinks();
            if (modLinks != null && modLinks.Count > 0)
            {
                // Filter to HTTPS-only at runtime (defense-in-depth)
                var filtered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in modLinks)
                {
                    if (Uri.TryCreate(kvp.Value, UriKind.Absolute, out var uri) && uri.Scheme == "https")
                        filtered[kvp.Key] = kvp.Value;
                }
                KnownVideoLinks = filtered;
            }
            else
            {
                KnownVideoLinks = new Dictionary<string, string>(_builtInVideoLinks, StringComparer.OrdinalIgnoreCase);
            }
        }

        private void PopulateSpeechBubble(string text)
        {
            BuildLinkedInlines(text, TxtSpeech.Inlines);
        }

        /// <summary>
        /// Strips markdown link syntax, finds known video/playlist titles and raw URLs in
        /// <paramref name="text"/>, and writes Run / Hyperlink inlines into <paramref name="target"/>.
        /// Used by the live speech bubble AND the chat history items so both render the same
        /// clickable pink hyperlinks.
        /// </summary>
        private void BuildLinkedInlines(string text, InlineCollection target)
        {
            target.Clear();

            if (string.IsNullOrEmpty(text))
                return;

            // Pass 1: collapse markdown links into just their text BUT remember the (text, url)
            // pairs so we can re-attach them as real Hyperlink inlines below. We used to drop
            // URLs entirely, which meant the AI couldn't reliably surface clickable links —
            // small models often produce correct URLs (copied verbatim from the prompt) but
            // approximate the title text, so the URL is the more authoritative signal.
            var mdLinks = new List<(string LinkText, string Url)>();
            text = Regex.Replace(text, @"\[([^\]]+)\]\(([^)]+)\)", m =>
            {
                var linkText = m.Groups[1].Value;
                var url = m.Groups[2].Value.Trim();
                if (Uri.TryCreate(url, UriKind.Absolute, out var u) &&
                    (u.Scheme == "https" || u.Scheme == "http"))
                {
                    mdLinks.Add((linkText, url));
                }
                return linkText; // collapse to plain text in the working buffer
            });

            // Also clean up any malformed markdown like [Url] or (url)
            text = Regex.Replace(text, @"\s*\[Url\]|\s*\(url\)", "", RegexOptions.IgnoreCase);

            // Try to find known video names in the text and make them clickable
            var processedText = text;
            var linkPositions = new List<(int start, int length, string name, string url)>();

            // Pass 2: re-find the markdown link texts (now plain) in the buffer and claim them
            // as the highest-priority link source. Authoritative URL beats name-based guess.
            foreach (var (linkText, url) in mdLinks)
            {
                var idx = processedText.IndexOf(linkText, StringComparison.Ordinal);
                if (idx < 0) continue;
                bool overlaps = linkPositions.Any(lp =>
                    (idx >= lp.start && idx < lp.start + lp.length) ||
                    (idx + linkText.Length > lp.start && idx + linkText.Length <= lp.start + lp.length));
                if (!overlaps)
                    linkPositions.Add((idx, linkText.Length, linkText, url));
            }

            // Match against BOTH the static link table AND the active mod's LIVE video pool — the
            // exact same source the AI prompt drew its suggestions from (App.Mods.GetVideoLinks()).
            // ReloadVideoLinks() is supposed to keep KnownVideoLinks in sync on mod switch, but if
            // it runs before the active mod is set the table lags behind, so a real Sissy pool title
            // the companion was told to say (e.g. "Sissy Dreams 3") isn't in KnownVideoLinks and
            // renders as dead plain text. Merging the live pool here guarantees any title the prompt
            // could offer is clickable. (Static table wins on key collisions — it's the canonical URL.)
            var linkTable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var livePool = App.Mods?.GetVideoLinks();
            if (livePool != null)
            {
                foreach (var kvp in livePool)
                {
                    if (Uri.TryCreate(kvp.Value, UriKind.Absolute, out var pu) && pu.Scheme == "https")
                        linkTable[kvp.Key] = kvp.Value;
                }
            }
            foreach (var kvp in KnownVideoLinks)
                linkTable[kvp.Key] = kvp.Value;
            // Also fold in the FULL built-in catalogue (all videos + the BambiCloud audio
            // playlists). The prompt already controls what gets SUGGESTED per mod; the linker
            // should be permissive so anything offered renders clickable. Without this, a mod
            // swap replaces KnownVideoLinks with the mod's video-only pool, dropping the audio
            // playlists — so a bare "IQ Programming" (named without markdown) wouldn't link.
            if (_builtInVideoLinks != null)
            {
                foreach (var kvp in _builtInVideoLinks)
                    if (!linkTable.ContainsKey(kvp.Key))
                        linkTable[kvp.Key] = kvp.Value;
            }

            foreach (var kvp in linkTable.OrderByDescending(k => k.Key.Length)) // Longest first to avoid partial matches
            {
                var idx = processedText.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    // Check if this position overlaps with an already found link
                    bool overlaps = linkPositions.Any(lp =>
                        (idx >= lp.start && idx < lp.start + lp.length) ||
                        (idx + kvp.Key.Length > lp.start && idx + kvp.Key.Length <= lp.start + lp.length));

                    if (!overlaps)
                    {
                        linkPositions.Add((idx, kvp.Key.Length, kvp.Key, kvp.Value));
                    }
                }
            }

            // Also detect raw URLs in the text (the AI sometimes outputs full URLs from the link pool)
            var urlRegex = new Regex(@"https?://[^\s,""'<>]+", RegexOptions.IgnoreCase);
            foreach (Match match in urlRegex.Matches(text))
            {
                bool overlaps = linkPositions.Any(lp =>
                    (match.Index >= lp.start && match.Index < lp.start + lp.length) ||
                    (match.Index + match.Length > lp.start && match.Index + match.Length <= lp.start + lp.length));

                if (!overlaps)
                {
                    linkPositions.Add((match.Index, match.Length, match.Value, match.Value));
                }
            }

            // Sort by position
            linkPositions = linkPositions.OrderBy(lp => lp.start).ToList();

            if (linkPositions.Count == 0)
            {
                // No known videos found - just show plain text
                target.Add(new Run(text));
                return;
            }

            // Build inlines with links
            int lastIndex = 0;
            foreach (var (start, length, name, url) in linkPositions)
            {
                // Add text before the link
                if (start > lastIndex)
                {
                    target.Add(new Run(text.Substring(lastIndex, start - lastIndex)));
                }

                // Get the actual text from the original (preserving case)
                var actualText = text.Substring(start, length);

                // A bare URL is ugly as link text. Replace it with the canonical pool title when
                // the URL is known, else a readable title derived from the slug ("this video" if
                // even that fails) — so a model that emits a URL still gets a clean clickable link.
                var displayText = actualText;
                if (actualText.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    var known = linkTable.FirstOrDefault(kvp =>
                        string.Equals(kvp.Value, url, StringComparison.OrdinalIgnoreCase)).Key;
                    displayText = known ?? Helpers.HtUrlHelper.DeriveTitleFromUrl(url);
                }

                try
                {
                    var hyperlink = new Hyperlink(new Run(displayText))
                    {
                        NavigateUri = new Uri(url),
                        Foreground = new SolidColorBrush(Color.FromRgb(255, 176, 224)), // Light pink
                        TextDecorations = TextDecorations.Underline
                    };
                    hyperlink.RequestNavigate += SpeechBubbleHyperlink_RequestNavigate;
                    target.Add(hyperlink);
                    App.Logger?.Information("Auto-linked video: '{Name}' -> {Url}", displayText, url);
                }
                catch
                {
                    target.Add(new Run(displayText));
                }

                lastIndex = start + length;
            }

            // Add remaining text
            if (lastIndex < text.Length)
            {
                target.Add(new Run(text.Substring(lastIndex)));
            }
        }

        /// <summary>
        /// Loaded handler for chat history TextBlocks. Pulls the message text from Tag
        /// (we can't bind to Text because then we couldn't write Inlines) and renders it
        /// through the same hyperlink builder used by the live bubble.
        /// </summary>
        private void ChatHistoryText_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBlock tb && tb.Tag is string text)
                BuildLinkedInlines(text, tb.Inlines);
        }

        /// <summary>
        /// Handles clicks on hyperlinks in the speech bubble.
        /// Routes to the embedded browser with correct tab selection (BambiCloud/HypnoTube).
        /// Opens in fullscreen mode for immersive viewing.
        /// </summary>
        private void SpeechBubbleHyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                // Block link clicks during remote control — navigating the browser
                // while a web video is playing fullscreen breaks the playback state
                if (App.RemoteControl?.ControllerConnected == true)
                {
                    App.Logger?.Debug("Speech bubble link blocked - remote controller is connected");
                    e.Handled = true;
                    return;
                }

                var url = e.Uri.AbsoluteUri;
                App.Logger?.Information("Speech bubble link clicked - Raw URI: {Uri}, AbsoluteUri: {Url}", e.Uri, url);

                // Find MainWindow - it might not be Application.Current.MainWindow if AvatarTube is detached
                var mainWindow = _parentWindow as MainWindow
                    ?? Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();

                App.Logger?.Information("MainWindow found: {Found}", mainWindow != null);

                if (mainWindow?.NavigateToUrlInBrowser(url, autoPlayFullscreen: true) == true)
                {
                    App.Logger?.Information("Speech bubble link routed to embedded browser: {Url}", url);
                }
                else
                {
                    // Fallback: open in external browser (HTTPS only for safety)
                    if (Uri.TryCreate(url, UriKind.Absolute, out var fallbackUri) && fallbackUri.Scheme == "https")
                    {
                        App.Logger?.Warning("Embedded browser unavailable, opening externally: {Url}", url);
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                    }
                }

                e.Handled = true;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open speech bubble link: {Message}", ex.Message);
            }
        }

        /// <summary>
        /// Plays a quick double bounce animation to attract attention.
        /// Used when AI or awareness responses are shown.
        /// </summary>
        private void PlayDoubleBounce()
        {
            // Create a double bounce animation: up-down-up-down
            var bounceAnimation = new DoubleAnimationUsingKeyFrames
            {
                // CRITICAL: Stop after completion so timer-based float animation can resume
                FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
            };

            // First bounce (larger)
            bounceAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            bounceAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(-15, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80))));
            bounceAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160))));

            // Second bounce (smaller)
            bounceAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(-8, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220))));
            bounceAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(280))));

            // Apply to both avatar images (static and animated)
            AvatarTranslate.BeginAnimation(TranslateTransform.YProperty, bounceAnimation);
            AvatarAnimatedTranslate.BeginAnimation(TranslateTransform.YProperty, bounceAnimation);
        }

        /// <summary>
        /// Temporarily brings the tube window to front (above main window)
        /// Only works if attached and parent window is visible and not minimized
        /// </summary>
        private void BringToFrontTemporarily()
        {
            if (_tubeHandle == IntPtr.Zero) return;

            // Don't bring to front if detached (topmost handles that)
            if (!_isAttached) return;

            // Don't bring to front if parent window is not visible or minimized
            if (_parentWindow == null || !_parentWindow.IsVisible || _parentWindow.WindowState == WindowState.Minimized)
                return;

            // Bring window to top of z-order (above main window)
            // Use only SWP_NOACTIVATE - do NOT use SWP_SHOWWINDOW as it can interfere with keyboard focus
            SetWindowPos(_tubeHandle, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        /// <summary>
        /// Public hook for App.ForceWindowToFront: after main pulses Topmost
        /// to defeat ForegroundLockTimeout, the tube needs to be re-raised so
        /// the attached pair stays paired. Wraps the existing private method.
        /// </summary>
        public void RaiseAttachedTubeAboveOwner() => BringAttachedPairToFront(force: true);

        /// <summary>
        /// Bring both the parent window and the tube to the top of z-order together.
        /// This prevents the tube from being separated from the parent (e.g. tube on top,
        /// parent behind other apps) after video ends, fullscreen exit, or tab changes.
        /// </summary>
        /// <param name="force">
        /// When true, skip the "our process owns the foreground" gate. Use this only when
        /// the caller is DELIBERATELY foregrounding our app right now (startup show, a
        /// Topmost true→false pulse, panic/video-end restore). In those moments Activate()
        /// hasn't transferred foreground yet, so the gate sees the previous app and wrongly
        /// bails — leaving the tube/bubble buried behind the main window. Passive callers
        /// (poll timer, Activated, mouse-down, position changes) leave this false so we
        /// never steal z-order from other apps (e.g. fullscreen video players).
        /// </param>
        private void BringAttachedPairToFront(bool force = false)
        {
            if (_tubeHandle == IntPtr.Zero) return;
            if (!_isAttached) return;
            if (_parentWindow == null || !_parentWindow.IsVisible || _parentWindow.WindowState == WindowState.Minimized)
                return;

            // Don't fight with pop quiz — it uses HWND_TOPMOST and must stay on top
            if ((PopQuizWindow.IsOpen || QuizWindow.IsOpen))
                return;

            if (_parentHandle == IntPtr.Zero)
                _parentHandle = new WindowInteropHelper(_parentWindow).Handle;
            if (_parentHandle == IntPtr.Zero) return;

            // Only bring to front when our process owns the foreground window —
            // otherwise we'd steal z-order from other apps (e.g. fullscreen video players).
            // Skipped when force=true (caller is intentionally foregrounding our app).
            if (!force)
            {
                var foreground = GetForegroundWindow();
                if (foreground != IntPtr.Zero && foreground != _parentHandle && foreground != _tubeHandle)
                {
                    GetWindowThreadProcessId(foreground, out uint foregroundPid);
                    uint ourPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                    if (foregroundPid != ourPid)
                        return;
                }
            }

            // Parent to top first, then tube above it — keeps them as a pair
            SetWindowPos(_parentHandle, HWND_TOP, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            SetWindowPos(_tubeHandle, HWND_TOP, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        /// <summary>
        /// When the tube window gets activated while attached (e.g. after a topmost video window closes),
        /// redirect activation to the parent window so they stay paired.
        /// </summary>
        private void TubeWindow_Activated(object? sender, EventArgs e)
        {
            if (!_isAttached || _parentWindow == null) return;

            // Don't redirect activation when user is typing in the chat input
            if (_isInputVisible) return;

            // Don't activate parent when pop quiz is open — it would cover the quiz
            if ((PopQuizWindow.IsOpen || QuizWindow.IsOpen)) return;

            try
            {
                // Only redirect activation to parent if our process already owns the foreground —
                // otherwise we'd steal focus from other apps (e.g. fullscreen video players)
                var foreground = GetForegroundWindow();
                if (foreground != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(foreground, out uint foregroundPid);
                    uint ourPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                    if (foregroundPid != ourPid)
                        return; // Another app is in front, don't steal focus
                }

                // Don't redirect activation when speech bubble is showing —
                // redirecting brings parent to front, hiding the bubble behind it
                if (SpeechBubble.Visibility == Visibility.Visible)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_isAttached && _tubeHandle != IntPtr.Zero)
                            BringAttachedPairToFront();
                    }), DispatcherPriority.Background);
                    return;
                }

                // Defer activation to parent so Windows finishes current activation first.
                // Include BringAttachedPairToFront in the same callback to avoid a double-deferral
                // gap where the tube drops behind the parent between two Background dispatches.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if ((PopQuizWindow.IsOpen || QuizWindow.IsOpen)) return;
                        if (_isAttached && _parentWindow != null && _parentWindow.IsVisible
                            && _parentWindow.WindowState != WindowState.Minimized)
                        {
                            _parentWindow.Activate();
                            BringAttachedPairToFront();
                        }
                    }
                    catch { /* Window may be closing */ }
                }), DispatcherPriority.Background);
            }
            catch { /* Window may be closing */ }
        }

        /// <summary>
        /// Called by <see cref="Services.Chaos.ChaosModeService"/> around a run. A chaos run blankets the
        /// screen with TOPMOST windows (the FX vignette, payload washes, effect bubbles); an ATTACHED tube
        /// lives in the non-topmost band, so its speech bubbles get buried under that layer. Rather than
        /// fight the z-order, we simply auto-detach for the run — detached mode is a self-contained topmost
        /// widget that stays visible on top — and re-attach when the run ends. Only auto-restores if WE
        /// detached it (an already-detached avatar is left as the user set it).
        /// </summary>
        public void SetChaosRunActive(bool active)
        {
            if (_chaosRunActive == active) return;
            _chaosRunActive = active;
            try
            {
                if (active)
                {
                    // Detach so the companion floats above the run as a topmost widget. Remember to
                    // re-attach afterwards only if it was attached to begin with.
                    if (_isAttached)
                    {
                        _reattachAfterChaos = true;
                        Detach(silent: true);
                        // The attached anchor sits over the sidebar's Stop button; drop the
                        // detached widget down so the Chaos run's controls stay clickable.
                        try
                        {
                            double drop = 250;
                            var area = System.Windows.Forms.Screen.FromHandle(_tubeHandle).WorkingArea;
                            double maxTop = area.Bottom - Math.Max(120, ActualHeight) - 8;
                            Top = Math.Min(Top + drop, maxTop);
                        }
                        catch { /* positioning is best-effort */ }
                    }
                }
                else if (_reattachAfterChaos)
                {
                    _reattachAfterChaos = false;
                    Attach(silent: true);
                }
            }
            catch { /* window may be tearing down */ }
        }

        /// <summary>
        /// Reassert topmost status when detached - ensures avatar stays on top as a widget
        /// </summary>
        private void ReassertTopmost()
        {
            if (_tubeHandle == IntPtr.Zero || _isAttached) return;

            // Don't fight with pop quiz for topmost z-order
            if ((PopQuizWindow.IsOpen || QuizWindow.IsOpen)) return;

            // Use Win32 SetWindowPos with HWND_TOPMOST to force topmost z-order
            // This is more reliable than WPF's Topmost property across monitor/focus changes
            SetWindowPos(_tubeHandle, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        /// <summary>
        /// Start a timer that periodically brings the window to front while speech bubble is visible.
        /// This ensures the bubble stays on top even when user interacts with main window.
        /// </summary>
        private void StartZOrderRefreshTimer()
        {
            StopZOrderRefreshTimer();
            // Backstop to the ParentWndProc z-order hook. Runs at Render priority (NOT the
            // DispatcherTimer default of Background, which gets starved during busy AI
            // speech — GIF animation, text streaming, effects — and was letting the bubble
            // sit behind main for far longer than one tick). The hook does the heavy
            // lifting now; this just catches anything message-driven raises miss.
            _zOrderRefreshTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _zOrderRefreshTimer.Tick += (s, e) =>
            {
                if ((PopQuizWindow.IsOpen || QuizWindow.IsOpen)) return;
                if (_isAttached && _tubeHandle != IntPtr.Zero && SpeechBubble.Visibility == Visibility.Visible)
                {
                    // Only refresh z-order when our app owns the foreground — don't steal
                    // z-order from other apps. ParentWindow_Activated handles restoration
                    // when the user returns to us.
                    if (IsOurAppForeground())
                    {
                        BringAttachedPairToFront();
                    }
                }
            };
            _zOrderRefreshTimer.Start();
        }

        /// <summary>
        /// Stop the z-order refresh timer when speech bubble is hidden
        /// </summary>
        private void StopZOrderRefreshTimer()
        {
            _zOrderRefreshTimer?.Stop();
            _zOrderRefreshTimer = null;
        }

        private void StartIdleTimer()
        {
            var interval = App.Settings?.Current?.IdleGiggleIntervalSeconds ?? 120;
            _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(interval) };
            _idleTimer.Tick += OnIdleTick;
            _idleTimer.Start();
        }

        private void ResetIdleTimer()
        {
            _idleTimer?.Stop();
            StartIdleTimer();

            // Also report user activity to autonomy service
            App.Autonomy?.ReportUserActivity();
        }

        /// <summary>
        /// Restart idle timer with the current setting. Call when the user changes
        /// IdleGiggleIntervalSeconds via the slider so the running timer picks up
        /// the new value immediately instead of waiting for an unrelated reset.
        /// </summary>
        public void RestartIdleTimer()
        {
            _idleTimer?.Stop();
            StartIdleTimer();
        }

        private void OnIdleTick(object? sender, EventArgs e)
        {
            // Re-read setting on every tick so slider changes self-heal even if
            // RestartIdleTimer is never called (e.g. slider changed while no
            // speech is happening). DispatcherTimer applies the new Interval
            // after the current tick completes.
            var configured = App.Settings?.Current?.IdleGiggleIntervalSeconds ?? 120;
            if (_idleTimer != null && Math.Abs(_idleTimer.Interval.TotalSeconds - configured) > 0.5)
            {
                _idleTimer.Interval = TimeSpan.FromSeconds(configured);
            }

            // Skip if speech is on cooldown or currently showing
            if (!IsSpeechReady()) return;

            // Idle chatter IS the bark system now: this timer only sets the cadence (the
            // companion-tab slider); the line itself comes from the Idle bark pool with its
            // pool-wide no-repeat rotation. Preset phrases are just the no-barks fallback.
            // (flashes_audio voicelines are flash material and no longer spoken here.)
            if (App.Bark != null) App.Bark.DispatchIdle();
            else Giggle(GetRandomBambiPhrase());
        }

        // ============================================================
        // TRIGGER MODE - Random trigger phrases (free for all)
        // ============================================================

        private void StartTriggerTimer()
        {
            if (App.Settings?.Current?.TriggerModeEnabled != true)
            {
                App.Logger?.Debug("TriggerMode: Not enabled, skipping timer start");
                return;
            }

            var interval = App.Settings?.Current?.TriggerIntervalSeconds ?? 60;
            _triggerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(interval) };
            _triggerTimer.Tick += OnTriggerTick;
            _triggerTimer.Start();

            App.Logger?.Information("TriggerMode: Started with {Interval}s interval", interval);

            // Show first trigger immediately (after short delay for window to be ready)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                System.Threading.Tasks.Task.Delay(2000).ContinueWith(_ =>
                {
                    if (Application.Current?.Dispatcher?.HasShutdownStarted == true) return;
                    Dispatcher.Invoke(() => OnTriggerTick(null, EventArgs.Empty));
                });
            }));
        }

        private void StopTriggerTimer()
        {
            _triggerTimer?.Stop();
            _triggerTimer = null;
            App.Logger?.Debug("TriggerMode: Timer stopped");
        }

        /// <summary>
        /// Restart trigger timer (call when settings change)
        /// </summary>
        public void RestartTriggerTimer()
        {
            StopTriggerTimer();
            StartTriggerTimer();
        }

        // ============================================================
        // RANDOM BUBBLE TIMER - Spawns clickable bubbles near avatar
        // ============================================================

        private void StartRandomBubbleTimer()
        {
            if (App.Settings?.Current?.RandomBubbleEnabled != true)
            {
                App.Logger?.Debug("RandomBubble: Not enabled, skipping timer start");
                return;
            }

            // Random interval between 3-5 minutes (180-300 seconds)
            var interval = _random.Next(180, 301);
            _randomBubbleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(interval) };
            _randomBubbleTimer.Tick += OnRandomBubbleTick;
            _randomBubbleTimer.Start();

            App.Logger?.Information("RandomBubble: Started with {Interval}s interval", interval);
        }

        private void StopRandomBubbleTimer()
        {
            _randomBubbleTimer?.Stop();
            _randomBubbleTimer = null;
            App.Logger?.Debug("RandomBubble: Timer stopped");
        }

        /// <summary>
        /// Restart random bubble timer (call when settings change)
        /// </summary>
        public void RestartRandomBubbleTimer()
        {
            StopRandomBubbleTimer();
            StartRandomBubbleTimer();
        }

        private void OnRandomBubbleTick(object? sender, EventArgs e)
        {
            // Re-randomize interval for next tick (3-5 minutes)
            if (_randomBubbleTimer != null)
            {
                var nextInterval = _random.Next(180, 301);
                _randomBubbleTimer.Interval = TimeSpan.FromSeconds(nextInterval);
            }

            // Skip if avatar is not in focus (another app is in foreground)
            IntPtr foregroundWindow = GetForegroundWindow();
            if (foregroundWindow != _tubeHandle && foregroundWindow != _parentHandle)
            {
                GetWindowThreadProcessId(foregroundWindow, out uint foregroundPid);
                uint ourPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                if (foregroundPid != ourPid)
                {
                    App.Logger?.Debug("RandomBubble: Skipped - app not in focus");
                    return;
                }
            }

            // Show phrase and spawn bubble
            SpawnRandomBubble();
        }

        private void SpawnRandomBubble()
        {
            // Pick a random phrase (mode-aware, filtered by service)
            GiggleFromCategory("RandomBubble");

            // Spawn a bubble near the avatar after 1 second (speech bubble appears first)
            Task.Delay(1000).ContinueWith(_ =>
            {
                if (Application.Current?.Dispatcher?.HasShutdownStarted == true) return;
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        // Get avatar position in screen coordinates
                        var avatarPos = AvatarBorder.PointToScreen(new Point(
                            AvatarBorder.ActualWidth / 2,
                            AvatarBorder.ActualHeight / 2));

                        // Create and show the bubble
                        var bubble = new AvatarRandomBubble(avatarPos, _random, OnRandomBubblePopped);
                        App.Logger?.Debug("RandomBubble: Spawned at ({X}, {Y})", avatarPos.X, avatarPos.Y);
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning("RandomBubble: Failed to spawn - {Error}", ex.Message);
                    }
                });
            });
        }

        private void OnRandomBubblePopped()
        {
            // Play pop sound (use same sound as bubble service)
            PlayBubblePopSound();

            // Award XP
            App.Progression?.AddXP(5, XPSource.AvatarInteraction);

            // Show reaction
            Giggle("Good girl! *giggles*");
        }

        private void PlayBubblePopSound()
        {
            try
            {
                var soundsPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "bubbles");
                var popFiles = new[] { "Pop.mp3", "Pop2.mp3", "Pop3.mp3" };
                var chosenPop = popFiles[_random.Next(popFiles.Length)];
                var popPath = System.IO.Path.Combine(soundsPath, chosenPop);

                if (System.IO.File.Exists(popPath))
                {
                    var masterVolume = (App.Settings?.Current?.MasterVolume ?? 100) / 100f;
                    var bubblesVolume = (App.Settings?.Current?.BubblesVolume ?? 50) / 100f;
                    var normalizedVolume = Math.Max(0.05f, (float)Math.Pow(bubblesVolume * masterVolume, 1.5));

                    Task.Run(() =>
                    {
                        try
                        {
                            using var audioFile = new NAudio.Wave.AudioFileReader(popPath);
                            audioFile.Volume = normalizedVolume;
                            using var outputDevice = new NAudio.Wave.WaveOutEvent();
                            outputDevice.Init(audioFile);
                            outputDevice.Play();
                            while (outputDevice.PlaybackState == NAudio.Wave.PlaybackState.Playing)
                            {
                                System.Threading.Thread.Sleep(50);
                            }
                        }
                        catch { }
                    });
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("RandomBubble: Failed to play pop sound - {Error}", ex.Message);
            }
        }

        // ============================================================
        // VOICE LINE SYSTEM - Audio files used for idle/trigger comments
        // ============================================================

        /// <summary>
        /// Refreshes the list of voice line files from the flash audio folder
        /// </summary>
        private void RefreshVoiceLines()
        {
            try
            {
                if (!System.IO.Directory.Exists(_voiceLinesPath))
                {
                    _voiceLineFiles.Clear();
                    return;
                }

                var extensions = new[] { "*.mp3", "*.wav", "*.ogg" };
                var files = new List<string>();
                foreach (var ext in extensions)
                {
                    files.AddRange(System.IO.Directory.GetFiles(_voiceLinesPath, ext));
                }
                _voiceLineFiles = files;
                App.Logger?.Debug("VoiceLines: Loaded {Count} voice line files", _voiceLineFiles.Count);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("VoiceLines: Failed to load - {Error}", ex.Message);
                _voiceLineFiles.Clear();
            }
        }

        /// <summary>
        /// Gets a random voice line file path (filtered by phrase manager)
        /// </summary>
        private string? GetRandomVoiceLinePath()
        {
            // Use service filtering if available
            var enabledFiles = App.CompanionPhrases?.GetEnabledVoiceLineFiles();
            if (enabledFiles != null && enabledFiles.Count > 0)
                return enabledFiles[_random.Next(enabledFiles.Count)];

            // Fallback to unfiltered list
            if (_voiceLineFiles.Count == 0)
                RefreshVoiceLines();

            if (_voiceLineFiles.Count == 0)
                return null;

            return _voiceLineFiles[_random.Next(_voiceLineFiles.Count)];
        }

        /// <summary>
        /// Plays a voice line audio file. Suppressed entirely when the avatar
        /// menu's "Mute Whispers" toggle is on (SubAudioEnabled=false) — the
        /// bubble + text still show, only the attached voiceline goes silent.
        /// </summary>
        private void PlayVoiceLineAudio(string filePath)
        {
            if (App.Settings?.Current?.SubAudioEnabled != true) return;
            var masterVolume = (App.Settings?.Current?.MasterVolume ?? 100) / 100f;
            if (masterVolume <= 0f) return;
            // Idle voicelines were full-volume (×1.0); -20% so today's hotter v3 lines sit under the barks.
            var volume = (float)Math.Pow(masterVolume, 1.5) * 0.8f;
            PlaySpokenAudio(filePath, volume);
        }

        /// <summary>Stops any currently playing spoken line (kept for external callers).</summary>
        public void StopVoiceLineAudio() => StopSpokenAudio();

        /// <summary>
        /// The single companion-voice channel. Cuts off whatever is currently speaking, then plays
        /// <paramref name="filePath"/>. Drives <see cref="_isSpeakingAudio"/> for the speaking wobble/mist,
        /// for exactly the clip's duration. All voice paths (bark / event / idle) route through here so two
        /// lines never overlap.
        /// </summary>
        private void PlaySpokenAudio(string filePath, float volume)
        {
            try
            {
                if (!System.IO.File.Exists(filePath)) return;
                StopSpokenAudio(); // cut off the previous line → no overlap

                NAudio.Wave.AudioFileReader reader;
                NAudio.Wave.WaveOutEvent player;
                try
                {
                    reader = new NAudio.Wave.AudioFileReader(filePath) { Volume = volume };
                    player = new NAudio.Wave.WaveOutEvent();
                    player.Init(reader);
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("PlaySpokenAudio: init failed - {Error}", ex.Message);
                    return;
                }

                lock (_spokenLock) { _spokenReader = reader; _spokenPlayer = player; _isSpeakingAudio = true; }

                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        player.Play();
                        while (player.PlaybackState == NAudio.Wave.PlaybackState.Playing)
                            System.Threading.Thread.Sleep(40);
                    }
                    catch { /* ignore audio errors */ }
                    finally
                    {
                        lock (_spokenLock)
                        {
                            if (ReferenceEquals(_spokenPlayer, player)) // not already replaced by a newer line
                            {
                                _spokenPlayer = null;
                                _spokenReader = null;
                                _isSpeakingAudio = false; // stops the speaking wobble/mist exactly at clip end
                            }
                        }
                        try { player.Dispose(); } catch { }
                        try { reader.Dispose(); } catch { }
                    }
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("PlaySpokenAudio failed - {Error}", ex.Message);
            }
        }

        /// <summary>Stop the current spoken line immediately (its play-loop sees Stopped and disposes).</summary>
        public void StopSpokenAudio()
        {
            NAudio.Wave.WaveOutEvent? p;
            lock (_spokenLock) { p = _spokenPlayer; }
            try { p?.Stop(); } catch { }
        }

        /// <summary>
        /// Shows a voice line as a speech bubble with synchronized audio playback
        /// </summary>
        private void ShowVoiceLineBubble(string filePath)
        {
            DispatcherHelper.RunOnUI(() =>
            {
                if (_isMuted || !IsAvatarVisibleOnScreen) return;

                var text = App.CompanionPhrases?.GetVoiceLineDisplayText(filePath)
                    ?? System.IO.Path.GetFileNameWithoutExtension(filePath);
                // Activity voicelines bake the "{0}" app placeholder into their
                // filename ("target application"); swap it for the focused app so
                // the bubble names what's actually on screen instead of the literal.
                text = Services.CompanionPhraseService.ResolveVoiceLinePlaceholder(text);
                if (string.IsNullOrWhiteSpace(text)) return;

                // Clear the queue - voice line takes priority
                _speechQueue.Clear();
                _speechTimer?.Stop();
                _speechDelayTimer?.Stop();

                _isGiggling = true;
                PopulateSpeechBubble(text);
                var plainText = MarkdownLinkRegex.Replace(text ?? "", "$1");
                AdjustBubbleSize(plainText);

                SpeechBubble.UpdateLayout();
                SpeechBubble.Visibility = Visibility.Visible;

                // Start z-order refresh to keep bubble on top of main window
                // Skip all z-order work when pop quiz is open — must not cover the quiz
                if (!(PopQuizWindow.IsOpen || QuizWindow.IsOpen))
                {
                    StartZOrderRefreshTimer();
                    BringAttachedPairToFront();
                }

                // Play the voice line audio in sync with the bubble
                PlayVoiceLineAudio(filePath);

                // Drive the portrait emotion: idle affirmations are unmapped → seductive mix
                // (alluring/dreamy/entrancing/teasing), pose count scaled to the line's audio length.
                PlayEmotionForLine(System.IO.Path.GetFileNameWithoutExtension(filePath), filePath, text);

                App.Logger?.Information("VoiceLine: Displayed '{Text}'", text);

                // Calculate display duration based on text length
                double baseDuration = 5.0;
                double perCharDuration = 0.05;
                double calculatedDuration = baseDuration + (text.Length * perCharDuration);
                double displayDuration = Math.Clamp(calculatedDuration, 5.0, 14.0);

                var textLength = text.Length;
                _speechTimer?.Stop();
                _speechTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(displayDuration) };
                _speechTimer.Tick += (s, e) =>
                {
                    if (_isMouseOverSpeechBubble)
                    {
                        _speechTimer.Interval = TimeSpan.FromSeconds(1);
                        return;
                    }
                    _speechTimer.Stop();
                    _isGiggling = false;
                    _isShowingAiBubble = false; // Clear AI bubble flag when any bubble hides
                    SpeechBubble.Visibility = Visibility.Collapsed;
                    StopZOrderRefreshTimer();

                    _lastSpeechEndTime = DateTime.Now;
                    _lastSpeechSource = SpeechSource.Preset;
                    _lastSpeechLength = textLength;
                    ProcessNextSpeech();
                };
                _speechTimer.Start();
            });
        }

        private void OnTriggerTick(object? sender, EventArgs e)
        {
            // Skip if speech is on cooldown or currently showing
            if (!IsSpeechReady()) return;

            // Trigger mode speaks triggers only — the flashes_audio voicelines are flash
            // material, no longer borrowed here (they repeated badly: uniform random, no memory).
            var triggers = App.Settings?.Current?.CustomTriggers;
            if (triggers == null || triggers.Count == 0)
            {
                App.Logger?.Debug("TriggerMode: No triggers configured");
                return;
            }

            // Pick a random trigger from subliminal pool
            var trigger = triggers[_random.Next(triggers.Count)];

            // Show it as a speech bubble
            ShowTriggerBubble(trigger);
        }

        private void ShowTriggerBubble(string trigger)
        {
            // Use direct dispatcher invoke to ensure audio plays exactly when bubble shows
            DispatcherHelper.RunOnUI(() =>
            {
                // When muted, still trigger haptic+audio but skip visual queue logic
                if (_isMuted)
                {
                    ShowTriggerBubbleImmediate(trigger); // Will handle haptic+audio even when muted
                    return;
                }

                // Check if we need to delay based on last speech (delay based on PREVIOUS speech properties)
                double timeSinceLastSpeech = (DateTime.Now - _lastSpeechEndTime).TotalSeconds;
                double requiredDelay = CalculateRequiredDelayAfterLastSpeech();

                if (_isGiggling || timeSinceLastSpeech < requiredDelay)
                {
                    // Queue the trigger and let delay system handle it
                    _speechQueue.Enqueue((trigger, SpeechSource.Trigger, null, null));
                    App.Logger?.Debug("Queued trigger speech: {Trigger}", trigger);
                    if (!_isGiggling)
                    {
                        _isGiggling = true;
                        ProcessNextSpeech();
                    }
                    return;
                }

                // Show the speech bubble immediately
                ShowTriggerBubbleImmediate(trigger);
            });
        }

        /// <summary>
        /// Internal method to show trigger bubble immediately (called after delay if needed)
        /// </summary>
        private void ShowTriggerBubbleImmediate(string trigger)
        {
            // ALWAYS trigger haptic, even when muted/off-screen
            _ = App.Haptics?.TriggerSubliminalPatternAsync(trigger);

            // Skip visual and audio if muted OR avatar not visible on screen
            if (_isMuted || !IsAvatarVisibleOnScreen)
            {
                _isGiggling = false;
                // Track timing and properties even when hidden (for delay calculation)
                _lastSpeechEndTime = DateTime.Now;
                _lastSpeechSource = SpeechSource.Trigger;
                _lastSpeechLength = trigger.Length;
                ProcessNextSpeech();
                App.Logger?.Information("TriggerMode: Haptic only for '{Trigger}' (avatar not visible)", trigger);
                return;
            }

            _isGiggling = true;

            // Defer the trigger audio + bubble by the same lead-in as spoken lines, and type the word
            // out with the slow (non-AI) typewriter.
            Action speak = () =>
            {
                if (!_isGiggling) return; // a newer bubble took over during the lead-in

                // Play trigger audio only when avatar is visible
                App.Subliminal?.PlayTriggerAudio(trigger);

                StartTypewriter(trigger, slow: true);
                var plainText = MarkdownLinkRegex.Replace(trigger ?? "", "$1");
                AdjustBubbleSize(plainText);

                // Force layout update before showing to prevent flickering
                SpeechBubble.UpdateLayout();
                SpeechBubble.Visibility = Visibility.Visible;

                // Start z-order refresh to keep bubble on top of main window
                // Skip all z-order work when pop quiz is open — must not cover the quiz
                if (!(PopQuizWindow.IsOpen || QuizWindow.IsOpen))
                {
                    StartZOrderRefreshTimer();
                    BringAttachedPairToFront();
                }

                App.Logger?.Information("TriggerMode: Displayed trigger '{Trigger}'", trigger);

                // Calculate display duration based on text length (+ typewriter runtime so the word
                // doesn't vanish before it's finished typing).
                double baseDuration = 5.0;
                double perCharDuration = 0.05;
                double calculatedDuration = baseDuration + (trigger.Length * perCharDuration);
                double displayDuration = Math.Clamp(calculatedDuration, 5.0, 14.0)
                                         + EstimateTypewriterDurationMs(trigger.Length, slow: true) / 1000.0;

                // Hide after calculated duration
                _speechTimer?.Stop();
                _speechTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(displayDuration) };

                // Capture trigger length for delay calculation
                var triggerLength = trigger.Length;

                _speechTimer.Tick += (s, e) =>
                {
                    // If mouse is over speech bubble, keep it open - recheck in 1 second
                    if (_isMouseOverSpeechBubble)
                    {
                        _speechTimer.Interval = TimeSpan.FromSeconds(1);
                        return; // Don't stop timer, keep checking
                    }

                    _speechTimer.Stop();
                    StopZOrderRefreshTimer();
                    SpeechBubble.Visibility = Visibility.Collapsed;
                    _isShowingAiBubble = false; // Clear AI bubble flag when any bubble hides

                    // Track this speech's properties for delay calculation on next speech
                    _lastSpeechEndTime = DateTime.Now;
                    _lastSpeechSource = SpeechSource.Trigger;
                    _lastSpeechLength = triggerLength;

                    // Process next speech with proper delay handling
                    ProcessNextSpeech();
                };
                _speechTimer.Start();

                // Reset idle timer when speaking
                ResetIdleTimer();
            };

            _speechLeadInTimer?.Stop();
            _speechLeadInTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(SpeechLeadInSeconds) };
            _speechLeadInTimer.Tick += (s, e) =>
            {
                _speechLeadInTimer?.Stop();
                _speechLeadInTimer = null;
                speak();
            };
            _speechLeadInTimer.Start();
        }

        /// <summary>
        /// Greeting phrases when the app starts
        /// </summary>
        private static readonly string[] GreetingPhrases = new[]
        {
            "Hi Bambi! Ready to get conditioned?~",
            "*bounces* Yay! You're back!",
            "Welcome back, bestie!~",
            "Ooh! Time for some fun~",
            "Hi cutie! Let's get ditzy!",
            "*giggles* There you are!~",
            "Ready to drop, good girl?",
            "Pink thoughts incoming!~"
        };

        /// <summary>
        /// Phrases when the engine stops
        /// </summary>
        private static readonly string[] EngineStopPhrases = new[]
        {
            "I feel dizzy...",
            "Aw... Bambi was having fun...",
            "*blinks* W-what happened?",
            "Mmmm that was nice~",
            "Already? But we were vibing!",
            "My head feels so fuzzy...",
            "*wobbles* Whoa...",
            "Can we do that again soon?~",
            "So floaty right now...",
            "*dreamy sigh* That was good~"
        };

        /// <summary>
        /// Phrases when spawning a random bubble
        /// </summary>
        private static readonly string[] RandomBubblePhrases = new[]
        {
            "Be a good girl and burst that bubble!",
            "Oh... here's a bubble for you~",
            "*Pop* Catch it, Bambi!",
            "Bubble time! Pop it~",
            "Look! A pretty bubble!",
            "*giggles* Pop it quick!",
            "Ooh, get the bubble!",
            "Pop it for me, good girl~"
        };

        /// <summary>
        /// Generic phrases that work for both modes
        /// </summary>
        /// <summary>
        /// Get a random themed phrase based on current content mode
        /// </summary>
        private string GetRandomBambiPhrase()
        {
            var svc = App.CompanionPhrases;

            var genericEnabled = svc?.GetEnabledPhrases("Generic") ?? App.Mods?.GetPhrases("Generic") ?? System.Array.Empty<string>();
            var floatingEnabled = svc?.GetEnabledPhrases("RandomFloating") ?? App.Mods?.GetPhrases("RandomFloating") ?? System.Array.Empty<string>();
            var allPhrases = genericEnabled.Concat(floatingEnabled).ToArray();

            if (allPhrases.Length == 0)
            {
                // Fallback if all phrases disabled
                var fallback = (App.Mods?.GetPhrases("Generic") ?? System.Array.Empty<string>())
                    .Concat(App.Mods?.GetPhrases("RandomFloating") ?? System.Array.Empty<string>()).ToArray();
                if (fallback.Length == 0) return "*giggles*";
                return fallback[_random.Next(fallback.Length)];
            }

            return allPhrases[_random.Next(allPhrases.Length)];
        }

        /// <summary>
        /// Picks a random enabled phrase from a category and giggles it,
        /// playing any custom audio that's been attached to it.
        /// </summary>
        private void GiggleFromCategory(string category)
        {
            var svc = App.CompanionPhrases;
            var enabled = svc?.GetEnabledPhrases(category);

            if (enabled == null || enabled.Length == 0)
                return; // All phrases in this category disabled

            var text = enabled[_random.Next(enabled.Length)];

            // Resolve phrase audio
            string? audioPath = null;
            var phraseId = svc?.GetPhraseId(category, text);
            if (phraseId != null)
            {
                var audioFile = GetPhraseAudioFile(phraseId);
                if (audioFile != null)
                    audioPath = System.IO.Path.Combine(Services.CompanionPhraseService.CompanionAudioFolder, audioFile);
            }

            // No manual override? Fall back to a mod-shipped event-audio file keyed by the line text.
            // Sissy voices these; mods without an event_audio folder resolve to null and stay text-only.
            audioPath ??= Services.CompanionPhraseService.ResolveEventAudio(text);

            Giggle(text, audioPath);
        }

        /// <summary>
        /// Gets the audio filename for a phrase ID (checks overrides and custom).
        /// </summary>
        private string? GetPhraseAudioFile(string phraseId)
        {
            var settings = App.Settings?.Current;
            if (settings == null) return null;

            if (settings.PhraseAudioOverrides.TryGetValue(phraseId, out var overrideFile))
            {
                var path = System.IO.Path.Combine(Services.CompanionPhraseService.CompanionAudioFolder, overrideFile);
                if (System.IO.File.Exists(path)) return overrideFile;
            }

            var custom = settings.CustomCompanionPhrases?.FirstOrDefault(c => c.Id == phraseId);
            if (custom?.AudioFileName != null)
            {
                var path = System.IO.Path.Combine(Services.CompanionPhraseService.CompanionAudioFolder, custom.AudioFileName);
                if (System.IO.File.Exists(path)) return custom.AudioFileName;
            }

            return null;
        }

        // Counters for feature awareness
        private int _subliminalCounter = 0;
        private int _flashCounter = 0;

        /// <summary>
        /// Get a random Bambi Sleep themed phrase for a specific activity category.
        /// Phrases may include {0} placeholder for the detected app/service name.
        /// </summary>
        private string GetPhraseForCategory(ActivityCategory category, string detectedName = "")
        {
            // Check for special services first
            var lowerName = detectedName?.ToLowerInvariant() ?? "";
            var svc = App.CompanionPhrases;

            // Discord - special phrases
            if (lowerName.Contains("discord"))
            {
                var discordPhrases = svc?.GetEnabledPhrases("Discord") is { Length: > 0 } dp
                    ? dp : App.Mods?.GetPhrases("Discord") ?? System.Array.Empty<string>();
                if (discordPhrases.Length == 0) return "*giggles*";
                return discordPhrases[_random.Next(discordPhrases.Length)];
            }

            // BambiCloud/Hypnotube - positive reinforcement (training sites)
            if (lowerName.Contains("bambicloud") || lowerName.Contains("hypnotube"))
            {
                var sitePhrases = svc?.GetEnabledPhrases("TrainingSite") is { Length: > 0 } sp
                    ? sp : App.Mods?.GetPhrases("TrainingSite") ?? System.Array.Empty<string>();
                if (sitePhrases.Length == 0) return "*giggles*";
                return sitePhrases[_random.Next(sitePhrases.Length)];
            }

            // Hypno content in tab name - congratulate for bimbofication
            if (lowerName.Contains("bambi") || lowerName.Contains("sissy") || lowerName.Contains("hypno"))
            {
                var hypnoPhrases = svc?.GetEnabledPhrases("HypnoContent") is { Length: > 0 } hp
                    ? hp : App.Mods?.GetPhrases("HypnoContent") ?? System.Array.Empty<string>();
                if (hypnoPhrases.Length == 0) return "*giggles*";
                return hypnoPhrases[_random.Next(hypnoPhrases.Length)];
            }

            var categoryName = category switch
            {
                ActivityCategory.Gaming => "Gaming",
                ActivityCategory.Browsing => "Browsing",
                ActivityCategory.Shopping => "Shopping",
                ActivityCategory.Social => "Social",
                ActivityCategory.Working => "Working",
                ActivityCategory.Media => "Media",
                ActivityCategory.Learning => "Learning",
                ActivityCategory.Idle => "WindowAwarenessIdle",
                _ => "RandomFloating"
            };

            var phrases = svc?.GetEnabledPhrases(categoryName) is { Length: > 0 } enabled
                ? enabled
                : App.Mods?.GetPhrases(categoryName) ?? System.Array.Empty<string>();
            if (phrases.Length == 0) phrases = new[] { "*giggles*" };

            var phrase = phrases[_random.Next(phrases.Length)];

            // Replace {0} placeholder with detected name if present
            if (phrase.Contains("{0}") && !string.IsNullOrEmpty(detectedName))
            {
                phrase = string.Format(phrase, detectedName);
            }
            else if (phrase.Contains("{0}"))
            {
                // Remove placeholder if no name detected
                phrase = phrase.Replace("{0} ", "").Replace("{0}", "").Replace("  ", " ").Trim();
            }

            return phrase;
        }

        /// <summary>
        /// Handle activity change from WindowAwarenessService
        /// </summary>
        private async void OnActivityChanged(object? sender, ActivityChangedEventArgs e)
        {
            // Don't trigger during startup cooldown (let greeting show first)
            if ((DateTime.Now - _startupTime).TotalSeconds < StartupCooldownSeconds)
                return;

            // Don't trigger if speech bubble is still showing - wait for user to clear it
            if (SpeechBubble.Visibility == Visibility.Visible)
                return;

            // Check if we're allowed to react to this category
            if (!App.WindowAwareness?.IsCategoryEnabled(e.Category) ?? true)
                return;

            // Check user-configured cooldown from settings
            if (!App.WindowAwareness?.CanReact() ?? true)
                return;

            // Mark that we're reacting (resets cooldown timer)
            App.WindowAwareness?.MarkReaction();

            // Always use the currently focused window's full context
            // Use service name as primary, with page title for additional context
            string displayName = string.IsNullOrEmpty(e.ServiceName) ? e.DetectedName : e.ServiceName;
            string pageTitle = e.PageTitle ?? "";

            // Try AI first, fall back to preset phrase
            string? reaction = null;
            bool isAiResponse = false;

            if (App.Settings?.Current?.AiChatEnabled == true && App.Ai?.IsAvailable == true)
            {
                try
                {
                    // Pass full context from currently focused window
                    reaction = await App.Ai.GetAwarenessReactionAsync(displayName, e.Category.ToString(), e.ServiceName, pageTitle);
                    if (reaction != null)
                    {
                        // No truncation - scrollable speech bubble handles long text
                        isAiResponse = true;
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Failed to get AI awareness reaction");
                }
            }

            // Use preset if AI didn't work
            reaction ??= GetPhraseForCategory(e.Category, displayName);

            // AI responses get priority and double bounce, presets queue normally
            if (isAiResponse)
            {
                PlayDoubleBounce();
                GigglePriority(reaction);
            }
            else
            {
                Giggle(reaction);
            }

            App.Logger?.Debug("Awareness reaction for {DisplayName} ({Category}): {Reaction}",
                displayName, e.Category, reaction);
        }

        /// <summary>
        /// Handle "still on" activity event - user has been on the same activity for a while
        /// </summary>
        private async void OnStillOnActivity(object? sender, ActivityChangedEventArgs e)
        {
            // Don't trigger during startup cooldown (let greeting show first)
            if ((DateTime.Now - _startupTime).TotalSeconds < StartupCooldownSeconds)
                return;

            // Don't trigger if speech bubble is still showing - wait for user to clear it
            if (SpeechBubble.Visibility == Visibility.Visible)
                return;

            // Check if we're allowed to react to this category
            if (!App.WindowAwareness?.IsCategoryEnabled(e.Category) ?? true)
                return;

            // Check user-configured cooldown from settings
            if (!App.WindowAwareness?.CanStillOnReact() ?? true)
                return;

            // Mark that we're reacting (resets cooldown timer)
            App.WindowAwareness?.MarkStillOnReaction();

            // Get duration from the awareness service
            var duration = App.WindowAwareness?.CurrentActivityDuration ?? TimeSpan.Zero;

            // 50/50 chance to use just service name vs page title
            bool useServiceNameOnly = _random.Next(2) == 0;
            string displayName = useServiceNameOnly || string.IsNullOrEmpty(e.PageTitle)
                ? e.ServiceName
                : e.PageTitle;

            // Try AI first, fall back to preset phrase
            string? reaction = null;
            bool isAiResponse = false;

            if (App.Settings?.Current?.AiChatEnabled == true && App.Ai?.IsAvailable == true)
            {
                try
                {
                    // Use the selected display name based on 50/50 choice
                    reaction = await App.Ai.GetStillOnReactionAsync(displayName, e.Category.ToString(), duration);
                    if (reaction != null)
                    {
                        // No truncation - scrollable speech bubble handles long text
                        isAiResponse = true;
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Failed to get AI still-on reaction");
                }
            }

            // Use preset if AI didn't work - include time in the fallback
            if (reaction == null)
            {
                var minutes = (int)duration.TotalMinutes;
                var timeText = minutes < 1 ? "a bit" : $"{minutes} min";
                reaction = $"Still on {displayName}? {timeText} already~ Do your nails instead!";
            }

            // AI responses get priority
            if (isAiResponse)
                GigglePriority(reaction);
            else
                Giggle(reaction);

            App.Logger?.Debug("Still-on reaction for {DisplayName} ({Duration}, useServiceOnly={UseService}): {Reaction}",
                displayName, duration, useServiceNameOnly, reaction);
        }

        /// <summary>
        /// Plays a fallback sound when no specific audio is connected to a speech bubble.
        /// Randomly chooses between "um" sounds and giggle sounds.
        /// </summary>
        private void PlayFallbackBubbleSound()
        {
            try
            {
                // Use giggle sounds 1-4 for regular speech bubbles
                var fallbackSounds = new[] {
                    "giggle1.MP3", "giggle2.MP3", "giggle3.MP3", "giggle4.MP3"
                };
                var chosenSound = fallbackSounds[_random.Next(fallbackSounds.Length)];
                var soundPath = Services.ModResourceResolver.ResolveAudioPath(chosenSound);

                if (!System.IO.File.Exists(soundPath))
                {
                    App.Logger?.Debug("Fallback sound not found: {Path}", soundPath);
                    return;
                }

                var masterVolume = (App.Settings?.Current?.MasterVolume ?? 100) / 100f;
                // Keep fallback sounds quieter (50% of master)
                var volume = (float)Math.Pow(masterVolume, 1.5) * 0.5f;

                Task.Run(() =>
                {
                    try
                    {
                        using var audioFile = new NAudio.Wave.AudioFileReader(soundPath);
                        audioFile.Volume = volume;
                        using var outputDevice = new NAudio.Wave.WaveOutEvent();
                        outputDevice.Init(audioFile);
                        outputDevice.Play();
                        while (outputDevice.PlaybackState == NAudio.Wave.PlaybackState.Playing)
                        {
                            System.Threading.Thread.Sleep(50);
                        }
                    }
                    catch { /* Ignore audio errors */ }
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to play fallback bubble sound: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Play a random pop sound when clicking the avatar
        /// </summary>
        /// <summary>
        /// Plays a custom phrase audio file (NAudio pattern from PlayGiggleSound).
        /// Suppressed when "Mute Whispers" is on so the phrase bubble still
        /// appears but the attached audio doesn't play.
        /// </summary>
        /// <summary>
        /// Play a real recorded clip (e.g. the New Year note) uninterruptibly: no giggle sound, no
        /// dom voice, just the file. Holds <c>_isPlayingUninterruptibleClip</c> so nothing preempts
        /// it and shows a silent priority bubble for the clip's duration. No-ops gracefully (returns
        /// false) if the file is missing, so it can be wired before the recording ships. Returns true
        /// only if a real clip actually started — callers latch their once-ever flag on true.
        /// </summary>
        public bool PlayNoteClip(string clipPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(clipPath) || !System.IO.File.Exists(clipPath))
                {
                    App.Logger?.Information("PlayNoteClip: no clip at {Path} — skipping gracefully", clipPath);
                    return false;
                }

                DispatcherHelper.RunOnUI(() =>
                {
                    _isPlayingUninterruptibleClip = true;
                    // Silent, top-priority bubble for the clip's duration. Source=AI suppresses the
                    // fallback pop; playSound:false means no giggle; bypassClipLock renders past the latch.
                    ShowGiggle(NoteClipCaption, playSound: false, source: SpeechSource.AI,
                        phraseAudioPath: null, aiGenerated: false, bypassClipLock: true);
                });

                var masterVolume = (App.Settings?.Current?.MasterVolume ?? 100) / 100f;
                var volume = (float)Math.Pow(masterVolume, 1.5) * 0.9f; // a touch louder — it's a once-ever moment

                Task.Run(() =>
                {
                    try
                    {
                        using var audioFile = new NAudio.Wave.AudioFileReader(clipPath);
                        audioFile.Volume = volume;
                        using var outputDevice = new NAudio.Wave.WaveOutEvent();
                        outputDevice.Init(audioFile);
                        outputDevice.Play();
                        while (outputDevice.PlaybackState == NAudio.Wave.PlaybackState.Playing)
                            System.Threading.Thread.Sleep(50);
                    }
                    catch (Exception ex) { App.Logger?.Warning(ex, "PlayNoteClip: playback failed"); }
                    finally
                    {
                        DispatcherHelper.RunOnUI(() => _isPlayingUninterruptibleClip = false);
                    }
                });
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "PlayNoteClip failed");
                _isPlayingUninterruptibleClip = false;
                return false;
            }
        }

        /// <summary>
        /// Play a companion bark voiceline. Gated on BOTH MasterVolume (master/mute control) and
        /// SubAudioEnabled — bark voicelines are "whispers", so the Mute Whispers toggle silences them
        /// too. When whispers are muted the bubble still shows; it just has no voiceline audio.
        /// </summary>
        private void PlayBarkVoice(string audioPath)
        {
            try
            {
                if (!System.IO.File.Exists(audioPath)) return;

                // Mute Whispers (SubAudioEnabled == false) silences spoken barks, same as subliminal whispers.
                if (App.Settings?.Current?.SubAudioEnabled != true) return;

                var masterVolume = (App.Settings?.Current?.MasterVolume ?? 0) / 100f;
                if (masterVolume <= 0f) return; // muted
                var volume = (float)Math.Pow(masterVolume, 1.5) * 0.85f;
                PlaySpokenAudio(audioPath, volume); // unified channel → no overlap
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to play bark voice: {Error}", ex.Message);
            }
        }

        private void PlayPhraseAudio(string audioPath)
        {
            try
            {
                if (!System.IO.File.Exists(audioPath)) return;
                if (App.Settings?.Current?.SubAudioEnabled != true) return;

                var masterVolume = (App.Settings?.Current?.MasterVolume ?? 100) / 100f;
                if (masterVolume <= 0f) return;
                // Event lines were ×0.7; -20% (→ ×0.56) so today's hotter v3 lines sit under the barks.
                var volume = (float)Math.Pow(masterVolume, 1.5) * 0.56f;
                PlaySpokenAudio(audioPath, volume); // unified channel → no overlap
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to play phrase audio: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Plays a random giggle sound (giggle1-4.mp3) for AI responses or preset phrases
        /// </summary>
        private void PlayGiggleSound()
        {
            try
            {
                // Use giggle sounds 5-8 for AI responses (reserved for special interactions)
                var giggleFiles = new[] {
                    "giggle5.mp3", "giggle6.mp3", "giggle7.mp3", "giggle8.mp3"
                };
                var chosenGiggle = giggleFiles[_random.Next(giggleFiles.Length)];
                var gigglePath = Services.ModResourceResolver.ResolveAudioPath(chosenGiggle);

                if (System.IO.File.Exists(gigglePath))
                {
                    var masterVolume = (App.Settings?.Current?.MasterVolume ?? 100) / 100f;
                    // Apply volume curve, cap at 70% of master to not be too loud
                    var volume = (float)Math.Pow(masterVolume, 1.5) * 0.7f;

                    // Mute egg / silenced audio (Fork F): MasterVolume == 0 means don't attempt audio.
                    if (masterVolume <= 0f) return;

                    // Use NAudio for async playback
                    Task.Run(() =>
                    {
                        try
                        {
                            using var audioFile = new NAudio.Wave.AudioFileReader(gigglePath);
                            audioFile.Volume = volume;
                            using var outputDevice = new NAudio.Wave.WaveOutEvent();
                            outputDevice.Init(audioFile);
                            outputDevice.Play();
                            while (outputDevice.PlaybackState == NAudio.Wave.PlaybackState.Playing)
                            {
                                System.Threading.Thread.Sleep(50);
                            }
                        }
                        catch { /* Ignore audio errors */ }
                    });
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to play giggle sound: {Error}", ex.Message);
            }
        }

        private void PlayAvatarPopSound()
        {
            try
            {
                var popFiles = new[] { "Pop.mp3", "Pop2.mp3", "Pop3.mp3" };
                var chosenPop = popFiles[_random.Next(popFiles.Length)];
                var popPath = Services.ModResourceResolver.ResolveAudioPath("bubbles/" + chosenPop);

                if (System.IO.File.Exists(popPath))
                {
                    var masterVolume = (App.Settings?.Current?.MasterVolume ?? 100) / 100f;
                    var volume = (float)Math.Pow(masterVolume, 1.5);

                    // Use NAudio for async playback
                    Task.Run(() =>
                    {
                        try
                        {
                            using var audioFile = new NAudio.Wave.AudioFileReader(popPath);
                            audioFile.Volume = volume;
                            using var outputDevice = new NAudio.Wave.WaveOutEvent();
                            outputDevice.Init(audioFile);
                            outputDevice.Play();
                            while (outputDevice.PlaybackState == NAudio.Wave.PlaybackState.Playing)
                            {
                                System.Threading.Thread.Sleep(50);
                            }
                        }
                        catch { /* Ignore audio errors */ }
                    });
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to play avatar pop sound: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Triggered when user clicks avatar 50 times within 1 minute - plays audio and shows trigger
        /// </summary>
        private void TriggerBambiCumAndCollapse()
        {
            App.Logger?.Information("Bambi Cum and Collapse triggered! (50 clicks in 1 minute)");

            // Play the "cum and collapse" audio
            try
            {
                var soundsPath = Services.CompanionPhraseService.VoiceLineFolder;
                var collapseFiles = new[] { "come and coll.mp3", "come and coll (1).mp3", "come and coll (2).mp3" };
                var chosenFile = collapseFiles[_random.Next(collapseFiles.Length)];
                var audioPath = System.IO.Path.Combine(soundsPath, chosenFile);

                if (System.IO.File.Exists(audioPath))
                {
                    var masterVolume = (App.Settings?.Current?.MasterVolume ?? 100) / 100f;
                    var volume = (float)Math.Pow(masterVolume, 1.5);

                    Task.Run(() =>
                    {
                        try
                        {
                            using var audioFile = new NAudio.Wave.AudioFileReader(audioPath);
                            audioFile.Volume = volume;
                            using var outputDevice = new NAudio.Wave.WaveOutEvent();
                            outputDevice.Init(audioFile);
                            outputDevice.Play();
                            while (outputDevice.PlaybackState == NAudio.Wave.PlaybackState.Playing)
                            {
                                System.Threading.Thread.Sleep(50);
                            }
                        }
                        catch { /* Ignore audio errors */ }
                    });
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to play Bambi Cum and Collapse audio: {Error}", ex.Message);
            }

            // Show the trigger message with priority
            GigglePriority("BAMBI CUM AND COLLAPSE", aiGenerated: false);
        }

        private void OnVideoAboutToStart(object? sender, EventArgs e)
        {
            const string line = "Ooh! Pretty spir-rals...";
            Giggle(line, Services.CompanionPhraseService.ResolveEventAudio(line));
        }

        private async void OnVideoEnded(object? sender, EventArgs e)
        {
            // After video ends, restore z-order so both windows come back together
            if (_isAttached)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (_isAttached && _tubeHandle != IntPtr.Zero)
                        {
                            BringAttachedPairToFront();
                        }
                    }
                    catch { /* Window may be closing */ }
                }), DispatcherPriority.Background);
            }

            if (App.Settings?.Current?.AiChatEnabled == true && App.Ai?.IsAvailable == true)
            {
                var title = App.Video?.LastVideoTitle;
                if (string.IsNullOrEmpty(title)) return;

                try
                {
                    var reaction = await App.Ai.GetVideoDoneReaction(title);
                    if (!string.IsNullOrWhiteSpace(reaction))
                        GigglePriority(reaction);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Failed to get AI video-done reaction");
                }
            }
        }

        private void OnGameCompleted(object? sender, EventArgs e)
        {
            Giggle("Good girl! So smart!");
        }

        /// <summary>
        /// Play the AI-generated lock-screen reaction for a completed lock card. Invoked by
        /// BarkService (the sole LockCardCompleted subscriber) on a "heads" coin flip. Returns
        /// true if it actually spoke, so the caller can fall through to a pool bark when the AI
        /// produced nothing — guaranteeing exactly one reaction fires (Fork D).
        /// </summary>
        public async Task<bool> PlayLockCardAiReactionAsync(Services.LockCardCompletedEventArgs e)
        {
            if (App.Settings?.Current?.AiChatEnabled != true || App.Ai?.IsAvailable != true)
                return false;

            try
            {
                var reaction = await App.Ai.GetLockScreenReaction(e.Phrase, e.Mistakes, e.Repeats);
                if (!string.IsNullOrWhiteSpace(reaction))
                {
                    GigglePriority(reaction);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to get AI lock-screen reaction");
                return false;
            }
        }

        /// <summary>
        /// Called just before a flash image is shown - announce it occasionally
        /// </summary>
        private void OnFlashAboutToDisplay(object? sender, EventArgs e)
        {
            _flashCounter++;

            // Skip pre-phrase if flash audio is enabled - the audio filename will be shown instead
            if (App.Settings?.Current?.FlashAudioEnabled == true) return;

            // Only announce ~1 in 4 flashes to avoid being annoying
            if (_flashCounter % 4 == 1)
            {
                GiggleFromCategory("FlashPre");
            }
        }

        /// <summary>
        /// Called when flash audio starts playing - show the audio filename as a speech bubble
        /// </summary>
        private void OnFlashAudioPlaying(object? sender, Services.FlashAudioEventArgs e)
        {
            if (_isMuted || string.IsNullOrWhiteSpace(e.Text)) return;

            // Skip if a bubble is currently showing to avoid overlap
            // (audio will play but text won't show - prevents text/audio desync)
            if (_isGiggling)
            {
                App.Logger?.Debug("Flash audio speech skipped (bubble showing): {Text}", e.Text);
                return;
            }

            // Show the audio filename text as a speech bubble (audio is already playing from FlashService)
            DispatcherHelper.RunOnUI(() =>
            {
                // Double-check in case state changed
                if (_isGiggling) return;

                // Clear the queue - flash audio text takes priority
                _speechQueue.Clear();
                _speechDelayTimer?.Stop();

                // Show immediately WITHOUT playing sound (FlashService already plays the audio)
                ShowGiggle(e.Text, playSound: false, source: SpeechSource.Preset);

                App.Logger?.Debug("Flash audio speech: {Text}", e.Text);
            });
        }

        /// <summary>
        /// Called after each subliminal is displayed - acknowledge occasionally
        /// </summary>
        private void OnSubliminalDisplayed(object? sender, EventArgs e)
        {
            _subliminalCounter++;

            // Only acknowledge ~1 in 10 subliminals
            if (_subliminalCounter % 10 == 0)
            {
                GiggleFromCategory("SubliminalAck");
            }
        }

        private int _bubblePopCounter = 0;

        /// <summary>
        /// Called when user pops a bubble - acknowledge occasionally
        /// </summary>
        private void OnBubblePopped()
        {
            _bubblePopCounter++;

            // Only acknowledge ~1 in 5 bubble pops
            if (_bubblePopCounter % 5 == 0)
            {
                GiggleFromCategory("BubblePop");
            }
        }

        // GameFailed, BubbleMissed, FlashClicked, LevelUp, MindWipe, BrainDrain
        // phrases provided by App.Mods (ModService)

        // Counters for MindWipe/BrainDrain (not too often)
        private int _mindWipeCounter = 0;
        private int _brainDrainCounter = 0;

        private void OnGameFailed(object? sender, EventArgs e)
        {
            GiggleFromCategory("GameFailed");
        }

        private void OnBubbleMissed()
        {
            // Only react occasionally to avoid spam
            if (_random.Next(3) == 0)
            {
                GiggleFromCategory("BubbleMissed");
            }
        }

        private void OnFlashClicked(object? sender, EventArgs e)
        {
            // React to 1 in 3 flash clicks
            if (_random.Next(3) == 0)
            {
                GiggleFromCategory("FlashClicked");
            }
        }

        private void OnAchievementUnlocked(object? sender, Achievement achievement)
        {
            GigglePriority($"Achievement unlocked: {achievement.Name}! *giggles*", aiGenerated: false);
        }

        private void OnLevelUp(object? sender, int newLevel)
        {
            // Use regular Giggle instead of GigglePriority to avoid cutting off current speech
            // Level up is exciting but shouldn't interrupt active triggers/speech
            GiggleFromCategory("LevelUp");
        }

        /// <summary>
        /// React to companion level up (v5.3).
        /// </summary>
        private void OnCompanionLevelUp(object? sender, (Models.CompanionId Companion, int NewLevel) args)
        {
            RefreshCompanionDisplay();

            // Special level-up phrases based on companion. Route the roster name through
            // the active mod's terminology map so a themed mod (e.g. Circe's Lock) speaks
            // its own name instead of the Bambi roster name like "Synthetic Blowdoll"
            // (#325 — BUG-GLMA287TET). No-op for mod-agnostic modes.
            var rawCompanionName = Models.CompanionDefinition.GetById(args.Companion).Name;
            var companionName = App.Mods?.MakeModAware(rawCompanionName) ?? rawCompanionName;
            if (args.NewLevel == Models.CompanionProgress.MaxLevel)
            {
                GigglePriority($"{companionName} reached MAX LEVEL! *sparkles*", aiGenerated: false);
            }
            else if (args.NewLevel % 10 == 0)
            {
                GigglePriority($"{companionName} is now level {args.NewLevel}! Keep going!", aiGenerated: false);
            }
            else
            {
                // Regular level up - use standard phrases
                GiggleFromCategory("LevelUp");
            }
        }

        /// <summary>
        /// React to companion switch (v5.3).
        /// </summary>
        private System.Windows.Threading.DispatcherTimer? _companionGreetingDebounce;

        private void OnCompanionSwitched(object? sender, Models.CompanionId newCompanion)
        {
            RefreshCompanionDisplay();

            // Clear any queued speech so rapid cycling doesn't stack up greetings
            _speechQueue.Clear();
            _speechTimer?.Stop();
            _speechDelayTimer?.Stop();
            _isGiggling = false;

            // Debounce: delay greeting so only the final companion in a rapid cycle gets one
            _companionGreetingDebounce?.Stop();
            _companionGreetingDebounce = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(600)
            };
            _companionGreetingDebounce.Tick += (_, _) =>
            {
                _companionGreetingDebounce.Stop();
                var companionName = Models.CompanionDefinition.GetById(newCompanion).Name;
                companionName = App.Mods?.MakeModAware(companionName) ?? companionName;
                var greeting = $"Hi! {companionName} is here now~";
                Giggle(App.Mods?.MakeModAware(greeting) ?? greeting);
            };
            _companionGreetingDebounce.Start();
        }

        /// <summary>
        /// React to MindWipe audio - not too often (1 in 6)
        /// </summary>
        private void OnMindWipeTriggered(object? sender, EventArgs e)
        {
            _mindWipeCounter++;

            // Only react ~1 in 6 times to avoid being annoying
            if (_mindWipeCounter % 6 == 0)
            {
                GiggleFromCategory("MindWipe");
            }
        }

        /// <summary>
        /// React to BrainDrain audio - not too often (1 in 6)
        /// </summary>
        private void OnBrainDrainTriggered(object? sender, EventArgs e)
        {
            _brainDrainCounter++;

            // Only react ~1 in 6 times to avoid being annoying
            if (_brainDrainCounter % 6 == 0)
            {
                GiggleFromCategory("BrainDrain");
            }
        }

        /// <summary>
        /// Show a greeting when the app starts
        /// </summary>
        // Ensures the warm welcome-back greeting fires at most once per launch, even if the
        // avatar window is re-created (e.g. attach/detach). Process-lifetime.
        private static bool _absenceGreetingShownThisLaunch = false;

        private void ShowGreeting()
        {
            // Subsequent avatar re-creations within the same launch keep the original
            // startup-greeting behavior; only the first one gets the absence-aware welcome.
            if (_absenceGreetingShownThisLaunch)
            {
                GiggleFromCategory("StartupGreeting");
                return;
            }
            _absenceGreetingShownThisLaunch = true;

            var settings = App.Settings?.Current;

            // Snapshot the previous local "last seen" timestamp BEFORE refreshing it.
            DateTime? lastSeen = settings?.LastSeenUtc;

            // Refresh last-seen on app open (local only). We write on open rather than on
            // close so the value is self-contained to this method and survives crashes /
            // force-kills; the tiny imprecision (open-to-open vs. activity-to-open) is fine
            // for a warm greeting. suppressCloudBackup keeps this purely on-device — the
            // timestamp is never added to any server call, sync payload, or telemetry. (It is
            // also listed in ProfileSyncService.ExcludedBackupProperties as defense in depth.)
            if (settings != null)
            {
                settings.LastSeenUtc = DateTime.UtcNow;
                App.Settings?.Save(suppressCloudBackup: true);
            }

            // Voiced, time-aware welcome via the bark system: the greeting bark shows its line in
            // the bubble AND plays audio (per-mod flavored, no-repeat). We pass an away-bucket so
            // rules can pick a line matching how long the user's been gone. If no greeting bark
            // fired (bark system disabled, or no matching rule) we fall back to the legacy
            // text-only absence greeting so the bubble is never silent.
            bool spoke = App.Bark?.NotifyAppOpened(GreetingAwayBucket(lastSeen)) ?? false;
            if (!spoke)
            {
                var greeting = BuildAbsenceGreeting(lastSeen);
                if (greeting == null)
                    GiggleFromCategory("StartupGreeting"); // first run, no prior timestamp
                else
                    Giggle(greeting);
            }

            // Celebrate a daily-streak milestone once (queues after the welcome line).
            CheckStreakMilestoneGreeting();
        }

        /// <summary>Daily-login-streak day counts the companion calls out on app open. Ascending.</summary>
        private static readonly int[] StreakMilestoneDays = { 7, 14, 30, 60, 100, 365 };

        /// <summary>
        /// Buckets the time-since-last-seen into a coarse label the AppOpened bark rules key off
        /// (mirrors the thresholds in <see cref="BuildAbsenceGreeting"/>). "first" = no prior
        /// timestamp (first run on this device).
        /// </summary>
        private static string GreetingAwayBucket(DateTime? lastSeen)
        {
            if (lastSeen == null) return "first";
            var elapsed = DateTime.UtcNow - lastSeen.Value;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            if (elapsed < TimeSpan.FromHours(6)) return "soon";
            if (elapsed < TimeSpan.FromHours(18)) return "back";
            if (elapsed < TimeSpan.FromDays(3)) return "while";
            return "long";
        }

        /// <summary>
        /// Fires a one-time voiced celebration the first time the daily login streak reaches a
        /// milestone (7/14/30/60/100/365 days). The latch lives in
        /// <see cref="AppSettings.LastAnnouncedStreakMilestone"/>: we announce only when a higher
        /// milestone is newly reached, and silently reset the latch downward if the streak drops
        /// so re-reaching a milestone announces again. Tone is celebratory only — never loss
        /// pressure (matching the welcome greeting's intent).
        /// </summary>
        private void CheckStreakMilestoneGreeting()
        {
            var settings = App.Settings?.Current;
            if (settings == null || App.Bark == null) return;

            int streak = settings.CurrentStreak;
            int reached = 0;
            foreach (var m in StreakMilestoneDays)
                if (m <= streak) reached = m;

            if (reached == settings.LastAnnouncedStreakMilestone) return;

            bool isNewMilestone = reached > settings.LastAnnouncedStreakMilestone;
            settings.LastAnnouncedStreakMilestone = reached; // also resets the latch on a streak drop
            App.Settings?.Save(suppressCloudBackup: true);

            if (isNewMilestone && reached > 0)
                App.Bark.NotifyStreakMilestone(reached);
        }

        /// <summary>
        /// Builds a warm, in-character welcome-back line varied by how long the user has been
        /// away, reusing the existing display name (<see cref="App.UserDisplayName"/>) if one is
        /// set and a generic line otherwise. Returns null when there is no prior timestamp
        /// (first run) so the caller can fall back to the normal startup greeting.
        ///
        /// Tone is welcoming only — never guilt, anxiety, or streak/loss pressure. A long
        /// absence gets a happy "welcome back", never a reproach.
        /// </summary>
        private string? BuildAbsenceGreeting(DateTime? lastSeen)
        {
            if (lastSeen == null) return null;

            // Guard against clock changes / future timestamps: treat as a quick return.
            var elapsed = DateTime.UtcNow - lastSeen.Value;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;

            string[] templates;
            if (elapsed < TimeSpan.FromHours(6))
            {
                templates = new[]
                {
                    "Back already? Hehe~ 💕",
                    "Ooh, you're back so soon! ✨",
                    "Couldn't stay away, {name}? 😘",
                };
            }
            else if (elapsed < TimeSpan.FromHours(18))
            {
                templates = new[]
                {
                    "Welcome back, {name}! 💖",
                    "Yay, you're here again! ✨",
                    "Hi again, cutie~ 💕",
                };
            }
            else if (elapsed < TimeSpan.FromDays(3))
            {
                templates = new[]
                {
                    "There you are! So good to see you~ 💕",
                    "You're back, {name}! ✨",
                    "Hehe, hi again! 💖",
                };
            }
            else if (elapsed < TimeSpan.FromDays(7))
            {
                templates = new[]
                {
                    "Yay, you came back! 💕",
                    "So happy you're here, {name}! ✨",
                    "Hi hi! Let's have some fun~ 💖",
                };
            }
            else if (elapsed < TimeSpan.FromDays(30))
            {
                templates = new[]
                {
                    "Look who's back! 💕",
                    "So good to see you again, {name}! ✨",
                    "Welcome back, gorgeous~ 💖",
                };
            }
            else
            {
                templates = new[]
                {
                    "It's been ages — welcome back! 💕✨",
                    "Yaaay, you're back, {name}! So happy to see you! 💖",
                    "Welcome back! Let's pick up right where we left off~ ✨",
                };
            }

            var template = templates[_random.Next(templates.Length)];
            return FormatGreetingName(template, App.UserDisplayName);
        }

        /// <summary>
        /// Substitutes the user's name into a greeting template. If no name is available the
        /// "{name}" token (and a leading ", "/" " separator, if any) is removed cleanly so the
        /// generic line still reads naturally.
        /// </summary>
        private static string FormatGreetingName(string template, string? name)
        {
            if (!string.IsNullOrWhiteSpace(name))
                return template.Replace("{name}", name.Trim());

            return template
                .Replace(", {name}", "")
                .Replace(" {name}", "")
                .Replace("{name}", "")
                .Replace("  ", " ")
                .Trim();
        }

        /// <summary>
        /// React when the engine stops
        /// </summary>
        private void OnEngineStopped(object? sender, EventArgs e)
        {
            GiggleFromCategory("EngineStop");
        }

        private void ToggleInputPanel()
        {
            _isInputVisible = !_isInputVisible;
            InputPanel.Visibility = _isInputVisible ? Visibility.Visible : Visibility.Collapsed;

            if (_isInputVisible)
            {
                FocusInputAfterLayout();
            }
        }

        private void ShowInputPanel()
        {
            _isInputVisible = true;
            InputPanel.Visibility = Visibility.Visible;
            FocusInputAfterLayout();
        }

        /// <summary>
        /// Public entry point for opening the avatar chat input (used by Ctrl+T keybindings
        /// on this window and on MainWindow). Marshals to the UI thread because the
        /// keybinding handler may run from MainWindow's dispatcher; the avatar window
        /// could be on a different one if it's been reparented.
        /// </summary>
        public void OpenChatInput()
        {
            if (Dispatcher.CheckAccess()) ShowInputPanel();
            else Dispatcher.BeginInvoke(new Action(ShowInputPanel));
        }

        /// <summary>
        /// Routed command bound to Ctrl+T on this window (and on MainWindow via
        /// App.AvatarWindow?.OpenChatInput()). Opens the chat input panel.
        /// </summary>
        public static readonly RoutedUICommand OpenChatCommand =
            new RoutedUICommand("Open Avatar Chat", "OpenChat", typeof(AvatarTubeWindow));

        private void OpenChatCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            OpenChatInput();
        }

        /// <summary>
        /// Rebuilds the chat-shortcut KeyBinding on a window from the user's setting.
        /// Removes any prior binding bound to <see cref="OpenChatCommand"/> first so
        /// repeated calls don't stack duplicates. Safe to call from any thread; falls
        /// back to defaults if the setting is empty or unparseable.
        /// </summary>
        public static void ApplyChatShortcutTo(Window window)
        {
            if (window == null) return;

            var s = App.Settings?.Current?.CompanionPrompt;
            var keyName = string.IsNullOrWhiteSpace(s?.ChatShortcutKey) ? "T" : s!.ChatShortcutKey;
            var modsName = s?.ChatShortcutModifiers ?? "Control";

            if (!Enum.TryParse<Key>(keyName, ignoreCase: true, out var key)) key = Key.T;
            if (!TryParseModifiers(modsName, out var mods)) mods = ModifierKeys.Control;

            // Remove any existing chat-shortcut bindings.
            for (int i = window.InputBindings.Count - 1; i >= 0; i--)
            {
                if (window.InputBindings[i] is KeyBinding kb && kb.Command == OpenChatCommand)
                    window.InputBindings.RemoveAt(i);
            }

            // KeyGesture rejects letter keys without Ctrl/Alt (e.g. Shift+T alone)
            // and a handful of other unusual combos. Fall back to Ctrl+T rather
            // than crashing the click handler.
            try
            {
                window.InputBindings.Add(new KeyBinding(OpenChatCommand, key, mods));
            }
            catch (NotSupportedException)
            {
                App.Logger?.Warning("ApplyChatShortcutTo: rejected combo {Mods}+{Key}, falling back to Ctrl+T", mods, key);
                try
                {
                    window.InputBindings.Add(new KeyBinding(OpenChatCommand, Key.T, ModifierKeys.Control));
                }
                catch { }
            }
        }

        /// <summary>"Ctrl+T" / "Alt+Shift+B" — for the hero card button label.</summary>
        public static string FormatChatShortcut()
        {
            var s = App.Settings?.Current?.CompanionPrompt;
            var keyName = string.IsNullOrWhiteSpace(s?.ChatShortcutKey) ? "T" : s!.ChatShortcutKey;
            var modsName = s?.ChatShortcutModifiers ?? "Control";

            if (!Enum.TryParse<Key>(keyName, ignoreCase: true, out var key)) key = Key.T;
            if (!TryParseModifiers(modsName, out var mods)) mods = ModifierKeys.Control;

            var parts = new List<string>();
            if ((mods & ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((mods & ModifierKeys.Alt) != 0) parts.Add("Alt");
            if ((mods & ModifierKeys.Shift) != 0) parts.Add("Shift");
            if ((mods & ModifierKeys.Windows) != 0) parts.Add("Win");
            parts.Add(key.ToString());
            return string.Join("+", parts);
        }

        private static bool TryParseModifiers(string s, out ModifierKeys result)
        {
            result = ModifierKeys.None;
            if (string.IsNullOrWhiteSpace(s)) return true;
            foreach (var part in s.Split(new[] { ',', '+', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (Enum.TryParse<ModifierKeys>(part, ignoreCase: true, out var mk))
                    result |= mk;
                else
                    return false;
            }
            return true;
        }

        public static string SerializeModifiers(ModifierKeys m)
        {
            if (m == ModifierKeys.None) return "";
            var parts = new List<string>();
            if ((m & ModifierKeys.Control) != 0) parts.Add("Control");
            if ((m & ModifierKeys.Alt) != 0) parts.Add("Alt");
            if ((m & ModifierKeys.Shift) != 0) parts.Add("Shift");
            if ((m & ModifierKeys.Windows) != 0) parts.Add("Windows");
            return string.Join(",", parts);
        }

        /// <summary>
        /// Reliably moves keyboard focus into the chat input. The avatar tube is a
        /// transparent, borderless WS_EX_TOOLWINDOW — Windows' focus-stealing prevention
        /// silently rejects <see cref="Window.Activate"/> in that configuration, so we
        /// bypass it via AttachThreadInput before SetForegroundWindow. Then the focus
        /// calls are deferred to Input priority so the panel is fully laid out by the
        /// time we try to put the cursor in the textbox.
        /// </summary>
        private void FocusInputAfterLayout()
        {
            // ContextIdle runs after all pending input events (mouse-up from a
            // double-click, etc.) have been processed. Using the higher Input priority
            // raced with the second click's mouse-up and intermittently lost focus.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    ForceForegroundWindow();
                    TxtUserInput.Focus();
                    Keyboard.Focus(TxtUserInput);
                    TxtUserInput.SelectAll();
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("AvatarTube: focus chat input failed: {Error}", ex.Message);
                }
            }), DispatcherPriority.ContextIdle);
        }

        /// <summary>
        /// Forces this window to the foreground regardless of focus-stealing prevention,
        /// using the AttachThreadInput technique. Required for tool windows
        /// (WS_EX_TOOLWINDOW) which Windows otherwise refuses to bring forward when
        /// requested by an app that doesn't currently own the foreground.
        /// </summary>
        private void ForceForegroundWindow()
        {
            // First try the WPF-friendly path. On the rare occasion it succeeds we save
            // the Win32 round trip; when it fails it's a no-op and we fall through.
            try { Activate(); } catch { }

            var hWnd = _tubeHandle != IntPtr.Zero ? _tubeHandle : new WindowInteropHelper(this).Handle;
            if (hWnd == IntPtr.Zero) return;

            var fg = GetForegroundWindow();
            if (fg == hWnd) return;

            uint fgThread = GetWindowThreadProcessId(fg, out _);
            uint myThread = GetCurrentThreadId();

            if (fgThread == 0 || fgThread == myThread)
            {
                SetForegroundWindow(hWnd);
                return;
            }

            // Briefly share input state with the foreground thread so SetForegroundWindow
            // is allowed through. Always detach in finally — leaving threads attached
            // wedges keyboard input across the whole desktop.
            bool attached = false;
            try
            {
                attached = AttachThreadInput(myThread, fgThread, true);
                SetForegroundWindow(hWnd);
            }
            finally
            {
                if (attached)
                {
                    try { AttachThreadInput(myThread, fgThread, false); } catch { }
                }
            }
        }

        private void TxtUserInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _ = SendChatMessageAsync();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                ToggleInputPanel();
                e.Handled = true;
            }
        }

        private void BtnSendChat_Click(object sender, RoutedEventArgs e)
        {
            _ = SendChatMessageAsync();
        }

        // Default thinking phrases (used when no mod overrides)
        private static readonly string[] DefaultThinkingPhrases = new[]
        {
            "*POP*",
            "*Poppin bubbles...*",
            "*giggles*",
            "*blink blink*",
            "*~*",
            "*teehee*"
        };

        private string GetRandomThinkingPhrase()
        {
            var modPhrases = App.Mods?.GetPhrases("Thinking");
            var phrases = modPhrases != null && modPhrases.Length > 0 ? modPhrases : DefaultThinkingPhrases;
            return phrases[_random.Next(phrases.Length)];
        }

        // Matches a run of trailing dots/ellipsis at the end of the phrase, even when
        // tucked just inside closing wrapper chars () [] * _ ~ / whitespace. Removing
        // it (while leaving the wrappers themselves in place) is what stops the static
        // dots in phrases like "(thinking...)" or "[PROCESSING...]" from doubling up
        // with the thinking animation's own dots.
        private static readonly Regex TrailingDotsInsideWrappersRegex =
            new Regex(@"[.…]+(?=[)\]\s*_~]*$)", RegexOptions.Compiled);

        // Strips dots, ellipsis, whitespace, AND markdown emphasis chars (* _ ~) from
        // both ends of a thinking phrase so the animation's dots aren't duplicated and
        // wrapping characters like "*Poppin bubbles...*" don't render asterisks around
        // the animated dots. Also strips a trailing dot-run sitting just inside () or []
        // wrappers (e.g. "(thinking...)" -> "(thinking)") while preserving those brackets,
        // since paren/bracket-wrapped phrases would otherwise keep their static dots.
        // Trims both ends because phrases come pre-wrapped in *...*.
        private static string StripTrailingDots(string phrase)
        {
            if (string.IsNullOrEmpty(phrase)) return phrase;
            // Drop the trailing dot-run first (handles dots tucked inside ) or ] so the
            // brackets survive), then trim the outer wrapper decoration as before.
            var withoutDots = TrailingDotsInsideWrappersRegex.Replace(phrase, "");
            var trimmed = withoutDots.Trim('.', '…', '*', '_', '~', ' ', '\t');
            // If the phrase was nothing but decoration (e.g. "*~*"), keep the original
            // so we don't end up animating just bare dots.
            return string.IsNullOrEmpty(trimmed) ? phrase : trimmed;
        }

        // ============================================================
        // THINKING ANIMATION (rotating phrases + animated dots)
        // ============================================================

        /// <summary>
        /// Begins the "still thinking" animation in the speech bubble. Picks a phrase,
        /// shows it via ShowGiggle (so the bubble is visible/sized/z-ordered), then
        /// rotates phrase + dots every 500ms until <see cref="StopThinkingAnimation"/>
        /// is called. The dismiss timer that ShowGiggle starts is cancelled — the
        /// bubble stays visible until the AI reply lands.
        /// </summary>
        private void StartThinkingAnimation()
        {
            StopThinkingAnimation(); // clear any prior animation

            _isWaitingForAi = true;
            _thinkingPhraseBase = StripTrailingDots(GetRandomThinkingPhrase());
            _thinkingTickCount = 0;
            var generation = ++_thinkingGeneration;

            // Use ShowGiggle for first frame so bubble is sized + visible. playSound:false
            // because the AI reply will play its own sound when it arrives.
            ShowGiggle(_thinkingPhraseBase, playSound: false, source: SpeechSource.AI);

            // Cancel auto-dismiss — the bubble stays up until the reply pre-empts it.
            _speechTimer?.Stop();

            _thinkingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _thinkingTimer.Tick += (s, e) =>
            {
                if (generation != _thinkingGeneration)
                {
                    (s as DispatcherTimer)?.Stop();
                    return;
                }
                ThinkingTick();
            };
            _thinkingTimer.Start();
        }

        /// <summary>
        /// Cancels any in-flight thinking animation. Safe to call repeatedly.
        /// Bumping the generation counter causes any pending tick callbacks to bail.
        /// </summary>
        private void StopThinkingAnimation()
        {
            _thinkingGeneration++;
            _thinkingTimer?.Stop();
            _thinkingTimer = null;
        }

        private void ThinkingTick()
        {
            _thinkingTickCount++;
            if (_thinkingTickCount > 3)
            {
                // Cycle complete — pick a new phrase, restart dots.
                _thinkingPhraseBase = StripTrailingDots(GetRandomThinkingPhrase());
                _thinkingTickCount = 0;
                RenderSpeechBubbleRaw(_thinkingPhraseBase);
            }
            else
            {
                var dots = new string('.', _thinkingTickCount);
                RenderSpeechBubbleRaw(_thinkingPhraseBase + dots);
            }
        }

        /// <summary>
        /// Paints a single string into the speech bubble without going through
        /// PopulateSpeechBubble's hyperlink/markdown processing. Used by the thinking
        /// animation and the typewriter (which re-renders with full processing on
        /// completion).
        /// </summary>
        private void RenderSpeechBubbleRaw(string text)
        {
            TxtSpeech.Inlines.Clear();
            if (!string.IsNullOrEmpty(text))
            {
                TxtSpeech.Inlines.Add(new Run(text));
            }
        }

        // ============================================================
        // TYPEWRITER EFFECT (AI replies stream in char-by-char)
        // ============================================================

        // Tunable: ~18ms/char default, but auto-speed-up so any reply finishes within budget.
        private const int TypewriterMinStepMs = 8;
        private const int TypewriterMaxStepMs = 30;
        private const int TypewriterTotalBudgetMs = 2000;

        // Slower profile for non-AI bubbles (barks, presets, triggers) — deliberately more leisurely
        // than AI replies so short lines still read as "typed out".
        private const int TypewriterSlowMinStepMs = 24;
        private const int TypewriterSlowMaxStepMs = 75;
        private const int TypewriterSlowTotalBudgetMs = 7000;

        private static (int min, int max, int budget) TypewriterProfile(bool slow) => slow
            ? (TypewriterSlowMinStepMs, TypewriterSlowMaxStepMs, TypewriterSlowTotalBudgetMs)
            : (TypewriterMinStepMs, TypewriterMaxStepMs, TypewriterTotalBudgetMs);

        /// <summary>
        /// Types <paramref name="fullText"/> into the speech bubble character by character.
        /// On completion, calls <see cref="PopulateSpeechBubble"/> for the final pass so
        /// video-name hyperlinks and markdown links become clickable.
        /// </summary>
        /// <summary>
        /// Estimates how long the typewriter will take for a given text length, mirroring
        /// the math in <see cref="StartTypewriter"/> and <see cref="TypewriterTick"/>.
        /// Used by <see cref="ShowGiggle"/> to compensate the bubble timer so the slider
        /// value reflects readable time rather than bubble-open time.
        /// </summary>
        private static int EstimateTypewriterDurationMs(int length, bool slow = false)
        {
            if (length <= 0) return 0;
            var (min, max, budget) = TypewriterProfile(slow);
            int charsPerTick = Math.Max(1, length / 100);
            int stepMs = Math.Min(max, Math.Max(min, budget / Math.Max(1, length)));
            int ticks = (int)Math.Ceiling((double)length / charsPerTick);
            return stepMs * ticks;
        }

        private void StartTypewriter(string fullText, bool slow = false)
        {
            StopTypewriter();

            _typewriterFullText = fullText ?? string.Empty;
            _typewriterIndex = 0;
            var generation = ++_typewriterGeneration;

            // Strip markdown links the same way PopulateSpeechBubble does, so the typewriter
            // shows the same plain text the final render will. Otherwise the bubble would
            // briefly show "[Naughty Bambi](https://...)" before snapping to "Naughty Bambi".
            _typewriterFullText = MarkdownLinkRegex.Replace(_typewriterFullText, "$1");

            // Render starts empty.
            RenderSpeechBubbleRaw(string.Empty);

            if (_typewriterFullText.Length == 0)
            {
                // Nothing to type — fall through to the final render so links get processed.
                PopulateSpeechBubble(fullText);
                return;
            }

            var (min, max, budget) = TypewriterProfile(slow);
            var stepMs = Math.Min(max, Math.Max(min, budget / Math.Max(1, _typewriterFullText.Length)));

            _typewriterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(stepMs) };
            _typewriterTimer.Tick += (s, e) =>
            {
                if (generation != _typewriterGeneration)
                {
                    (s as DispatcherTimer)?.Stop();
                    return;
                }
                TypewriterTick(fullText);
            };
            _typewriterTimer.Start();
        }

        private void StopTypewriter()
        {
            _typewriterGeneration++;
            _typewriterTimer?.Stop();
            _typewriterTimer = null;
        }

        private void TypewriterTick(string originalFullText)
        {
            // Type 1-2 chars per tick depending on length so very long replies don't drag.
            // (stepMs is already auto-scaled, but we can also batch chars per tick.)
            var charsThisTick = Math.Max(1, _typewriterFullText.Length / 100);

            for (int i = 0; i < charsThisTick && _typewriterIndex < _typewriterFullText.Length; i++)
            {
                _typewriterIndex++;
            }

            var partial = _typewriterFullText.Substring(0, _typewriterIndex);
            RenderSpeechBubbleRaw(partial);

            if (_typewriterIndex >= _typewriterFullText.Length)
            {
                _typewriterTimer?.Stop();
                _typewriterTimer = null;
                // Final pass: re-render with the original (un-stripped) text so PopulateSpeechBubble
                // can wire up clickable video links and URL hyperlinks.
                PopulateSpeechBubble(originalFullText);
            }
        }

        /// <summary>
        /// Truncates text to a maximum number of words, adding "..." if truncated
        /// </summary>
        private static string TruncateToWords(string text, int maxWords)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var words = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length <= maxWords) return text;

            return string.Join(" ", words.Take(maxWords)) + "...";
        }

        private async Task SendChatMessageAsync()
        {
            var input = TxtUserInput.Text?.Trim();
            if (string.IsNullOrEmpty(input)) return;

            // P1.4 — chat cooldown gate. When the moderation counter is in cooldown
            // we swallow the send silently (no new bubble, no AI call). The countdown
            // text is already rendered into the input box; user can see why.
            var counterState = App.ModerationCounter?.GetState();
            if (counterState?.CooldownActive == true)
            {
                App.Logger?.Information("AvatarTubeWindow: chat send swallowed (cooldown active, ends={End})",
                    counterState.CooldownEndsAt);
                return;
            }

            TxtUserInput.Text = "";
            ToggleInputPanel();

            // EMIT hook for GamificationBridge companion-chat achievements. Fired once
            // per genuine user send (past the cooldown gate, non-empty input), before
            // the moderation/AI path so it counts the attempt regardless of outcome.
            App.Companion?.NotifyUserMessageSent();

            // P2/H5: user input is NOT added to chat history yet. If the moderation
            // guard refuses below we throw the input away — the prohibited text must
            // not remain visible in the in-memory history view. AddToChatHistory is
            // called only after the AI call returns with a non-refusal result.

            if (App.Settings?.Current?.AiChatEnabled == true && App.Ai != null && App.Ai.IsAvailable)
            {
                try
                {
                    // Animated thinking bubble: rotates phrases + dots while we wait.
                    // Sets _isWaitingForAi internally so other giggles don't interrupt.
                    StartThinkingAnimation();

                    // P2/C4: typed result. IsAiGenerated tells us whether the pink "AI"
                    // badge should appear (true only for a genuine LLM reply; cloud
                    // fallback / offline / login-required / local-Ollama-down all return
                    // IsAiGenerated=false so the bubble appears unbadged).
                    var result = await App.Ai.GetBambiReplyExAsync(input);

                    if (result.Refusal != null)
                    {
                        // Refused — render the POLICY badge + the localized refusal string
                        // instead of the normal AI bubble. The user's prohibited input is
                        // dropped without ever entering chat history (P2/H5). The textbox
                        // was already cleared above, so there's nothing left for the user
                        // to re-send by accident.
                        PlayDoubleBounce();
                        ShowModerationRefusalBubble(result.Refusal.Source);
                    }
                    else
                    {
                        // Allowed — NOW persist the user prompt to chat history (P2/H5).
                        AddToChatHistory(input, isUser: true);

                        // Double bounce to attract attention, then show AI response.
                        // aiGenerated flag flows through so canned fallbacks don't wear
                        // the AI badge (P2/C4 — audit smoke-test #1).
                        PlayDoubleBounce();
                        GigglePriority(result.Text, aiGenerated: result.IsAiGenerated);
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Failed to get AI reply");
                    // Exception is NOT a moderation refusal — the user's input was a
                    // legitimate send that failed for an infrastructure reason. Persist
                    // it so the conversation transcript stays coherent.
                    AddToChatHistory(input, isUser: true);
                    GigglePriority(GetRandomBambiPhrase(), aiGenerated: false); // Clears _isWaitingForAi
                }
            }
            else
            {
                // No AI configured / disabled — still a legitimate send, persist the input
                // and respond with a preset phrase (no AI badge, no moderation in this path).
                AddToChatHistory(input, isUser: true);
                Giggle(GetRandomBambiPhrase());
            }
        }

        /// <summary>
        /// True when the active mod overrides tube.png but not tube2.png. In that case
        /// the detached state would otherwise mix the mod's avatar with the embedded
        /// default tube2.png — leaving the avatar floating outside the mod's chamber.
        /// We treat this as "use the mod's tube.png and the attached layout" so the
        /// avatar lands inside the chamber the mod author actually drew.
        /// </summary>
        private static bool ModOverridesAttachedTubeOnly()
        {
            return Services.ModResourceResolver.HasModOverride("tube.png")
                && !Services.ModResourceResolver.HasModOverride("tube2.png");
        }

        /// <summary>
        /// Switch between tube.png and tube2.png
        /// </summary>
        public void SetTubeStyle(bool useAlternative)
        {
            try
            {
                // If the active mod only ships a tube.png override, use it in both states
                // so the chamber stays consistent with the mod's art.
                if (useAlternative && ModOverridesAttachedTubeOnly())
                    useAlternative = false;

                var tubeName = useAlternative ? "tube2.png" : "tube.png";
                ImgTubeFrame.Source = Services.ModResourceResolver.ResolveImage(tubeName);
                App.Logger?.Information("Tube style changed to: {Style}", tubeName);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to change tube style");
            }
        }

        // ============================================================
        // DETACH/ATTACH FUNCTIONALITY
        // ============================================================

        /// <summary>
        /// Gets whether the avatar tube is currently detached (floating independently)
        /// </summary>
        public bool IsDetached => !_isAttached;

        /// <summary>
        /// Gets whether the avatar is currently visible on screen.
        /// Returns false if attached and main window is minimized or not visible.
        /// Returns true if detached (independent widget window).
        /// </summary>
        private bool IsAvatarVisibleOnScreen
        {
            get
            {
                // If avatar is disabled, it's never visible
                if (App.Settings?.Current?.AvatarEnabled != true)
                    return false;

                // Detached mode - avatar is always visible as independent widget
                if (!_isAttached)
                    return true;

                // Attached mode - check parent window visibility
                if (_parentWindow == null)
                    return false;

                // Hidden for fullscreen app
                if (_hiddenForFullscreen)
                    return false;

                // Check window state
                return _parentWindow.IsVisible && _parentWindow.WindowState != WindowState.Minimized;
            }
        }

        /// <summary>
        /// Toggles between attached and detached states
        /// </summary>
        public void ToggleDetached()
        {
            if (_isAttached)
            {
                Detach();
            }
            else
            {
                Attach();
            }
        }

        /// <summary>
        /// Detach the avatar tube from the main window, making it a free-floating draggable widget.
        /// <paramref name="silent"/> suppresses the "I'm free!" giggle for automatic detaches (e.g. the
        /// auto-detach when a chaos run starts), where a spoken line would be intrusive.
        /// </summary>
        public void Detach(bool silent = false)
        {
            if (!_isAttached) return;

            _isAttached = false;

            // Switch to alternative tube image
            SetTubeStyle(true);

            // Apply tube layout offsets for detached mode
            ApplyTubeLayoutOffsets();

            // Speech bubble stays at same position in both modes (right side of tube, clearly visible)
            if (SpeechBubble.Visibility == Visibility.Visible && !string.IsNullOrEmpty(TxtSpeech.Text))
            {
                AdjustBubbleSize(TxtSpeech.Text);
            }

            // Keep hidden from taskbar and Alt+Tab
            ShowInTaskbar = false;
            SetToolWindowStyle(true);

            // Set topmost - use both WPF property and Win32 for reliability
            Topmost = true;
            ReassertTopmost(); // Use Win32 to ensure topmost is applied immediately

            // Show the move cursor only over the draggable avatar visuals (not the transparent
            // dead-zones — see Window_MouseLeftButtonDown, #346). Window cursor stays default.
            AvatarBorder.Cursor = Cursors.SizeAll;
            SpeechBubble.Cursor = Cursors.SizeAll;
            TitleBox.Cursor = Cursors.SizeAll;
            // Let the whole visible tube vessel be grabbed (not just the avatar art) — it's
            // IsHitTestVisible=False in XAML for attached mode; turn it on while detached.
            ImgTubeFrame.IsHitTestVisible = true;
            ImgTubeFrame.Cursor = Cursors.SizeAll;
            MouseLeftButtonDown += Window_MouseLeftButtonDown;

            // Update context menu visibility
            UpdateContextMenuForState();

            App.Logger?.Information("Avatar tube detached - now floating independently");
            if (!silent) Giggle("I'm free! Ctrl+scroll to resize!");
        }

        /// <summary>
        /// Attach the avatar tube back to the main window. <paramref name="silent"/> suppresses the
        /// "Back home~" giggle for automatic re-attaches (e.g. when a chaos run ends).
        /// </summary>
        public void Attach(bool silent = false)
        {
            if (_isAttached) return;

            _isAttached = true;

            // Switch back to original tube image
            SetTubeStyle(false);

            // Apply tube layout offsets for attached mode
            ApplyTubeLayoutOffsets();

            // Restore speech bubble position when attached
            if (SpeechBubble.Visibility == Visibility.Visible && !string.IsNullOrEmpty(TxtSpeech.Text))
            {
                AdjustBubbleSize(TxtSpeech.Text);
            }

            // Hide from taskbar and Alt+Tab when attached
            ShowInTaskbar = false;

            // No longer topmost when attached
            Topmost = false;

            // Disable dragging
            Cursor = Cursors.Arrow;
            AvatarBorder.Cursor = Cursors.Arrow;
            SpeechBubble.Cursor = Cursors.Arrow;
            TitleBox.Cursor = Cursors.Arrow;
            // Restore the attached-mode tube frame (non-interactive, behind everything).
            ImgTubeFrame.IsHitTestVisible = false;
            ImgTubeFrame.Cursor = Cursors.Arrow;
            MouseLeftButtonDown -= Window_MouseLeftButtonDown;

            // Reset scale BEFORE updating position - otherwise position is calculated
            // using the old scaled dimensions from when it was detached
            _currentScale = 1.0;
            try
            {
                // Reset to base calculated size
                ContentViewbox.Width = DesignWidth * _scaleFactor;
                ContentViewbox.Height = DesignHeight * _scaleFactor;
            }
            catch { }
            UpdateLayout(); // Force layout update so ActualWidth/Height reflect new size

            // Snap back to parent window position
            UpdatePosition();
            BringAttachedPairToFront();

            // Defer the TOOLWINDOW style to ensure it's applied after all window state changes
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SetToolWindowStyle(true);
            }), System.Windows.Threading.DispatcherPriority.Background);

            // Update context menu visibility
            UpdateContextMenuForState();

            App.Logger?.Information("Avatar tube attached - anchored to main window");
            if (!silent) Giggle("Back home~");
        }

        /// <summary>
        /// Handle Ctrl+scroll wheel to resize avatar when detached
        /// </summary>
        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            try
            {
                // Only resize when detached and Ctrl is held
                if (_isAttached || !Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl))
                    return;

                e.Handled = true;

                // Scroll up = bigger, scroll down = smaller
                if (e.Delta > 0)
                    _currentScale = Math.Min(MaxScale, _currentScale + ScaleStep);
                else
                    _currentScale = Math.Max(MinScale, _currentScale - ScaleStep);

                ApplyScale();
                // Clamp position after resize to keep avatar visible
                Dispatcher.BeginInvoke(new Action(ClampAvatarPosition), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Mouse wheel resize error: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Handle Up/Down arrow keys to resize avatar when detached
        /// </summary>
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                // Only resize when detached
                if (_isAttached)
                    return;

                if (e.Key == Key.Up)
                {
                    e.Handled = true;
                    _currentScale = Math.Min(MaxScale, _currentScale + ScaleStep);
                    ApplyScale();
                    // Clamp position after resize to keep avatar visible
                    Dispatcher.BeginInvoke(new Action(ClampAvatarPosition), System.Windows.Threading.DispatcherPriority.Loaded);
                }
                else if (e.Key == Key.Down)
                {
                    e.Handled = true;
                    _currentScale = Math.Max(MinScale, _currentScale - ScaleStep);
                    ApplyScale();
                    // Clamp position after resize to keep avatar visible
                    Dispatcher.BeginInvoke(new Action(ClampAvatarPosition), System.Windows.Threading.DispatcherPriority.Loaded);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Key resize error: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Apply the current scale to the avatar content
        /// </summary>
        private void ApplyScale()
        {
            try
            {
                if (ContentViewbox == null || !IsLoaded) return;

                // Use Width/Height instead of transforms - much safer with animated GIFs
                // Calculate new size based on current scale factor and user scale
                var newWidth = DesignWidth * _scaleFactor * _currentScale;
                var newHeight = DesignHeight * _scaleFactor * _currentScale;

                ContentViewbox.Width = newWidth;
                ContentViewbox.Height = newHeight;
                // Window follows via the ContentViewbox.SizeChanged handler wired in OnLoaded
                // (auto-sizing is off after first paint — see OnFirstContentRendered).
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("ApplyScale error: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Updates context menu items based on attached/detached state
        /// </summary>
        private void UpdateContextMenuForState()
        {
            if (_isAttached)
            {
                // When attached: show Detach, hide Attach, Dismiss, and resize options
                MenuItemDetach.Visibility = Visibility.Visible;
                MenuItemAttach.Visibility = Visibility.Collapsed;
                MenuItemShrink.Visibility = Visibility.Collapsed;
                MenuItemGrow.Visibility = Visibility.Collapsed;
                MenuItemDismiss.Visibility = Visibility.Collapsed;
            }
            else
            {
                // When detached: hide Detach, show Attach, Dismiss, and resize options
                MenuItemDetach.Visibility = Visibility.Collapsed;
                MenuItemAttach.Visibility = Visibility.Visible;
                MenuItemShrink.Visibility = Visibility.Visible;
                MenuItemGrow.Visibility = Visibility.Visible;
                MenuItemDismiss.Visibility = Visibility.Visible;

                // Update resize button states
                UpdateResizeMenuState();
            }
        }

        /// <summary>
        /// Updates the shrink/grow menu items based on current scale
        /// </summary>
        private void UpdateResizeMenuState()
        {
            // Disable shrink at minimum, grow at maximum
            MenuItemShrink.IsEnabled = _currentScale > MinScale;
            MenuItemGrow.IsEnabled = _currentScale < MaxScale;

            // Show current scale percentage
            int scalePercent = (int)(_currentScale * 100);
            MenuItemShrink.Header = _currentScale > MinScale ? Loc.Get("menu_shrink") : Loc.Get("menu_shrink_min");
            MenuItemGrow.Header = _currentScale < MaxScale ? Loc.Get("menu_grow") : Loc.Get("menu_grow_max");

            // Gray out disabled items
            MenuItemShrink.Foreground = MenuItemShrink.IsEnabled
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Colors.Gray);
            MenuItemGrow.Foreground = MenuItemGrow.IsEnabled
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Colors.Gray);
        }

        // Manual drag tracking
        private bool _isDragging = false;
        private Point _dragStartPoint;
        private double _dragStartLeft;
        private double _dragStartTop;

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Allow dragging the window when detached — but only when the click actually lands on a
            // visible part of the avatar (art, speech bubble, or name tag). The window is sized to its
            // content with large transparent margins around the corner-positioned avatar; without this
            // guard those invisible dead-zones to the top/bottom-right were draggable, which felt like
            // a phantom hitbox (#346 — BUG-7KHMJW9CH7).
            if (!_isAttached)
            {
                var hit = e.OriginalSource as DependencyObject;
                bool onAvatar = IsDescendantOf(hit, AvatarBorder)
                                || IsDescendantOf(hit, SpeechBubble)
                                || IsDescendantOf(hit, TitleBox)
                                // The visible tube vessel is draggable too (enabled only when
                                // detached — see Detach). Z-index 0, so it only catches clicks the
                                // avatar/bubble/menu didn't, and the far transparent margins past
                                // the tube image's rect stay non-draggable (#346 dead-zone guard).
                                || IsDescendantOf(hit, ImgTubeFrame);
                if (!onAvatar) return;

                _isDragging = true;
                _dragStartPoint = PointToScreen(e.GetPosition(this));
                _dragStartLeft = Left;
                _dragStartTop = Top;
                CaptureMouse();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_isDragging && !_isAttached)
            {
                var currentPoint = PointToScreen(e.GetPosition(this));
                Left = _dragStartLeft + (currentPoint.X - _dragStartPoint.X);
                Top = _dragStartTop + (currentPoint.Y - _dragStartPoint.Y);
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (_isDragging)
            {
                _isDragging = false;
                ReleaseMouseCapture();
            }
        }

        /// <summary>
        /// Clamps the avatar window position to ensure it stays at least half visible on screen
        /// </summary>
        private void ClampAvatarPosition()
        {
            if (_isAttached) return;

            try
            {
                // Get DPI scale factor for proper coordinate conversion
                var source = PresentationSource.FromVisual(this);
                var dpiScale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

                // Convert WPF coordinates to physical pixels for screen comparison
                var physicalLeft = Left * dpiScale;
                var physicalTop = Top * dpiScale;
                var physicalWidth = ActualWidth * dpiScale;
                var physicalHeight = ActualHeight * dpiScale;

                // Get the screen that contains most of the avatar (using physical pixel coordinates)
                var screen = System.Windows.Forms.Screen.FromPoint(
                    new System.Drawing.Point(
                        (int)(physicalLeft + physicalWidth / 2),
                        (int)(physicalTop + physicalHeight / 2)));

                var bounds = screen.WorkingArea;

                // Calculate how much of the avatar must remain visible in physical pixels
                var minVisibleWidth = physicalWidth / 2;

                // Calculate allowed bounds in physical pixels
                var minPhysicalLeft = bounds.Left - physicalWidth + minVisibleWidth;
                var maxPhysicalLeft = bounds.Right - minVisibleWidth;
                // Allow avatar to go way off the top - practically no limit
                var minPhysicalTop = bounds.Top - physicalHeight - 1000;
                var maxPhysicalTop = bounds.Bottom - (physicalHeight / 2);

                // Clamp position in physical pixels (only clamp left/right, not top)
                var newPhysicalLeft = Math.Max(minPhysicalLeft, Math.Min(maxPhysicalLeft, physicalLeft));
                // Don't clamp top - allow avatar to go anywhere vertically
                var newPhysicalTop = Math.Min(maxPhysicalTop, physicalTop); // Only prevent going off bottom

                // Convert back to WPF units
                var newLeft = newPhysicalLeft / dpiScale;
                var newTop = newPhysicalTop / dpiScale;

                // Only update if position changed to avoid unnecessary redraws
                if (Math.Abs(newLeft - Left) > 1 || Math.Abs(newTop - Top) > 1)
                {
                    Left = newLeft;
                    Top = newTop;
                }
            }
            catch
            {
                // Ignore errors - position clamping is best-effort
            }
        }

        // ============================================================
        // SPEECH BUBBLE MOUSE HANDLERS
        // ============================================================

        private void SpeechBubble_MouseEnter(object sender, MouseEventArgs e)
        {
            // Keep speech bubble open while mouse is over it
            _isMouseOverSpeechBubble = true;
        }

        private void SpeechBubble_MouseLeave(object sender, MouseEventArgs e)
        {
            // Allow speech bubble to close after mouse leaves
            _isMouseOverSpeechBubble = false;
        }

        // ============================================================
        // CONTEXT MENU HANDLERS
        // ============================================================

        private void AvatarContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            // Use Dispatcher to ensure UI updates are processed
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateQuickMenuState();
                UpdateContextMenuForState();
                RefreshEmoteMenuItemsForRemoteState();
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        // When a remote controller is connected, swap the 5 normally-locked items
        // (Engine / TriggerMode / BambiTakeover / Personality / Mute) for the 5
        // emote preset items. Existing items' IsEnabled gating is left untouched —
        // they're just Collapsed in remote mode, so their disabled state doesn't
        // matter. Re-runs on every Opened so preset edits show up immediately.
        private void RefreshEmoteMenuItemsForRemoteState()
        {
            try
            {
                var remoteActive = App.RemoteControl?.ControllerConnected == true;
                var originalVis = remoteActive ? Visibility.Collapsed : Visibility.Visible;
                var emoteVis = remoteActive ? Visibility.Visible : Visibility.Collapsed;

                if (MenuItemEngine != null) MenuItemEngine.Visibility = originalVis;
                if (MenuItemTriggerMode != null) MenuItemTriggerMode.Visibility = originalVis;
                if (MenuItemBambiTakeover != null) MenuItemBambiTakeover.Visibility = originalVis;
                if (MenuItemPersonality != null) MenuItemPersonality.Visibility = originalVis;
                if (MenuItemMute != null) MenuItemMute.Visibility = originalVis;

                var emoteItems = new[] { MenuItemEmote1, MenuItemEmote2, MenuItemEmote3, MenuItemEmote4, MenuItemEmote5 };
                foreach (var mi in emoteItems)
                {
                    if (mi != null) mi.Visibility = emoteVis;
                }

                if (!remoteActive) return;

                var presets = App.Settings?.Current?.RemoteEmotePresets;
                if (presets == null) return;

                for (int i = 0; i < emoteItems.Length && i < presets.Count; i++)
                {
                    var mi = emoteItems[i];
                    if (mi == null) continue;
                    var p = presets[i];
                    var icon = string.IsNullOrEmpty(p.Icon) ? "" : p.Icon + "  ";
                    mi.Header = icon + (p.Text ?? "");
                    mi.Tag = p;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Avatar] Emote menu refresh failed");
            }
        }

        private async void MenuItemEmote_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem mi || mi.Tag is not Models.EmotePreset preset) return;
            if (string.IsNullOrWhiteSpace(preset.Text)) return; // label-less slot — silent no-op
            // Route through MainWindow.SendEmoteAndReportAsync so this surface picks up
            // the centralized avatar speech-bubble feedback (step 3.6). Null status target
            // means inline status text is skipped — appropriate for a context menu that
            // closes on click. Rate-limit / session-ended errors remain silent here.
            if (_parentWindow is MainWindow mw)
            {
                await mw.SendEmoteAndReportAsync(preset.Text, preset.Icon ?? "", "preset", null);
            }
            else if (App.RemoteControl != null)
            {
                // Fallback: parent isn't MainWindow (shouldn't happen, but be defensive).
                await App.RemoteControl.SendEmoteAsync(preset.Text, preset.Icon ?? "", "preset");
            }
        }

        /// <summary>
        /// Shows a speech bubble for emote feedback. isPending=true renders "Sending...";
        /// isPending=false renders Sent: "<text>" (text truncated to 40 chars).
        ///
        /// Behavior:
        ///   - Skips silently if the avatar is currently waiting on / showing an AI bubble
        ///     (don't fight the conversational surface).
        ///   - Otherwise interrupts any in-flight preset speech and shows immediately.
        ///   - Does NOT add to chat history (this is transient remote-emote feedback,
        ///     not a conversational turn).
        ///   - Plays no audio (preset source but suppressed sound).
        ///   - Uses Dispatcher under the hood — safe from any thread but expected to be
        ///     called from the UI thread (SendEmoteAndReportAsync runs on UI thread).
        /// </summary>
        public void ShowEmoteFeedback(string text, bool isPending)
        {
            try
            {
                DispatcherHelper.RunOnUI(() =>
                {
                    // Don't fight the AI bubble or interrupt a pending AI response.
                    if (_isWaitingForAi || _isShowingAiBubble) return;

                    var safe = (text ?? "").Trim();
                    if (safe.Length > 40) safe = safe.Substring(0, 40) + "...";
                    var content = isPending ? "Sending..." : $"Sent: \"{safe}\"";

                    // Clear any in-flight preset speech so the bubble updates instantly.
                    _speechTimer?.Stop();
                    _speechDelayTimer?.Stop();
                    _speechQueue.Clear();
                    _isGiggling = false;

                    ShowGiggle(content, playSound: false, source: SpeechSource.Preset);

                    // Bump the bubble lifetime by 1 second. The default
                    // (App.Settings.BubbleDurationSeconds, ~2s) is tuned for
                    // ambient preset speech but feels too fast for transient
                    // "Sending..." / "Sent: ..." emote feedback. ShowGiggle
                    // has just called _speechTimer.Start() with no elapsed
                    // time, so setting Interval to (current + 1s) cleanly
                    // extends the lifetime by exactly 1 second.
                    if (_speechTimer != null)
                    {
                        _speechTimer.Interval = _speechTimer.Interval + TimeSpan.FromSeconds(1);
                    }
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Avatar] ShowEmoteFeedback failed");
            }
        }

        private void MenuItemDetach_Click(object sender, RoutedEventArgs e)
        {
            Detach();
        }

        private void MenuItemAttach_Click(object sender, RoutedEventArgs e)
        {
            // Show and activate the parent window first
            if (_parentWindow != null)
            {
                _parentWindow.Show();
                _parentWindow.WindowState = WindowState.Normal;
                _parentWindow.Activate();
            }

            Attach();
        }

        private void MenuItemShrink_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isAttached && _currentScale > MinScale)
                {
                    _currentScale = Math.Max(MinScale, _currentScale - ScaleStep);
                    ApplyScale();
                    UpdateResizeMenuState();
                    // Clamp position after resize to keep avatar visible
                    Dispatcher.BeginInvoke(new Action(ClampAvatarPosition), System.Windows.Threading.DispatcherPriority.Loaded);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Menu shrink error: {Error}", ex.Message);
            }
        }

        private void MenuItemGrow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isAttached && _currentScale < MaxScale)
                {
                    _currentScale = Math.Min(MaxScale, _currentScale + ScaleStep);
                    ApplyScale();
                    UpdateResizeMenuState();
                    // Clamp position after resize to keep avatar visible
                    Dispatcher.BeginInvoke(new Action(ClampAvatarPosition), System.Windows.Threading.DispatcherPriority.Loaded);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Menu grow error: {Error}", ex.Message);
            }
        }

        private void MenuItemEngine_Click(object sender, RoutedEventArgs e)
        {
            if (_parentWindow is MainWindow mainWindow)
            {
                // Use Flash.IsRunning as proxy for engine state
                if (App.Flash?.IsRunning == true)
                {
                    mainWindow.StopEngine();
                    Giggle("Engine stopped~");
                }
                else
                {
                    mainWindow.StartEngine();
                    Giggle("Engine started! *giggles*");
                }
                UpdateQuickMenuState();
            }
        }

        private void MenuItemTriggerMode_Click(object sender, RoutedEventArgs e)
        {
            var current = App.Settings?.Current?.TriggerModeEnabled ?? false;
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.TriggerModeEnabled = !current;
                App.Settings.Save();
                RestartTriggerTimer();
                UpdateQuickMenuState();

                // Sync MainWindow UI
                if (_parentWindow is MainWindow mainWindow)
                {
                    mainWindow.SyncTriggerModeUI(!current);
                }

                Giggle(!current ? "Trigger mode ON~" : "Trigger mode off~");
            }
        }

        private void MenuItemBambiTakeover_Click(object sender, RoutedEventArgs e)
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            // Check Patreon requirement
            if (App.Patreon?.HasPremiumAccess != true)
            {
                Giggle("This is Patreon only~");
                return;
            }

            // Auto-grant consent when enabling from avatar menu
            // (user is explicitly choosing to enable, so consent is implied)
            if (!settings.AutonomyConsentGiven)
            {
                settings.AutonomyConsentGiven = true;
            }

            var current = settings.AutonomyModeEnabled;
            settings.AutonomyModeEnabled = !current;
            App.Settings.Save();

            // Start/stop autonomy service
            if (!current)
            {
                App.Autonomy?.Start();
                Giggle(App.Mods?.GetAutonomyOnPhrase() ?? "Bambi takes over~ *giggles*");
            }
            else
            {
                App.Autonomy?.Stop();
                Giggle("Takeover mode off~");
            }

            // Sync main window checkbox
            App.Logger?.Information("AvatarTubeWindow: Syncing checkbox, _parentWindow type={Type}, !current={NewValue}",
                _parentWindow?.GetType().Name ?? "null", !current);
            if (_parentWindow is MainWindow mainWindow)
            {
                App.Logger?.Information("AvatarTubeWindow: Calling SyncAutonomyCheckbox({NewValue})", !current);
                mainWindow.SyncAutonomyCheckbox(!current);
            }
            else
            {
                App.Logger?.Warning("AvatarTubeWindow: _parentWindow is not MainWindow!");
            }

            UpdateQuickMenuState();
        }

        private void MenuItemTalkToBambi_Click(object sender, RoutedEventArgs e)
        {
            // Show input panel for user to type to companion
            ShowInputPanel();
        }

        /// <summary>
        /// Populates the personality submenu with all available presets.
        /// Shows "Custom Prompt" indicator when custom prompts are active.
        /// </summary>
        private void PopulatePersonalityMenu()
        {
            MenuItemPersonality.Items.Clear();

            // Check if custom prompt is active
            var customPromptActive = App.Settings?.Current?.CompanionPrompt?.UseCustomPrompt == true;

            // Dark background for submenu items
            var darkBg = (SolidColorBrush)Application.Current.Resources["PanelBgBrush"];

            if (customPromptActive)
            {
                // Show custom prompt indicator
                MenuItemPersonality.Header = Loc.Get("label_personality_custom_prompt");
                MenuItemPersonality.Foreground = new SolidColorBrush(Color.FromRgb(255, 165, 0)); // Orange for custom

                // Add info item
                var infoItem = new MenuItem
                {
                    Header = Loc.Get("menu_custom_prompt_active"),
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 165, 0)),
                    Background = darkBg,
                    IsEnabled = false
                };
                MenuItemPersonality.Items.Add(infoItem);

                // Add separator
                MenuItemPersonality.Items.Add(new Separator { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4")) });

                // Add option to disable custom prompt
                var disableItem = new MenuItem
                {
                    Header = Loc.Get("menu_disable_custom_prompt"),
                    Foreground = new SolidColorBrush(Colors.White),
                    Background = darkBg
                };
                disableItem.Click += (s, e) =>
                {
                    if (App.Settings?.Current?.CompanionPrompt != null)
                    {
                        App.Settings.Current.CompanionPrompt.UseCustomPrompt = false;
                        App.Settings.Save();
                        UpdateQuickMenuState();
                        Giggle(Loc.Get("avatar_back_to_presets"));
                    }
                };
                MenuItemPersonality.Items.Add(disableItem);

                return;
            }

            // Normal preset menu
            MenuItemPersonality.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4")); // Pink default

            // Slut Mode used to be its own preset in this list, but it now lives as a
            // toggle on the Companion tab that swaps the active preset's personality
            // text with its SlutModePersonality variant. Filter the legacy preset out.
            var presets = (App.Personality?.GetAllPresets() ?? new List<PersonalityPreset>())
                .Where(p => p.Id != PersonalityPresets.SlutModeId)
                .ToList();
            var activeId = App.Settings?.Current?.ActivePersonalityPresetId ?? PersonalityPresets.BambiSpriteId;

            foreach (var preset in presets)
            {
                var menuItem = new MenuItem
                {
                    Header = GetPersonalityMenuHeader(preset, activeId),
                    Tag = preset.Id,
                    Background = darkBg,
                    Foreground = preset.Id == activeId
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4")) // Pink for active
                        : new SolidColorBrush(Colors.White)
                };

                menuItem.Click += PersonalityMenuItem_Click;
                MenuItemPersonality.Items.Add(menuItem);
            }

            // Update parent menu header with mode-aware name
            var activePreset = App.Personality?.GetActivePreset();
            var displayName = App.Mods?.GetPersonalityDisplayName(activePreset?.Name ?? "BambiSprite") ?? activePreset?.Name ?? "BambiSprite";
            MenuItemPersonality.Header = Loc.GetF("avatar_personality_format", displayName);
        }

        private string GetPersonalityMenuHeader(PersonalityPreset preset, string activeId)
        {
            var check = preset.Id == activeId ? "☑" : "☐";
            var displayName = App.Mods?.GetPersonalityDisplayName(preset.Name) ?? preset.Name;
            return $"{check} {displayName}";
        }

        private void PersonalityMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string presetId)
            {
                var preset = App.Personality?.GetPresetById(presetId);
                if (preset == null) return;

                // CCBill AI Addendum: gate explicit presets behind an age + content-policy
                // acknowledgement dialog. The SlutMode state used for the rule check is the
                // current value (we're not flipping it here, only selecting a preset).
                var slutModeOn = App.Settings?.Current?.SlutModeEnabled == true;
                if (Services.ExplicitContentGate.RequiresAcknowledgement(preset, slutModeOn))
                {
                    var promptSettings = App.Settings?.Current?.CompanionPrompt;
                    if (!Services.ExplicitContentGate.IsAlreadyAcknowledged(promptSettings))
                    {
                        var dlg = new ExplicitContentAcknowledgementDialog { Owner = this };
                        var ok = dlg.ShowDialog() == true;
                        if (!ok) return; // Cancel: revert (no-op, since we hadn't switched yet).
                        if (promptSettings != null)
                        {
                            Services.ExplicitContentGate.MarkAcknowledged(promptSettings);
                            App.Settings?.Save();
                        }
                    }
                }

                // Set the new personality
                if (App.Personality?.SetActivePreset(presetId) == true)
                {
                    UpdateQuickMenuState();
                    // Use the mod-aware display name (the menu header already does this via
                    // GetPersonalityDisplayName) so the confirmation bubble shows e.g. "Circe"
                    // instead of the raw base preset name "BambiSprite" under the Locked mod.
                    var shownName = App.Mods?.GetPersonalityDisplayName(preset.Name) ?? preset.Name;
                    var confirm = $"Now using {shownName}~ *giggles*";
                    Giggle(App.Mods?.MakeModAware(confirm) ?? confirm);
                }
            }
        }

        private void MenuItemMute_Click(object sender, RoutedEventArgs e)
        {
            _isMuted = !_isMuted;
            UpdateQuickMenuState();

            // Hide speech bubble immediately when muting
            if (_isMuted)
            {
                SpeechBubble.Visibility = Visibility.Collapsed;
            }

            // Persist to settings
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.AvatarMuted = _isMuted;
                App.Settings.Save();
            }

            // Sync to MainWindow UI
            if (_parentWindow is MainWindow mainWindow)
            {
                mainWindow.SyncQuickControlsUI(muteAvatar: _isMuted);
            }
        }

        private void MenuItemMuteWhispers_Click(object sender, RoutedEventArgs e)
        {
            // Toggle SubAudioEnabled setting (mute = disabled)
            var currentEnabled = App.Settings?.Current?.SubAudioEnabled ?? false;
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.SubAudioEnabled = !currentEnabled;
                App.Settings.Save();
            }

            UpdateQuickMenuState();

            // Sync to MainWindow UI (Settings tab and Companion tab)
            if (_parentWindow is MainWindow mainWindow)
            {
                mainWindow.SyncWhispersUI(!currentEnabled);
            }
        }

        private async void MenuItemPauseBrowser_Click(object sender, RoutedEventArgs e)
        {
            _isBrowserPaused = !_isBrowserPaused;

            try
            {
                // Access the browser through MainWindow
                if (_parentWindow is MainWindow mainWindow)
                {
                    var webView = mainWindow.GetBrowserWebView();
                    if (webView?.CoreWebView2 != null)
                    {
                        if (_isBrowserPaused)
                        {
                            // Mute browser audio using WebView2's IsMuted property
                            webView.CoreWebView2.IsMuted = true;
                            // Also try to pause any playing audio/video elements
                            await webView.CoreWebView2.ExecuteScriptAsync(@"
                                document.querySelectorAll('audio, video').forEach(el => el.pause());
                            ");
                        }
                        else
                        {
                            // Unmute browser and resume
                            webView.CoreWebView2.IsMuted = false;
                            await webView.CoreWebView2.ExecuteScriptAsync(@"
                                document.querySelectorAll('audio, video').forEach(el => el.play());
                            ");
                        }
                    }

                    // Sync to MainWindow UI
                    mainWindow.SyncQuickControlsUI(pauseBrowser: _isBrowserPaused);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to toggle browser audio: {Error}", ex.Message);
            }

            UpdateQuickMenuState();
        }

        /// <summary>
        /// Updates the quick menu items to reflect current state
        /// </summary>
        public void UpdateQuickMenuState()
        {
            // Talk to companion - mode-aware label
            var talkToLabel = App.Mods?.GetTalkToLabel() ?? Loc.Get("menu_talk_to_companion");
            var chatAvailable = App.Ai?.IsAvailable == true;
            MenuItemTalkToBambi.IsEnabled = chatAvailable;
            if (chatAvailable)
            {
                MenuItemTalkToBambi.Header = Loc.GetF("menu_talk_to_format", talkToLabel);
                MenuItemTalkToBambi.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4")); // Pink
            }
            else
            {
                MenuItemTalkToBambi.Header = Loc.GetF("menu_talk_to_locked_format", talkToLabel);
                MenuItemTalkToBambi.Foreground = new SolidColorBrush(Color.FromRgb(155, 89, 182)); // Purple for Patreon
            }

            // Engine state (use Flash.IsRunning as proxy)
            var engineRunning = App.Flash?.IsRunning == true;
            MenuItemEngine.Header = engineRunning ? Loc.Get("menu_stop_engine") : Loc.Get("menu_start_engine");
            MenuItemEngine.Foreground = engineRunning ? new SolidColorBrush(Color.FromRgb(255, 99, 71)) : new SolidColorBrush(Color.FromRgb(144, 238, 144));

            // Trigger mode
            var triggerOn = App.Settings?.Current?.TriggerModeEnabled == true;
            MenuItemTriggerMode.Header = triggerOn ? Loc.Get("menu_trigger_mode_on") : Loc.Get("menu_trigger_mode_off");
            MenuItemTriggerMode.Foreground = triggerOn ? new SolidColorBrush(Color.FromRgb(144, 238, 144)) : new SolidColorBrush(Colors.White);

            // Takeover (Patreon only) - mode-aware name
            var takeoverAvailable = App.Patreon?.HasPremiumAccess == true;
            var takeoverOn = App.Settings?.Current?.AutonomyModeEnabled == true;
            var takeoverName = App.Mods?.GetTakeoverLabel() ?? Loc.Get("menu_takeover");
            MenuItemBambiTakeover.Header = takeoverOn ? Loc.GetF("menu_takeover_on_format", takeoverName) : Loc.GetF("menu_takeover_off_format", takeoverName);
            MenuItemBambiTakeover.Foreground = takeoverOn ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4")) : new SolidColorBrush(Colors.White);
            MenuItemBambiTakeover.IsEnabled = takeoverAvailable;
            if (!takeoverAvailable)
            {
                MenuItemBambiTakeover.Header = Loc.GetF("menu_takeover_locked_format", takeoverName);
                MenuItemBambiTakeover.Foreground = new SolidColorBrush(Color.FromRgb(155, 89, 182)); // Purple for Patreon
            }

            // Personality menu
            PopulatePersonalityMenu();

            // Mute avatar
            MenuItemMute.Header = _isMuted ? Loc.Get("menu_mute_avatar_on") : Loc.Get("menu_mute_avatar_off");
            MenuItemMute.Foreground = _isMuted ? new SolidColorBrush(Color.FromRgb(255, 99, 71)) : new SolidColorBrush(Colors.White);

            // Mute whispers (inverted - muted when SubAudioEnabled is false)
            var whispersMuted = App.Settings?.Current?.SubAudioEnabled != true;
            MenuItemMuteWhispers.Header = whispersMuted ? Loc.Get("menu_mute_whispers_on") : Loc.Get("menu_mute_whispers_off");
            MenuItemMuteWhispers.Foreground = whispersMuted ? new SolidColorBrush(Color.FromRgb(255, 99, 71)) : new SolidColorBrush(Colors.White);

            // Pause browser
            MenuItemPauseBrowser.Header = _isBrowserPaused ? Loc.Get("menu_resume_browser") : Loc.Get("menu_pause_browser");
            MenuItemPauseBrowser.Foreground = _isBrowserPaused ? new SolidColorBrush(Color.FromRgb(144, 238, 144)) : new SolidColorBrush(Colors.White);

            // Lock most options when remote controlled (keep talk, attach/detach, resize)
            if (App.RemoteControl?.ControllerConnected == true)
            {
                var lockedBrush = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x70));
                MenuItemEngine.IsEnabled = false;
                MenuItemEngine.Header = Loc.Get("label_start_engine");
                MenuItemEngine.Foreground = lockedBrush;
                MenuItemTriggerMode.IsEnabled = false;
                MenuItemTriggerMode.Foreground = lockedBrush;
                MenuItemBambiTakeover.IsEnabled = false;
                MenuItemBambiTakeover.Foreground = lockedBrush;
                MenuItemPersonality.IsEnabled = false;
                MenuItemPersonality.Foreground = lockedBrush;
                MenuItemMute.IsEnabled = false;
                MenuItemMute.Foreground = lockedBrush;
                MenuItemMuteWhispers.IsEnabled = false;
                MenuItemMuteWhispers.Foreground = lockedBrush;
                MenuItemPauseBrowser.IsEnabled = false;
                MenuItemPauseBrowser.Foreground = lockedBrush;
            }
            else
            {
                // Remote controller disconnected: re-enable everything the lock block disables.
                // Without this the items stay stuck un-clickable after exiting remote control, because
                // the normal section above only refreshes their Header/Foreground, never IsEnabled.
                // (Foreground is already restored above; Takeover stays gated on Patreon access.)
                MenuItemEngine.IsEnabled = true;
                MenuItemTriggerMode.IsEnabled = true;
                MenuItemBambiTakeover.IsEnabled = takeoverAvailable;
                MenuItemPersonality.IsEnabled = true;
                MenuItemMute.IsEnabled = true;
                MenuItemMuteWhispers.IsEnabled = true;
                MenuItemPauseBrowser.IsEnabled = true;
            }
        }

        /// <summary>
        /// Gets whether the avatar is currently muted
        /// </summary>
        public bool IsMuted => _isMuted;

        /// <summary>
        /// Set mute avatar state from MainWindow
        /// </summary>
        public void SetMuteAvatar(bool isMuted)
        {
            _isMuted = isMuted;
            if (_isMuted)
            {
                SpeechBubble.Visibility = Visibility.Collapsed;
            }
            UpdateQuickMenuState();
        }

        /// <summary>
        /// Set mute whispers state from MainWindow (toggles SubAudioEnabled)
        /// </summary>
        public void SetMuteWhispers(bool isMuted)
        {
            // isMuted = true means disable whispers (SubAudioEnabled = false)
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.SubAudioEnabled = !isMuted;
                App.Settings.Save();
            }
            UpdateQuickMenuState();
        }

        /// <summary>
        /// Refreshes the personality menu to reflect current selection.
        /// Called when personality changes from another source.
        /// </summary>
        public void RefreshPersonalityMenu()
        {
            UpdateQuickMenuState();
        }

        /// <summary>
        /// Set browser paused state from MainWindow (just updates UI, MainWindow handles actual browser control)
        /// </summary>
        public void SetBrowserPaused(bool isPaused)
        {
            _isBrowserPaused = isPaused;
            UpdateQuickMenuState();
        }
    }
}
