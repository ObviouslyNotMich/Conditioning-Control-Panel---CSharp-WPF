using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Models;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Avalonia.Views;
using CoreApp = ConditioningControlPanel.App;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable CS0169 // Avalonia port: unused stub fields kept for future companion/avatar work
#pragma warning disable CS0414
#pragma warning disable CS0649

namespace ConditioningControlPanel.Avalonia.AvatarTube
{
    public enum ModerationSource
    {
        Input,
        Output
    }

    public partial class AvatarTubeWindow : Window
    {
        private readonly global::ConditioningControlPanel.IAppLogger? _logger;
        private readonly global::ConditioningControlPanel.Core.Services.Settings.ISettingsService? _settings;


        private readonly Window? _parentWindow;
        private bool _isAttached = true;
        private readonly Random _random = new();

        private readonly DispatcherTimer _poseTimer;
        private Bitmap[] _avatarPoses = Array.Empty<Bitmap>();
        private int _currentPoseIndex;
        private int _currentAvatarSet = 1;
        private int _selectedAvatarSet = 1;
        private int _maxUnlockedSet = 1;
        private bool _useAnimatedAvatar;

        private bool _circeEmoteMode;
        private bool _portraitMode;

        private DispatcherTimer? _cooldownTickTimer;
        private string? _normalChatPlaceholder;
        private DateTime? _cooldownEndsAt;

        private bool _isInputVisible;
        private bool _isShowingChatHistory;

        private readonly Queue<(string text, SpeechSource source, string? emotionLineId, string? mood)> _speechQueue = new();
        private bool _isGiggling;
        private bool _isWaitingForAi;
        private bool _isShowingAiBubble;
        private bool _isMuted;
        private bool _isMouseOverSpeechBubble;
        private bool _isPlayingUninterruptibleClip;
        private DateTime _lastSpeechEndTime = DateTime.MinValue;
        private SpeechSource _lastSpeechSource = SpeechSource.Preset;
        private int _lastSpeechLength;
        private DateTime _lastAiBubbleUtc = DateTime.MinValue;

        private int _presetGiggleCounter;
        private DateTime _lastClickTime = DateTime.MinValue;
        private DateTime _lastInteractionTime = DateTime.MinValue;
        private readonly List<DateTime> _rapidClickTimestamps = new();
        private int _animationRefreshClickCount;
        private int _interactionCount;

        private DispatcherTimer? _speechTimer;
        private DispatcherTimer? _speechDelayTimer;
        private DispatcherTimer? _idleTimer;
        private DispatcherTimer? _triggerTimer;
        private DispatcherTimer? _randomBubbleTimer;
        private DispatcherTimer? _zOrderRefreshTimer;
        private DispatcherTimer? _thinkingTimer;
        private string _thinkingPhraseBase = string.Empty;
        private int _thinkingTickCount;
        private DispatcherTimer? _typewriterTimer;
        private string _typewriterFullText = string.Empty;
        private int _typewriterIndex;
        private DispatcherTimer? _speechLeadInTimer;
        private DispatcherTimer? _mutedIndicatorTimer;
        private DispatcherTimer? _fullscreenCheckTimer;
        private DispatcherTimer? _floatTimer;
        private DispatcherTimer? _companionGreetingDebounce;
        private IAchievementService? _achievementService;

        private int _flashCounter;
        private int _subliminalCounter;
        private int _bubblePopCounter;
        private int _mindWipeCounter;
        private int _brainDrainCounter;

        private const string NoteClipCaption = "\u2661";
        private const double StartupCooldownSeconds = 3.0;
        private const double MinSpeechDelaySeconds = 2.0;
        private const double AiSpeechBonusSeconds = 5.0;
        private const int LongTextThreshold = 100;
        private const double PerCharDelaySeconds = 0.02;
        private const double SpeechLeadInSeconds = 0.6;
        private const int MaxChatHistorySize = 100;
        private const double MinScale = 0.5;
        private const double MaxScale = 1.5;
        private const double ScaleStep = 0.25;

        private const double DesignWidth = 780;
        private const double DesignHeight = 1020;
        private const double BaseOffsetFromParent = -350;
        private const double VerticalOffset = 20;
        private const double FloatDistance = 4;
        private const double FloatDuration = 2.0;

        private double _scaleFactor = 1.0;
        private double _currentScale = 1.0;
        private double _floatPhase;
        private bool _hiddenForFullscreen;
        private bool _wasAttachedBeforeFullscreen;
        private bool _hiddenForChaos;
        private bool _reattachAfterChaos;
        private bool _chaosRunActive;

        private bool IsAvatarVisibleOnScreen => IsVisible && !IsEffectivelyMinimized();

        public ObservableCollection<ChatMessage> ChatHistory { get; } = new();

        public bool IsSpeaking => _isGiggling;
        public int CurrentAvatarSet => _currentAvatarSet;

        public AvatarTubeWindow() : this(null) { }

        public AvatarTubeWindow(Window? parentWindow)
        {
            InitializeComponent();

            _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
            _settings = App.Services.GetRequiredService<global::ConditioningControlPanel.Core.Services.Settings.ISettingsService>();
_parentWindow = parentWindow;

            _poseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _poseTimer.Tick += (_, _) => PoseTimer_Tick();

            Loaded += (_, _) => ApplyChatShortcutTo(this);
            KeyDown += AvatarTubeWindow_PreviewKeyDown;
            PointerPressed += Window_PointerPressed;
            PointerWheelChanged += Window_PointerWheelChanged;

            ChatHistoryList.ItemsSource = ChatHistory;

            int playerLevel = _settings?.Current?.PlayerLevel ?? 1;
            _maxUnlockedSet = GetAvatarSetForLevel(playerLevel);
            _selectedAvatarSet = _settings?.Current?.SelectedAvatarSet ?? _maxUnlockedSet;
            _selectedAvatarSet = Math.Clamp(_selectedAvatarSet, 1, _maxUnlockedSet);
            _currentAvatarSet = _selectedAvatarSet;
            _useAnimatedAvatar = HasAnimatedAvatar(_currentAvatarSet);
            _avatarPoses = LoadAvatarPoses(_currentAvatarSet);

            if (!_useAnimatedAvatar && _avatarPoses.Length > 0)
                ImgAvatar.Source = _avatarPoses[0];

            ApplyAvatarTransform(_currentAvatarSet);
            UpdateTitleDisplay(playerLevel);
            UpdateNavigationArrows();

            WireParentEvents();

            _achievementService = global::ConditioningControlPanel.Avalonia.App.Services?.GetService<IAchievementService>();
            if (_achievementService != null)
            {
                _achievementService.AchievementUnlocked += OnAchievementUnlocked;
            }

            StartIdleTimer();
            StartTriggerTimer();
            StartRandomBubbleTimer();
            WireModerationCounter();

            _logger?.Information("AvatarTubeWindow initialized with avatar set {Set} for level {Level}", _currentAvatarSet, playerLevel);
        }

        private void WireParentEvents()
        {
            if (_parentWindow == null) return;
            _parentWindow.PositionChanged += ParentWindow_PositionChanged;
            _parentWindow.PropertyChanged += ParentWindow_PropertyChanged;
            _parentWindow.Closed += ParentWindow_Closed;
        }

        private bool IsEffectivelyMinimized()
        {
            return _parentWindow?.WindowState == WindowState.Minimized || !IsVisible;
        }

        private void PoseTimer_Tick()
        {
            if (_avatarPoses.Length <= 1 || _useAnimatedAvatar || _portraitMode) return;
            _currentPoseIndex = (_currentPoseIndex + 1) % _avatarPoses.Length;
            if (ImgAvatar != null) ImgAvatar.Source = _avatarPoses[_currentPoseIndex];
        }

        private void ContentViewbox_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            // TODO: mirror the explicit SizeToContent=Manual sizing used in WPF to avoid layered-window hangs.
        }

        private void SpeechBubble_MouseEnter(object? sender, PointerEventArgs e)
        {
            _isMouseOverSpeechBubble = true;
        }

        private void SpeechBubble_MouseLeave(object? sender, PointerEventArgs e)
        {
            _isMouseOverSpeechBubble = false;
        }

        private void AvatarTubeWindow_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _isShowingChatHistory)
            {
                ExitChatHistoryMode();
                e.Handled = true;
            }
        }

        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // TODO: report autonomy activity and close input panel when clicking outside it.
        }

        private void Window_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            // TODO: resize detached avatar with Ctrl+scroll.
        }

        private void BtnCloseChatHistory_Click(object? sender, RoutedEventArgs e)
        {
            ExitChatHistoryMode();
        }

        private void ChatHistoryText_Loaded(object? sender, RoutedEventArgs e)
        {
            if (sender is TextBlock tb && tb.Tag is string text && tb.Inlines != null)
                BuildLinkedInlines(text, tb.Inlines);
        }

        private void TxtUserInput_KeyDown(object? sender, KeyEventArgs e)
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

        private void BtnSendChat_Click(object? sender, RoutedEventArgs e)
        {
            _ = SendChatMessageAsync();
        }

        private void ImgAvatar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            {
                HideInputPanel();
                e.Handled = true;
                return;
            }

            ImgAvatar_MouseLeftButtonDown();
            e.Handled = true;
        }

        private void AvatarContextMenu_Opened(object? sender, EventArgs e)
        {
            UpdateQuickMenuState();
            RefreshEmoteMenuItemsForRemoteState();
        }

        // ===== Menu item click handlers =====
        private void MenuItemTalkToBambi_Click(object? sender, RoutedEventArgs e) => ShowInputPanel();
        private void MenuItemDetach_Click(object? sender, RoutedEventArgs e) => Detach();
        private void MenuItemAttach_Click(object? sender, RoutedEventArgs e) => Attach();
        private void MenuItemShrink_Click(object? sender, RoutedEventArgs e)
        {
            if (!_isAttached && _currentScale > MinScale)
            {
                _currentScale = Math.Max(MinScale, _currentScale - ScaleStep);
                ApplyScale();
                UpdateResizeMenuState();
            }
        }
        private void MenuItemGrow_Click(object? sender, RoutedEventArgs e)
        {
            if (!_isAttached && _currentScale < MaxScale)
            {
                _currentScale = Math.Min(MaxScale, _currentScale + ScaleStep);
                ApplyScale();
                UpdateResizeMenuState();
            }
        }
        private void MenuItemDismiss_Click(object? sender, RoutedEventArgs e) => HideTube();
        private void MenuItemEngine_Click(object? sender, RoutedEventArgs e)
        {
            // TODO: toggle engine through the main window seam once available.
        }
        private void MenuItemTriggerMode_Click(object? sender, RoutedEventArgs e)
        {
            // TODO: toggle trigger mode in settings.
        }
        private void MenuItemBambiTakeover_Click(object? sender, RoutedEventArgs e)
        {
            // TODO: toggle autonomy/takeover mode (Patreon-gated).
        }
        private void MenuItemMute_Click(object? sender, RoutedEventArgs e)
        {
            _isMuted = !_isMuted;
            Giggle(_isMuted ? "Muted~" : "Unmuted~");
            UpdateQuickMenuState();
        }
        private void MenuItemEmote_Click(object? sender, RoutedEventArgs e)
        {
            // TODO: send remote emote through the companion/chat seam.
        }
        private void MenuItemShowChatHistory_Click(object? sender, RoutedEventArgs e) => EnterChatHistoryMode();
        private void MenuItemMuteWhispers_Click(object? sender, RoutedEventArgs e)
        {
            // TODO: toggle sub-audio whispers.
        }
        private void MenuItemPauseBrowser_Click(object? sender, RoutedEventArgs e)
        {
            // TODO: toggle browser audio pause.
        }

        private void BtnPrevAvatar_Click(object? sender, PointerPressedEventArgs e)
        {
            var sets = EffectiveAvatarSets();
            int idx = Array.IndexOf(sets, _currentAvatarSet);
            if (idx > 0) SwitchToAvatarSet(sets[idx - 1]);
        }

        private void BtnNextAvatar_Click(object? sender, PointerPressedEventArgs e)
        {
            var sets = EffectiveAvatarSets();
            int idx = Array.IndexOf(sets, _currentAvatarSet);
            if (idx >= 0 && idx < sets.Length - 1) SwitchToAvatarSet(sets[idx + 1]);
        }

        // ===== Open chat command =====
        public static readonly ICommand OpenChatCommand = new RelayCommand(_ => { });

        public static void ApplyChatShortcutTo(Window window)
        {
            // TODO: parse CoreApp.Settings.Current.CompanionPrompt and attach a KeyBinding to OpenChatCommand.
        }

        public static string FormatChatShortcut()
        {
            // TODO: read CompanionPrompt settings.
            return "Ctrl+T";
        }

        public void OpenChatInput()
        {
            if (Dispatcher.UIThread.CheckAccess()) ShowInputPanel();
            else Dispatcher.UIThread.Post(ShowInputPanel);
        }

        private void UpdateQuickMenuState()
        {
            // TODO: sync menu item visibility/enabled state from settings and remote control state.
        }
        private void UpdateResizeMenuState()
        {
            // TODO: enable/disable shrink/grow based on current scale.
        }
        private void UpdateContextMenuForState()
        {
            // TODO: swap attach/detach visibility.
        }
        private void RefreshEmoteMenuItemsForRemoteState()
        {
            // TODO: populate remote emote presets when a controller is connected.
        }

        // ===== Input panel =====
        private void ToggleInputPanel()
        {
            _isInputVisible = !_isInputVisible;
            if (InputPanel != null) InputPanel.IsVisible = _isInputVisible;
            if (_isInputVisible) FocusInputAfterLayout();
        }
        private void ShowInputPanel()
        {
            _isInputVisible = true;
            if (InputPanel != null) InputPanel.IsVisible = true;
            FocusInputAfterLayout();
        }
        private void HideInputPanel()
        {
            _isInputVisible = false;
            if (InputPanel != null) InputPanel.IsVisible = false;
        }
        private void FocusInputAfterLayout()
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    TxtUserInput?.Focus();
                    TxtUserInput?.SelectAll();
                }
                catch (Exception ex)
                {
                    _logger?.Warning("AvatarTube: focus chat input failed: {Error}", ex.Message);
                }
            }, DispatcherPriority.ContextIdle);
        }

        // ===== Chat history =====
        private void EnterChatHistoryMode()
        {
            _speechTimer?.Stop();
            _speechDelayTimer?.Stop();
            _isShowingChatHistory = true;
            if (TxtChatHistoryEmpty != null)
                TxtChatHistoryEmpty.IsVisible = ChatHistory.Count == 0;
            if (SpeechScroller != null) SpeechScroller.IsVisible = false;
            if (ChatHistoryView != null) ChatHistoryView.IsVisible = true;
            if (AiBadge != null) AiBadge.IsVisible = false;
            if (SpeechBubble != null)
            {
                SpeechBubble.MaxWidth = 600;
                SpeechBubble.IsVisible = true;
            }
            ChatHistoryScroller?.ScrollToEnd();
        }

        private void ExitChatHistoryMode()
        {
            _isShowingChatHistory = false;
            if (ChatHistoryView != null) ChatHistoryView.IsVisible = false;
            if (SpeechScroller != null) SpeechScroller.IsVisible = true;
            if (SpeechBubble != null)
            {
                SpeechBubble.MaxWidth = 380;
                SpeechBubble.IsVisible = false;
            }
        }

        private void AddToChatHistory(string text, bool isUser)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            ChatHistory.Add(new ChatMessage { Text = text, IsUser = isUser });
            while (ChatHistory.Count > MaxChatHistorySize) ChatHistory.RemoveAt(0);
        }

        // ===== Chat send =====
        private async Task SendChatMessageAsync()
        {
            var input = TxtUserInput?.Text?.Trim();
            if (string.IsNullOrEmpty(input)) return;

            if (TxtUserInput != null) TxtUserInput.Text = "";
            ToggleInputPanel();

            // TODO: moderation cooldown gate.
            AddToChatHistory(input, true);

            bool aiEnabled = _settings?.Current?.AiChatEnabled == true;
            if (aiEnabled)
            {
                StartThinkingAnimation();
                await Task.Delay(500); // placeholder
                StopThinkingAnimation();
                GigglePriority("TODO: AI reply placeholder", aiGenerated: false);
            }
            else
            {
                Giggle(GetRandomBambiPhrase());
            }
        }

        // ===== Speech helpers =====
        private void Giggle(string text, string? phraseAudioPath = null, bool barkVoice = false, string? mood = null)
        {
            if (_isPlayingUninterruptibleClip) return;
            GigglePriority(text, playSound: false, aiGenerated: false, mood: mood);
        }

        private void GigglePriority(string text, bool playSound = true, bool aiGenerated = true,
            string? phraseAudioPath = null, bool barkVoice = false, string? mood = null)
        {
            if (_isPlayingUninterruptibleClip) return;
            if (aiGenerated) _lastAiBubbleUtc = DateTime.UtcNow;
            StopThinkingAnimation();
            _isWaitingForAi = false;
            _speechQueue.Clear();
            _speechTimer?.Stop();
            _speechDelayTimer?.Stop();
            _isGiggling = false;
            if (aiGenerated) AddToChatHistory(text, false);
            ShowGiggle(text, playSound, aiGenerated ? SpeechSource.AI : SpeechSource.Preset, mood: mood);
        }

        private void ShowGiggle(string text, bool playSound = false, SpeechSource source = SpeechSource.Preset,
            string? phraseAudioPath = null, bool aiGenerated = false, bool bypassClipLock = false,
            bool barkVoice = false, string? emotionLineId = null, string? mood = null)
        {
            if (_isPlayingUninterruptibleClip && !bypassClipLock) return;
            if (AiBadge != null) AiBadge.IsVisible = aiGenerated;
            if (PolicyBadge != null) PolicyBadge.IsVisible = false;

            if (_isShowingChatHistory)
            {
                _isShowingChatHistory = false;
                if (ChatHistoryView != null) ChatHistoryView.IsVisible = false;
                if (SpeechScroller != null) SpeechScroller.IsVisible = true;
                if (SpeechBubble != null) SpeechBubble.MaxWidth = 380;
            }

            if (_isMuted || !IsAvatarVisibleOnScreen)
            {
                _isGiggling = false;
                _lastSpeechEndTime = DateTime.Now;
                _lastSpeechSource = source;
                _lastSpeechLength = text?.Length ?? 0;
                ProcessNextSpeech();
                return;
            }

            _isGiggling = true;
            _isShowingAiBubble = source == SpeechSource.AI;

            PopulateSpeechBubble(text);
            AdjustBubbleSize(text);
            if (SpeechBubble != null) SpeechBubble.IsVisible = true;

            double duration = Math.Clamp(_settings?.Current?.BubbleDurationSeconds ?? 2.0, 1.0, 10.0);
            _speechTimer?.Stop();
            _speechTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(duration) };
            var capturedSource = source;
            var capturedLength = text?.Length ?? 0;
            _speechTimer.Tick += (_, _) =>
            {
                if (_isMouseOverSpeechBubble)
                {
                    _speechTimer.Interval = TimeSpan.FromSeconds(1);
                    return;
                }
                _speechTimer.Stop();
                if (SpeechBubble != null) SpeechBubble.IsVisible = false;
                _isShowingAiBubble = false;
                _lastSpeechEndTime = DateTime.Now;
                _lastSpeechSource = capturedSource;
                _lastSpeechLength = capturedLength;
                _isGiggling = false;
                ProcessNextSpeech();
            };
            _speechTimer.Start();
            ResetIdleTimer();
        }

        private void ProcessNextSpeech()
        {
            if (_speechQueue.Count == 0)
            {
                _isGiggling = false;
                return;
            }
            var next = _speechQueue.Dequeue();
            ShowSpeechBySource(next.text, next.source, next.emotionLineId, next.mood);
        }

        private void ShowSpeechBySource(string text, SpeechSource source, string? emotionLineId = null, string? mood = null)
        {
            if (source == SpeechSource.Trigger)
                ShowTriggerBubbleImmediate(text);
            else
                ShowGiggle(text, source == SpeechSource.AI, source, mood: mood);
        }

        private void ShowTriggerBubbleImmediate(string text)
        {
            ShowGiggle(text, playSound: false, source: SpeechSource.Trigger);
        }

        private void PopulateSpeechBubble(string text)
        {
            if (TxtSpeech != null && TxtSpeech.Inlines != null)
                BuildLinkedInlines(text, TxtSpeech.Inlines);
        }

        private void AdjustBubbleSize(string text)
        {
            int chars = text?.Length ?? 0;
            double fontSize = chars <= 50 ? 22 : chars <= 120 ? 20 : chars <= 250 ? 18 : 16;
            if (TxtSpeech != null) TxtSpeech.FontSize = fontSize;
            SpeechScroller?.ScrollToHome();
        }

        private void BuildLinkedInlines(string text, InlineCollection target)
        {
            target.Clear();
            if (string.IsNullOrEmpty(text))
            {
                target.Add(new Run(text ?? ""));
                return;
            }
            target.Add(new Run(text));
            // TODO: parse markdown and known video links into Hyperlink inlines.
        }

        private void ShowModerationRefusalBubble(ModerationSource source)
        {
            var locKey = source == ModerationSource.Input ? "moderation_input_refusal" : "moderation_output_refusal";
            var text = Loc.Get(locKey);
            if (string.IsNullOrEmpty(text))
                text = source == ModerationSource.Input
                    ? "This message can't be sent under our content policy."
                    : "AI declined to respond.";
            AddToChatHistory(text, false);
            ShowGiggle(text, playSound: false, source: SpeechSource.AI, aiGenerated: false);
            if (AiBadge != null) AiBadge.IsVisible = false;
            if (PolicyBadge != null) PolicyBadge.IsVisible = true;
        }

        private void ShowMutedIndicator()
        {
            if (SpeechBubble?.IsVisible == true) return;
            if (TxtSpeech != null && TxtSpeech.Inlines != null)
            {
                TxtSpeech.Inlines.Clear();
                TxtSpeech.Inlines.Add(new Run("MUTED \U0001F509") { Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 200)) });
                TxtSpeech.FontSize = 20;
            }
            if (SpeechBubble != null) SpeechBubble.IsVisible = true;
            _mutedIndicatorTimer?.Stop();
            _mutedIndicatorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _mutedIndicatorTimer.Tick += (_, _) =>
            {
                _mutedIndicatorTimer.Stop();
                if (SpeechBubble != null) SpeechBubble.IsVisible = false;
            };
            _mutedIndicatorTimer.Start();
        }

        // ===== Idle / trigger / random bubble timers =====
        private void StartIdleTimer()
        {
            _idleTimer?.Stop();
            _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _idleTimer.Tick += (_, _) =>
            {
                // TODO: occasional idle giggle.
            };
            _idleTimer.Start();
        }
        private void ResetIdleTimer()
        {
            _idleTimer?.Stop();
            _idleTimer?.Start();
        }
        private void StartTriggerTimer()
        {
            _triggerTimer?.Stop();
            _triggerTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _triggerTimer.Tick += (_, _) =>
            {
                // TODO: custom trigger phrases.
            };
            _triggerTimer.Start();
        }
        private void RestartTriggerTimer()
        {
            StartTriggerTimer();
        }
        private void StartRandomBubbleTimer()
        {
            _randomBubbleTimer?.Stop();
            _randomBubbleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _randomBubbleTimer.Tick += (_, _) =>
            {
                // TODO: spawn AvatarRandomBubble near the avatar.
            };
            _randomBubbleTimer.Start();
        }
        private void StopZOrderRefreshTimer()
        {
            _zOrderRefreshTimer?.Stop();
        }
        private void StartZOrderRefreshTimer()
        {
            _zOrderRefreshTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _zOrderRefreshTimer.Tick += (_, _) => BringAttachedPairToFront();
            _zOrderRefreshTimer.Start();
        }
        private void StartThinkingAnimation()
        {
            _isWaitingForAi = true;
            _thinkingTimer?.Stop();
            _thinkingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _thinkingTimer.Tick += (_, _) =>
            {
                if (!_isWaitingForAi) { _thinkingTimer?.Stop(); return; }
                PopulateSpeechBubble("Thinking" + new string('.', (_thinkingTickCount++ % 3) + 1));
                if (SpeechBubble != null) SpeechBubble.IsVisible = true;
            };
            _thinkingTimer.Start();
        }
        private void StopThinkingAnimation()
        {
            _isWaitingForAi = false;
            _thinkingTimer?.Stop();
        }

        // ===== Audio =====
        private void PlayFallbackBubbleSound() { /* TODO: cross-platform IAudioPlayer */ }
        private void PlayGiggleSound() { /* TODO: cross-platform IAudioPlayer */ }
        private void PlayBarkVoice(string path) { /* TODO: cross-platform IAudioPlayer */ }
        private void PlayPhraseAudio(string path) { /* TODO: cross-platform IAudioPlayer */ }
        private void PlayAvatarPopSound() { /* TODO: cross-platform IAudioPlayer */ }
        private void StopSpokenAudio() { /* TODO: stop cross-platform spoken audio channel */ }
        private void StopVoiceLineAudio() { /* TODO: stop voice line playback */ }
        private double AudioDurationSec(string path) => 0;
        private double EstimateDurationSec(string? text) => Math.Max(1, (text?.Length ?? 0) / 12.0);

        // ===== Helpers =====
        private string GetRandomBambiPhrase() => "Bimbo doll~";
        private void PlayClickBounce() { /* TODO: Avalonia animation */ }
        private void PlayDoubleBounce() { /* TODO: Avalonia animation */ }
        private void TriggerBambiCumAndCollapse() { /* TODO: chaos / achievement hook */ }

        private double CalculateRequiredDelayAfterLastSpeech()
        {
            double delay = MinSpeechDelaySeconds;
            if (_lastSpeechSource == SpeechSource.AI) delay += AiSpeechBonusSeconds;
            if (_lastSpeechLength > LongTextThreshold)
                delay += (_lastSpeechLength - LongTextThreshold) * PerCharDelaySeconds;
            return delay;
        }

        private void WireModerationCounter()
        {
            // TODO: subscribe to Core moderation counter when available.
        }
        private void OnWarningTriggered(object state)
        {
            // TODO: show content policy warning dialog.
        }
        private void OnCooldownStarted(DateTime endsAt)
        {
            _cooldownEndsAt = endsAt;
            if (TxtUserInput != null) { TxtUserInput.IsEnabled = false; TxtUserInput.Opacity = 0.5; TxtUserInput.Text = ""; }
            if (BtnSendChat != null) { BtnSendChat.IsEnabled = false; BtnSendChat.Opacity = 0.5; }
            _cooldownTickTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _cooldownTickTimer.Tick += CooldownTick;
            _cooldownTickTimer.Start();
            CooldownTick(null, EventArgs.Empty);
        }
        private void CooldownTick(object? sender, EventArgs e)
        {
            if (!_cooldownEndsAt.HasValue) { _cooldownTickTimer?.Stop(); return; }
            var remaining = _cooldownEndsAt.Value - DateTime.UtcNow;
            if (remaining.TotalSeconds <= 0)
            {
                OnCooldownEnded();
                return;
            }
            if (TxtUserInput != null)
                TxtUserInput.Text = string.Format(Loc.Get("chat_cooldown_active"), (int)Math.Ceiling(remaining.TotalSeconds));
        }
        private void OnCooldownEnded()
        {
            _cooldownEndsAt = null;
            _cooldownTickTimer?.Stop();
            if (TxtUserInput != null) { TxtUserInput.IsEnabled = true; TxtUserInput.Opacity = 1.0; TxtUserInput.Text = ""; }
            if (BtnSendChat != null) { BtnSendChat.IsEnabled = true; BtnSendChat.Opacity = 1.0; }
        }

        // ===== Window lifecycle =====
        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            Topmost = false;
            CalculateScaleFactor();
            Dispatcher.UIThread.Post(() =>
            {
                if (_parentWindow?.IsVisible == true && _parentWindow.WindowState != WindowState.Minimized)
                {
                    UpdatePosition();
                    StartFloatingAnimation();
                    BringAttachedPairToFront(true);
                }
                if (SpeechBubble != null) SpeechBubble.Margin = new Thickness(0, 0, 125, 550);
            }, DispatcherPriority.Normal);
            StartFullscreenDetection();
        }

        private void OnFirstContentRendered(object? sender, EventArgs e)
        {
            Opened -= OnFirstContentRendered;
            if (ClientSize.Width > 0 && ClientSize.Height > 0)
            {
                Width = ClientSize.Width;
                Height = ClientSize.Height;
            }
            SizeToContent = SizeToContent.Manual;
            InvalidateVisual();
        }

        protected override void OnClosed(EventArgs e)
        {
            _poseTimer?.Stop();
            _fullscreenCheckTimer?.Stop();
            StopFloatingAnimation();
            _speechTimer?.Stop();
            _speechDelayTimer?.Stop();
            _idleTimer?.Stop();
            _triggerTimer?.Stop();
            _randomBubbleTimer?.Stop();
            _zOrderRefreshTimer?.Stop();
            StopVoiceLineAudio();
            if (_parentWindow != null)
            {
                _parentWindow.PositionChanged -= ParentWindow_PositionChanged;
                _parentWindow.PropertyChanged -= ParentWindow_PropertyChanged;
                _parentWindow.Closed -= ParentWindow_Closed;
            }
            if (_achievementService != null)
            {
                _achievementService.AchievementUnlocked -= OnAchievementUnlocked;
            }
            base.OnClosed(e);
        }

        // ===== Parent window event handlers =====
        private void ParentWindow_PositionChanged(object? sender, EventArgs e)
        {
            if (_parentWindow?.WindowState == WindowState.Minimized) return;
            UpdatePosition();
            if (_isAttached) BringAttachedPairToFront();
        }

        private void ParentWindow_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Window.WindowStateProperty)
                ParentWindow_StateChanged(sender, e);
            else if (e.Property == Visual.IsVisibleProperty)
                ParentWindow_IsVisibleChanged(sender, e);
        }

        private void ParentWindow_StateChanged(object? sender, EventArgs e)
        {
            if (_parentWindow == null) return;
            switch (_parentWindow.WindowState)
            {
                case WindowState.Minimized:
                    PauseAvatarGif();
                    if (_isAttached) Hide();
                    break;
                case WindowState.Normal:
                case WindowState.Maximized:
                    ResumeAvatarGif();
                    if (_parentWindow.IsVisible && _settings?.Current?.AvatarEnabled == true)
                    {
                        Show();
                        if (_isAttached) { UpdatePosition(); BringAttachedPairToFront(); }
                    }
                    break;
            }
        }

        private void ParentWindow_IsVisibleChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_parentWindow == null) return;
            bool visible = (bool)e.NewValue!;
            if (visible && _parentWindow.WindowState != WindowState.Minimized && _settings?.Current?.AvatarEnabled == true)
            {
                ResumeAvatarGif();
                Show();
                if (_isAttached) { UpdatePosition(); BringAttachedPairToFront(); }
            }
            else
            {
                PauseAvatarGif();
                if (_isAttached) Hide();
            }
        }

        private void ParentWindow_Closed(object? sender, EventArgs e)
        {
            if (_isAttached)
            {
                try { Close(); } catch { /* already closing */ }
            }
            else
            {
                Giggle("Main window closed! Right-click to dismiss~");
            }
        }

        // ===== Public show/hide =====
        public void ShowTube()
        {
            _hiddenForFullscreen = false;
            Show();
            if (_parentWindow?.IsVisible == true && _parentWindow.WindowState != WindowState.Minimized)
            {
                UpdatePosition();
                if (_isAttached) BringAttachedPairToFront();
            }
            StartFloatingAnimation();
        }

        public void HideTube()
        {
            Hide();
        }

        public void SetMuted(bool muted)
        {
            _isMuted = muted;
        }

        // ===== Commands =====
        private class RelayCommand : ICommand
        {
            private readonly Action<object?> _execute;
public RelayCommand(Action<object?> execute) => _execute = execute;
            public event EventHandler? CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) => _execute(parameter);
        }
    }
}

#pragma warning restore CS0169
#pragma warning restore CS0414
#pragma warning restore CS0649
