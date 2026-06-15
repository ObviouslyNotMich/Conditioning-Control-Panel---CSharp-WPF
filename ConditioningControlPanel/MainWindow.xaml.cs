using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;
using NAudio.Wave;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    public partial class MainWindow : Window
    {
        // DWM API for Windows 11 rounded corners
        [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;
        private const int DWMWCP_ROUNDSMALL = 3;

        // Win32 API for forcing window to foreground
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new(-2);
        private const uint SWP_NOACTIVATE = 0x0010;

        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;

        private bool _isRunning = false;
        public bool IsEngineRunning => _isRunning;
        private bool _isLoading = true;

        /// <summary>
        /// Items shown in the top-bar mod switcher ComboBox. Rebuilt by InitializeModSelector.
        /// </summary>
        public ObservableCollection<ModSelectorItem> AvailableMods { get; } = new();
        // Guards SelectionChanged from re-entering activation while we repopulate the list.
        private bool _suppressModSelectorChange;
        private BrowserService? _browser;
        private bool _browserInitialized = false;
        private bool _skipSiteToggleNavigation = false;
        private Window? _browserPopoutWindow = null;
        private bool _isDualMonitorPlaybackActive = false;
        private bool _isBrowserFullscreen = false;
        private bool _browserFullscreenWasPopout = false;
        private double _browserPreFullscreenZoom = 1.0;
        // W3 Piece 1 — catalogue lookup state. One CTS per in-flight lookup;
        // the navigation hook cancels the previous one when a new HT URL is
        // detected, so a slow lookup landing after the user moved on can't
        // surface a stale toast. _currentCatalogueHtVideoId is set just before
        // the toast appears and verified on action click — if it no longer
        // matches the current page, the action no-ops.
        private System.Threading.CancellationTokenSource? _catalogueLookupCts;
        private string? _currentCatalogueHtVideoId;
        // Popout pre-fullscreen state
        private WindowStyle _popoutPreFsStyle;
        private ResizeMode _popoutPreFsResize;
        private WindowState _popoutPreFsState;
        private double _popoutPreFsLeft, _popoutPreFsTop, _popoutPreFsWidth, _popoutPreFsHeight;
        private bool _popoutPreFsTopmost;
        private TrayIconService? _trayIcon;
        private GlobalKeyboardHook? _keyboardHook;
        private bool _isCapturingPanicKey = false;
        internal bool IsCapturingPanicKey => _isCapturingPanicKey;
        private bool _exitRequested = false;
        private int _panicPressCount = 0;
        private string _leaderboardMode = "monthly";

        // Lockdown mode
        private int _lockdownTimerClickCount = 0;
        private DateTime _lockdownTimerLastClick = DateTime.MinValue;
        private Brush? _preLockdownWindowBg;
        private Brush? _preLockdownTitleBarBg;
        private bool _isStreakFixMode = false;
        private DispatcherTimer? _remoteNotificationTimer;
        private DispatcherTimer? _remoteSessionInfoTimer;

        // Tab animation storyboards (so they can be stopped when tab is hidden)
        private Storyboard? _seasonTitleStoryboard;
        private Storyboard? _lockdownPulseStoryboard;
        private bool _skillTreeAnimationsActive = false;

        private static readonly Dictionary<string, string> CommandLabels = new()
        {
            ["show_pink_filter"] = "cmd_pink_filter_enabled",
            ["stop_pink_filter"] = "cmd_pink_filter_disabled",
            ["show_spiral"] = "cmd_spiral_enabled",
            ["stop_spiral"] = "cmd_spiral_disabled",
            ["start_bubbles"] = "cmd_bubbles_started",
            ["stop_bubbles"] = "cmd_bubbles_stopped",
            ["trigger_video"] = "cmd_video_triggered",
            ["trigger_haptic"] = "cmd_haptic_triggered",
            ["trigger_bubble_count"] = "cmd_bubble_count_triggered",
            ["start_autonomy"] = "cmd_autonomy_enabled",
            ["stop_autonomy"] = "cmd_autonomy_disabled",
            ["start_session"] = "cmd_session_started",
            ["pause_session"] = "cmd_session_paused",
            ["resume_session"] = "cmd_session_resumed",
            ["stop_session"] = "cmd_session_stopped",
            ["enable_strict_lock"] = "cmd_strict_lock_enabled",
            ["disable_strict_lock"] = "cmd_strict_lock_disabled",
            ["disable_panic"] = "cmd_panic_key_disabled",
            ["enable_panic"] = "cmd_panic_key_enabled",
            ["trigger_panic"] = "cmd_all_effects_stopped",
        };

        private static readonly HashSet<string> SuppressedCommands = new()
        {
            "trigger_flash", "trigger_subliminal",
            "set_pink_opacity", "set_spiral_opacity",
            "duck_audio", "unduck_audio",
        };

        /// <summary>
        /// Fires when the engine is stopped (for avatar reactions)
        /// </summary>
        public event EventHandler? EngineStopped;
        private DateTime _lastPanicTime = DateTime.MinValue;
        private string? _lastKnownUnifiedId;

        /// <summary>
        /// Gets the browser WebView2 control for external access (e.g., avatar audio controls)
        /// </summary>
        public Microsoft.Web.WebView2.Wpf.WebView2? GetBrowserWebView() => _browser?.WebView;
        
        // Session Engine
        private SessionEngine? _sessionEngine;
        
        // Avatar Tube Window
        private AvatarTubeWindow? _avatarTubeWindow;
        private WaveOutEvent? _levelUpSoundDevice;
        private AudioFileReader? _levelUpSoundFile;
        private bool _avatarWasAttachedBeforeMaximize = false;
        private bool _avatarWasAttachedBeforeBrowserFullscreen = false;

        // Auto-pause state when minimized with attached avatar
        private bool _autonomyWasPausedOnMinimize = false;
        private bool _avatarWasMutedOnMinimize = false;
        private bool _wasAutonomyRunningBeforeMinimize = false;
        private bool _wasAvatarUnmutedBeforeMinimize = false;

        // Achievement tracking
        private Dictionary<string, Image> _achievementImages = new();

        // Pink Rush popup
        private PinkRushPopup? _pinkRushPopup;

        // Lucky proc toast popup
        private Window? _luckyProcPopup;
        
        // Ramp tracking
        private DispatcherTimer? _rampTimer;
        private DateTime _rampStartTime;
        private Dictionary<string, double> _rampBaseValues = new();

        // Easter egg tracking (100 clicks in 60 seconds)
        private int _easterEggClickCount = 0;
        private DateTime _easterEggFirstClick = DateTime.MinValue;
        private bool _easterEggTriggered = false;
        
        // Scheduler tracking
        private DispatcherTimer? _schedulerTimer;
        private bool _schedulerAutoStarted = false;
        private bool _manuallyStoppedDuringSchedule = false;

        // Banner rotation (cycles through 3 messages: support, welcome, thanks)
        private DispatcherTimer? _bannerRotationTimer;
        private int _bannerCurrentIndex = 0; // 0=Primary (support), 1=Secondary (welcome), 2=Tertiary (thanks)
        private List<string> _bannerMessages = new();

        // Marquee animation
        private System.Windows.Media.Animation.Storyboard? _marqueeStoryboard;
        private DispatcherTimer? _marqueeRefreshTimer;
        private string _currentMarqueeMessage = "";

        // Content packs
        // PacksSection in MainWindow.xaml is currently Visibility="Collapsed" — most packs live outside the app,
        // and users are routed to Discord via BtnGetPacks. Flip this const + the two Visibility values to restore.
        private const bool PacksSectionEnabled = false;
        private ObservableCollection<ContentPack> _availablePacks = new();
        private DispatcherTimer? _packPreviewTimer;

        // Stat pills
        private DispatcherTimer? _statPillUpdateTimer;

        // Conditioning time tracker
        private DispatcherTimer? _conditioningTimeTimer;
        private DateTime _conditioningStartTime;
        private double _conditioningBaselineMinutes; // TotalConditioningMinutes at session start (avoids double-counting)
        private DispatcherTimer? _conditioningTimeSyncTimer; // Server sync every 15 minutes
        private int _conditioningTimeSecondCounter; // Count seconds for minute-based saves

        public MainWindow()
        {
            InitializeComponent();

            // Apply the user-configured chat shortcut. AvatarTubeWindow does the same
            // for itself; both windows respond to the same RoutedUICommand. We ALSO
            // register a Win32 system-wide hotkey via GlobalHotkeyService so the same
            // combo opens chat from any other app (browser, terminal, etc.) without
            // needing one of our windows to have focus.
            Loaded += (_, _) =>
            {
                AvatarTubeWindow.ApplyChatShortcutTo(this);
                RefreshChatShortcutLabel();
                ApplyGlobalChatHotkey();
                HookFocusGazeService();
                HookBlinkTrainerService();
            };
            Closing += (_, _) => Services.GlobalHotkeyService.Unregister();

            // Set version dynamically from assembly
            var version = Services.UpdateService.GetCurrentVersion();
            TxtVersion.Text = $"Version {version}";
            Title = $"Conditioning Control Panel v{version}";
            TxtTitleBarVersion.Text = $"Conditioning Control Panel v{version}";
            TxtHeaderVersion.Text = $"v{version}";

            // Center on primary monitor
            CenterOnPrimaryScreen();
            
            // Load logo
            LoadLogo();

            // Initialize mod selector display
            InitializeModSelector();

            // Apply the persisted active mod to the rest of the UI. Without
            // these calls, a fresh launch keeps the XAML-default (Bambi)
            // feature card icons + accent brushes regardless of which mod
            // is actually active — the user only saw the correct theme
            // after manually re-picking the mod in the selector
            // (ApplyActiveModChange). Logo + selector chip + tube/avatar
            // already painted correctly because they were on the startup
            // path; these three were only reached through ApplyActiveModChange.
            LoadTakeoverImage();
            LoadFeatureImages();
            RefreshThemeAwareElements();

            // Initialize tray icon
            _trayIcon = new TrayIconService(this);
            // Let the bark system observe tray-driven events (e.g. "wake Bambi").
            App.Bark?.AttachTray(_trayIcon);
            _trayIcon.OnExitRequested += () =>
            {
                if (App.Lockdown?.IsActive == true) return;

                _exitRequested = true;
                if (_isRunning) StopEngine();

                // Kill all audio and effects - ensures clean exit with audio unducked
                App.KillAllAudio();

                // Explicitly dispose overlay
                try
                {
                    App.Overlay?.Dispose();
                }
                catch { }

                EnsureSessionRestoredForExit();
                SaveSettings();
                Application.Current.Shutdown();
            };
            _trayIcon.OnShowRequested += () =>
            {
                ShowAvatarTube();
            };
            _trayIcon.OnWakeBambiRequested += () =>
            {
                WakeBambiUp();
            };

            // Initialize global keyboard hook (only if panic key is enabled)
            _keyboardHook = new GlobalKeyboardHook();
            _keyboardHook.KeyPressed += OnGlobalKeyPressed;
            _keyboardHook.KeyPressedWithVkCode += (key, vkCode) => App.KeywordTriggers?.OnKeyPressed(key, vkCode);
            App.KeywordTriggers?.SetSessionActiveCallback(() => _sessionEngine?.IsRunning == true);
            if (App.Settings.Current.KeywordTriggersEnabled && KeywordTriggerService.HasAccess())
                App.KeywordTriggers?.Start();
            if (App.Settings.Current.PanicKeyEnabled || App.Settings.Current.KeywordTriggersEnabled)
            {
                _keyboardHook.Start();
            }

            // Initialize lockdown mode event handlers
            InitializeLockdown();

            // Subscribe to progression events for real-time XP updates
            App.Progression.XPChanged += OnXPChanged;
            App.Progression.LevelUp += OnLevelUp;

            // Post-session media log: the dialog appears here for both natural completion
            // and abort. SessionEngine raises LogReady AFTER it fires SessionCompleted, so
            // OnSessionCompleted handles XP awarding only - the dialog is shown from this hook.
            if (App.SessionLog != null)
            {
                App.SessionLog.LogReady += OnSessionLogReady;
            }

            // Subscribe to companion events for real-time UI updates (v5.3)
            if (App.Companion != null)
            {
                App.Companion.XPAwarded += OnCompanionXPAwarded;
                App.Companion.CompanionLevelUp += OnCompanionLevelUp;
                App.Companion.XPDrained += OnCompanionXPDrained;
                App.Companion.CompanionSwitched += OnCompanionSwitched;
            }

            // Subscribe to cloud profile sync event to refresh UI when profile loads
            App.ProfileSync.ProfileLoaded += OnProfileLoaded;
            App.ProfileSync.SyncHealthChanged += OnSyncHealthChanged;

            LoadSettings();
            InitializePresets();
            UpdateUI();
            SetupHelpButtons();

            // Sync startup registration with settings
            StartupManager.SyncWithSettings(App.Settings.Current.RunOnStartup);

            _isLoading = false;

            // Initialize phrase count display
            UpdatePhraseCountDisplay();

            // Initialize achievement grid and subscribe to unlock events
            PopulateAchievementGrid();
            if (App.Achievements != null)
            {
                App.Achievements.AchievementUnlocked += OnAchievementUnlockedInMainWindow;
            }

            // Subscribe to quest events
            if (App.Quests != null)
            {
                App.Quests.QuestCompleted += OnQuestCompleted;
                App.Quests.QuestProgressChanged += OnQuestProgressChanged;
                App.Quests.QuestsRefreshed += (s, e) => Dispatcher.Invoke(() => RefreshQuestUI());
            }

            // Subscribe to skill tree events
            if (App.SkillTree != null)
            {
                App.SkillTree.PinkRushStarted += OnPinkRushStarted;
                App.SkillTree.PinkRushEnded += OnPinkRushEnded;
                App.SkillTree.LuckyProc += OnLuckyProc;
            }

            // Subscribe to roadmap events
            if (App.Roadmap != null)
            {
                App.Roadmap.StepCompleted += OnRoadmapStepCompleted;
                App.Roadmap.TrackUnlocked += OnRoadmapTrackUnlocked;
            }

            // Initialize Avatar tab settings
            InitializePatreonTab();

            // Initialize Exclusives section visibility for already-logged-in users
            UpdateAccountLinkingUI();

            // Initialize banner rotation
            InitializeBannerRotation();

            // Ensure all services are stopped on startup (cleanup any leftover state)
            App.BouncingText.Stop();
            App.Overlay.Stop();

            // v6.0: fresh installs land on CCP Default (neutral baseline). No first-launch mod picker.

            // Show welcome dialog on first launch, then start tutorial
            // But delay tutorial if update dialog is being shown
            if (WelcomeDialog.ShowIfNeeded())
            {
                Dispatcher.BeginInvoke(new Action(async () =>
                {
                    // Wait for any update dialog to be dismissed first
                    // Check every 500ms for up to 30 seconds
                    for (int i = 0; i < 60 && App.IsUpdateDialogActive; i++)
                    {
                        await Task.Delay(500);
                    }

                    // Only start tutorial if update dialog is done
                    if (!App.IsUpdateDialogActive)
                    {
                        StartTutorial();
                    }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else
            {
                // Not first launch - check if we need to show "What's New" after an update
                ShowWhatsNewIfNeeded();
                TryPresentSeasonRecap();
            }

            // Initialize scheduler timer (checks every 30 seconds)
            _schedulerTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _schedulerTimer.Tick += SchedulerTimer_Tick;

            // Delay scheduler startup by 60 seconds to allow app to fully initialize
            // This prevents issues when restarting after an update while in a scheduled time window
            const int schedulerGracePeriodSeconds = 60;
            App.Logger?.Information("Scheduler will start after {Seconds}s grace period", schedulerGracePeriodSeconds);

            Task.Delay(TimeSpan.FromSeconds(schedulerGracePeriodSeconds)).ContinueWith(_ =>
            {
                if (Application.Current?.Dispatcher?.HasShutdownStarted == true) return;
                Dispatcher.BeginInvoke(() =>
                {
                    if (Application.Current == null) return;

                    _schedulerTimer.Start();
                    CheckSchedulerOnStartup();
                    App.Logger?.Information("Scheduler grace period complete - scheduler now active");
                });
            });
            
            // Show local level/XP immediately (cloud sync may update later via ProfileLoaded)
            UpdateLevelDisplay();

            // Initialize browser when window is loaded
            Loaded += MainWindow_Loaded;

            // Phase 10: live-refresh the Deeper tab on library changes
            // (FileSystemWatcher fires through dispatcher.BeginInvoke, debounced
            // 300ms). Detached on window close so a closed window doesn't keep
            // reacting to file drops.
            if (App.EnhancementLibrary != null)
                App.EnhancementLibrary.LibraryChanged += OnDeeperLibraryChanged;

            // W3 Piece 1 — register the "open file in Deeper Player" opener so
            // the catalogue lookup service can hand a freshly-downloaded
            // enhancement straight to the runtime UI without taking a static
            // reference to MainWindow.
            //
            // NOT OpenDeeperFile (which routes to the Editor). Catalogue
            // enhancements should auto-play, matching the user's expectation
            // after clicking "Use one" / picking a row. We mirror the Editor's
            // own Preview button (DeeperEditorWindow.cs:3637) — the canonical
            // "open this .ccpenh.json into the Player" pattern uses the 4-arg
            // EnhancementPlayerWindow constructor with a source tag so the
            // discovery-source badge can show "catalogue" later if we add it.
            //
            // Returns true on successful Show(), false on any failure so the
            // service can surface the OpenError toast (which offers "Open
            // Library" as a recovery action).
            App.CatalogueLookup?.SetOpener(path =>
            {
                try
                {
                    var enhancement = App.EnhancementLibrary?.Open(path);
                    if (enhancement == null)
                    {
                        App.Logger?.Warning("[Catalogue] EnhancementLibrary.Open returned null for {Path}", path);
                        return false;
                    }
                    // captures MainWindow as owner; valid since this opener is registered during MainWindow's lifetime
                    var win = new Views.Deeper.EnhancementPlayerWindow(
                        App.DeeperPlayer, App.DeeperHost, enhancement, "catalogue") { Owner = this };
                    win.Show();
                    return true;
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "[Catalogue] Player open failed for {Path}", path);
                    return false;
                }
            });

            // Close the Exclusives submenu popup on Alt+Tab / focus loss.
            // MouseLeave doesn't fire during Alt+Tab, so without this the popup
            // stays pinned on top of whatever app the user switched to.
            Deactivated += (_, __) =>
            {
                if (ExclusivesSubmenuPopup != null && ExclusivesSubmenuPopup.IsOpen)
                {
                    _exclusivesMenuCloseTimer?.Stop();
                    _exclusivesPinned = false;
                    ExclusivesSubmenuPopup.IsOpen = false;
                }
            };

            // The Exclusives popup uses StaysOpen=True (to avoid WPF's mouse-capture
            // quirk where StaysOpen=False swallows clicks on the placement target
            // and holds capture until the window loses+regains focus). We close
            // it manually here when the user clicks outside both the launcher AND
            // the popup content. Clicks inside the popup DO reach this handler
            // (input is routed through the main window), so we must also bail on
            // them — otherwise the popup closes before the sub-item Click can
            // run and the tab-switch is swallowed.
            PreviewMouseDown += (_, e) =>
            {
                if (ExclusivesSubmenuPopup == null || !ExclusivesSubmenuPopup.IsOpen) return;
                if (BtnPatreonExclusives == null) return;
                if (e.OriginalSource is not DependencyObject src) return;
                if (IsVisualDescendant(src, BtnPatreonExclusives)) return;
                if (ExclusivesSubmenuPopup.Child is DependencyObject popupChild
                    && IsVisualDescendant(src, popupChild)) return;
                _exclusivesPinned = false;
                ExclusivesSubmenuPopup.IsOpen = false;
            };

            // velvet-mosaic: highlight dashboard cards whose feature is enabled, and
            // keep them in sync when settings change anywhere else.
            Loaded += (_, __) => RefreshFeatureCardActiveStates();
            if (App.Settings?.Current is System.ComponentModel.INotifyPropertyChanged settingsInpc)
            {
                settingsInpc.PropertyChanged += OnSettingsPropertyChangedForCards;
            }
            // velvet-mosaic: right-clicking a card quick-toggles its feature on/off.
            VelvetFeatureGrid.AddHandler(Features.FeatureCard.ToggleRequestedEvent,
                new RoutedEventHandler(OnFeatureCardToggleRequested));
        }

        private void OnXPChanged(object? sender, double xp)
        {
            Dispatcher.Invoke(() => UpdateLevelDisplay());
        }

        private void OnProfileLoaded(object? sender, EventArgs e)
        {
            // Cloud profile was loaded - refresh UI to show updated XP/level
            Dispatcher.Invoke(() =>
            {
                App.Logger?.Information("Cloud profile loaded, refreshing UI");
                UpdateLevelDisplay();
                // Also update avatar in case level changed significantly
                _avatarTubeWindow?.UpdateAvatarForLevel(App.Settings.Current.PlayerLevel);

                // Start autonomy if it was enabled but couldn't start earlier (Patreon wasn't validated yet)
                var s = App.Settings?.Current;
                if (s != null && s.AutonomyModeEnabled && s.AutonomyConsentGiven
                    && App.Autonomy?.IsEnabled != true)
                {
                    var hasAccess = s.PatreonTier >= 1 || App.Patreon?.IsWhitelisted == true;
                    if (hasAccess)
                    {
                        App.Autonomy?.Start();
                        App.Logger?.Information("Started autonomy service after profile loaded");
                    }
                }
            });
        }

        private void OnSyncHealthChanged(object? sender, int failureCount)
        {
            Dispatcher.Invoke(() =>
            {
                if (failureCount >= 3)
                {
                    App.Logger?.Warning("[SyncHealth] {Count} consecutive sync failures — notifying user", failureCount);
                    // Show a subtle notification in the title bar area
                    Title = $"Conditioning Control Panel — Cloud sync issue";
                }
                else if (failureCount == 0)
                {
                    // Restore normal title
                    Title = "Conditioning Control Panel";
                }
            });
        }

        private void OnLevelUp(object? sender, int newLevel)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateLevelDisplay();
                // Show level up notification
                _trayIcon?.ShowNotification("Level Up!", $"You reached Level {newLevel}!", System.Windows.Forms.ToolTipIcon.Info);
                // Play level up sound
                PlayLevelUpSound();
                // Update avatar if level threshold reached (20, 50, 100)
                _avatarTubeWindow?.UpdateAvatarForLevel(newLevel);
            });
        }


        private void PlayLevelUpSound()
        {
            try
            {
                var soundPaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "lvup.mp3"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "lvlup.mp3"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "sounds", "lvlup.mp3"),
                };

                var soundPath = soundPaths.FirstOrDefault(File.Exists);
                if (soundPath == null)
                {
                    App.Logger?.Debug("Level up sound not found in any of: {Paths}", string.Join(", ", soundPaths));
                    return;
                }

                // Stop any previous level up sound still playing
                StopLevelUpSound();

                Task.Run(() =>
                {
                    try
                    {
                        var audioFile = new AudioFileReader(soundPath);
                        var outputDevice = new WaveOutEvent();
                        App.Audio?.ApplyPreferredDevice(outputDevice);

                        var masterVolume = App.Settings.Current.MasterVolume / 100f;
                        var curvedVolume = (float)Math.Pow(masterVolume, 1.5) * 0.2625f;
                        audioFile.Volume = Math.Max(0.01f, curvedVolume);

                        outputDevice.Init(audioFile);
                        outputDevice.PlaybackStopped += (s, e) =>
                        {
                            // Defer disposal — disposing inside PlaybackStopped causes
                            // "Handle is not initialized" when NAudio's internal cleanup
                            // races with our Dispose call.
                            Task.Run(() =>
                            {
                                try
                                {
                                    Thread.Sleep(50); // Let NAudio finish its internal cleanup
                                    outputDevice.Dispose();
                                    audioFile.Dispose();
                                    if (_levelUpSoundDevice == outputDevice)
                                    {
                                        _levelUpSoundDevice = null;
                                        _levelUpSoundFile = null;
                                    }
                                }
                                catch (Exception) { }
                            });
                        };

                        _levelUpSoundDevice = outputDevice;
                        _levelUpSoundFile = audioFile;
                        outputDevice.Play();

                        App.Logger?.Debug("Level up sound played from: {Path}", soundPath);
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning("Failed to play level up sound: {Error}", ex.Message);
                    }
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to play level up sound: {Error}", ex.Message);
            }
        }

        private void StopLevelUpSound()
        {
            try
            {
                _levelUpSoundDevice?.Stop();
                _levelUpSoundDevice?.Dispose();
                _levelUpSoundFile?.Dispose();
                _levelUpSoundDevice = null;
                _levelUpSoundFile = null;
            }
            catch { }
        }

        private void OnGlobalKeyPressed(Key key)
        {
            // Lockdown mode: block all key handling (panic key, etc.)
            if (App.Lockdown?.IsActive == true)
                return;

            // Track Alt+Tab for achievement (Player 2 Disconnected)
            if (key == Key.Tab && (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt)))
            {
                if (_isRunning)
                {
                    App.Achievements?.TrackAltTab();
                    App.Logger?.Debug("Alt+Tab detected during session");
                }
            }
            
            // Handle panic key capture mode
            if (_isCapturingPanicKey)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    App.Settings.Current.PanicKey = key.ToString();
                    _isCapturingPanicKey = false;
                    UpdatePanicKeyButton();
                    App.Logger?.Information("Panic key changed to: {Key}", key);
                });
                return;
            }
            
            // Check if panic key is enabled and pressed
            var settings = App.Settings.Current;
            if (settings.PanicKeyEnabled)
            {
                var panicKey = settings.PanicKey;
                if (key.ToString() == panicKey)
                {
                    Dispatcher.BeginInvoke(() => HandlePanicKeyPress());
                }
            }
        }

        private void HandlePanicKeyPress()
        {
            // A live Rabbit Hole descent owns the panic key: the chaos key hook pauses the
            // run (and a second press surfaces it). Without this hand-off a mid-run panic
            // fell into the "not running" branch below — where a second press EXITS the app.
            if (App.Chaos?.IsDescending == true) return;

            // Let the companion say a calm, persona-neutral safety line (highest priority,
            // bypasses the bark gate). Fired before the stop flow so it's not suppressed.
            App.Bark?.NotifyPanic();

            // Dismiss any open/pinned help popover so it never lingers over a panic.
            Controls.HelpPopover.CloseActive();

            // Stop standalone Lab minigames first — they run independently of
            // the main engine, so the rest of the panic flow won't touch them.
            App.BlinkTrainer?.Stop();

            var now = DateTime.Now;
            var timeSinceLastPress = (now - _lastPanicTime).TotalMilliseconds;
            
            // Reset counter if more than 2 seconds since last press
            if (timeSinceLastPress > 2000)
            {
                _panicPressCount = 0;
            }
            
            _panicPressCount++;
            _lastPanicTime = now;
            
            if (_isRunning)
            {
                // First press while running: stop engine, pause session if active
                App.Logger?.Information("Panic key pressed! Stopping engine...");

                // IMMEDIATELY kill ALL audio before anything else
                App.KillAllAudio();

                // Cancel any active autonomy pulses (restore original settings)
                App.Autonomy?.CancelActivePulses();

                // Track panic press for Relapse achievement (must be before stopping session)
                App.Achievements?.TrackPanicPressed();

                // Pause session if one is running (instead of stopping it)
                bool sessionWasPaused = false;
                if (_sessionEngine != null && _sessionEngine.IsRunning && !_sessionEngine.IsPaused)
                {
                    _sessionEngine.PauseSession();
                    sessionWasPaused = true;
                }

                // Remember if autonomy was running before we stop everything
                bool autonomyWasRunning = App.Autonomy?.IsEnabled == true;

                StopEngine();

                // Reset interaction queue to clear any pending queued items
                App.InteractionQueue?.ForceReset();

                // Restart autonomy if it was running — panic should skip the current action, not kill autonomy
                if (autonomyWasRunning && !sessionWasPaused)
                {
                    App.Autonomy?.Start();
                    App.Logger?.Information("Panic key: Restarted autonomy after skipping current action");
                }

                // Restore window - always show and bring to front
                Show();
                WindowState = WindowState.Normal;
                Activate();
                Topmost = true;  // Temporarily topmost to ensure it's visible
                Topmost = false; // Then disable topmost
                App.Overlay?.NotifyTopWindowClosed();
                ShowAvatarTube();

                if (sessionWasPaused)
                {
                    // Update pause button to show resume icon
                    if (TxtPauseIcon != null) TxtPauseIcon.Text = "▶";
                    if (BtnPauseSession != null) BtnPauseSession.ToolTip = Loc.Get("tooltip_resume_session");
                }
            }
            else if (_panicPressCount >= 2)
            {
                // Second press while stopped: exit application
                App.Logger?.Information("Double panic! Exiting application...");

                // IMMEDIATELY kill ALL audio before anything else
                App.KillAllAudio();

                // Stop session if one is paused before exiting
                if (_sessionEngine != null && _sessionEngine.IsRunning)
                {
                    _sessionEngine.StopSession(completed: false);
                }

                // CRITICAL: Force close all video windows SYNCHRONOUSLY before exit
                // LibVLC windows become orphaned if we exit without proper cleanup
                App.Video?.ForceCleanup(synchronous: true);
                BubbleCountWindow.ForceCloseAll();
                BubbleCountResultWindow.ForceCloseAll();

                // Give LibVLC a moment to release native resources
                Thread.Sleep(100);

                _exitRequested = true;
                SaveSettings();
                _keyboardHook?.Dispose();
                _trayIcon?.Dispose();
                _browser?.Dispose();
                Application.Current.Shutdown();
            }
        }

        private void UpdatePanicKeyButton()
        {
            if (BtnPanicKey != null)
            {
                var currentKey = App.Settings.Current.PanicKey;
                BtnPanicKey.Content = _isCapturingPanicKey ? "Press any key..." : $"🔑 {currentKey}";
            }
        }

        // ---- velvet-mosaic: internal wrappers called by popup feature UserControls ----
        // These delegate complex system-level operations (assets, panic key, offline mode,
        // no-panic) to the existing private handlers so the popup doesn't duplicate logic.

        internal void RequestPickAssetsFolder()
        {
            BtnPickAssetsFolder_Click(this, new RoutedEventArgs());
        }

        internal void RequestBeginPanicKeyCapture()
        {
            BtnPanicKey_Click(this, new RoutedEventArgs());
        }

        internal void RequestToggleOfflineMode(bool enable)
        {
            // Drive the existing handler via the legacy checkbox so the two-way sync logic
            // (UpdateOfflineModeUI, login button disable, etc.) runs exactly once.
            if (ChkOfflineMode == null) return;
            if ((ChkOfflineMode.IsChecked ?? false) == enable) return;
            ChkOfflineMode.IsChecked = enable;
        }

        internal void RequestToggleNoPanic(bool disablePanic)
        {
            if (ChkNoPanic == null) return;
            if ((ChkNoPanic.IsChecked ?? false) == disablePanic) return;
            ChkNoPanic.IsChecked = disablePanic;
        }

        /// <summary>
        /// Applies no-panic mode change directly (for use by feature popups).
        /// Returns true if the change was applied, false if cancelled.
        /// </summary>
        internal bool ApplyNoPanic(bool disablePanic, Window dialogOwner)
        {
            if (disablePanic)
            {
                var confirmed = WarningDialog.ShowDoubleWarning(dialogOwner,
                    "Disable Panic Key",
                    "• You will have NO emergency escape option\n" +
                    "• The ONLY way to exit will be the Exit button\n" +
                    "• Combined with Strict Lock, this is VERY restrictive\n" +
                    "• Make sure you know what you're doing!");

                if (!confirmed) return false;

                if (App.Settings.Current.KeywordTriggersEnabled != true)
                    _keyboardHook?.Stop();
                App.Settings.Current.PanicKeyEnabled = false;
                App.Settings?.Save();
                App.Logger?.Information("Keyboard hook stopped - panic key disabled");
            }
            else
            {
                _keyboardHook?.Start();
                App.Settings.Current.PanicKeyEnabled = true;
                App.Settings?.Save();
                App.Logger?.Information("Keyboard hook started - panic key enabled");
            }

            // Sync MainWindow checkbox without triggering handler
            _isLoading = true;
            ChkNoPanic.IsChecked = disablePanic;
            _isLoading = false;

            return true;
        }

        /// <summary>
        /// Applies offline mode change directly (for use by feature popups).
        /// Returns true if the change was applied, false if cancelled.
        /// </summary>
        internal bool ApplyOfflineMode(bool enable, Window dialogOwner)
        {
            if (enable)
            {
                if (string.IsNullOrWhiteSpace(App.Settings.Current.OfflineUsername))
                {
                    var dialog = new OfflineUsernameDialog();
                    dialog.Owner = dialogOwner;
                    dialog.Topmost = true;

                    if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Username))
                    {
                        App.Settings.Current.OfflineUsername = dialog.Username;
                    }
                    else
                    {
                        return false;
                    }
                }

                App.Settings.Current.OfflineMode = true;
                DisconnectNetworkServices();
                App.Logger?.Information("Offline mode enabled with username '{Username}'",
                    App.Settings.Current.OfflineUsername);
            }
            else
            {
                App.Settings.Current.OfflineMode = false;
                App.Logger?.Information("Offline mode disabled");
            }

            UpdateOfflineModeUI(enable);
            App.Settings.Save();

            // Sync MainWindow checkbox without triggering handler
            _isLoading = true;
            ChkOfflineMode.IsChecked = enable;
            _isLoading = false;

            return true;
        }

        /// <summary>
        /// Syncs the keyboard hook and MainWindow NoPanic checkbox after the setting changes externally.
        /// </summary>
        internal void SyncNoPanicState()
        {
            var panicEnabled = App.Settings.Current.PanicKeyEnabled;
            if (panicEnabled)
            {
                _keyboardHook?.Start();
                App.Logger?.Information("Keyboard hook started - panic key enabled");
            }
            else
            {
                if (App.Settings.Current.KeywordTriggersEnabled != true)
                    _keyboardHook?.Stop();
                App.Logger?.Information("Keyboard hook stopped - panic key disabled");
            }

            _isLoading = true;
            ChkNoPanic.IsChecked = !panicEnabled;
            _isLoading = false;
        }

        /// <summary>
        /// Syncs the MainWindow offline mode UI after the setting changes externally.
        /// </summary>
        internal void SyncOfflineModeState()
        {
            var isOffline = App.Settings.Current.OfflineMode;
            if (isOffline)
                DisconnectNetworkServices();
            UpdateOfflineModeUI(isOffline);

            _isLoading = true;
            ChkOfflineMode.IsChecked = isOffline;
            _isLoading = false;
        }

        internal bool RequestToggleWindowsStartup(bool enable)
        {
            // The legacy ChkWinStart hidden on MainWindow uses a Click handler that
            // doesn't fire on programmatic IsChecked changes — so just toggling the
            // checkbox here would silently skip StartupManager.SetStartupState and
            // the OS shortcut would never be created/removed. Drive the registration
            // ourselves and mirror the result onto the legacy checkbox for any code
            // that still reads it.
            if (ChkWinStart == null) return StartupManager.IsRegistered();
            if ((ChkWinStart.IsChecked ?? false) == enable && StartupManager.IsRegistered() == enable)
                return enable;

            if (!StartupManager.SetStartupState(enable))
            {
                MessageBox.Show(this,
                    Loc.Get("msg_failed_to_update_startup"),
                    Loc.Get("title_startup_error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                var actual = StartupManager.IsRegistered();
                _isLoading = true;
                try { ChkWinStart.IsChecked = actual; } finally { _isLoading = false; }
                App.Settings.Current.RunOnStartup = actual;
                App.Settings.Save();
                return actual;
            }

            _isLoading = true;
            try { ChkWinStart.IsChecked = enable; } finally { _isLoading = false; }
            App.Settings.Current.RunOnStartup = enable;
            App.Settings.Save();
            return enable;
        }

        private void LoadLogo()
        {
            try
            {
                // Use mod resource resolver for logo — allows mod overrides.
                // logo.png is the Bambi-branded wordmark; logo2.png is the neutral
                // "Conditioning Control Panel" wordmark used by CCP Default and Sissy.
                var useNeutralLogo = App.Mods?.IsCCPDefault == true
                                     || App.Settings?.Current?.IsSissyMode == true;
                var logoFile = useNeutralLogo ? "logo2.png" : "logo.png";
                var image = Services.ModResourceResolver.ResolveImage(logoFile);
                if (image != null)
                    ImgLogo.Source = image;
                App.Logger?.Debug("Logo loaded: {Logo}", logoFile);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to load logo: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Loads the takeover feature image based on current content mode.
        /// </summary>
        private void LoadTakeoverImage()
        {
            try
            {
                // Update mod-aware takeover labels. ImgTakeover and TxtTakeoverHeader
                // were removed when the Bambi feature image moved out of the Exclusives
                // page into BambiTakeoverTab — guard the legacy element references.
                var takeoverLabel = App.Mods?.GetTakeoverLabel() ?? "Bambi Takeover";
                if (TxtTakeoverLocked != null) TxtTakeoverLocked.Text = $"🤖 {takeoverLabel}";
                if (TxtTakeoverUnlocked != null) TxtTakeoverUnlocked.Text = $"🤖 {takeoverLabel}";
                if (BtnAutonomyStartStop != null)
                    BtnAutonomyStartStop.ToolTip = Loc.GetF("tooltip_start_stop_takeover", takeoverLabel);
                if (RunPatreonFeatures != null)
                    RunPatreonFeatures.Text = Loc.GetF("label_patreon_features", takeoverLabel);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to load takeover image: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Refreshes UI elements that need manual updates when theme changes.
        /// Updates Application.Current.Resources Color and Brush entries so all
        /// DynamicResource bindings across the app auto-update.
        /// Also updates named elements that use direct property assignment.
        /// </summary>
        private void RefreshThemeAwareElements()
        {
            try
            {
                var accentHex = App.Mods?.GetAccentColorHex() ?? "#FF69B4";
                var darkHex = App.Mods?.GetAccentDarkColorHex() ?? "#FF1493";
                var lightHex = App.Mods?.GetAccentLightColorHex() ?? "#FF8FAF";
                var secondaryHex = App.Mods?.GetSecondaryColorHex() ?? "#9B59B6";

                var accent = (Color)ColorConverter.ConvertFromString(accentHex);
                var dark = (Color)ColorConverter.ConvertFromString(darkHex);
                var light = (Color)ColorConverter.ConvertFromString(lightHex);
                var secondary = (Color)ColorConverter.ConvertFromString(secondaryHex);
                var transparent30 = Color.FromArgb(0x30, accent.R, accent.G, accent.B);
                var transparent20 = Color.FromArgb(0x20, accent.R, accent.G, accent.B);
                var accentPressed = Color.FromArgb(0xFF,
                    (byte)Math.Max(0, accent.R - 30),
                    (byte)Math.Max(0, accent.G - 30),
                    (byte)Math.Max(0, accent.B - 30));

                // === BACKGROUND COLORS (mod-customizable) ===
                var bgHex = App.Mods?.GetBackgroundColorHex() ?? "#1A1A2E";
                var panelHex = App.Mods?.GetPanelColorHex() ?? "#252542";
                var surfaceHex = App.Mods?.GetSurfaceColorHex() ?? "#1E1E3A";

                var bgColor = (Color)ColorConverter.ConvertFromString(bgHex);
                var panelColor = (Color)ColorConverter.ConvertFromString(panelHex);
                var surfaceColor = (Color)ColorConverter.ConvertFromString(surfaceHex);

                // Auto-computed derivatives
                var panelAccentColor = LightenColor(panelColor, 0.15);
                var panelAccentHoverColor = LightenColor(panelColor, 0.25);
                var previewBgColor = DarkenColor(bgColor, 0.15);
                var panelBgTransparent = Color.FromArgb(0xB0, panelColor.R, panelColor.G, panelColor.B);

                var res = Application.Current.Resources;

                // Update background Color resources
                res["DarkerBg"] = bgColor;
                res["PanelBg"] = panelColor;
                res["SurfaceBg"] = surfaceColor;
                res["PanelAccent"] = panelAccentColor;
                res["PanelAccentHover"] = panelAccentHoverColor;
                res["PreviewBg"] = previewBgColor;
                res["PanelBgTransparent"] = panelBgTransparent;

                // Update background Brush resources
                res["DarkerBgBrush"] = new SolidColorBrush(bgColor);
                res["PanelBgBrush"] = new SolidColorBrush(panelColor);
                res["SurfaceBgBrush"] = new SolidColorBrush(surfaceColor);
                res["PanelAccentBrush"] = new SolidColorBrush(panelAccentColor);
                res["PanelAccentHoverBrush"] = new SolidColorBrush(panelAccentHoverColor);
                res["PreviewBgBrush"] = new SolidColorBrush(previewBgColor);
                res["PanelBgTransparentBrush"] = new SolidColorBrush(panelBgTransparent);

                // Accent-tinted dark backgrounds: blend accent onto mod's background color
                byte baseR = bgColor.R, baseG = bgColor.G, baseB = bgColor.B;
                var tintedBg = Color.FromRgb(
                    (byte)(baseR + (accent.R - baseR) * 0.15),
                    (byte)(baseG + (accent.G - baseG) * 0.15),
                    (byte)(baseB + (accent.B - baseB) * 0.15));
                var tintedBgHover = Color.FromRgb(
                    (byte)(baseR + (accent.R - baseR) * 0.20),
                    (byte)(baseG + (accent.G - baseG) * 0.20),
                    (byte)(baseB + (accent.B - baseB) * 0.20));
                var midGradient = Color.FromRgb(
                    (byte)(baseR + (accent.R - baseR) * 0.10),
                    (byte)(baseG + (accent.G - baseG) * 0.10),
                    (byte)(baseB + (accent.B - baseB) * 0.10));

                var transparent40 = Color.FromArgb(0x40, accent.R, accent.G, accent.B);
                var transparent50 = Color.FromArgb(0x50, accent.R, accent.G, accent.B);

                // === UPDATE COLOR RESOURCES (drives DynamicResource brushes in Brushes.xaml) ===
                res["PinkColor"] = accent;
                res["DarkPink"] = dark;
                res["PinkButtonHovered"] = light;
                res["TransparentPink"] = transparent30;
                res["TransparentPink20"] = transparent20;
                res["TransparentPink40"] = transparent40;
                res["TransparentPink50"] = transparent50;
                res["AccentPressed"] = accentPressed;
                res["PatreonPurple"] = secondary;
                res["AccentTintedBg"] = tintedBg;
                res["AccentTintedBgHover"] = tintedBgHover;
                res["AccentMidGradient"] = midGradient;

                // === ALSO UPDATE BRUSH RESOURCES (in case any are frozen from initial load) ===
                res["PinkBrush"] = new SolidColorBrush(accent);
                res["DarkPinkBrush"] = new SolidColorBrush(dark);
                res["PinkButtonHoveredBrush"] = new SolidColorBrush(light);
                res["TransparentPinkBrush"] = new SolidColorBrush(transparent30);
                res["TransparentPink20Brush"] = new SolidColorBrush(transparent20);
                res["TransparentPink40Brush"] = new SolidColorBrush(transparent40);
                res["TransparentPink50Brush"] = new SolidColorBrush(transparent50);
                res["AccentPressedBrush"] = new SolidColorBrush(accentPressed);
                res["PatreonPurpleBrush"] = new SolidColorBrush(secondary);
                res["SecondaryBrush"] = new SolidColorBrush(secondary);
                res["AccentTintedBgBrush"] = new SolidColorBrush(tintedBg);
                res["AccentTintedBgHoverBrush"] = new SolidColorBrush(tintedBgHover);
                res["AccentMidGradientBrush"] = new SolidColorBrush(midGradient);

                // === v6 BRAND GRADIENT — anchor swap ===
                // CCP Default activates the static BrandGradient at the four brand anchors (logo, START,
                // XP bar, primary nav active). Other mods render solid SolidColorBrush(accent) so their
                // anchor pixels stay byte-identical to pre-v6 state.
                if (App.Mods?.IsCCPDefault == true && TryFindResource("BrandGradient") is Brush brandGradient)
                    res["AccentGradientBrush"] = brandGradient;
                else
                    res["AccentGradientBrush"] = new SolidColorBrush(accent);

                // === TITLE BAR (most visible — direct assignment for immediate update) ===
                var accentBrush = new SolidColorBrush(accent);
                if (TitleBarBorder != null)
                    TitleBarBorder.Background = accentBrush;

                // === HEADER AREA ===
                if (TxtPlayerTitle != null)
                {
                    TxtPlayerTitle.Foreground = accentBrush;
                    if (TxtPlayerTitle.Effect is System.Windows.Media.Effects.DropShadowEffect glow)
                        glow.Color = accent;
                }
                if (TxtHeaderVersion != null)
                    TxtHeaderVersion.Foreground = accentBrush;

                // === XP/LEVEL DISPLAY ===
                if (TxtLevelLabel != null)
                    TxtLevelLabel.Foreground = accentBrush;
                if (XPBar != null)
                {
                    // Anchor 3: CCP Default gets BrandGradient, every other mod gets the solid accent it always had.
                    XPBar.Background = (Brush)res["AccentGradientBrush"];
                }

                // Logo frame stays near-transparent for every mod. The Phase 3 plan put the
                // BrandGradient behind the logo as one of the four anchors, but in practice it
                // read as a glaring colored halo around the wordmark instead of a brand anchor.
                // Gradient now lives only at the START button, XP bar, and active primary nav tab.
                if (LogoBrandFrame != null)
                    LogoBrandFrame.Background = new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0));

                // === BANNER AREA ===
                if (TxtBannerPrimary != null)
                    TxtBannerPrimary.Foreground = accentBrush;
                if (TxtBannerSecondary != null)
                    TxtBannerSecondary.Foreground = accentBrush;
                if (TxtBannerTertiary != null)
                    TxtBannerTertiary.Foreground = accentBrush;

                // Mod selector ComboBox repopulates itself in InitializeModSelector — no per-element refresh here.

                App.Logger?.Debug("Theme-aware UI elements refreshed for mod {ModId}", App.Mods?.ActiveModId);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to refresh some theme-aware elements");
            }
        }

        private static Color LightenColor(Color c, double amount)
        {
            return Color.FromRgb(
                (byte)Math.Min(255, c.R + (255 - c.R) * amount),
                (byte)Math.Min(255, c.G + (255 - c.G) * amount),
                (byte)Math.Min(255, c.B + (255 - c.B) * amount));
        }

        private static Color DarkenColor(Color c, double amount)
        {
            return Color.FromRgb(
                (byte)Math.Max(0, c.R * (1 - amount)),
                (byte)Math.Max(0, c.G * (1 - amount)),
                (byte)Math.Max(0, c.B * (1 - amount)));
        }

        /// <summary>
        /// Rebuilds the top-bar mod-switcher ComboBox and selects the active mod.
        /// Order: CCP Default → Bambi Sleep → Sissy Hypno → Dronification → user mods (alphabetical).
        /// </summary>
        private void InitializeModSelector()
        {
            _suppressModSelectorChange = true;
            try
            {
                AvailableMods.Clear();
                if (App.Mods != null)
                {
                    // Stock mods in a fixed canonical order.
                    var stockOrder = new[]
                    {
                        BuiltInMods.CCPDefaultId,
                        BuiltInMods.BambiSleepId,
                        BuiltInMods.SissyHypnoId,
                        BuiltInMods.DronificationId,
                        BuiltInMods.LockedId,
                    };
                    foreach (var id in stockOrder)
                    {
                        if (App.Mods.InstalledMods.TryGetValue(id, out var mod))
                            AvailableMods.Add(BuildSelectorItem(mod));
                    }
                    // User-installed mods after stock, alphabetical.
                    foreach (var mod in App.Mods.InstalledMods.Values
                        .Where(m => !m.IsBuiltIn)
                        .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        AvailableMods.Add(BuildSelectorItem(mod));
                    }

                    if (ModSelectorCombo != null)
                        ModSelectorCombo.SelectedValue = App.Mods.ActiveModId;
                }
            }
            finally
            {
                _suppressModSelectorChange = false;
            }

            // Hide BambiCloud option if mod doesn't want it
            var showBambiCloud = App.Mods?.ShowBambiCloudOption() ?? true;
            RbBambiCloud.Visibility = showBambiCloud ? Visibility.Visible : Visibility.Collapsed;

            if (!showBambiCloud)
                RbHypnoTube.IsChecked = true;

            RefreshBrowserLoadingText();
        }

        private static ModSelectorItem BuildSelectorItem(ModPackage mod)
        {
            Brush accent;
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(mod.Manifest.Theme?.AccentColor ?? "#E84393");
                accent = new SolidColorBrush(color);
            }
            catch
            {
                accent = new SolidColorBrush(Color.FromRgb(0xE8, 0x43, 0x93));
            }
            return new ModSelectorItem(mod.Id, mod.Name, accent);
        }

        private void RefreshBrowserLoadingText()
        {
            if (BrowserLoadingText == null) return;
            var showBambiCloud = App.Mods?.ShowBambiCloudOption() ?? false;
            var siteName = showBambiCloud
                ? "BambiCloud"
                : (App.Mods?.ActiveMod.Manifest.Browser?.SiteName ?? "HypnoTube");
            BrowserLoadingText.Text = $"🌐 Click to connect to {siteName}";
        }

        /// <summary>
        /// Loads feature images from mod resources (if overrides exist) or embedded resources.
        /// </summary>
        private void LoadFeatureImages()
        {
            try
            {
                // Dashboard feature cards (velvet mosaic)
                var cardMap = new (string resourcePath, Features.FeatureCard? card)[]
                {
                    ("features/flash.png", CardFlash),
                    ("features/mandatory_videos.png", CardVideo),
                    ("features/subliminal.png", CardSubliminal),
                    ("features/spiral_overlay.png", CardSpiral),
                    ("features/Pink_filter.png", CardPinkFilter),
                    ("features/Bubble_pop.png", CardBubblePop),
                    ("features/Phrase_Lock.png", CardLockCard),
                    ("features/bouncing_text.png", CardBouncingText),
                    ("features/Mind_Wipers.png", CardMindWipe),
                    ("features/Bubble_count.png", CardBubbleCount),
                };
                foreach (var (path, card) in cardMap)
                {
                    if (card == null) continue;
                    var image = ModResourceResolver.ResolveImage(path);
                    if (image != null)
                        card.Icon = image;
                }

                // Legacy progression tab rectangles
                var featureMap = new (string resourcePath, System.Windows.Shapes.Rectangle? rect)[]
                {
                    ("features/spiral_overlay.png", SpiralFeatureImage),
                    ("features/Pink_filter.png", PinkFilterFeatureImage),
                    ("features/Bubble_pop.png", BubblePopFeatureImage),
                    ("features/Phrase_Lock.png", LockCardFeatureImage),
                    ("features/Bubble_count.png", BubbleCountFeatureImage),
                    ("features/bouncing_text.png", BouncingTextFeatureImage),
                    ("features/brain_drain.png", BrainDrainFeatureImage),
                    ("features/Mind_Wipers.png", MindWipeFeatureImage),
                };

                foreach (var (path, rect) in featureMap)
                {
                    if (rect == null) continue;
                    var image = ModResourceResolver.ResolveImage(path);
                    if (image != null)
                    {
                        rect.Fill = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
                    }
                }

                // Image elements in description cards + Video Haptic Sync card.
                // Takeover image is mod-specific: BambiSleep uses "bambi takeover.png",
                // other mods use the generic "takeover.png" (or override via their resources/ folder).
                var takeoverPath = App.Mods?.ActiveModId == Models.BuiltInMods.BambiSleepId
                    ? "features/bambi takeover.png"
                    : "features/takeover.png";
                var descImageMap = new (string resourcePath, System.Windows.Controls.Image? img)[]
                {
                    (takeoverPath, ImgBambiTakeoverDesc),
                    ("features/vibe.png", ImgHapticsVibeDesc),
                    ("features/vibe.png", ImgVideoHapticSync),
                    // These three render via a hardcoded pack:// URI in XAML, so without a
                    // code override they'd always show the base (embedded) art, never a mod's.
                    ("features/awareness.png", ImgAwarenessFeature),
                    ("features/remote_control.png", ImgRemoteControlFeature),
                    ("features/blink_trainer.png", ImgBlinkTrainerFeature),
                };
                foreach (var (path, img) in descImageMap)
                {
                    if (img == null) continue;
                    var resolved = ModResourceResolver.ResolveImage(path);
                    if (resolved != null)
                        img.Source = resolved;
                }

                // Lab hero headers (mod-sensitive): drone-mode ships green versions under
                // resources/features/lab_*_hero.png; the embedded pink ones are the fallback.
                var labHeroMap = new (string resourcePath, ImageBrush? brush)[]
                {
                    ("features/lab_quiz_hero.png", LabQuizHeroBrush),
                    ("features/lab_aimemory_hero.png", LabAiMemoryHeroBrush),
                    ("features/lab_gaze_hero.png", LabGazeHeroBrush),
                    ("features/lab_focusgaze_hero.png", LabFocusHeroBrush),
                };
                foreach (var (path, brush) in labHeroMap)
                {
                    if (brush == null) continue;
                    var image = ModResourceResolver.ResolveImage(path);
                    if (image != null)
                        brush.ImageSource = image;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to load some feature images");
            }
        }

        private void BtnManageMods_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var dialog = new ModManagerDialog { Owner = this };
            dialog.ShowDialog();

            if (dialog.ModWasChanged)
            {
                ApplyActiveModChange();
            }
        }

        private void ModSelectorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading || _suppressModSelectorChange) return;
            if (ModSelectorCombo?.SelectedValue is not string newModId) return;
            if (App.Mods == null || App.Mods.ActiveModId == newModId) return;

            App.Mods.ActivateMod(newModId);
            ApplyActiveModChange();
        }

        /// <summary>
        /// Centralized refresh of mod-aware UI after the active mod changes.
        /// Called by both the top-bar ComboBox and the Manage Mods dialog return path.
        /// </summary>
        private void ApplyActiveModChange()
        {
            if (App.Mods == null) return;

            App.Settings.Current.ActiveModId = App.Mods.ActiveModId;
            App.Settings.Current.ModChosen = true;
            App.Settings.Save();

            InitializeModSelector();
            LoadLogo();
            LoadTakeoverImage();
            LoadFeatureImages();
            RefreshThemeAwareElements();
            PopulateAchievementGrid();
            DrawSkillTree();

            var showBambiCloud = App.Mods.ShowBambiCloudOption();
            RbBambiCloud.Visibility = showBambiCloud ? Visibility.Visible : Visibility.Collapsed;
            if (!showBambiCloud)
            {
                RbHypnoTube.IsChecked = true;
                if (_browser != null && _browserInitialized)
                {
                    var url = App.Mods.GetDefaultBrowserUrl();
                    _browser.Navigate(url);
                }
            }

            RefreshHypnotubeLinksUI();
            _avatarTubeWindow?.UpdateQuickMenuState();

            App.Logger?.Information("Mod changed to {ModId}", App.Mods.ActiveModId);
        }

        // Live name+URL rows in the mod-aware video link pool editor.
        private readonly List<(TextBox NameBox, TextBox UrlBox)> _videoLinkRows = new();

        private void RefreshHypnotubeLinksUI()
        {
            if (TxtHypnotubeModeLabel != null)
                TxtHypnotubeModeLabel.Text = App.Settings?.Current?.ContentModeDisplay ?? "CCP Default";

            if (VideoLinkPoolPanel == null) return;

            // Rebuild the rows from the active mod's pool (user override, else shipped links).
            _videoLinkRows.Clear();
            VideoLinkPoolPanel.Children.Clear();
            if (TxtNoVideoLinks != null) VideoLinkPoolPanel.Children.Add(TxtNoVideoLinks);

            var links = App.Mods?.GetVideoLinks();
            if (links != null)
                foreach (var kvp in links)
                {
                    // Drop non-video "browse" links (e.g. a stray /videos/ listing) — they're not
                    // videos, so they don't belong in the pool and won't be re-saved.
                    if (IsListingUrl(kvp.Value)) continue;
                    AddVideoLinkRow(kvp.Key, kvp.Value);
                }

            UpdateNoVideoLinksPlaceholder();
        }

        /// <summary>
        /// True for a HypnoTube browse/listing page (e.g. /videos/ or the site root) rather than a
        /// specific video. Deliberately narrow: a /video/... page — even a typo'd one missing .html —
        /// is still a video and stays editable.
        /// </summary>
        private static bool IsListingUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            var host = uri.Host.ToLowerInvariant();
            if (host != "hypnotube.com" && !host.EndsWith(".hypnotube.com", StringComparison.Ordinal))
                return false;
            var path = uri.AbsolutePath.TrimEnd('/').ToLowerInvariant();
            return path == "" || path == "/videos" || path == "/video";
        }

        private void BtnAddVideoLink_Click(object sender, RoutedEventArgs e)
        {
            var row = AddVideoLinkRow("", "");
            UpdateNoVideoLinksPlaceholder();
            // Drop the cursor straight into the URL field — paste-and-go is the common case.
            row.UrlBox.Focus();
        }

        /// <summary>
        /// Builds one editable name + URL row (with a bin button) and registers it. Edits persist
        /// on focus loss; the bin removes just that row. Mirrors ModCreatorWindow.AddVideoLinkRow.
        /// </summary>
        private (TextBox NameBox, TextBox UrlBox) AddVideoLinkRow(string name, string url)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameBox = MakePoolTextBox(name, isUrl: false);
            nameBox.ToolTip = Loc.Get("tooltip_video_link_name_optional");
            Grid.SetColumn(nameBox, 0);
            row.Children.Add(nameBox);

            var urlBox = MakePoolTextBox(url, isUrl: true);
            Grid.SetColumn(urlBox, 2);
            row.Children.Add(urlBox);

            // Preview: open the link externally so the user can check it (HTTPS only).
            var openBtn = new Button
            {
                Content = "", // Segoe MDL2 'OpenInNewWindow'
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 12,
                Width = 28,
                Height = 28,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 200, 255)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = Loc.Get("tooltip_preview_video_link")
            };
            openBtn.Content = ""; // Segoe MDL2 'OpenInNewWindow' (set via escape so the glyph can't be stripped)
            openBtn.Click += (_, _) =>
            {
                var u = urlBox.Text?.Trim() ?? "";
                if (Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                {
                    try { Process.Start(new ProcessStartInfo(u) { UseShellExecute = true }); }
                    catch (Exception ex) { App.Logger?.Warning(ex, "Failed to open video link preview: {Url}", u); }
                }
            };
            Grid.SetColumn(openBtn, 3);
            row.Children.Add(openBtn);

            var removeBtn = new Button
            {
                Content = "", // Segoe MDL2 'Delete' (trash can)
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 13,
                Width = 28,
                Height = 28,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = Loc.Get("tooltip_remove_video_link")
            };
            var entry = (NameBox: nameBox, UrlBox: urlBox);
            removeBtn.Click += (_, _) =>
            {
                VideoLinkPoolPanel.Children.Remove(row);
                _videoLinkRows.Remove(entry);
                PersistVideoLinks();
                UpdateNoVideoLinksPlaceholder();
            };
            Grid.SetColumn(removeBtn, 4);
            row.Children.Add(removeBtn);

            // Host validation: grey the row and flip the preview glyph to a warning when the URL is
            // present but not a usable absolute http(s) link (such rows are dropped on save). The
            // preview button doubles as the status indicator, so there's no extra column.
            void UpdateRowValidity()
            {
                var u = urlBox.Text?.Trim() ?? "";
                bool empty = string.IsNullOrWhiteSpace(u);
                bool valid = !empty && Uri.TryCreate(u, UriKind.Absolute, out var uri)
                             && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
                bool invalid = !empty && !valid;

                row.Opacity = invalid ? 0.55 : 1.0;
                urlBox.BorderBrush = invalid
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0x8B, 0x5A))            // warning orange
                    : new SolidColorBrush(Color.FromArgb(0x55, 0x80, 0x80, 0x80));    // default
                openBtn.FontFamily = invalid ? new FontFamily("Segoe UI Symbol") : new FontFamily("Segoe MDL2 Assets");
                openBtn.Content = invalid ? "⚠" : "";    // Warning sign U+26A0 : OpenInNewWindow U+E8A7
                openBtn.Foreground = invalid
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0x8B, 0x5A))
                    : new SolidColorBrush(Color.FromRgb(120, 200, 255));
                openBtn.ToolTip = invalid
                    ? "Not a valid http(s) link — this row won't be saved."
                    : Loc.Get("tooltip_preview_video_link");
            }
            urlBox.TextChanged += (_, _) => UpdateRowValidity();

            nameBox.LostFocus += (_, _) => PersistVideoLinks();
            urlBox.LostFocus += (_, _) => PersistVideoLinks();

            _videoLinkRows.Add(entry);
            VideoLinkPoolPanel.Children.Add(row);
            UpdateRowValidity();
            return entry;
        }

        private TextBox MakePoolTextBox(string text, bool isUrl)
        {
            return new TextBox
            {
                Text = text ?? "",
                MaxLength = isUrl ? 500 : 200,
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E)),
                Foreground = isUrl ? new SolidColorBrush(Color.FromRgb(120, 200, 255)) : Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x80, 0x80, 0x80)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 4, 6, 4),
                VerticalContentAlignment = VerticalAlignment.Center,
                FontSize = 12
            };
        }

        /// <summary>
        /// Collects the current rows into a name→URL pool and saves it as the active mod's
        /// override. Blank names are auto-titled from the URL (HtUrlHelper.DeriveTitleFromUrl);
        /// blank/invalid URLs are dropped; duplicate names are made unique.
        /// </summary>
        private void PersistVideoLinks()
        {
            var pool = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (nameBox, urlBox) in _videoLinkRows)
            {
                var url = urlBox.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(url)) continue; // a row with no URL isn't a link yet
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    continue;

                var name = nameBox.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(name))
                    name = HtUrlHelper.DeriveTitleFromUrl(url);

                var unique = name;
                int n = 2;
                while (pool.ContainsKey(unique) && !string.Equals(pool[unique], url, StringComparison.OrdinalIgnoreCase))
                    unique = $"{name} ({n++})";
                pool[unique] = url;
            }

            App.Mods?.SetUserVideoLinks(pool);
            App.Settings?.Save();
            // Refresh the clickable-link lookup so the companion links these titles immediately.
            AvatarTubeWindow.ReloadVideoLinks();
        }

        private void UpdateNoVideoLinksPlaceholder()
        {
            if (TxtNoVideoLinks != null)
                TxtNoVideoLinks.Visibility = _videoLinkRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Returns a mode-appropriate image path for quests.
        /// Supports both local cached images and embedded resources.
        /// Swaps Bambi Sleep specific images when in Sissy Hypno mode.
        /// </summary>
        private string GetModeAwareQuestImagePath(Models.QuestDefinition quest)
        {
            // Use EffectiveImagePath which prefers cached remote images over embedded
            var imagePath = quest.EffectiveImagePath;

            if (string.IsNullOrEmpty(imagePath))
                return imagePath;

            // For embedded resources, check if mod has overrides or if mode-specific swap is needed
            if (imagePath.StartsWith("pack://"))
            {
                // Extract relative path from pack URI
                var prefix = "pack://application:,,,/Resources/";
                if (imagePath.StartsWith(prefix))
                {
                    var relativePath = imagePath.Substring(prefix.Length);
                    if (Services.ModResourceResolver.HasModOverride(relativePath))
                        return Services.ModResourceResolver.ResolveUri(relativePath);
                }

                // Legacy mode-specific swaps for built-in mods
                if (App.Settings?.Current?.IsSissyMode == true)
                {
                    if (imagePath.Contains("logo.png"))
                        return "pack://application:,,,/Resources/logo2.png";
                    if (imagePath.Contains("bambi takeover.png"))
                        return "pack://application:,,,/Resources/features/mandatory_videos.png";
                }
                // CCP Default uses the same neutral wordmark as Sissy.
                if (App.Mods?.IsCCPDefault == true)
                {
                    if (imagePath.Contains("logo.png"))
                        return "pack://application:,,,/Resources/logo2.png";
                }
            }

            return imagePath;
        }

        /// <summary>
        /// Load an image from either a local file path or pack:// URI
        /// </summary>
        private System.Windows.Media.Imaging.BitmapImage? LoadQuestImage(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath))
                return null;

            try
            {
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();

                if (imagePath.StartsWith("pack://"))
                {
                    // Embedded resource
                    bitmap.UriSource = new Uri(imagePath);
                }
                else if (System.IO.File.Exists(imagePath))
                {
                    // Local file (cached remote image)
                    bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                    bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                }
                else
                {
                    return null;
                }

                bitmap.EndInit();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private void CenterOnPrimaryScreen()
        {
            // Get the primary screen
            var primaryScreen = System.Windows.Forms.Screen.PrimaryScreen;
            if (primaryScreen == null) return;
            
            // Get DPI scaling
            var dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            if (dpiScale == 0) dpiScale = 1;
            
            // Calculate center position on primary screen
            var screenWidth = primaryScreen.WorkingArea.Width / dpiScale;
            var screenHeight = primaryScreen.WorkingArea.Height / dpiScale;
            var screenLeft = primaryScreen.WorkingArea.Left / dpiScale;
            var screenTop = primaryScreen.WorkingArea.Top / dpiScale;
            
            Left = screenLeft + (screenWidth - Width) / 2;
            Top = screenTop + (screenHeight - Height) / 2;
        }

        #region Custom Title Bar

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // Double-click to maximize/restore
                BtnMaximize_Click(sender, e);
            }
            else
            {
                // Drag window
                if (WindowState == WindowState.Maximized)
                {
                    // Restore before dragging from maximized
                    var point = PointToScreen(e.GetPosition(this));
                    WindowState = WindowState.Normal;
                    Left = point.X - (Width / 2);
                    Top = point.Y - 15;
                }
                DragMove();
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("minimize"); } catch { }
            // Hide avatar tube BEFORE minimizing to prevent visual artifacts
            HideAvatarTube();
            WindowState = WindowState.Minimized;
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                BtnMaximize.Content = "☐";

                // Re-attach avatar if it was attached before maximizing
                if (_avatarWasAttachedBeforeMaximize && _avatarTubeWindow != null && _avatarTubeWindow.IsDetached)
                {
                    _avatarTubeWindow.Attach();
                    _avatarWasAttachedBeforeMaximize = false;
                }
            }
            else
            {
                // Detach avatar before maximizing (it would be in wrong position otherwise)
                if (_avatarTubeWindow != null && !_avatarTubeWindow.IsDetached)
                {
                    _avatarWasAttachedBeforeMaximize = true;
                    _avatarTubeWindow.Detach();
                }

                WindowState = WindowState.Maximized;
                BtnMaximize.Content = "❐";
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("close"); } catch { }
            Close();
        }

        #endregion

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Hook window messages to intercept minimize BEFORE it happens
            var hwnd = new WindowInteropHelper(this).Handle;
            var hwndSource = HwndSource.FromHwnd(hwnd);
            hwndSource?.AddHook(WndProc);

            // Browser-header Webcam Tracking toggle: keep label/tooltip in sync
            // with the tracking service so manual starts (Lab tab, Blink Trainer,
            // etc.) reflect on this button too.
            EnsureBrowserWebcamStateSubscribed();

            // Wire the in-app notification surface. Anything App.Notifications.Show()'d
            // before this point is replayed on attach.
            App.Notifications?.AttachHost(NotificationHost);

            // Phase 1.6: legacy calibration prompt. Pre-multi-monitor-hotfix
            // saves have MonitorBounds without DeviceName, so the runtime
            // can't pin gaze content to the calibrated screen. Show a
            // dismissable sticky toast suggesting recalibration. Placeholder
            // copy — voice-pass at ship time.
            try
            {
                var cal = App.Webcam?.Calibration;
                if (cal?.MonitorBounds != null && string.IsNullOrEmpty(cal.MonitorBounds.DeviceName))
                {
                    App.Notifications?.ShowSticky(
                        "recalibrate-multimonitor",
                        "Your calibration needs updating for multi-monitor support.",
                        Services.NotificationType.Warning,
                        actionLabel: "Recalibrate",
                        action: () =>
                        {
                            try
                            {
                                WebcamCalibrationWindow.ShowDialogWithRecalibrate(this);
                            }
                            catch (Exception ex)
                            {
                                App.Logger?.Warning(ex, "Recalibrate toast: failed to open calibration window");
                            }
                        });
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Recalibrate-suggest check failed: {Error}", ex.Message);
            }

            // v5.9.8 Blink Trainer flagship sticky. Fires once for users who
            // updated to v5.9.8 and haven't yet visited the new Exclusives →
            // Blink Trainer page. Suppression is belt-and-suspenders:
            //   - ShowSticky no-ops if its key is in DismissedNotificationKeys
            //   - the if-check below short-circuits when HasSeenBlinkTrainerFlagship is true
            //   - the action handler ALSO adds the key to DismissedNotificationKeys
            //     in case HasSeen somehow doesn't persist.
            try
            {
                const string flagshipKey = "blink-trainer-flagship-v5.9.8";
                if (App.Settings?.Current?.HasSeenBlinkTrainerFlagship == false)
                {
                    App.Notifications?.ShowSticky(
                        flagshipKey,
                        Localization.Loc.Get("blink_trainer_flagship_toast"),
                        Services.NotificationType.Info,
                        actionLabel: Localization.Loc.Get("blink_trainer_flagship_toast_action"),
                        action: () =>
                        {
                            try
                            {
                                // Belt-and-suspenders dedupe: add the key to
                                // DismissedNotificationKeys so the toast can't
                                // refire next launch even if HasSeen flag fails
                                // to persist.
                                var s = App.Settings?.Current;
                                if (s != null && !s.DismissedNotificationKeys.Contains(flagshipKey))
                                {
                                    s.DismissedNotificationKeys.Add(flagshipKey);
                                    App.Settings?.Save();
                                }
                                ShowTab("blinktrainer");
                            }
                            catch (Exception ex)
                            {
                                App.Logger?.Warning(ex, "Blink Trainer toast action: failed");
                            }
                        });
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Blink Trainer flagship sticky: failed");
            }

            // Catalogue submission feedback: poll for any pending Deeper
            // submissions that have been accepted/published since last launch and
            // surface a one-time notification. Host is attached above, so a
            // sticky toast shows even though the Deeper tab hasn't been opened.
            _ = CheckDeeperSubmissionStatusesAsync(force: true);


            // Enable Windows 11 rounded corners
            try
            {
                int preference = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
            }
            catch
            {
                // Silently fail on Windows 10 or earlier - they don't support this API
            }

            // Re-center after load in case DPI wasn't available in constructor
            CenterOnPrimaryScreen();

            // Update panic key button
            UpdatePanicKeyButton();

            // Title-bar camera-active indicator
            WireWebcamActivePill();

            // Movable loading splash shown while the webcam engine starts up
            InstallWebcamLoadingSplash();

            // Global 6-blink → stop-everything + recalibrate gesture
            WireRapidBlinkRecalibrateShortcut();
            SyncBlinkRecalToggles(App.Settings?.Current?.BlinkRecalibrateShortcutEnabled ?? true);

            // Load custom sessions from disk (so they persist across restarts)
            if (_sessionManager == null)
                InitializeSessionManager();

            // Initialize hypnotube links UI
            RefreshHypnotubeLinksUI();

            // Initialize Deeper "Enhance if possible" toggle from settings
            try
            {
                if (ToggleEnhanceIfPossible != null)
                    ToggleEnhanceIfPossible.IsChecked = App.Settings?.Current?.BrowserEnhanceIfPossible ?? true;
            }
            catch { }

            // Apply mod-aware feature names to static XAML labels
            ApplyModFeatureNames();
            if (App.Mods != null)
            {
                App.Mods.ModChanged += (_, _) => Dispatcher.Invoke(ApplyModFeatureNames);
                // Re-render the Remote Control QR code in the new mod's accent color
                App.Mods.ModChanged += (_, _) => Dispatcher.Invoke(() =>
                {
                    var code = App.RemoteControl?.SessionCode;
                    if (!string.IsNullOrEmpty(code))
                        RefreshRemoteQrCode(BuildRemotePairingUrl(code));
                });
                // Re-load mod-aware feature images (description card images, VHS card)
                App.Mods.ModChanged += (_, _) => Dispatcher.Invoke(LoadFeatureImages);
            }

            // Re-apply code-behind strings when language changes (section headers, feature names, etc.)
            LocalizationManager.Instance.LanguageChanged += (_, _) => Dispatcher.Invoke(ApplyModFeatureNames);

            // Initialize language selector
            InitializeLanguageSelector();

            // Initialize quick login UI
            UpdateQuickLoginUI();

            // Load past quizzes list
            RefreshPastQuizzes();

            // Initialize wallpaper override from settings
            if (ChkWallpaperEnabled != null && App.Settings.Current.WallpaperEnabled)
                ChkWallpaperEnabled.IsChecked = true;

            // Initialize pop quiz UI from settings
            if (ChkPopQuizEnabled != null)
                ChkPopQuizEnabled.IsChecked = App.Settings.Current.PopQuizEnabled;
            if (SliderPopQuizFrequency != null)
            {
                SliderPopQuizFrequency.Value = App.Settings.Current.PopQuizFrequency;
                if (TxtPopQuizFrequency != null)
                    TxtPopQuizFrequency.Text = $"{App.Settings.Current.PopQuizFrequency}/session hr";
            }

            // Handle start minimized (to tray) - delay briefly to let window render properly first
            if (App.Settings.Current.StartMinimized)
            {
                // Let the window fully render before minimizing to avoid black window artifacts
                await Task.Delay(100);
                _trayIcon?.MinimizeToTray();
            }

            // Handle auto-start engine
            if (App.Settings.Current.AutoStartEngine)
            {
                StartEngine();
            }

            // Handle force video on launch (after a brief delay to let things initialize)
            if (App.Settings.Current.ForceVideoOnLaunch)
            {
                await Task.Delay(200);
                TriggerStartupVideo();
            }

            // Fetch initial leaderboard data for stat pills
            if (App.Leaderboard != null && App.IsLoggedIn)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(500);
                    await App.Leaderboard.RefreshAsync();

                    // Update UI after leaderboard loads
                    Dispatcher.Invoke(() => UpdateStatPills());
                });
            }

            // Start periodic stat pill update timer
            StartStatPillUpdateTimer();

            // Browser is lazy-loaded on first interaction (click radio toggle, pop-out, or external navigation)

            // Check if this is first run and prompt for assets folder
            await CheckFirstRunAssetsPromptAsync();

            // Initialize Avatar Tube Window
            InitializeAvatarTube();

            // Initialize Discord Rich Presence checkboxes (both locations).
            // Guard with _isLoading so the Changed handler doesn't fire the
            // "Discord Not Linked" MessageBox during startup for users whose
            // saved setting is enabled but who haven't linked Discord.
            _isLoading = true;
            try
            {
                ChkDiscordRichPresence.IsChecked = App.Settings.Current.DiscordRichPresenceEnabled;
                ChkQuickDiscordRichPresence.IsChecked = App.Settings.Current.DiscordRichPresenceEnabled;
            }
            finally { _isLoading = false; }

            // Initialize Audio Sync checkbox and sliders
            ChkHapticAudioSync.IsChecked = App.Settings.Current.Haptics.AudioSync.Enabled;
            if (SliderAudioSyncLatency != null)
            {
                SliderAudioSyncLatency.Value = App.Settings.Current.Haptics.AudioSync.ManualLatencyOffsetMs;
                var latencyMs = App.Settings.Current.Haptics.AudioSync.ManualLatencyOffsetMs;
                var sign = latencyMs >= 0 ? "+" : "";
                TxtAudioSyncLatency.Text = $"{sign}{latencyMs}ms";
            }
            if (SliderAudioSyncIntensity != null)
            {
                var intensityPercent = (int)(App.Settings.Current.Haptics.AudioSync.LiveIntensity * 100);
                SliderAudioSyncIntensity.Value = intensityPercent;
                TxtAudioSyncIntensity.Text = $"{intensityPercent}%";
            }
            if (AudioSyncLatencyPanel != null)
            {
                AudioSyncLatencyPanel.Visibility = App.Settings.Current.Haptics.AudioSync.Enabled
                    ? Visibility.Visible : Visibility.Collapsed;
            }

            // Initialize Video Haptic Sync enhanced UI sliders
            if (SliderVideoHapticDelay != null)
            {
                SliderVideoHapticDelay.Value = App.Settings.Current.Haptics.AudioSync.ManualLatencyOffsetMs;
                var latencyMs = App.Settings.Current.Haptics.AudioSync.ManualLatencyOffsetMs;
                var sign = latencyMs >= 0 ? "+" : "";
                if (TxtVideoHapticDelay != null)
                    TxtVideoHapticDelay.Text = $"{sign}{latencyMs}ms";
            }
            if (SliderVideoHapticPower != null)
            {
                var intensityPercent = (int)(App.Settings.Current.Haptics.AudioSync.LiveIntensity * 100);
                SliderVideoHapticPower.Value = intensityPercent;
                if (TxtVideoHapticPower != null)
                    TxtVideoHapticPower.Text = $"{intensityPercent}%";
            }
            if (VideoHapticSyncSliders != null)
            {
                VideoHapticSyncSliders.Visibility = App.Settings.Current.Haptics.AudioSync.Enabled
                    ? Visibility.Visible : Visibility.Collapsed;
            }

            // Initialize Quick Links login buttons
            UpdateQuickPatreonUI();
            UpdateQuickDiscordUI();

            // Initialize scrolling marquee banner
            InitializeMarqueeBanner();

            // Deeper tab first-launch pulse — draw the eye to the new tab once,
            // unless the user has already opened it (HasSeenDeeperTab) or disabled it.
            var deeperSettings = App.Settings?.Current;
            if (deeperSettings != null && deeperSettings.EnableDeeper && !deeperSettings.HasSeenDeeperTab)
            {
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        await Task.Delay(1200);
                        StartDeeperTabPulse();
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning(ex, "Failed to start Deeper tab pulse");
                    }
                });
            }

            // Check if any authenticated user needs to complete registration (choose display name)
            // This handles users who had cached tokens but cancelled the registration dialog previously
            _ = CheckPendingRegistrationAsync();
        }

        /// <summary>
        /// Check if any authenticated user needs to complete registration (choose display name).
        /// This catches users who have profiles with null display_name from before the fix.
        /// </summary>
        private async Task CheckPendingRegistrationAsync()
        {
            try
            {
                // Wait a bit for background authentication to complete
                await Task.Delay(2000);

                // If user already has a UnifiedId (registered in V2 system), skip this check
                // The old /patreon/validate endpoint doesn't know about V2 users
                if (!string.IsNullOrEmpty(App.Settings?.Current?.UnifiedId))
                {
                    App.Logger?.Debug("User already has UnifiedId, skipping pending registration check");
                    return;
                }

                // Check if user is authenticated but needs registration
                bool patreonNeedsReg = App.Patreon?.IsAuthenticated == true && App.Patreon.NeedsRegistration;
                bool discordNeedsReg = App.Discord?.IsAuthenticated == true && App.Discord.NeedsRegistration;

                if (!patreonNeedsReg && !discordNeedsReg)
                    return;

                App.Logger?.Information("User needs to complete registration: Patreon={Patreon}, Discord={Discord}",
                    patreonNeedsReg, discordNeedsReg);

                // Determine which provider to use for registration (prefer Patreon)
                string provider = patreonNeedsReg ? "patreon" : "discord";

                // Show the display name dialog (HandlePostAuthAsync gets the token internally)
                await Dispatcher.InvokeAsync(async () =>
                {
                    var success = await Services.AccountService.HandlePostAuthAsync(this, provider);
                    if (success)
                    {
                        App.Logger?.Information("Pending registration completed successfully");
                        // Refresh the profile to get updated data
                        if (App.ProfileSync != null)
                            await App.ProfileSync.LoadProfileAsync();
                        UpdateQuickPatreonUI();
                        UpdateQuickDiscordUI();
                    }
                    else
                    {
                        App.Logger?.Warning("Pending registration failed or was cancelled");
                    }
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Error checking pending registration");
            }
        }

        /// <summary>
        /// Checks if this is a first run (no assets) and prompts user to choose a content folder.
        /// </summary>
        private async Task CheckFirstRunAssetsPromptAsync()
        {
            try
            {
                // Skip if custom assets path is already set
                if (!string.IsNullOrWhiteSpace(App.Settings?.Current?.CustomAssetsPath))
                    return;

                // Check if default assets folder has any content
                var defaultImagesPath = System.IO.Path.Combine(App.UserAssetsPath, "images");
                var defaultVideosPath = System.IO.Path.Combine(App.UserAssetsPath, "videos");

                int imageCount = 0;
                int videoCount = 0;

                if (System.IO.Directory.Exists(defaultImagesPath))
                {
                    imageCount = System.IO.Directory.GetFiles(defaultImagesPath, "*.*")
                        .Count(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                   f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                   f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                   f.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
                                   f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase));
                }

                if (System.IO.Directory.Exists(defaultVideosPath))
                {
                    videoCount = System.IO.Directory.GetFiles(defaultVideosPath, "*.*")
                        .Count(f => f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                                   f.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ||
                                   f.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase));
                }

                // If user has content, don't bother them
                if (imageCount > 5 || videoCount > 2)
                    return;

                // Check if there's a "first run shown" flag
                if (App.Settings?.Current?.FirstRunAssetsPromptShown == true)
                    return;

                // Show first-run prompt after a brief delay
                await Task.Delay(500);

                var result = MessageBox.Show(
                    "Welcome to Conditioning Control Panel!\n\n" +
                    "Would you like to choose a custom folder for your content?\n\n" +
                    "This folder will store:\n" +
                    "  • Your images and videos\n" +
                    "  • Downloaded content packs\n\n" +
                    "Choosing a custom folder is recommended if you want to:\n" +
                    "  • Keep content on a different drive\n" +
                    "  • Preserve content across reinstalls\n\n" +
                    "You can always change this later in Settings > Assets.",
                    "Choose Content Folder",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                // Mark as shown regardless of choice
                if (App.Settings?.Current != null)
                {
                    App.Settings.Current.FirstRunAssetsPromptShown = true;
                    App.Settings.Save();
                }

                if (result == MessageBoxResult.Yes)
                {
                    // Open the assets folder selection dialog
                    BtnPickAssetsFolder_Click(this, new RoutedEventArgs());
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Error in first-run assets prompt");
            }
        }

        private const int WM_GETMINMAXINFO = 0x0024;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // Fix maximized window extending behind taskbar (buttons cut off)
            if (msg == WM_GETMINMAXINFO)
            {
                var mmi = System.Runtime.InteropServices.Marshal.PtrToStructure<MINMAXINFO>(lParam);

                // Get the monitor this window is on
                var monitor = System.Windows.Forms.Screen.FromHandle(hwnd);
                var workingArea = monitor.WorkingArea;

                // Constrain maximized size to working area (excludes taskbar)
                mmi.ptMaxPosition.X = workingArea.Left;
                mmi.ptMaxPosition.Y = workingArea.Top;
                mmi.ptMaxSize.X = workingArea.Width;
                mmi.ptMaxSize.Y = workingArea.Height;

                System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, lParam, true);
                handled = true;
            }
            return IntPtr.Zero;
        }


        #region Tab Navigation

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("settings");
        }

        private void BtnPresets_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("presets");
            RefreshPresetsList();
        }

        // BtnProgression handler removed in velvet-mosaic phase 6 — the Progression
        // tab no longer has a header button; its features live on the Dashboard now.

        private void BtnQuests_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("quests");
        }

        private void BtnEnhancements_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("enhancements");
        }

        private void BtnDeeper_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("deeper");
            if (App.Settings?.Current is { } s && !s.HasSeenDeeperTab)
            {
                s.HasSeenDeeperTab = true;
                StopDeeperTabPulse();
                App.Settings?.Save();
            }
            UpdateDeeperWelcomeCardVisibility();
            // Mission 2: lazy-init the hub the first time the user opens the
            // tab, then on every show pull a fresh scan. Cheap; doesn't churn
            // if the library hasn't changed.
            InitializeDeeperHub();
            ReloadDeeperLibraryFromDisk();
        }

        private void UpdateDeeperWelcomeCardVisibility()
        {
            if (DeeperWelcomeCard == null) return;
            var seen = App.Settings?.Current?.HasSeenDeeperWelcome ?? true;
            DeeperWelcomeCard.Visibility = seen ? Visibility.Collapsed : Visibility.Visible;
        }

        private void DismissDeeperWelcomeCard()
        {
            if (App.Settings?.Current is { } s && !s.HasSeenDeeperWelcome)
            {
                s.HasSeenDeeperWelcome = true;
                App.Settings?.Save();
            }
            if (DeeperWelcomeCard != null) DeeperWelcomeCard.Visibility = Visibility.Collapsed;
        }

        private void BtnDeeperWelcomeTour_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("deeper_tour"); } catch { }
            DismissDeeperWelcomeCard();
            StartDeeperTabTutorial();
        }

        private void BtnDeeperWelcomeDemo_Click(object sender, RoutedEventArgs e)
        {
            DismissDeeperWelcomeCard();
            OpenDeeperBundledDemo();
        }

        private void BtnDeeperWelcomeDismiss_Click(object sender, RoutedEventArgs e)
        {
            DismissDeeperWelcomeCard();
        }

        private void BtnDeeperTutorial_Click(object sender, RoutedEventArgs e)
        {
            StartDeeperTabTutorial();
        }

        // The bundled "Welcome to Deeper" demo is seeded into the user's library
        // on first run. Match by the literal filename rather than a hardcoded
        // path so we follow the user's library folder if they moved it.
        private void OpenDeeperBundledDemo()
        {
            try
            {
                var lib = App.EnhancementLibrary;
                if (lib == null) return;
                var match = lib.ScanLibrary()
                    .FirstOrDefault(e =>
                        string.Equals(System.IO.Path.GetFileName(e.FilePath), "welcome.ccpenh.json",
                            StringComparison.OrdinalIgnoreCase));
                if (match == null)
                {
                    MessageBox.Show(this,
                        "The bundled demo couldn't be found in your library — try restarting the app.",
                        "Deeper", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                OpenDeeperFile(match.FilePath);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Open bundled Deeper demo failed: {Error}", ex.Message);
            }
        }

        private void StartDeeperTabTutorial()
        {
            ShowTab("deeper");
            UpdateDeeperWelcomeCardVisibility(); // keep the card consistent with state
            StartTutorial(TutorialType.Deeper);
        }

        private void ChkEnableDeeper_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var enabled = ChkEnableDeeper.IsChecked ?? true;
            if (App.Settings?.Current is { } s) s.EnableDeeper = enabled;
            if (BtnDeeper != null) BtnDeeper.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            // If the user just disabled Deeper while it's the active tab, fall back to Settings.
            if (!enabled && DeeperTab?.Visibility == Visibility.Visible) ShowTab("settings");
            App.Settings?.Save();
        }

        private bool _deeperPulseRunning;

        private void StartDeeperTabPulse()
        {
            if (BtnDeeperScale == null || _deeperPulseRunning) return;
            _deeperPulseRunning = true;
            var anim = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 1.0,
                To = 1.12,
                Duration = TimeSpan.FromMilliseconds(700),
                AutoReverse = true,
                RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(4),
                EasingFunction = new System.Windows.Media.Animation.SineEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut
                }
            };
            anim.Completed += (_, _) =>
            {
                _deeperPulseRunning = false;
                if (BtnDeeperScale != null)
                {
                    BtnDeeperScale.ScaleX = 1.0;
                    BtnDeeperScale.ScaleY = 1.0;
                }
            };
            BtnDeeperScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, anim);
            BtnDeeperScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, anim);
        }

        private void StopDeeperTabPulse()
        {
            if (!_deeperPulseRunning && BtnDeeperScale == null) return;
            _deeperPulseRunning = false;
            if (BtnDeeperScale != null)
            {
                BtnDeeperScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
                BtnDeeperScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
                BtnDeeperScale.ScaleX = 1.0;
                BtnDeeperScale.ScaleY = 1.0;
            }
        }

        private void BtnDeeperNewEnhancement_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("deeper_new"); } catch { }
            var dialog = new Views.Deeper.NewEnhancementDialog { Owner = this };
            if (dialog.ShowDialog() != true) return;

            var enhancement = App.EnhancementLibrary?.CreateBlank(dialog.SelectedMediaType, dialog.SelectedSource);
            if (enhancement == null) return;

            OpenDeeperEditor(enhancement, null);
        }

        private void BtnDeeperOpenPlayer_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("deeper_player"); } catch { }
            try
            {
                var win = new Views.Deeper.EnhancementPlayerWindow(App.DeeperPlayer, App.DeeperHost) { Owner = this };
                win.Show();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open Deeper player");
                MessageBox.Show(this,
                    $"Couldn't open Deeper Player:\n\n{ex.GetType().Name}: {ex.Message}",
                    "Open Player failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnDeeperBrowserBound(string pageUrl, Models.Deeper.Enhancement enhancement)
        {
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    DeeperBrowserBadge.Visibility = Visibility.Visible;
                    var name = string.IsNullOrEmpty(enhancement.Metadata?.Name) ? "(untitled)" : enhancement.Metadata!.Name;
                    TxtDeeperBrowserBadge.Text = $"🌊 {name}";
                    DeeperBrowserBadge.Tag = $"{name}\n{pageUrl}";

                    // QoL: if the bound enhancement uses webcam-driven rules and
                    // tracking isn't already running, ask the user once whether
                    // they want to turn it on. Mirrors the player's behavior so
                    // gaze/blink/attention rules actually fire in the browser.
                    MaybePromptBrowserWebcamForEnhancement(enhancement);
                }
                catch { }
            });
        }

        private void OnDeeperBrowserUnbound()
        {
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    DeeperBrowserBadge.Visibility = Visibility.Collapsed;
                    DeeperBrowserBadge.Tag = null;
                    _browserWebcamPromptShownForUrl = null;
                }
                catch { }
            });
        }

        // ────────────────────────────────────────────────────────────────────
        // Browser Webcam Tracking toggle (button above the embedded WebView2)
        // ────────────────────────────────────────────────────────────────────

        private bool _browserWebcamStateSubscribed;
        private Action<WebcamTrackingState>? _onBrowserWebcamStateChanged;
        // Tracks the page URL we've already prompted about so reload-binds
        // don't badger the user repeatedly for the same enhancement.
        private string? _browserWebcamPromptShownForUrl;

        private void EnsureBrowserWebcamStateSubscribed()
        {
            if (_browserWebcamStateSubscribed || App.Webcam == null) return;
            _browserWebcamStateSubscribed = true;
            _onBrowserWebcamStateChanged = _ => Dispatcher.BeginInvoke(RefreshBrowserWebcamButton);
            App.Webcam.OnTrackingStateChanged += _onBrowserWebcamStateChanged;
            RefreshBrowserWebcamButton();
        }

        private void RefreshBrowserWebcamButton()
        {
            try
            {
                if (BtnWebcamTracking == null) return;
                var on = App.Webcam?.IsRunning == true;
                if (TxtWebcamTracking != null)
                    TxtWebcamTracking.Text = Loc.Get(on
                        ? "btn_browser_webcam_tracking_on"
                        : "btn_browser_webcam_tracking_off");
                BtnWebcamTracking.ToolTip = Loc.Get(on
                    ? "tooltip_browser_webcam_tracking_on"
                    : "tooltip_browser_webcam_tracking_off");
            }
            catch { }
        }

        private async void BtnWebcamTracking_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("webcam_tracking"); } catch { }
            var svc = App.Webcam;
            if (svc == null) return;

            if (svc.IsRunning)
            {
                svc.Stop();
                RefreshBrowserWebcamButton();
                return;
            }

            // Consent gate. If declined or cancelled, bail silently — user can
            // try again from this same button or from the Lab tab later.
            if (!WebcamTrackingService.IsConsentCurrent())
            {
                var consent = new WebcamConsentDialog { Owner = this };
                var ok = consent.ShowDialog();
                if (ok != true || !consent.ConsentGiven) return;
            }

            // Needs-calibration path: the calibration window reads OnRawIris,
            // which only fires while the tracker is running. So we start the
            // camera FIRST, then open calibration on top of the live stream.
            // Tell the user up front so the camera light surprising them
            // doesn't feel like the app did something it shouldn't have.
            bool needsCalibration = svc.Calibration == null;
            if (needsCalibration)
            {
                var confirm = MessageBox.Show(this,
                    Loc.Get("browser_webcam_calibrate_prompt_body"),
                    Loc.Get("browser_webcam_calibrate_prompt_title"),
                    MessageBoxButton.OKCancel, MessageBoxImage.Information);
                if (confirm != MessageBoxResult.OK) return;
            }

            // Start off the UI thread — Start() does VideoCapture open + ONNX
            // session ctors and can block 10-30s on slow USB negotiation.
            bool started;
            try
            {
                started = await Task.Run(() => svc.Start());
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Browser webcam toggle: Start() threw");
                started = false;
            }
            RefreshBrowserWebcamButton();

            if (!started)
            {
                App.Logger?.Warning("Browser webcam toggle: Start() returned false, state={State}", svc.State);
                return;
            }

            // Now the tracker is live — run the 16-point calibration. If the
            // user cancels we stop the tracker again so the camera light
            // doesn't stay on for a feature they backed out of.
            if (needsCalibration)
            {
                var calibrated = WebcamCalibrationWindow.ShowDialogWithRecalibrate(this);
                if (calibrated != true)
                {
                    svc.Stop();
                    RefreshBrowserWebcamButton();
                }
            }
        }

        // Webcam-needing enhancement check. Delegates to the shared
        // EnhancementCapabilities.NeedsWebcam so the browser hub, the mandatory-
        // video engine-start nudge, and the Deeper player all answer identically
        // (AutoTags first, then a trigger scan across both the unified timeline
        // and the legacy Rules collection).
        private static bool BrowserEnhancementNeedsWebcam(Models.Deeper.Enhancement enh)
            => Services.Deeper.EnhancementCapabilities.NeedsWebcam(enh);

        private void MaybePromptBrowserWebcamForEnhancement(Models.Deeper.Enhancement enhancement)
        {
            try
            {
                if (enhancement == null) return;
                if (!BrowserEnhancementNeedsWebcam(enhancement)) return;
                var svc = App.Webcam;
                if (svc == null || svc.IsRunning) return;

                // Dedupe per-page so reload/dom-mutation doesn't re-pop the dialog.
                var url = App.DeeperBrowserDiscovery?.ActiveUrl ?? "";
                if (!string.IsNullOrEmpty(_browserWebcamPromptShownForUrl) &&
                    string.Equals(_browserWebcamPromptShownForUrl, url, StringComparison.OrdinalIgnoreCase))
                    return;
                _browserWebcamPromptShownForUrl = url;

                var result = MessageBox.Show(this,
                    Loc.Get("browser_webcam_prompt_body"),
                    Loc.Get("browser_webcam_prompt_title"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;

                // Reuse the toggle button's flow so consent + calibration
                // gating is identical to the manual-click path.
                BtnWebcamTracking_Click(this, new RoutedEventArgs());
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("MaybePromptBrowserWebcamForEnhancement: {Error}", ex.Message);
            }
        }

        // Set once the engine-start enhancement nudge has been shown this launch,
        // so repeated Start/Stop cycles don't re-pop it (once per launch; a "Not
        // now" is remembered until the app restarts — webcam isn't auto-started,
        // so a fresh launch legitimately re-asks).
        private bool _mandatoryVideoEnhanceNudgeShown;

        /// <summary>
        /// Engine-start nudge: if the mandatory / asset video folder contains an
        /// enhanced video the current settings won't fully honour, offer to flip
        /// the missing switch(es) in one combined dialog:
        ///   • VideoEnhanceIfPossible is off but enhanced videos exist → enable it.
        ///   • An enhancement needs the webcam tracker but it isn't running → start
        ///     it, routing through the same consent/calibration flow as the manual
        ///     toggle.
        /// Scans the folder off the UI thread (cached + short-circuited) and
        /// prompts at most once per launch. No-op unless mandatory videos are on.
        /// </summary>
        private async void MaybePromptMandatoryVideoEnhancement()
        {
            try
            {
                if (_mandatoryVideoEnhanceNudgeShown) return;

                var settings = App.Settings?.Current;
                if (settings == null || !settings.MandatoryVideosEnabled) return;

                // Don't interrupt remote-controlled or locked-down sessions.
                if (App.RemoteControl?.ControllerConnected == true) return;
                if (App.Lockdown?.IsActive == true) return;

                var folder = System.IO.Path.Combine(App.EffectiveAssetsPath, "videos");

                Services.Deeper.MandatoryVideoEnhancementScanner.ScanResult scan;
                try
                {
                    scan = await Task.Run(() => Services.Deeper.MandatoryVideoEnhancementScanner.Scan(folder));
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("MaybePromptMandatoryVideoEnhancement: scan failed: {Error}", ex.Message);
                    return;
                }

                // The window/engine may have gone away during the async scan.
                if (!IsLoaded || Dispatcher.HasShutdownStarted) return;
                if (!_isRunning) return;            // engine stopped before scan returned
                if (_mandatoryVideoEnhanceNudgeShown) return; // a concurrent start won the race

                if (!scan.AnyEnhanced) return;      // nothing to nudge about

                var enhanceOff = !settings.VideoEnhanceIfPossible;
                var webcamSvc = App.Webcam;
                var webcamGap = scan.AnyWebcamEnhanced && (webcamSvc == null || !webcamSvc.IsRunning);

                // No gap → no prompt (enhancement already on and either no webcam
                // rules or the webcam is already tracking).
                if (!enhanceOff && !webcamGap) return;

                _mandatoryVideoEnhanceNudgeShown = true;

                var sb = new System.Text.StringBuilder();
                sb.Append("Some videos in your mandatory video folder have enhancements ");
                sb.Append("(synced flashes, haptics, overlays and more).\n\n");
                if (enhanceOff)
                    sb.Append("• Video enhancement is currently turned OFF, so they won't play.\n");
                if (webcamGap)
                    sb.Append("• Some use webcam tracking (gaze / blink), but the webcam engine isn't running.\n");
                sb.Append("\nWould you like to turn ");
                sb.Append(enhanceOff && webcamGap ? "these on now?"
                          : enhanceOff ? "enhancement on now?"
                          : "the webcam on now?");

                var yes = enhanceOff && webcamGap ? "Yes, set it up"
                          : enhanceOff ? "Yes, enable enhancement"
                          : "Yes, turn on webcam";

                var confirmed = ShowStyledDialog("✨ Enhanced videos detected", sb.ToString(), yes, "Not now");
                if (!confirmed) return;

                if (enhanceOff)
                {
                    settings.VideoEnhanceIfPossible = true;
                    App.Settings?.Save();
                    App.Logger?.Information("Mandatory-video enhancement enabled via engine-start nudge.");
                }

                if (webcamGap)
                {
                    // Reuse the manual toggle's flow so consent + calibration
                    // gating is identical to clicking the webcam button.
                    BtnWebcamTracking_Click(this, new RoutedEventArgs());
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("MaybePromptMandatoryVideoEnhancement: {Error}", ex.Message);
            }
        }

        private void ToggleEnhanceIfPossible_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                var newValue = ToggleEnhanceIfPossible?.IsChecked == true;
                if (App.Settings?.Current != null)
                {
                    App.Settings.Current.BrowserEnhanceIfPossible = newValue;
                    App.Settings.Save();
                }
                App.BrowserEnhanceBridge?.Refresh();

                // If just turned off, status text needs an immediate reset since
                // Refresh() will fire MatchChanged(null) but we want to be explicit.
                if (!newValue && TxtEnhanceMatchStatus != null)
                    TxtEnhanceMatchStatus.Text = Loc.Get("browser_enhance_match_off");
            }
            catch (Exception ex) { App.Logger?.Debug("ToggleEnhanceIfPossible_Changed: {Error}", ex.Message); }
        }

        private void OnBrowserEnhanceMatchChanged(Services.Deeper.EnhancementLibraryEntry? match)
        {
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    if (TxtEnhanceMatchStatus == null) return;
                    if (App.Settings?.Current?.BrowserEnhanceIfPossible == false)
                    {
                        TxtEnhanceMatchStatus.Text = Loc.Get("browser_enhance_match_off");
                        return;
                    }
                    if (match == null)
                    {
                        TxtEnhanceMatchStatus.Text = Loc.Get("browser_enhance_match_none");
                        return;
                    }
                    var name = string.IsNullOrEmpty(match.Name) ? "(untitled)" : match.Name;
                    TxtEnhanceMatchStatus.Text = string.Format(Loc.Get("browser_enhance_match_fmt"), name);
                }
                catch { }
            });
        }

        private void OpenDeeperEditor(Models.Deeper.Enhancement enhancement, string? filePath)
        {
            try
            {
                var window = new Views.Deeper.DeeperEditorWindow(enhancement, filePath) { Owner = this };
                window.Closed += (_, _) => RefreshDeeperLibraryUI();
                window.Show();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to open Deeper editor");
                MessageBox.Show(this, ex.Message, "Deeper", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ---- Public entry points for "Open with CCP" + drag-drop dispatch ----

        public void OpenInDeeperPlayer(string mediaPath)
        {
            try
            {
                var win = new Views.Deeper.EnhancementPlayerWindow(App.DeeperPlayer, App.DeeperHost) { Owner = this };
                win.Show();
                win.OpenLocalMediaFile(mediaPath);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open Deeper player for {Path}", mediaPath);
                MessageBox.Show(this,
                    $"Couldn't open Deeper Player:\n\n{ex.GetType().Name}: {ex.Message}",
                    "Open Player failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Opens the Player and loads a Deeper enhancement JSON. The host fires
        // its Loaded event which routes to OnHostLoaded → UpdateHostUi → the
        // correct media loader (remote URL / local video / audio) based on the
        // enhancement's MediaType + MediaSource. Used by the hub row's ▶
        // button — distinct from OpenInDeeperPlayer (which takes a media path).
        // Mission 3: opens the editor for an existing .ccpenh.json file. Used
        // by the Player's "Open in editor" jump (header button + event log
        // link). Routes through OpenDeeperFile so the same load/error path
        // and library-refresh-on-close behavior applies as the hub row click.
        public void OpenDeeperEditorFromPlayer(string ccpenhJsonPath)
        {
            if (string.IsNullOrWhiteSpace(ccpenhJsonPath)) return;
            OpenDeeperFile(ccpenhJsonPath);
        }

        public void OpenDeeperEnhancementInPlayer(string ccpenhJsonPath)
        {
            try
            {
                var win = new Views.Deeper.EnhancementPlayerWindow(App.DeeperPlayer, App.DeeperHost) { Owner = this };
                win.Show();
                win.LoadEnhancementFile(ccpenhJsonPath);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open Deeper player for enhancement {Path}", ccpenhJsonPath);
                MessageBox.Show(this,
                    $"Couldn't open Deeper Player:\n\n{ex.GetType().Name}: {ex.Message}",
                    "Open Player failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void OpenInDeeperEditorForMedia(string mediaPath)
        {
            try
            {
                var ext = Path.GetExtension(mediaPath);
                var mediaType = AssetVideoExtensions.Contains(ext)
                    ? Models.Deeper.MediaTypes.Video
                    : Models.Deeper.MediaTypes.Audio;
                var enhancement = App.EnhancementLibrary?.CreateBlank(mediaType, mediaPath);
                if (enhancement == null) return;
                OpenDeeperEditor(enhancement, null);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open Deeper editor for {Path}", mediaPath);
                MessageBox.Show(this,
                    $"Couldn't open Deeper Editor:\n\n{ex.GetType().Name}: {ex.Message}",
                    "Open Editor failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void HandlePendingFileOpen(string action, string path)
        {
            if (string.IsNullOrEmpty(action) || string.IsNullOrEmpty(path)) return;
            if (action == "play") OpenInDeeperPlayer(path);
            else if (action == "edit") OpenInDeeperEditorForMedia(path);
        }

        private void OnDeeperLibraryChanged(object? sender, EventArgs e)
        {
            // Library change events arrive on the dispatcher thread already
            // (EnhancementLibrary marshals via Application.Current.Dispatcher),
            // but only refresh if the tab is actually visible — a hidden tab
            // would just throw the work away on the next ShowTab.
            try
            {
                if (DeeperTab?.Visibility == Visibility.Visible)
                    RefreshDeeperLibraryUI();
            }
            catch (Exception ex) { App.Logger?.Debug("Deeper library refresh error: {Error}", ex.Message); }
        }

        // Mission 2 (hub redesign): the old StackPanel-rebuild was replaced by
        // the ObservableCollection + DataTemplate path in MainWindow.DeeperHub.cs.
        // Kept as a stable hook so the ~5 call sites (OpenDeeperEditor's Closed
        // handler, DeleteDeeperLibraryEntry, OnDeeperLibraryChanged, etc.) don't
        // need touching.
        private void RefreshDeeperLibraryUI() => ReloadDeeperLibraryFromDisk();

        // Mission 2: BuildDeeperLibraryRow / BuildDeeperRecentRow / BuildDeeperAutoTagsRow
        // / BuildDeeperMediaLine deleted with the two-column Library + Recent grid.
        // Their visual roles moved into the DeeperLibraryRowTemplate DataTemplate
        // in MainWindow.xaml (Mission 2 commit). VM construction lives in
        // MainWindow.DeeperHub.cs:BuildRowVm + ResolveMediaSourceDisplay.

        private void OpenDeeperFile(string path)
        {
            try
            {
                var enhancement = App.EnhancementLibrary?.Open(path);
                if (enhancement == null) return;
                OpenDeeperEditor(enhancement, path);
            }
            catch (Services.Deeper.EnhancementLoadException ex)
            {
                MessageBox.Show(this, ex.Message, "Deeper", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to open Deeper file {Path}", path);
                MessageBox.Show(this, ex.Message, "Deeper", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnDeeperOpenLibraryFolder_Click(object sender, RoutedEventArgs e)
        {
            var folder = App.EnhancementLibrary?.LibraryFolder;
            if (string.IsNullOrEmpty(folder)) return;
            try
            {
                System.IO.Directory.CreateDirectory(folder);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to open Deeper library folder {Folder}", folder);
                MessageBox.Show(this, ex.Message, "Deeper", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnDeeperImport_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("deeper_import"); } catch { }
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import enhancement",
                Filter = "Deeper enhancements (*.ccpenh.json)|*.ccpenh.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = ".ccpenh.json",
                Multiselect = true,
                CheckFileExists = true,
                InitialDirectory = App.EnhancementLibrary?.LastDirectory
            };
            if (dlg.ShowDialog(this) != true) return;
            ImportEnhancementFiles(dlg.FileNames);
        }

        // NOTE: Deeper-tab drag-and-drop is intentionally handled by the window-wide
        // Window_Drop / DetectDropType system (DropType.Enhancement → ImportEnhancementFiles)
        // rather than a tab-local handler. A tab-local AllowDrop handler swallowed the
        // bubbling drag events the global drop overlay relies on, so it only covered
        // part of the tab. Keeping one global handler makes the entire window — and thus
        // the whole Deeper tab — a uniform drop target.

        private static bool IsImportableEnhancementPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            // Accept either the canonical "*.ccpenh.json" double-suffix or a plain
            // ".json" — the serializer will reject anything that doesn't carry the
            // expected $schema tag, so plain .json is safe to offer.
            return path.EndsWith(".ccpenh.json", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }

        private void ImportEnhancementFiles(System.Collections.Generic.IEnumerable<string> paths)
        {
            var lib = App.EnhancementLibrary;
            if (lib == null)
            {
                MessageBox.Show(this, "Enhancement library isn't ready yet.", "Import failed",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var imported = new System.Collections.Generic.List<string>();
            var errors = new System.Collections.Generic.List<string>();
            string? lastImportedPath = null;

            foreach (var path in paths)
            {
                if (!IsImportableEnhancementPath(path))
                {
                    errors.Add($"{System.IO.Path.GetFileName(path)} — not a .ccpenh.json file.");
                    continue;
                }
                try
                {
                    // Validate by loading; bad schema / oversized files throw with
                    // a useful message from the serializer.
                    var enhancement = Services.Deeper.EnhancementSerializer.LoadFromFile(path);
                    var saved = lib.PromoteToLibrary(enhancement, sourceTag: "import");
                    if (saved == null)
                    {
                        errors.Add($"{System.IO.Path.GetFileName(path)} — couldn't write into the library folder.");
                        continue;
                    }
                    lastImportedPath = saved;
                    imported.Add(System.IO.Path.GetFileName(saved));
                }
                catch (Services.Deeper.EnhancementLoadException ex)
                {
                    errors.Add($"{System.IO.Path.GetFileName(path)} — {ex.Message}");
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "ImportEnhancementFiles: failed on {Path}", path);
                    errors.Add($"{System.IO.Path.GetFileName(path)} — {ex.GetType().Name}: {ex.Message}");
                }
            }

            // Remember the source folder for next manual import so the file dialog
            // opens where the user picked from.
            if (lastImportedPath != null && App.Settings?.Current != null)
            {
                var src = paths.FirstOrDefault();
                if (!string.IsNullOrEmpty(src))
                {
                    var dir = System.IO.Path.GetDirectoryName(src);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        App.Settings.Current.DeeperLastDirectory = dir;
                        App.Settings.Save();
                    }
                }
            }

            // Force-refresh the hub list now in addition to the FileSystemWatcher
            // signal, since the watcher's debounce can lag a fast manual import.
            try { RefreshDeeperLibraryUI(); } catch { }

            if (errors.Count == 0 && imported.Count > 0)
            {
                var msg = imported.Count == 1
                    ? $"Imported \"{imported[0]}\" into your library."
                    : $"Imported {imported.Count} enhancements into your library.";
                MessageBox.Show(this, msg, "Import complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (errors.Count > 0)
            {
                var head = imported.Count > 0
                    ? $"Imported {imported.Count}, but {errors.Count} failed:\n\n"
                    : $"{errors.Count} file(s) couldn't be imported:\n\n";
                MessageBox.Show(this, head + string.Join("\n", errors), "Import failed",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void DeleteDeeperLibraryEntry(Services.Deeper.EnhancementLibraryEntry entry)
        {
            var label = string.IsNullOrEmpty(entry.Name) ? System.IO.Path.GetFileName(entry.FilePath) : entry.Name;
            var msg = string.Format(Loc.Get("deeper_library_delete_confirm_fmt"), label);
            var result = MessageBox.Show(this, msg, Loc.Get("deeper_library_delete_title"),
                MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (result != MessageBoxResult.OK) return;
            try
            {
                if (System.IO.File.Exists(entry.FilePath))
                    System.IO.File.Delete(entry.FilePath);
                // FileSystemWatcher in EnhancementLibrary will fire LibraryChanged
                // and refresh the UI, but force an immediate refresh for snappiness.
                RefreshDeeperLibraryUI();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to delete Deeper library entry {Path}", entry.FilePath);
                MessageBox.Show(this, ex.Message, "Deeper", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // W3 Piece 1 — invoked from BrowserService.NavigationCompleted on every
        // page load in the embedded browser. Filters non-HT URLs out, debounces
        // rapid navigations via a per-window CTS, and surfaces the result as a
        // toast (or no toast at all when there are no results / lookup failed).
        //
        // Runs entirely off the WebView2 navigation thread context because the
        // caller already marshals through Dispatcher.Invoke before calling us,
        // but the lookup itself awaits on the thread pool so we don't block UI
        // during the network call.
        private void TriggerCatalogueLookupForNavigation(string url)
        {
            try
            {
                if (App.CatalogueLookup == null) return;
                // Cheap pre-filter — saves the cost of starting a Task for the
                // (very common) case of a non-HT navigation. The service does
                // the same check defensively.
                if (!Helpers.HtUrlHelper.IsEligibleHtUrl(url)) return;

                // Cancel any in-flight lookup from the previous navigation so a
                // delayed response doesn't surface a toast for a page the user
                // has already left.
                try { _catalogueLookupCts?.Cancel(); }
                catch { /* idempotent */ }
                _catalogueLookupCts?.Dispose();
                var cts = new System.Threading.CancellationTokenSource();
                _catalogueLookupCts = cts;

                _ = RunCatalogueLookupAsync(url, cts.Token);
            }
            catch (Exception ex)
            {
                // Defensive — must never propagate out of a navigation event handler.
                App.Logger?.Warning(ex, "[Catalogue] TriggerCatalogueLookupForNavigation threw");
            }
        }

        private async System.Threading.Tasks.Task RunCatalogueLookupAsync(string url, System.Threading.CancellationToken ct)
        {
            LookupResult result;
            try
            {
                result = await App.CatalogueLookup!.LookupForUrlAsync(url, ct).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return; // user navigated away; nothing to do
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Catalogue] Lookup threw unexpectedly");
                return;
            }

            // Drop the result silently if a newer navigation has since started
            // — protects against the (small) race window between the lookup
            // returning and the CTS getting cancelled.
            if (ct.IsCancellationRequested) return;

            switch (result)
            {
                case LookupResult.Success s:
                    ShowCatalogueLookupToast(url, s.Entries);
                    break;
                case LookupResult.None:
                case LookupResult.InvalidUrl:
                case LookupResult.NetworkError:
                    // No user-visible feedback on these — silent by design.
                    break;
            }
        }

        // Surface a toast for {N} discovered enhancements. Updates
        // _currentCatalogueHtVideoId so the action handler can validate the
        // user is still on the same video when they click.
        private void ShowCatalogueLookupToast(string url, System.Collections.Generic.List<CatalogueEntry> entries)
        {
            if (entries == null || entries.Count == 0) return;

            var videoId = Helpers.HtUrlHelper.TryExtractHtVideoId(url);
            _currentCatalogueHtVideoId = videoId;

            string message;
            string actionLabel;
            if (entries.Count == 1)
            {
                message = Loc.Get("catalogue_lookup_toast_one");
                actionLabel = Loc.Get("catalogue_lookup_action_use_one");
            }
            else
            {
                message = string.Format(Loc.Get("catalogue_lookup_toast_many_fmt"), entries.Count);
                actionLabel = Loc.Get("catalogue_lookup_action_pick_one");
            }

            // Snapshot the entries + video ID for the action closure so a later
            // mutation of _currentCatalogueHtVideoId by a parallel navigation
            // can be detected.
            var snapshotEntries = entries;
            var snapshotVideoId = videoId;

            App.Notifications?.Show(message, NotificationType.Info, TimeSpan.FromSeconds(10),
                actionLabel,
                () =>
                {
                    // Stale-toast guard: the user navigated away before clicking.
                    // Silently bail — they'll see a fresh toast for whatever
                    // video they're now on (if any).
                    if (!string.Equals(_currentCatalogueHtVideoId, snapshotVideoId, StringComparison.Ordinal))
                    {
                        App.Logger?.Information("[Catalogue] Toast action ignored (user navigated away)");
                        return;
                    }

                    if (snapshotEntries.Count == 1)
                    {
                        _ = DownloadAndOpenCatalogueEntryAsync(snapshotEntries[0]);
                    }
                    else
                    {
                        OpenCataloguePickerDialog(snapshotEntries, snapshotVideoId);
                    }
                });
        }

        private void OpenCataloguePickerDialog(System.Collections.Generic.List<CatalogueEntry> entries, string? videoId)
        {
            try
            {
                var dlg = new CataloguePickerDialog(entries, videoId) { Owner = this };
                dlg.ShowDialog();
                if (dlg.SelectedEntry != null)
                {
                    _ = DownloadAndOpenCatalogueEntryAsync(dlg.SelectedEntry);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Catalogue] Picker dialog threw");
            }
        }

        private async System.Threading.Tasks.Task DownloadAndOpenCatalogueEntryAsync(CatalogueEntry entry)
        {
            DownloadResult result;
            try
            {
                // Pass default cancellation here — the per-navigation CTS is
                // about lookups, not downloads. Once the user has clicked
                // through, they expect the download to complete even if they
                // navigate the browser away while it's in flight.
                result = await App.CatalogueLookup!.DownloadAndOpenAsync(entry, default).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Catalogue] Download flow threw");
                App.Notifications?.Show(Loc.Get("catalogue_lookup_toast_download_failed"),
                    NotificationType.Error, TimeSpan.FromSeconds(8));
                return;
            }

            switch (result)
            {
                case DownloadResult.Success s:
                    App.Notifications?.Show(
                        string.Format(Loc.Get("catalogue_lookup_toast_loaded_fmt"), entry.Title),
                        NotificationType.Info, TimeSpan.FromSeconds(6));
                    break;
                case DownloadResult.NetworkError:
                    App.Notifications?.Show(Loc.Get("catalogue_lookup_toast_download_failed"),
                        NotificationType.Error, TimeSpan.FromSeconds(8));
                    break;
                case DownloadResult.InvalidFile:
                    App.Notifications?.Show(Loc.Get("catalogue_lookup_toast_invalid_file"),
                        NotificationType.Error, TimeSpan.FromSeconds(8));
                    break;
                case DownloadResult.SaveError:
                    App.Notifications?.Show(Loc.Get("catalogue_lookup_toast_save_failed"),
                        NotificationType.Error, TimeSpan.FromSeconds(8));
                    break;
                case DownloadResult.OpenError oe:
                    App.Notifications?.Show(
                        string.Format(Loc.Get("catalogue_lookup_toast_open_failed_fmt"), oe.LocalFilename),
                        NotificationType.Warning, TimeSpan.FromSeconds(10),
                        Loc.Get("catalogue_lookup_action_open_library"),
                        () => SwitchToDeeperLibraryTab());
                    break;
            }
        }

        // Focus the Deeper Library tab when the user clicks "Open Library"
        // from the OpenError recovery toast.
        private void SwitchToDeeperLibraryTab()
        {
            try { ShowTab("deeper"); }
            catch (Exception ex) { App.Logger?.Debug("[Catalogue] SwitchToDeeperLibraryTab failed: {Msg}", ex.Message); }
        }

        // Catalogue eligibility check — wraps the URL helper with the media-type
        // gate (audio enhancements aren't catalogued in W2).
        //
        // URL eligibility itself lives in Helpers/HtUrlHelper.cs because it's
        // now shared by three callers:
        //   1. This W2 row-level submit gate
        //   2. The catalogue server (kept in sync via cclabs-web's enhancements.ts)
        //   3. W3 Piece 1's CatalogueLookupService navigation hook
        // The two client consumers MUST agree on what counts as an HT URL —
        // see HtUrlHelper for the shared regex pair and update both this client
        // helper AND the server's normalizeHtUrl together when adding patterns.
        private static bool IsCatalogueEligible(Services.Deeper.EnhancementLibraryEntry entry)
        {
            if (entry == null) return false;
            if (entry.MediaType != Models.Deeper.MediaTypes.Video) return false;
            return Helpers.HtUrlHelper.IsEligibleHtUrl(entry.MediaSource);
        }

        // W2 — submit a library enhancement to the cclabs catalogue. Opens the
        // affirmation modal; on confirmation, awaits CatalogueService and maps
        // the SubmissionResult to a NotificationService toast per the spec.
        private async Task SubmitDeeperLibraryEntryAsync(Services.Deeper.EnhancementLibraryEntry entry)
        {
            // Defense in depth — the button is already disabled when auth is
            // missing, but a race (token expiring between row build and click)
            // would otherwise produce an AuthFailed toast as the first feedback.
            if (string.IsNullOrEmpty(App.Settings?.Current?.AuthToken))
            {
                App.Notifications?.Show(Loc.Get("catalogue_toast_auth_failed"),
                    Services.NotificationType.Warning, TimeSpan.FromSeconds(8));
                return;
            }

            var label = string.IsNullOrEmpty(entry.Name) ? System.IO.Path.GetFileName(entry.FilePath) : entry.Name;
            var dialog = new CatalogueSubmitDialog(label) { Owner = this };
            if (dialog.ShowDialog() != true || !dialog.Confirmed) return;

            SubmissionResult result;
            try
            {
                result = await App.Catalogue.SubmitEnhancementAsync(entry.FilePath, default).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                // CatalogueService is designed to never throw, but defensively
                // surface anything that escapes as an UnknownError.
                App.Logger?.Warning(ex, "[Catalogue] Submit threw unexpectedly");
                result = new SubmissionResult.UnknownError(0, ex.Message);
            }

            // Remember the submission so the library badge + the eventual
            // "published" notification can track it (no-op for non-ack results).
            RecordDeeperSubmission(entry.FilePath, result);

            ShowCatalogueSubmissionResultToast(result);
        }

        // Map a SubmissionResult to a localized toast. Kept separate from the
        // submit method so tests / future flows (e.g. a "retry all failed"
        // button) can reuse the mapping.
        private void ShowCatalogueSubmissionResultToast(SubmissionResult result)
        {
            switch (result)
            {
                case SubmissionResult.Success:
                    // Success uses the distinct green border (#4CAF50) so the
                    // happy-path outcome is visually unambiguous next to the
                    // pink Info border that Duplicate uses.
                    App.Notifications?.Show(Loc.Get("catalogue_toast_success"),
                        Services.NotificationType.Success, TimeSpan.FromSeconds(6));
                    break;

                case SubmissionResult.Duplicate d:
                {
                    var key = d.ExistingStatus switch
                    {
                        "approved" => "catalogue_toast_duplicate_approved",
                        "rejected" => "catalogue_toast_duplicate_rejected",
                        _ => "catalogue_toast_duplicate_pending",
                    };
                    App.Notifications?.Show(Loc.Get(key),
                        Services.NotificationType.Info, TimeSpan.FromSeconds(6));
                    break;
                }

                case SubmissionResult.ValidationError v:
                {
                    var key = v.ErrorCode switch
                    {
                        "missing_title" => "catalogue_toast_error_missing_title",
                        "missing_creator" => "catalogue_toast_error_missing_creator",
                        "invalid_media_source" => "catalogue_toast_error_invalid_media_source",
                        "invalid_schema" => "catalogue_toast_error_invalid_schema",
                        "file_too_large" => "catalogue_toast_error_file_too_large",
                        "stale_guidelines_version" => "catalogue_toast_error_stale_guidelines",
                        _ => "",
                    };
                    var msg = !string.IsNullOrEmpty(key)
                        ? Loc.Get(key)
                        : Loc.GetF("catalogue_toast_error_generic_fmt", v.ErrorCode);
                    App.Notifications?.Show(msg, Services.NotificationType.Warning, TimeSpan.FromSeconds(8));
                    break;
                }

                case SubmissionResult.AuthFailed:
                    // CatalogueService.MapResponse already invalidated the
                    // cache for us; the user just needs to re-link in Settings.
                    App.Notifications?.Show(Loc.Get("catalogue_toast_auth_failed"),
                        Services.NotificationType.Warning, TimeSpan.FromSeconds(10));
                    break;

                case SubmissionResult.TooLarge:
                    App.Notifications?.Show(Loc.Get("catalogue_toast_too_large"),
                        Services.NotificationType.Error, TimeSpan.FromSeconds(8));
                    break;

                case SubmissionResult.RateLimited r:
                {
                    string msg;
                    if (r.RetryAfterSeconds.HasValue && r.RetryAfterSeconds.Value > 0)
                    {
                        var minutes = Math.Max(1, (int)Math.Ceiling(r.RetryAfterSeconds.Value / 60.0));
                        msg = Loc.GetF("catalogue_toast_rate_limited_minutes_fmt", minutes);
                    }
                    else
                    {
                        msg = Loc.Get("catalogue_toast_rate_limited_unknown");
                    }
                    App.Notifications?.Show(msg, Services.NotificationType.Warning, TimeSpan.FromSeconds(10));
                    break;
                }

                case SubmissionResult.UnknownError u:
                    App.Logger?.Warning("[Catalogue] Submission UnknownError status={Status} body={Body}",
                        u.StatusCode, u.Body);
                    App.Notifications?.Show(Loc.Get("catalogue_toast_unknown_error"),
                        Services.NotificationType.Error, TimeSpan.FromSeconds(8));
                    break;
            }
        }

        private void BtnRerollDaily_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("reroll_daily"); } catch { }
            if (App.Quests?.RerollDailyQuest() == true)
            {
                RefreshQuestUI();
            }
            else
            {
                var hasPatreon = App.Patreon?.HasPremiumAccess == true;
                var msg = hasPatreon
                    ? "You've used all 3 daily rerolls! Rerolls reset at midnight."
                    : "You've used your daily reroll! Patreon supporters get 2 extra rerolls.";
                MessageBox.Show(msg, "Reroll Limit", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnRerollWeekly_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("reroll_weekly"); } catch { }
            if (App.Quests?.RerollWeeklyQuest() == true)
            {
                RefreshQuestUI();
            }
            else
            {
                var hasPatreon = App.Patreon?.HasPremiumAccess == true;
                var msg = hasPatreon
                    ? "You've used all 3 weekly rerolls! Rerolls reset on Sunday."
                    : "You've used your weekly reroll! Patreon supporters get 2 extra rerolls.";
                MessageBox.Show(msg, "Reroll Limit", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }


        private void RefreshQuestUI()
        {
            var questService = App.Quests;
            if (questService == null) return;

            // Proactively recalculate streak from calendar so stale values are caught immediately
            questService.RecalculateStreak();

            // Update season title from server or defaults
            var seasonTitle = App.QuestDefinitions?.SeasonTitle;
            if (!string.IsNullOrEmpty(seasonTitle))
            {
                TxtSeasonTitle.Text = seasonTitle;
            }

            // Update daily quest counter badge
            int dailyCompleted = questService.GetDailyQuestsCompletedToday();
            TxtDailyQuestCounter.Text = $"{dailyCompleted}/{QuestService.MaxDailyQuestsPerDay}";
            bool allDailyDone = questService.AreAllDailyQuestsCompleted();

            // Update daily progress segments
            var goldBrush = _dailySegmentGold ??= new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00));
            var greyBrush = _dailySegmentGrey ??= new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x60));
            DailySegment1.Background = dailyCompleted >= 1 ? goldBrush : greyBrush;
            DailySegment2.Background = dailyCompleted >= 2 ? goldBrush : greyBrush;
            DailySegment3.Background = dailyCompleted >= 3 ? goldBrush : greyBrush;

            // Refresh daily quest display
            var dailyDef = questService.GetCurrentDailyDefinition();
            var dailyProgress = questService.Progress.DailyQuest;
            if (allDailyDone)
            {
                // All 3 daily quests completed - show the "all done" message
                DailyQuestCard.Visibility = Visibility.Collapsed;
                DailyAllCompletedMessage.Visibility = Visibility.Visible;
                BtnRerollDaily.Visibility = Visibility.Collapsed;
            }
            else if (dailyDef != null && dailyProgress != null)
            {
                DailyQuestCard.Visibility = Visibility.Visible;
                DailyAllCompletedMessage.Visibility = Visibility.Collapsed;
                BtnRerollDaily.Visibility = Visibility.Visible;

                TxtDailyQuestIcon.Text = dailyDef.Icon;
                TxtDailyQuestName.Text = App.Mods?.MakeModAware(dailyDef.Name) ?? dailyDef.Name;
                TxtDailyQuestDesc.Text = App.Mods?.MakeModAware(dailyDef.Description) ?? dailyDef.Description;
                TxtDailyProgress.Text = $"{dailyProgress.CurrentProgress} / {dailyDef.TargetValue}";
                // Show scaled XP based on level (+4% per level), reroll bonus, and streak bonus
                var playerLevel = App.Settings?.Current?.PlayerLevel ?? 1;
                var rerollMult = App.SkillTree?.GetRerollBonusMultiplier() ?? 1.0;
                var questStreak = App.Settings?.Current?.DailyQuestStreak ?? 0;
                var streakMult = 1.0 + (questStreak * 0.03);
                var scaledDailyXP = (int)Math.Round(dailyDef.XPReward * (1 + playerLevel * 0.04) * rerollMult * streakMult);
                TxtDailyXP.Text = $"🎁 {scaledDailyXP} XP";
                if (questStreak > 0)
                {
                    TxtDailyStreakBonus.Text = $"(+{questStreak * 3}%\U0001f525)";
                    TxtDailyStreakBonus.Visibility = Visibility.Visible;
                }
                else
                {
                    TxtDailyStreakBonus.Visibility = Visibility.Collapsed;
                }
                if (rerollMult > 1.0)
                {
                    TxtDailyRerollBonus.Text = $"(+{(int)((rerollMult - 1.0) * 100)}%\U0001f503)";
                    TxtDailyRerollBonus.Visibility = Visibility.Visible;
                }
                else
                {
                    TxtDailyRerollBonus.Visibility = Visibility.Collapsed;
                }

                // Load quest image (supports remote cached images)
                try
                {
                    var dailyImagePath = GetModeAwareQuestImagePath(dailyDef);
                    var dailyImage = LoadQuestImage(dailyImagePath);
                    if (dailyImage != null)
                    {
                        ImgDailyQuest.Source = dailyImage;
                    }
                }
                catch { /* Image load failed, leave blank */ }

                // Update progress bar
                double progressPercent = dailyDef.TargetValue > 0
                    ? Math.Min(1.0, (double)dailyProgress.CurrentProgress / dailyDef.TargetValue)
                    : 0;
                DailyProgressFill.Width = DailyProgressTrack.ActualWidth > 0
                    ? DailyProgressTrack.ActualWidth * progressPercent
                    : 0;

                // Show completed overlay if done (briefly visible before next quest loads)
                if (dailyProgress.IsCompleted)
                {
                    DailyCompletedOverlay.Visibility = Visibility.Visible;
                    BtnRerollDaily.IsEnabled = false;
                    BtnRerollDaily.Content = Loc.Get("btn_completed");
                }
                else
                {
                    DailyCompletedOverlay.Visibility = Visibility.Collapsed;
                    int remainingRerolls = questService.GetRemainingDailyRerolls();
                    BtnRerollDaily.IsEnabled = remainingRerolls > 0;
                    BtnRerollDaily.Content = remainingRerolls > 0 ? $"🔄 Reroll ({remainingRerolls} left)" : "🔄 No rerolls left";
                }
            }

            // Refresh weekly quest display
            var weeklyDef = questService.GetCurrentWeeklyDefinition();
            var weeklyProgress = questService.Progress.WeeklyQuest;
            if (weeklyDef != null && weeklyProgress != null)
            {
                TxtWeeklyQuestIcon.Text = weeklyDef.Icon;
                TxtWeeklyQuestName.Text = App.Mods?.MakeModAware(weeklyDef.Name) ?? weeklyDef.Name;
                TxtWeeklyQuestDesc.Text = App.Mods?.MakeModAware(weeklyDef.Description) ?? weeklyDef.Description;
                TxtWeeklyProgress.Text = $"{weeklyProgress.CurrentProgress} / {weeklyDef.TargetValue}";
                // Show scaled XP based on level (+4% per level), reroll bonus, and streak bonus
                var wPlayerLevel = App.Settings?.Current?.PlayerLevel ?? 1;
                var wRerollMult = App.SkillTree?.GetRerollBonusMultiplier() ?? 1.0;
                var wQuestStreak = App.Settings?.Current?.DailyQuestStreak ?? 0;
                var wStreakMult = 1.0 + (wQuestStreak * 0.03);
                var scaledWeeklyXP = (int)Math.Round(weeklyDef.XPReward * (1 + wPlayerLevel * 0.04) * wRerollMult * wStreakMult);
                TxtWeeklyXP.Text = $"🎁 {scaledWeeklyXP} XP";
                if (wQuestStreak > 0)
                {
                    TxtWeeklyStreakBonus.Text = $"(+{wQuestStreak * 3}%\U0001f525)";
                    TxtWeeklyStreakBonus.Visibility = Visibility.Visible;
                }
                else
                {
                    TxtWeeklyStreakBonus.Visibility = Visibility.Collapsed;
                }
                if (wRerollMult > 1.0)
                {
                    TxtWeeklyRerollBonus.Text = $"(+{(int)((wRerollMult - 1.0) * 100)}%\U0001f503)";
                    TxtWeeklyRerollBonus.Visibility = Visibility.Visible;
                }
                else
                {
                    TxtWeeklyRerollBonus.Visibility = Visibility.Collapsed;
                }

                // Load quest image (supports remote cached images)
                try
                {
                    var weeklyImagePath = GetModeAwareQuestImagePath(weeklyDef);
                    var weeklyImage = LoadQuestImage(weeklyImagePath);
                    if (weeklyImage != null)
                    {
                        ImgWeeklyQuest.Source = weeklyImage;
                    }
                }
                catch { /* Image load failed, leave blank */ }

                // Update progress bar
                double progressPercent = weeklyDef.TargetValue > 0
                    ? Math.Min(1.0, (double)weeklyProgress.CurrentProgress / weeklyDef.TargetValue)
                    : 0;
                WeeklyProgressFill.Width = WeeklyProgressTrack.ActualWidth > 0
                    ? WeeklyProgressTrack.ActualWidth * progressPercent
                    : 0;

                // Show completed overlay if done
                if (weeklyProgress.IsCompleted)
                {
                    WeeklyCompletedOverlay.Visibility = Visibility.Visible;
                    BtnRerollWeekly.IsEnabled = false;
                    BtnRerollWeekly.Content = Loc.Get("btn_completed");
                }
                else
                {
                    WeeklyCompletedOverlay.Visibility = Visibility.Collapsed;
                    int remainingRerolls = questService.GetRemainingWeeklyRerolls();
                    BtnRerollWeekly.IsEnabled = remainingRerolls > 0;
                    BtnRerollWeekly.Content = remainingRerolls > 0 ? $"🔄 Reroll ({remainingRerolls} left)" : "🔄 No rerolls left";
                }
            }

            // Update statistics
            TxtTotalDailyCompleted.Text = questService.Progress.TotalDailyQuestsCompleted.ToString();
            TxtTotalWeeklyCompleted.Text = questService.Progress.TotalWeeklyQuestsCompleted.ToString();
            TxtTotalQuestXP.Text = questService.Progress.TotalXPFromQuests.ToString();

            // Update header stats
            int completedToday = dailyCompleted + (weeklyProgress?.IsCompleted == true ? 1 : 0);
            TxtQuestStats.Text = $"{completedToday} completed today";

            // Refresh streak calendar
            RefreshStreakCalendar();
        }

        private void RefreshStreakCalendar()
        {
            if (StreakCalendarCanvas == null) return;

            StreakCalendarCanvas.Children.Clear();

            var questService = App.Quests;
            var completedDates = new HashSet<DateTime>(
                questService?.Progress?.DailyQuestCompletionDates?.Select(d => d.Date)
                ?? Enumerable.Empty<DateTime>());

            var shieldedDates = new HashSet<DateTime>(
                App.Settings?.Current?.StreakShieldUsedDates?.Select(d => d.Date)
                ?? Enumerable.Empty<DateTime>());

            var today = DateTime.Today;

            // Show current month's days
            int daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
            var days = Enumerable.Range(1, daysInMonth)
                .Select(d => new DateTime(today.Year, today.Month, d)).ToList();

            // Canvas doesn't auto-stretch, so use parent's actual width minus padding
            double canvasWidth = StreakCalendarCanvas.ActualWidth;
            if (canvasWidth <= 0)
            {
                var parent = StreakCalendarCanvas.Parent as FrameworkElement;
                canvasWidth = parent?.ActualWidth ?? 0;
            }
            if (canvasWidth <= 0) canvasWidth = 600;

            double spacing = canvasWidth / daysInMonth;
            double centerY = 25;

            double prevCenterX = 0;
            bool prevCompleted = false;
            bool hasMissedDays = false;

            string[] dayLetters = { "S", "M", "T", "W", "T", "F", "S" };

            for (int i = 0; i < days.Count; i++)
            {
                var day = days[i];
                bool isSunday = day.DayOfWeek == DayOfWeek.Sunday;
                bool isToday = day.Date == today;
                bool isCompleted = completedDates.Contains(day.Date);
                bool isFuture = day.Date > today;
                bool isMissed = !isCompleted && !isFuture && day.Date < today;

                if (isMissed) hasMissedDays = true;

                double nodeSize = isSunday ? 26 : 20;
                double centerX = spacing * i + spacing / 2.0;

                // Draw connecting line from previous node
                if (i > 0)
                {
                    var line = new System.Windows.Shapes.Line
                    {
                        X1 = prevCenterX,
                        Y1 = centerY,
                        X2 = centerX,
                        Y2 = centerY,
                        StrokeThickness = 2,
                        Stroke = (isCompleted && prevCompleted)
                            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4"))
                            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3D3D60"))
                    };
                    Canvas.SetZIndex(line, 0);
                    StreakCalendarCanvas.Children.Add(line);
                }

                // Draw node (rounded rectangle to fit text)
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = nodeSize,
                    Height = nodeSize,
                    RadiusX = nodeSize / 2.0,
                    RadiusY = nodeSize / 2.0,
                    Fill = isCompleted
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4"))
                        : (SolidColorBrush)Application.Current.Resources["PanelBgBrush"],
                    Stroke = isToday
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD700"))
                        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3D3D60")),
                    StrokeThickness = isToday ? 2 : 1
                };

                Canvas.SetLeft(rect, centerX - nodeSize / 2.0);
                Canvas.SetTop(rect, centerY - nodeSize / 2.0);
                Canvas.SetZIndex(rect, 1);
                StreakCalendarCanvas.Children.Add(rect);

                // Day letter + day number label (e.g. "S1", "M2", "T3")
                string dayLetter = dayLetters[(int)day.DayOfWeek];
                var label = new TextBlock
                {
                    Text = $"{dayLetter}{day.Day}",
                    Foreground = isCompleted
                        ? Brushes.White
                        : isFuture
                            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#444444"))
                            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888")),
                    FontSize = 7,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center
                };
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(label, centerX - label.DesiredSize.Width / 2.0);
                Canvas.SetTop(label, centerY - label.DesiredSize.Height / 2.0);
                Canvas.SetZIndex(label, 2);
                StreakCalendarCanvas.Children.Add(label);

                // Shield overlay on days protected by streak shield
                if (shieldedDates.Contains(day.Date))
                {
                    var shieldLabel = new TextBlock
                    {
                        Text = "🛡️",
                        FontFamily = new FontFamily("Segoe UI Emoji"),
                        FontSize = 10,
                        TextAlignment = TextAlignment.Center
                    };
                    shieldLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Canvas.SetLeft(shieldLabel, centerX - shieldLabel.DesiredSize.Width / 2.0);
                    Canvas.SetTop(shieldLabel, centerY - nodeSize / 2.0 - shieldLabel.DesiredSize.Height + 2);
                    Canvas.SetZIndex(shieldLabel, 4);
                    StreakCalendarCanvas.Children.Add(shieldLabel);
                }

                // In fix mode, overlay a pulsing pink highlight on missed days
                if (_isStreakFixMode && isMissed)
                {
                    double highlightSize = nodeSize + 4;
                    var highlight = new System.Windows.Shapes.Rectangle
                    {
                        Width = highlightSize,
                        Height = highlightSize,
                        RadiusX = highlightSize / 2.0,
                        RadiusY = highlightSize / 2.0,
                        Fill = Brushes.Transparent,
                        Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4")),
                        StrokeThickness = 2,
                        Cursor = System.Windows.Input.Cursors.Hand,
                        Tag = day.Date
                    };

                    // Pulsing opacity animation
                    var pulseAnim = new System.Windows.Media.Animation.DoubleAnimation
                    {
                        From = 1.0,
                        To = 0.3,
                        Duration = TimeSpan.FromMilliseconds(600),
                        AutoReverse = true,
                        RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
                    };
                    highlight.BeginAnimation(OpacityProperty, pulseAnim);

                    highlight.MouseLeftButtonDown += StreakFixDay_Click;

                    Canvas.SetLeft(highlight, centerX - highlightSize / 2.0);
                    Canvas.SetTop(highlight, centerY - highlightSize / 2.0);
                    Canvas.SetZIndex(highlight, 3);
                    StreakCalendarCanvas.Children.Add(highlight);
                }

                prevCenterX = centerX;
                prevCompleted = isCompleted;
            }

            // Update streak text
            var streak = App.Settings?.Current?.DailyQuestStreak ?? 0;
            TxtQuestStreakCount.Text = streak > 0 ? $"\U0001f525 {streak} day streak (+{streak * 3}% XP)" : "";

            // Show/hide/enable Fix Day button based on skill, XP, season usage, and missed days
            var settings = App.Settings?.Current;
            bool hasSkill = App.SkillTree?.HasSkill("oopsie_insurance") == true;
            bool alreadyUsed = settings?.SeasonalStreakRecoveryUsed == true;
            bool hasEnoughXP = (settings?.PlayerXP ?? 0) >= 500;

            if (hasSkill)
            {
                BtnFixStreak.Visibility = Visibility.Visible;
                BtnFixStreak.IsEnabled = !_isStreakFixMode || _isStreakFixMode; // Always enabled when skill owned

                if (_isStreakFixMode)
                {
                    BtnFixStreak.Content = Loc.Get("btn_cancel_2");
                }
                else
                {
                    BtnFixStreak.Content = Loc.Get("btn_fix_day");
                }

                if (alreadyUsed)
                    BtnFixStreak.ToolTip = Loc.Get("tooltip_already_used_this_season");
                else if (!hasEnoughXP)
                    BtnFixStreak.ToolTip = Loc.Get("tooltip_requires_500_xp");
                else if (!hasMissedDays)
                    BtnFixStreak.ToolTip = Loc.Get("tooltip_no_missed_days_your_streak_is_perfect");
                else
                    BtnFixStreak.ToolTip = Loc.Get("tooltip_use_oopsie_insurance_to_fix_a_missed_day_500");
            }
            else
            {
                BtnFixStreak.Visibility = Visibility.Collapsed;
            }
        }

        private void StreakCalendarCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RefreshStreakCalendar();
        }

        private void BtnFixStreak_Click(object sender, RoutedEventArgs e)
        {
            if (_isStreakFixMode)
            {
                ExitStreakFixMode();
                return;
            }

            // Validate prerequisites with user-friendly messages
            var settings = App.Settings?.Current;
            if (settings == null) return;
            if (App.SkillTree?.HasSkill("oopsie_insurance") != true) return;

            if (settings.SeasonalStreakRecoveryUsed)
            {
                TxtFixStreakStatus.Text = Loc.Get("label_already_used_oopsie_insurance_this_season");
                TxtFixStreakStatus.Visibility = Visibility.Visible;
                return;
            }

            // Check if there are any missed days
            var questService = App.Quests;
            var completedDates = new HashSet<DateTime>(
                questService?.Progress?.DailyQuestCompletionDates?.Select(d => d.Date)
                ?? Enumerable.Empty<DateTime>());
            var today = DateTime.Today;
            bool hasMissedDays = Enumerable.Range(1, today.Day - 1)
                .Select(d => new DateTime(today.Year, today.Month, d))
                .Any(d => !completedDates.Contains(d.Date));

            if (!hasMissedDays)
            {
                TxtFixStreakStatus.Text = Loc.Get("label_no_broken_streak_you_re_doing_great_sweetie");
                TxtFixStreakStatus.Visibility = Visibility.Visible;
                return;
            }

            if (settings.PlayerXP < 500)
            {
                TxtFixStreakStatus.Text = Loc.Get("label_not_enough_xp_you_need_500_xp_to_fix_a_day");
                TxtFixStreakStatus.Visibility = Visibility.Visible;
                return;
            }

            // Enter fix mode
            _isStreakFixMode = true;
            TxtFixStreakStatus.Text = Loc.Get("label_click_a_missed_day_to_fix_it_costs_500_xp_onc");
            TxtFixStreakStatus.Visibility = Visibility.Visible;
            RefreshStreakCalendar();
        }

        private void ExitStreakFixMode()
        {
            _isStreakFixMode = false;
            TxtFixStreakStatus.Visibility = Visibility.Collapsed;
            TxtFixStreakStatus.Text = "";
            RefreshStreakCalendar();
        }

        private async void StreakFixDay_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not System.Windows.Shapes.Rectangle highlight) return;
            if (highlight.Tag is not DateTime fixDate) return;

            var settings = App.Settings?.Current;
            if (settings == null) return;

            // Confirm with user
            var result = MessageBox.Show(
                $"Fix {fixDate:MMMM d}?\n\nThis will cost 500 XP and can only be used once per season.",
                "Oopsie Insurance",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            // Use server-side oopsie insurance if online
            var fixDateStr = fixDate.ToString("yyyy-MM-dd");
            if (App.ProfileSync != null && !string.IsNullOrEmpty(App.Settings?.Current?.UnifiedId))
            {
                TxtFixStreakStatus.Text = Loc.Get("label_processing");
                TxtFixStreakStatus.Visibility = Visibility.Visible;

                var (success, error, newXp) = await App.ProfileSync.UseOopsieInsuranceAsync(fixDateStr);
                if (!success)
                {
                    TxtFixStreakStatus.Text = $"❌ {error ?? "Failed to use Oopsie Insurance"}";
                    TxtFixStreakStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5252"));
                    TxtFixStreakStatus.Visibility = Visibility.Visible;
                    return;
                }

                // Server succeeded - update local state
                if (newXp.HasValue)
                {
                    // Server returns total XP; convert back to current-level XP
                    var currentLevel = settings.PlayerLevel;
                    var newLevelXp = App.Progression?.GetCurrentLevelXP(currentLevel, newXp.Value) ?? (settings.PlayerXP - 500);
                    settings.PlayerXP = Math.Max(0, newLevelXp);
                }
                else
                {
                    settings.PlayerXP -= 500;
                }
                settings.SeasonalStreakRecoveryUsed = true;
            }
            else
            {
                // No cloud account
                TxtFixStreakStatus.Text = Loc.Get("label_oopsie_insurance_requires_a_cloud_account_ple");
                TxtFixStreakStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5252"));
                TxtFixStreakStatus.Visibility = Visibility.Visible;
                return;
            }

            // Add the fixed date to completion dates
            var questService = App.Quests;
            if (questService?.Progress != null)
            {
                questService.Progress.DailyQuestCompletionDates.Add(fixDate);
                questService.Save();
            }

            // Recalculate the streak
            RecalculateDailyQuestStreak();

            App.Settings?.Save();
            App.Logger?.Information("Oopsie Insurance used to fix {Date} for 500 XP (server-validated)", fixDate);

            // Exit fix mode and refresh
            _isStreakFixMode = false;
            TxtFixStreakStatus.Text = $"✅ Fixed {fixDate:MMMM d}! Streak updated.";
            TxtFixStreakStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676"));
            TxtFixStreakStatus.Visibility = Visibility.Visible;
            RefreshStreakCalendar();

            // Auto-hide status after 3 seconds
            await Task.Delay(3000);
            if (!_isStreakFixMode)
            {
                TxtFixStreakStatus.Visibility = Visibility.Collapsed;
                TxtFixStreakStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4"));
            }
        }

        private void RecalculateDailyQuestStreak()
        {
            App.Quests?.RecalculateStreak();
        }

        private void BtnAchievements_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("achievements");
        }

        private void BtnCompanion_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("companion");
        }

        private void BtnLeaderboard_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("leaderboard");
            // Surface the Season Recap re-view button only when a persisted snapshot exists.
            try
            {
                if (BtnViewSeasonRecap != null)
                    BtnViewSeasonRecap.Visibility = Services.SeasonRecapService.HasAnySnapshot()
                        ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "SeasonRecap: failed to update re-view button visibility");
            }
        }

        /// <summary>Re-view the most recent season's recap card from its persisted snapshot.</summary>
        private void BtnViewSeasonRecap_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("season_recap"); } catch { }
            try
            {
                var snapshot = Services.SeasonRecapService.LoadLatest();
                if (snapshot == null)
                {
                    App.Notifications?.Show(Loc.Get("recap_toast_none"), Services.NotificationType.Info);
                    return;
                }
                var vm = new ViewModels.SeasonRecapViewModel(snapshot);
                var win = new Controls.SeasonRecapWindow(vm) { Owner = this };
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "SeasonRecap: failed to open re-view window");
            }
        }

        private void BtnLab_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("lab");
        }

        // ─── [DEBUG] Webcam smoke test — TEMPORARY, remove with the XAML card ───
        private bool _webcamDebugSubscribed;
        private int _webcamDebugBlinkCount;
        private int _webcamDebugMouthOpenCount;
        private int _webcamDebugTongueOutCount;
        private GazeSide _webcamDebugLastGaze = GazeSide.Center;
        private bool _webcamDebugLastGazeSet;
        private string _webcamDebugFaceLabel = "—";

        // Stored delegate refs so EnsureWebcamDebugSubscribed's six lambdas can
        // actually be unhooked from App.Webcam in OnClosing — the pre-existing
        // _webcamDebugSubscribed flag only blocked re-subscription, it didn't
        // tear down. Without these the lambdas (which capture `this`) hold a
        // reference to MainWindow forever.
        private Action<WebcamTrackingState>? _onDebugStateChanged;
        private Action? _onDebugFaceFound;
        private Action? _onDebugFaceLost;
        private Action? _onDebugBlink;
        private Action? _onDebugMouthOpen;
        private Action? _onDebugTongueOut;
        private Action<GazeSide>? _onDebugGazeSide;

        // Camera-active pill in the title bar — visible whenever any webcam
        // feature has the capture loop running. Stored handler so we can
        // unhook in OnClosing alongside the debug subscriptions above.
        private Action<WebcamTrackingState>? _onPillStateChanged;

        private void WireWebcamActivePill()
        {
            if (App.Webcam == null || _onPillStateChanged != null) return;

            void Update(WebcamTrackingState s)
            {
                if (WebcamActivePill != null)
                {
                    WebcamActivePill.Visibility = (s == WebcamTrackingState.Tracking || s == WebcamTrackingState.FaceLost)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
                UpdateLabTrackerUi(s);
            }

            _onPillStateChanged = Update;
            App.Webcam.OnTrackingStateChanged += _onPillStateChanged;
            // Reflect current state on wire-up — service may already be running
            // if we got here after a previous Stop/Start cycle.
            Update(App.Webcam.State);
        }

        // --- Rapid 6-blink recalibration gesture -----------------------------
        // Blinking fast 6 times in a row halts all active conditioning (keeping
        // the camera on) and offers recalibration. The blink detector enforces a
        // 500ms cooldown between fires (WebcamTrackingService.BlinkCooldownMs),
        // so 6 blinks physically span >=2.5s — a literal "6 in 2s" can't fire.
        // 3.5s is the achievable window, and still far above the natural blink
        // rate (~1 per 3-4s) so spontaneous blinking never triggers it.
        private const int RapidBlinkRecalCount = 6;
        private const int RapidBlinkRecalWindowMs = 3500;
        private readonly Queue<DateTime> _rapidBlinkTimes = new();
        private Action? _onRapidBlinkRecal;
        private bool _rapidBlinkRecalInProgress;

        private void WireRapidBlinkRecalibrateShortcut()
        {
            if (App.Webcam == null || _onRapidBlinkRecal != null) return;

            void OnBlink()
            {
                // Opt-in via the toggle shown on every webcam card.
                if (App.Settings?.Current?.BlinkRecalibrateShortcutEnabled != true) return;
                // Don't fire while a calibration window is already open (its
                // verify step asks the user to blink) or while we're mid-trigger.
                if (_rapidBlinkRecalInProgress || WebcamCalibrationWindow.IsShowing) return;

                var now = DateTime.UtcNow;
                _rapidBlinkTimes.Enqueue(now);
                var cutoff = now.AddMilliseconds(-RapidBlinkRecalWindowMs);
                while (_rapidBlinkTimes.Count > 0 && _rapidBlinkTimes.Peek() < cutoff)
                    _rapidBlinkTimes.Dequeue();

                if (_rapidBlinkTimes.Count >= RapidBlinkRecalCount)
                {
                    _rapidBlinkTimes.Clear();
                    _ = TriggerRapidBlinkRecalibrateAsync();
                }
            }

            _onRapidBlinkRecal = OnBlink;
            App.Webcam.OnBlink += _onRapidBlinkRecal;
        }

        private async Task TriggerRapidBlinkRecalibrateAsync()
        {
            if (_rapidBlinkRecalInProgress) return;
            _rapidBlinkRecalInProgress = true;
            try
            {
                App.Logger?.Information("Rapid 6-blink gesture: stopping all activity and offering recalibration.");

                // Halt everything the user is experiencing — same surface as a
                // panic press — but DELIBERATELY leave App.Webcam running: the
                // calibration window requires the capture loop to be live.
                StopAllForRecalibration();

                // Guard against a race where the capture loop stopped between the
                // triggering blink and here.
                var svc = App.Webcam;
                if (svc != null && !svc.IsRunning)
                    await Task.Run(() => svc.Start());
                if (svc == null || !svc.IsRunning)
                {
                    App.Logger?.Warning("Rapid-blink recal: webcam not running and could not be (re)started; aborting.");
                    return;
                }

                var choice = MessageBox.Show(this,
                    "You blinked to stop everything. Recalibrate webcam tracking now?",
                    "Recalibrate?",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (choice != MessageBoxResult.Yes) return;

                WebcamCalibrationWindow.ShowDialogWithRecalibrate(this);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Rapid-blink recalibration failed");
            }
            finally
            {
                _rapidBlinkRecalInProgress = false;
                _rapidBlinkTimes.Clear();
            }
        }

        /// <summary>
        /// Stops all active conditioning output (engine, session, videos, audio,
        /// gaze features) — mirroring the "stop" branch of the panic key, minus
        /// the window-restore / exit / achievement bookkeeping. Leaves the
        /// webcam capture loop (App.Webcam) RUNNING so recalibration can proceed.
        /// </summary>
        private void StopAllForRecalibration()
        {
            try { Controls.HelpPopover.CloseActive(); } catch { }
            try { App.KillAllAudio(); } catch (Exception ex) { App.Logger?.Warning(ex, "Recal stop: KillAllAudio failed"); }
            try { App.Autonomy?.CancelActivePulses(); } catch { }
            try { App.GazeFocus?.Stop(); } catch { }
            try { App.BlinkTrainer?.Stop(); } catch { }
            try
            {
                if (_sessionEngine != null && _sessionEngine.IsRunning && !_sessionEngine.IsPaused)
                {
                    _sessionEngine.PauseSession();
                    if (TxtPauseIcon != null) TxtPauseIcon.Text = "▶";
                    if (BtnPauseSession != null) BtnPauseSession.ToolTip = Loc.Get("tooltip_resume_session");
                }
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "Recal stop: pause session failed"); }
            try { if (_isRunning) StopEngine(); } catch (Exception ex) { App.Logger?.Warning(ex, "Recal stop: StopEngine failed"); }
            try { App.InteractionQueue?.ForceReset(); } catch { }
        }

        // --- "Blink to recalibrate" toggle, mirrored on every webcam card -----
        // A single setting (BlinkRecalibrateShortcutEnabled) surfaced as a small
        // checkbox on each webcam card. Toggling any one writes the setting and
        // keeps the others in sync.
        private bool _syncingBlinkRecalToggles;

        private void ChkBlinkRecalShortcut_Changed(object sender, RoutedEventArgs e)
        {
            if (_syncingBlinkRecalToggles) return;
            bool val = (sender as System.Windows.Controls.CheckBox)?.IsChecked == true;
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.BlinkRecalibrateShortcutEnabled = val;
                App.Settings?.Save();
            }
            SyncBlinkRecalToggles(val);
        }

        private void SyncBlinkRecalToggles(bool val)
        {
            _syncingBlinkRecalToggles = true;
            try
            {
                var boxes = new[]
                {
                    ChkBlinkRecalGaze, ChkBlinkRecalFocus, ChkBlinkRecalWebcamBar,
                    ChkBlinkRecalBlinkTrainer, ChkBlinkRecalDeeper
                };
                foreach (var cb in boxes)
                {
                    if (cb != null && cb.IsChecked != val) cb.IsChecked = val;
                }
            }
            finally { _syncingBlinkRecalToggles = false; }
        }

        // Lab redesign: reflect tracker state on the Eyes engine-bar status pill and
        // dim the two Eyes cards (with "start tracking" hints) when the tracker is off.
        // Additive — keyed off the same OnTrackingStateChanged path as the title pill.
        private void UpdateLabTrackerUi(WebcamTrackingState s)
        {
            bool live = (s == WebcamTrackingState.Tracking || s == WebcamTrackingState.FaceLost);
            var green = TryFindResource("SuccessGreenBrush") as Brush;
            var muted = TryFindResource("TextMutedBrush") as Brush;
            var panelAccent = TryFindResource("PanelAccentBrush") as Brush;

            if (LabTrackerDot != null) LabTrackerDot.Fill = live ? (green ?? LabTrackerDot.Fill) : (muted ?? LabTrackerDot.Fill);
            if (LabTrackerPill != null) LabTrackerPill.BorderBrush = live ? (green ?? LabTrackerPill.BorderBrush) : (panelAccent ?? LabTrackerPill.BorderBrush);

            if (LabGazeCard != null) LabGazeCard.Opacity = live ? 1.0 : 0.62;
            if (LabFocusCard != null) LabFocusCard.Opacity = live ? 1.0 : 0.62;
            if (LabGazeNeedsTracker != null) LabGazeNeedsTracker.Visibility = live ? Visibility.Collapsed : Visibility.Visible;
            if (LabFocusNeedsTracker != null) LabFocusNeedsTracker.Visibility = live ? Visibility.Collapsed : Visibility.Visible;
        }

        private void WebcamActivePill_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Click is the panic-stop affordance. Stops every consumer that
            // shares App.Webcam — Webcam Triggers, Focus Gaze, Blink Trainer,
            // Gaze Minigame all release together when the service stops.
            try { App.GazeFocus?.Stop(); } catch { }
            try { App.BlinkTrainer?.Stop(); } catch { }
            try { App.Webcam?.Stop(); } catch { }
        }

        private async void BtnWebcamDebugStart_Click(object sender, RoutedEventArgs e)
        {
            var svc = App.Webcam;
            if (svc == null)
            {
                AppendWebcamDebugLog("App.Webcam is null — service not initialized");
                return;
            }

            if (svc.IsRunning)
            {
                svc.Stop();
                BtnWebcamDebugStart.Content = "Start tracking";
                AppendWebcamDebugLog("Stop requested.");
                RefreshBlinkTrainerTrackerButton();
                return;
            }

            if (!WebcamTrackingService.IsConsentCurrent())
            {
                AppendWebcamDebugLog("Consent not given — opening consent dialog…");
                var dlg = new WebcamConsentDialog { Owner = this };
                var ok = dlg.ShowDialog();
                if (ok != true || !dlg.ConsentGiven)
                {
                    AppendWebcamDebugLog("Consent declined or dialog cancelled.");
                    return;
                }
                AppendWebcamDebugLog("Consent granted.");
            }

            EnsureWebcamDebugSubscribed();
            _webcamDebugBlinkCount = 0;
            _webcamDebugMouthOpenCount = 0;
            _webcamDebugTongueOutCount = 0;
            _webcamDebugLastGazeSet = false;
            _webcamDebugFaceLabel = "—";
            UpdateWebcamDebugCounters();

            var started = await StartWebcamOffUiThreadAsync(svc);
            if (started)
            {
                BtnWebcamDebugStart.Content = "Stop tracking";
                AppendWebcamDebugLog("Start() returned true — capture thread launching.");
            }
            else
            {
                AppendWebcamDebugLog($"Start() returned false. State={svc.State}. See logs/app.log.");
            }
            RefreshBlinkTrainerTrackerButton();
        }

        // Webcam Start() does VideoCapture open + 3 ONNX InferenceSession ctors
        // synchronously. On slow USB negotiation or driver-init paths that can
        // block 10-30s; doing it on the UI thread freezes the window long
        // enough for Windows' "not responding" reaper to terminate the app
        // (XTNSN's BUG-T3HE68DHXY pattern: instant freeze on click → silent
        // crash 10-15s later, no managed exception). Hop to a worker thread.
        private async Task<bool> StartWebcamOffUiThreadAsync(WebcamTrackingService svc)
        {
            AppendWebcamDebugLog("Starting webcam (camera open + model load can take a few seconds)…");
            if (TxtWebcamDebugStatus != null) TxtWebcamDebugStatus.Text = "Starting…";
            // The movable loading splash is driven globally off the service's
            // OnStartupProgress event (see InstallWebcamLoadingSplash), so it
            // shows no matter which code path calls Start() — not just this one.
            try
            {
                return await Task.Run(() => svc.Start());
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "MainWindow: webcam Start() threw on worker thread");
                AppendWebcamDebugLog($"Start() threw: {ex.Message}");
                return false;
            }
        }

        // The webcam loading splash is shown/updated/closed purely in response
        // to WebcamTrackingService.OnStartupProgress (fired from inside Start())
        // and OnTrackingStateChanged. Wiring it here, once, means every entry
        // point that starts the engine — the Lab debug button, calibration, the
        // Blink Trainer, the enhanced-video / browser nudges, the mandatory
        // video player — gets the splash without each call site knowing about
        // it. All these events are marshalled to the UI thread by the service.
        private WebcamLoadingSplash? _webcamLoadingSplash;
        private Action<double, string>? _onWebcamStartupProgress;
        private Action<WebcamTrackingState>? _onWebcamStartupState;

        private void InstallWebcamLoadingSplash()
        {
            if (App.Webcam == null || _onWebcamStartupProgress != null) return;

            _onWebcamStartupProgress = (progress, status) =>
            {
                try
                {
                    if (progress >= 1.0)
                    {
                        // Engine is up — show the bar full for a beat, then fade.
                        _webcamLoadingSplash?.SetProgress(1.0, status);
                        _webcamLoadingSplash?.CloseSplash();
                        return;
                    }

                    if (_webcamLoadingSplash == null)
                    {
                        // Don't pop a splash if the main window isn't on screen
                        // (e.g. minimized to tray during a background start).
                        if (!IsVisible) return;
                        var splash = new WebcamLoadingSplash { Owner = this };
                        splash.Closed += (s, e) =>
                        {
                            if (ReferenceEquals(_webcamLoadingSplash, splash)) _webcamLoadingSplash = null;
                        };
                        _webcamLoadingSplash = splash;
                        splash.Show();
                    }
                    _webcamLoadingSplash.SetProgress(progress, status);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "MainWindow: webcam loading splash update failed");
                }
            };

            _onWebcamStartupState = state =>
            {
                if (_webcamLoadingSplash == null) return;
                // Start() failed or was aborted before reaching 1.0. Surface WHY
                // rather than letting the bar silently vanish (#300) or hang
                // forever waiting on a wedged camera open (#311).
                switch (state)
                {
                    case WebcamTrackingState.CameraInUse:
                        _webcamLoadingSplash.ShowErrorAndClose(
                            "Camera unavailable — it may be in use by another app, or blocked by antivirus / Windows camera privacy.");
                        break;
                    case WebcamTrackingState.CameraDenied:
                        _webcamLoadingSplash.ShowErrorAndClose(
                            "Camera access denied — enable it in Windows Settings ▸ Privacy ▸ Camera, then try again.");
                        break;
                    case WebcamTrackingState.Error:
                        _webcamLoadingSplash.ShowErrorAndClose(
                            "Eye-tracking engine failed to start. See the webcam debug log for details.");
                        break;
                    case WebcamTrackingState.Stopped:
                        _webcamLoadingSplash.CloseSplash();
                        break;
                }
            };

            App.Webcam.OnStartupProgress += _onWebcamStartupProgress;
            App.Webcam.OnTrackingStateChanged += _onWebcamStartupState;
        }

        private void EnsureWebcamDebugSubscribed()
        {
            if (_webcamDebugSubscribed || App.Webcam == null) return;
            _webcamDebugSubscribed = true;

            _onDebugStateChanged = s =>
            {
                if (TxtWebcamDebugStatus != null) TxtWebcamDebugStatus.Text = s.ToString();
                AppendWebcamDebugLog($"State → {s}");
                if (s == WebcamTrackingState.Stopped || s == WebcamTrackingState.Error
                    || s == WebcamTrackingState.CameraInUse || s == WebcamTrackingState.CameraDenied)
                {
                    if (BtnWebcamDebugStart != null) BtnWebcamDebugStart.Content = "Start tracking";
                }
            };
            _onDebugFaceFound = () =>
            {
                _webcamDebugFaceLabel = "yes";
                UpdateWebcamDebugCounters();
                AppendWebcamDebugLog("Face FOUND");
            };
            _onDebugFaceLost = () =>
            {
                _webcamDebugFaceLabel = "lost";
                UpdateWebcamDebugCounters();
                AppendWebcamDebugLog("Face LOST");
            };
            _onDebugBlink = () =>
            {
                _webcamDebugBlinkCount++;
                UpdateWebcamDebugCounters();
                AppendWebcamDebugLog($"Blink #{_webcamDebugBlinkCount}");
            };
            _onDebugMouthOpen = () =>
            {
                _webcamDebugMouthOpenCount++;
                AppendWebcamDebugLog($"Mouth-open #{_webcamDebugMouthOpenCount}");
            };
            _onDebugTongueOut = () =>
            {
                _webcamDebugTongueOutCount++;
                AppendWebcamDebugLog($"Tongue-out #{_webcamDebugTongueOutCount}");
            };
            _onDebugGazeSide = side =>
            {
                // Only log on CHANGE — gaze side fires every frame and would
                // otherwise drown out blinks and face events.
                if (_webcamDebugLastGazeSet && side == _webcamDebugLastGaze)
                {
                    _webcamDebugLastGaze = side;
                    return;
                }
                _webcamDebugLastGaze = side;
                _webcamDebugLastGazeSet = true;
                UpdateWebcamDebugCounters();
                AppendWebcamDebugLog($"Gaze → {side}");
            };

            App.Webcam.OnTrackingStateChanged += _onDebugStateChanged;
            App.Webcam.OnFaceFound += _onDebugFaceFound;
            App.Webcam.OnFaceLost += _onDebugFaceLost;
            App.Webcam.OnBlink += _onDebugBlink;
            App.Webcam.OnMouthOpen += _onDebugMouthOpen;
            App.Webcam.OnTongueOut += _onDebugTongueOut;
            App.Webcam.OnGazeSide += _onDebugGazeSide;
        }

        private void UnsubscribeWebcamDebug()
        {
            if (!_webcamDebugSubscribed || App.Webcam == null) return;
            if (_onDebugStateChanged != null) App.Webcam.OnTrackingStateChanged -= _onDebugStateChanged;
            if (_onDebugFaceFound    != null) App.Webcam.OnFaceFound -= _onDebugFaceFound;
            if (_onDebugFaceLost     != null) App.Webcam.OnFaceLost  -= _onDebugFaceLost;
            if (_onDebugBlink        != null) App.Webcam.OnBlink     -= _onDebugBlink;
            if (_onDebugMouthOpen    != null) App.Webcam.OnMouthOpen -= _onDebugMouthOpen;
            if (_onDebugTongueOut    != null) App.Webcam.OnTongueOut -= _onDebugTongueOut;
            if (_onDebugGazeSide     != null) App.Webcam.OnGazeSide  -= _onDebugGazeSide;
            _webcamDebugSubscribed = false;
        }

        private void UpdateWebcamDebugCounters()
        {
            if (TxtWebcamDebugCounters == null) return;
            var gaze = _webcamDebugLastGazeSet ? _webcamDebugLastGaze.ToString() : "—";
            TxtWebcamDebugCounters.Text = $"Face: {_webcamDebugFaceLabel} | Blinks: {_webcamDebugBlinkCount} | Gaze: {gaze}";
        }

        private async void BtnWebcamDebugCalibrate_Click(object sender, RoutedEventArgs e)
        {
            var svc = App.Webcam;
            if (svc == null)
            {
                AppendWebcamDebugLog("App.Webcam is null — service not initialized");
                return;
            }

            if (!WebcamTrackingService.IsConsentCurrent())
            {
                AppendWebcamDebugLog("Consent not given — opening consent dialog…");
                var consent = new WebcamConsentDialog { Owner = this };
                var ok = consent.ShowDialog();
                if (ok != true || !consent.ConsentGiven)
                {
                    AppendWebcamDebugLog("Consent declined.");
                    return;
                }
            }

            // Calibration window expects the service to be running so OnRawIris fires.
            EnsureWebcamDebugSubscribed();
            var startedHere = false;
            if (!svc.IsRunning)
            {
                if (!await StartWebcamOffUiThreadAsync(svc))
                {
                    AppendWebcamDebugLog($"Couldn't start tracking. State={svc.State}.");
                    return;
                }
                startedHere = true;
                BtnWebcamDebugStart.Content = "Stop tracking";
            }

            AppendWebcamDebugLog("Opening calibration window…");
            var result = WebcamCalibrationWindow.ShowDialogWithRecalibrate(this);

            if (result == true)
            {
                AppendWebcamDebugLog("Calibration applied. Gaze classification should now be much more accurate.");
            }
            else
            {
                AppendWebcamDebugLog("Calibration cancelled or failed.");
            }

            // Leave the service running if the user manually started it earlier.
            // Only auto-stop if calibration was the only reason it's running.
            if (startedHere && result != true)
            {
                svc.Stop();
                BtnWebcamDebugStart.Content = "Start tracking";
            }

            // Cross-tab propagation (Cleanup 2 + Phase D): the Blink Trainer
            // page shows calibration status AND has a NeedsCalibration status
            // state; refresh both surfaces if the user has visited the tab.
            RefreshBlinkTrainerWebcamColumn();
            RefreshBlinkTrainerStatusRow();
        }

        private void BtnGazeMinigame_Click(object sender, RoutedEventArgs e)
        {
            new Lab.GazeMinigame.GazeMinigameWindow { Owner = this }.Show();
        }

        // ─── Focus Gaze (Lab) ──────────────────────────────────────────
        private bool _focusGazeSyncing;

        private void HookFocusGazeService()
        {
            if (App.GazeFocus == null) return;

            // Belt-and-suspenders: stop GazeFocus before WPF checks its window
            // count on MainWindow close. Without this, the cursor window can
            // keep the OnLastWindowClose process alive — App.OnExit then never
            // runs, leaving Webcam.Dispose uncalled and the camera lit.
            Closing += (_, _) => App.GazeFocus?.Stop();

            App.GazeFocus.OnActiveChanged += active =>
            {
                // Service may stop itself (e.g., webcam death) — keep the
                // toggle visually in sync without re-entering the handler.
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.BeginInvoke(() => SyncFocusGazeToggle(active));
                    return;
                }
                SyncFocusGazeToggle(active);
            };
        }

        private void SyncFocusGazeToggle(bool active)
        {
            if (ChkFocusGaze == null) return;
            if (ChkFocusGaze.IsChecked == active) return;
            _focusGazeSyncing = true;
            try { ChkFocusGaze.IsChecked = active; }
            finally { _focusGazeSyncing = false; }
            if (TxtFocusGazeStatus != null && !active) TxtFocusGazeStatus.Text = "";
        }

        private async void ChkFocusGaze_Changed(object sender, RoutedEventArgs e)
        {
            if (_focusGazeSyncing) return;
            if (App.GazeFocus == null) return;

            var on = ChkFocusGaze.IsChecked == true;
            if (on)
            {
                if (!WebcamTrackingService.IsConsentCurrent())
                {
                    var dlg = new WebcamConsentDialog { Owner = this };
                    var ok = dlg.ShowDialog();
                    if (ok != true || !dlg.ConsentGiven)
                    {
                        SyncFocusGazeToggle(false);
                        if (TxtFocusGazeStatus != null) TxtFocusGazeStatus.Text = Localization.Loc.Get("label_focus_gaze_consent_required");
                        return;
                    }
                }

                // Pre-warm the webcam off the UI thread so GazeFocus.Start —
                // which would otherwise call WebcamTrackingService.Start
                // synchronously — finds it already running and just subscribes.
                if (App.Webcam != null && !App.Webcam.IsRunning)
                {
                    if (TxtFocusGazeStatus != null) TxtFocusGazeStatus.Text = "Starting webcam…";
                    var started = await Task.Run(() => App.Webcam.Start());
                    if (!started)
                    {
                        SyncFocusGazeToggle(false);
                        if (TxtFocusGazeStatus != null)
                            TxtFocusGazeStatus.Text = Localization.Loc.GetF("label_focus_gaze_webcam_failed_format", App.Webcam?.State);
                        return;
                    }
                }

                if (App.GazeFocus.Start())
                {
                    if (TxtFocusGazeStatus != null) TxtFocusGazeStatus.Text = Localization.Loc.Get("label_focus_gaze_active");
                }
                else
                {
                    SyncFocusGazeToggle(false);
                    if (TxtFocusGazeStatus != null)
                    {
                        if (App.Webcam?.Calibration == null)
                            TxtFocusGazeStatus.Text = Localization.Loc.Get("label_focus_gaze_calibrate_first");
                        else
                            TxtFocusGazeStatus.Text = Localization.Loc.GetF("label_focus_gaze_webcam_failed_format", App.Webcam?.State);
                    }
                }
            }
            else
            {
                App.GazeFocus.Stop();
                if (TxtFocusGazeStatus != null) TxtFocusGazeStatus.Text = "";
            }
        }

        // ─── Blink Trainer — service lifecycle + Running countdown ───────
        // The configurator + stage UI now lives on the dedicated Exclusives
        // page (see "Blink Trainer Tab — *" regions). What's left here is the
        // service-side glue: hook StateChanged, run the 1Hz countdown timer
        // that the new status row's Running text depends on, and stop on
        // window close.
        private DispatcherTimer? _blinkTrainerTickTimer;

        private void HookBlinkTrainerService()
        {
            if (App.BlinkTrainer == null) return;

            // Stop on MainWindow close so the camera doesn't stay lit if the
            // overlay window is the only thing keeping the process alive.
            Closing += (_, _) => App.BlinkTrainer?.Stop();

            App.BlinkTrainer.StateChanged += () =>
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.BeginInvoke(OnBlinkTrainerServiceStateChanged);
                    return;
                }
                OnBlinkTrainerServiceStateChanged();
            };

            // Defensive: if the service is somehow already running at hook
            // time (shouldn't happen — hook runs at window load before the
            // user can start anything — but cheap insurance), make sure the
            // countdown timer is wired.
            SyncBlinkTrainerCountdownTimer();
        }

        /// <summary>
        /// Single fan-out for BlinkTrainerService.StateChanged. Updates the
        /// new Exclusives tab's status row + stage mode and manages the
        /// per-second countdown timer that BlinkTrainerTick drives.
        /// </summary>
        private void OnBlinkTrainerServiceStateChanged()
        {
            try { SyncBlinkTrainerCountdownTimer(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "SyncBlinkTrainerCountdownTimer failed"); }

            try { RefreshBlinkTrainerStatusRow(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "RefreshBlinkTrainerStatusRow failed"); }

            try { ApplyBlinkTrainerStageMode(DetermineBlinkTrainerStageMode()); }
            catch (Exception ex) { App.Logger?.Warning(ex, "ApplyBlinkTrainerStageMode failed"); }
        }

        /// <summary>
        /// Starts / stops the 1Hz countdown timer to match the service's
        /// running state. Idempotent. Drives the new Exclusives tab's Running
        /// status text via BlinkTrainerTick.
        /// </summary>
        private void SyncBlinkTrainerCountdownTimer()
        {
            if (App.BlinkTrainer == null) return;
            var running = App.BlinkTrainer.IsRunning;

            if (running)
            {
                if (_blinkTrainerTickTimer == null)
                {
                    _blinkTrainerTickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    _blinkTrainerTickTimer.Tick += BlinkTrainerTick;
                    _blinkTrainerTickTimer.Start();
                }
                BlinkTrainerTick(this, EventArgs.Empty);
            }
            else
            {
                if (_blinkTrainerTickTimer != null)
                {
                    try { _blinkTrainerTickTimer.Stop(); } catch { }
                    _blinkTrainerTickTimer.Tick -= BlinkTrainerTick;
                    _blinkTrainerTickTimer = null;
                }
            }
        }

        private void BlinkTrainerTick(object? sender, EventArgs e)
        {
            if (App.BlinkTrainer == null || !App.BlinkTrainer.IsRunning) return;
            var rem = App.BlinkTrainer.Remaining;

            // New Exclusives tab status text — only overwrite while we're
            // displaying the Running state. Other states (Error / NeedsX) get
            // their own copy from ApplyBlinkTrainerStatusState.
            if (BlinkTrainerStatusText != null && _currentBlinkTrainerStatusState == BlinkTrainerStatusState.Running)
                BlinkTrainerStatusText.Text = Localization.Loc.GetF("blink_trainer_status_running", rem.ToString(rem.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss"));
        }

        /// <summary>
        /// Lab "Moved to Exclusives" stub navigates to the new home.
        /// </summary>
        private void BtnLabBlinkTrainerOpenNew_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ShowTab("blinktrainer");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Lab Blink Trainer stub navigation failed");
            }
        }

        private async void BtnWebcamDebugTrackerTest_Click(object sender, RoutedEventArgs e)
        {
            var svc = App.Webcam;
            if (svc == null)
            {
                AppendWebcamDebugLog("App.Webcam is null — service not initialized");
                return;
            }

            if (!WebcamTrackingService.IsConsentCurrent())
            {
                AppendWebcamDebugLog("Consent not given — opening consent dialog…");
                var consent = new WebcamConsentDialog { Owner = this };
                var ok = consent.ShowDialog();
                if (ok != true || !consent.ConsentGiven)
                {
                    AppendWebcamDebugLog("Consent declined.");
                    return;
                }
            }

            // Tracker test needs the service running so OnGazeMove fires, AND a
            // calibration loaded so there's a homography to project through.
            EnsureWebcamDebugSubscribed();
            var startedHere = false;
            if (!svc.IsRunning)
            {
                if (!await StartWebcamOffUiThreadAsync(svc))
                {
                    AppendWebcamDebugLog($"Couldn't start tracking. State={svc.State}.");
                    return;
                }
                startedHere = true;
                BtnWebcamDebugStart.Content = "Stop tracking";
            }

            if (svc.Calibration == null)
            {
                AppendWebcamDebugLog("No calibration loaded — run Calibrate (16-point) first.");
                if (startedHere) { svc.Stop(); BtnWebcamDebugStart.Content = "Start tracking"; }
                return;
            }

            AppendWebcamDebugLog("Opening tracker test window…");
            var trackerDlg = new WebcamGazeTrackerWindow { Owner = this };
            App.ApplyCalibrationScreenPlacement(trackerDlg);
            trackerDlg.ShowDialog();
            AppendWebcamDebugLog("Tracker test closed.");

            // Match calibration handler's lifetime: only auto-stop tracking if we
            // were the ones that started it. If the user already had it running,
            // leave it running.
            if (startedHere)
            {
                svc.Stop();
                BtnWebcamDebugStart.Content = "Start tracking";
            }
        }

        private async void BtnWebcamDebugQuickRecal_Click(object sender, RoutedEventArgs e)
        {
            var svc = App.Webcam;
            if (svc == null)
            {
                AppendWebcamDebugLog("Webcam service unavailable.");
                return;
            }

            if (!WebcamTrackingService.IsConsentCurrent())
            {
                AppendWebcamDebugLog("Consent not given — opening consent dialog…");
                var consent = new WebcamConsentDialog { Owner = this };
                var ok = consent.ShowDialog();
                if (ok != true || !consent.ConsentGiven)
                {
                    AppendWebcamDebugLog("Consent declined.");
                    return;
                }
            }

            EnsureWebcamDebugSubscribed();
            var startedHere = false;
            if (!svc.IsRunning)
            {
                if (!await StartWebcamOffUiThreadAsync(svc))
                {
                    AppendWebcamDebugLog($"Couldn't start tracking. State={svc.State}.");
                    return;
                }
                startedHere = true;
                BtnWebcamDebugStart.Content = "Stop tracking";
            }

            if (svc.Calibration == null)
            {
                AppendWebcamDebugLog("No calibration loaded — run Calibrate (16-point) first. Quick Recal only nudges an existing calibration.");
                if (startedHere) { svc.Stop(); BtnWebcamDebugStart.Content = "Start tracking"; }
                return;
            }

            AppendWebcamDebugLog("Opening quick-recal window…");
            var recalDlg = new WebcamQuickRecalWindow { Owner = this };
            App.ApplyCalibrationScreenPlacement(recalDlg);
            var result = recalDlg.ShowDialog();
            AppendWebcamDebugLog(result == true
                ? $"Quick recal applied (offset {svc.Calibration.RuntimeOffset?.Dx:F0}, {svc.Calibration.RuntimeOffset?.Dy:F0} px)."
                : "Quick recal cancelled.");

            if (startedHere)
            {
                svc.Stop();
                BtnWebcamDebugStart.Content = "Start tracking";
            }

            // Cross-tab propagation (Cleanup 2 + Phase D).
            RefreshBlinkTrainerWebcamColumn();
            RefreshBlinkTrainerStatusRow();
        }

        private void BtnWebcamReviewPrivacy_Click(object sender, RoutedEventArgs e)
        {
            // Re-open the consent flow for users who want to read the privacy
            // contract again after they've already agreed. The dialog only
            // overwrites WebcamConsentGiven when the user explicitly walks
            // through the gates and clicks Enable — Cancel/close leaves the
            // existing consent state alone, so this is safe to invoke any
            // time as a "review only" path.
            try
            {
                var dlg = new WebcamConsentDialog { Owner = this };
                dlg.ShowDialog();
                AppendWebcamDebugLog("Privacy info reviewed.");
                // Cross-tab propagation (Cleanup 2 + Phase D): consent may have
                // been toggled via the dialog's Enable path.
                RefreshBlinkTrainerWebcamColumn();
                RefreshBlinkTrainerStatusRow();
                ApplyBlinkTrainerStageMode(DetermineBlinkTrainerStageMode());
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Webcam review privacy dialog failed");
            }
        }

        private void BtnWebcamRevokeConsent_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show(
                    this,
                    "Revoke webcam consent?\n\n" +
                    "This will:\n" +
                    "  • Stop webcam tracking immediately\n" +
                    "  • Delete your calibration data\n" +
                    "  • Disable Focus Gaze and any webcam triggers\n" +
                    "  • Clear your consent record\n\n" +
                    "You'll be re-prompted to consent and recalibrate the next time you enable a webcam feature.",
                    "Revoke webcam consent",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning,
                    MessageBoxResult.Cancel);

                if (result != MessageBoxResult.OK) return;

                App.Webcam?.RevokeConsent();
                if (ChkWebcamDebugCursor != null) ChkWebcamDebugCursor.IsChecked = false;
                AppendWebcamDebugLog("Consent revoked. Calibration deleted; webcam features disabled.");

                // Cross-tab propagation (Cleanup 2 + Phase D).
                RefreshBlinkTrainerWebcamColumn();
                RefreshBlinkTrainerStatusRow();
                ApplyBlinkTrainerStageMode(DetermineBlinkTrainerStageMode());
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Webcam revoke consent failed");
            }
        }

        private void ChkWebcamDebugCursor_Changed(object sender, RoutedEventArgs e)
        {
            if (ChkWebcamDebugCursor == null) return;
            if (ChkWebcamDebugCursor.IsChecked == true)
            {
                App.GazeCursor?.Show("debug-toggle");
                AppendWebcamDebugLog("Debug cursor enabled. Tracking must be running + calibrated for the dot to appear.");
            }
            else
            {
                App.GazeCursor?.Hide("debug-toggle");
                AppendWebcamDebugLog("Debug cursor hidden.");
            }
        }

        private void ChkRestrictGazeToCalScreen_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading || _restrictGazeCheckboxSyncing) return;
            if (ChkRestrictGazeToCalScreen == null || App.Settings?.Current == null) return;
            bool v = ChkRestrictGazeToCalScreen.IsChecked == true;
            App.Settings.Current.RestrictGazeContentToCalibratedScreen = v;
            MirrorRestrictGazeToOtherCards(v, except: ChkRestrictGazeToCalScreen);
        }

        // Re-entrancy guard for cross-tab Restrict-gaze checkbox sync (Lab,
        // Blink Trainer, Deeper hub all bind the same AppSettings flag).
        private bool _restrictGazeCheckboxSyncing;

        private void ChkBlinkTrainerRestrictGazeToCalScreen_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading || _restrictGazeCheckboxSyncing) return;
            if (ChkBlinkTrainerRestrictGazeToCalScreen == null || App.Settings?.Current == null) return;
            bool v = ChkBlinkTrainerRestrictGazeToCalScreen.IsChecked == true;
            App.Settings.Current.RestrictGazeContentToCalibratedScreen = v;
            MirrorRestrictGazeToOtherCards(v, except: ChkBlinkTrainerRestrictGazeToCalScreen);
        }

        /// <summary>
        /// Sync the Restrict-gaze checkbox across the three cards (Lab, Blink
        /// Trainer, Deeper hub) without re-entering the change-handler save
        /// path. The guard makes the mirrored .IsChecked assignment a no-op
        /// from each handler's POV.
        /// </summary>
        private void MirrorRestrictGazeToOtherCards(bool value, System.Windows.Controls.CheckBox? except)
        {
            _restrictGazeCheckboxSyncing = true;
            try
            {
                if (ChkRestrictGazeToCalScreen != null
                    && ChkRestrictGazeToCalScreen != except
                    && ChkRestrictGazeToCalScreen.IsChecked != value)
                    ChkRestrictGazeToCalScreen.IsChecked = value;

                if (ChkBlinkTrainerRestrictGazeToCalScreen != null
                    && ChkBlinkTrainerRestrictGazeToCalScreen != except
                    && ChkBlinkTrainerRestrictGazeToCalScreen.IsChecked != value)
                    ChkBlinkTrainerRestrictGazeToCalScreen.IsChecked = value;

                if (ChkDeeperWebcamRestrictGazeToCalScreen != null
                    && ChkDeeperWebcamRestrictGazeToCalScreen != except
                    && ChkDeeperWebcamRestrictGazeToCalScreen.IsChecked != value)
                    ChkDeeperWebcamRestrictGazeToCalScreen.IsChecked = value;
            }
            finally { _restrictGazeCheckboxSyncing = false; }
        }


        // Suppresses the SelectionChanged save while we programmatically
        // (re)populate either webcam ComboBox during enumeration / restore /
        // cross-tab sync. Single flag covers BOTH Lab + Blink Trainer combos
        // so a populate-one-then-the-other sequence inside
        // PopulateWebcamDeviceCombos doesn't trip the save path mid-loop.
        private bool _webcamDevicePopulating;

        /// <summary>
        /// Single enumeration → both combos. The Lab (CmbWebcamDevice) and
        /// the Blink Trainer page (CmbBlinkTrainerWebcamDevice) share device
        /// state via AppSettings.WebcamDeviceIndex but historically only
        /// re-populated on their own tab's entry — leaving the other combo
        /// stale until the user navigated to it. This helper rebuilds both
        /// at once. Safe to call when one combo's parent tab hasn't been
        /// loaded yet (null check inside PopulateWebcamCombo).
        /// </summary>
        private void PopulateWebcamDeviceCombos()
        {
            if (App.Webcam == null) return;
            var devices = App.Webcam.EnumerateDevices();
            _webcamDevicePopulating = true;
            try
            {
                PopulateWebcamCombo(CmbWebcamDevice, devices);
                PopulateWebcamCombo(CmbBlinkTrainerWebcamDevice, devices);
                PopulateWebcamCombo(CmbDeeperWebcamDevice, devices);
            }
            finally
            {
                _webcamDevicePopulating = false;
            }
        }

        private static void PopulateWebcamCombo(
            ComboBox? cb,
            IReadOnlyList<Services.WebcamDeviceEnumerator.WebcamDevice> devices)
        {
            if (cb == null) return;
            cb.Items.Clear();
            if (devices.Count == 0)
            {
                cb.Items.Add(new ComboBoxItem
                {
                    Content = "(no cameras detected)",
                    Tag = -1,
                    IsEnabled = false,
                });
                cb.SelectedIndex = 0;
                return;
            }
            foreach (var d in devices)
            {
                cb.Items.Add(new ComboBoxItem
                {
                    Content = $"[{d.Index}] {d.Name}",
                    Tag = d.Index,
                });
            }
            int saved = App.Settings?.Current?.WebcamDeviceIndex ?? -1;
            int target = saved >= 0 && saved < devices.Count ? saved : 0;
            cb.SelectedIndex = target;
        }

        /// <summary>
        /// After a user selects a device on one combo, sync the other combo's
        /// SelectedIndex to match so the two surfaces don't visually diverge.
        /// Uses the _webcamDevicePopulating guard to suppress the partner
        /// combo's SelectionChanged save path.
        /// </summary>
        private void SyncWebcamComboSelections(int idx)
        {
            _webcamDevicePopulating = true;
            try
            {
                SelectComboByDeviceIndex(CmbWebcamDevice, idx);
                SelectComboByDeviceIndex(CmbBlinkTrainerWebcamDevice, idx);
                SelectComboByDeviceIndex(CmbDeeperWebcamDevice, idx);
            }
            finally { _webcamDevicePopulating = false; }
        }

        private static void SelectComboByDeviceIndex(ComboBox? cb, int idx)
        {
            if (cb == null) return;
            for (int i = 0; i < cb.Items.Count; i++)
            {
                if (cb.Items[i] is ComboBoxItem cbi && cbi.Tag is int t && t == idx)
                {
                    if (cb.SelectedIndex != i) cb.SelectedIndex = i;
                    return;
                }
            }
        }

        /// <summary>
        /// Kept for backwards-compat with existing callers (Lab ShowTab,
        /// BtnWebcamDeviceRefresh_Click). Both combos refresh in one pass.
        /// </summary>
        private void RefreshWebcamDeviceList() => PopulateWebcamDeviceCombos();

        private void CmbWebcamDevice_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_webcamDevicePopulating) return;
            if (CmbWebcamDevice?.SelectedItem is not ComboBoxItem item) return;
            if (item.Tag is not int idx || idx < 0) return;

            if (App.Settings?.Current is { } s)
            {
                if (s.WebcamDeviceIndex == idx) return;
                s.WebcamDeviceIndex = idx;
                s.WebcamDeviceName = item.Content?.ToString() ?? "";
                App.Settings.Save();
            }

            // Keep the Blink Trainer combo in lockstep if it's been instantiated.
            SyncWebcamComboSelections(idx);

            AppendWebcamDebugLog($"Camera set to {item.Content}. {(App.Webcam?.IsRunning == true ? "Stop and Start tracking to apply." : "Will be used on next Start.")}");
        }

        private void BtnWebcamDeviceRefresh_Click(object sender, RoutedEventArgs e)
        {
            PopulateWebcamDeviceCombos();
            // Report the count of actually-enumerated devices, NOT CmbWebcamDevice.Items.Count
            // — when zero cameras are found the combo holds a single "(no cameras detected)"
            // placeholder item, which made the message falsely say "1 found" (#291).
            int found = App.Webcam?.EnumerateDevices().Count ?? 0;
            AppendWebcamDebugLog(found == 0
                ? "Re-scanned cameras: none detected."
                : $"Re-scanned cameras: {found} found.");
        }

        // Re-entrancy guard so seeding the ComboBox doesn't trigger the save path.
        private bool _webcamMonitorPopulating;

        private void RefreshWebcamMonitorList()
        {
            // Populates both the Lab combo (CmbWebcamMonitor) and the Blink Trainer
            // mirror (CmbBlinkTrainerWebcamMonitor) from the same screen list, with
            // the same saved-selection lookup. The populating flag guards the
            // SelectionChanged handlers on both combos.
            _webcamMonitorPopulating = true;
            try
            {
                var screens = App.GetAllScreensCached();
                var saved = App.Settings?.Current?.WebcamCalibrationScreen ?? "Primary";

                FillMonitorCombo(CmbWebcamMonitor, screens, saved);
                FillMonitorCombo(CmbBlinkTrainerWebcamMonitor, screens, saved);
                FillMonitorCombo(CmbDeeperWebcamMonitor, screens, saved);
            }
            finally
            {
                _webcamMonitorPopulating = false;
            }
        }

        private static void FillMonitorCombo(ComboBox? cb, System.Collections.Generic.IList<System.Windows.Forms.Screen> screens, string saved)
        {
            if (cb == null) return;
            cb.Items.Clear();
            // Always include "Primary" — survives monitor reorder. GetWebcamCalibrationScreen
            // short-circuits to Screen.PrimaryScreen when set to this sentinel.
            cb.Items.Add(new ComboBoxItem
            {
                Content = Loc.Get("webcam_monitor_primary"),
                Tag = "Primary",
            });
            int n = 1;
            foreach (var s in screens)
            {
                var label = string.Format(
                    Loc.Get("webcam_monitor_item_fmt"),
                    n++,
                    s.DeviceName,
                    s.Bounds.Width,
                    s.Bounds.Height);
                cb.Items.Add(new ComboBoxItem { Content = label, Tag = s.DeviceName });
            }
            int target = 0;
            for (int i = 0; i < cb.Items.Count; i++)
            {
                if (cb.Items[i] is ComboBoxItem ci
                    && ci.Tag is string tag
                    && string.Equals(tag, saved, StringComparison.OrdinalIgnoreCase))
                {
                    target = i; break;
                }
            }
            cb.SelectedIndex = target;
        }

        private void CmbWebcamMonitor_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_webcamMonitorPopulating) return;
            if (CmbWebcamMonitor?.SelectedItem is not ComboBoxItem item) return;
            if (item.Tag is not string deviceName) return;

            if (App.Settings?.Current is { } s)
            {
                if (string.Equals(s.WebcamCalibrationScreen, deviceName, StringComparison.OrdinalIgnoreCase)) return;
                s.WebcamCalibrationScreen = deviceName;
                App.Settings.Save();
            }

            SyncMonitorComboSelection(CmbBlinkTrainerWebcamMonitor, deviceName);
            SyncMonitorComboSelection(CmbDeeperWebcamMonitor, deviceName);
            AppendWebcamDebugLog($"Calibration monitor set to {item.Content}.");
        }

        private void CmbBlinkTrainerWebcamMonitor_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_webcamMonitorPopulating) return;
            if (CmbBlinkTrainerWebcamMonitor?.SelectedItem is not ComboBoxItem item) return;
            if (item.Tag is not string deviceName) return;

            if (App.Settings?.Current is { } s)
            {
                if (string.Equals(s.WebcamCalibrationScreen, deviceName, StringComparison.OrdinalIgnoreCase)) return;
                s.WebcamCalibrationScreen = deviceName;
                App.Settings.Save();
            }

            SyncMonitorComboSelection(CmbWebcamMonitor, deviceName);
            SyncMonitorComboSelection(CmbDeeperWebcamMonitor, deviceName);
        }

        private void SyncMonitorComboSelection(ComboBox? cb, string deviceName)
        {
            if (cb == null) return;
            for (int i = 0; i < cb.Items.Count; i++)
            {
                if (cb.Items[i] is ComboBoxItem ci
                    && ci.Tag is string tag
                    && string.Equals(tag, deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    if (cb.SelectedIndex == i) return;
                    _webcamMonitorPopulating = true;
                    try { cb.SelectedIndex = i; }
                    finally { _webcamMonitorPopulating = false; }
                    return;
                }
            }
        }

        private void AppendWebcamDebugLog(string line)
        {
            if (TxtWebcamDebugLog == null) return;
            var stamp = DateTime.Now.ToString("HH:mm:ss");
            var existing = TxtWebcamDebugLog.Text;
            if (existing == "(events will appear here)") existing = "";
            var lines = (existing + (existing.Length > 0 ? "\n" : "") + $"[{stamp}] {line}")
                .Split('\n');
            if (lines.Length > 12) lines = lines[(lines.Length - 12)..];
            TxtWebcamDebugLog.Text = string.Join("\n", lines);
        }

        private void BtnPatreonExclusives_Click(object sender, RoutedEventArgs e)
        {
            // Toggle the menu. With StaysOpen=True (see the Popup in XAML) the
            // button's Click event always fires reliably — outside-click closing
            // is handled by the window-level PreviewMouseDown handler set up in
            // the constructor. If the popup is already pinned-open, click closes
            // it. Otherwise click opens & pins it (so MouseLeave won't dismiss it).
            _exclusivesMenuCloseTimer?.Stop();
            if (ExclusivesSubmenuPopup.IsOpen && _exclusivesPinned)
            {
                _exclusivesPinned = false;
                ExclusivesSubmenuPopup.IsOpen = false;
                return;
            }
            RefreshExclusivesSubmenuLocks();
            _exclusivesPinned = true;
            ExclusivesSubmenuPopup.IsOpen = true;
        }

        // Walks up the visual tree (with a logical-tree fallback for content like
        // popups) checking whether `node` is `ancestor` or descended from it.
        private static bool IsVisualDescendant(DependencyObject? node, DependencyObject ancestor)
        {
            while (node != null)
            {
                if (node == ancestor) return true;
                var parent = VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node);
                node = parent;
            }
            return false;
        }

        /// <summary>
        /// Opens the dashboard's "App Info &amp; Data" popup. This is the new home
        /// for account management (Patreon/Discord login, cloud backup, data
        /// export, privacy policy, support links) that used to live in the
        /// Patreon Exclusives tab.
        /// </summary>
        internal void ShowAppInfoPopup()
        {
            VelvetBtnAppInfo_Click(this, new RoutedEventArgs());
        }

        private void BtnAwareness_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("awareness");
        }

        #region Exclusives Submenu

        private DispatcherTimer? _exclusivesMenuCloseTimer;
        // True when the popup was opened by a click — hover-leave will not
        // dismiss a pinned popup. Outside-click and Alt+Tab close it via the
        // window-level handlers in the constructor.
        private bool _exclusivesPinned;

        private void BtnPatreonExclusives_MouseEnter(object sender, MouseEventArgs e)
        {
            _exclusivesMenuCloseTimer?.Stop();
            if (ExclusivesSubmenuPopup.IsOpen) return;
            RefreshExclusivesSubmenuLocks();
            ExclusivesSubmenuPopup.IsOpen = true;
        }

        private void ExclusivesSubmenuPopup_MouseEnter(object sender, MouseEventArgs e)
        {
            _exclusivesMenuCloseTimer?.Stop();
        }

        private void ExclusivesMenu_MouseLeave(object sender, MouseEventArgs e)
        {
            // Click-pinned popups don't dismiss on hover-out — they only close
            // via click-outside or sub-item selection.
            if (_exclusivesPinned) return;

            if (_exclusivesMenuCloseTimer == null)
            {
                _exclusivesMenuCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                _exclusivesMenuCloseTimer.Tick += ExclusivesMenuCloseTick;
            }
            _exclusivesMenuCloseTimer.Stop();
            _exclusivesMenuCloseTimer.Start();
        }

        private void ExclusivesMenuCloseTick(object? sender, EventArgs e)
        {
            _exclusivesMenuCloseTimer?.Stop();
            if (_exclusivesPinned) return;
            ExclusivesSubmenuPopup.IsOpen = false;
        }

        private void ExclusivesSubmenuPopup_Closed(object? sender, EventArgs e)
        {
            _exclusivesPinned = false;
        }

        private void CloseExclusivesSubmenu()
        {
            _exclusivesPinned = false;
            ExclusivesSubmenuPopup.IsOpen = false;
        }

        private void BtnSubRemoteControl_Click(object sender, RoutedEventArgs e)
        {
            CloseExclusivesSubmenu();
            ShowTab("remotecontrol");
        }

        private void BtnSubBambiTakeover_Click(object sender, RoutedEventArgs e)
        {
            CloseExclusivesSubmenu();
            ShowTab("bambitakeover");
        }

        private void BtnSubHaptics_Click(object sender, RoutedEventArgs e)
        {
            CloseExclusivesSubmenu();
            ShowTab("haptics");
        }

        private void BtnSubAwareness_Click(object sender, RoutedEventArgs e)
        {
            CloseExclusivesSubmenu();
            ShowTab("awareness");
        }

        private void BtnSubLockdown_Click(object sender, RoutedEventArgs e)
        {
            CloseExclusivesSubmenu();
            ShowTab("lockdown");
        }

        private void BtnSubBlinkTrainer_Click(object sender, RoutedEventArgs e)
        {
            CloseExclusivesSubmenu();
            ShowTab("blinktrainer");
        }

        /// <summary>
        /// Updates "Premium" badges on the Exclusives submenu items based on the
        /// user's current subscription state. Called whenever the popup opens.
        /// </summary>
        private void RefreshExclusivesSubmenuLocks()
        {
            var hasPremium = App.Patreon?.HasPremiumAccess == true;
            var badgeVis = hasPremium ? Visibility.Collapsed : Visibility.Visible;
            if (SubBadgeRemoteControl != null) SubBadgeRemoteControl.Visibility = badgeVis;
            if (SubBadgeBambiTakeover != null) SubBadgeBambiTakeover.Visibility = badgeVis;
            if (SubBadgeHaptics != null) SubBadgeHaptics.Visibility = badgeVis;
            if (SubBadgeAwareness != null) SubBadgeAwareness.Visibility = badgeVis;
            if (SubBadgeLockdown != null) SubBadgeLockdown.Visibility = badgeVis;
            if (SubBadgeBlinkTrainer != null) SubBadgeBlinkTrainer.Visibility = badgeVis;
        }

        /// <summary>
        /// Routes the gating overlay's CTA button to the App Info &amp; Data popup,
        /// where users can sign in with Patreon/Discord to unlock premium features.
        /// </summary>
        private void BtnGateUnlock_Click(object sender, RoutedEventArgs e)
        {
            ShowAppInfoPopup();
        }

        /// <summary>
        /// Toggles a translucent gating overlay's visibility based on the user's
        /// premium subscription state. Used by the new visible-but-locked tabs.
        /// </summary>
        private void RefreshPremiumGate(Border? gate)
        {
            if (gate == null) return;
            var hasPremium = App.Patreon?.HasPremiumAccess == true;
            gate.Visibility = hasPremium ? Visibility.Collapsed : Visibility.Visible;
        }

        #endregion


        private async void BtnQuickPatreonLogin_Click(object sender, RoutedEventArgs e)
        {
            await HandleQuickPatreonLoginAsync();
        }

        private async Task HandleQuickPatreonLoginAsync()
        {
            if (App.Patreon == null) return;

            if (App.Patreon.IsAuthenticated)
            {
                // Logout
                App.ProfileSync?.StopHeartbeat();
                App.Patreon.Logout();
                if (App.Discord?.IsAuthenticated != true)
                {
                    // No provider left — full logout
                    ClearAccountData();
                }
                else
                {
                    // Discord still active — just update Patreon UI
                    App.Patreon.UnifiedUserId = null;
                    UpdateQuickPatreonUI();
                    UpdatePatreonUI();
                    UpdateBannerWelcomeMessage();
                }
            }
            else
            {
                // Start OAuth flow (legacy - now use LoginDialog instead)
                try
                {
                    await App.Patreon.StartOAuthFlowAsync();

                    // Use V2 unified account flow (v5.5+ with seasons system)
                    var result = await AccountService.HandlePostAuthV2Async(this, "patreon");

                    if (result.Success)
                    {
                        UpdateQuickPatreonUI();
                        UpdatePatreonUI();
                        UpdateBannerWelcomeMessage();
                        UpdateAccountLinkingUI();
                    }
                }
                catch (OperationCanceledException)
                {
                    // User cancelled - ignore
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "Patreon login failed");
                    MessageBox.Show(
                        $"Failed to connect to Patreon.\n\n{ex.Message}",
                        "Connection Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                finally
                {
                    UpdateQuickPatreonUI();
                }
            }
        }

        private void UpdateQuickPatreonUI()
        {
            // Now managed by unified login panel
            UpdateQuickLoginUI();
        }

        private async void BtnQuickDiscordLogin_Click(object sender, RoutedEventArgs e)
        {
            await HandleDiscordLoginAsync();
        }

        private async Task HandleDiscordLoginAsync()
        {
            if (App.Discord == null) return;

            if (App.Discord.IsAuthenticated)
            {
                // Logout
                App.Discord.Logout();
                if (App.Patreon?.IsAuthenticated != true)
                {
                    // No provider left — full logout
                    ClearAccountData();
                }
                else
                {
                    // Patreon still active — just update Discord UI
                    App.Discord.UnifiedUserId = null;
                    UpdateQuickDiscordUI();
                    UpdateBannerWelcomeMessage();
                }
            }
            else
            {
                // Start OAuth flow
                SetDiscordButtonsEnabled(false);
                SetDiscordButtonsContent("Connecting...");

                try
                {
                    await App.Discord.StartOAuthFlowAsync();

                    // Use V2 unified account flow (v5.5+ with seasons system)
                    var result = await AccountService.HandlePostAuthV2Async(this, "discord");

                    if (result.Success)
                    {
                        UpdateQuickDiscordUI();
                        UpdateBannerWelcomeMessage();
                        UpdateAccountLinkingUI();
                    }
                }
                catch (OperationCanceledException)
                {
                    // User cancelled - ignore
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "Discord login failed");
                    MessageBox.Show(
                        $"Failed to connect to Discord.\n\n{ex.Message}",
                        "Connection Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                finally
                {
                    SetDiscordButtonsEnabled(true);
                    UpdateQuickDiscordUI();
                }
            }
        }

        private void SetDiscordButtonsEnabled(bool enabled)
        {
            // Old quick button removed - now using unified login
        }

        private void SetDiscordButtonsContent(string text)
        {
            // Old quick button removed - now using unified login
        }

        private void UpdateQuickDiscordUI()
        {
            // Now managed by unified login panel
            UpdateQuickLoginUI();

            // Also update the Patreon tab Discord UI
            UpdateDiscordUI();
        }

        private void BtnDiscord_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://discord.gg/YxVAMt4qaZ",
                    UseShellExecute = true
                });
                App.Logger?.Information("Opened Discord invite link");
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open Discord link");
            }
        }


        private void ChkDiscordRichPresence_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            // Get the state from whichever checkbox was clicked
            var checkbox = sender as CheckBox;
            var isEnabled = checkbox?.IsChecked == true;

            // Block enabling Rich Presence if Discord is not linked — prevents accidental
            // exposure for users who chose anonymous invite-code accounts
            if (isEnabled && App.Settings?.Current?.HasLinkedDiscord != true)
            {
                _isLoading = true;
                ChkDiscordRichPresence.IsChecked = false;
                ChkQuickDiscordRichPresence.IsChecked = false;
                if (ChkDiscordTabRichPresence != null) ChkDiscordTabRichPresence.IsChecked = false;
                _isLoading = false;
                MessageBox.Show(Loc.Get("msg_discord_rich_presence_requires_a_linked_disco"),
                    "Discord Not Linked", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Sync all checkboxes without re-entrancy
            _isLoading = true;
            ChkDiscordRichPresence.IsChecked = isEnabled;
            ChkQuickDiscordRichPresence.IsChecked = isEnabled;
            if (ChkDiscordTabRichPresence != null) ChkDiscordTabRichPresence.IsChecked = isEnabled;
            _isLoading = false;

            App.Settings.Current.DiscordRichPresenceEnabled = isEnabled;

            if (App.DiscordRpc != null)
            {
                App.DiscordRpc.IsEnabled = isEnabled;
                App.Logger?.Information("Discord Rich Presence {Status}", isEnabled ? "enabled" : "disabled");
            }
        }


        private void InitializeLanguageSelector()
        {
            if (CmbLanguagePill == null) return;

            CmbLanguagePill.Items.Clear();
            int selectedIndex = 0;
            var currentLang = App.Settings?.Current?.Language ?? "en";

            for (int i = 0; i < LocalizationManager.AvailableLanguages.Length; i++)
            {
                var (code, displayName, shortName) = LocalizationManager.AvailableLanguages[i];
                CmbLanguagePill.Items.Add(new ComboBoxItem
                {
                    Content = $"🌐 {shortName}",
                    Tag = code,
                    ToolTip = displayName
                });
                if (code == currentLang)
                    selectedIndex = i;
            }

            CmbLanguagePill.SelectedIndex = selectedIndex;
        }

        private void CmbLanguagePill_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbLanguagePill?.SelectedItem is not ComboBoxItem selected) return;
            var langCode = selected.Tag as string ?? "en";

            if (App.Settings?.Current != null && App.Settings.Current.Language != langCode)
            {
                App.Settings.Current.Language = langCode;
                LocalizationManager.Instance.SetLanguage(langCode);
                App.Settings.Save();

                // XAML bindings update live; code-behind strings need a restart
                if (TxtBannerSecondary != null)
                {
                    TxtBannerSecondary.Text = Loc.Get("msg_restart_to_apply");
                    TxtBannerSecondary.Opacity = 1;
                    TxtBannerSecondary.IsHitTestVisible = true;
                }
            }
        }

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            BtnCheckUpdates.IsEnabled = false;
            BtnCheckUpdates.Content = Loc.Get("btn_checking");

            try
            {
                await App.CheckForUpdatesManuallyAsync(this);
            }
            finally
            {
                BtnCheckUpdates.IsEnabled = true;
                BtnCheckUpdates.Content = Loc.Get("btn_check_updates");
            }
        }

        private async void BtnUpdateAvailable_Click(object sender, RoutedEventArgs e)
        {
            // If server provided a URL, open it in browser instead of auto-updating
            if (!string.IsNullOrEmpty(_serverUpdateUrl))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = _serverUpdateUrl,
                        UseShellExecute = true
                    });
                    return;
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning("Failed to open update URL: {Error}", ex.Message);
                }
            }

            // Trigger the update installation
            await App.CheckForUpdatesManuallyAsync(this);
        }

        /// <summary>
        /// Sets the update button state in the tab bar.
        /// Called from App when an update is detected or after checking.
        /// </summary>
        public void ShowUpdateAvailableButton(bool updateAvailable)
        {
            Dispatcher.Invoke(() =>
            {
                BtnUpdateAvailable.Tag = updateAvailable ? "UpdateAvailable" : "NoUpdate";
                BtnUpdateAvailable.Content = updateAvailable ? "UPDATE" : "LATEST VERSION :3";
                BtnUpdateAvailable.ToolTip = updateAvailable
                    ? "Update Available - Click to install!"
                    : "You're on the latest version";
            });
        }


        private void AnimateTabIn(UIElement tab)
        {
            try
            {
                tab.Opacity = 0;
                var anim = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };
                tab.BeginAnimation(OpacityProperty, anim);
            }
            catch
            {
                tab.Opacity = 1;
            }
        }

        internal void ShowTab(string tab)
        {
            // Legacy redirect: the "patreon" tab was eliminated and its
            // account/data content lives in the dashboard's App Info popup now.
            // Route any legacy callers there WITHOUT disturbing the currently
            // active tab (opening a popup is overlay-style, not a tab switch).
            if (tab == "patreon")
            {
                ShowAppInfoPopup();
                return;
            }

            // Bark hook: announce navigation (gated/chanced in the rules so it isn't spammy).
            try { App.Bark?.NotifyTabNavigated(tab); } catch { }

            // Stop animations on tabs we're leaving to reduce idle CPU
            StopSeasonTitleShimmer();
            StopLockdownPulse();
            StopSkillTreeAnimations();

            // Hide all tabs
            SettingsTab.Visibility = Visibility.Collapsed;
            PresetsTab.Visibility = Visibility.Collapsed;
            ProgressionTab.Visibility = Visibility.Collapsed;
            QuestsTab.Visibility = Visibility.Collapsed;
            AchievementsTab.Visibility = Visibility.Collapsed;
            CompanionTab.Visibility = Visibility.Collapsed;
            PatreonTab.Visibility = Visibility.Collapsed;
            LeaderboardTab.Visibility = Visibility.Collapsed;
            AssetsTab.Visibility = Visibility.Collapsed;
            DiscordTab.Visibility = Visibility.Collapsed;
            EnhancementsTab.Visibility = Visibility.Collapsed;
            if (DeeperTab != null) DeeperTab.Visibility = Visibility.Collapsed;
            LabTab.Visibility = Visibility.Collapsed;
            AwarenessTab.Visibility = Visibility.Collapsed;
            if (RemoteControlTab != null) RemoteControlTab.Visibility = Visibility.Collapsed;
            if (AvailableSubjectsTab != null) AvailableSubjectsTab.Visibility = Visibility.Collapsed;
            if (BambiTakeoverTab != null) BambiTakeoverTab.Visibility = Visibility.Collapsed;
            // SP5L3: stop polling whenever we leave the Available Subjects
            // tab. Idempotent — safe to call even if not currently polling.
            App.AvailableSubjects?.StopPolling();
            if (HapticsTab != null) HapticsTab.Visibility = Visibility.Collapsed;
            if (LockdownTab != null) LockdownTab.Visibility = Visibility.Collapsed;
            if (BlinkTrainerTab != null)
            {
                // Stop the demo timer AND drop the live-mode OnBlink subscription
                // when leaving the tab so neither runs while the user is
                // elsewhere. Both are idempotent.
                if (BlinkTrainerTab.Visibility == Visibility.Visible)
                {
                    StopBlinkTrainerDemoLoop();
                    UnsubscribeBlinkTrainerLiveBlink();
                    // Reset cached mode so the next entry re-runs the resolver
                    // and starts whatever's appropriate from scratch.
                    _currentBlinkTrainerStageMode = BlinkTrainerStageMode.Demo;
                }
                BlinkTrainerTab.Visibility = Visibility.Collapsed;
            }

            // Reset all button styles to inactive. activeStyle is the primary-nav-only v6 variant —
            // quest sub-tabs and roadmap tracks use TabButtonActive directly (see lines further down).
            var inactiveStyle = FindResource("TabButton") as Style;
            var activeStyle = FindResource("TabButtonActivePrimary") as Style;
            BtnSettings.Style = inactiveStyle;
            BtnPresets.Style = inactiveStyle;
            BtnQuests.Style = inactiveStyle;
            BtnEnhancements.Style = inactiveStyle;
            if (BtnDeeper != null) BtnDeeper.Style = FindResource("TabButtonDeeper") as Style;
            if (BtnAvailableSubjects != null) BtnAvailableSubjects.Style = FindResource("TabButtonNeon") as Style;
            BtnAchievements.Style = inactiveStyle;
            BtnCompanion.Style = inactiveStyle;
            BtnLeaderboard.Style = inactiveStyle;
            BtnLab.Style = inactiveStyle;
            BtnOpenAssetsTop.Style = inactiveStyle;
            // BtnAwareness was removed from the primary tab bar — its only entry point
            // is now the Exclusives popup submenu
            // BtnPatreonExclusives keeps its inline Patreon red style defined in XAML

            switch (tab)
            {
                case "settings":
                    SettingsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(SettingsTab);
                    BtnSettings.Style = activeStyle;
                    break;

                case "presets":
                    PresetsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(PresetsTab);
                    BtnPresets.Style = activeStyle;
                    break;

                // "progression" tab removed in velvet-mosaic phase 6 — its content
                // is now on the Dashboard. Legacy callers (e.g. older tutorial steps)
                // that request ShowTab("progression") fall through to the Dashboard.
                case "progression":
                    SettingsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(SettingsTab);
                    BtnSettings.Style = activeStyle;
                    break;

                case "quests":
                    QuestsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(QuestsTab);
                    BtnQuests.Style = activeStyle;
                    StartSeasonTitleShimmer();
                    RefreshQuestUI();
                    break;

                case "enhancements":
                    EnhancementsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(EnhancementsTab);
                    BtnEnhancements.Style = activeStyle;
                    RefreshEnhancementsUI();
                    break;

                case "deeper":
                    if (DeeperTab != null)
                    {
                        DeeperTab.Visibility = Visibility.Visible;
                        AnimateTabIn(DeeperTab);
                        RefreshDeeperLibraryUI();
                        // Populate the Deeper-hub webcam card (device + monitor
                        // combos populate empty until something asks). Refresh
                        // also fills the consent + calibration status cells.
                        try { PopulateWebcamDeviceCombos(); } catch { }
                        try { RefreshWebcamMonitorList(); } catch { }
                        RefreshDeeperWebcamColumn();
                        RefreshBlinkTrainerTrackerButton();
                        // Refresh submission statuses on tab open (throttled) so
                        // an acceptance reflects without restarting the app.
                        _ = CheckDeeperSubmissionStatusesAsync();
                    }
                    if (BtnDeeper != null) BtnDeeper.Style = FindResource("TabButtonDeeperActive") as Style;
                    break;

                case "achievements":
                    AchievementsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(AchievementsTab);
                    BtnAchievements.Style = activeStyle;
                    RefreshAllAchievementTiles();
                    UpdateAchievementCount();
                    break;

                case "companion":
                    CompanionTab.Visibility = Visibility.Visible;
                    AnimateTabIn(CompanionTab);
                    BtnCompanion.Style = activeStyle;
                    SyncCompanionTabUI();
                    InitializePhrasePresets();
                    break;

                case "lab":
                    LabTab.Visibility = Visibility.Visible;
                    AnimateTabIn(LabTab);
                    BtnLab.Style = activeStyle;
                    RefreshWebcamDeviceList();
                    RefreshWebcamMonitorList();
                    if (ChkRestrictGazeToCalScreen != null && App.Settings?.Current != null)
                        ChkRestrictGazeToCalScreen.IsChecked = App.Settings.Current.RestrictGazeContentToCalibratedScreen;
                    break;

                // Note: "patreon" case is handled at the top of ShowTab as a
                // legacy redirect to the App Info & Data popup (Exclusives tab
                // was eliminated; account/data UI now lives in the dashboard).

                case "leaderboard":
                    LeaderboardTab.Visibility = Visibility.Visible;
                    AnimateTabIn(LeaderboardTab);
                    BtnLeaderboard.Style = activeStyle;
                    _ = RefreshLeaderboardAsync(); // Load on first view
                    break;

                case "assets":
                    AssetsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(AssetsTab);
                    BtnOpenAssetsTop.Style = activeStyle;
                    RefreshAssetTree();
                    InitializeAssetPresets();
                    if (PacksSectionEnabled) _ = RefreshPacksAsync();
                    break;

                case "discord":
                    DiscordTab.Visibility = Visibility.Visible;
                    AnimateTabIn(DiscordTab);
                    // BtnDiscordTab keeps its inline Discord blue style defined in XAML
                    UpdateDiscordTabUI();
                    break;

                case "awareness":
                    AwarenessTab.Visibility = Visibility.Visible;
                    AnimateTabIn(AwarenessTab);
                    SyncAwarenessTabUI();
                    break;

                case "remotecontrol":
                    RemoteControlTab.Visibility = Visibility.Visible;
                    AnimateTabIn(RemoteControlTab);
                    UpdateRemoteControlUI();
                    break;

                case "availablesubjects":
                    if (AvailableSubjectsTab != null)
                    {
                        AvailableSubjectsTab.Visibility = Visibility.Visible;
                        AnimateTabIn(AvailableSubjectsTab);
                    }
                    if (BtnAvailableSubjects != null)
                        BtnAvailableSubjects.Style = FindResource("TabButtonNeonActive") as Style;
                    EnsureAvailableSubjectsBound();
                    App.AvailableSubjects?.StartPolling();
                    break;

                case "bambitakeover":
                    BambiTakeoverTab.Visibility = Visibility.Visible;
                    AnimateTabIn(BambiTakeoverTab);
                    UpdatePatreonUI();
                    break;

                case "haptics":
                    HapticsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(HapticsTab);
                    UpdatePatreonUI();
                    break;

                case "lockdown":
                    LockdownTab.Visibility = Visibility.Visible;
                    AnimateTabIn(LockdownTab);
                    StartLockdownPulse();
                    RefreshPremiumGate(LockdownGate);
                    break;

                case "blinktrainer":
                    BlinkTrainerTab.Visibility = Visibility.Visible;
                    AnimateTabIn(BlinkTrainerTab);
                    RefreshBlinkTrainerTab();
                    break;

            }
        }

        /// <summary>
        /// Per-tab refresh hook for the Blink Trainer page. Called on every
        /// transition into the tab. Phase C: syncs all control state from
        /// settings + webcam status. Phase D will add live-mode detection
        /// (consent + folders + active session) and skip the demo when live
        /// mode takes over.
        /// </summary>
        private void RefreshBlinkTrainerTab()
        {
            // First-visit flag flip (Phase G) — suppresses the v5.9.8 flagship
            // sticky toast on next launch. Also dismisses the toast in this
            // session if it's currently showing (H.3): once the user finds
            // the feature, the announcement has done its job.
            // Isolated try/catch so a settings failure here can't keep the
            // rest of the refresh from running.
            try
            {
                if (App.Settings?.Current is { HasSeenBlinkTrainerFlagship: false } first)
                {
                    first.HasSeenBlinkTrainerFlagship = true;
                    App.Settings?.Save();

                    // Fade out the toast if it's still on screen, and persist
                    // the dismissal so it can't refire even if HasSeen somehow
                    // doesn't stick.
                    const string flagshipKey = "blink-trainer-flagship-v5.9.8";
                    App.Notifications?.Dismiss(flagshipKey);
                    if (!first.DismissedNotificationKeys.Contains(flagshipKey))
                    {
                        first.DismissedNotificationKeys.Add(flagshipKey);
                        App.Settings?.Save();
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "HasSeenBlinkTrainerFlagship flag: failed to set");
            }

            try
            {
                var s = App.Settings?.Current;
                if (s != null)
                {
                    // IncludeVideos toggle — set before rebuilding cards so count
                    // summaries use the current mode.
                    if (ToggleBlinkTrainerIncludeVideos != null)
                        ToggleBlinkTrainerIncludeVideos.IsChecked = s.BlinkTrainerIncludeVideos;

                    // Duration
                    if (SliderBlinkTrainerDurationNew != null)
                        SliderBlinkTrainerDurationNew.Value = s.BlinkTrainerDurationMinutes;
                    if (TxtBlinkTrainerDurationValue != null)
                        TxtBlinkTrainerDurationValue.Text = $"{s.BlinkTrainerDurationMinutes} min";

                    // Opacity
                    if (SliderBlinkTrainerOpacityNew != null)
                        SliderBlinkTrainerOpacityNew.Value = s.BlinkTrainerOpacity;
                    if (TxtBlinkTrainerOpacityValue != null)
                        TxtBlinkTrainerOpacityValue.Text = $"{s.BlinkTrainerOpacity}%";

                    // Mix-mode selection visual
                    SetMixModeSelection(s.BlinkTrainerMixImages);
                }

                RebuildBlinkTrainerFolderCards();
                RefreshBlinkTrainerWebcamColumn();
                // Monitor picker + Restrict-gaze checkbox mirror the Lab card.
                // RefreshWebcamMonitorList now populates both combos; the checkbox
                // gets its initial state here so the BT tab matches without
                // requiring a Lab visit first.
                RefreshWebcamMonitorList();
                if (ChkBlinkTrainerRestrictGazeToCalScreen != null && s != null)
                {
                    _restrictGazeCheckboxSyncing = true;
                    try { ChkBlinkTrainerRestrictGazeToCalScreen.IsChecked = s.RestrictGazeContentToCalibratedScreen; }
                    finally { _restrictGazeCheckboxSyncing = false; }
                }
                RefreshBlinkTrainerGate();
                RefreshBlinkTrainerTrackerButton();

                // Phase D: status row + stage mode are now state-machine driven.
                // RefreshBlinkTrainerStatusRow paints the dot/text/action button;
                // ApplyBlinkTrainerStageMode handles demo-vs-live transitions.
                // ApplyBlinkTrainerStageMode also calls StartBlinkTrainerDemoLoop
                // when it decides demo mode is appropriate.
                RefreshBlinkTrainerStatusRow();
                ApplyBlinkTrainerStageMode(DetermineBlinkTrainerStageMode());

                // ApplyBlinkTrainerStageMode is a no-op when the mode hasn't
                // changed (e.g. second tab visit while already in Demo). Cover
                // the initial-show case where there's nothing to transition
                // FROM by ensuring the demo loop is running if we're in Demo.
                if (_currentBlinkTrainerStageMode == BlinkTrainerStageMode.Demo)
                    StartBlinkTrainerDemoLoop();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "RefreshBlinkTrainerTab failed");
            }
        }

        #region Blink Trainer Tab — demo loop (Phase B)

        // Demo loop state. The loop cycles through 4 SFW abstract gradient PNGs
        // every 2 seconds with a 200ms cross-fade between two overlapping Image
        // controls. This is DEMO MODE ONLY — Phase D's live-mode swap (driven by
        // App.Webcam.OnBlink) will hard-cut, not cross-fade. Keep the two paths
        // separate so we can tune timing independently.
        private DispatcherTimer? _blinkTrainerDemoTimer;
        private int _blinkTrainerDemoIndex = 0;
        private bool _blinkTrainerDemoUsingA = true;
        private List<BitmapImage>? _blinkTrainerDemoAssets;

        /// <summary>
        /// Lazily loads the 4 demo PNGs from pack:// URIs and shuffles them so
        /// the play order isn't predictable. Cached in a field; the images
        /// outlive every tab visit and are only released on app shutdown.
        /// </summary>
        private void EnsureBlinkTrainerDemoAssetsLoaded()
        {
            if (_blinkTrainerDemoAssets != null) return;

            var loaded = new List<BitmapImage>(4);
            for (int i = 1; i <= 4; i++)
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(
                        $"pack://application:,,,/assets/BlinkTrainer/Demo/demo_{i:00}.png",
                        UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();
                    loaded.Add(bmp);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "BlinkTrainer demo asset demo_{Index:00}.png failed to load", i);
                }
            }

            // Fisher-Yates shuffle so the first run isn't always demo_01 -> 02 -> ...
            var rng = Random.Shared;
            for (int i = loaded.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (loaded[i], loaded[j]) = (loaded[j], loaded[i]);
            }

            _blinkTrainerDemoAssets = loaded;
        }

        /// <summary>
        /// Starts the 2s cross-fade cycle on the stage preview. Idempotent — if
        /// already running, returns immediately. Sets the initial image
        /// synchronously so the user never sees an empty frame.
        /// </summary>
        private void StartBlinkTrainerDemoLoop()
        {
            try
            {
                if (_blinkTrainerDemoTimer != null) return; // already running

                EnsureBlinkTrainerDemoAssetsLoaded();
                if (_blinkTrainerDemoAssets == null || _blinkTrainerDemoAssets.Count == 0)
                {
                    App.Logger?.Warning("BlinkTrainer: demo loop skipped — no demo assets loaded");
                    return;
                }

                _blinkTrainerDemoIndex = 0;
                _blinkTrainerDemoUsingA = true;
                if (BlinkTrainerStageImageA != null)
                {
                    BlinkTrainerStageImageA.Source = _blinkTrainerDemoAssets[0];
                    BlinkTrainerStageImageA.Opacity = 1;
                }
                if (BlinkTrainerStageImageB != null)
                {
                    BlinkTrainerStageImageB.Source = null;
                    BlinkTrainerStageImageB.Opacity = 0;
                }

                _blinkTrainerDemoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.0) };
                _blinkTrainerDemoTimer.Tick += BlinkTrainerDemoTimer_Tick;
                _blinkTrainerDemoTimer.Start();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "StartBlinkTrainerDemoLoop failed");
            }
        }

        private void BlinkTrainerDemoTimer_Tick(object? sender, EventArgs e) => AdvanceBlinkTrainerDemo();

        /// <summary>
        /// Stops the demo timer and detaches its handler. Idempotent. Does NOT
        /// clear the cached assets (they're cheap to keep around for the next
        /// tab visit).
        /// </summary>
        private void StopBlinkTrainerDemoLoop()
        {
            try
            {
                if (_blinkTrainerDemoTimer == null) return;
                _blinkTrainerDemoTimer.Stop();
                _blinkTrainerDemoTimer.Tick -= BlinkTrainerDemoTimer_Tick;
                _blinkTrainerDemoTimer = null;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "StopBlinkTrainerDemoLoop failed");
            }
        }

        private void AdvanceBlinkTrainerDemo()
        {
            if (_blinkTrainerDemoAssets == null || _blinkTrainerDemoAssets.Count == 0) return;
            if (BlinkTrainerStageImageA == null || BlinkTrainerStageImageB == null) return;

            _blinkTrainerDemoIndex = (_blinkTrainerDemoIndex + 1) % _blinkTrainerDemoAssets.Count;
            var nextAsset = _blinkTrainerDemoAssets[_blinkTrainerDemoIndex];

            Image incoming = _blinkTrainerDemoUsingA ? BlinkTrainerStageImageB : BlinkTrainerStageImageA;
            Image outgoing = _blinkTrainerDemoUsingA ? BlinkTrainerStageImageA : BlinkTrainerStageImageB;

            incoming.Source = nextAsset;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } };
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } };

            incoming.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            outgoing.BeginAnimation(UIElement.OpacityProperty, fadeOut);

            _blinkTrainerDemoUsingA = !_blinkTrainerDemoUsingA;
        }

        /// <summary>
        /// Toggles a Blink Trainer session via BlinkTrainerService. Mirrors
        /// the legacy Lab handler's pre-warm-then-start sequence, but routes
        /// the post-action UI refresh through Phase D's state machine.
        /// </summary>
        private async void BtnBlinkTrainerStartSession_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (App.BlinkTrainer == null) return;

                if (App.BlinkTrainer.IsRunning)
                {
                    App.BlinkTrainer.Stop();
                    // StateChanged fires inside Stop() and refreshes both UIs;
                    // call explicitly here too for the case where Stop runs
                    // synchronously enough that StateChanged is already done.
                    RefreshBlinkTrainerStatusRow();
                    ApplyBlinkTrainerStageMode(DetermineBlinkTrainerStageMode());
                    return;
                }

                // Pre-warm webcam off the UI thread so BlinkTrainerService.Start
                // doesn't block on capture device init. Same pattern as the
                // legacy Lab handler (BtnBlinkTrainerStart_Click).
                if (App.Webcam != null && !App.Webcam.IsRunning && WebcamTrackingService.IsConsentCurrent())
                {
                    try
                    {
                        await Task.Run(() =>
                        {
                            try { App.Webcam.Start(); }
                            catch (Exception ex) { App.Logger?.Warning(ex, "Blink Trainer prewarm failed"); }
                        });
                    }
                    catch { /* swallowed — Start() handles its own error reporting */ }
                }

                App.BlinkTrainer.Start();
                // Defensive refresh (see Stop branch).
                RefreshBlinkTrainerStatusRow();
                ApplyBlinkTrainerStageMode(DetermineBlinkTrainerStageMode());
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Blink Trainer Start handler failed");
            }
        }

        #endregion

        #region Blink Trainer Tab — live mode + state machine (Phase D)

        // ──────────────────────────────────────────────────────────────────
        // Stage mode (Demo vs Live)
        // ──────────────────────────────────────────────────────────────────

        private enum BlinkTrainerStageMode
        {
            Demo,         // no consent / no folders / non-premium (Phase E)
            LivePreview,  // ready but no session running — show real swaps in preview
            LiveSession,  // session running — preview keeps mirroring (D.5)
        }

        private BlinkTrainerStageMode _currentBlinkTrainerStageMode = BlinkTrainerStageMode.Demo;
        private bool _blinkTrainerLiveSubscribed;

        // Live-mode pool cache. Token combines the folder list + IncludeVideos
        // bool; rebuild whenever it changes.
        private BlinkTrainerAssetPool? _blinkTrainerLivePool;
        private string _blinkTrainerLivePoolToken = "";
        private string? _blinkTrainerLiveLastPickedPath;

        private BlinkTrainerStageMode DetermineBlinkTrainerStageMode()
        {
            // Phase E: non-premium users always see the demo loop. Their saved
            // folder list stays in settings but doesn't render on the stage,
            // because the demo is what's visible to them through the gate.
            if (App.Patreon?.HasPremiumAccess != true)
                return BlinkTrainerStageMode.Demo;

            var s = App.Settings?.Current;
            bool consented = WebcamTrackingService.IsConsentCurrent();
            bool hasFolders = (s?.BlinkTrainerFolders?.Count ?? 0) > 0;
            bool running = App.BlinkTrainer?.IsRunning == true;

            if (running) return BlinkTrainerStageMode.LiveSession;
            if (consented && hasFolders) return BlinkTrainerStageMode.LivePreview;
            return BlinkTrainerStageMode.Demo;
        }

        /// <summary>
        /// Shows / hides the premium gate based on HasPremiumAccess. Mirrors
        /// the existing RefreshPremiumGate pattern (Bambi / Awareness / etc.)
        /// but keeps Blink Trainer's gate self-contained so the gate logic and
        /// the stage-mode short-circuit live together. Also disables the
        /// gated StackPanel (H.1) so keyboard focus can't tab past the gate
        /// into covered controls — a non-premium user shouldn't be able to
        /// adjust the duration slider via arrow keys with no visible feedback.
        /// </summary>
        private void RefreshBlinkTrainerGate()
        {
            if (BlinkTrainerGate == null) return;
            bool premium = App.Patreon?.HasPremiumAccess == true;
            BlinkTrainerGate.Visibility = premium ? Visibility.Collapsed : Visibility.Visible;
            if (BlinkTrainerGatedContent != null)
                BlinkTrainerGatedContent.IsEnabled = premium;
            // Stage actions (status row, Start session, tracker toggle) moved
            // under the preview in v5.9.9; they sit outside the gate overlay's
            // reach, so gate them via IsEnabled here.
            if (BlinkTrainerStageActions != null)
                BlinkTrainerStageActions.IsEnabled = premium;
        }

        private async void BtnBlinkTrainerStartStopTracker_Click(object sender, RoutedEventArgs e)
        {
            var svc = App.Webcam;
            if (svc == null) return;

            if (svc.IsRunning)
            {
                svc.Stop();
                RefreshBlinkTrainerTrackerButton();
                return;
            }

            if (!WebcamTrackingService.IsConsentCurrent())
            {
                var dlg = new WebcamConsentDialog { Owner = this };
                var ok = dlg.ShowDialog();
                if (ok != true || !dlg.ConsentGiven) return;
            }

            EnsureWebcamDebugSubscribed();
            await StartWebcamOffUiThreadAsync(svc);
            RefreshBlinkTrainerTrackerButton();
        }

        // Keeps the BT tracker toggle in sync with WebcamTrackingService.IsRunning.
        // Called from RefreshBlinkTrainerTab and after any local start/stop.
        // Also mirrors the label onto the Deeper-hub Start/Stop button so the
        // duplicated setup card stays consistent.
        private void RefreshBlinkTrainerTrackerButton()
        {
            bool running = App.Webcam?.IsRunning == true;
            var label = running ? "Stop tracker" : "Start tracker";
            if (BtnBlinkTrainerStartStopTracker != null)
                BtnBlinkTrainerStartStopTracker.Content = label;
            if (BtnDeeperWebcamStartStopTracker != null)
                BtnDeeperWebcamStartStopTracker.Content = label;
        }

        private void BtnBlinkTrainerGateUnlock_Click(object sender, RoutedEventArgs e)
        {
            // Same path every other gated tab uses (Bambi / Haptics / etc.):
            // open the dashboard's App Info & Data popup where the Patreon
            // login lives.
            ShowAppInfoPopup();
        }

        /// <summary>
        /// Idempotent transition. Stops the loop/subscription owned by the
        /// outgoing mode and starts the incoming one. Both LivePreview and
        /// LiveSession use the same OnBlink subscription so transitioning
        /// between them is a no-op.
        /// </summary>
        private void ApplyBlinkTrainerStageMode(BlinkTrainerStageMode mode)
        {
            if (mode == _currentBlinkTrainerStageMode) return;

            bool wasLive = _currentBlinkTrainerStageMode != BlinkTrainerStageMode.Demo;
            bool nowLive = mode != BlinkTrainerStageMode.Demo;

            if (wasLive && !nowLive)
            {
                UnsubscribeBlinkTrainerLiveBlink();
                StartBlinkTrainerDemoLoop();
            }
            else if (!wasLive && nowLive)
            {
                StopBlinkTrainerDemoLoop();
                // Reset stage to a known state before live mode takes over so
                // the user doesn't see a stale demo asset in their first frame.
                ResetBlinkTrainerStageForLive();
                SubscribeBlinkTrainerLiveBlink();
            }

            _currentBlinkTrainerStageMode = mode;
            App.Logger?.Debug("BlinkTrainer stage mode -> {Mode}", mode);
        }

        private void ResetBlinkTrainerStageForLive()
        {
            try
            {
                // Park both images on the first live blink's "incoming" slot.
                if (BlinkTrainerStageImageA != null)
                {
                    BlinkTrainerStageImageA.BeginAnimation(UIElement.OpacityProperty, null);
                    BlinkTrainerStageImageA.Opacity = 0;
                }
                if (BlinkTrainerStageImageB != null)
                {
                    BlinkTrainerStageImageB.BeginAnimation(UIElement.OpacityProperty, null);
                    BlinkTrainerStageImageB.Opacity = 0;
                }
                if (BlinkTrainerStageMedia != null)
                {
                    try { BlinkTrainerStageMedia.Stop(); } catch { }
                    BlinkTrainerStageMedia.Opacity = 0;
                }
                _blinkTrainerLiveLastPickedPath = null;
                _blinkTrainerDemoUsingA = true; // first live pick goes to A
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "ResetBlinkTrainerStageForLive failed"); }
        }

        private void SubscribeBlinkTrainerLiveBlink()
        {
            if (_blinkTrainerLiveSubscribed) return;
            if (App.Webcam == null) return;
            App.Webcam.OnBlink += OnBlinkTrainerStagePreviewBlink;
            _blinkTrainerLiveSubscribed = true;
        }

        private void UnsubscribeBlinkTrainerLiveBlink()
        {
            if (!_blinkTrainerLiveSubscribed) return;
            if (App.Webcam != null) App.Webcam.OnBlink -= OnBlinkTrainerStagePreviewBlink;
            _blinkTrainerLiveSubscribed = false;
        }

        /// <summary>
        /// Invalidate the cached live pool so the next OnBlink rebuilds. Called
        /// on folder add/remove and IncludeVideos toggle.
        /// </summary>
        private void InvalidateBlinkTrainerLivePool()
        {
            _blinkTrainerLivePool = null;
            _blinkTrainerLivePoolToken = "";
        }

        private BlinkTrainerAssetPool GetOrBuildBlinkTrainerLivePool()
        {
            var s = App.Settings?.Current;
            var folders = s?.BlinkTrainerFolders ?? new List<string>();
            bool includeVideos = s?.BlinkTrainerIncludeVideos == true;
            var token = string.Join("|", folders) + "::" + includeVideos;
            if (_blinkTrainerLivePool == null || _blinkTrainerLivePoolToken != token)
            {
                _blinkTrainerLivePool = BlinkTrainerAssetPool.Build(folders, includeVideos);
                _blinkTrainerLivePoolToken = token;
            }
            return _blinkTrainerLivePool;
        }

        /// <summary>
        /// Hard-cut swap on every real blink. App.Webcam.OnBlink already raises
        /// on the UI dispatcher (per WebcamTrackingService:1642), but Dispatcher
        /// check is cheap defense.
        /// </summary>
        private void OnBlinkTrainerStagePreviewBlink()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(OnBlinkTrainerStagePreviewBlink));
                return;
            }
            try
            {
                var pool = GetOrBuildBlinkTrainerLivePool();
                if (pool.IsEmpty) return;
                var path = pool.PickRandom(_blinkTrainerLiveLastPickedPath);
                if (string.IsNullOrEmpty(path)) return;
                _blinkTrainerLiveLastPickedPath = path;

                if (BlinkTrainerAssetPool.IsVideo(path))
                    ApplyBlinkTrainerLiveVideo(path);
                else
                    ApplyBlinkTrainerLiveImage(path);
            }
            catch (Exception ex)
            {
                // Don't crash; just skip this swap. Demo fallback is reserved
                // for non-configured state, not live errors.
                App.Logger?.Warning(ex, "OnBlinkTrainerStagePreviewBlink failed");
            }
        }

        private void ApplyBlinkTrainerLiveImage(string path)
        {
            if (BlinkTrainerStageImageA == null || BlinkTrainerStageImageB == null) return;

            // Stop any playing video first.
            if (BlinkTrainerStageMedia != null)
            {
                try { BlinkTrainerStageMedia.Stop(); } catch { }
                BlinkTrainerStageMedia.Opacity = 0;
            }

            BitmapImage bmp;
            try
            {
                bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BlinkTrainer live image load failed for {Path}", path);
                return;
            }

            // Hard-cut swap — kill any in-flight animations from demo mode and
            // pin opacities directly. Toggle which Image control is "active".
            Image incoming = _blinkTrainerDemoUsingA ? BlinkTrainerStageImageB : BlinkTrainerStageImageA;
            Image outgoing = _blinkTrainerDemoUsingA ? BlinkTrainerStageImageA : BlinkTrainerStageImageB;

            incoming.BeginAnimation(UIElement.OpacityProperty, null);
            outgoing.BeginAnimation(UIElement.OpacityProperty, null);
            incoming.Source = bmp;
            incoming.Opacity = 1;
            outgoing.Opacity = 0;

            _blinkTrainerDemoUsingA = !_blinkTrainerDemoUsingA;
        }

        private void ApplyBlinkTrainerLiveVideo(string path)
        {
            if (BlinkTrainerStageMedia == null) return;

            // Hide both images while the video is on top.
            if (BlinkTrainerStageImageA != null)
            {
                BlinkTrainerStageImageA.BeginAnimation(UIElement.OpacityProperty, null);
                BlinkTrainerStageImageA.Opacity = 0;
            }
            if (BlinkTrainerStageImageB != null)
            {
                BlinkTrainerStageImageB.BeginAnimation(UIElement.OpacityProperty, null);
                BlinkTrainerStageImageB.Opacity = 0;
            }

            try
            {
                BlinkTrainerStageMedia.Stop();
                BlinkTrainerStageMedia.Source = new Uri(path);
                BlinkTrainerStageMedia.Opacity = 1;
                BlinkTrainerStageMedia.Play();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BlinkTrainer live video load failed for {Path}", path);
                BlinkTrainerStageMedia.Opacity = 0;
            }
        }

        private void BlinkTrainerStageMedia_MediaEnded(object sender, RoutedEventArgs e)
        {
            // Loop the preview video until the next blink swaps it.
            try
            {
                if (BlinkTrainerStageMedia == null) return;
                BlinkTrainerStageMedia.Position = TimeSpan.Zero;
                BlinkTrainerStageMedia.Play();
            }
            catch { }
        }

        // ──────────────────────────────────────────────────────────────────
        // Status row state machine
        // ──────────────────────────────────────────────────────────────────

        private enum BlinkTrainerStatusState
        {
            IdleReady,
            Running,
            NeedsConsent,
            NeedsFolders,
            NeedsCalibration,
            Error,
        }

        private BlinkTrainerStatusState _currentBlinkTrainerStatusState = BlinkTrainerStatusState.IdleReady;
        private RoutedEventHandler? _blinkTrainerStatusActionHandler;
        private Storyboard? _blinkTrainerStatusDotPulseClock;

        private BlinkTrainerStatusState DetermineBlinkTrainerStatusState()
        {
            if (App.BlinkTrainer?.IsRunning == true)
                return BlinkTrainerStatusState.Running;

            // Service exposes LastError as a non-empty string after a failure.
            if (!string.IsNullOrEmpty(App.BlinkTrainer?.LastError))
                return BlinkTrainerStatusState.Error;

            if (!WebcamTrackingService.IsConsentCurrent())
                return BlinkTrainerStatusState.NeedsConsent;

            var folderCount = App.Settings?.Current?.BlinkTrainerFolders?.Count ?? 0;
            if (folderCount == 0)
                return BlinkTrainerStatusState.NeedsFolders;

            if (IsMultiMonitorEnvironment() && !HasUsableCalibration())
                return BlinkTrainerStatusState.NeedsCalibration;

            return BlinkTrainerStatusState.IdleReady;
        }

        private static bool IsMultiMonitorEnvironment()
        {
            try
            {
                var screens = System.Windows.Forms.Screen.AllScreens;
                return screens != null && screens.Length > 1;
            }
            catch { return false; }
        }

        private static bool HasUsableCalibration()
        {
            var cal = App.Webcam?.Calibration;
            if (cal == null) return false;
            // Pre-multimonitor saves had empty DeviceName — see the existing
            // recalibrate-multimonitor sticky toast at MainWindow.xaml.cs:1690.
            if (cal.MonitorBounds == null) return false;
            return !string.IsNullOrEmpty(cal.MonitorBounds.DeviceName);
        }

        private void RefreshBlinkTrainerStatusRow()
        {
            if (BlinkTrainerStatusDot == null || BlinkTrainerStatusText == null) return;

            var state = DetermineBlinkTrainerStatusState();
            if (state != _currentBlinkTrainerStatusState)
            {
                App.Logger?.Debug("BlinkTrainer status state -> {State}", state);
                _currentBlinkTrainerStatusState = state;
            }
            ApplyBlinkTrainerStatusState(state);
        }

        private void ApplyBlinkTrainerStatusState(BlinkTrainerStatusState state)
        {
            var pinkBrush = FindResource("PinkBrush") as Brush ?? Brushes.HotPink;
            Brush amber = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xD0, 0x80));
            Brush green = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4A, 0xDE, 0x80));
            Brush red = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44));
            amber.Freeze();
            green.Freeze();
            red.Freeze();

            // Stop any prior animation explicitly so dot opacity isn't stuck
            // mid-pulse when leaving IdleReady.
            BlinkTrainerStatusDot.BeginAnimation(UIElement.OpacityProperty, null);
            BlinkTrainerStatusDot.Opacity = 1;
            StopBlinkTrainerStatusDotPulse();

            string startLabel = Localization.Loc.Get("blink_trainer_start_session");
            string stopLabel = Localization.Loc.Get("blink_trainer_stop_session");

            switch (state)
            {
                case BlinkTrainerStatusState.IdleReady:
                    BlinkTrainerStatusDot.Fill = pinkBrush;
                    BlinkTrainerStatusText.Text = Localization.Loc.Get("blink_trainer_status_ready");
                    BlinkTrainerStatusText.Foreground = FindResource("TextMutedBrush") as Brush ?? Brushes.Gray;
                    WireBlinkTrainerStatusAction(null, null);
                    SetStartButtonState(enabled: true, content: startLabel);
                    StartBlinkTrainerStatusDotPulse();
                    break;

                case BlinkTrainerStatusState.Running:
                    BlinkTrainerStatusDot.Fill = green;
                    // Initial text — BlinkTrainerTick takes over each second.
                    var rem = App.BlinkTrainer?.Remaining ?? TimeSpan.Zero;
                    BlinkTrainerStatusText.Text = Localization.Loc.GetF("blink_trainer_status_running", rem.ToString(rem.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss"));
                    BlinkTrainerStatusText.Foreground = FindResource("TextMutedBrush") as Brush ?? Brushes.Gray;
                    WireBlinkTrainerStatusAction(null, null);
                    SetStartButtonState(enabled: true, content: stopLabel);
                    break;

                case BlinkTrainerStatusState.NeedsConsent:
                    BlinkTrainerStatusDot.Fill = amber;
                    BlinkTrainerStatusText.Text = Localization.Loc.Get("blink_trainer_status_needs_consent");
                    BlinkTrainerStatusText.Foreground = FindResource("TextMutedBrush") as Brush ?? Brushes.Gray;
                    WireBlinkTrainerStatusAction(
                        Localization.Loc.Get("blink_trainer_consent_grant"),
                        BlinkTrainerStatusAction_GrantConsent);
                    SetStartButtonState(enabled: false, content: startLabel);
                    break;

                case BlinkTrainerStatusState.NeedsFolders:
                    BlinkTrainerStatusDot.Fill = amber;
                    BlinkTrainerStatusText.Text = Localization.Loc.Get("blink_trainer_status_needs_folders");
                    BlinkTrainerStatusText.Foreground = FindResource("TextMutedBrush") as Brush ?? Brushes.Gray;
                    WireBlinkTrainerStatusAction(
                        Localization.Loc.Get("blink_trainer_add_folder"),
                        BlinkTrainerStatusAction_AddFolder);
                    SetStartButtonState(enabled: false, content: startLabel);
                    break;

                case BlinkTrainerStatusState.NeedsCalibration:
                    BlinkTrainerStatusDot.Fill = amber;
                    BlinkTrainerStatusText.Text = Localization.Loc.Get("blink_trainer_status_needs_calibration");
                    BlinkTrainerStatusText.Foreground = FindResource("TextMutedBrush") as Brush ?? Brushes.Gray;
                    WireBlinkTrainerStatusAction(
                        Localization.Loc.Get("blink_trainer_calibration_btn"),
                        BlinkTrainerStatusAction_Calibrate);
                    // Calibration is recommended for multi-monitor only; let
                    // the user start without it.
                    SetStartButtonState(enabled: true, content: startLabel);
                    break;

                case BlinkTrainerStatusState.Error:
                    BlinkTrainerStatusDot.Fill = red;
                    // Service-supplied error text — passed through as-is. Service
                    // LastError strings are not currently localized; if/when they
                    // are, this branch picks up the loc'd value transparently.
                    BlinkTrainerStatusText.Text = App.BlinkTrainer?.LastError ?? "";
                    BlinkTrainerStatusText.Foreground = red;
                    WireBlinkTrainerStatusAction(null, null);
                    SetStartButtonState(enabled: true, content: startLabel);
                    break;
            }
        }

        private void SetStartButtonState(bool enabled, string content)
        {
            if (BtnBlinkTrainerStartSession == null) return;
            BtnBlinkTrainerStartSession.IsEnabled = enabled;
            BtnBlinkTrainerStartSession.Content = content;
        }

        /// <summary>
        /// Single point of truth for the status action button's delegate. Unhooks
        /// any prior handler before wiring the new one, so handlers can't accumulate
        /// across state transitions.
        /// </summary>
        private void WireBlinkTrainerStatusAction(string? content, RoutedEventHandler? handler)
        {
            if (BlinkTrainerStatusAction == null) return;

            if (_blinkTrainerStatusActionHandler != null)
                BlinkTrainerStatusAction.Click -= _blinkTrainerStatusActionHandler;
            _blinkTrainerStatusActionHandler = handler;

            if (content != null && handler != null)
            {
                BlinkTrainerStatusAction.Content = content;
                BlinkTrainerStatusAction.Click += handler;
                BlinkTrainerStatusAction.Visibility = Visibility.Visible;
            }
            else
            {
                BlinkTrainerStatusAction.Visibility = Visibility.Collapsed;
            }
        }

        private void StartBlinkTrainerStatusDotPulse()
        {
            try
            {
                if (BlinkTrainerStatusDot == null) return;
                var sb = BlinkTrainerStatusDot.Resources["BlinkTrainerStatusDotPulse"] as Storyboard;
                if (sb == null) return;
                _blinkTrainerStatusDotPulseClock = sb;
                sb.Begin(BlinkTrainerStatusDot, true);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "StartBlinkTrainerStatusDotPulse failed"); }
        }

        private void StopBlinkTrainerStatusDotPulse()
        {
            try
            {
                if (_blinkTrainerStatusDotPulseClock != null && BlinkTrainerStatusDot != null)
                    _blinkTrainerStatusDotPulseClock.Stop(BlinkTrainerStatusDot);
                _blinkTrainerStatusDotPulseClock = null;
            }
            catch { }
        }

        // Status action button handlers — routed via WireBlinkTrainerStatusAction
        // so only the current state's handler is wired at any time.

        private void BlinkTrainerStatusAction_GrantConsent(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new WebcamConsentDialog { Owner = this };
                dlg.ShowDialog();
                RefreshBlinkTrainerWebcamColumn();
                RefreshBlinkTrainerStatusRow();
                ApplyBlinkTrainerStageMode(DetermineBlinkTrainerStageMode());
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "BlinkTrainerStatusAction_GrantConsent failed"); }
        }

        private void BlinkTrainerStatusAction_AddFolder(object sender, RoutedEventArgs e)
        {
            // Reuse the same flow as the Asset Packs column's "+ Add folder" so
            // there's exactly one folder-pick path.
            BtnBlinkTrainerAddFolderCard_Click(sender, e);
            InvalidateBlinkTrainerLivePool();
            RefreshBlinkTrainerStatusRow();
            ApplyBlinkTrainerStageMode(DetermineBlinkTrainerStageMode());
        }

        private void BlinkTrainerStatusAction_Calibrate(object sender, RoutedEventArgs e)
        {
            try
            {
                WebcamCalibrationWindow.ShowDialogWithRecalibrate(this);
                RefreshBlinkTrainerWebcamColumn();
                RefreshBlinkTrainerStatusRow();
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "BlinkTrainerStatusAction_Calibrate failed"); }
        }

        #endregion

        #region Blink Trainer Tab — controls (Phase C)

        // ──────────────────────────────────────────────────────────────────
        // Column 1: Asset Packs
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds the folder card stack from AppSettings.Current.BlinkTrainerFolders.
        /// Each card shows the folder's display name, an image/video count summary,
        /// and a × remove button. Card hover state is handled inline via a local
        /// Style trigger (no separate XAML resource).
        /// </summary>
        private void RebuildBlinkTrainerFolderCards()
        {
            try
            {
                if (BlinkTrainerFolderCardsHost == null) return;
                BlinkTrainerFolderCardsHost.Children.Clear();

                var settings = App.Settings?.Current;
                if (settings?.BlinkTrainerFolders == null) return;

                bool includeVideos = settings.BlinkTrainerIncludeVideos;
                foreach (var folder in settings.BlinkTrainerFolders.ToList())
                {
                    BlinkTrainerFolderCardsHost.Children.Add(BuildBlinkTrainerFolderCard(folder, includeVideos));
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "RebuildBlinkTrainerFolderCards failed");
            }
        }

        private Border BuildBlinkTrainerFolderCard(string folder, bool includeVideos)
        {
            // Card border with hover-state border-brush swap via a local Style.
            // Background brush stays #11000000; the only thing that changes is
            // the border color (0.3 pink at rest, full pink on hover).
            var pinkColorObj = TryFindResource("PinkColor");
            var pinkColor = pinkColorObj is System.Windows.Media.Color c
                ? c
                : System.Windows.Media.Color.FromRgb(0xFF, 0x69, 0xB4);
            var restBorder = new SolidColorBrush(pinkColor) { Opacity = 0.3 };
            restBorder.Freeze();

            var style = new Style(typeof(Border));
            var hoverTrigger = new Trigger { Property = Border.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(
                Border.BorderBrushProperty,
                FindResource("PinkBrush")));
            style.Triggers.Add(hoverTrigger);

            var card = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x11, 0, 0, 0)),
                BorderBrush = restBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 8),
                Style = style,
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel();
            Grid.SetColumn(info, 0);

            // Display name: folder basename, falling back to parent if blank
            // (e.g. trailing-slash paths). Tooltip carries the full path.
            string displayName = "";
            try
            {
                displayName = System.IO.Path.GetFileName(folder);
                if (string.IsNullOrWhiteSpace(displayName))
                    displayName = new System.IO.DirectoryInfo(folder).Name;
            }
            catch { displayName = folder; }
            if (string.IsNullOrWhiteSpace(displayName)) displayName = folder;

            info.Children.Add(new TextBlock
            {
                Text = displayName,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Medium,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = folder,
            });

            // Count summary via AssetPack.FromFolder. Null pack = invalid/empty.
            var pack = Lab.GazeMinigame.AssetPack.FromFolder(folder);
            string countLine;
            Brush countBrush = FindResource("TextMutedBrush") as Brush ?? Brushes.Gray;
            if (pack == null)
            {
                countLine = Localization.Loc.Get("blink_trainer_folder_empty_or_invalid");
                countBrush = FindResource("TextDimBrush") as Brush ?? countBrush;
            }
            else
            {
                int gifCount = pack.ImagePaths.Count(p =>
                    System.IO.Path.GetExtension(p).Equals(".gif", StringComparison.OrdinalIgnoreCase));
                int nonGifImages = pack.ImagePaths.Count - gifCount;

                if (includeVideos)
                {
                    countLine = $"{pack.ImagePaths.Count} images, {pack.VideoPaths.Count} videos";
                }
                else
                {
                    countLine = gifCount > 0
                        ? $"{nonGifImages} images, {gifCount} GIFs"
                        : $"{pack.ImagePaths.Count} images";
                }
            }
            info.Children.Add(new TextBlock
            {
                Text = countLine,
                Foreground = countBrush,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
            });

            grid.Children.Add(info);

            // × remove button. Tag carries the folder path so the handler can
            // disambiguate when multiple cards live in the same StackPanel.
            var removeBtn = new Button
            {
                Content = "×",
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = FindResource("TextMutedBrush") as Brush ?? Brushes.Gray,
                Padding = new Thickness(6, 0, 6, 0),
                FontSize = 16,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Top,
                Tag = folder,
            };
            removeBtn.Click += BtnBlinkTrainerRemoveFolderCard_Click;
            Grid.SetColumn(removeBtn, 1);
            grid.Children.Add(removeBtn);

            card.Child = grid;
            return card;
        }

        private void BtnBlinkTrainerAddFolderCard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using var dlg = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Pick a folder of images / GIFs for Blink Trainer",
                };
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

                var folder = dlg.SelectedPath;
                if (string.IsNullOrWhiteSpace(folder)) return;

                var settings = App.Settings?.Current;
                if (settings == null) return;

                if (settings.BlinkTrainerFolders.Any(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase)))
                    return;

                settings.BlinkTrainerFolders.Add(folder);
                App.Settings?.Save();
                RebuildBlinkTrainerFolderCards();
                InvalidateBlinkTrainerLivePool();
                RefreshBlinkTrainerStatusRow();
                ApplyBlinkTrainerStageMode(DetermineBlinkTrainerStageMode());
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BtnBlinkTrainerAddFolderCard_Click failed");
            }
        }

        private void BtnBlinkTrainerRemoveFolderCard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not Button btn) return;
                if (btn.Tag is not string folder) return;

                var settings = App.Settings?.Current;
                if (settings == null) return;

                settings.BlinkTrainerFolders.RemoveAll(f =>
                    string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
                App.Settings?.Save();
                RebuildBlinkTrainerFolderCards();
                InvalidateBlinkTrainerLivePool();
                RefreshBlinkTrainerStatusRow();
                ApplyBlinkTrainerStageMode(DetermineBlinkTrainerStageMode());
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BtnBlinkTrainerRemoveFolderCard_Click failed");
            }
        }

        private void ToggleBlinkTrainerIncludeVideos_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ToggleBlinkTrainerIncludeVideos == null) return;
                var settings = App.Settings?.Current;
                if (settings == null) return;

                bool newValue = ToggleBlinkTrainerIncludeVideos.IsChecked == true;
                if (settings.BlinkTrainerIncludeVideos == newValue) return;
                settings.BlinkTrainerIncludeVideos = newValue;
                App.Settings?.Save();

                // Rebuild cards so count summaries reflect the new mode, and
                // invalidate the live pool so the next blink picks from the
                // updated mix.
                RebuildBlinkTrainerFolderCards();
                InvalidateBlinkTrainerLivePool();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "ToggleBlinkTrainerIncludeVideos_Changed failed");
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // Column 2: Session
        // ──────────────────────────────────────────────────────────────────

        private void SliderBlinkTrainerDurationNew_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                int v = (int)Math.Round(e.NewValue);
                if (App.Settings?.Current is { } s) s.BlinkTrainerDurationMinutes = v;
                if (TxtBlinkTrainerDurationValue != null)
                    TxtBlinkTrainerDurationValue.Text = $"{v} min";
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "SliderBlinkTrainerDurationNew_Changed failed"); }
        }

        private void SliderBlinkTrainerOpacityNew_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                int v = (int)Math.Round(e.NewValue);
                if (App.Settings?.Current is { } s) s.BlinkTrainerOpacity = v;
                if (TxtBlinkTrainerOpacityValue != null)
                    TxtBlinkTrainerOpacityValue.Text = $"{v}%";
                ApplyBlinkTrainerOpacityFillOpacity(v);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "SliderBlinkTrainerOpacityNew_Changed failed"); }
        }

        // ── H.7: reactive opacity slider fill ──
        // The filled portion of the BlinkTrainerGradientSlider template is a
        // RepeatButton inside the Track named "PART_Track". On the slider's
        // Loaded event we walk the template once to cache that RepeatButton,
        // then ValueChanged sets its Opacity to map 1-100 -> 0.109-1.0. The
        // shared style means the Duration slider has the same RepeatButton,
        // but only the Opacity slider's Loaded handler caches a reference, so
        // only its fill fades.
        private RepeatButton? _blinkTrainerOpacityFillButton;

        private void SliderBlinkTrainerOpacityNew_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not Slider slider) return;
                if (slider.Template?.FindName("PART_Track", slider) is not Track track) return;
                _blinkTrainerOpacityFillButton = track.DecreaseRepeatButton;
                ApplyBlinkTrainerOpacityFillOpacity((int)Math.Round(slider.Value));
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "SliderBlinkTrainerOpacityNew_Loaded failed"); }
        }

        private void ApplyBlinkTrainerOpacityFillOpacity(int sliderValue)
        {
            if (_blinkTrainerOpacityFillButton == null) return;
            // Linear map 1-100 -> ~0.109-1.0. Slope 0.9 over 99-unit range,
            // offset 0.1 so v=1 is still faintly visible.
            int v = Math.Clamp(sliderValue, 1, 100);
            _blinkTrainerOpacityFillButton.Opacity = v / 100.0 * 0.9 + 0.1;
        }

        // ── H.6: reactive value-label scale on slider drag ──
        // PreviewMouseLeftButtonDown fires on press anywhere in the slider area
        // (track or thumb). LostMouseCapture catches the release-outside-slider
        // case where MouseLeftButtonUp wouldn't fire on the slider itself.

        private void BlinkTrainerSlider_DragStart(object sender, MouseButtonEventArgs e)
        {
            if (sender is Slider s) AnimateBlinkTrainerSliderLabel(s, scaleTo: 1.15, durationMs: 100, easeOut: true);
        }

        private void BlinkTrainerSlider_DragEnd(object sender, MouseButtonEventArgs e)
        {
            if (sender is Slider s) AnimateBlinkTrainerSliderLabel(s, scaleTo: 1.0, durationMs: 150, easeOut: false);
        }

        private void BlinkTrainerSlider_LostCapture(object sender, MouseEventArgs e)
        {
            if (sender is Slider s) AnimateBlinkTrainerSliderLabel(s, scaleTo: 1.0, durationMs: 150, easeOut: false);
        }

        private void AnimateBlinkTrainerSliderLabel(Slider slider, double scaleTo, int durationMs, bool easeOut)
        {
            TextBlock? label = null;
            if (slider == SliderBlinkTrainerDurationNew) label = TxtBlinkTrainerDurationValue;
            else if (slider == SliderBlinkTrainerOpacityNew) label = TxtBlinkTrainerOpacityValue;
            if (label?.RenderTransform is not ScaleTransform st) return;

            IEasingFunction ease = easeOut
                ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 }
                : new QuadraticEase { EasingMode = EasingMode.EaseOut };
            var anim = new DoubleAnimation(scaleTo, TimeSpan.FromMilliseconds(durationMs))
            {
                EasingFunction = ease,
            };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }

        private void BlinkTrainerMixOptionSame_Click(object sender, MouseButtonEventArgs e) => SetMixMode(false);
        private void BlinkTrainerMixOptionMix_Click(object sender, MouseButtonEventArgs e) => SetMixMode(true);

        private void SetMixMode(bool isMix)
        {
            try
            {
                if (App.Settings?.Current is { } s)
                {
                    if (s.BlinkTrainerMixImages != isMix)
                    {
                        s.BlinkTrainerMixImages = isMix;
                        App.Settings?.Save();
                    }
                }
                SetMixModeSelection(isMix);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "SetMixMode failed"); }
        }

        /// <summary>
        /// Paints the selected mix-mode option border with full pink + a pink
        /// drop-shadow glow, and clears the unselected one. Called on tab show
        /// and on every option click.
        /// </summary>
        private void SetMixModeSelection(bool isMix)
        {
            if (BlinkTrainerMixOptionSame == null || BlinkTrainerMixOptionMix == null) return;

            var pinkBrush = FindResource("PinkBrush") as Brush ?? Brushes.HotPink;
            var pinkColorObj = TryFindResource("PinkColor");
            var pinkColor = pinkColorObj is System.Windows.Media.Color c
                ? c
                : System.Windows.Media.Color.FromRgb(0xFF, 0x69, 0xB4);

            void Apply(Border b, bool selected)
            {
                if (selected)
                {
                    b.BorderBrush = pinkBrush;
                    b.Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = pinkColor,
                        BlurRadius = 16,
                        ShadowDepth = 0,
                        Opacity = 0.6,
                    };
                }
                else
                {
                    b.BorderBrush = Brushes.Transparent;
                    b.Effect = null;
                }
            }
            Apply(BlinkTrainerMixOptionSame, !isMix);
            Apply(BlinkTrainerMixOptionMix, isMix);
        }

        // ──────────────────────────────────────────────────────────────────
        // Column 3: Webcam
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Refreshes every Webcam-column control: device list, consent
        /// card tint + button text, calibration status line. Called on tab
        /// show and after any dialog return (consent / calibrate / quick recal).
        /// </summary>
        private void RefreshBlinkTrainerWebcamColumn()
        {
            try
            {
                // Shared populator (Cleanup 1) — also refreshes the Lab combo.
                PopulateWebcamDeviceCombos();

                // Consent
                bool consented = WebcamTrackingService.IsConsentCurrent();
                if (BlinkTrainerConsentCard != null)
                {
                    if (consented)
                    {
                        // Green-tinted (granted)
                        BlinkTrainerConsentCard.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x1A, 0x4A, 0xDE, 0x80));
                        BlinkTrainerConsentCard.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4A, 0xDE, 0x80));
                    }
                    else
                    {
                        // Amber-tinted (required)
                        BlinkTrainerConsentCard.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x1A, 0xFF, 0xD0, 0x80));
                        BlinkTrainerConsentCard.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xD0, 0x80));
                    }
                }
                if (BlinkTrainerConsentStatus != null)
                {
                    BlinkTrainerConsentStatus.Text = Localization.Loc.Get(
                        consented ? "blink_trainer_consent_granted" : "blink_trainer_consent_required");
                }
                if (BtnBlinkTrainerManageConsent != null)
                {
                    BtnBlinkTrainerManageConsent.Content = Localization.Loc.Get(
                        consented ? "blink_trainer_consent_manage" : "blink_trainer_consent_grant");
                }
                if (BtnBlinkTrainerRevokeConsent != null)
                {
                    BtnBlinkTrainerRevokeConsent.Visibility = consented ? Visibility.Visible : Visibility.Collapsed;
                }

                // Calibration status line. All three branches are now loc'd;
                // the "Calibrated for {device}" line uses GetF with the device
                // name passed as the {0} substitution (still system-provided
                // text, but the surrounding wording is translatable).
                if (BlinkTrainerCalibrationStatus != null)
                {
                    var cal = App.Webcam?.Calibration;
                    if (cal == null)
                    {
                        BlinkTrainerCalibrationStatus.Text = Localization.Loc.Get("blink_trainer_calibration_none");
                    }
                    else if (cal.MonitorBounds != null && !string.IsNullOrEmpty(cal.MonitorBounds.DeviceName))
                    {
                        BlinkTrainerCalibrationStatus.Text = Localization.Loc.GetF(
                            "blink_trainer_calibration_calibrated_format", cal.MonitorBounds.DeviceName);
                    }
                    else
                    {
                        // Pre-multimonitor calibration data — flagged by the
                        // existing recalibrate-multimonitor sticky toast.
                        BlinkTrainerCalibrationStatus.Text = Localization.Loc.Get("blink_trainer_calibration_outdated");
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "RefreshBlinkTrainerWebcamColumn failed");
            }

            // Fan out to the Deeper hub's duplicate card (same state, same
            // service). Null-safe — Deeper card may not be loaded yet.
            RefreshDeeperWebcamColumn();
            // Tracker button label is shared state across both cards.
            RefreshBlinkTrainerTrackerButton();
        }

        private void CmbBlinkTrainerWebcamDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_webcamDevicePopulating) return;
            if (CmbBlinkTrainerWebcamDevice?.SelectedItem is not ComboBoxItem item) return;
            if (item.Tag is not int idx || idx < 0) return;

            if (App.Settings?.Current is { } s)
            {
                if (s.WebcamDeviceIndex == idx) return;
                s.WebcamDeviceIndex = idx;
                s.WebcamDeviceName = item.Content?.ToString() ?? "";
                App.Settings?.Save();
            }

            // Cross-tab sync (Cleanup 1) — propagate to the Lab combo.
            SyncWebcamComboSelections(idx);
        }

        private void BtnBlinkTrainerWebcamRefresh_Click(object sender, RoutedEventArgs e)
        {
            try { PopulateWebcamDeviceCombos(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "BtnBlinkTrainerWebcamRefresh_Click failed"); }
        }

        private void BtnBlinkTrainerManageConsent_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Same dialog in both directions — non-consenting users go through
                // the grant gates; already-consenting users see review-only copy.
                // Closing without explicit Enable leaves WebcamConsentGiven alone.
                var dlg = new WebcamConsentDialog { Owner = this };
                dlg.ShowDialog();
                RefreshBlinkTrainerWebcamColumn();
                RefreshBlinkTrainerStatusRow();
                ApplyBlinkTrainerStageMode(DetermineBlinkTrainerStageMode());
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BtnBlinkTrainerManageConsent_Click failed");
            }
        }

        private void BtnBlinkTrainerRevokeConsent_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show(
                    this,
                    Localization.Loc.Get("blink_trainer_consent_revoke_confirm_body"),
                    Localization.Loc.Get("blink_trainer_consent_revoke_confirm_title"),
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning,
                    MessageBoxResult.Cancel);
                if (result != MessageBoxResult.OK) return;

                App.Webcam?.RevokeConsent();
                if (ChkWebcamDebugCursor != null) ChkWebcamDebugCursor.IsChecked = false;

                RefreshBlinkTrainerWebcamColumn();
                RefreshBlinkTrainerStatusRow();
                ApplyBlinkTrainerStageMode(DetermineBlinkTrainerStageMode());
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BtnBlinkTrainerRevokeConsent_Click failed");
            }
        }

        private void BtnBlinkTrainerCalibrate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                WebcamCalibrationWindow.ShowDialogWithRecalibrate(this);
                RefreshBlinkTrainerWebcamColumn();
                RefreshBlinkTrainerStatusRow();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BtnBlinkTrainerCalibrate_Click failed");
            }
        }

        private async void BtnBlinkTrainerQuickRecal_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var svc = App.Webcam;
                if (svc == null) return;
                if (!WebcamTrackingService.IsConsentCurrent())
                {
                    var consent = new WebcamConsentDialog { Owner = this };
                    if (consent.ShowDialog() != true || !consent.ConsentGiven)
                    {
                        RefreshBlinkTrainerWebcamColumn();
                        return;
                    }
                }
                if (svc.Calibration == null)
                {
                    System.Windows.MessageBox.Show(this,
                        Localization.Loc.Get("blink_trainer_quick_recal_needs_full_body"),
                        Localization.Loc.Get("blink_trainer_quick_recal_needs_full_title"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                bool startedHere = false;
                if (!svc.IsRunning)
                {
                    // Off the UI thread so the camera/ONNX load doesn't freeze
                    // the window; the loading splash shows during the wait.
                    if (await svc.StartAsync()) startedHere = true;
                }

                var recalDlg = new WebcamQuickRecalWindow { Owner = this };
                App.ApplyCalibrationScreenPlacement(recalDlg);
                recalDlg.ShowDialog();

                if (startedHere) svc.Stop();
                RefreshBlinkTrainerWebcamColumn();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BtnBlinkTrainerQuickRecal_Click failed");
            }
        }

        // ──────────────────────────────────────────────────────────────
        // Deeper hub webcam setup card. The card is a 1:1 visual copy of
        // the Blink Trainer setup card so non-Patreon users can grant
        // consent + calibrate without bumping into the Blink Trainer gate.
        // State is shared (AppSettings + App.Webcam), so handlers here
        // either delegate to the Blink Trainer handler (consent dialogs,
        // calibration windows) or replicate the device/monitor combo logic
        // with the same _webcamDevicePopulating / _webcamMonitorPopulating
        // guards. RefreshBlinkTrainerWebcamColumn fans out to the Deeper
        // card via RefreshDeeperWebcamColumn so any state change updates
        // both surfaces.
        // ──────────────────────────────────────────────────────────────

        private void CmbDeeperWebcamDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_webcamDevicePopulating) return;
            if (CmbDeeperWebcamDevice?.SelectedItem is not ComboBoxItem item) return;
            if (item.Tag is not int idx || idx < 0) return;

            if (App.Settings?.Current is { } s)
            {
                if (s.WebcamDeviceIndex == idx) return;
                s.WebcamDeviceIndex = idx;
                s.WebcamDeviceName = item.Content?.ToString() ?? "";
                App.Settings?.Save();
            }

            SyncWebcamComboSelections(idx);
        }

        private void BtnDeeperWebcamRefresh_Click(object sender, RoutedEventArgs e)
        {
            try { PopulateWebcamDeviceCombos(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "BtnDeeperWebcamRefresh_Click failed"); }
        }

        private void CmbDeeperWebcamMonitor_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_webcamMonitorPopulating) return;
            if (CmbDeeperWebcamMonitor?.SelectedItem is not ComboBoxItem item) return;
            if (item.Tag is not string deviceName) return;

            if (App.Settings?.Current is { } s)
            {
                if (string.Equals(s.WebcamCalibrationScreen, deviceName, StringComparison.OrdinalIgnoreCase)) return;
                s.WebcamCalibrationScreen = deviceName;
                App.Settings.Save();
            }

            SyncMonitorComboSelection(CmbWebcamMonitor, deviceName);
            SyncMonitorComboSelection(CmbBlinkTrainerWebcamMonitor, deviceName);
        }

        private void BtnDeeperWebcamManageConsent_Click(object sender, RoutedEventArgs e)
            => BtnBlinkTrainerManageConsent_Click(sender, e);

        private void BtnDeeperWebcamRevokeConsent_Click(object sender, RoutedEventArgs e)
            => BtnBlinkTrainerRevokeConsent_Click(sender, e);

        private void BtnDeeperWebcamCalibrate_Click(object sender, RoutedEventArgs e)
            => BtnBlinkTrainerCalibrate_Click(sender, e);

        private void BtnDeeperWebcamQuickRecal_Click(object sender, RoutedEventArgs e)
            => BtnBlinkTrainerQuickRecal_Click(sender, e);

        private void BtnDeeperWebcamStartStopTracker_Click(object sender, RoutedEventArgs e)
            => BtnBlinkTrainerStartStopTracker_Click(sender, e);

        private void ChkDeeperWebcamRestrictGazeToCalScreen_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading || _restrictGazeCheckboxSyncing) return;
            if (ChkDeeperWebcamRestrictGazeToCalScreen == null || App.Settings?.Current == null) return;
            bool v = ChkDeeperWebcamRestrictGazeToCalScreen.IsChecked == true;
            App.Settings.Current.RestrictGazeContentToCalibratedScreen = v;
            MirrorRestrictGazeToOtherCards(v, except: ChkDeeperWebcamRestrictGazeToCalScreen);
        }

        /// <summary>
        /// Mirrors RefreshBlinkTrainerWebcamColumn for the Deeper hub's setup
        /// card. Called from inside RefreshBlinkTrainerWebcamColumn so every
        /// existing consent/calibration trigger fans out here automatically.
        /// All element accesses are null-guarded — the Deeper hub UI may not
        /// be loaded yet on the first refresh (e.g. before the tab is shown).
        /// </summary>
        private void RefreshDeeperWebcamColumn()
        {
            try
            {
                if (App.Webcam == null) return;
                var consented = WebcamTrackingService.IsConsentCurrent();

                if (DeeperWebcamConsentCard != null)
                {
                    if (consented)
                    {
                        DeeperWebcamConsentCard.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x1A, 0x4A, 0xDE, 0x80));
                        DeeperWebcamConsentCard.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4A, 0xDE, 0x80));
                    }
                    else
                    {
                        DeeperWebcamConsentCard.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x1A, 0xFF, 0xD0, 0x80));
                        DeeperWebcamConsentCard.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xD0, 0x80));
                    }
                }
                if (DeeperWebcamConsentStatus != null)
                {
                    DeeperWebcamConsentStatus.Text = Localization.Loc.Get(
                        consented ? "blink_trainer_consent_granted" : "blink_trainer_consent_required");
                }
                if (BtnDeeperWebcamManageConsent != null)
                {
                    BtnDeeperWebcamManageConsent.Content = Localization.Loc.Get(
                        consented ? "blink_trainer_consent_manage" : "blink_trainer_consent_grant");
                }
                if (BtnDeeperWebcamRevokeConsent != null)
                {
                    BtnDeeperWebcamRevokeConsent.Visibility = consented ? Visibility.Visible : Visibility.Collapsed;
                }

                if (DeeperWebcamCalibrationStatus != null)
                {
                    var cal = App.Webcam.Calibration;
                    if (cal == null)
                    {
                        DeeperWebcamCalibrationStatus.Text = Localization.Loc.Get("blink_trainer_calibration_none");
                    }
                    else if (cal.MonitorBounds != null && !string.IsNullOrEmpty(cal.MonitorBounds.DeviceName))
                    {
                        DeeperWebcamCalibrationStatus.Text = Localization.Loc.GetF(
                            "blink_trainer_calibration_calibrated_format", cal.MonitorBounds.DeviceName);
                    }
                    else
                    {
                        DeeperWebcamCalibrationStatus.Text = Localization.Loc.Get("blink_trainer_calibration_outdated");
                    }
                }

                if (ChkDeeperWebcamRestrictGazeToCalScreen != null && App.Settings?.Current is { } s)
                {
                    bool want = s.RestrictGazeContentToCalibratedScreen;
                    if (ChkDeeperWebcamRestrictGazeToCalScreen.IsChecked != want)
                    {
                        _restrictGazeCheckboxSyncing = true;
                        try { ChkDeeperWebcamRestrictGazeToCalScreen.IsChecked = want; }
                        finally { _restrictGazeCheckboxSyncing = false; }
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "RefreshDeeperWebcamColumn failed");
            }
        }

        #endregion



        private void UpdateAchievementCount()
        {
            if (App.Achievements == null) return;

            // Free and patron counts are kept strictly separate — never summed.
            if (TxtAchievementCount != null)
            {
                var unlocked = App.Achievements.GetUnlockedCount(exclusive: false);
                var total = App.Achievements.GetTotalCount(exclusive: false);
                TxtAchievementCount.Text = Loc.GetF("label_0_1_achievements_unlocked", unlocked, total);
            }

            if (TxtPatronAchievementCount != null)
            {
                var pUnlocked = App.Achievements.GetUnlockedCount(exclusive: true);
                var pTotal = App.Achievements.GetTotalCount(exclusive: true);
                TxtPatronAchievementCount.Text = Loc.GetF("label_0_1_achievements_unlocked", pUnlocked, pTotal);
            }

            // Free users see the patron collection as a labeled, locked section.
            if (PatronAchievementsOverlay != null)
            {
                PatronAchievementsOverlay.Visibility = App.Patreon?.HasPremiumAccess == true
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
        }

        /// <summary>
        /// Sync Companion tab UI controls with current state
        /// </summary>
        private void SyncCompanionTabUI()
        {
            _isLoading = true;
            try
            {
                // Sync avatar enabled
                ChkAvatarEnabledCompanion.IsChecked = _avatarTubeWindow?.IsVisible == true;

                // Sync trigger mode
                ChkTriggerModeCompanion.IsChecked = App.Settings?.Current?.TriggerModeEnabled == true;
                TriggerSettingsPanelCompanion.Visibility = ChkTriggerModeCompanion.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

                // Sync trigger interval
                var interval = App.Settings?.Current?.TriggerIntervalSeconds ?? 60;
                SliderTriggerIntervalCompanion.Value = interval;
                TxtTriggerIntervalCompanion.Text = $"{interval}s";

                // Sync idle interval
                var idleInterval = App.Settings?.Current?.IdleGiggleIntervalSeconds ?? 120;
                SliderIdleIntervalCompanion.Value = idleInterval;
                TxtIdleIntervalCompanion.Text = $"{idleInterval}s";

                // Sync bubble persistence duration
                var bubbleDuration = App.Settings?.Current?.BubbleDurationSeconds ?? 2.0;
                SliderBubbleDurationCompanion.Value = bubbleDuration;
                TxtBubbleDurationCompanion.Text = $"{(int)bubbleDuration}s";

                // Sync detach status
                var isDetached = _avatarTubeWindow?.IsDetached == true;
                TxtDetachStatusCompanion.Text = isDetached ? "Floating freely" : "Anchored to window";
                BtnDetachCompanionTab.Content = isDetached ? "Attach" : "Detach";

                // Sync companion leveling UI (v5.3)
                UpdateCompanionCardsUI();

                // Sync AI Brain panel + hero pills (v5.9)
                SyncAiBrainUI();
            }
            finally
            {
                _isLoading = false;
            }
        }

        /// <summary>
        /// Updates the companion selection cards UI with current progress and active state.
        /// </summary>
        private void UpdateCompanionCardsUI()
        {
            if (App.Companion == null || App.Settings?.Current == null) return;

            var activeId = App.Companion.ActiveCompanion;
            var playerLevel = App.Settings.Current.PlayerLevel;

            // Update each companion card
            var cards = new[] { CompanionCard0, CompanionCard1, CompanionCard2, CompanionCard3, CompanionCard4 };
            var levelTexts = new[] { TxtCompanion0Level, TxtCompanion1Level, TxtCompanion2Level, TxtCompanion3Level, TxtCompanion4Level };
            var lockTexts = new[] { TxtCompanion0Lock, TxtCompanion1Lock, TxtCompanion2Lock, TxtCompanion3Lock, TxtCompanion4Lock };
            var nameTexts = new[] { TxtCompanion0Name, TxtCompanion1Name, TxtCompanion2Name, TxtCompanion3Name, TxtCompanion4Name };
            var colors = new[] { App.Mods?.GetAccentColorHex() ?? "#FF69B4", "#9370DB", "#50C878", "#FF6B6B", "#F5DEB3" };

            for (int i = 0; i < 5; i++)
            {
                var companionId = (Models.CompanionId)i;
                var def = Models.CompanionDefinition.GetById(companionId);
                var progress = App.Companion.GetProgress(companionId);

                // Hide companion card if the active mod doesn't support this avatar set
                if (App.Mods?.IsCompanionSupported(companionId) == false)
                {
                    cards[i].Visibility = Visibility.Collapsed;
                    continue;
                }
                cards[i].Visibility = Visibility.Visible;

                // Update companion name with mod text replacements
                bool isSlutMode = App.Settings?.Current?.SlutModeEnabled ?? false;
                var companionName = def.GetDisplayName(isSlutMode);
                nameTexts[i].Text = App.Mods?.MakeModAware(companionName) ?? companionName;

                // All companions are unlocked from level 1
                levelTexts[i].Text = progress.IsMaxLevel ? "MAX" : $"Lv.{progress.Level}";

                // Highlight active companion with colored border
                var isActive = companionId == activeId;
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colors[i]);
                cards[i].BorderBrush = isActive
                    ? new System.Windows.Media.SolidColorBrush(color)
                    : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Transparent);

                // Companion lock visuals removed — every companion is available from level 1.
                lockTexts[i].Visibility = Visibility.Collapsed;
                cards[i].Opacity = 1.0;
            }

            // Update active companion details
            var activeDef = Models.CompanionDefinition.GetById(activeId);
            var activeProgress = App.Companion.ActiveProgress;

            var activeDisplayName = activeDef.GetDisplayName(App.Settings?.Current?.SlutModeEnabled ?? false);
            TxtActiveCompanionName.Text = App.Mods?.MakeModAware(activeDisplayName) ?? activeDisplayName;
            TxtActiveCompanionLevel.Text = activeProgress.IsMaxLevel ? " · MAX LEVEL" : $" · Level {activeProgress.Level}";
            TxtActiveCompanionDesc.Text = activeDef.Description;
            TxtActiveCompanionXP.Text = activeProgress.IsMaxLevel
                ? "Complete!"
                : $"{activeProgress.CurrentXP:F0} / {activeProgress.XPForNextLevel:F0} XP";

            // Update main progress bar
            PrgCompanion0.Value = activeProgress.LevelProgress * 100;
            PrgCompanion0.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colors[(int)activeId]));

            // Update community prompts UI
            UpdateCommunityPromptsUI();

            // Update companion prompt labels
            UpdateCompanionPromptLabels();

            // Refresh hero avatar GIF (v5.9)
            RefreshHeroAvatar();
        }

        /// <summary>
        /// Loads the active companion's pose-1 portrait into the hero avatar circle.
        /// Uses Stretch="Uniform" so the full figure shows centered inside the gradient ring,
        /// scaled down to fit, instead of being cropped (which broke for avatars whose figure
        /// isn't anchored to the top of the source PNG). Uses the same naming pattern as
        /// AvatarTubeWindow (avatar_pose1.png / avatarN_pose1.png).
        /// </summary>
        private void RefreshHeroAvatar()
        {
            if (HeroAvatarImage == null) return;
            try
            {
                var setNumber = App.Settings?.Current?.SelectedAvatarSet ?? 1;
                if (setNumber < 1)
                {
                    var playerLevel = App.Settings?.Current?.PlayerLevel ?? 1;
                    setNumber = AvatarTubeWindow.GetAvatarSetForLevel(playerLevel);
                }
                var prefix = setNumber == 1 ? "avatar_pose" : $"avatar{setNumber}_pose";
                var resourceName = $"{prefix}1.png";

                var resolved = Services.ModResourceResolver.ResolveImage(resourceName);
                if (resolved != null)
                {
                    HeroAvatarImage.Source = resolved;
                    return;
                }

                var uri = new Uri($"pack://application:,,,/Resources/{resourceName}", UriKind.Absolute);
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = uri;
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                HeroAvatarImage.Source = bitmap;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to load hero avatar pose");
            }
        }

        /// <summary>
        /// Gets the display name for the currently active prompt.
        /// </summary>
        private string GetActivePromptDisplayName()
        {
            var activePromptId = App.Settings?.Current?.ActiveCommunityPromptId;

            if (!string.IsNullOrEmpty(activePromptId))
            {
                var prompt = App.CommunityPrompts?.GetInstalledPrompt(activePromptId);
                return prompt?.Name ?? "Unknown";
            }
            else if (App.Settings?.Current?.CompanionPrompt?.UseCustomPrompt == true)
            {
                return "Custom";
            }
            return "Default";
        }

        /// <summary>
        /// Updates the community prompts section UI.
        /// </summary>
        private void UpdateCommunityPromptsUI()
        {
            var activePromptId = App.Settings?.Current?.ActiveCommunityPromptId;
            var installedIds = App.Settings?.Current?.InstalledCommunityPromptIds ?? new List<string>();

            // Update the Customize button prompt name
            TxtCustomizePromptName.Text = GetActivePromptDisplayName();

            // Update active prompt display
            if (string.IsNullOrEmpty(activePromptId))
            {
                if (App.Settings?.Current?.CompanionPrompt?.UseCustomPrompt == true)
                {
                    TxtActivePromptName.Text = Loc.Get("label_custom_edited");
                }
                else
                {
                    TxtActivePromptName.Text = Loc.Get("label_default_built_in");
                }
                BtnDeactivatePrompt.Visibility = Visibility.Collapsed;
            }
            else
            {
                var prompt = App.CommunityPrompts?.GetInstalledPrompt(activePromptId);
                TxtActivePromptName.Text = prompt != null ? $"{prompt.Name} by {prompt.Author}" : "Custom";
                BtnDeactivatePrompt.Visibility = Visibility.Visible;
            }

            // Update installed prompts list
            InstalledPromptsPanel.Children.Clear();
            if (installedIds.Count == 0)
            {
                InstalledPromptsPanel.Children.Add(new TextBlock
                {
                    Text = "No prompts installed",
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(80, 80, 80)),
                    FontSize = 10,
                    FontStyle = FontStyles.Italic,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }
            else
            {
                foreach (var id in installedIds)
                {
                    var prompt = App.CommunityPrompts?.GetInstalledPrompt(id);
                    if (prompt == null) continue;

                    var isActive = id == activePromptId;
                    var row = CreatePromptRow(prompt, isActive);
                    InstalledPromptsPanel.Children.Add(row);
                }
            }
        }

        private FrameworkElement CreatePromptRow(Models.CommunityPrompt prompt, bool isActive)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Name + Author
            var namePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (isActive)
            {
                namePanel.Children.Add(new TextBlock
                {
                    Text = "● ",
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(147, 112, 219)),
                    FontSize = 10
                });
            }
            namePanel.Children.Add(new TextBlock
            {
                Text = prompt.Name,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
                FontSize = 10,
                FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal
            });
            namePanel.Children.Add(new TextBlock
            {
                Text = $" by {prompt.Author}",
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(96, 96, 96)),
                FontSize = 9
            });
            Grid.SetColumn(namePanel, 0);
            grid.Children.Add(namePanel);

            // Action buttons
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal };

            if (!isActive)
            {
                var activateBtn = new Button
                {
                    Content = "Use",
                    Background = System.Windows.Media.Brushes.Transparent,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(147, 112, 219)),
                    BorderThickness = new Thickness(0),
                    FontSize = 9,
                    Padding = new Thickness(6, 2, 6, 2),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = prompt.Id
                };
                activateBtn.Click += (s, e) =>
                {
                    if (s is Button btn && btn.Tag is string promptId)
                    {
                        // CCBill AI Addendum: gate community prompts that ship a SlutModePersonality
                        // when SlutMode is on. Synthesize a probe preset for the gate check.
                        var probePrompt = App.CommunityPrompts?.GetInstalledPrompt(promptId);
                        var slutModeOn = App.Settings?.Current?.SlutModeEnabled == true;
                        var probe = new Models.PersonalityPreset { PromptSettings = probePrompt?.PromptSettings };
                        if (Services.ExplicitContentGate.RequiresAcknowledgement(probe, slutModeOn))
                        {
                            var prevSettings = App.Settings?.Current?.CompanionPrompt;
                            if (!Services.ExplicitContentGate.IsAlreadyAcknowledged(prevSettings))
                            {
                                var dlg = new ExplicitContentAcknowledgementDialog { Owner = this };
                                if (dlg.ShowDialog() != true) return;
                                if (prevSettings != null)
                                {
                                    Services.ExplicitContentGate.MarkAcknowledged(prevSettings);
                                    App.Settings?.Save();
                                }
                            }
                        }

                        App.CommunityPrompts?.ActivatePrompt(promptId);
                        UpdateCommunityPromptsUI();
                    }
                };
                buttonPanel.Children.Add(activateBtn);
            }

            var removeBtn = new Button
            {
                Content = "×",
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(128, 128, 128)),
                BorderThickness = new Thickness(0),
                FontSize = 12,
                Padding = new Thickness(4, 0, 4, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = prompt.Id,
                ToolTip = "Remove"
            };
            removeBtn.Click += (s, e) =>
            {
                if (s is Button btn && btn.Tag is string promptId)
                {
                    App.CommunityPrompts?.RemovePrompt(promptId);
                    UpdateCommunityPromptsUI();
                }
            };
            buttonPanel.Children.Add(removeBtn);

            Grid.SetColumn(buttonPanel, 1);
            grid.Children.Add(buttonPanel);

            return grid;
        }

        /// <summary>
        /// Handles clicking on a companion card to switch companions.
        /// Also switches the avatar to match the selected companion.
        /// </summary>
        private void CompanionCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // If the click came from the personality button, ignore it (let the button handle it)
            if (e.OriginalSource is FrameworkElement source)
            {
                // Check if clicked element or any of its parents is a personality button
                var parent = source;
                while (parent != null)
                {
                    if (parent is Button btn && btn.Name != null && btn.Name.Contains("Personality"))
                    {
                        App.Logger?.Information("Click originated from personality button, ignoring card click");
                        return; // Don't handle card click, let button handle it
                    }
                    parent = System.Windows.Media.VisualTreeHelper.GetParent(parent) as FrameworkElement;
                }
            }

            if (sender is not FrameworkElement element || element.Tag == null) return;
            if (!int.TryParse(element.Tag.ToString(), out int companionIndex)) return;

            var companionId = (Models.CompanionId)companionIndex;
            var def = Models.CompanionDefinition.GetById(companionId);

            // Switch companion
            if (App.Companion?.SwitchCompanion(companionId) == true)
            {
                UpdateCompanionCardsUI();

                // Also switch the avatar to match the companion
                _avatarTubeWindow?.SwitchToCompanionAvatar(companionId);

                App.Logger?.Information("Switched to companion: {Name}", def.Name);
            }
        }

        /// <summary>
        /// Handles clicking the personality button on a companion card.
        /// Opens a dialog to assign a prompt JSON to this companion.
        /// </summary>
        private void BtnCompanionPersonality_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            App.Logger?.Information("Personality button clicked");
            e.Handled = true; // Prevent card click from also triggering

            if (sender is not FrameworkElement element || element.Tag == null)
            {
                App.Logger?.Warning("Personality button: sender or tag is null");
                return;
            }
            if (!int.TryParse(element.Tag.ToString(), out int companionIndex))
            {
                App.Logger?.Warning("Personality button: failed to parse index");
                return;
            }

            var companionId = (Models.CompanionId)companionIndex;
            var def = Models.CompanionDefinition.GetById(companionId);
            var isUnlocked = App.Companion?.IsCompanionUnlocked(companionId) ?? false;

            App.Logger?.Information("Personality clicked: {Companion}, unlocked: {Unlocked}", def.Name, isUnlocked);

            // Check if companion is unlocked
            if (!isUnlocked)
            {
                App.Logger?.Warning("{Companion} is locked", def.Name);
                ShowStyledDialog(Loc.Get("dialog_locked"), Loc.GetF("msg_companion_locked", def.Name), "OK", "");
                return;
            }

            // Show options: Import JSON, Choose from installed, or Clear
            var currentPromptId = App.Settings?.Current?.GetCompanionPromptId(companionIndex);
            var currentPromptName = Services.CompanionService.GetAssignedPromptName(companionId);
            var hasAssigned = !string.IsNullOrEmpty(currentPromptName);

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = $"Select AI Personality for {def.Name}",
                Filter = "Prompt JSON files (*.json)|*.json",
                DefaultExt = ".json"
            };

            // Check for prompts folder
            var promptsFolder = System.IO.Path.Combine(App.EffectiveAssetsPath, "prompts");
            if (System.IO.Directory.Exists(promptsFolder))
            {
                dialog.InitialDirectory = promptsFolder;
            }

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // Import the prompt file if needed
                    var prompt = App.CommunityPrompts?.ImportFromFile(dialog.FileName);
                    if (prompt != null)
                    {
                        // Assign to companion
                        App.Settings?.Current?.SetCompanionPromptId(companionIndex, prompt.Id);
                        App.Settings?.Save();

                        // Update UI
                        UpdateCompanionPromptLabels();

                        App.Logger?.Information("Assigned prompt '{Prompt}' to companion {Companion}",
                            prompt.Name, def.Name);

                        ShowStyledDialog(Loc.Get("title_personality_assigned"),
                            Loc.GetF("msg_personality_assigned", def.Name, prompt.Name),
                            Loc.Get("btn_ok"), "");
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Failed to assign prompt to companion");
                    ShowStyledDialog(Loc.Get("title_error"), Loc.GetF("msg_failed_to_import_prompt", ex.Message), Loc.Get("btn_ok"), "");
                }
            }
        }

        /// <summary>
        /// Updates the prompt labels on all companion cards.
        /// </summary>
        private void UpdateCompanionPromptLabels()
        {
            var promptTexts = new[] { TxtCompanion0Prompt, TxtCompanion1Prompt, TxtCompanion2Prompt, TxtCompanion3Prompt, TxtCompanion4Prompt };

            for (int i = 0; i < promptTexts.Length; i++)
            {
                var promptName = Services.CompanionService.GetAssignedPromptName((Models.CompanionId)i);
                var displayName = App.Mods?.MakeModAware(promptName ?? "") ?? promptName ?? "";
                promptTexts[i].Text = displayName;
                promptTexts[i].ToolTip = string.IsNullOrEmpty(displayName) ? null : Loc.GetF("tooltip_ai_personality", displayName);
            }
        }

        #region Community Prompts

        private async void BtnRefreshPrompts_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BtnRefreshPrompts.IsEnabled = false;
                BtnRefreshPrompts.Content = "...";
                await App.CommunityPrompts?.GetAvailablePromptsAsync(forceRefresh: true);
                UpdateCommunityPromptsUI();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to refresh prompts: {Error}", ex.Message);
            }
            finally
            {
                BtnRefreshPrompts.IsEnabled = true;
                BtnRefreshPrompts.Content = Loc.Get("btn_refresh");
            }
        }

        private void BtnDeactivatePrompt_Click(object sender, RoutedEventArgs e)
        {
            App.CommunityPrompts?.DeactivatePrompt();
            UpdateCommunityPromptsUI();
        }

        private async void BtnBrowsePrompts_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Fetch available prompts
                var available = await App.CommunityPrompts?.GetAvailablePromptsAsync();
                if (available == null || available.Count == 0)
                {
                    ShowStyledDialog(Loc.Get("title_community_prompts"), Loc.Get("msg_no_community_prompts"), Loc.Get("btn_ok"), "");
                    return;
                }

                // Build selection list
                var installed = App.Settings?.Current?.InstalledCommunityPromptIds ?? new List<string>();
                var notInstalled = available.Where(p => !installed.Contains(p.Id)).ToList();

                if (notInstalled.Count == 0)
                {
                    ShowStyledDialog(Loc.Get("title_community_prompts"), Loc.Get("msg_all_prompts_installed"), Loc.Get("btn_ok"), "");
                    return;
                }

                // Show simple selection (first 5)
                var message = Loc.Get("label_available_prompts");
                for (int i = 0; i < Math.Min(5, notInstalled.Count); i++)
                {
                    var p = notInstalled[i];
                    message += $"• {p.Name} by {p.Author}\n  {p.Description}\n\n";
                }

                if (notInstalled.Count > 5)
                    message += Loc.GetF("label_and_more_prompts", notInstalled.Count - 5);

                message += Loc.Get("label_install_first_one");

                var result = ShowStyledDialog(Loc.Get("title_browse_community_prompts"), message, Loc.Get("btn_install"), Loc.Get("btn_cancel"));
                if (result && notInstalled.Count > 0)
                {
                    var prompt = await App.CommunityPrompts?.InstallPromptAsync(notInstalled[0].Id);
                    if (prompt != null)
                    {
                        ShowStyledDialog(Loc.Get("title_installed"), Loc.GetF("msg_prompt_installed", prompt.Name), Loc.Get("btn_ok"), "");
                        UpdateCommunityPromptsUI();
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Error browsing prompts");
                ShowStyledDialog(Loc.Get("title_error"), Loc.GetF("msg_failed_to_browse_prompts", ex.Message), Loc.Get("btn_ok"), "");
            }
        }

        private void BtnImportPrompt_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    Title = Loc.Get("title_import_community_prompt")
                };

                if (dialog.ShowDialog() == true)
                {
                    var prompt = App.CommunityPrompts?.ImportFromFile(dialog.FileName);
                    if (prompt != null)
                    {
                        ShowStyledDialog(Loc.Get("title_imported"), Loc.GetF("msg_prompt_imported", prompt.Name, prompt.Author), Loc.Get("btn_ok"), "");
                        UpdateCommunityPromptsUI();
                    }
                    else
                    {
                        ShowStyledDialog(Loc.Get("title_error"), Loc.Get("msg_failed_to_import_prompt_invalid"), Loc.Get("btn_ok"), "");
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Error importing prompt");
                ShowStyledDialog(Loc.Get("title_error"), Loc.GetF("msg_failed_to_import_prompt_error", ex.Message), Loc.Get("btn_ok"), "");
            }
        }

        private async void BtnExportPrompt_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Create export dialog with name/author input
                var name = "My Custom Personality";
                var author = App.Patreon?.DisplayName ?? "Anonymous";

                var prompt = App.CommunityPrompts?.ExportCurrentSettings(name, author, "A custom AI personality.");
                if (prompt == null)
                {
                    ShowStyledDialog(Loc.Get("title_error"), Loc.Get("msg_failed_to_export_settings"), Loc.Get("btn_ok"), "");
                    return;
                }

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json",
                    Title = Loc.Get("title_export_community_prompt"),
                    FileName = $"{name.Replace(" ", "_")}.json"
                };

                if (dialog.ShowDialog() == true)
                {
                    await App.CommunityPrompts?.SavePromptToFileAsync(prompt, dialog.FileName);
                    ShowStyledDialog(Loc.Get("title_exported"), Loc.GetF("msg_prompt_exported", dialog.FileName), Loc.Get("btn_ok"), "");
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Error exporting prompt");
                ShowStyledDialog(Loc.Get("title_error"), Loc.GetF("msg_failed_to_export_prompt", ex.Message), Loc.Get("btn_ok"), "");
            }
        }

        #endregion

        #region Patreon Exclusives Tab

        private void UpdatePatreonUI()
        {
            var tier = App.Patreon?.CurrentTier ?? PatreonTier.None;
            var isAuthenticated = App.Patreon?.IsAuthenticated ?? false;
            var isActivePatron = App.Patreon?.IsActivePatron ?? false;

            // Update login status
            if (isAuthenticated)
            {
                var isWhitelisted = App.Patreon?.IsWhitelisted == true;

                // Use unified display name first (what user chose), then fall back to Patreon-specific
                var unifiedDisplayName = App.Settings?.Current?.UserDisplayName;
                var patreonDisplayName = App.Patreon?.DisplayName;

                // Show unified DisplayName if available, otherwise Patreon display name
                var nameToShow = unifiedDisplayName ?? patreonDisplayName;
                TxtPatreonStatus.Text = string.IsNullOrEmpty(nameToShow) ? "Connected to Patreon" : $"Welcome, {nameToShow}!";
                TxtPatreonTier.Text = tier switch
                {
                    PatreonTier.Level2 => Loc.Get("label_patreon_tier_level2"),
                    PatreonTier.Level1 => Loc.Get("label_patreon_tier_level1"),
                    _ when isWhitelisted => Loc.Get("label_patreon_tier_whitelisted"),
                    _ => Loc.Get(isActivePatron ? "label_patreon_tier_patron" : "label_patreon_tier_connected")
                };
                BtnPatreonLogin.Content = Loc.Get("btn_logout");
            }
            else
            {
                // Check if user is logged in with another provider (has unified_id)
                var hasUnifiedId = !string.IsNullOrEmpty(App.Settings?.Current?.UnifiedId);

                TxtPatreonStatus.Text = Loc.Get("label_not_connected");
                TxtPatreonTier.Text = Loc.Get("label_login_to_unlock_exclusive_features");

                // Show "Link Patreon" if logged in via Discord, otherwise "Login"
                BtnPatreonLogin.Content = hasUnifiedId ? "Link Patreon" : "Login";
            }

            // AI Features lock overlay - hide when user is logged in (any provider)
            AiFeaturesLockOverlay.Visibility = App.HasCloudIdentity ? Visibility.Collapsed : Visibility.Visible;

            // Update feature lockboxes
            // All features are now Tier 1 (or whitelisted)
            var hasPremiumAccess = App.Patreon?.HasPremiumAccess == true;
            var level1Unlocked = hasPremiumAccess;
            var level2Unlocked = hasPremiumAccess; // Same as Level 1 now - all features at Tier 1

            // Master overlay for the entire features grid
            PatreonFeaturesOverlay.Visibility = hasPremiumAccess ? Visibility.Collapsed : Visibility.Visible;

            // Keep the patron-achievements section lock + counts in sync with entitlement.
            UpdateAchievementCount();

            // Haptics - unlock for all Patreon supporters
            var hasHapticsAccess = hasPremiumAccess;
            HapticsContentGrid.Opacity = hasHapticsAccess ? 1.0 : 0.3;
            HapticsContentGrid.IsHitTestVisible = hasHapticsAccess;
            HapticsConnectionLock.Visibility = hasHapticsAccess ? Visibility.Collapsed : Visibility.Visible;
            HapticsFeatureLock.Visibility = hasHapticsAccess ? Visibility.Collapsed : Visibility.Visible;
            HapticsConnectionBox.IsEnabled = hasHapticsAccess;
            HapticsFeatureBox.IsEnabled = hasHapticsAccess;

            // Hide "Coming Soon" overlay for Patreon supporters
            HapticsComingSoonOverlay.Visibility = hasHapticsAccess ? Visibility.Collapsed : Visibility.Visible;

            // Bambi Takeover (Autonomy) — visible-but-locked: keep AutonomyUnlocked
            // always visible, AutonomyLocked stays collapsed (legacy element), and the
            // new BambiTakeoverGate translucent overlay handles gating.
            if (AutonomyLocked != null) AutonomyLocked.Visibility = Visibility.Collapsed;
            if (AutonomyUnlocked != null) AutonomyUnlocked.Visibility = Visibility.Visible;
            RefreshPremiumGate(BambiTakeoverGate);
            RefreshPremiumGate(HapticsGate);
            RefreshPremiumGate(RemoteControlGate);
            RefreshPremiumGate(AwarenessGate);
            RefreshPremiumGate(LockdownGate);
            RefreshBecomeASubjectCta();
            // Blink Trainer uses its own gate refresh (also re-resolves stage
            // mode + status state since premium loss/gain flips the resolver
            // short-circuit and may swap demo↔live).
            RefreshBlinkTrainerGate();
            if (BlinkTrainerTab != null)
            {
                RefreshBlinkTrainerStatusRow();
                ApplyBlinkTrainerStageMode(DetermineBlinkTrainerStageMode());
            }

            // Update AI connection status
            if (TxtAiStatus != null)
            {
                if (App.Ai?.IsAvailable == true)
                {
                    TxtAiStatus.Text = $"AI Ready - {App.Ai.DailyRequestsRemaining} requests remaining today";
                }
                else
                {
                    TxtAiStatus.Text = Loc.Get("label_ai_initializing");
                }
            }

            // Re-evaluate keyword triggers access (may have been disabled before Patreon validated)
            var hasKeywordAccess = KeywordTriggerService.HasAccess();
            if (TxtKeywordTriggersLocked != null)
                TxtKeywordTriggersLocked.Visibility = hasKeywordAccess ? Visibility.Collapsed : Visibility.Visible;
            if (BtnKeywordTriggersStartStop != null)
                BtnKeywordTriggersStartStop.IsEnabled = hasKeywordAccess;
            if (ChkScreenOcrEnabled != null)
                ChkScreenOcrEnabled.IsEnabled = hasKeywordAccess;

            // If triggers were enabled in settings but couldn't start earlier (Patreon not validated yet),
            // start them now that access is confirmed
            if (hasKeywordAccess && App.Settings?.Current?.KeywordTriggersEnabled == true)
            {
                App.KeywordTriggers?.Start();
                _keyboardHook?.Start();
                if (App.Settings.Current.ScreenOcrEnabled)
                    App.ScreenOcr?.Start();
            }

            // Update XP bar login state when Patreon auth changes
            UpdateXPBarLoginState();
        }

        // ========================================================================
        // Account sections reparenting (App Info & Data popup)
        // ========================================================================
        // The Patreon login card, Discord login card, AccountLinkingSection,
        // CloudSettingsBackupSection and DataPrivacySection live physically inside
        // PatreonTab's XAML tree (so their x:Name fields resolve for ~64 handler
        // references across this file). When the dashboard's "App Info & Data"
        // popup opens, we temporarily detach these Borders and attach them to the
        // popup's host StackPanel so the user can manage their account/data from
        // the dashboard. When the popup closes we put them back — the same element
        // instances, so all handler refs remain valid.

        private readonly System.Collections.Generic.List<System.Windows.FrameworkElement> _detachedAccountSections = new();

        /// <summary>
        /// Detaches the account/data sections from PatreonTab's content StackPanel
        /// and attaches them to the provided target host (usually the AppInfoFeatureControl's
        /// ExternalSectionsHost). Called when the App Info &amp; Data popup opens.
        /// </summary>
        internal void DetachAccountSectionsInto(System.Windows.Controls.Panel target)
        {
            if (target == null) return;
            if (_detachedAccountSections.Count > 0) return; // already detached

            // Order matters — this is the vertical order they'll appear in the popup.
            var toMove = new System.Windows.FrameworkElement?[]
            {
                PatreonLoginCard,
                DiscordLoginCard,
                AccountLinkingSection,
                CloudSettingsBackupSection,
                DataPrivacySection,
                SupportDevelopmentCard,
            };

            foreach (var fe in toMove)
            {
                if (fe == null) continue;

                // Detach from whichever parent it currently has (defensive:
                // could be PatreonTabContent on first open, or a stale popup
                // host if a previous close didn't clean up).
                if (fe.Parent is System.Windows.Controls.Panel currentParent)
                {
                    currentParent.Children.Remove(fe);
                }
                else if (fe.Parent is System.Windows.Controls.ContentControl cc)
                {
                    cc.Content = null;
                }

                try
                {
                    target.Children.Add(fe);
                    _detachedAccountSections.Add(fe);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "DetachAccountSectionsInto: failed to attach {Name}", fe.Name);
                }
            }
        }

        /// <summary>
        /// Returns the detached account/data sections to PatreonTab so their
        /// x:Name references stay valid and they can be borrowed again next time
        /// the popup opens. Called when the App Info &amp; Data popup closes.
        /// </summary>
        internal void ReattachAccountSections()
        {
            if (_detachedAccountSections.Count == 0 || PatreonTabContent == null) return;

            // Insert right after the header Grid (index 0), preserving the original order.
            int insertAt = 1;
            foreach (var fe in _detachedAccountSections)
            {
                if (fe.Parent is System.Windows.Controls.Panel currentParent)
                    currentParent.Children.Remove(fe);

                if (insertAt > PatreonTabContent.Children.Count)
                    insertAt = PatreonTabContent.Children.Count;
                PatreonTabContent.Children.Insert(insertAt, fe);
                insertAt++;
            }
            _detachedAccountSections.Clear();
        }

        private async void BtnPatreonLogin_Click(object sender, RoutedEventArgs e)
        {
            if (App.Patreon == null) return;

            if (App.Patreon.IsAuthenticated)
            {
                // Logout
                App.ProfileSync?.StopHeartbeat();
                App.Patreon.Logout();
                if (App.Discord?.IsAuthenticated != true)
                {
                    // No provider left — full logout
                    ClearAccountData();
                }
                else
                {
                    // Discord still active — just update Patreon UI
                    App.Patreon.UnifiedUserId = null;
                    UpdatePatreonUI();
                    UpdateBannerWelcomeMessage();
                }
            }
            else
            {
                // Check if user is already logged in with another provider
                var hasUnifiedId = !string.IsNullOrEmpty(App.Settings?.Current?.UnifiedId);

                if (hasUnifiedId)
                {
                    // Link Patreon to existing account
                    BtnPatreonLogin.IsEnabled = false;
                    BtnPatreonLogin.Content = Loc.Get("login_connecting");

                    try
                    {
                        await App.Patreon.StartOAuthFlowAsync();
                        var success = await AccountService.LinkProviderV2Async(this, "patreon");

                        if (success)
                        {
                            UpdateQuickPatreonUI();
                            UpdatePatreonUI();
                            UpdateDiscordUI();
                            UpdateAccountLinkingUI();
                            UpdateBannerWelcomeMessage();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // User cancelled
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Error(ex, "Failed to link Patreon");
                        MessageBox.Show($"Failed to link Patreon account.\n\n{ex.Message}",
                            "Link Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    finally
                    {
                        BtnPatreonLogin.IsEnabled = true;
                        UpdatePatreonUI();
                    }
                }
                else
                {
                    // No account yet - open unified login dialog
                    OpenUnifiedLoginDialog();
                }
            }
        }

        private async void BtnDiscordLogin_Click(object sender, RoutedEventArgs e)
        {
            if (App.Discord == null) return;

            if (App.Discord.IsAuthenticated)
            {
                // Logout
                App.Discord.Logout();
                if (App.Patreon?.IsAuthenticated != true)
                {
                    // No provider left — full logout
                    ClearAccountData();
                }
                else
                {
                    // Patreon still active — just update Discord UI
                    App.Discord.UnifiedUserId = null;
                    UpdateDiscordUI();
                    UpdateBannerWelcomeMessage();
                }
            }
            else
            {
                // Check if user is already logged in with another provider
                var hasUnifiedId = !string.IsNullOrEmpty(App.Settings?.Current?.UnifiedId);

                if (hasUnifiedId)
                {
                    // Link Discord to existing account
                    BtnDiscordLogin.IsEnabled = false;
                    BtnDiscordLogin.Content = Loc.Get("login_connecting");

                    try
                    {
                        await App.Discord.StartOAuthFlowAsync();
                        var success = await AccountService.LinkProviderV2Async(this, "discord");

                        if (success)
                        {
                            UpdateQuickDiscordUI();
                            UpdateDiscordUI();
                            UpdatePatreonUI();
                            UpdateAccountLinkingUI();
                            UpdateBannerWelcomeMessage();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // User cancelled
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Error(ex, "Failed to link Discord");
                        MessageBox.Show($"Failed to link Discord account.\n\n{ex.Message}",
                            "Link Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    finally
                    {
                        BtnDiscordLogin.IsEnabled = true;
                        UpdateDiscordUI();
                    }
                }
                else
                {
                    // No account yet - open unified login dialog
                    OpenUnifiedLoginDialog();
                }
            }
        }

        private void UpdateDiscordUI()
        {
            if (App.Discord?.IsAuthenticated == true)
            {
                // Use unified display name first, then fall back to Discord-specific
                var discordDisplayName = App.Settings?.Current?.UserDisplayName ?? App.Discord.DisplayName;
                TxtDiscordStatus.Text = $"Connected as {discordDisplayName}";
                TxtDiscordInfo.Text = $"@{App.Discord.Username}";
                BtnDiscordLogin.Content = Loc.Get("btn_logout");
            }
            else
            {
                // Check if user is logged in with another provider (has unified_id)
                var hasUnifiedId = !string.IsNullOrEmpty(App.Settings?.Current?.UnifiedId);

                TxtDiscordStatus.Text = Loc.Get("label_not_connected");
                TxtDiscordInfo.Text = Loc.Get("label_link_discord_for_community_features");

                // Show "Link Discord" if logged in via Patreon, otherwise "Login"
                BtnDiscordLogin.Content = hasUnifiedId ? "Link Discord" : "Login";
            }

            // Update XP bar login state when Discord auth changes
            UpdateXPBarLoginState();
        }

        /// <summary>
        /// Updates the visibility of account linking buttons based on current login state
        /// </summary>
        private void UpdateAccountLinkingUI()
        {
            // Only show linking section if user is logged in with a unified account
            var hasUnifiedId = !string.IsNullOrEmpty(App.Settings?.Current?.UnifiedId);
            var hasLinkedPatreon = App.Settings?.Current?.HasLinkedPatreon == true || App.Patreon?.IsAuthenticated == true;
            var hasLinkedDiscord = App.Settings?.Current?.HasLinkedDiscord == true || App.Discord?.IsAuthenticated == true;

            // Show section only if logged in and missing at least one provider
            bool showLinkingSection = hasUnifiedId && (!hasLinkedPatreon || !hasLinkedDiscord);
            AccountLinkingSection.Visibility = showLinkingSection ? Visibility.Visible : Visibility.Collapsed;

            // Show individual buttons for unlinked providers
            BtnLinkPatreon.Visibility = (hasUnifiedId && !hasLinkedPatreon) ? Visibility.Visible : Visibility.Collapsed;
            BtnLinkDiscord.Visibility = (hasUnifiedId && !hasLinkedDiscord) ? Visibility.Visible : Visibility.Collapsed;

            // Show cloud settings backup section if user has a cloud identity
            CloudSettingsBackupSection.Visibility = hasUnifiedId ? Visibility.Visible : Visibility.Collapsed;
            DataPrivacySection.Visibility = hasUnifiedId ? Visibility.Visible : Visibility.Collapsed;
            if (hasUnifiedId)
            {
                _ = UpdateBackupStatus();
            }
        }

        /// <summary>
        /// Link Patreon account to existing unified account
        /// </summary>
        private async void BtnLinkPatreon_Click(object sender, RoutedEventArgs e)
        {
            if (App.Patreon == null) return;

            BtnLinkPatreon.IsEnabled = false;
            BtnLinkPatreon.Content = Loc.Get("login_connecting");

            try
            {
                // Start Patreon OAuth flow
                await App.Patreon.StartOAuthFlowAsync();

                // Link to existing unified account
                var success = await AccountService.LinkProviderV2Async(this, "patreon");

                if (success)
                {
                    UpdateQuickPatreonUI();
                    UpdatePatreonUI();
                    UpdateAccountLinkingUI();
                }
            }
            catch (OperationCanceledException)
            {
                // User cancelled
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to link Patreon");
                MessageBox.Show($"Failed to link Patreon account.\n\n{ex.Message}",
                    "Link Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                BtnLinkPatreon.IsEnabled = true;
                BtnLinkPatreon.Content = Loc.Get("btn_link_patreon");
            }
        }

        /// <summary>
        /// Link Discord account to existing unified account
        /// </summary>
        private async void BtnLinkDiscord_Click(object sender, RoutedEventArgs e)
        {
            if (App.Discord == null) return;

            BtnLinkDiscord.IsEnabled = false;
            BtnLinkDiscord.Content = Loc.Get("login_connecting");

            try
            {
                // Start Discord OAuth flow
                await App.Discord.StartOAuthFlowAsync();

                // Link to existing unified account
                var success = await AccountService.LinkProviderV2Async(this, "discord");

                if (success)
                {
                    UpdateQuickDiscordUI();
                    UpdateDiscordUI();
                    UpdateAccountLinkingUI();
                }
            }
            catch (OperationCanceledException)
            {
                // User cancelled
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to link Discord");
                MessageBox.Show($"Failed to link Discord account.\n\n{ex.Message}",
                    "Link Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                BtnLinkDiscord.IsEnabled = true;
                BtnLinkDiscord.Content = Loc.Get("btn_link_discord");
            }
        }


        private void ChkShareAchievements_Changed(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current != null && sender is CheckBox chk)
            {
                App.Settings.Current.DiscordShareAchievements = chk.IsChecked == true;
            }
        }

        private void ChkShareLevelUps_Changed(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current != null && sender is CheckBox chk)
            {
                App.Settings.Current.DiscordShareLevelUps = chk.IsChecked == true;
            }
        }

        private void ChkShowLevelInPresence_Changed(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current != null && sender is CheckBox chk)
            {
                App.Settings.Current.DiscordShowLevelInPresence = chk.IsChecked == true;
                // Update presence immediately to reflect change
                App.DiscordRpc?.UpdateLevel(App.Settings.Current.PlayerLevel);
            }
        }

        private async void ChkAllowDiscordDm_Changed(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current != null && sender is CheckBox chk)
            {
                var isChecked = chk.IsChecked == true;
                App.Settings.Current.AllowDiscordDm = isChecked;

                // Sync profile tab checkbox
                if (ChkDiscordTabAllowDm != null && ChkDiscordTabAllowDm != chk)
                    ChkDiscordTabAllowDm.IsChecked = isChecked;

                // Sync immediately so the setting takes effect on the leaderboard
                if (App.ProfileSync != null)
                {
                    await App.ProfileSync.SyncProfileAsync();
                }

                // Refresh profile viewer to show/hide DM button
                if (ProfileCardWrapper?.Visibility == Visibility.Visible)
                {
                    // Update the Discord button visibility based on new setting
                    if (BtnProfileDiscord != null)
                    {
                        if (isChecked && !string.IsNullOrEmpty(App.Discord?.UserId))
                        {
                            BtnProfileDiscord.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            BtnProfileDiscord.Visibility = Visibility.Collapsed;
                        }
                    }
                }
            }
        }

        private async void ChkShareProfilePicture_Changed(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current != null && sender is CheckBox chk)
            {
                var isChecked = chk.IsChecked == true;
                App.Settings.Current.ShareProfilePicture = isChecked;

                // Sync profile tab checkbox
                if (ChkDiscordTabSharePfp != null && ChkDiscordTabSharePfp != chk)
                    ChkDiscordTabSharePfp.IsChecked = isChecked;

                // Sync immediately so the setting takes effect
                if (App.ProfileSync != null)
                {
                    await App.ProfileSync.SyncProfileAsync();
                }
            }
        }

        private async void ChkShowOnlineStatus_Changed(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current != null && sender is CheckBox chk)
            {
                var isChecked = chk.IsChecked == true;
                App.Settings.Current.ShowOnlineStatus = isChecked;

                // Sync profile tab checkbox
                if (ChkDiscordTabShowOnline != null && ChkDiscordTabShowOnline != chk)
                    ChkDiscordTabShowOnline.IsChecked = isChecked;

                App.Logger?.Information("Online status visibility changed: {Visible}", isChecked);

                // Sync immediately so the setting takes effect
                if (App.ProfileSync != null)
                {
                    await App.ProfileSync.SyncProfileAsync();
                }
            }
        }

        private void BtnVisitPatreon_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://www.patreon.com/CodeBambi",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open Patreon page");
            }
        }

        private void OnPatreonTierChanged(object? sender, PatreonTier tier)
        {
            Dispatcher.Invoke(() =>
            {
                UpdatePatreonUI();
                UpdateUnlockablesVisibility(App.Settings?.Current?.PlayerLevel ?? 1);
            });
        }

        private void InitializePatreonTab()
        {
            if (_isLoading) return;

            var settings = App.Settings?.Current;
            if (settings == null) return;

            // Subscribe to Patreon tier changes
            if (App.Patreon != null)
            {
                App.Patreon.TierChanged += OnPatreonTierChanged;
            }

            // Initialize companion settings
            ChkAvatarEnabledCompanion.IsChecked = settings.AvatarEnabled;
            ChkMuteAvatarCompanion.IsChecked = settings.AvatarMuted;
            ChkMuteWhispersCompanion.IsChecked = !settings.SubAudioEnabled;
            SliderIdleIntervalCompanion.Value = settings.IdleGiggleIntervalSeconds;
            TxtIdleIntervalCompanion.Text = $"{settings.IdleGiggleIntervalSeconds}s";
            SliderBubbleDurationCompanion.Value = settings.BubbleDurationSeconds;
            TxtBubbleDurationCompanion.Text = $"{(int)settings.BubbleDurationSeconds}s";

            // Awareness Mode settings (free for all users)
            var awarenessAvailable = true;
            ChkAwarenessMode.IsChecked = settings.AwarenessModeEnabled && settings.AwarenessConsentGiven;
            SliderAwarenessCooldown.Value = settings.AwarenessReactionCooldownSeconds;
            TxtAwarenessCooldown.Text = $"{settings.AwarenessReactionCooldownSeconds}s";

            // Show/hide awareness settings panel based on enabled state
            var awarenessEnabled = awarenessAvailable && settings.AwarenessModeEnabled && settings.AwarenessConsentGiven;
            AwarenessSettingsPanel.Visibility = awarenessEnabled ? Visibility.Visible : Visibility.Collapsed;

            // Trigger Mode settings (free for all)
            ChkTriggerModeCompanion.IsChecked = settings.TriggerModeEnabled;
            SliderTriggerIntervalCompanion.Value = settings.TriggerIntervalSeconds;
            TxtTriggerIntervalCompanion.Text = $"{settings.TriggerIntervalSeconds}s";
            TriggerSettingsPanelCompanion.Visibility = settings.TriggerModeEnabled ? Visibility.Visible : Visibility.Collapsed;

            // Restore the Companion accordion open/closed state (sections default to collapsed)
            RestoreCompanionSectionStates();

            // Hide avatar if disabled
            if (!settings.AvatarEnabled)
            {
                HideAvatarTube();
            }

            UpdatePatreonUI();
        }

        private void ChkAvatarEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var checkbox = sender as CheckBox;
            var isEnabled = checkbox?.IsChecked == true;
            App.Settings.Current.AvatarEnabled = isEnabled;

            if (isEnabled)
            {
                ShowAvatarTube();
            }
            else
            {
                HideAvatarTube();
            }

            App.Settings.Save();
        }

        private void BtnDetachCompanion_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("detach_companion"); } catch { }
            if (_avatarTubeWindow == null) return;

            _avatarTubeWindow.ToggleDetached();

            // Update button and status text
            if (_avatarTubeWindow.IsDetached)
            {
                BtnDetachCompanionTab.Content = Loc.Get("btn_attach");
                TxtDetachStatusCompanion.Text = Loc.Get("label_floating_freely_drag_to_reposition");
            }
            else
            {
                BtnDetachCompanionTab.Content = Loc.Get("btn_detach");
                TxtDetachStatusCompanion.Text = Loc.Get("label_anchored_to_window");
            }
        }

        private void BtnCustomizeCompanion_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("customize_companion"); } catch { }
            var dialog = new CompanionPromptEditorDialog
            {
                Owner = this
            };
            dialog.ShowDialog();

            // Refresh UI to reflect any prompt changes
            UpdateCommunityPromptsUI();
        }

        private void BtnResetCompanionMemory_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("reset_memory"); } catch { }
            var confirm = System.Windows.MessageBox.Show(
                this,
                "Wipe the companion's chat memory?\n\nThis clears the AI's conversation history both in memory and on disk, plus the chat log shown in the avatar bubble. " +
                "Useful when she's stuck in an old pattern (e.g. skipping links). She'll start fresh on the next message.\n\nThis can't be undone.",
                "Reset Companion Memory",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                // Cloud provider is stateless, so this only does work for local Ollama users.
                // App.Ai is typed as the IAiService interface; ClearLocalHistory lives on
                // the concrete strategy (which is what's always assigned).
                (App.Ai as Services.AIService.AiServiceStrategy)?.ClearLocalHistory();

                // Drop the on-screen history too (the data store the avatar window binds to).
                _avatarTubeWindow?.ChatHistory.Clear();

                App.Logger?.Information("Companion memory reset by user");

                System.Windows.MessageBox.Show(
                    this,
                    "Done — the companion's memory is clear. Send her a new message and she'll respond with no prior context.",
                    "Reset Companion Memory",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to reset companion memory");
                System.Windows.MessageBox.Show(
                    this,
                    "Couldn't fully reset the companion's memory: " + ex.Message,
                    "Reset Companion Memory",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void BtnManagePhrases_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CompanionPhraseEditorDialog { Owner = this };
            dialog.ShowDialog();
            UpdatePhraseCountDisplay();
        }

        private void UpdatePhraseCountDisplay()
        {
            var count = App.CompanionPhrases?.GetActivePhraseCount() ?? 0;
            TxtPhraseCount.Text = $"{count} active";
        }

        // Persist each Companion accordion's open/collapsed state so it survives a restart.
        // The x:Name is "Section<Name>"; we store under just "<Name>".
        private void CompanionSection_ExpandedChanged(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (sender is not Expander exp || string.IsNullOrEmpty(exp.Name)) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var key = exp.Name.StartsWith("Section") ? exp.Name.Substring("Section".Length) : exp.Name;
            s.CompanionSectionOpen[key] = exp.IsExpanded;
            App.Settings.Save();
        }

        // Re-apply the remembered open/collapsed state. Runs while _isLoading is true so the
        // Expanded/Collapsed handlers above no-op and we don't write back what we just read.
        private void RestoreCompanionSectionStates()
        {
            var map = App.Settings?.Current?.CompanionSectionOpen;
            if (map == null) return;
            if (SectionBehaviour != null && map.TryGetValue("Behaviour", out var b)) SectionBehaviour.IsExpanded = b;
            if (SectionPhrases   != null && map.TryGetValue("Phrases",   out var p)) SectionPhrases.IsExpanded   = p;
            if (SectionContent   != null && map.TryGetValue("Content",   out var c)) SectionContent.IsExpanded   = c;
            if (SectionCommunity != null && map.TryGetValue("Community", out var m)) SectionCommunity.IsExpanded  = m;
        }

        private void SliderIdleInterval_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtIdleIntervalCompanion == null) return;

            var slider = sender as Slider;
            var value = (int)(slider?.Value ?? 120);
            TxtIdleIntervalCompanion.Text = $"{value}s";
            App.Settings.Current.IdleGiggleIntervalSeconds = value;
            App.Settings.Save();
            _avatarTubeWindow?.RestartIdleTimer();
        }

        private void SliderBubbleDuration_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtBubbleDurationCompanion == null) return;

            var slider = sender as Slider;
            var value = slider?.Value ?? 2.0;
            TxtBubbleDurationCompanion.Text = $"{(int)value}s";
            App.Settings.Current.BubbleDurationSeconds = value;
            App.Settings.Save();
        }

        // ============================================================
        // TRIGGER MODE (Free for all)
        // ============================================================

        private void ChkTriggerMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var checkbox = sender as CheckBox;
            var isEnabled = checkbox?.IsChecked == true;
            TriggerSettingsPanelCompanion.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;

            App.Settings.Current.TriggerModeEnabled = isEnabled;
            App.Settings.Save();

            // Restart trigger timer on avatar window
            _avatarTubeWindow?.RestartTriggerTimer();

            App.Logger?.Information("Trigger Mode {State}", isEnabled ? "enabled" : "disabled");
        }

        /// <summary>
        /// Sync the Trigger Mode UI when changed from avatar context menu
        /// </summary>
        public void SyncTriggerModeUI(bool isEnabled)
        {
            ChkTriggerModeCompanion.IsChecked = isEnabled;
            TriggerSettingsPanelCompanion.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SliderTriggerInterval_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtTriggerIntervalCompanion == null) return;

            var slider = sender as Slider;
            var value = (int)(slider?.Value ?? 60);
            TxtTriggerIntervalCompanion.Text = $"{value}s";
            App.Settings.Current.TriggerIntervalSeconds = value;

            // Restart trigger timer with new interval
            _avatarTubeWindow?.RestartTriggerTimer();
        }

        private void BtnEditTriggers_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Convert List<string> to Dictionary<string, bool> for the editor
                // Use Distinct() to handle any duplicate triggers that could crash ToDictionary
                var triggers = App.Settings.Current.CustomTriggers ?? new List<string>();
                var triggerDict = triggers
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(t => t, _ => true);

                // Note: We no longer auto-populate defaults when empty.
                // Users can add triggers manually via the editor if they want them.
                // This fixes the bug where removed triggers would reappear.

                var dialog = new TextEditorDialog("Trigger Phrases", triggerDict);
                dialog.Owner = this;

                if (dialog.ShowDialog() == true && dialog.ResultData != null)
                {
                    // Get only enabled triggers
                    var newTriggers = dialog.ResultData
                        .Where(kvp => kvp.Value)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    App.Settings.Current.CustomTriggers = newTriggers;
                    App.Settings.Save();
                    App.Logger?.Information("Updated {Count} custom triggers", newTriggers.Count);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open trigger editor");
                MessageBox.Show($"Error opening trigger editor: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ChkAwarenessMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = ChkAwarenessMode.IsChecked == true;

            // Show/hide awareness settings panel
            AwarenessSettingsPanel.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;

            // Update settings
            App.Settings.Current.AwarenessModeEnabled = isEnabled;
            App.Settings.Current.AwarenessConsentGiven = isEnabled; // Auto-consent when enabling via UI
            App.Settings.Save();

            // Start or stop the awareness service
            if (isEnabled)
            {
                App.WindowAwareness?.Start();
                App.Logger?.Information("Awareness Mode enabled via UI");
            }
            else
            {
                App.WindowAwareness?.Stop();
                App.Logger?.Information("Awareness Mode disabled via UI");
            }

            UpdateAiBrainPills();
        }

        private void BtnPrivacySpoiler_Click(object sender, RoutedEventArgs e)
        {
            if (TxtPrivacyDetails.Visibility == Visibility.Collapsed)
            {
                TxtPrivacyDetails.Visibility = Visibility.Visible;
                BtnPrivacySpoiler.Content = Loc.Get("btn_hide");
            }
            else
            {
                TxtPrivacyDetails.Visibility = Visibility.Collapsed;
                BtnPrivacySpoiler.Content = Loc.Get("btn_click_to_reveal");
            }
        }

        private void SliderAwarenessCooldown_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtAwarenessCooldown == null) return;

            var value = (int)SliderAwarenessCooldown.Value;
            TxtAwarenessCooldown.Text = $"{value}s";
            App.Settings.Current.AwarenessReactionCooldownSeconds = value;
            App.Settings.Save();
        }

        // ============================================================
        // COMPANION TAB — Hero + AI Brain redesign (v5.9)
        // ============================================================

        private void BtnSwitchCompanion_Click(object sender, RoutedEventArgs e)
        {
            if (CompanionRosterTray == null) return;
            CompanionRosterTray.Visibility = CompanionRosterTray.Visibility == Visibility.Visible
                ? Visibility.Collapsed : Visibility.Visible;
        }

        private void RadioAiOff_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.AiChatEnabled = false;
            App.Settings.Save();
            if (LocalConfigPanel != null) LocalConfigPanel.Visibility = Visibility.Collapsed;
            // Drop any stale Live Actions — only local AI populates this feed.
            App.AiLiveActions?.Clear();
            UpdateAiBrainPills();
        }

        private void RadioAiCloud_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s?.CompanionPrompt == null) return;
            s.AiChatEnabled = true;
            s.CompanionPrompt.UseLocalAi = false;
            App.Settings.Save();
            if (LocalConfigPanel != null) LocalConfigPanel.Visibility = Visibility.Collapsed;
            // Cloud can't trigger effects, so prior local-session entries would be misleading.
            App.AiLiveActions?.Clear();
            UpdateAiBrainPills();
        }

        private void RadioAiLocal_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s?.CompanionPrompt == null) return;
            s.AiChatEnabled = true;
            s.CompanionPrompt.UseLocalAi = true;
            App.Settings.Save();
            if (LocalConfigPanel != null) LocalConfigPanel.Visibility = Visibility.Visible;
            UpdateAiBrainPills();

            // First-time opt-in: if Ollama isn't reachable, offer the setup wizard so
            // the user doesn't have to hunt for the button. Detect runs on a 2s timeout.
            _ = MaybeOfferLocalAiSetupAsync();
        }

        private async Task MaybeOfferLocalAiSetupAsync()
        {
            try
            {
                var model = App.Settings?.Current?.CompanionPrompt?.AiModel;
                var snap = await Services.AIService.OllamaSetupService.DetectAsync(targetModel: model);
                if (snap.Status == Services.AIService.OllamaSetupService.InstallStatus.Ready) return;

                var result = MessageBox.Show(
                    this,
                    Loc.Get("dialog_local_ai_setup_offer_body"),
                    Loc.Get("dialog_local_ai_setup_offer_title"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes) LaunchLocalAiSetupWizard();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "MainWindow: detect-on-local-toggle failed");
            }
        }

        private void BtnSetupLocalAi_Click(object sender, RoutedEventArgs e)
        {
            LaunchLocalAiSetupWizard();
        }

        /// <summary>
        /// Lab tab "AI Companion Effects & Memory" notice button — switches to the
        /// Companion tab so the user can see the AI Brain provider controls, then
        /// launches the setup wizard. Effects need a local LLM (cloud is stateless +
        /// has no command-output capability).
        /// </summary>
        private void BtnLabEffectsSetupLocal_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("companion");
            LaunchLocalAiSetupWizard();
        }

        /// <summary>
        /// Slut Mode toggle: swaps the active personality's Personality text with its
        /// SlutModePersonality variant in BambiSprite.GetSystemPrompt. Takes effect on
        /// the next chat — no restart, no provider switch needed.
        /// </summary>
        private void ChkSlutMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null || ChkSlutMode == null) return;

            var newValue = ChkSlutMode.IsChecked == true;

            // CCBill AI Addendum: flipping SlutMode ON activates the explicit variant of
            // any active preset that ships a SlutModePersonality. Gate behind acknowledgement.
            if (newValue && !s.SlutModeEnabled)
            {
                var activePreset = App.Personality?.GetActivePreset();
                if (Services.ExplicitContentGate.RequiresAcknowledgement(activePreset, slutModeOn: true))
                {
                    if (!Services.ExplicitContentGate.IsAlreadyAcknowledged(s.CompanionPrompt))
                    {
                        var dlg = new ExplicitContentAcknowledgementDialog { Owner = this };
                        var ok = dlg.ShowDialog() == true;
                        if (!ok)
                        {
                            // Revert checkbox without re-triggering this handler.
                            _isLoading = true;
                            try { ChkSlutMode.IsChecked = false; }
                            finally { _isLoading = false; }
                            return;
                        }
                        Services.ExplicitContentGate.MarkAcknowledged(s.CompanionPrompt);
                    }
                }
            }

            s.SlutModeEnabled = newValue;
            App.Settings?.Save();
        }

        private void LaunchLocalAiSetupWizard()
        {
            var wizard = new LocalAiSetupWizard { Owner = this };
            var ok = wizard.ShowDialog() == true;
            if (ok && wizard.LocalAiReady)
            {
                if (TxtAiModel != null) TxtAiModel.Text = wizard.SelectedModel;
                if (RadioAiLocal != null) RadioAiLocal.IsChecked = true;
                UpdateAiBrainPills();
            }
        }

        private void TxtAiModel_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current?.CompanionPrompt;
            if (s == null || TxtAiModel == null) return;
            s.AiModel = (TxtAiModel.Text ?? "").Trim();
            App.Settings.Save();
        }

        private void TxtAiHost_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current?.CompanionPrompt;
            if (s == null || TxtAiHost == null) return;
            s.AiOllamaHost = (TxtAiHost.Text ?? "").Trim();
            App.Settings.Save();
        }

        private async void BtnTestOllamaConnection_Click(object sender, RoutedEventArgs e)
        {
            if (TxtAiHealthStatus == null || TxtAiHost == null) return;

            var host = (TxtAiHost.Text ?? "").Trim();
            if (string.IsNullOrEmpty(host))
            {
                TxtAiHealthStatus.Text = Loc.Get("label_status_failed");
                TxtAiHealthStatus.Foreground = new SolidColorBrush(Colors.Red);
                return;
            }
            var url = host.TrimEnd('/') + "/api/tags";

            TxtAiHealthStatus.Text = Loc.Get("label_status_testing");
            TxtAiHealthStatus.Foreground = new SolidColorBrush(Colors.Gray);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var http = new HttpClient();
                var resp = await http.GetAsync(url, cts.Token);
                sw.Stop();
                if (resp.IsSuccessStatusCode)
                {
                    TxtAiHealthStatus.Text = $"{Loc.Get("label_status_connected")} · {sw.ElapsedMilliseconds}ms";
                    TxtAiHealthStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x50, 0xC8, 0x78));
                }
                else
                {
                    TxtAiHealthStatus.Text = $"{Loc.Get("label_status_failed")} · {(int)resp.StatusCode}";
                    TxtAiHealthStatus.Foreground = new SolidColorBrush(Colors.Red);
                }
            }
            catch (Exception ex)
            {
                TxtAiHealthStatus.Text = $"{Loc.Get("label_status_failed")} · {ex.GetType().Name}";
                TxtAiHealthStatus.Foreground = new SolidColorBrush(Colors.Red);
            }
        }

        /// <summary>
        /// Wipes the local AI's persisted chat history (in-memory + on-disk).
        /// Cloud provider has no memory, so this is a local-only action.
        /// </summary>
        private void BtnClearChatMemory_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                Loc.Get("dialog_forget_everything_prompt"),
                Loc.Get("btn_forget_everything"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                if (App.Ai is Services.AIService.AiServiceStrategy strategy)
                {
                    strategy.ClearLocalHistory();
                }

                // Also clear the live actions feed so the visual state matches "fresh slate".
                App.AiLiveActions.Clear();
                UpdateLiveActionsPlaceholder();

                MessageBox.Show(
                    Loc.Get("dialog_forget_everything_done"),
                    Loc.Get("btn_forget_everything"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BtnClearChatMemory_Click failed");
            }
        }

        private void ChkChatMemoryEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current?.CompanionPrompt;
            if (s == null || ChkChatMemoryEnabled == null) return;
            var on = ChkChatMemoryEnabled.IsChecked == true;
            if (s.ChatMemoryEnabled == on) return;
            s.ChatMemoryEnabled = on;
            App.Settings?.Save();

            // Turning memory off should wipe what's already saved — not just stop persisting new turns.
            if (!on && App.Ai is Services.AIService.AiServiceStrategy strategy)
            {
                try { strategy.ClearLocalHistory(); }
                catch (Exception ex) { App.Logger?.Warning(ex, "ChkChatMemoryEnabled_Changed: ClearLocalHistory failed"); }
            }
        }

        private void ChkCapEffects_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current?.CompanionPrompt;
            if (s == null || ChkCapEffects == null) return;
            var on = ChkCapEffects.IsChecked == true;
            s.AllowAiToControlEffects = on;
            if (EffectPermsPanel != null) EffectPermsPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            App.Settings.Save();
        }

        private void ChkAllowEffect_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (sender is not CheckBox cb) return;
            var s = App.Settings?.Current?.CompanionPrompt;
            if (s == null) return;
            var on = cb.IsChecked == true;
            switch (cb.Tag as string)
            {
                case "Flash":       s.AllowAiFlash = on; break;
                case "Video":       s.AllowAiVideo = on; break;
                case "Audio":       s.AllowAiAudio = on; break;
                case "Bubbles":     s.AllowAiBubbles = on; break;
                case "Subliminal":  s.AllowAiSubliminal = on; break;
                case "Overlay":     s.AllowAiOverlay = on; break;
                case "LockCard":    s.AllowAiLockCard = on; break;
                case "Bounce":      s.AllowAiBounce = on; break;
                case "Haptic":      s.AllowAiHaptic = on; break;
                case "GetBackToMe": s.AllowAiGetBackToMe = on; break;
            }
            App.Settings.Save();
        }

        private void SliderMaxHapticIntensity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current?.CompanionPrompt;
            if (s == null || SliderMaxHapticIntensity == null) return;
            s.MaxAiHapticIntensity = SliderMaxHapticIntensity.Value;
            if (TxtMaxHapticIntensity != null)
                TxtMaxHapticIntensity.Text = $"{(int)(SliderMaxHapticIntensity.Value * 100)}%";
            App.Settings.Save();
        }

        private void UpdateAiBrainPills()
        {
            if (PillAiProvider == null || PillAwareness == null) return;
            var s = App.Settings?.Current;
            if (s?.CompanionPrompt == null) return;
            var aiOn = s.AiChatEnabled;
            var local = s.CompanionPrompt.UseLocalAi;
            PillAiProvider.Text = !aiOn ? Loc.Get("label_ai_status_pill_off")
                                : local ? Loc.Get("label_ai_status_pill_local")
                                        : Loc.Get("label_ai_status_pill_cloud");
            PillAwareness.Text = s.AwarenessModeEnabled
                                ? Loc.Get("label_awareness_pill_on")
                                : Loc.Get("label_awareness_pill_off");

            // Effects only work with local AI (cloud has no command output). Hide the
            // Live Actions feed in the AI Brain panel and show the "needs local" notice
            // in the Lab effects card whenever the user isn't on local AI.
            var localAiActive = aiOn && local;
            if (LiveActionsContainer != null)
                LiveActionsContainer.Visibility = localAiActive ? Visibility.Visible : Visibility.Collapsed;
            if (LabEffectsNeedsLocalNotice != null)
                LabEffectsNeedsLocalNotice.Visibility = localAiActive ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateLiveActionsPlaceholder()
        {
            if (TxtLiveActionsPlaceholder == null) return;
            TxtLiveActionsPlaceholder.Visibility = (App.AiLiveActions?.Count ?? 0) == 0
                ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Populate AI Brain controls from settings. Called from SyncCompanionTabUI.
        /// </summary>
        private void SyncAiBrainUI()
        {
            var s = App.Settings?.Current;
            if (s?.CompanionPrompt == null) return;

            // Provider radios
            var aiOn = s.AiChatEnabled;
            var local = s.CompanionPrompt.UseLocalAi;
            if (RadioAiOff != null)   RadioAiOff.IsChecked   = !aiOn;
            if (RadioAiCloud != null) RadioAiCloud.IsChecked = aiOn && !local;
            if (RadioAiLocal != null) RadioAiLocal.IsChecked = aiOn && local;
            if (LocalConfigPanel != null)
                LocalConfigPanel.Visibility = (aiOn && local) ? Visibility.Visible : Visibility.Collapsed;

            // Local config fields
            if (TxtAiModel != null) TxtAiModel.Text = s.CompanionPrompt.AiModel ?? "";
            if (TxtAiHost != null)  TxtAiHost.Text  = s.CompanionPrompt.AiOllamaHost ?? "";

            // Capability checkboxes (ChkAwarenessMode handled by its own sync path; AiChatEnabled is driven solely by the provider radios)
            if (ChkCapEffects != null)
                ChkCapEffects.IsChecked = s.CompanionPrompt.AllowAiToControlEffects;
            if (EffectPermsPanel != null)
                EffectPermsPanel.Visibility = s.CompanionPrompt.AllowAiToControlEffects
                    ? Visibility.Visible : Visibility.Collapsed;

            // Effect permission grid
            if (ChkAllowFlash != null)       ChkAllowFlash.IsChecked       = s.CompanionPrompt.AllowAiFlash;
            if (ChkAllowVideo != null)       ChkAllowVideo.IsChecked       = s.CompanionPrompt.AllowAiVideo;
            if (ChkAllowAudio != null)       ChkAllowAudio.IsChecked       = s.CompanionPrompt.AllowAiAudio;
            if (ChkAllowBubbles != null)     ChkAllowBubbles.IsChecked     = s.CompanionPrompt.AllowAiBubbles;
            if (ChkAllowSubliminal != null)  ChkAllowSubliminal.IsChecked  = s.CompanionPrompt.AllowAiSubliminal;
            if (ChkAllowOverlay != null)     ChkAllowOverlay.IsChecked     = s.CompanionPrompt.AllowAiOverlay;
            if (ChkAllowLockCard != null)    ChkAllowLockCard.IsChecked    = s.CompanionPrompt.AllowAiLockCard;
            if (ChkAllowBounce != null)      ChkAllowBounce.IsChecked      = s.CompanionPrompt.AllowAiBounce;
            if (ChkAllowHaptic != null)      ChkAllowHaptic.IsChecked      = s.CompanionPrompt.AllowAiHaptic;
            if (ChkAllowGetBackToMe != null) ChkAllowGetBackToMe.IsChecked = s.CompanionPrompt.AllowAiGetBackToMe;

            // Max haptic intensity
            if (SliderMaxHapticIntensity != null) SliderMaxHapticIntensity.Value = s.CompanionPrompt.MaxAiHapticIntensity;
            if (TxtMaxHapticIntensity != null)    TxtMaxHapticIntensity.Text    = $"{(int)(s.CompanionPrompt.MaxAiHapticIntensity * 100)}%";

            // Chat memory toggle
            if (ChkChatMemoryEnabled != null) ChkChatMemoryEnabled.IsChecked = s.CompanionPrompt.ChatMemoryEnabled;

            // Awareness panel visibility (from previous handler logic)
            if (AwarenessSettingsPanel != null)
                AwarenessSettingsPanel.Visibility = s.AwarenessModeEnabled ? Visibility.Visible : Visibility.Collapsed;

            // Hero pills
            UpdateAiBrainPills();

            // Slut Mode toggle (no Patreon gate — available to all)
            if (ChkSlutMode != null) ChkSlutMode.IsChecked = s.SlutModeEnabled;

            // Live actions placeholder + ItemsSource binding
            if (LiveActionsList != null && LiveActionsList.ItemsSource == null)
            {
                LiveActionsList.ItemsSource = App.AiLiveActions;
                // Auto-toggle the placeholder when entries arrive (added by AiCommandService).
                App.AiLiveActions.CollectionChanged += (_, _) =>
                {
                    if (Dispatcher.CheckAccess()) UpdateLiveActionsPlaceholder();
                    else Dispatcher.BeginInvoke(new Action(UpdateLiveActionsPlaceholder));
                };
            }
            UpdateLiveActionsPlaceholder();
        }




        #region Remote Control Handlers

        private async void ChkRemoteControlEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var isEnabled = ChkRemoteControlEnabled.IsChecked ?? false;

            if (isEnabled)
            {
                // Must have a cloud identity (unified ID from Patreon or Discord login)
                if (string.IsNullOrEmpty(App.UnifiedUserId))
                {
                    _isLoading = true;
                    ChkRemoteControlEnabled.IsChecked = false;
                    _isLoading = false;
                    ShowStyledDialog(Loc.Get("title_login_required"), Loc.Get("msg_login_required_remote"), Loc.Get("btn_ok"), "");
                    return;
                }

                var tier = GetSelectedRemoteTier();

                // Show consent waiver
                if (!ShowRemoteControlWaiver(tier))
                {
                    // Defer revert so it runs after the dialog's event stack fully unwinds,
                    // preventing WPF toggle animation from getting stuck in the ON position.
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _isLoading = true;
                        ChkRemoteControlEnabled.IsChecked = false;
                        _isLoading = false;
                    }));
                    return;
                }

                // Start session
                RemoteControlPanel.Visibility = System.Windows.Visibility.Visible;
                var code = await App.RemoteControl.StartSessionAsync(tier);
                if (code == null)
                {
                    _isLoading = true;
                    ChkRemoteControlEnabled.IsChecked = false;
                    _isLoading = false;
                    RemoteControlPanel.Visibility = System.Windows.Visibility.Collapsed;
                    ShowStyledDialog(Loc.Get("title_connection_error"), Loc.Get("msg_remote_connection_error"), Loc.Get("btn_ok"), "");
                    return;
                }

                TxtRemoteCode.Text = string.Join(" ", code.ToCharArray());
                var pin = App.RemoteControl?.ConnectPin;
                TxtRemotePin.Text = !string.IsNullOrEmpty(pin) ? $"PIN: {pin}" : "";
                TxtRemotePin.Visibility = !string.IsNullOrEmpty(pin)
                    ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
                RemoteLinkPanel.Visibility = System.Windows.Visibility.Visible;
                RemoteCodePanel.Visibility = System.Windows.Visibility.Visible;
                RemoteStatusPanel.Visibility = System.Windows.Visibility.Visible;
                BtnStopRemote.Visibility = System.Windows.Visibility.Visible;
                UpdateRemoteStatus(false);
                RefreshRemoteQrCode(BuildRemotePairingUrl(code));

                // SP5 layer 3 / community feedback: instead of hiding the opt-in
                // ("Show Me") section once a session is active, keep it visible but
                // grey it out + disable it. This gives the user a clear "you're
                // listed" confirmation rather than the controls silently vanishing.
                // Then chain the directory opt-in call if the user ticked the
                // checkbox. Best-effort — the session is already running.
                if (OptInSectionPanel != null)
                {
                    OptInSectionPanel.IsEnabled = false;
                    OptInSectionPanel.Opacity = 0.5;
                }
                UpdateDirectoryListingStatus();
                _ = RunOptInChainAsync();

                // Listen for controller connection changes
                App.RemoteControl.ControllerConnectedChanged += OnRemoteControllerChanged;
                App.RemoteControl.ControllerIdleChanged += OnRemoteControllerIdleChanged;
                App.RemoteControl.CommandReceived += OnRemoteCommandReceived;
                App.RemoteControl.SessionEnded += OnRemoteSessionEnded;

                // Wire up session callbacks for remote status
                WireRemoteSessionCallbacks();
            }
            else
            {
                await StopRemoteControl();
            }
        }

        private string GetSelectedRemoteTier()
        {
            return (CmbRemoteTier.SelectedIndex) switch
            {
                0 => "light",
                1 => "standard",
                2 => "full",
                _ => "light"
            };
        }

        private bool ShowRemoteControlWaiver(string tier)
        {
            var actions = new System.Text.StringBuilder();
            actions.AppendLine("  - Trigger flash images (from YOUR image folder)");
            actions.AppendLine("  - Trigger subliminal messages (from YOUR subliminal pool)");
            actions.AppendLine("  - Toggle overlays (pink filter, spiral)");
            actions.AppendLine("  - Start/stop bubbles");

            if (tier is "standard" or "full")
            {
                actions.AppendLine("  - Trigger mandatory videos (from YOUR video folder)");
                actions.AppendLine("  - Trigger haptic device patterns");
                actions.AppendLine("  - Duck/unduck audio");
            }

            if (tier == "full")
            {
                actions.AppendLine("  - Start/stop autonomy mode");
                actions.AppendLine("  - Start/pause/stop sessions");
                actions.AppendLine("  - Enable strict lock (videos cannot be skipped)");
                actions.AppendLine("  - Disable panic button (ESC key won't work)");
            }

            var message = $"You are about to allow another person to remotely control parts of your app.\n\n" +
                          $"The Controller will be able to:\n{actions}\n" +
                          $"All media content shown comes from YOUR local files and settings.\n" +
                          $"You assume full responsibility for this interaction.\n" +
                          $"You can stop the session at ANY time by clicking \"Stop Session\" or closing the app.\n" +
                          $"The session stays active as long as the app is running. If the app closes without stopping the session, it expires within 4 hours.";

            var confirmed = WarningDialog.ShowDoubleWarning(this,
                "Remote Control",
                message);

            return confirmed;
        }

        private async void CmbRemoteTier_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            if (!App.RemoteControl?.IsActive == true) return;

            // If session is active and tier changed, restart with new tier
            var newTier = GetSelectedRemoteTier();
            if (newTier != App.RemoteControl.Tier)
            {
                if (!ShowRemoteControlWaiver(newTier))
                    return;

                // Unsubscribe before stopping so OnRemoteSessionEnded doesn't collapse the panel
                App.RemoteControl.ControllerConnectedChanged -= OnRemoteControllerChanged;
                App.RemoteControl.CommandReceived -= OnRemoteCommandReceived;
                App.RemoteControl.SessionEnded -= OnRemoteSessionEnded;

                await App.RemoteControl.StopSessionAsync();
                var code = await App.RemoteControl.StartSessionAsync(newTier);
                if (code != null)
                {
                    TxtRemoteCode.Text = string.Join(" ", code.ToCharArray());
                    var reconnectPin = App.RemoteControl?.ConnectPin;
                    TxtRemotePin.Text = !string.IsNullOrEmpty(reconnectPin) ? $"PIN: {reconnectPin}" : "";
                    TxtRemotePin.Visibility = !string.IsNullOrEmpty(reconnectPin)
                        ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
                    UpdateRemoteStatus(false);
                }

                // Re-subscribe after restart
                App.RemoteControl.ControllerConnectedChanged += OnRemoteControllerChanged;
                App.RemoteControl.ControllerIdleChanged += OnRemoteControllerIdleChanged;
                App.RemoteControl.CommandReceived += OnRemoteCommandReceived;
                App.RemoteControl.SessionEnded += OnRemoteSessionEnded;
            }
        }

        private void BtnCopyRemoteCode_Click(object sender, RoutedEventArgs e)
        {
            var code = App.RemoteControl?.SessionCode;
            if (!string.IsNullOrEmpty(code))
            {
                try
                {
                    var pin = App.RemoteControl?.ConnectPin;
                    var copyText = !string.IsNullOrEmpty(pin) ? $"{code} (PIN: {pin})" : code;
                    System.Windows.Clipboard.SetText(copyText);
                    BtnCopyRemoteCode.Content = Loc.Get("btn_copied");
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Failed to copy remote code to clipboard");
                    BtnCopyRemoteCode.Content = Loc.Get("label_failed");
                }
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (s, _) => { BtnCopyRemoteCode.Content = Loc.Get("btn_copy"); timer.Stop(); };
                timer.Start();
            }
        }

        private void BtnCopyRemoteLink_Click(object sender, RoutedEventArgs e)
        {
            var code = App.RemoteControl?.SessionCode;
            var url = !string.IsNullOrEmpty(code)
                ? BuildRemotePairingUrl(code)
                : "https://cclabs.app/remote/";
            try
            {
                System.Windows.Clipboard.SetText(url);
                BtnCopyRemoteLink.Content = Loc.Get("btn_copied");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to copy remote link to clipboard");
                BtnCopyRemoteLink.Content = Loc.Get("label_failed");
            }
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (s, _) => { BtnCopyRemoteLink.Content = Loc.Get("btn_copy_link"); timer.Stop(); };
            timer.Start();
        }

        private async void BtnStopRemote_Click(object sender, RoutedEventArgs e)
        {
            await StopRemoteControl();
            _isLoading = true;
            ChkRemoteControlEnabled.IsChecked = false;
            _isLoading = false;
        }

        private void ChkStopEffectsOnRemoteDisconnect_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (App.Settings?.Current == null) return;
            App.Settings.Current.StopEffectsOnRemoteDisconnect = ChkStopEffectsOnRemoteDisconnect.IsChecked ?? false;
            App.Settings.Save();
        }

        // Privacy toggle: share linked avatar with controllers. Persists to settings,
        // and if a remote session is currently active pushes status immediately so the
        // controller's pinned strip flips within their next poll (~3s) rather than
        // waiting up to ~15s for the next scheduled status push.
        private async void ChkRemoteShareAvatar_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (App.Settings?.Current == null) return;
            App.Settings.Current.RemoteShareAvatar = ChkRemoteShareAvatar.IsChecked ?? false;
            App.Settings.Save();
            if (App.RemoteControl?.IsActive == true)
            {
                try { await App.RemoteControl.PushStatusNowAsync(); }
                catch (Exception ex) { App.Logger?.Warning(ex, "[RemoteShareAvatar] immediate status push failed"); }
            }
        }

        // Emote picker — preset click, custom send, edit popup. The preset list
        // is bound to App.Settings.Current.RemoteEmotePresets in LoadSettings().
        // Watermark on TxtEmoteCustom is driven by EmoteHelper.LastSentEmoteHint
        // (session-only state, reset on app restart) with EmoteHelper.PlaceholderText
        // as the localized fallback.
        private Models.EmotePreset? _editingPreset;

        private async void BtnEmotePreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not Models.EmotePreset preset) return;
            if (string.IsNullOrWhiteSpace(preset.Text)) return; // can't send a label-less slot
            await SendEmoteAndReportAsync(preset.Text, preset.Icon ?? "", "preset", TxtEmoteStatus);
        }

        private async void BtnEmoteCustomSend_Click(object sender, RoutedEventArgs e)
        {
            await SendCustomEmoteAsync(TxtEmoteCustom, TxtEmoteStatus);
        }

        private async void TxtEmoteCustom_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await SendCustomEmoteAsync(TxtEmoteCustom, TxtEmoteStatus);
            }
        }

        // Splash-overlay (big) picker. Shares the same source list, same service,
        // same debounce (per-RemoteControlService instance), different UI targets.
        private async void BtnEmotePresetBig_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not Models.EmotePreset preset) return;
            if (string.IsNullOrWhiteSpace(preset.Text)) return;
            await SendEmoteAndReportAsync(preset.Text, preset.Icon ?? "", "preset", TxtEmoteStatusBig);
        }

        private async void BtnEmoteCustomSendBig_Click(object sender, RoutedEventArgs e)
        {
            await SendCustomEmoteAsync(TxtEmoteCustomBig, TxtEmoteStatusBig);
        }

        private async void TxtEmoteCustomBig_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await SendCustomEmoteAsync(TxtEmoteCustomBig, TxtEmoteStatusBig);
            }
        }

        private async Task SendCustomEmoteAsync(TextBox? textbox, TextBlock? statusTarget)
        {
            var raw = textbox?.Text ?? "";
            var trimmed = raw.Trim();
            if (trimmed.Length == 0) return; // whitespace-only / empty: silent no-op
            var sent = await SendEmoteAndReportAsync(trimmed, "", "custom", statusTarget);
            if (sent && textbox != null)
            {
                // Clear the box; the watermark switches to the ghost-of-last-sent
                // on THIS textbox only — the small and big custom boxes maintain
                // independent ghost state because each has its own attached prop.
                textbox.Text = "";
                Helpers.EmoteHelper.SetLastSentEmoteHint(textbox, trimmed);
            }
        }

        // Returns true on successful send (so the custom path knows to clear the box).
        // Internal so the avatar context-menu surface (AvatarTubeWindow.MenuItemEmote_Click)
        // can route through the same centralized helper for step 3.6 speech-bubble feedback.
        internal async Task<bool> SendEmoteAndReportAsync(string text, string icon, string kind, TextBlock? statusTarget)
        {
            if (App.RemoteControl == null) return false;

            // Step 3.6: flash the avatar speech bubble before the await so the user gets
            // instant feedback regardless of which surface they fired from. Skip when the
            // call would immediately bounce (no active session or still in debounce window)
            // to avoid a "Sending..." flicker that would never resolve to "Sent: ...".
            var willActuallySend = App.RemoteControl.IsActive && !App.RemoteControl.IsWithinDebounceWindow;
            if (willActuallySend)
            {
                App.AvatarWindow?.ShowEmoteFeedback(text, isPending: true);
            }

            var (ok, error, retry) = await App.RemoteControl.SendEmoteAsync(text, icon, kind);

            if (ok)
            {
                // Update the bubble to "Sent: ..." once the server confirms 200.
                App.AvatarWindow?.ShowEmoteFeedback(text, isPending: false);
                if (statusTarget != null)
                {
                    statusTarget.Foreground = System.Windows.Media.Brushes.LightGreen;
                    statusTarget.Text = Localization.Loc.Get("status_emote_sent");
                }
                return true;
            }
            if (error == "debounced")
            {
                // Silent — debounce is a UX guard, not a user-facing condition.
                return false;
            }
            if (statusTarget != null)
            {
                statusTarget.Foreground = System.Windows.Media.Brushes.Salmon;
                if (error == "rate_limited" && retry.HasValue)
                {
                    statusTarget.Text = Localization.Loc.GetF("status_emote_rate_limited", retry.Value);
                }
                else if (error == "session not active")
                {
                    statusTarget.Text = Localization.Loc.Get("status_emote_no_session");
                }
                else
                {
                    statusTarget.Text = Localization.Loc.Get("status_emote_failed");
                }
            }
            return false;
        }

        private void BtnEmoteEdit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not Models.EmotePreset preset) return;
            _editingPreset = preset;
            if (TxtEditEmoteIcon != null) TxtEditEmoteIcon.Text = preset.Icon ?? "";
            if (TxtEditEmoteText != null) TxtEditEmoteText.Text = preset.Text ?? "";
            if (BtnEditEmoteSave != null) BtnEditEmoteSave.IsEnabled = !string.IsNullOrWhiteSpace(preset.Text);
            if (EmoteEditPopup != null)
            {
                EmoteEditPopup.PlacementTarget = btn;
                EmoteEditPopup.IsOpen = true;
            }
            TxtEditEmoteText?.Focus();
        }

        private void TxtEditEmoteText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (BtnEditEmoteSave == null || TxtEditEmoteText == null) return;
            BtnEditEmoteSave.IsEnabled = !string.IsNullOrWhiteSpace(TxtEditEmoteText.Text);
        }

        private void BtnEditEmoteSave_Click(object sender, RoutedEventArgs e)
        {
            if (_editingPreset == null) return;
            var newText = (TxtEditEmoteText?.Text ?? "").Trim();
            if (newText.Length == 0) return; // defense in depth — Save button should already be disabled
            _editingPreset.Icon = (TxtEditEmoteIcon?.Text ?? "");
            _editingPreset.Text = newText;
            App.Settings?.Save();
            if (EmoteEditPopup != null) EmoteEditPopup.IsOpen = false;
            _editingPreset = null;
        }

        private void BtnEditEmoteCancel_Click(object sender, RoutedEventArgs e)
        {
            if (EmoteEditPopup != null) EmoteEditPopup.IsOpen = false;
            _editingPreset = null;
        }

        // =====================================================================
        // SP5 layer 3 — Available Subjects tab (controller side)
        // =====================================================================

        private bool _availableSubjectsBound;

        private void BtnAvailableSubjects_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("availablesubjects");
        }

        private void BtnBecomeASubject_Click(object sender, RoutedEventArgs e)
        {
            // Premium → take them straight to the Remote Control tab so they
            // can opt into the directory. Free → open the Patreon page.
            if (App.Patreon?.HasPremiumAccess == true)
            {
                ShowTab("remotecontrol");
                return;
            }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://www.patreon.com/CodeBambi",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Subjects] failed to open Patreon URL");
            }
        }

        /// <summary>
        /// Shows the italic "support the project" subtitle only to free users.
        /// Premium users see just the button (which opens the Remote Control tab).
        /// </summary>
        private void RefreshBecomeASubjectCta()
        {
            if (TxtBecomeASubjectSubtitle == null) return;
            var hasPremium = App.Patreon?.HasPremiumAccess == true;
            TxtBecomeASubjectSubtitle.Visibility = hasPremium ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>
        /// One-time binding: hook the service's ObservableCollection to the
        /// ItemsControl ItemsSource and the IsEmpty/HasError flags to the
        /// empty/error panels. Called from ShowTab on first navigation.
        /// </summary>
        private void EnsureAvailableSubjectsBound()
        {
            if (_availableSubjectsBound) return;
            if (App.AvailableSubjects == null) return;
            if (AvailableSubjectsList == null) return;

            AvailableSubjectsList.ItemsSource = App.AvailableSubjects.Entries;
            App.AvailableSubjects.PropertyChanged += OnAvailableSubjectsServicePropertyChanged;
            UpdateAvailableSubjectsEmptyAndError();
            RefreshBecomeASubjectCta();
            _availableSubjectsBound = true;
        }

        private void OnAvailableSubjectsServicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // The service raises these from a background task — marshal to UI.
            Dispatcher.Invoke(UpdateAvailableSubjectsEmptyAndError);
        }

        private void AvailableSubjectsScroller_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (sender is not System.Windows.Controls.ScrollViewer sv) return;
            sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
            e.Handled = true;
        }

        private void UpdateAvailableSubjectsEmptyAndError()
        {
            var svc = App.AvailableSubjects;
            if (svc == null) return;
            // Show error panel if last refresh failed; show empty panel if
            // last refresh was clean but the roster is empty. Otherwise both
            // hidden (cards visible).
            if (AvailableSubjectsErrorPanel != null)
                AvailableSubjectsErrorPanel.Visibility = svc.HasError
                    ? Visibility.Visible : Visibility.Collapsed;
            if (AvailableSubjectsEmptyPanel != null)
                AvailableSubjectsEmptyPanel.Visibility = (!svc.HasError && svc.IsEmpty)
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Connect button on a subject card. Reads the entry from the button's
        /// DataContext, calls the service to claim, and on success opens the
        /// returned session_url in the user's default browser via Process.Start.
        ///
        /// Privacy: the session_url string lives in this method's stack only —
        /// referenced once for Process.Start, never logged, never assigned to
        /// any field. The hash fragment carries the PIN; the cclabs.app/remote/
        /// page strips it from the URL after parsing.
        ///
        /// 409 → service handles silently (re-fetches, card flips to TAKEN).
        /// other failures → no toast in v1; user can re-click. Audit-log
        /// coverage for the failure mode is filed as the SP6 followup.
        /// </summary>
        private async void BtnConnectSubject_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;
            if (btn.DataContext is not ConditioningControlPanel.Services.DirectoryEntry entry) return;
            if (entry.Claimed) return; // belt-and-braces; IsEnabled binding already guards
            if (App.AvailableSubjects == null) return;

            btn.IsEnabled = false;
            try
            {
                var url = await App.AvailableSubjects.TryClaimAsync(entry.UnifiedId);
                if (string.IsNullOrEmpty(url)) return; // 409 handled silently or transient error

                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
                    {
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    // Don't echo the URL into the log line — exception message
                    // typically only carries the OS error code anyway.
                    App.Logger?.Warning(ex, "[AvailableSubjects] failed to open browser for claimed session");
                }
            }
            finally
            {
                // Restore the button — IsEnabled binding will recompute on the
                // next refresh based on entry.Claimed.
                btn.IsEnabled = entry.IsConnectEnabled;
            }
        }

        // =====================================================================
        // SP5 layer 3 — Available Subjects directory opt-in
        // =====================================================================
        // The opt-in checkbox itself NEVER persists across sessions. The user
        // re-opts every time. Tags + status_text persist to AppSettings only
        // when ChkRememberOptInDetails is ticked at the moment of session
        // start. The chained opt-in API call happens after StartSessionAsync
        // returns a code; failure is best-effort (logged + non-blocking inline
        // status), the session itself is unaffected.

        // Per locked decisions: 10 fixed tags, cap selection at 5.
        private const int OptInMaxTags = 5;

        private System.Windows.Controls.CheckBox[] OptInTagCheckBoxes() => new[]
        {
            ChkTagBimbo, ChkTagDrone, ChkTagTrance, ChkTagFeminization,
            ChkTagSubmission, ChkTagDegradation, ChkTagAudioOk, ChkTagSoftOnly,
            ChkTagLockdownOk, ChkTagChastity
        };

        private void ChkOptIntoDirectory_Changed(object sender, RoutedEventArgs e)
        {
            if (OptInFormPanel == null) return;
            var checkedNow = ChkOptIntoDirectory?.IsChecked == true;
            OptInFormPanel.Visibility = checkedNow
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

            // First time user opens the form this session → pre-populate from
            // saved settings (only if Remember was previously ticked).
            if (checkedNow) PopulateOptInFormFromSavedSettings();
        }

        private void PopulateOptInFormFromSavedSettings()
        {
            var s = App.Settings?.Current;
            if (s == null) return;

            // Saved tags → check matching boxes.
            var saved = new HashSet<string>(s.SavedDirectoryTags ?? new List<string>());
            foreach (var cb in OptInTagCheckBoxes())
            {
                var tag = cb.Tag as string ?? "";
                cb.IsChecked = saved.Contains(tag);
            }

            // Saved status text + char count.
            if (TxtOptInStatus != null) TxtOptInStatus.Text = s.SavedDirectoryStatusText ?? "";
            UpdateOptInStatusCharCount();

            // Remember toggle reflects saved preference.
            if (ChkRememberOptInDetails != null)
                ChkRememberOptInDetails.IsChecked = s.RememberDirectoryDetails;
        }

        private void TxtOptInStatus_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdateOptInStatusCharCount();
        }

        private void UpdateOptInStatusCharCount()
        {
            if (TxtOptInStatusCount == null || TxtOptInStatus == null) return;
            var len = (TxtOptInStatus.Text ?? "").Length;
            TxtOptInStatusCount.Text = $"{len}/80";
        }

        private void ChkOptInTag_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.CheckBox cb) return;
            if (cb.IsChecked != true) return; // unchecking is always fine; only cap on check

            var checkedCount = OptInTagCheckBoxes().Count(c => c.IsChecked == true);
            if (checkedCount > OptInMaxTags)
            {
                // Soft cap: undo the just-clicked check and surface a brief inline
                // hint via the feedback TextBlock.
                cb.IsChecked = false;
                ShowOptInFeedback(Loc.Get("msg_optin_directory_max_tags"), persistMs: 2500);
            }
        }

        private List<string> GetSelectedDirectoryTags()
        {
            var list = new List<string>();
            foreach (var cb in OptInTagCheckBoxes())
            {
                if (cb.IsChecked == true && cb.Tag is string tag && !string.IsNullOrEmpty(tag))
                    list.Add(tag);
            }
            return list;
        }

        private System.Windows.Threading.DispatcherTimer? _optInFeedbackTimer;
        private void ShowOptInFeedback(string message, int persistMs)
        {
            if (TxtOptInFeedback == null) return;
            TxtOptInFeedback.Text = message;
            TxtOptInFeedback.Visibility = System.Windows.Visibility.Visible;
            _optInFeedbackTimer?.Stop();
            _optInFeedbackTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(persistMs)
            };
            _optInFeedbackTimer.Tick += (_, _) =>
            {
                _optInFeedbackTimer?.Stop();
                if (TxtOptInFeedback != null)
                {
                    TxtOptInFeedback.Text = "";
                    TxtOptInFeedback.Visibility = System.Windows.Visibility.Collapsed;
                }
            };
            _optInFeedbackTimer.Start();
        }

        /// <summary>
        /// Runs after a successful StartSessionAsync. If the user opted in,
        /// chains the proxy /v2/directory/opt-in call and persists the
        /// tags+status on success when "Remember" is ticked. All best-effort:
        /// the session is already running and is not affected by failures here.
        /// </summary>
        private async Task RunOptInChainAsync()
        {
            if (App.RemoteControl == null) return;
            if (ChkOptIntoDirectory?.IsChecked != true) return;

            var tags = GetSelectedDirectoryTags();
            var statusText = TxtOptInStatus?.Text ?? "";
            // Defensive: cap to OptInMaxTags + 80c even if UI somehow let more
            // through (paste, accessibility, etc.). The proxy validates again,
            // but failing client-side here is faster + clearer.
            if (tags.Count > OptInMaxTags) tags = tags.Take(OptInMaxTags).ToList();
            if (statusText.Length > 80) statusText = statusText.Substring(0, 80);

            var ok = await App.RemoteControl.OptInToDirectoryAsync(tags, statusText);
            if (!ok)
            {
                ShowOptInFeedback(Loc.Get("msg_optin_directory_failed"), persistMs: 4000);
                return;
            }

            // Listed successfully → confirm it in the Session Code section and
            // the header status pill so the user doesn't have to tab to Subjects.
            _directoryOptedIn = true;
            UpdateDirectoryListingStatus();

            // Persist tags+status only on success AND only when Remember is on.
            if (ChkRememberOptInDetails?.IsChecked == true && App.Settings?.Current is { } s)
            {
                s.RememberDirectoryDetails = true;
                s.SavedDirectoryTags = tags;
                s.SavedDirectoryStatusText = statusText;
                App.Settings.Save();
            }
            else if (ChkRememberOptInDetails?.IsChecked != true && App.Settings?.Current is { } s2 && s2.RememberDirectoryDetails)
            {
                // Remember was previously on, now off → clear saved state.
                s2.RememberDirectoryDetails = false;
                s2.SavedDirectoryTags = new List<string>();
                s2.SavedDirectoryStatusText = "";
                App.Settings.Save();
            }
        }

        // True once the directory opt-in for the active remote session has
        // succeeded. Drives the "Listed/Claimed" header pill + Session Code
        // confirmation. Reset when the remote session stops.
        private bool _directoryOptedIn;

        /// <summary>
        /// Refreshes the header listing pill and the Session Code confirmation
        /// banner from the current remote-session state:
        ///   • no active session            → pill hidden, banner hidden
        ///   • active, not opted in         → "Private only"
        ///   • active, opted in, no claimer → "Listed" + confirmation banner
        ///   • active, opted in, claimed    → "Claimed" + confirmation banner
        /// </summary>
        private void UpdateDirectoryListingStatus()
        {
            var active = App.RemoteControl?.IsActive ?? false;
            var claimed = App.RemoteControl?.ControllerConnected ?? false;

            // Session Code confirmation banner: only once listed.
            if (ListedConfirmationPanel != null)
                ListedConfirmationPanel.Visibility = (active && _directoryOptedIn)
                    ? System.Windows.Visibility.Visible
                    : System.Windows.Visibility.Collapsed;

            if (DirectoryStatusPill == null) return;

            if (!active)
            {
                DirectoryStatusPill.Visibility = System.Windows.Visibility.Collapsed;
                return;
            }

            string text;
            System.Windows.Media.Color dot;
            string tip;
            if (!_directoryOptedIn)
            {
                text = "Private only";
                dot = System.Windows.Media.Color.FromRgb(0x8A, 0x8A, 0xA0); // muted grey
                tip = "Your session is private — you are not listed in the Available Subjects Directory.";
            }
            else if (claimed)
            {
                text = "Claimed";
                dot = System.Windows.Media.Color.FromRgb(0x00, 0xFF, 0x88); // green
                tip = "A controller has claimed your directory listing.";
            }
            else
            {
                text = "Listed";
                dot = System.Windows.Media.Color.FromRgb(0xB4, 0x7B, 0xFF); // neon purple (directory accent)
                tip = "You're listed in the Available Subjects Directory and waiting to be claimed.";
            }

            if (TxtDirectoryStatus != null) TxtDirectoryStatus.Text = text;
            if (DirectoryStatusDot != null) DirectoryStatusDot.Fill = new System.Windows.Media.SolidColorBrush(dot);
            DirectoryStatusPill.ToolTip = tip;
            DirectoryStatusPill.Visibility = System.Windows.Visibility.Visible;
        }

        private async Task StopRemoteControl()
        {
            if (App.RemoteControl != null)
            {
                App.RemoteControl.ControllerConnectedChanged -= OnRemoteControllerChanged;
                App.RemoteControl.CommandReceived -= OnRemoteCommandReceived;
                App.RemoteControl.SessionEnded -= OnRemoteSessionEnded;
                await App.RemoteControl.StopSessionAsync();
            }

            HideRemoteControlOverlay();
            UpdateStartButtonForRemoteControl(false);
            RemoteControlPanel.Visibility = System.Windows.Visibility.Collapsed;
            RemoteLinkPanel.Visibility = System.Windows.Visibility.Collapsed;
            RemoteCodePanel.Visibility = System.Windows.Visibility.Collapsed;
            RemoteStatusPanel.Visibility = System.Windows.Visibility.Collapsed;
            BtnStopRemote.Visibility = System.Windows.Visibility.Collapsed;
            if (ImgRemoteQrCode != null) ImgRemoteQrCode.Source = null;
            if (LstRemoteCommandLog != null) LstRemoteCommandLog.Items.Clear();

            // SP5 layer 3: restore the opt-in section so the user can
            // configure it for the next session (or untick if they're done).
            // The opt-in checkbox itself stays unchecked — re-opt every time.
            // Re-enable + un-grey it (it stays visible during the session now).
            if (OptInSectionPanel != null)
            {
                OptInSectionPanel.Visibility = System.Windows.Visibility.Visible;
                OptInSectionPanel.IsEnabled = true;
                OptInSectionPanel.Opacity = 1.0;
            }
            if (ChkOptIntoDirectory != null)
                ChkOptIntoDirectory.IsChecked = false;
            if (OptInFormPanel != null)
                OptInFormPanel.Visibility = System.Windows.Visibility.Collapsed;

            // Clear the directory listing state + header pill.
            _directoryOptedIn = false;
            UpdateDirectoryListingStatus();
        }

        private void OnRemoteControllerChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                var connected = App.RemoteControl?.ControllerConnected ?? false;
                UpdateRemoteStatus(connected);
                UpdateStartButtonForRemoteControl(connected);
                // Header listing pill flips Listed ↔ Claimed as a controller
                // connects/disconnects.
                UpdateDirectoryListingStatus();

                if (connected)
                {
                    // Only stop the local session on the FIRST controller of this remote
                    // session. On a takeover (controller A leaves, B joins), connected
                    // briefly transitions true→false→true; without this guard, B's connect
                    // would re-stop a session that A or the sub had running, even when
                    // B hasn't sent any command yet (bug report #166).
                    if (!_remoteSessionHasTakenLocal)
                    {
                        _remoteSessionHasTakenLocal = true;
                        try { _sessionEngine?.StopSession(completed: false); } catch { }
                    }

                    ShowRemoteControlOverlay();
                    NotifyRemoteControllerJoined();
                }
                else
                {
                    HideRemoteControlOverlay();
                }
            });
        }

        // Set true once the first controller of the active remote-control session has
        // claimed control. Reset when the remote session ends so a future remote session
        // re-applies the take-over-local-session step on its first controller.
        private bool _remoteSessionHasTakenLocal;

        private void OnRemoteControllerIdleChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                var idle = App.RemoteControl?.ControllerIdle ?? false;
                TxtRemoteOverlaySubtitle.Text = idle
                    ? "Controller may be idle..."
                    : "Someone else is controlling your app";
                TxtRemoteOverlaySubtitle.Foreground = new System.Windows.Media.SolidColorBrush(
                    idle ? System.Windows.Media.Color.FromRgb(0xFF, 0xA5, 0x00)  // orange
                         : System.Windows.Media.Color.FromRgb(0xA0, 0xA0, 0xA0)); // gray
            });
        }

        private void OnRemoteSessionEnded(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                _remoteSessionHasTakenLocal = false;
                HideRemoteControlOverlay();
                UpdateStartButtonForRemoteControl(false);
                _isLoading = true;
                ChkRemoteControlEnabled.IsChecked = false;
                _isLoading = false;
                RemoteControlPanel.Visibility = System.Windows.Visibility.Collapsed;
                RemoteCodePanel.Visibility = System.Windows.Visibility.Collapsed;
                RemoteStatusPanel.Visibility = System.Windows.Visibility.Collapsed;
                BtnStopRemote.Visibility = System.Windows.Visibility.Collapsed;

                // Re-enable + un-grey the opt-in section and clear the listing pill.
                if (OptInSectionPanel != null)
                {
                    OptInSectionPanel.IsEnabled = true;
                    OptInSectionPanel.Opacity = 1.0;
                }
                _directoryOptedIn = false;
                UpdateDirectoryListingStatus();
            });
        }

        private void UpdateRemoteStatus(bool controllerConnected)
        {
            if (controllerConnected)
            {
                RemoteStatusDot.Fill = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x00, 0xFF, 0x88));
                TxtRemoteStatus.Text = Loc.Get("label_controller_connected");
                TxtRemoteStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x00, 0xFF, 0x88));
            }
            else
            {
                RemoteStatusDot.Fill = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xFF, 0xA5, 0x00));
                TxtRemoteStatus.Text = Loc.Get("label_waiting_for_controller");
                TxtRemoteStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xA0, 0xA0, 0xA0));
            }
        }

        private void ShowRemoteControlOverlay()
        {
            var code = App.RemoteControl?.SessionCode;
            var overlayPin = App.RemoteControl?.ConnectPin;
            var sessionText = !string.IsNullOrEmpty(code)
                ? $"Session: {string.Join(" ", code.ToCharArray())}"
                : "";
            if (!string.IsNullOrEmpty(overlayPin))
                sessionText += $"  PIN: {overlayPin}";
            TxtOverlaySessionCode.Text = sessionText;

            // Hide browser to avoid WebView2 airspace issue (renders on top of WPF overlays)
            BrowserContainer.Visibility = System.Windows.Visibility.Hidden;
            RemoteControlOverlay.Visibility = System.Windows.Visibility.Visible;

            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
            RemoteControlOverlay.BeginAnimation(OpacityProperty, fadeIn);

            StartRemoteSessionInfoTimer();
        }

        private void HideRemoteControlOverlay()
        {
            if (RemoteControlOverlay.Visibility != System.Windows.Visibility.Visible) return;

            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (s, _) =>
            {
                RemoteControlOverlay.Visibility = System.Windows.Visibility.Collapsed;
                // Restore browser visibility now that overlay is gone
                BrowserContainer.Visibility = System.Windows.Visibility.Visible;
            };
            RemoteControlOverlay.BeginAnimation(OpacityProperty, fadeOut);
            _remoteNotificationTimer?.Stop();
            _remoteSessionInfoTimer?.Stop();
        }

        private void WireRemoteSessionCallbacks()
        {
            if (App.RemoteControl == null) return;

            App.RemoteControl.GetAvailableSessionsCallback = () =>
            {
                var sessions = new List<object>();
                try
                {
                    // Include built-in sessions
                    foreach (var s in Models.Session.GetAllSessions().Where(s => s.IsAvailable))
                    {
                        sessions.Add(new { id = s.Id, name = s.GetModeAwareName(), icon = s.Icon, duration_minutes = s.DurationMinutes, difficulty = s.Difficulty.ToString() });
                    }
                    // Include custom sessions from SessionManager
                    if (_sessionManager != null)
                    {
                        foreach (var s in _sessionManager.CustomSessions.Where(s => s.IsAvailable))
                        {
                            sessions.Add(new { id = s.Id, name = s.GetModeAwareName(), icon = s.Icon, duration_minutes = s.DurationMinutes, difficulty = s.Difficulty.ToString() });
                        }
                    }
                }
                catch { }
                return sessions;
            };

            App.RemoteControl.GetSessionProgressCallback = () =>
            {
                try
                {
                    if (_sessionEngine?.IsRunning != true || _sessionEngine.CurrentSession == null)
                        return null;

                    var session = _sessionEngine.CurrentSession;
                    var phaseIndex = _sessionEngine.CurrentPhaseIndex;
                    var phaseName = session.Phases != null && phaseIndex >= 0 && phaseIndex < session.Phases.Count
                        ? session.Phases[phaseIndex].Name : "";

                    return new Services.SessionProgressInfo
                    {
                        Name = session.GetModeAwareName(),
                        Icon = session.Icon,
                        ElapsedSeconds = (int)_sessionEngine.ElapsedTime.TotalSeconds,
                        TotalSeconds = session.DurationMinutes * 60,
                        IsPaused = _sessionEngine.IsPaused,
                        CurrentPhase = phaseName
                    };
                }
                catch { return null; }
            };

            App.RemoteControl.FindSessionByIdCallback = (sessionId) =>
            {
                try
                {
                    // Check built-in sessions first
                    var session = Models.Session.GetAllSessions()
                        .FirstOrDefault(s => s.Id == sessionId && s.IsAvailable);
                    // Then check custom sessions
                    if (session == null && _sessionManager != null)
                    {
                        session = _sessionManager.CustomSessions
                            .FirstOrDefault(s => s.Id == sessionId && s.IsAvailable);
                    }
                    return session;
                }
                catch { return null; }
            };
        }

        private void StartRemoteSessionInfoTimer()
        {
            _remoteSessionInfoTimer?.Stop();
            _remoteSessionInfoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _remoteSessionInfoTimer.Tick += (s, _) => UpdateRemoteSessionInfo();
            _remoteSessionInfoTimer.Start();
            UpdateRemoteSessionInfo();
        }

        private void UpdateRemoteSessionInfo()
        {
            try
            {
                if (_sessionEngine?.IsRunning == true && _sessionEngine.CurrentSession != null)
                {
                    var session = _sessionEngine.CurrentSession;
                    // Show active state, hide idle state
                    RemoteSessionIdle.Visibility = Visibility.Collapsed;
                    RemoteSessionActive.Visibility = Visibility.Visible;

                    TxtRemoteSessionName.Text = $"{session.Icon} {session.GetModeAwareName()}";

                    var elapsed = _sessionEngine.ElapsedTime;
                    var total = TimeSpan.FromMinutes(session.DurationMinutes);
                    var pauseLabel = _sessionEngine.IsPaused ? "  ⏸ PAUSED" : "";
                    TxtRemoteSessionTime.Text = $"{elapsed:mm\\:ss} / {total:mm\\:ss}{pauseLabel}";

                    var phaseIndex = _sessionEngine.CurrentPhaseIndex;
                    if (session.Phases != null && phaseIndex >= 0 && phaseIndex < session.Phases.Count)
                        TxtRemoteSessionPhase.Text = session.Phases[phaseIndex].Name;
                    else
                        TxtRemoteSessionPhase.Text = "";
                }
                else
                {
                    // Show idle state, hide active state
                    RemoteSessionIdle.Visibility = Visibility.Visible;
                    RemoteSessionActive.Visibility = Visibility.Collapsed;
                }
            }
            catch { }
        }

        private void OnRemoteCommandReceived(object? sender, string action)
        {
            if (SuppressedCommands.Contains(action)) return;

            Dispatcher.Invoke(() =>
            {
                ShowCommandNotification(action);
                AppendRemoteCommandLog(action);
            });
        }

        /// <summary>
        /// Appends a command to the Remote Control tab's command log.
        /// Caps the log at 50 entries (oldest dropped).
        /// </summary>
        private void AppendRemoteCommandLog(string action)
        {
            if (LstRemoteCommandLog == null) return;
            try
            {
                var label = CommandLabels.TryGetValue(action, out var l) ? Loc.Get(l) : action.Replace("_", " ");
                var entry = $"{DateTime.Now:HH:mm:ss}  {label}";
                LstRemoteCommandLog.Items.Insert(0, entry);
                while (LstRemoteCommandLog.Items.Count > 50)
                    LstRemoteCommandLog.Items.RemoveAt(LstRemoteCommandLog.Items.Count - 1);
            }
            catch { }
        }

        /// <summary>
        /// Refreshes the Remote Control tab UI: gating overlay, QR code (if a session
        /// is active), tier card highlight. Called whenever the tab is shown.
        /// </summary>
        private void UpdateRemoteControlUI()
        {
            RefreshPremiumGate(RemoteControlGate);
            RefreshTierCardHighlight();
            // If a session is already running, refresh the QR code with the current code.
            var code = App.RemoteControl?.SessionCode;
            if (!string.IsNullOrEmpty(code))
                RefreshRemoteQrCode(BuildRemotePairingUrl(code));
            else if (ImgRemoteQrCode != null)
                ImgRemoteQrCode.Source = null;
        }

        /// <summary>
        /// Generates the pairing URL for the QR code from the current session code.
        /// Uses a hash fragment so the PIN never appears in server access logs or
        /// Referer headers. The web page parses the fragment and auto-connects.
        /// </summary>
        private string BuildRemotePairingUrl(string code)
        {
            var pin = App.RemoteControl?.ConnectPin;
            if (!string.IsNullOrEmpty(pin))
                return $"https://cclabs.app/remote/#code={code}&pin={pin}";
            return $"https://cclabs.app/remote/#code={code}";
        }

        /// <summary>
        /// Renders a QR code image into ImgRemoteQrCode for the given pairing URL.
        /// </summary>
        private void RefreshRemoteQrCode(string url)
        {
            if (ImgRemoteQrCode == null) return;
            try
            {
                // Pull mod-themed colors. Use AccentDarkColor for foreground (max contrast on white).
                byte[] fgRgb = new byte[] { 0xFF, 0x14, 0x93 };
                byte[] bgRgb = new byte[] { 0xFF, 0xFF, 0xFF };
                try
                {
                    var accentDarkHex = App.Mods?.GetAccentDarkColorHex();
                    if (!string.IsNullOrEmpty(accentDarkHex))
                    {
                        var fgColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(accentDarkHex);
                        fgRgb = new byte[] { fgColor.R, fgColor.G, fgColor.B };
                    }
                }
                catch { /* fall back to default pink */ }

                using var generator = new QRCoder.QRCodeGenerator();
                using var data = generator.CreateQrCode(url, QRCoder.QRCodeGenerator.ECCLevel.M);
                using var qr = new QRCoder.PngByteQRCode(data);
                var bytes = qr.GetGraphic(10, fgRgb, bgRgb);
                using var ms = new MemoryStream(bytes);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = ms;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                ImgRemoteQrCode.Source = bmp;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to render remote QR code");
            }
        }

        /// <summary>
        /// Highlights the active tier card based on CmbRemoteTier.SelectedIndex.
        /// </summary>
        private void RefreshTierCardHighlight()
        {
            if (TierCardLight == null || TierCardStandard == null || TierCardFull == null) return;
            var dim = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x48));
            var active = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4));
            TierCardLight.BorderBrush = dim;
            TierCardLight.BorderThickness = new Thickness(1);
            TierCardStandard.BorderBrush = dim;
            TierCardStandard.BorderThickness = new Thickness(1);
            TierCardFull.BorderBrush = dim;
            TierCardFull.BorderThickness = new Thickness(1);

            var idx = CmbRemoteTier?.SelectedIndex ?? 0;
            Border? activeCard = idx switch
            {
                1 => TierCardStandard,
                2 => TierCardFull,
                _ => TierCardLight,
            };
            if (activeCard != null)
            {
                activeCard.BorderBrush = active;
                activeCard.BorderThickness = new Thickness(2);
            }
        }

        /// <summary>
        /// Routes a tier card click to the legacy CmbRemoteTier handler so the
        /// existing tier-change logic still fires.
        /// </summary>
        private void TierCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string tagStr && int.TryParse(tagStr, out var idx))
            {
                if (CmbRemoteTier != null && CmbRemoteTier.SelectedIndex != idx)
                    CmbRemoteTier.SelectedIndex = idx;
                RefreshTierCardHighlight();
            }
        }

        private void ShowCommandNotification(string action)
        {
            var label = CommandLabels.TryGetValue(action, out var l) ? Loc.Get(l) : action.Replace("_", " ");
            TxtRemoteCommand.Text = label;

            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            RemoteCommandNotification.BeginAnimation(OpacityProperty, fadeIn);

            _remoteNotificationTimer?.Stop();
            _remoteNotificationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _remoteNotificationTimer.Tick += (s, _) =>
            {
                _remoteNotificationTimer.Stop();
                HideCommandNotification();
            };
            _remoteNotificationTimer.Start();
        }

        private void HideCommandNotification()
        {
            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            RemoteCommandNotification.BeginAnimation(OpacityProperty, fadeOut);
        }

        private async void BtnEndRemoteSession_Click(object sender, RoutedEventArgs e)
        {
            await StopRemoteControl();
            _isLoading = true;
            ChkRemoteControlEnabled.IsChecked = false;
            _isLoading = false;
        }

        // Methods called by RemoteControlService for session commands
        internal async void StartSessionFromRemote(Models.Session session)
        {
            try
            {
                App.Logger?.Information("[RemoteControl] StartSessionFromRemote called for: {Name} (id: {Id})", session.Name, session.Id);

                // Stop any existing running session first
                if (_sessionEngine?.IsRunning == true)
                {
                    App.Logger?.Information("[RemoteControl] Stopping existing session engine before starting new one");
                    _sessionEngine.StopSession(completed: false);
                }

                if (_sessionEngine == null)
                {
                    _sessionEngine = new Services.SessionEngine(this);
                    _sessionEngine.SessionCompleted += OnSessionCompleted;
                    _sessionEngine.ProgressUpdated += OnSessionProgressUpdated;
                    _sessionEngine.PhaseChanged += OnSessionPhaseChanged;
                    _sessionEngine.SessionStarted += OnSessionStarted;
                    _sessionEngine.SessionStopped += OnSessionStopped;
                    // Attach the bark system to this session engine (it's MainWindow-owned
                    // and created lazily, so BarkService can't subscribe at its own Start()).
                    App.Bark?.AttachSessionEngine(_sessionEngine);
                }

                // Call StartEngine directly — BtnStart_Click returns early
                // when remote controlled due to its guard check
                if (!_isRunning)
                {
                    App.Logger?.Information("[RemoteControl] Starting main engine for remote session");
                    StartEngine();

                    // Kill overlays that StartEngine activated from saved settings —
                    // the session engine will control them based on session segments
                    App.Overlay?.StopPinkFilter();
                    App.Overlay?.StopSpiral();
                    App.Logger?.Information("[RemoteControl] Cleared overlays — session engine will control them");
                }

                App.IsSessionRunning = true;
                await _sessionEngine.StartSessionAsync(session);
                App.Logger?.Information("[RemoteControl] Session engine started successfully for: {Name}", session.Name);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[RemoteControl] Failed to start session from remote: {Name}", session?.Name);
            }
        }

        internal void PauseSessionFromRemote()
        {
            try
            {
                if (_sessionEngine?.IsRunning == true && !_sessionEngine.IsPaused)
                    _sessionEngine.PauseSession();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[RemoteControl] Failed to pause session from remote");
            }
        }

        internal void ResumeSessionFromRemote()
        {
            try
            {
                if (_sessionEngine?.IsRunning == true && _sessionEngine.IsPaused)
                    _sessionEngine.ResumeSession();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[RemoteControl] Failed to resume session from remote");
            }
        }

        internal void StopSessionFromRemote() => StopEngineAndSession("RemoteControl");

        /// <summary>Stop the running session + main engine from an external trigger (remote control,
        /// or diving into a Chaos run). Safe to call when nothing is running — it self-guards.</summary>
        internal void StopEngineAndSession(string source)
        {
            try
            {
                App.Logger?.Information("[{Source}] StopEngineAndSession called", source);
                if (_sessionEngine?.IsRunning == true)
                    _sessionEngine.StopSession();

                App.IsSessionRunning = false;

                // Also stop the main engine to reset services and _isRunning state
                if (_isRunning)
                {
                    App.Logger?.Information("[{Source}] Also stopping main engine", source);
                    StopEngine();
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[{Source}] Failed to stop engine/session", source);
            }
        }

        internal void TriggerPanicFromRemote()
        {
            try
            {
                App.Logger?.Information("[RemoteControl] Panic triggered from remote");

                // Track panic press for Relapse achievement
                App.Achievements?.TrackPanicPressed();

                // Kill all audio immediately
                App.KillAllAudio();
                App.Autonomy?.CancelActivePulses();

                if (_sessionEngine != null && _sessionEngine.IsRunning && !_sessionEngine.IsPaused)
                {
                    _sessionEngine.PauseSession();
                }

                // Stop video explicitly (closes all video windows)
                App.Video?.Stop();

                // Stop other active effects
                App.Flash?.Stop();
                App.Subliminal?.Stop();
                App.Bubbles?.Stop();
                App.BouncingText?.Stop();
                App.BubbleCount?.Stop();
                App.MindWipe?.Stop();
                App.BrainDrain?.Stop();
                App.LockCard?.Stop();

                // Turn off overlays but keep the overlay service alive
                // so the controller can turn them back on
                EnablePinkFilter(false);
                EnableSpiral(false);
                App.Overlay?.RefreshOverlays();

                App.InteractionQueue?.ForceReset();

                Show();
                WindowState = WindowState.Normal;
                Activate();
                Topmost = true;
                Topmost = false;
                App.Overlay?.NotifyTopWindowClosed();
                ShowAvatarTube();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[RemoteControl] Failed to trigger panic from remote");
            }
        }

        internal void MinimizeToTrayForRemote()
        {
            _trayIcon?.MinimizeToTray();
            _trayIcon?.ShowNotification("Remote Control", "Session active — minimized to tray.", System.Windows.Forms.ToolTipIcon.Info);
        }

        /// <summary>
        /// Alerts the host that a remote controller just joined. Pops a tray
        /// balloon and flashes the taskbar icon if minimized — does NOT restore
        /// the window so the host stays in control of window state.
        /// </summary>
        private void NotifyRemoteControllerJoined()
        {
            // Always show a tray balloon — it's a useful cue even when visible.
            try
            {
                _trayIcon?.ShowNotification(
                    Loc.Get("title_remote_controller_joined"),
                    Loc.Get("msg_remote_controller_joined"),
                    System.Windows.Forms.ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to show remote controller tray balloon: {Error}", ex.Message);
            }

            // Flash the taskbar button so the host notices even with notifications off.
            if (this.WindowState == WindowState.Minimized || !this.IsVisible)
            {
                try { Helpers.FlashWindowHelper.Flash(this); } catch { }
            }
        }

        internal void RestoreFromTrayForRemote()
        {
            _trayIcon?.ShowWindow();
        }

        /// <summary>
        /// Called when a second instance signals this instance to show itself.
        /// </summary>
        public void ShowFromTray()
        {
            _trayIcon?.ShowWindow();
        }

        #endregion

        private void ChkMuteAvatar_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var checkbox = sender as CheckBox;
            var isEnabled = checkbox?.IsChecked == true;
            _avatarTubeWindow?.SetMuteAvatar(isEnabled);

            if (App.Settings?.Current != null)
            {
                App.Settings.Current.AvatarMuted = isEnabled;
                App.Settings.Save();
            }
        }

        private void ChkMuteWhispers_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var checkbox = sender as CheckBox;
            var isMuted = checkbox?.IsChecked == true;

            // Toggle SubAudioEnabled (muted = disabled)
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.SubAudioEnabled = !isMuted;
                App.Settings.Save();
            }

            // Sync Settings tab checkbox (inverted - it's "enabled" not "muted")
            _isLoading = true;
            ChkAudioWhispers.IsChecked = !isMuted;
            _isLoading = false;

            // Sync avatar menu
            _avatarTubeWindow?.UpdateQuickMenuState();
        }

        private async void ChkPauseBrowser_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var checkbox = sender as CheckBox;
            var isPaused = checkbox?.IsChecked == true;
            await SetBrowserPaused(isPaused);
            _avatarTubeWindow?.SetBrowserPaused(isPaused);
        }

        private async Task SetBrowserPaused(bool isPaused)
        {
            try
            {
                var webView = GetBrowserWebView();
                if (webView?.CoreWebView2 != null)
                {
                    if (isPaused)
                    {
                        webView.CoreWebView2.IsMuted = true;
                        await webView.CoreWebView2.ExecuteScriptAsync(@"
                            document.querySelectorAll('audio, video').forEach(el => el.pause());
                        ");
                    }
                    else
                    {
                        webView.CoreWebView2.IsMuted = false;
                        await webView.CoreWebView2.ExecuteScriptAsync(@"
                            document.querySelectorAll('audio, video').forEach(el => el.play());
                        ");
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to toggle browser audio: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Sync Quick Controls UI from avatar context menu
        /// </summary>
        public void SyncQuickControlsUI(bool? muteAvatar = null, bool? muteWhispers = null, bool? pauseBrowser = null)
        {
            _isLoading = true;
            try
            {
                // Update Companion tab controls
                if (muteAvatar.HasValue) ChkMuteAvatarCompanion.IsChecked = muteAvatar.Value;
                if (muteWhispers.HasValue) ChkMuteWhispersCompanion.IsChecked = muteWhispers.Value;
                if (pauseBrowser.HasValue) ChkPauseBrowserCompanion.IsChecked = pauseBrowser.Value;
            }
            finally
            {
                _isLoading = false;
            }
        }

        /// <summary>
        /// Sync whispers enabled state across all UI controls (Settings tab + Companion tab)
        /// </summary>
        public void SyncWhispersUI(bool enabled)
        {
            _isLoading = true;
            try
            {
                // Settings tab - ChkAudioWhispers represents "whispers enabled"
                ChkAudioWhispers.IsChecked = enabled;

                // Companion tab - ChkMuteWhispersCompanion represents "whispers muted" (inverted)
                ChkMuteWhispersCompanion.IsChecked = !enabled;
            }
            finally
            {
                _isLoading = false;
            }
        }

        #endregion

        #region Banner Rotation

        private void InitializeBannerRotation()
        {
            // Start the rotation timer (switches every 4 seconds)
            _bannerRotationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(4)
            };
            _bannerRotationTimer.Tick += BannerRotationTimer_Tick;

            // Update welcome message based on login status
            UpdateBannerWelcomeMessage();

            // Always start rotation now (we have 3 messages including the thanks message)
            _bannerRotationTimer.Start();
        }

        private void UpdateBannerWelcomeMessage()
        {
            // Check offline mode first
            if (App.Settings?.Current?.OfflineMode == true &&
                !string.IsNullOrWhiteSpace(App.Settings?.Current?.OfflineUsername))
            {
                TxtBannerSecondary.Text = Loc.GetF("label_welcome_back_0_offline_mode", App.Settings.Current.OfflineUsername);
                return;
            }

            // Check unified display name first, then fall back to provider-specific
            var displayName = App.Settings?.Current?.UserDisplayName
                           ?? App.Patreon?.DisplayName
                           ?? App.Discord?.DisplayName;
            if (!string.IsNullOrEmpty(displayName))
            {
                TxtBannerSecondary.Text = Loc.GetF("label_welcome_back_0", displayName);
            }
            else
            {
                // Not logged in - show generic welcome
                TxtBannerSecondary.Text = Loc.Get("label_welcome_consider_logging_in_with_patreon_for");
            }
        }

        /// <summary>
        /// Shows the "Welcome Back, Pioneer!" popup for Season 0 OG users
        /// </summary>
        private void ShowOgWelcomePopup()
        {
            try
            {
                var dialog = new Window
                {
                    Title = Loc.Get("title_welcome_back"),
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    SizeToContent = SizeToContent.WidthAndHeight,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = System.Windows.Media.Brushes.Transparent
                };

                var border = new System.Windows.Controls.Border
                {
                    BorderBrush = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0xFF, 0xD7, 0x00)), // Gold
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(10),
                    Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x1A, 0x1A, 0x2E)),
                    Padding = new Thickness(30)
                };

                var stack = new System.Windows.Controls.StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    MaxWidth = 400
                };

                // Star header
                stack.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = "⭐ Welcome Back, Pioneer! ⭐",
                    FontSize = 24,
                    FontWeight = FontWeights.Bold,
                    Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0xFF, 0xD7, 0x00)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 15)
                });

                // Message
                stack.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = "You've been recognized as a Season 0 OG.\n\n" +
                           "Your account has been reset for Season 1, but your legacy lives on:\n\n" +
                           "  ⭐ Your name now has a star icon on the leaderboard\n" +
                           "  ✨ Your row is highlighted in gold\n" +
                           "  👑 Everyone will know you were here from the beginning\n\n" +
                           "Your unlocks and achievements have been preserved.\n" +
                           "Good luck climbing the leaderboard again!",
                    FontSize = 13,
                    Foreground = System.Windows.Media.Brushes.White,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 0, 20)
                });

                // Continue button
                var button = new System.Windows.Controls.Button
                {
                    Content = "Continue",
                    Padding = new Thickness(30, 10, 30, 10),
                    Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0xFF, 0x69, 0xB4)),
                    Foreground = System.Windows.Media.Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                button.Click += (s, e) => dialog.Close();
                stack.Children.Add(button);

                border.Child = stack;
                dialog.Content = border;
                dialog.MouseLeftButtonDown += (s, e) =>
                {
                    if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                        dialog.DragMove();
                };

                dialog.ShowDialog();

                // Mark as shown so we don't show again
                if (App.Settings?.Current != null)
                {
                    App.Settings.Current.HasShownOgWelcome = true;
                    App.Settings.Save();
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to show OG welcome popup");
            }
        }

        /// <summary>
        /// Flag to indicate when a startup dialog (What's New) is showing.
        /// Used to prevent update dialog from showing behind it.
        /// </summary>
        public static bool IsStartupDialogShowing { get; set; } = false;

        /// <summary>
        /// Shows a "What's New" dialog if the app was updated since last launch
        /// </summary>
        // Season Recap is shown at most once per app run; guards the two trigger paths
        // (startup month-check and the server-reset nudge from ProfileSyncService).
        private bool _seasonRecapShown;

        /// <summary>
        /// Presents the Season Recap card when the user has been reset. Triggers on EITHER:
        ///   • a monthly rollover (UTC month != LastSeasonResetSeen) — fires on any day of the
        ///     new month, not just the 1st; or
        ///   • a server-driven reset (AppSettings.SeasonResetPending, set by ProfileSyncService
        ///     when the server returns level_reset) — this is how an admin reset of a single
        ///     account surfaces the card mid-month, and makes the feature testable.
        ///
        /// Snapshots the just-ended season BEFORE clearing its counters, then shows the card
        /// (or the legacy textual notice when there's no season data yet). The actual level/XP/
        /// streak reset still happens via the server + SkillTreeService — this only wraps it.
        /// Safe to call repeatedly; shows at most once per app run. Public so ProfileSyncService
        /// can nudge it the moment a reset arrives.
        /// </summary>
        public void TryPresentSeasonRecap()
        {
            try
            {
                if (_seasonRecapShown) return;
                if (App.Settings?.Current == null) return;

                var currentSeason = DateTime.UtcNow.ToString("yyyy-MM");
                var lastSeasonSeen = App.Settings.Current.LastSeasonResetSeen ?? "";
                var highestLevel = App.Settings.Current.HighestLevelEver;
                var resetPending = App.Settings.Current.SeasonResetPending;

                // Brand-new users (never leveled up) skip this. They'll see it once they progress.
                if (highestLevel < 2) return;

                var monthRolled = lastSeasonSeen != currentSeason;
                if (!monthRolled && !resetPending) return;

                _seasonRecapShown = true;
                App.Logger?.Information("Presenting season recap (monthRolled={Month}, resetPending={Pending}, last={Old}, current={New}, highestLevel={Highest})",
                    monthRolled, resetPending, string.IsNullOrEmpty(lastSeasonSeen) ? "(none)" : lastSeasonSeen, currentSeason, highestLevel);

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        IsStartupDialogShowing = true;

                        // Snapshot the just-ended season BEFORE its counters are cleared, then roll
                        // the bucket. CaptureAndRollover writes the JSON first and only then clears —
                        // order is load-bearing (an empty snapshot = an empty card).
                        var snapshot = Services.SeasonRecapService.CaptureAndRollover(currentSeason);

                        // Advance the persisted idempotency latch IMMEDIATELY after the
                        // destructive roll and BEFORE presenting the card. CaptureAndRollover
                        // has already written the snapshot (if any) and cleared the live
                        // counters. If we deferred this write until after ShowDialog and the
                        // window threw (XAML resource lookups in a DataTemplate are a known
                        // hazard in this codebase), the catch below would swallow it, the latch
                        // would never advance, and the next launch would re-roll the now-empty
                        // season — permanently losing the real recap. Persist the latch first.
                        App.Settings.Current.LastSeasonResetSeen = currentSeason;
                        App.Settings.Current.SeasonResetPending = false;
                        App.Settings.Save();

                        if (snapshot != null)
                        {
                            var vm = new ViewModels.SeasonRecapViewModel(snapshot);
                            var recapWindow = new Controls.SeasonRecapWindow(vm) { Owner = this };
                            recapWindow.ShowDialog();
                        }
                        else
                        {
                            // No meaningful season data yet (e.g. first reset after this feature
                            // shipped, before any tracking accrued) — fall back to the legacy notice
                            // so the user still understands what happened.
                            var message =
                                "The monthly leaderboard season has rotated. This happens at the start of every month so everyone has a fresh chance to climb the rankings.\n\n" +
                                "What resets:\n" +
                                "  - Current Level and XP\n" +
                                "  - Daily quest streak\n" +
                                "  - Monthly leaderboard position\n\n" +
                                "What's preserved:\n" +
                                "  - All achievements\n" +
                                "  - Highest Level Ever (yours: " + highestLevel + ")\n" +
                                "  - Skill points and unlocked enhancements\n" +
                                "  - Total lifetime XP\n" +
                                "  - Patreon perks and whitelist\n\n" +
                                "Welcome to season " + currentSeason + "!";

                            MessageBox.Show(
                                message,
                                "New Season Started",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning(ex, "Failed to present season recap");
                    }
                    finally
                    {
                        IsStartupDialogShowing = false;
                    }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Error checking for season recap");
            }
        }

        private void ShowWhatsNewIfNeeded()
        {
            try
            {
                var currentVersion = Services.UpdateService.AppVersion;
                var lastSeenVersion = App.Settings?.Current?.LastSeenVersion ?? "";

                // If versions differ, show the patch notes
                if (lastSeenVersion != currentVersion)
                {
                    App.Logger?.Information("Version changed from {OldVersion} to {NewVersion}, showing What's New",
                        string.IsNullOrEmpty(lastSeenVersion) ? "(none)" : lastSeenVersion, currentVersion);

                    // Delay slightly to let the window fully load
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            // Set flag BEFORE showing MessageBox so update dialog knows to wait
                            IsStartupDialogShowing = true;
                            App.Logger?.Information("What's New dialog showing, setting IsStartupDialogShowing=true");

                            MessageBox.Show(
                                Services.UpdateService.CurrentPatchNotes,
                                $"What's New in v{currentVersion}",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);

                            // Update the last seen version
                            if (App.Settings?.Current != null)
                            {
                                App.Settings.Current.LastSeenVersion = currentVersion;
                                App.Settings.Save();
                            }
                        }
                        catch (Exception ex)
                        {
                            App.Logger?.Warning(ex, "Failed to show What's New dialog");
                        }
                        finally
                        {
                            // Clear flag AFTER MessageBox is dismissed
                            IsStartupDialogShowing = false;
                            App.Logger?.Information("What's New dialog dismissed, setting IsStartupDialogShowing=false");
                        }
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Error checking for What's New");
            }
        }

        private void BannerRotationTimer_Tick(object? sender, EventArgs e)
        {
            // Get the 3 banner textblocks
            var banners = new[] { TxtBannerPrimary, TxtBannerSecondary, TxtBannerTertiary };

            // Determine which one to fade out and which to fade in
            var fadeOutTarget = banners[_bannerCurrentIndex];
            var nextIndex = (_bannerCurrentIndex + 1) % 3;
            var fadeInTarget = banners[nextIndex];

            // Create fade animations
            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(500),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
            };

            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(500),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
            };

            // Apply animations
            fadeOutTarget.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            fadeInTarget.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            // Disable hit testing on faded-out banner so hyperlinks don't capture clicks
            // (hyperlinks can still receive clicks even at Opacity=0)
            fadeOutTarget.IsHitTestVisible = false;
            fadeInTarget.IsHitTestVisible = true;

            _bannerCurrentIndex = nextIndex;
        }

        /// <summary>
        /// Set a temporary announcement message to display in the banner rotation
        /// </summary>
        public void SetBannerAnnouncement(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            TxtBannerSecondary.Text = message;

            // Ensure timer is running
            if (_bannerRotationTimer != null && !_bannerRotationTimer.IsEnabled)
            {
                _bannerRotationTimer.Start();
            }
        }

        #endregion

        private void PopulateAchievementGrid()
        {
            if (AchievementGrid == null) return;
            
            AchievementGrid.Children.Clear();
            PatronAchievementGrid?.Children.Clear();
            _achievementImages.Clear();

            var tileStyle = FindResource("AchievementTile") as Style;

            // Add all achievements (patron-exclusive ones routed to the separate grid)
            foreach (var kvp in Models.Achievement.All)
            {
                var achievement = kvp.Value;
                // Skip parked achievements (no reachable unlock path in this build).
                if (achievement.IsHidden) continue;
                var isUnlocked = App.Achievements?.Progress.IsUnlocked(achievement.Id) ?? false;
                
                var border = new Border { Style = tileStyle };
                var achName = App.Mods?.MakeModAware(achievement.Name) ?? achievement.Name;
                var achFlavor = App.Mods?.MakeModAware(achievement.FlavorText) ?? achievement.FlavorText;
                var achReq = App.Mods?.MakeModAware(achievement.Requirement) ?? achievement.Requirement;
                border.ToolTip = isUnlocked
                    ? $"{achName}\n\n\"{achFlavor}\""
                    : $"???\n\nRequirement: {achReq}";

                var image = new Image
                {
                    Stretch = Stretch.Uniform,
                    Source = LoadAchievementImage(achievement.ImageName)
                };

                // Apply blur if locked
                if (!isUnlocked)
                {
                    image.Effect = new BlurEffect { Radius = 15 };
                }

                border.Child = image;

                if (achievement.IsExclusive)
                    PatronAchievementGrid?.Children.Add(border);
                else
                    AchievementGrid.Children.Add(border);

                // Store reference for later updates
                _achievementImages[achievement.Id] = image;
            }
            
            // Note: All placeholders have been replaced with real achievements
            
            UpdateAchievementCount();
            App.Logger?.Information("Achievement grid populated with {Count} achievements", _achievementImages.Count);
        }
        
        private BitmapImage? LoadAchievementImage(string imageName)
        {
            try
            {
                var image = Services.ModResourceResolver.ResolveImage($"achievements/{imageName}");
                return image as BitmapImage ?? new BitmapImage(new Uri($"pack://application:,,,/Resources/achievements/{imageName}", UriKind.Absolute));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to load achievement image {Name}: {Error}", imageName, ex.Message);
                return null;
            }
        }
        
        private void RefreshAchievementTile(string achievementId)
        {
            if (!_achievementImages.TryGetValue(achievementId, out var image)) return;

            var isUnlocked = App.Achievements?.Progress.IsUnlocked(achievementId) ?? false;

            // Update blur
            image.Effect = isUnlocked ? null : new BlurEffect { Radius = 15 };

            // Update tooltip
            if (Models.Achievement.All.TryGetValue(achievementId, out var achievement))
            {
                var parent = image.Parent as Border;
                if (parent != null)
                {
                    var achName2 = App.Mods?.MakeModAware(achievement.Name) ?? achievement.Name;
                    var achFlavor2 = App.Mods?.MakeModAware(achievement.FlavorText) ?? achievement.FlavorText;
                    var achReq2 = App.Mods?.MakeModAware(achievement.Requirement) ?? achievement.Requirement;
                    parent.ToolTip = isUnlocked
                        ? $"{achName2}\n\n\"{achFlavor2}\""
                        : $"???\n\nRequirement: {achReq2}";
                }
            }

            UpdateAchievementCount();
        }

        private void RefreshAllAchievementTiles()
        {
            // Refresh all achievement tiles to reflect current unlock state
            foreach (var achievementId in _achievementImages.Keys.ToList())
            {
                RefreshAchievementTile(achievementId);
            }
            App.Logger?.Debug("All achievement tiles refreshed");
        }

        private void OnAchievementUnlockedInMainWindow(object? sender, Models.Achievement achievement)
        {
            Dispatcher.Invoke(() =>
            {
                RefreshAchievementTile(achievement.Id);
                App.Logger?.Information("Achievement tile refreshed: {Name}", achievement.Name);
            });
        }

        #endregion

        #region Enhancements (Skill Tree)

        // Node size constants for skill tree (sized for image backgrounds)
        private const double NodeWidth = 156;  // 10% smaller than 173
        private const double NodeHeight = 139;  // Includes name label row
        private const double TierSpacing = 350; // Much larger vertical spacing between tiers

        // Skill grid image cell dimensions (determined dynamically from skills1.png)
        private static int _skillCellWidth = 0;
        private static int _skillCellHeight = 0;
        private static bool _skillCellSizeInitialized = false;

        /// <summary>
        /// Refreshes the entire Enhancements tab UI
        /// </summary>
        private void RefreshEnhancementsUI()
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            // Update skill points display
            TxtSkillPoints.Text = settings.SkillPoints.ToString();

            // Update XP multiplier display
            var multiplier = App.SkillTree?.GetTotalXpMultiplier() ?? 1.0;
            TxtXpMultiplier.Text = $"{multiplier:F2}x";

            // Update conditioning time display
            TxtConditioningTime.Text = App.SkillTree?.GetFormattedConditioningTime() ?? "0h 0m";

            // Update Pink Rush indicator
            TxtPinkRushIndicator.Visibility = settings.PinkRushActive ? Visibility.Visible : Visibility.Collapsed;

            // Draw the skill tree on canvas
            DrawSkillTree();

            // Update active bonuses panel
            RefreshActiveBonuses();
        }

        /// <summary>
        /// Draws the entire skill tree with nodes and connecting lines
        /// </summary>
        private void DrawSkillTree()
        {
            SkillTreeCanvas.Children.Clear();

            // Set animated background on the outer border
            SkillTreeOuterBorder.Background = CreateAnimatedSkillTreeBrush(isHeader: false);

            // Add sparkle particles behind everything
            AddSkillTreeParticles();
            _skillTreeAnimationsActive = true;

            // Add header section at the start of the canvas
            CreateSkillTreeHeader();

            // 3 LINEAR HORIZONTAL PATHS
            var nodePositions = new Dictionary<string, (double X, double Y)>();

            var startX = 570.0;  // Start after the header section (20 + 500 + 50 margin)
            var startY = 0.0;    // Align with header top
            var colSpacing = 270.0; // Horizontal spacing between nodes
            var rowSpacing = 160.0; // Vertical spacing between the 3 paths

            // COLUMN 0: Root node (centered, branches to 3 paths)
            var rootY = startY + rowSpacing; // Center vertically
            nodePositions["pink_hours"] = (startX, rootY);

            // PATH 1 (TOP ROW): ditzy_data branch
            var path1Y = startY;
            nodePositions["ditzy_data"] = (startX + colSpacing, path1Y);
            nodePositions["hive_mind"] = (startX + colSpacing * 2, path1Y);
            nodePositions["trophy_case"] = (startX + colSpacing * 3, path1Y);
            nodePositions["popular_girl"] = (startX + colSpacing * 4, path1Y);
            nodePositions["quest_refresh"] = (startX + colSpacing * 5, path1Y);
            nodePositions["better_quests"] = (startX + colSpacing * 6, path1Y);

            // PATH 2 (MIDDLE ROW): sparkle_boost_1 branch
            var path2Y = startY + rowSpacing;
            nodePositions["sparkle_boost_1"] = (startX + colSpacing, path2Y);
            nodePositions["sparkle_boost_2"] = (startX + colSpacing * 2, path2Y);
            nodePositions["lucky_bimbo"] = (startX + colSpacing * 3, path2Y);
            nodePositions["sparkle_boost_3"] = (startX + colSpacing * 4, path2Y);
            nodePositions["lucky_bubbles"] = (startX + colSpacing * 5, path2Y);
            nodePositions["pink_rush"] = (startX + colSpacing * 6, path2Y);

            // PATH 3 (BOTTOM ROW): good_girl_streak branch
            var path3Y = startY + rowSpacing * 2;
            nodePositions["good_girl_streak"] = (startX + colSpacing, path3Y);
            nodePositions["milestone_rewards"] = (startX + colSpacing * 2, path3Y);
            nodePositions["oopsie_insurance"] = (startX + colSpacing * 3, path3Y);
            nodePositions["streak_power"] = (startX + colSpacing * 4, path3Y);
            nodePositions["reroll_addict"] = (startX + colSpacing * 5, path3Y);
            nodePositions["perfect_bimbo_week"] = (startX + colSpacing * 6, path3Y);

            // Draw connection lines first (so they're behind nodes)
            DrawConnectionLines(nodePositions);

            // Draw skill nodes (excluding secret skills)
            foreach (var skill in Models.SkillDefinition.All.Where(s => !s.IsSecret))
            {
                if (nodePositions.TryGetValue(skill.Id, out var pos))
                {
                    var node = CreateSkillNode(skill);
                    Canvas.SetLeft(node, pos.X);
                    Canvas.SetTop(node, pos.Y);
                    SkillTreeCanvas.Children.Add(node);
                }
            }
        }

        /// <summary>
        /// Creates the header panel at the start of the skill tree
        /// </summary>
        private void CreateSkillTreeHeader()
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            // Main header border
            var headerBorder = new Border
            {
                Width = 500,
                Background = CreateAnimatedSkillTreeBrush(isHeader: true),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(15, 8, 15, 15) // Left, Top, Right, Bottom
            };
            Canvas.SetLeft(headerBorder, 5);
            Canvas.SetTop(headerBorder, 0);

            var mainStack = new StackPanel();

            // Title section
            var titleStack = new StackPanel { Margin = new Thickness(0, 0, 0, 15) };
            titleStack.Children.Add(new TextBlock
            {
                Text = "✨ " + (App.Mods?.GetEnhancementTreeTitle() ?? Loc.Get("label_enhancement_tree_title")),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4")),
                FontSize = 22,
                FontWeight = FontWeights.Bold
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = App.Mods?.GetEnhancementTreeSubtitle() ?? Loc.Get("label_enhancement_tree_subtitle"),
                Foreground = new SolidColorBrush(Color.FromRgb(176, 176, 176)),
                FontSize = 11,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 4, 0, 0)
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = App.Mods?.GetEnhancementTreeWarning() ?? Loc.Get("label_enhancement_tree_warning"),
                Foreground = new SolidColorBrush(Color.FromRgb(136, 170, 204)),
                FontSize = 10,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 2, 0, 0)
            });
            mainStack.Children.Add(titleStack);

            // Sparkle Points display
            var pointsBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(42, 42, 74)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(15, 10, 15, 10),
                Margin = new Thickness(0, 0, 0, 15)
            };
            var pointsStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            pointsStack.Children.Add(new TextBlock
            {
                Text = "💎",
                FontSize = 24,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            });
            var pointsInfoStack = new StackPanel();
            pointsInfoStack.Children.Add(new TextBlock
            {
                Text = App.Mods?.GetPointsLabel() ?? Loc.Get("label_sparkle_points"),
                Foreground = new SolidColorBrush(Color.FromRgb(176, 176, 176)),
                FontSize = 10
            });
            pointsInfoStack.Children.Add(new TextBlock
            {
                Text = settings.SkillPoints.ToString(),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4")),
                FontSize = 24,
                FontWeight = FontWeights.Bold
            });
            pointsStack.Children.Add(pointsInfoStack);
            pointsBorder.Child = pointsStack;
            mainStack.Children.Add(pointsBorder);

            // Ditzy Data Stats Toggle Button (only show if ditzy_data skill is unlocked)
            var hasDitzyData = App.SkillTree?.HasSkill("ditzy_data") == true;
            var ditzyButton = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(60, 40, 80)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 10),
                Cursor = Cursors.Hand,
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4")),
                BorderThickness = new Thickness(1)
            };
            var ditzyButtonStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var ditzyArrow = new TextBlock
            {
                Text = " ▼",
                Foreground = new SolidColorBrush(Color.FromRgb(176, 176, 176)),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            ditzyButtonStack.Children.Add(new TextBlock
            {
                Text = "📊 ",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
            ditzyButtonStack.Children.Add(new TextBlock
            {
                Text = App.Mods?.GetStatsTitle() ?? Loc.Get("label_ditzy_data_stats"),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentLightColorHex() ?? "#FFB6C1")),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });
            ditzyButtonStack.Children.Add(ditzyArrow);
            ditzyButton.Child = ditzyButtonStack;

            // Detailed Stats Box (initially hidden)
            var detailedStatsBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(22, 22, 42)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 15),
                Visibility = Visibility.Collapsed // Start hidden
            };
            var detailedStatsStack = new StackPanel();

            // Toggle click handler
            ditzyButton.MouseLeftButtonDown += (s, e) =>
            {
                var isCollapsed = detailedStatsBorder.Visibility == Visibility.Collapsed;
                detailedStatsBorder.Visibility = isCollapsed ? Visibility.Visible : Visibility.Collapsed;
                ditzyArrow.Text = isCollapsed ? " ▲" : " ▼";
            };
            if (hasDitzyData)
                mainStack.Children.Add(ditzyButton);

            // Stats title
            detailedStatsStack.Children.Add(new TextBlock
            {
                Text = "📊 " + (App.Mods?.GetStatsTitle() ?? "Ditzy Data Stats"),
                Foreground = new SolidColorBrush(Color.FromRgb(176, 176, 176)),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var achievements = App.Achievements?.Progress;
            if (achievements != null)
            {
                // Create a grid for stats layout (3 columns)
                var statsGrid = new Grid();
                statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                int row = 0;
                void AddStatRow(string label, string value, int column)
                {
                    var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
                    stack.Children.Add(new TextBlock
                    {
                        Text = label,
                        Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)),
                        FontSize = 9
                    });
                    stack.Children.Add(new TextBlock
                    {
                        Text = value,
                        Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                        FontSize = 10,
                        FontWeight = FontWeights.Bold
                    });
                    Grid.SetColumn(stack, column);
                    Grid.SetRow(stack, row);
                    statsGrid.Children.Add(stack);
                }

                // Row 1: Session stats
                statsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                AddStatRow(Loc.Get("label_sessions_started"), achievements.TotalSessionsStarted.ToString("N0"), 0);
                AddStatRow(Loc.Get("label_sessions_completed"), achievements.CompletedSessions.Count.ToString("N0"), 1);
                AddStatRow(Loc.Get("label_sessions_abandoned"), achievements.TotalSessionsAbandoned.ToString("N0"), 2);
                row++;

                // Row 2: XP & Skill Points
                statsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                AddStatRow(Loc.Get("label_total_xp_earned_stat"), achievements.TotalXPEarned.ToString("N0"), 0);
                AddStatRow(Loc.Get("label_skill_points_earned"), achievements.TotalSkillPointsEarned.ToString("N0"), 1);
                AddStatRow(Loc.Get("label_longest_session"), $"{achievements.LongestSessionMinutes:F1} {Loc.Get("label_min_abbrev")}", 2);
                row++;

                // Row 3: Attention checks
                statsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                AddStatRow(Loc.Get("label_attention_passes"), achievements.TotalAttentionChecksPassed.ToString("N0"), 0);
                AddStatRow(Loc.Get("label_video_att_passed"), achievements.VideoAttentionChecksPassed.ToString("N0"), 1);
                AddStatRow(Loc.Get("label_video_att_failed"), achievements.VideoAttentionChecksFailed.ToString("N0"), 2);
                row++;

                // Row 4: Bubble count
                statsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                AddStatRow(Loc.Get("label_bubble_count_games"), achievements.TotalBubbleCountGames.ToString("N0"), 0);
                AddStatRow(Loc.Get("label_bc_correct"), achievements.TotalBubbleCountCorrect.ToString("N0"), 1);
                AddStatRow(Loc.Get("label_bc_best_streak"), achievements.BubbleCountBestStreak.ToString("N0"), 2);
                row++;

                // Row 5: Content consumption
                statsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                AddStatRow(Loc.Get("label_total_flashes_stat"), achievements.TotalFlashImages.ToString("N0"), 0);
                AddStatRow(Loc.Get("label_bubbles_popped_stat"), achievements.TotalBubblesPopped.ToString("N0"), 1);
                AddStatRow(Loc.Get("label_lock_cards_done"), achievements.TotalLockCardsCompleted.ToString("N0"), 2);
                row++;

                // Row 6: Time stats
                statsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var videoMin = achievements.TotalVideoMinutes;
                var videoTimeStr = videoMin >= 60 ? $"{videoMin / 60:F1} {Loc.Get("label_hrs")}" : $"{videoMin:F1} {Loc.Get("label_min_abbrev")}";
                AddStatRow(Loc.Get("label_video_time"), videoTimeStr, 0);
                var pinkMin = achievements.TotalPinkFilterMinutes;
                var pinkTimeStr = pinkMin >= 60 ? $"{pinkMin / 60:F1} {Loc.Get("label_hrs")}" : $"{pinkMin:F1} {Loc.Get("label_min_abbrev")}";
                AddStatRow(Loc.Get("label_pink_filter_time"), pinkTimeStr, 1);
                var spiralMin = achievements.TotalSpiralMinutes;
                var spiralTimeStr = spiralMin >= 60 ? $"{spiralMin / 60:F1} {Loc.Get("label_hrs")}" : $"{spiralMin:F1} {Loc.Get("label_min_abbrev")}";
                AddStatRow(Loc.Get("label_spiral_time"), spiralTimeStr, 2);
                row++;

                // Row 7: Misc stats
                statsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                AddStatRow(Loc.Get("label_consecutive_days"), achievements.ConsecutiveDays.ToString("N0"), 0);

                detailedStatsStack.Children.Add(statsGrid);
            }

            detailedStatsBorder.Child = detailedStatsStack;
            if (hasDitzyData)
                mainStack.Children.Add(detailedStatsBorder);

            // Stats section
            var statsBorder = new Border
            {
                Background = Application.Current.Resources["SurfaceBgBrush"] as SolidColorBrush ?? new SolidColorBrush(Color.FromRgb(30, 30, 58)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12)
            };
            var statsStack = new StackPanel();

            // XP Mult
            var multiplier = App.SkillTree?.GetTotalXpMultiplier() ?? 1.0;
            var xpStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };
            xpStack.Children.Add(new TextBlock
            {
                Text = Loc.Get("label_xp_mult"),
                Foreground = new SolidColorBrush(Color.FromRgb(176, 176, 176)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
            xpStack.Children.Add(new TextBlock
            {
                Text = $"{multiplier:F2}x",
                Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 136)),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });
            if (settings.PinkRushActive)
            {
                xpStack.Children.Add(new TextBlock
                {
                    Text = " " + Loc.Get("label_xp_rush"),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentDarkColorHex() ?? "#FF1493")),
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            statsStack.Children.Add(xpStack);

            // Time
            var conditioningTime = App.SkillTree?.GetFormattedConditioningTime() ?? "0h 0m";
            var timeStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };
            timeStack.Children.Add(new TextBlock
            {
                Text = "⏱️ ",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
            timeStack.Children.Add(new TextBlock
            {
                Text = conditioningTime,
                Foreground = new SolidColorBrush(Color.FromRgb(176, 176, 176)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
            statsStack.Children.Add(timeStack);

            statsBorder.Child = statsStack;
            mainStack.Children.Add(statsBorder);

            // Active Bonuses Section
            var breakdown = App.SkillTree?.GetMultiplierBreakdown() ?? new List<(string, double)>();
            if (breakdown.Count > 1) // Only show if there are bonuses beyond base
            {
                var bonusesTitle = new TextBlock
                {
                    Text = "Active Bonuses:",
                    Foreground = new SolidColorBrush(Color.FromRgb(176, 176, 176)),
                    FontSize = 11,
                    Margin = new Thickness(0, 15, 0, 8)
                };
                mainStack.Children.Add(bonusesTitle);

                var bonusesWrap = new WrapPanel { Orientation = Orientation.Horizontal };
                foreach (var (source, value) in breakdown)
                {
                    if (source == "Base") continue; // Don't show base multiplier

                    var chip = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(60, 40, 80)),
                        CornerRadius = new CornerRadius(12),
                        Padding = new Thickness(10, 5, 10, 5),
                        Margin = new Thickness(0, 0, 8, 8)
                    };

                    chip.Child = new TextBlock
                    {
                        Text = $"{source}: +{value:P0}",
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentLightColorHex() ?? "#FFB6C1")),
                        FontSize = 11
                    };

                    bonusesWrap.Children.Add(chip);
                }
                mainStack.Children.Add(bonusesWrap);
            }

            headerBorder.Child = mainStack;
            SkillTreeCanvas.Children.Add(headerBorder);
        }

        /// <summary>
        /// Creates an animated gradient brush for the skill tree background or header
        /// </summary>
        private LinearGradientBrush CreateAnimatedSkillTreeBrush(bool isHeader)
        {
            var brush = new LinearGradientBrush();
            brush.StartPoint = new Point(0, 0);
            brush.EndPoint = new Point(1, 1);

            if (isHeader)
            {
                // Header: dark purple → vivid purple-pink → dark purple
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(35, 20, 60), 0.0));    // deeper purple edge
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(80, 30, 100), 0.5));   // vivid purple-pink center
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(35, 20, 60), 1.0));    // deeper purple edge

                // Animate middle stop offset: drift 0.2 ↔ 0.8
                var offsetAnim = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0.2,
                    To = 0.8,
                    Duration = TimeSpan.FromSeconds(5),
                    AutoReverse = true,
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                    EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
                };
                brush.GradientStops[1].BeginAnimation(GradientStop.OffsetProperty, offsetAnim);

                // Animate middle stop color: shift between purple tones
                var colorAnim = new System.Windows.Media.Animation.ColorAnimation
                {
                    From = Color.FromRgb(80, 30, 100),   // vivid purple
                    To = Color.FromRgb(120, 40, 90),      // bright magenta-purple
                    Duration = TimeSpan.FromSeconds(4),
                    AutoReverse = true,
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                    EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
                };
                brush.GradientStops[1].BeginAnimation(GradientStop.ColorProperty, colorAnim);
            }
            else
            {
                // Canvas background: deep purple → vivid purple → rich blue-purple → deep purple
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(25, 15, 50), 0.0));    // deep purple
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(60, 25, 80), 0.3));    // vivid purple
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(30, 35, 75), 0.7));    // rich blue-purple
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(25, 15, 50), 1.0));    // deep purple

                // Animate stop[1] offset: drift 0.15 ↔ 0.5
                var offset1Anim = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0.15,
                    To = 0.5,
                    Duration = TimeSpan.FromSeconds(6),
                    AutoReverse = true,
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                    EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
                };
                brush.GradientStops[1].BeginAnimation(GradientStop.OffsetProperty, offset1Anim);

                // Animate stop[2] offset: drift 0.5 ↔ 0.85
                var offset2Anim = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0.5,
                    To = 0.85,
                    Duration = TimeSpan.FromSeconds(8),
                    AutoReverse = true,
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                    EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
                };
                brush.GradientStops[2].BeginAnimation(GradientStop.OffsetProperty, offset2Anim);

                // Animate stop[1] color: shift between purple and blue tones
                var colorAnim = new System.Windows.Media.Animation.ColorAnimation
                {
                    From = Color.FromRgb(60, 25, 80),    // vivid purple
                    To = Color.FromRgb(35, 40, 90),       // bright blue
                    Duration = TimeSpan.FromSeconds(7),
                    AutoReverse = true,
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                    EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
                };
                brush.GradientStops[1].BeginAnimation(GradientStop.ColorProperty, colorAnim);
            }

            return brush;
        }

        /// <summary>
        /// Adds floating sparkle particles to the skill tree canvas background
        /// </summary>
        private void AddSkillTreeParticles()
        {
            var colors = new[]
            {
                Color.FromArgb(90, 255, 105, 180),   // pink
                Color.FromArgb(80, 180, 130, 255),    // purple
                Color.FromArgb(70, 255, 255, 255),    // white
                Color.FromArgb(100, 255, 182, 193),   // light pink
                Color.FromArgb(85, 200, 160, 255),    // lavender
            };

            for (int i = 0; i < 35; i++)
            {
                var size = 3.0 + Random.Shared.NextDouble() * 5.0; // 3-8px
                var ellipse = new System.Windows.Shapes.Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = new SolidColorBrush(colors[Random.Shared.Next(colors.Length)]),
                    Opacity = 0
                };

                Canvas.SetLeft(ellipse, Random.Shared.NextDouble() * 2400);
                Canvas.SetTop(ellipse, Random.Shared.NextDouble() * 460);
                Canvas.SetZIndex(ellipse, -1);

                // Pulsing opacity animation with random duration and start delay
                var opacityAnim = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromSeconds(2 + Random.Shared.NextDouble() * 3), // 2-5s
                    BeginTime = TimeSpan.FromSeconds(Random.Shared.NextDouble() * 5),     // 0-5s delay
                    AutoReverse = true,
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                    EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
                };
                ellipse.BeginAnimation(System.Windows.UIElement.OpacityProperty, opacityAnim);

                SkillTreeCanvas.Children.Add(ellipse);
            }
        }

        /// <summary>
        /// Redirects vertical mouse wheel scrolling to horizontal scrolling for the skill tree
        /// </summary>
        private void SkillTreeScroller_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                // Scroll horizontally instead of vertically
                double offset = scrollViewer.HorizontalOffset - (e.Delta * 0.5);
                scrollViewer.ScrollToHorizontalOffset(offset);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Draws connecting lines between parent and child nodes
        /// </summary>
        private void DrawConnectionLines(Dictionary<string, (double X, double Y)> positions)
        {
            var connections = new List<(string Parent, string Child)>
            {
                // Root branches into 3 paths
                ("pink_hours", "ditzy_data"),
                ("pink_hours", "sparkle_boost_1"),
                ("pink_hours", "good_girl_streak"),

                // PATH 1 (TOP): Linear progression
                ("ditzy_data", "hive_mind"),
                ("hive_mind", "trophy_case"),
                ("trophy_case", "popular_girl"),
                ("popular_girl", "quest_refresh"),
                ("quest_refresh", "better_quests"),

                // PATH 2 (MIDDLE): Linear progression
                ("sparkle_boost_1", "sparkle_boost_2"),
                ("sparkle_boost_2", "lucky_bimbo"),
                ("lucky_bimbo", "sparkle_boost_3"),
                ("sparkle_boost_3", "lucky_bubbles"),
                ("lucky_bubbles", "pink_rush"),

                // PATH 3 (BOTTOM): Linear progression
                ("good_girl_streak", "milestone_rewards"),
                ("milestone_rewards", "oopsie_insurance"),
                ("oopsie_insurance", "streak_power"),
                ("streak_power", "reroll_addict"),
                ("reroll_addict", "perfect_bimbo_week"),
            };

            foreach (var (parent, child) in connections)
            {
                if (positions.TryGetValue(parent, out var parentPos) &&
                    positions.TryGetValue(child, out var childPos))
                {
                    var isParentUnlocked = App.SkillTree?.HasSkill(parent) == true;
                    var isChildUnlocked = App.SkillTree?.HasSkill(child) == true;

                    // Line color based on unlock state
                    var lineColor = isChildUnlocked ? Color.FromRgb(100, 255, 150) :
                                   isParentUnlocked ? (Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4") :
                                   Color.FromRgb(60, 60, 80);

                    // HORIZONTAL LAYOUT: Connect right edge of parent to left edge of child
                    var line = new System.Windows.Shapes.Line
                    {
                        X1 = parentPos.X + NodeWidth,           // Right edge of parent
                        Y1 = parentPos.Y + NodeHeight / 2,      // Vertical center of parent
                        X2 = childPos.X,                        // Left edge of child
                        Y2 = childPos.Y + NodeHeight / 2,       // Vertical center of child
                        Stroke = new SolidColorBrush(lineColor),
                        StrokeThickness = isChildUnlocked ? 3 : 2,
                        Opacity = isParentUnlocked || isChildUnlocked ? 1.0 : 0.3
                    };

                    // Add glow effect for unlocked paths
                    if (isChildUnlocked)
                    {
                        line.Effect = new DropShadowEffect
                        {
                            Color = Colors.LimeGreen,
                            BlurRadius = 8,
                            ShadowDepth = 0,
                            Opacity = 0.6
                        };
                    }

                    SkillTreeCanvas.Children.Add(line);
                }
            }
        }

        /// <summary>
        /// Creates a skill node for the tree canvas with image background support
        /// </summary>
        private Border CreateSkillNode(Models.SkillDefinition skill)
        {
            var isUnlocked = App.SkillTree?.HasSkill(skill.Id) == true;
            var canPurchase = App.SkillTree?.CanPurchaseSkill(skill.Id) == true;
            var hasPrereq = string.IsNullOrEmpty(skill.PrerequisiteId) ||
                           App.SkillTree?.HasSkill(skill.PrerequisiteId) == true;
            var settings = App.Settings?.Current;
            var isLocked = !isUnlocked && !canPurchase;

            // Border color based on state
            Color borderColor;
            if (isUnlocked)
                borderColor = Color.FromRgb(100, 255, 150);
            else if (canPurchase)
                borderColor = (Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4");
            else
                borderColor = Color.FromRgb(60, 50, 70);

            var border = new Border
            {
                CornerRadius = new CornerRadius(10),
                Width = NodeWidth,
                Height = NodeHeight,
                Cursor = canPurchase ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow,
                Tag = skill.Id,
                ClipToBounds = true,
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1.0, 1.0)
            };

            // Add glow effect for unlocked or purchasable nodes
            if (isUnlocked)
            {
                border.Effect = new DropShadowEffect
                {
                    Color = Colors.LimeGreen,
                    BlurRadius = 18,
                    ShadowDepth = 0,
                    Opacity = 0.6
                };
            }
            else if (canPurchase)
            {
                border.Effect = new DropShadowEffect
                {
                    Color = Colors.HotPink,
                    BlurRadius = 15,
                    ShadowDepth = 0,
                    Opacity = 0.7
                };
            }

            // Hover animation - scale up with pop effect
            border.MouseEnter += (s, e) =>
            {
                var scaleTransform = border.RenderTransform as ScaleTransform;
                if (scaleTransform != null)
                {
                    Canvas.SetZIndex(border, 10); // bring to front while hovered
                    var anim = new System.Windows.Media.Animation.DoubleAnimation
                    {
                        To = 1.25,
                        Duration = TimeSpan.FromMilliseconds(250),
                        EasingFunction = new System.Windows.Media.Animation.BackEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut, Amplitude = 0.4 }
                    };
                    scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                    scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
                }
            };

            border.MouseLeave += (s, e) =>
            {
                var scaleTransform = border.RenderTransform as ScaleTransform;
                if (scaleTransform != null)
                {
                    Canvas.SetZIndex(border, 0); // restore z-order
                    var anim = new System.Windows.Media.Animation.DoubleAnimation
                    {
                        To = 1.0,
                        Duration = TimeSpan.FromMilliseconds(200),
                        EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
                    };
                    scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                    scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
                }
            };

            // Click handler
            if (canPurchase)
            {
                border.MouseLeftButtonUp += SkillCard_Click;
            }

            // Tooltip
            var tooltipStack = new StackPanel { MaxWidth = 280 };
            tooltipStack.Children.Add(new TextBlock
            {
                Text = App.Mods?.MakeModAware(skill.FlavorText) ?? skill.LocalizedFlavorText,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentLightColorHex() ?? "#FFB6C1")),
                FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            });
            tooltipStack.Children.Add(new TextBlock
            {
                Text = App.Mods?.MakeModAware(skill.Description) ?? skill.LocalizedDescription,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap
            });
            if (!string.IsNullOrEmpty(skill.PrerequisiteId) && !hasPrereq)
            {
                var prereqSkill = Models.SkillDefinition.All.FirstOrDefault(s => s.Id == skill.PrerequisiteId);
                tooltipStack.Children.Add(new TextBlock
                {
                    Text = Loc.GetF("label_skill_requires", prereqSkill?.LocalizedName ?? skill.PrerequisiteId),
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
                    Margin = new Thickness(0, 6, 0, 0)
                });
            }

            border.ToolTip = new ToolTip
            {
                Content = tooltipStack,
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 50)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4")),
                Padding = new Thickness(10)
            };

            // Main content grid: image, name label, gap, button
            var contentGrid = new Grid();
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(86) }); // Row 0: Image area
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) }); // Row 1: Skill name
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3) });  // Row 2: Gap
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) }); // Row 3: Button area

            // Row 0: Image (blurred if locked)
            bool imageLoaded = false;

            // Try to load skill image (will support individual files like skills/hive_mind.png)
            try
            {
                var skillImageSource = Services.ModResourceResolver.ResolveImage($"skills/{skill.Id}.png");
                var skillImage = new System.Windows.Controls.Image
                {
                    Source = skillImageSource,
                    Stretch = Stretch.UniformToFill
                };

                // Blur effect if locked
                if (isLocked)
                {
                    skillImage.Effect = new System.Windows.Media.Effects.BlurEffect
                    {
                        Radius = 8
                    };
                }

                Grid.SetRow(skillImage, 0);
                contentGrid.Children.Add(skillImage);
                imageLoaded = true;
            }
            catch
            {
                // Fallback to gradient placeholder
                var imagePlaceholder = new Border
                {
                    Background = CreateSkillPlaceholderGradient(skill.Tier),
                    CornerRadius = new CornerRadius(8, 8, 0, 0)
                };

                // Blur gradient if locked
                if (isLocked)
                {
                    imagePlaceholder.Effect = new System.Windows.Media.Effects.BlurEffect
                    {
                        Radius = 8
                    };
                }

                Grid.SetRow(imagePlaceholder, 0);
                contentGrid.Children.Add(imagePlaceholder);
            }

            // Row 1: Skill name label
            var nameLabel = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 28, 45)),
                Child = new TextBlock
                {
                    Text = App.Mods?.MakeModAware(skill.Name) ?? skill.LocalizedName,
                    Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 210)),
                    FontSize = 9.5,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            };
            Grid.SetRow(nameLabel, 1);
            contentGrid.Children.Add(nameLabel);

            // Row 3: Cost/Status Button
            var buttonBg = isUnlocked ? Color.FromRgb(100, 255, 150) :
                          canPurchase ? (Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4") :
                          Color.FromRgb(40, 35, 50);

            var buttonText = isUnlocked ? $"💎{skill.Cost} {Loc.Get("label_skill_owned")}" :
                            canPurchase ? $"💎 {skill.Cost}" :
                            $"🔒 {skill.Cost}";

            var buttonTextColor = isUnlocked ? Color.FromRgb(20, 20, 30) :
                                 canPurchase ? Colors.White :
                                 Color.FromRgb(120, 120, 130);

            var statusButton = new Border
            {
                Background = new SolidColorBrush(buttonBg),
                CornerRadius = new CornerRadius(0, 0, 8, 8),
                Child = new TextBlock
                {
                    Text = buttonText,
                    Foreground = new SolidColorBrush(buttonTextColor),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            Grid.SetRow(statusButton, 3);  // Row 3 (after gap)
            contentGrid.Children.Add(statusButton);

            border.Child = contentGrid;
            return border;
        }

        /// <summary>
        /// Creates a placeholder gradient for skill nodes based on tier
        /// </summary>
        private LinearGradientBrush CreateSkillPlaceholderGradient(int tier)
        {
            // Different color schemes per tier for visual distinction
            var (startColor, endColor) = tier switch
            {
                1 => (Color.FromRgb(80, 50, 100), Color.FromRgb(50, 30, 70)),   // Purple - Foundation
                2 => (Color.FromRgb(100, 50, 80), Color.FromRgb(60, 30, 50)),   // Pink - Core
                3 => (Color.FromRgb(80, 60, 100), Color.FromRgb(45, 35, 65)),   // Deep Purple - Specialization
                4 => (Color.FromRgb(100, 40, 90), Color.FromRgb(55, 25, 50)),   // Hot Pink - Mastery
                _ => (Color.FromRgb(60, 40, 80), Color.FromRgb(35, 25, 50))     // Default
            };

            return new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint = new System.Windows.Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(startColor, 0),
                    new GradientStop(endColor, 1)
                }
            };
        }

        /// <summary>
        /// Determines the cell dimensions of the skill grid images
        /// </summary>
        private static (int cellWidth, int cellHeight) GetSkillGridCellSize()
        {
            try
            {
                var resolvedImg = Services.ModResourceResolver.ResolveImage("skills1.png");
                var bitmap = resolvedImg as BitmapImage ?? new BitmapImage(new Uri("pack://application:,,,/Resources/skills1.png", UriKind.Absolute));

                // Grid is 3 columns × 2 rows
                int cellWidth = bitmap.PixelWidth / 3;
                int cellHeight = bitmap.PixelHeight / 2;

                return (cellWidth, cellHeight);
            }
            catch
            {
                // Fallback if image doesn't load
                return (0, 0);
            }
        }

        /// <summary>
        /// Maps skill IDs to their source image and crop coordinates
        /// </summary>
        private (string? imageFile, Int32Rect cropRect) GetSkillImageCrop(string skillId)
        {
            // Initialize cell dimensions if not already done
            if (!_skillCellSizeInitialized)
            {
                (_skillCellWidth, _skillCellHeight) = GetSkillGridCellSize();
                _skillCellSizeInitialized = true;
            }

            // If dimensions couldn't be determined, return null
            if (_skillCellWidth == 0 || _skillCellHeight == 0)
                return (null, new Int32Rect(0, 0, 0, 0));

            var mapping = new Dictionary<string, (string file, int col, int row)>
            {
                // skills1.png
                ["hive_mind"] = ("skills1.png", 0, 0),
                ["trophy_case"] = ("skills1.png", 1, 0),
                ["sparkle_boost_2"] = ("skills1.png", 2, 0),
                ["lucky_bimbo"] = ("skills1.png", 0, 1),
                ["milestone_rewards"] = ("skills1.png", 1, 1),
                ["oopsie_insurance"] = ("skills1.png", 2, 1),

                // skills2.png
                ["popular_girl"] = ("skills2.png", 0, 0),
                ["quest_refresh"] = ("skills2.png", 1, 0),
                ["better_quests"] = ("skills2.png", 2, 0),
                ["sparkle_boost_3"] = ("skills2.png", 0, 1),
                ["lucky_bubbles"] = ("skills2.png", 1, 1),
                ["pink_rush"] = ("skills2.png", 2, 1),

                // skills3.png
                ["streak_power"] = ("skills3.png", 0, 0),
                ["reroll_addict"] = ("skills3.png", 1, 0),
                ["perfect_bimbo_week"] = ("skills3.png", 2, 0),
                ["night_shift"] = ("skills3.png", 0, 1),
                ["early_bird_bimbo"] = ("skills3.png", 1, 1),
                ["eternal_doll"] = ("skills3.png", 2, 1),
            };

            if (mapping.TryGetValue(skillId, out var info))
            {
                int x = info.col * _skillCellWidth;
                int y = info.row * _skillCellHeight;
                return (info.file, new Int32Rect(x, y, _skillCellWidth, _skillCellHeight));
            }

            return (null, new Int32Rect(0, 0, 0, 0));
        }

        /// <summary>
        /// Populates the secret skills panel
        /// </summary>
        private void PopulateSecretSkills()
        {
            // DISABLED: Secret skills panel removed from UI
            return;
            // SecretSkills.Children.Clear();
            var secrets = Models.SkillDefinition.All.Where(s => s.IsSecret).ToList();

            foreach (var skill in secrets)
            {
                var isAvailable = App.SkillTree?.IsSecretSkillAvailable(skill.Id) == true;
                var isUnlocked = App.SkillTree?.HasSkill(skill.Id) == true;

                // Show hidden card if not available, actual card if available
                if (isAvailable || isUnlocked)
                {
                    // SecretSkills.Children.Add(CreateSecretSkillCard(skill));
                }
                else
                {
                    // SecretSkills.Children.Add(CreateHiddenSecretCard(skill));
                }
            }
        }

        /// <summary>
        /// Creates a hidden secret skill card showing only the requirement hint
        /// </summary>
        private Border CreateHiddenSecretCard(Models.SkillDefinition skill)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 20, 40)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(80, 60, 100)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Width = 140,
                Height = 100,
                Margin = new Thickness(5),
                Padding = new Thickness(8),
                Opacity = 0.6
            };

            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

            stack.Children.Add(new TextBlock
            {
                Text = "🔒",
                FontSize = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 5)
            });

            stack.Children.Add(new TextBlock
            {
                Text = "???",
                Foreground = new SolidColorBrush(Color.FromRgb(153, 50, 204)),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            stack.Children.Add(new TextBlock
            {
                Text = skill.SecretRequirementDesc ?? "Unknown requirement",
                Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                FontSize = 9,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            });

            border.Child = stack;
            return border;
        }

        /// <summary>
        /// Creates a secret skill card (revealed but maybe not purchased)
        /// </summary>
        private Border CreateSecretSkillCard(Models.SkillDefinition skill)
        {
            var settings = App.Settings?.Current;
            var isUnlocked = App.SkillTree?.HasSkill(skill.Id) == true;
            var canPurchase = App.SkillTree?.CanPurchaseSkill(skill.Id) == true;

            Color bgColor, borderColor;
            if (isUnlocked)
            {
                bgColor = Color.FromRgb(40, 30, 50);
                borderColor = Color.FromRgb(180, 100, 255);
            }
            else if (canPurchase)
            {
                bgColor = Color.FromRgb(50, 30, 60);
                borderColor = Color.FromRgb(153, 50, 204);
            }
            else
            {
                bgColor = Color.FromRgb(35, 25, 45);
                borderColor = Color.FromRgb(100, 70, 130);
            }

            var border = new Border
            {
                Background = new SolidColorBrush(bgColor),
                BorderBrush = new SolidColorBrush(borderColor),
                BorderThickness = new Thickness(isUnlocked ? 2 : 1),
                CornerRadius = new CornerRadius(8),
                Width = 140,
                Height = 100,
                Margin = new Thickness(5),
                Padding = new Thickness(8),
                Cursor = canPurchase ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow,
                Tag = skill.Id
            };

            if (isUnlocked)
            {
                border.Effect = new DropShadowEffect
                {
                    Color = Colors.Purple,
                    BlurRadius = 12,
                    ShadowDepth = 0,
                    Opacity = 0.5
                };
            }
            else if (canPurchase)
            {
                border.Effect = new DropShadowEffect
                {
                    Color = Colors.MediumPurple,
                    BlurRadius = 10,
                    ShadowDepth = 0,
                    Opacity = 0.4
                };
            }

            if (canPurchase)
            {
                border.MouseLeftButtonUp += SkillCard_Click;
            }

            // Tooltip
            var tooltipStack = new StackPanel { MaxWidth = 280 };
            tooltipStack.Children.Add(new TextBlock
            {
                Text = App.Mods?.MakeModAware(skill.FlavorText) ?? skill.LocalizedFlavorText,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 150, 255)),
                FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            });
            tooltipStack.Children.Add(new TextBlock
            {
                Text = App.Mods?.MakeModAware(skill.Description) ?? skill.LocalizedDescription,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap
            });

            border.ToolTip = new ToolTip
            {
                Content = tooltipStack,
                Background = new SolidColorBrush(Color.FromRgb(40, 25, 55)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(153, 50, 204)),
                Padding = new Thickness(10)
            };

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            stack.Children.Add(new Image
            {
                Source = Helpers.EmojiImage.Get(skill.Icon),
                Width = 22,
                Height = 22,
                Stretch = System.Windows.Media.Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 3)
            });

            stack.Children.Add(new TextBlock
            {
                Text = App.Mods?.MakeModAware(skill.Name) ?? skill.LocalizedName,
                Foreground = new SolidColorBrush(isUnlocked ? Color.FromRgb(180, 130, 255) : Color.FromRgb(153, 50, 204)),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center
            });

            if (isUnlocked)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = $"💎{skill.Cost} ✓ OWNED",
                    Foreground = new SolidColorBrush(Color.FromRgb(180, 130, 255)),
                    FontSize = 9,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 4, 0, 0)
                });
            }
            else
            {
                var costColor = (settings?.SkillPoints >= skill.Cost)
                    ? Color.FromRgb(255, 215, 0)
                    : Color.FromRgb(120, 120, 120);

                stack.Children.Add(new TextBlock
                {
                    Text = $"💎 {skill.Cost}",
                    Foreground = new SolidColorBrush(costColor),
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 4, 0, 0)
                });
            }

            border.Child = stack;
            return border;
        }

        /// <summary>
        /// Handles clicking on a purchasable skill card
        /// </summary>
        private async void SkillCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is string skillId)
            {
                var skill = Models.SkillDefinition.All.FirstOrDefault(s => s.Id == skillId);
                if (skill == null) return;

                // Show confirmation dialog
                var skillName = App.Mods?.MakeModAware(skill.Name) ?? skill.LocalizedName;
                var pointsLabel = (App.Mods?.GetPointsLabel() ?? Loc.Get("label_sparkle_points")).ToLower();
                var flavorText = App.Mods?.MakeModAware(skill.FlavorText) ?? skill.LocalizedFlavorText;
                var descText = App.Mods?.MakeModAware(skill.Description) ?? skill.LocalizedDescription;
                var result = MessageBox.Show(
                    Loc.GetF("msg_purchase_skill", skillName, skill.Cost, pointsLabel, flavorText, descText),
                    Loc.Get("dialog_purchase_enhancement"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // Disable the card during purchase to prevent double-clicks
                    border.IsEnabled = false;
                    try
                    {
                        var (success, error) = await (App.SkillTree?.PurchaseSkillAsync(skillId)
                            ?? Task.FromResult((false, (string?)"Skill tree unavailable")));

                        if (success)
                        {
                            // Show celebration
                            App.Flash?.PlayRandomSound();

                            // Update Trophy Case columns if trophy_case was purchased
                            if (skillId == "trophy_case")
                            {
                                UpdateTrophyCaseColumns();
                            }

                            App.Logger?.Information("Skill purchased via UI: {SkillId}", skillId);
                        }
                        else if (!string.IsNullOrEmpty(error))
                        {
                            MessageBox.Show(error, Loc.Get("dialog_purchase_failed"), MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                    finally
                    {
                        border.IsEnabled = true;
                        RefreshEnhancementsUI();
                    }
                }
            }
        }

        /// <summary>
        /// Refreshes the active bonuses panel showing current skill effects
        /// </summary>
        private void RefreshActiveBonuses()
        {
            var breakdown = App.SkillTree?.GetMultiplierBreakdown() ?? new List<(string, double)>();

            if (breakdown.Count <= 1) // Only base
            {
                ActiveBonusesPanel.Visibility = Visibility.Collapsed;
                return;
            }

            ActiveBonusesPanel.Visibility = Visibility.Visible;
            ActiveBonusesList.Children.Clear();

            foreach (var (source, value) in breakdown)
            {
                if (source == "Base") continue; // Don't show base multiplier

                var chip = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(60, 40, 80)),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(10, 5, 10, 5),
                    Margin = new Thickness(0, 0, 8, 8)
                };

                chip.Child = new TextBlock
                {
                    Text = $"{source}: +{value:P0}",
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentLightColorHex() ?? "#FFB6C1")),
                    FontSize = 11
                };

                ActiveBonusesList.Children.Add(chip);
            }
        }

        /// <summary>
        /// Called when skill tree service fires Pink Rush events
        /// </summary>
        private void OnPinkRushStarted(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                TxtPinkRushIndicator.Visibility = Visibility.Visible;

                // Full-screen pink flash effect
                try
                {
                    var flashWindow = new Window
                    {
                        WindowStyle = WindowStyle.None,
                        AllowsTransparency = true,
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 0xFF, 0x14, 0x93)),
                        Topmost = true,
                        ShowInTaskbar = false,
                        ShowActivated = false,
                        Left = SystemParameters.VirtualScreenLeft,
                        Top = SystemParameters.VirtualScreenTop,
                        Width = SystemParameters.VirtualScreenWidth,
                        Height = SystemParameters.VirtualScreenHeight,
                        IsHitTestVisible = false,
                        Focusable = false,
                        Opacity = 0.6
                    };
                    flashWindow.Show();

                    var fadeOut = new DoubleAnimation(0.6, 0, TimeSpan.FromMilliseconds(500));
                    fadeOut.Completed += (s, args) =>
                    {
                        try { flashWindow.Close(); } catch { }
                    };
                    flashWindow.BeginAnimation(Window.OpacityProperty, fadeOut);
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Pink Rush flash effect failed: {Error}", ex.Message);
                }

                // Show toast notification popup
                try
                {
                    _pinkRushPopup?.Close();
                }
                catch { }

                _pinkRushPopup = new PinkRushPopup();
                _pinkRushPopup.Show();
                App.Logger?.Information("Pink Rush activated! Showing popup.");
            });
        }

        private void OnPinkRushEnded(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                TxtPinkRushIndicator.Visibility = Visibility.Collapsed;

                try
                {
                    _pinkRushPopup?.Close();
                }
                catch { }
                _pinkRushPopup = null;
            });
        }

        private void OnLuckyProc(object? sender, LuckyProcEventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    // Close previous lucky popup if still showing
                    try { _luckyProcPopup?.Close(); } catch { }

                    var isGold = e.ProcType.Contains("Flash");
                    var glowColor = isGold
                        ? System.Windows.Media.Color.FromRgb(0xFF, 0xD7, 0x00)
                        : System.Windows.Media.Color.FromRgb(0xFF, 0x69, 0xB4);

                    var border = new Border
                    {
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xE0, 0x15, 0x15, 0x30)),
                        CornerRadius = new CornerRadius(12),
                        BorderBrush = new SolidColorBrush(glowColor),
                        BorderThickness = new Thickness(2),
                        Padding = new Thickness(20, 12, 20, 12),
                        Effect = new DropShadowEffect
                        {
                            Color = glowColor,
                            BlurRadius = 30,
                            ShadowDepth = 0,
                            Opacity = 0.8
                        }
                    };

                    var stack = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
                    stack.Children.Add(new TextBlock
                    {
                        Text = "LUCKY!",
                        Foreground = new SolidColorBrush(glowColor),
                        FontWeight = FontWeights.Bold,
                        FontSize = 22,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                    });
                    stack.Children.Add(new TextBlock
                    {
                        Text = $"{e.Multiplier}x XP!",
                        Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xB6, 0xC1)),
                        FontSize = 14,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        Margin = new Thickness(0, 4, 0, 0)
                    });

                    border.Child = stack;

                    var popup = new Window
                    {
                        WindowStyle = WindowStyle.None,
                        AllowsTransparency = true,
                        Background = System.Windows.Media.Brushes.Transparent,
                        Topmost = true,
                        ShowInTaskbar = false,
                        ShowActivated = false,
                        SizeToContent = SizeToContent.WidthAndHeight,
                        Content = border
                    };

                    // Position at top-center of primary screen
                    popup.Loaded += (s, args) =>
                    {
                        try
                        {
                            var workArea = SystemParameters.WorkArea;
                            popup.Left = workArea.Left + (workArea.Width - popup.ActualWidth) / 2;
                            popup.Top = workArea.Top + 40;
                        }
                        catch { }
                    };

                    _luckyProcPopup = popup;

                    // Fade in
                    popup.Opacity = 0;
                    popup.Show();

                    var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                    popup.BeginAnimation(Window.OpacityProperty, fadeIn);

                    // Auto-close after 3 seconds with fade-out
                    var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                    closeTimer.Tick += (s, args) =>
                    {
                        closeTimer.Stop();
                        try
                        {
                            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
                            fadeOut.Completed += (s2, args2) =>
                            {
                                try { popup.Close(); } catch { }
                                if (_luckyProcPopup == popup) _luckyProcPopup = null;
                            };
                            popup.BeginAnimation(Window.OpacityProperty, fadeOut);
                        }
                        catch
                        {
                            try { popup.Close(); } catch { }
                        }
                    };
                    closeTimer.Start();
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Lucky proc popup failed: {Error}", ex.Message);
                }
            });
        }

        #endregion




        #region Browser

        private async System.Threading.Tasks.Task InitializeBrowserAsync(string? overrideStartUrl = null)
        {
            if (_browserInitialized) return;

            try
            {
                TxtBrowserStatus.Text = Loc.Get("label_loading");
                TxtBrowserStatus.Foreground = FindResource("PinkBrush") as SolidColorBrush;
                BrowserLoadingText.Text = Loc.Get("label_initializing_webview2");

                // If a previous BrowserService was disposed but the bridge
                // survived, it's still subscribed to the dead service's events
                // and pointing at a dead WebView. Drop it so the BrowserReady
                // handler below re-creates a bridge wired to the new service.
                if (App.BrowserEnhanceBridge != null)
                {
                    try { App.BrowserEnhanceBridge.MatchChanged -= OnBrowserEnhanceMatchChanged; } catch { }
                    try { App.BrowserEnhanceBridge.Dispose(); } catch { }
                    App.BrowserEnhanceBridge = null;
                }

                _browser = new BrowserService();

                // Arm the audio-sync vibe track if the device is connected AFTER the user
                // is already sitting on a HypnoTube page (the natural "open video, then turn
                // the toy on" order). Nav-time injection only fires when already connected, so
                // without this the track would silently never start for that ordering.
                HookHapticAudioSyncRearm();

                _browser.BrowserReady += (s, e) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        TxtBrowserStatus.Text = Loc.Get("label_connected_2");
                        TxtBrowserStatus.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118)); // Green

                        // Now that CoreWebView2 is ready, attach message handler for video end notifications
                        if (_browser?.WebView?.CoreWebView2 != null)
                        {
                            _browser.WebView.CoreWebView2.WebMessageReceived += OnBrowserWebMessageReceived;
                            App.Logger?.Information("Browser WebMessageReceived handler attached");
                        }

                        // Phase 9: wire Deeper auto-discovery onto the WebView.
                        // Discovery is a separate listener so it doesn't interfere
                        // with audio-sync injection above. Bound/Unbound events
                        // drive the inline badge in the browser status row.
                        if (_browser?.WebView != null)
                        {
                            App.DeeperBrowserDiscovery?.Attach(_browser.WebView);
                            if (App.DeeperBrowserDiscovery != null)
                            {
                                App.DeeperBrowserDiscovery.Bound += OnDeeperBrowserBound;
                                App.DeeperBrowserDiscovery.Unbound += OnDeeperBrowserUnbound;
                            }
                        }

                        // Browser Enhancement Bridge: when the user navigates to
                        // a URL we have a saved enhancement for, drive effects on
                        // top of the browser. Toggle ON/OFF via the toolbar.
                        if (_browser?.WebView != null && App.BrowserEnhanceBridge == null)
                        {
                            App.BrowserEnhanceBridge = new Services.Deeper.BrowserEnhancementBridge(_browser.WebView, _browser);
                            App.BrowserEnhanceBridge.MatchChanged += OnBrowserEnhanceMatchChanged;
                        }
                    });
                };
                
                _browser.NavigationCompleted += (s, url) =>
                {
                    Dispatcher.Invoke(async () =>
                    {
                        TxtBrowserStatus.Text = Loc.Get("label_connected_2");
                        TxtBrowserStatus.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118)); // Green

                        // Inject audio sync script when navigating to video sites
                        var audioSyncEnabled = App.Settings.Current.Haptics.AudioSync.Enabled;
                        var hapticsConnected = App.Haptics?.IsConnected == true;
                        var isHypnotube = url.Contains("hypnotube", StringComparison.OrdinalIgnoreCase);

                        App.Logger?.Information("AudioSync check: Enabled={Enabled}, HapticsConnected={Connected}, IsHypnotube={IsHT}, URL={Url}",
                            audioSyncEnabled, hapticsConnected, isHypnotube, url);

                        if (audioSyncEnabled && hapticsConnected && isHypnotube)
                        {
                            App.Logger?.Information("AudioSync: Injecting script for HypnoTube page");
                            await _browser.InjectAudioSyncScriptAsync();
                        }

                        // W3 Piece 1 — fire a catalogue lookup for HT video URLs.
                        // Fully async, fire-and-forget; doesn't block navigation
                        // or anything else. Eligibility is re-checked inside the
                        // service so a non-HT URL just returns InvalidUrl
                        // without hitting the network.
                        TriggerCatalogueLookupForNavigation(url);
                    });
                };

                _browser.FullscreenChanged += (s, isFullscreen) =>
                {
                    Dispatcher.Invoke(() => HandleBrowserFullscreenChanged(isFullscreen));
                };

                // Chromium render/browser process crash. Tear down so the next
                // BrowserSiteToggle click lazy-reinits instead of throwing
                // InvalidOperationException at the user.
                _browser.BrowserProcessFailed += (s, e) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            var dead = _browser?.WebView;
                            if (dead != null) BrowserContainer.Children.Remove(dead);
                            try { (_browser as IDisposable)?.Dispose(); } catch { }
                        }
                        catch (Exception ex) { App.Logger?.Debug("Browser teardown after ProcessFailed: {Error}", ex.Message); }
                        _browser = null;
                        _browserInitialized = false;
                        BrowserLoadingText.Visibility = Visibility.Visible;
                        BrowserLoadingText.Text = "Browser crashed - click a site to restart";
                        TxtBrowserStatus.Text = "Disconnected";
                        TxtBrowserStatus.Foreground = new SolidColorBrush(Color.FromRgb(230, 80, 80));
                    });
                };

                BrowserLoadingText.Text = Loc.Get("label_creating_browser");

                // Navigate directly to the requested URL when lazy-init was triggered by
                // a speech-bubble link click. Otherwise fall back to the mod-appropriate
                // default site. The WebView2's _pendingUrl is the FIRST page Chromium
                // navigates to once CoreWebView2 finishes initializing — if we don't pass
                // the user's URL here, a subsequent Navigate would race the default-URL
                // load and get silently dropped.
                var startUrl = overrideStartUrl ?? App.Mods?.GetDefaultBrowserUrl() ?? "https://bambicloud.com/";
                var webView = await _browser.CreateBrowserAsync(startUrl);

                if (webView != null)
                {
                    BrowserLoadingText.Visibility = Visibility.Collapsed;
                    BrowserContainer.Children.Add(webView);
                    _browserInitialized = true;

                    // Note: WebMessageReceived handler is attached in BrowserReady event
                    // because CoreWebView2 isn't ready until then

                    App.Logger?.Information("Browser initialized - {Site} loaded", startUrl);
                }
                else
                {
                    var errorMsg = Loc.Get("msg_webview2_returned_null");
                    BrowserLoadingText.Text = Loc.GetF("label_0_n_ninstall_webview2_runtime_ngo_microsoft_c", errorMsg);
                    TxtBrowserStatus.Text = Loc.Get("label_error_2");
                    TxtBrowserStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 107));
                    MessageBox.Show(errorMsg, Loc.Get("title_browser_error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (InvalidOperationException invEx)
            {
                BrowserLoadingText.Text = $"❌ {invEx.Message}";
                TxtBrowserStatus.Text = Loc.Get("label_not_installed");
                TxtBrowserStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 107));
                MessageBox.Show(invEx.Message, Loc.Get("title_webview2_not_installed"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (System.Runtime.InteropServices.COMException comEx)
            {
                var errorMsg = Loc.GetF("msg_webview2_com_error_0_1", comEx.Message, comEx.HResult);
                BrowserLoadingText.Text = Loc.Get("label_com_error_install_webview2");
                TxtBrowserStatus.Text = Loc.Get("label_com_error");
                TxtBrowserStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 107));
                MessageBox.Show(errorMsg, Loc.Get("title_webview2_error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (System.DllNotFoundException dllEx)
            {
                var errorMsg = Loc.GetF("msg_webview2_dll_not_found_0", dllEx.Message);
                BrowserLoadingText.Text = Loc.Get("label_missing_dll_install_webview2");
                TxtBrowserStatus.Text = Loc.Get("label_missing_dll");
                TxtBrowserStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 107));
                MessageBox.Show(errorMsg, Loc.Get("title_missing_dll"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                var stack = ex.StackTrace;
                var errorMsg = $"Browser Error:\n\nType: {ex.GetType().Name}\n\nMessage: {ex.Message}\n\nStack: {(stack != null ? stack.Substring(0, Math.Min(500, stack.Length)) : "(none)")}";
                BrowserLoadingText.Text = $"❌ {ex.GetType().Name}\n{ex.Message}";
                TxtBrowserStatus.Text = Loc.Get("label_error_2");
                TxtBrowserStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 107));
                MessageBox.Show(errorMsg, Loc.Get("title_browser_error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BrowserLoadingText_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            await InitializeBrowserAsync();
        }

        private async System.Threading.Tasks.Task InitAndNavigateAsync(string url, bool autoPlayFullscreen)
        {
            // Pass the user's URL as the WebView2 start URL so initialization navigates
            // directly to it. Calling _browser.Navigate(url) right after init silently
            // dropped the call — BrowserService's _isInitialized only flips true inside
            // WebView_Loaded (which runs after we'd return), so the request never reached
            // CoreWebView2 and the start-URL load (BambiCloud) stuck.
            await InitializeBrowserAsync(url);
            if (!_browserInitialized || _browser == null) return;

            // Sync the radio button to the URL we just initialized to so the toggle UI
            // matches the page. Suppress the toggle handler's homepage navigation since
            // the WebView2 is already on its way to the right URL.
            var lowerUrl = url.ToLowerInvariant();
            if (lowerUrl.Contains("bambicloud.com"))
            {
                _skipSiteToggleNavigation = true;
                RbBambiCloud.IsChecked = true;
            }
            else if (lowerUrl.Contains("hypnotube.com"))
            {
                _skipSiteToggleNavigation = true;
                RbHypnoTube.IsChecked = true;
            }
            else
            {
                // External URL — deselect both so re-clicking either fires Checked again
                RbBambiCloud.IsChecked = false;
                RbHypnoTube.IsChecked = false;
            }

            _browser.ZoomFactor = 0.5;

            // Wire one-shot autoplay handler. BrowserService raises NavigationCompleted
            // for the start-URL load, so this catches it without us having to issue a
            // second Navigate. BambiCloud playlists need a different injection (audio,
            // no <video> element) — mirror the branch in NavigateToUrlInBrowser so the
            // first-ever click on a playlist link auto-plays just like subsequent ones.
            if (autoPlayFullscreen)
            {
                var isBambiCloudPlaylist = lowerUrl.Contains("bambicloud.com/playlist/");
                void OnNavCompleted(object? s, string completedUrl)
                {
                    _browser.NavigationCompleted -= OnNavCompleted;
                    if (isBambiCloudPlaylist)
                        _ = AutoPlayBambiCloudPlaylistAsync();
                    else
                        _ = AutoPlayAndFullscreenVideoAsync();
                }
                _browser.NavigationCompleted += OnNavCompleted;
            }

            // Show the Settings tab and bring the window forward
            ShowTab("settings");
            Activate();
            Focus();
        }

        private async void BrowserSiteToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return; // Don't auto-load browser during XAML init

            // Lazy-load browser on first toggle interaction. Pass the URL
            // matching the radio button the user just clicked — without an
            // override, InitializeBrowserAsync defaults to BambiCloud and the
            // first HT click would land on BC, forcing the user to bounce
            // BC→HT to actually get to HT.
            if (!_browserInitialized)
            {
                var initialUrl = RbHypnoTube?.IsChecked == true
                    ? "https://hypnotube.com/"
                    : "https://bambicloud.com/";
                await InitializeBrowserAsync(initialUrl);
                return;
            }
            if (_browser == null) return;

            // Block navigation in offline mode
            if (App.Settings?.Current?.OfflineMode == true) return;

            // Skip navigation if we're already navigating to a specific URL (from speech bubble link)
            if (_skipSiteToggleNavigation)
            {
                _skipSiteToggleNavigation = false;
                return;
            }

            var isBambiCloud = RbBambiCloud.IsChecked == true;
            var url = isBambiCloud
                ? "https://bambicloud.com/"
                : "https://hypnotube.com/";

            // Any property/method touching the WebView2 throws InvalidOperationException
            // if the underlying browser process has crashed. Tear down and lazy-reinit
            // on the next toggle rather than propagating the crash.
            try
            {
                _browser.ZoomFactor = 0.5;
                _browser.Navigate(url);
                App.Logger?.Information("Browser navigated to {Site} (zoom: 50%)",
                    isBambiCloud ? "BambiCloud" : "HypnoTube");
            }
            catch (InvalidOperationException ex)
            {
                App.Logger?.Warning(ex, "WebView2 unusable (browser process likely crashed) - resetting for next toggle");
                try { (_browser as IDisposable)?.Dispose(); } catch { }
                _browser = null;
                _browserInitialized = false;
            }
        }

        /// <summary>
        /// Navigates to a URL in the embedded browser, automatically selecting the correct tab.
        /// Called by speech bubble links in AvatarTubeWindow.
        /// </summary>
        /// <param name="url">The URL to navigate to</param>
        /// <param name="autoPlayFullscreen">If true, auto-plays video and requests fullscreen on the video element</param>
        /// <returns>True if navigation was initiated, false if browser unavailable</returns>
        public bool NavigateToUrlInBrowser(string url, bool autoPlayFullscreen = false)
        {
            // Block navigation in offline mode
            if (App.Settings?.Current?.OfflineMode == true)
            {
                App.Logger?.Debug("Browser navigation blocked in offline mode: {Url}", url);
                return false;
            }

            // Lazy-load browser if not yet initialized
            if (!_browserInitialized)
            {
                _ = InitAndNavigateAsync(url, autoPlayFullscreen);
                return true; // Navigation will happen after init completes
            }

            if (_browser == null)
            {
                App.Logger?.Warning("Browser not available for navigation: {Url}", url);
                return false;
            }

            try
            {
                // Bring window to focus and show the Settings tab (where the browser is)
                ShowTab("settings");
                Activate();
                Focus();

                var lowerUrl = url.ToLowerInvariant();

                // Switch to correct site tab based on URL
                // Set flag to skip the homepage navigation in the toggle handler
                if (lowerUrl.Contains("bambicloud.com") && RbBambiCloud.IsChecked != true)
                {
                    _skipSiteToggleNavigation = true;
                    RbBambiCloud.IsChecked = true;
                }
                else if (lowerUrl.Contains("hypnotube.com") && RbHypnoTube.IsChecked != true)
                {
                    _skipSiteToggleNavigation = true;
                    RbHypnoTube.IsChecked = true;
                }
                else if (!lowerUrl.Contains("bambicloud.com") && !lowerUrl.Contains("hypnotube.com"))
                {
                    // External URL — deselect both radio buttons so clicking either one
                    // fires a Checked event to navigate back (RadioButton.Checked only fires
                    // on false→true transitions, so re-clicking an already-checked button does nothing)
                    RbBambiCloud.IsChecked = false;
                    RbHypnoTube.IsChecked = false;
                }

                _browser.ZoomFactor = 0.5;

                // If auto-play fullscreen requested, set up handler for when navigation completes.
                // BambiCloud playlists are audio (no <video> element, no fullscreen) — they need a
                // different injection that clicks the playlist's main play button.
                if (autoPlayFullscreen && _browser.WebView?.CoreWebView2 != null)
                {
                    var isBambiCloudPlaylist = lowerUrl.Contains("bambicloud.com/playlist/");

                    void OnNavigationCompleted(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
                    {
                        _browser.WebView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;

                        if (e.IsSuccess)
                        {
                            if (isBambiCloudPlaylist)
                                _ = AutoPlayBambiCloudPlaylistAsync();
                            else
                                _ = AutoPlayAndFullscreenVideoAsync();
                        }
                    }

                    _browser.WebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
                }

                // Navigate
                _browser.Navigate(url);

                App.Logger?.Information("Speech link navigated to: {Url} (Site: {Site}, AutoPlay: {AutoPlay})",
                    url, lowerUrl.Contains("bambicloud") ? "BambiCloud" : "HypnoTube", autoPlayFullscreen);

                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Browser navigation failed for URL: {Url}", url);
                return false;
            }
        }

        /// <summary>
        /// Injects JavaScript to find the video element, play it, and request fullscreen.
        /// Also adds handlers for: video ended (exit fullscreen), double-click (exit fullscreen).
        /// Notifies AutonomyService when video playback ends.
        /// </summary>
        private async Task AutoPlayAndFullscreenVideoAsync()
        {
            if (_browser?.WebView?.CoreWebView2 == null) return;

            try
            {
                // Inject audio sync script if enabled
                if (App.Settings.Current.Haptics.AudioSync.Enabled && App.Haptics?.IsConnected == true)
                {
                    await _browser.InjectAudioSyncScriptAsync();
                }

                // Wait a moment for the page to fully render
                await Task.Delay(1500);

                // JavaScript to find video, play it, request fullscreen, and add event handlers
                // Posts message back to C# when video ends or fullscreen exits
                // Retries up to 10 times (5s total) if video element isn't in the DOM yet
                var script = @"
                    (async function() {
                        let video = document.querySelector('video');
                        if (!video) {
                            for (let i = 0; i < 10; i++) {
                                await new Promise(r => setTimeout(r, 500));
                                video = document.querySelector('video');
                                if (video) break;
                            }
                        }
                        if (video) {
                            let notified = false;

                            // Notify C# that video playback ended
                            const notifyVideoEnded = (reason) => {
                                if (!notified) {
                                    notified = true;
                                    window.chrome.webview.postMessage({ type: 'videoEnded', reason: reason });
                                }
                            };

                            // Exit fullscreen helper
                            const exitFullscreen = () => {
                                if (document.exitFullscreen) {
                                    document.exitFullscreen();
                                } else if (document.webkitExitFullscreen) {
                                    document.webkitExitFullscreen();
                                } else if (document.msExitFullscreen) {
                                    document.msExitFullscreen();
                                }
                            };

                            // When video ends, exit fullscreen and notify
                            video.addEventListener('ended', () => {
                                console.log('Video ended, exiting fullscreen');
                                exitFullscreen();
                                notifyVideoEnded('ended');
                            }, { once: true });

                            // Double-click to exit fullscreen and notify
                            video.addEventListener('dblclick', (e) => {
                                if (document.fullscreenElement || document.webkitFullscreenElement) {
                                    console.log('Double-click, exiting fullscreen');
                                    exitFullscreen();
                                    notifyVideoEnded('doubleclick');
                                    e.preventDefault();
                                    e.stopPropagation();
                                }
                            });

                            // Also notify when fullscreen is exited by any means (Escape key, etc.)
                            document.addEventListener('fullscreenchange', () => {
                                if (!document.fullscreenElement && !document.webkitFullscreenElement) {
                                    notifyVideoEnded('fullscreenExit');
                                }
                            }, { once: true });

                            // Notify C# that playback has actually begun so the autonomy
                            // watchdog (30s) can be cancelled — long videos must NOT free
                            // up _webVideoActive while still on screen.
                            const notifyVideoStarted = () => {
                                window.chrome.webview.postMessage({ type: 'videoStarted' });
                            };

                            // Start playing and go fullscreen
                            video.muted = false;
                            video.play().then(() => {
                                notifyVideoStarted();
                                if (video.requestFullscreen) {
                                    video.requestFullscreen();
                                } else if (video.webkitRequestFullscreen) {
                                    video.webkitRequestFullscreen();
                                } else if (video.msRequestFullscreen) {
                                    video.msRequestFullscreen();
                                }
                            }).catch(e => {
                                console.log('Autoplay blocked:', e);
                                // Still notify so the watchdog doesn't fire mid-playback if
                                // the user manually unblocks/plays the video later.
                                video.addEventListener('playing', notifyVideoStarted, { once: true });
                            });
                        } else {
                            console.log('No video element found after retries');
                            window.chrome.webview.postMessage({ type: 'videoEnded', reason: 'noVideoElement' });
                        }
                    })();
                ";

                await _browser.WebView.CoreWebView2.ExecuteScriptAsync(script);
                App.Logger?.Debug("Auto-play and fullscreen script injected with exit handlers");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to auto-play/fullscreen video");
            }
        }

        /// <summary>
        /// BambiCloud playlists are audio (no &lt;video&gt; element). The page renders a single
        /// .play-action button that starts the whole playlist; we click it once it hydrates,
        /// then post videoStarted/videoEnded messages so AutonomyService treats the playlist
        /// like a fullscreen video for blocking purposes.
        /// </summary>
        private async Task AutoPlayBambiCloudPlaylistAsync()
        {
            if (_browser?.WebView?.CoreWebView2 == null) return;

            try
            {
                // Wait for React hydration before looking for the button.
                await Task.Delay(1500);

                var script = @"
                    (async function() {
                        // Poll for the .play-action button - SPA hydration can take a few seconds.
                        let btn = document.querySelector('button.play-action');
                        for (let i = 0; i < 20 && !btn; i++) {
                            await new Promise(r => setTimeout(r, 250));
                            btn = document.querySelector('button.play-action');
                        }
                        if (!btn) {
                            window.chrome.webview.postMessage({ type: 'videoEnded', reason: 'noPlayButton' });
                            return;
                        }

                        let notified = false;
                        const notifyStarted = () => {
                            if (!notified) {
                                notified = true;
                                window.chrome.webview.postMessage({ type: 'videoStarted' });
                            }
                        };
                        const notifyEnded = (reason) => {
                            window.chrome.webview.postMessage({ type: 'videoEnded', reason: reason });
                        };

                        // Bind to any current/future <audio> element so we know when the
                        // playlist actually plays and when the last track ends.
                        const bindAudio = (audio) => {
                            if (!audio || audio.__bcBound) return;
                            audio.__bcBound = true;
                            audio.addEventListener('playing', notifyStarted);
                            audio.addEventListener('ended', () => notifyEnded('ended'));
                        };
                        document.querySelectorAll('audio').forEach(bindAudio);

                        // Also watch for audio elements added later (each track may swap one in).
                        const obs = new MutationObserver(() => {
                            document.querySelectorAll('audio').forEach(bindAudio);
                        });
                        obs.observe(document.body, { childList: true, subtree: true });

                        // Click the play button. Browser autoplay policies usually allow this
                        // because navigation-from-app counts as a user gesture in WebView2.
                        btn.click();

                        // Fallback: if no <audio> 'playing' fires within 3s, assume click took
                        // effect anyway and notify, so the autonomy watchdog doesn't fire.
                        setTimeout(notifyStarted, 3000);
                    })();
                ";

                await _browser.WebView.CoreWebView2.ExecuteScriptAsync(script);
                App.Logger?.Debug("BambiCloud playlist auto-play script injected");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to auto-play BambiCloud playlist");
            }
        }

        /// <summary>
        /// Handles messages from JavaScript in the browser (video ended, fullscreen exit, etc.)
        /// </summary>
        private void OnBrowserWebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                // Use TryGetWebMessageAsString to get the raw JSON (not double-encoded)
                var message = e.TryGetWebMessageAsString();
                if (string.IsNullOrEmpty(message))
                {
                    // Fallback to WebMessageAsJson if string is not available
                    message = e.WebMessageAsJson;
                }

                // Log audio sync messages at Information level for debugging
                if (message.Contains("audioSync"))
                {
                    App.Logger?.Information("AudioSync message received: {Message}", message);
                }
                else
                {
                    App.Logger?.Debug("Browser web message received: {Message}", message);
                }

                // Force-exit our WPF "forced fullscreen" surface — sent by the
                // dblclick / click-pair / fullscreenchange handlers injected
                // into every CCP WebView. Fires the same path Esc/F11 do.
                if (message == "ccp_exit_fullscreen")
                {
                    App.Logger?.Information("MainWindow: ccp_exit_fullscreen received (forced FS active = {Active})", _isBrowserFullscreen);
                    if (_isBrowserFullscreen) ExitBrowserFullscreen();
                    return;
                }

                // Parse the JSON message
                if (message.Contains("\"type\":\"videoStarted\""))
                {
                    // Playback confirmed - cancel the autonomy load-failure watchdog so
                    // long videos can't have _webVideoActive flipped off mid-stream.
                    App.Logger?.Information("Web video playback started");
                    App.Autonomy?.OnWebVideoStarted();
                }
                else if (message.Contains("\"type\":\"videoEnded\""))
                {
                    // Video ended or fullscreen exited - notify AutonomyService
                    App.Logger?.Information("Web video playback ended");
                    App.Autonomy?.OnWebVideoEnded();
                    ExitBrowserFullscreen();
                }
                // Audio sync messages
                else if (message.Contains("\"type\":\"audioSyncVideoDetected\""))
                {
                    App.Logger?.Information("AudioSync: Video detected message received");
                    HandleAudioSyncVideoDetected(message);
                }
                else if (message.Contains("\"type\":\"audioSyncState\""))
                {
                    HandleAudioSyncState(message);
                }
                else if (message.Contains("\"type\":\"audioSyncSeek\""))
                {
                    App.Logger?.Information("AudioSync: Seek message received");
                    HandleAudioSyncSeek(message);
                }
                else if (message.Contains("\"type\":\"audioSyncEnded\""))
                {
                    App.Logger?.Information("AudioSync: Video ended message received");
                    HandleAudioSyncEnded();
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to process browser web message");
            }
        }

        private void HandleAudioSyncVideoDetected(string message)
        {
            if (App.AudioSync == null)
            {
                App.Logger?.Warning("AudioSync: Service is null, cannot process video");
                // Signal ready anyway so video plays (without haptics)
                _ = _browser?.SignalHapticReadyAsync();
                return;
            }

            try
            {
                // Extract URL from message
                var urlMatch = System.Text.RegularExpressions.Regex.Match(message, "\"url\":\"([^\"]+)\"");
                if (urlMatch.Success)
                {
                    var videoUrl = urlMatch.Groups[1].Value;
                    App.Logger?.Information("AudioSync: Starting processing for video URL: {Url}", videoUrl);

                    // Wire up progress events
                    void OnProgress(object? sender, Services.Audio.ChunkProgressEventArgs e)
                    {
                        Dispatcher.BeginInvoke(async () =>
                        {
                            if (_browser != null)
                            {
                                await _browser.UpdateHapticProgressAsync(e.PercentComplete, e.Status);
                            }
                        });
                    }

                    void OnCompleted(object? sender, EventArgs e)
                    {
                        // Unsubscribe
                        App.AudioSync!.ProcessingProgress -= OnProgress;
                        App.AudioSync.ProcessingCompleted -= OnCompleted;

                        Dispatcher.BeginInvoke(async () =>
                        {
                            App.Logger?.Information("AudioSync: Processing completed, signaling browser");
                            if (_browser != null)
                            {
                                await _browser.SignalHapticReadyAsync();
                            }
                        });
                    }

                    // Wire up chunk loading events (for seek to unloaded sections)
                    void OnChunkLoadingRequired(object? sender, int chunkIndex)
                    {
                        Dispatcher.BeginInvoke(async () =>
                        {
                            App.Logger?.Information("AudioSync: Chunk {Index} loading required, showing overlay", chunkIndex);
                            if (_browser != null)
                            {
                                await _browser.ShowChunkLoadingOverlayAsync(chunkIndex);
                            }
                        });
                    }

                    void OnChunkLoadingCompleted(object? sender, EventArgs e)
                    {
                        Dispatcher.BeginInvoke(async () =>
                        {
                            App.Logger?.Information("AudioSync: Chunk loading completed, hiding overlay");
                            if (_browser != null)
                            {
                                await _browser.HideChunkLoadingOverlayAsync();
                            }
                        });
                    }

                    App.AudioSync.ProcessingProgress += OnProgress;
                    App.AudioSync.ProcessingCompleted += OnCompleted;
                    App.AudioSync.ChunkLoadingRequired += OnChunkLoadingRequired;
                    App.AudioSync.ChunkLoadingCompleted += OnChunkLoadingCompleted;

                    // Start processing in background
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await App.AudioSync.OnVideoDetectedAsync(videoUrl);
                        }
                        catch (Exception ex)
                        {
                            App.Logger?.Error(ex, "AudioSync: Processing failed");
                            // Signal ready anyway so video plays (without haptics)
                            await Dispatcher.InvokeAsync(async () =>
                            {
                                if (_browser != null)
                                {
                                    await _browser.SignalHapticReadyAsync();
                                }
                            });
                        }
                    });
                }
                else
                {
                    // No URL found, signal ready so video plays
                    App.Logger?.Warning("AudioSync: No URL found in message");
                    _ = _browser?.SignalHapticReadyAsync();
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to handle audio sync video detected");
                // Signal ready anyway so video plays (without haptics)
                _ = _browser?.SignalHapticReadyAsync();
            }
        }

        private void HandleAudioSyncState(string message)
        {
            if (App.AudioSync == null) return;

            try
            {
                // Extract currentTime and paused from message
                var timeMatch = System.Text.RegularExpressions.Regex.Match(message, "\"currentTime\":([\\d.]+)");
                var pausedMatch = System.Text.RegularExpressions.Regex.Match(message, "\"paused\":(true|false)");

                if (timeMatch.Success)
                {
                    var currentTime = double.Parse(timeMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    var paused = pausedMatch.Success && pausedMatch.Groups[1].Value == "true";

                    App.AudioSync.OnPlaybackStateUpdate(currentTime, paused);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to handle audio sync state: {Error}", ex.Message);
            }
        }

        private void HandleAudioSyncSeek(string message)
        {
            if (App.AudioSync == null) return;

            try
            {
                var timeMatch = System.Text.RegularExpressions.Regex.Match(message, "\"currentTime\":([\\d.]+)");
                if (timeMatch.Success)
                {
                    var newTime = double.Parse(timeMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    App.AudioSync.OnVideoSeek(newTime);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to handle audio sync seek: {Error}", ex.Message);
            }
        }

        private void HandleAudioSyncEnded()
        {
            App.AudioSync?.OnVideoEnded();
        }

        // Subscribed once to App.Haptics.ConnectionChanged so a late device connection can
        // arm the vibe track on a page that's already open. The browser can be torn down and
        // re-created (process-failure recovery), so the handler always uses the live _browser.
        private bool _hapticAudioSyncConnHooked;

        private void HookHapticAudioSyncRearm()
        {
            if (_hapticAudioSyncConnHooked || App.Haptics == null) return;
            _hapticAudioSyncConnHooked = true;
            App.Haptics.ConnectionChanged += OnHapticConnectionChangedForAudioSync;
        }

        private void OnHapticConnectionChangedForAudioSync(object? sender, bool connected)
        {
            if (!connected) return;

            // Device just connected. If the user is already on a HypnoTube page with audio-sync
            // enabled, inject (idempotent) and re-arm so the currently-loaded/playing video gets
            // synced now — instead of forcing a re-navigation. Marshalled to the UI thread because
            // ConnectionChanged fires from the provider's thread and GetCurrentUrl touches the WebView.
            Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    if (!App.Settings.Current.Haptics.AudioSync.Enabled) return;
                    var url = _browser?.GetCurrentUrl();
                    if (string.IsNullOrEmpty(url) ||
                        !url.Contains("hypnotube", StringComparison.OrdinalIgnoreCase))
                        return;

                    App.Logger?.Information("AudioSync: Haptics connected on HypnoTube page — arming vibe track for the current video");
                    await _browser!.InjectAudioSyncScriptAsync();
                    await _browser.RearmAudioSyncAsync();
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("AudioSync rearm-on-connect failed: {Error}", ex.Message);
                }
            });
        }

        private void BtnDiscordTab_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("discord");
        }

        private async void BtnDiscordTabLogin_Click(object sender, RoutedEventArgs e)
        {
            if (App.Discord == null) return;

            if (App.Discord.IsAuthenticated)
            {
                App.Discord.Logout();
                if (App.Patreon?.IsAuthenticated != true)
                {
                    // No provider left — full logout
                    ClearAccountData();
                }
                else
                {
                    // Patreon still active — just update Discord UI
                    UpdateDiscordTabUI();
                    UpdateDiscordUI();
                }
            }
            else
            {
                // Check if user is already logged in with another provider
                var hasUnifiedId = !string.IsNullOrEmpty(App.Settings?.Current?.UnifiedId);

                if (hasUnifiedId)
                {
                    // Link Discord to existing account
                    BtnDiscordTabLogin.IsEnabled = false;
                    BtnDiscordTabLogin.Content = Loc.Get("login_connecting");

                    try
                    {
                        await App.Discord.StartOAuthFlowAsync();
                        var success = await AccountService.LinkProviderV2Async(this, "discord");

                        if (success)
                        {
                            UpdateQuickDiscordUI();
                            UpdateDiscordUI();
                            UpdateDiscordTabUI();
                            UpdatePatreonUI();
                            UpdateAccountLinkingUI();
                            UpdateBannerWelcomeMessage();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // User cancelled
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Error(ex, "Failed to link Discord");
                        MessageBox.Show($"Failed to link Discord account.\n\n{ex.Message}",
                            "Link Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    finally
                    {
                        BtnDiscordTabLogin.IsEnabled = true;
                        UpdateDiscordTabUI();
                    }
                }
                else
                {
                    // No account yet - open unified login dialog
                    OpenUnifiedLoginDialog();
                }
            }
        }

        private void UpdateDiscordTabUI()
        {
            if (App.Discord == null) return;

            var isLoggedIn = App.Discord.IsAuthenticated;
            var s = App.Settings?.Current;

            // Update login status in Community Settings section
            if (TxtDiscordTabStatus != null && TxtDiscordTabInfo != null && BtnDiscordTabLogin != null)
            {
                if (isLoggedIn)
                {
                    TxtDiscordTabStatus.Text = Loc.GetF("label_connected_as_0", App.Discord.Username);
                    TxtDiscordTabInfo.Text = Loc.Get("label_discord_account_linked");
                    BtnDiscordTabLogin.Content = Loc.Get("btn_logout");
                }
                else
                {
                    // Check if user is logged in with another provider (has unified_id)
                    var hasUnifiedId = !string.IsNullOrEmpty(App.Settings?.Current?.UnifiedId);

                    TxtDiscordTabStatus.Text = Loc.Get("label_not_connected");
                    TxtDiscordTabInfo.Text = Loc.Get("label_link_discord_for_community_features");

                    // Show "Link Discord" if logged in via Patreon, otherwise "Login"
                    BtnDiscordTabLogin.Content = hasUnifiedId ? Loc.Get("btn_link_discord_2") : Loc.Get("btn_login");
                }
            }

            // Sync checkbox states
            if (s != null)
            {
                if (ChkDiscordTabRichPresence != null) ChkDiscordTabRichPresence.IsChecked = s.DiscordRichPresenceEnabled;
                if (ChkDiscordTabShowLevel != null) ChkDiscordTabShowLevel.IsChecked = s.DiscordShowLevelInPresence;
                if (ChkDiscordTabShareAchievements != null) ChkDiscordTabShareAchievements.IsChecked = s.DiscordShareAchievements;
                if (ChkDiscordTabShareLevelUps != null) ChkDiscordTabShareLevelUps.IsChecked = s.DiscordShareLevelUps;
                if (ChkDiscordTabAllowDm != null) ChkDiscordTabAllowDm.IsChecked = s.AllowDiscordDm;
                if (ChkDiscordTabSharePfp != null) ChkDiscordTabSharePfp.IsChecked = s.ShareProfilePicture;
                if (ChkDiscordTabShowOnline != null) ChkDiscordTabShowOnline.IsChecked = s.ShowOnlineStatus;
            }

            // Pre-fill search bar with user's unified display name (V2 auth) or fallback
            var displayName = App.Settings?.Current?.UserDisplayName
                ?? App.Discord?.CustomDisplayName ?? App.Discord?.DisplayName ?? App.Patreon?.DisplayName;
            if (TxtProfileSearch != null && !string.IsNullOrEmpty(displayName))
            {
                TxtProfileSearch.Text = displayName;
            }

            // Auto-display own profile when Discord tab is opened
            DisplayOwnProfile();
        }

        #region Profile Viewer

        private void TxtProfileSearch_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                SearchAndDisplayProfile(TxtProfileSearch?.Text);
            }
        }

        private void BtnProfileSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchAndDisplayProfile(TxtProfileSearch?.Text);
        }

        private void BtnViewMyProfile_Click(object sender, RoutedEventArgs e)
        {
            // Find current user in leaderboard by their unified display name (V2 auth) or fallback
            var displayName = App.Settings?.Current?.UserDisplayName
                ?? App.Discord?.CustomDisplayName ?? App.Discord?.DisplayName ?? App.Patreon?.DisplayName;
            if (string.IsNullOrEmpty(displayName))
            {
                // Not logged in - show own local stats
                DisplayOwnProfile();
                return;
            }

            // Try leaderboard search first, fall back to local profile if not found
            if (!SearchAndDisplayProfile(displayName))
            {
                DisplayOwnProfile();
            }
        }

        private void BtnClearProfile_Click(object sender, RoutedEventArgs e)
        {
            if (TxtProfileSearch != null) TxtProfileSearch.Text = "";
            ClearProfileViewer();
        }

        private void ClearProfileViewer()
        {
            if (ProfileCardWrapper != null) ProfileCardWrapper.Visibility = Visibility.Collapsed;
            if (NoProfileSelected != null) NoProfileSelected.Visibility = Visibility.Visible;
            if (ProfileAchievementGrid != null) ProfileAchievementGrid.ItemsSource = null;
            // Hide OG border and stop animation
            if (OgBorderContainer != null)
            {
                OgBorderContainer.Visibility = Visibility.Collapsed;
                if (OgBorderContainer.Resources["OgBorderAnimation"] is System.Windows.Media.Animation.Storyboard storyboard)
                {
                    storyboard.Stop(OgBorderContainer);
                }
            }
            // Hide OG banner badge
            if (OgBannerBadge != null)
            {
                OgBannerBadge.Visibility = Visibility.Collapsed;
            }
            // Hide Patreon tier badge
            if (ProfilePatreonTierBadge != null)
            {
                ProfilePatreonTierBadge.Visibility = Visibility.Collapsed;
            }
        }

        private void ProfileDiscordHandle_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var discordId = TxtProfileDiscordId?.Text;
            if (!string.IsNullOrEmpty(discordId))
            {
                try
                {
                    System.Windows.Clipboard.SetText(discordId);
                    // Show brief feedback
                    var originalText = TxtProfileDiscordId.Text;
                    TxtProfileDiscordId.Text = Loc.Get("btn_copied");
                    Task.Delay(1000).ContinueWith(_ =>
                    {
                        if (Application.Current?.Dispatcher?.HasShutdownStarted == true) return;
                        Dispatcher.Invoke(() =>
                        {
                            if (TxtProfileDiscordId != null)
                                TxtProfileDiscordId.Text = originalText;
                        });
                    });
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Failed to copy Discord ID to clipboard");
                }
            }
        }

        private void BtnProfileDiscord_Click(object sender, RoutedEventArgs e)
        {
            // Get Discord ID from button's Tag
            var button = sender as Button;
            var discordId = button?.Tag as string;

            if (string.IsNullOrEmpty(discordId))
            {
                discordId = TxtProfileDiscordId?.Text;
            }

            if (!string.IsNullOrEmpty(discordId))
            {
                try
                {
                    // Open Discord profile in browser using rundll32 to force browser
                    var profileUrl = $"https://discord.com/users/{discordId}";
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "rundll32.exe",
                        Arguments = $"url.dll,FileProtocolHandler {profileUrl}",
                        UseShellExecute = false
                    };
                    System.Diagnostics.Process.Start(startInfo);
                    App.Logger?.Information("Opened Discord profile for user: {DiscordId}", discordId);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Failed to open Discord profile");
                    // Fallback: copy to clipboard
                    try
                    {
                        System.Windows.Clipboard.SetText(discordId);
                        if (TxtProfileDiscordId != null)
                        {
                            var originalText = TxtProfileDiscordId.Text;
                            TxtProfileDiscordId.Text = Loc.Get("label_id_copied");
                            Task.Delay(1500).ContinueWith(_ =>
                            {
                                if (Application.Current?.Dispatcher?.HasShutdownStarted == true) return;
                                Dispatcher.Invoke(() =>
                                {
                                    if (TxtProfileDiscordId != null)
                                        TxtProfileDiscordId.Text = originalText;
                                });
                            });
                        }
                    }
                    catch { }
                }
            }
        }

        private async void BtnChangeDisplayName_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var currentName = App.Settings?.Current?.UserDisplayName ?? "";
                var dialog = new DisplayNameDialog(isChangeName: true, currentName: currentName);
                dialog.Owner = this;
                if (dialog.ShowDialog() != true) return;

                var newName = dialog.DisplayName;
                if (string.Equals(newName, currentName, StringComparison.Ordinal)) return;

                if (App.ProfileSync == null) return;

                // Disable button during request
                if (BtnChangeDisplayName != null) BtnChangeDisplayName.IsEnabled = false;

                var (success, error, resultName) = await App.ProfileSync.ChangeDisplayNameAsync(newName);

                if (success && resultName != null)
                {
                    if (App.Settings?.Current != null)
                    {
                        App.Settings.Current.UserDisplayName = resultName;
                        App.Settings.Save();
                    }
                    if (TxtProfileViewerName != null)
                        TxtProfileViewerName.Text = resultName;
                    UpdateQuickLoginUI();
                }
                else
                {
                    MessageBox.Show(
                        error ?? Loc.Get("msg_failed_to_change_display_name"),
                        Loc.Get("title_name_change_failed"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Error changing display name");
                MessageBox.Show(
                    Loc.Get("msg_error_changing_name"),
                    Loc.Get("label_error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                if (BtnChangeDisplayName != null) BtnChangeDisplayName.IsEnabled = true;
            }
        }

        private async void BtnDeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new DisplayNameDialog("delete");
                dialog.Owner = this;
                if (dialog.ShowDialog() != true) return;

                if (App.ProfileSync == null) return;

                // Disable button during request
                if (BtnDeleteProfile != null) BtnDeleteProfile.IsEnabled = false;

                var (success, error) = await App.ProfileSync.DeleteAccountAsync();

                if (success)
                {
                    App.ProfileSync?.StopHeartbeat();
                    App.Patreon?.Logout();
                    App.Discord?.Logout();

                    ClearAccountData();

                    MessageBox.Show(
                        Loc.Get("msg_profile_deleted"),
                        Loc.Get("title_profile_deleted"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(
                        error ?? Loc.Get("msg_failed_to_delete_profile"),
                        Loc.Get("title_deletion_failed"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Error deleting profile");
                MessageBox.Show(
                    Loc.Get("msg_error_deleting_profile"),
                    Loc.Get("label_error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                if (BtnDeleteProfile != null) BtnDeleteProfile.IsEnabled = true;
            }
        }

        /// <summary>
        /// Search leaderboard for a profile by display name and show it.
        /// Returns true if a match was found and displayed, false otherwise.
        /// </summary>
        private bool SearchAndDisplayProfile(string? searchName)
        {
            if (string.IsNullOrWhiteSpace(searchName))
            {
                return false;
            }

            App.Logger?.Information("SearchAndDisplayProfile: Searching for '{SearchName}'", searchName);

            // Search in leaderboard entries
            var entries = App.Leaderboard?.Entries;
            if (entries == null || entries.Count == 0)
            {
                App.Logger?.Information("SearchAndDisplayProfile: No entries, refreshing leaderboard...");
                // Try to refresh leaderboard first
                _ = RefreshAndSearchAsync(searchName);
                return false;
            }

            App.Logger?.Information("SearchAndDisplayProfile: Searching {Count} entries", entries.Count);

            // Find matching entry (case-insensitive)
            var entry = entries.FirstOrDefault(e =>
                e.DisplayName?.Equals(searchName, StringComparison.OrdinalIgnoreCase) == true);

            if (entry != null)
            {
                App.Logger?.Information("SearchAndDisplayProfile: Found exact match '{Name}'", entry.DisplayName);
                DisplayProfileEntry(entry);
                return true;
            }

            // No exact match - try partial match
            entry = entries.FirstOrDefault(e =>
                e.DisplayName?.Contains(searchName, StringComparison.OrdinalIgnoreCase) == true);

            if (entry != null)
            {
                App.Logger?.Information("SearchAndDisplayProfile: Found partial match '{Name}'", entry.DisplayName);
                DisplayProfileEntry(entry);
                return true;
            }

            App.Logger?.Information("SearchAndDisplayProfile: No match found for '{SearchName}'", searchName);
            // Show not found message
            if (NoProfileSelected != null)
            {
                NoProfileSelected.Visibility = Visibility.Visible;
            }
            if (ProfileCardWrapper != null)
            {
                ProfileCardWrapper.Visibility = Visibility.Collapsed;
            }
            return false;
        }

        private async Task RefreshAndSearchAsync(string searchName)
        {
            if (App.Leaderboard != null)
            {
                await App.Leaderboard.RefreshAsync();

                // After refresh, try to find the profile but don't recurse if still empty
                var entries = App.Leaderboard?.Entries;
                if (entries != null && entries.Count > 0)
                {
                    var entry = entries.FirstOrDefault(e =>
                        e.DisplayName?.Equals(searchName, StringComparison.OrdinalIgnoreCase) == true);

                    if (entry == null)
                    {
                        entry = entries.FirstOrDefault(e =>
                            e.DisplayName?.Contains(searchName, StringComparison.OrdinalIgnoreCase) == true);
                    }

                    if (entry != null)
                    {
                        DisplayProfileEntry(entry);
                        return;
                    }
                }

                // Show not found message
                if (NoProfileSelected != null)
                {
                    NoProfileSelected.Visibility = Visibility.Visible;
                }
                if (ProfileCardWrapper != null)
                {
                    ProfileCardWrapper.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void DisplayOwnProfile()
        {
            // Display local profile when not on leaderboard
            if (ProfileCardWrapper != null) ProfileCardWrapper.Visibility = Visibility.Visible;
            if (NoProfileSelected != null) NoProfileSelected.Visibility = Visibility.Collapsed;

            // OG user animated border for own profile
            var isOg = App.Settings?.Current?.IsSeason0Og == true;
            if (OgBorderContainer != null)
            {
                if (isOg)
                {
                    OgBorderContainer.Visibility = Visibility.Visible;
                    if (OgBorderContainer.Resources["OgBorderAnimation"] is System.Windows.Media.Animation.Storyboard storyboard)
                    {
                        storyboard.Begin(OgBorderContainer, true);
                    }
                }
                else
                {
                    OgBorderContainer.Visibility = Visibility.Collapsed;
                    if (OgBorderContainer.Resources["OgBorderAnimation"] is System.Windows.Media.Animation.Storyboard storyboard)
                    {
                        storyboard.Stop(OgBorderContainer);
                    }
                }
            }
            // OG GOOD GIRL banner badge for own profile
            if (OgBannerBadge != null)
            {
                OgBannerBadge.Visibility = isOg ? Visibility.Visible : Visibility.Collapsed;
            }

            // Avatar - load from Discord only if ShareProfilePicture is enabled
            if (ProfileViewerAvatar != null)
            {
                string? avatarUrl = null;
                // Only show avatar if user has ShareProfilePicture enabled
                if (App.Settings?.Current?.ShareProfilePicture == true && App.Discord?.IsAuthenticated == true)
                {
                    avatarUrl = App.Discord.GetAvatarUrl(256);
                }

                if (!string.IsNullOrEmpty(avatarUrl))
                {
                    try
                    {
                        var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(avatarUrl);
                        bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        ProfileViewerAvatar.ImageSource = bitmap;
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning(ex, "Failed to load profile avatar");
                        ProfileViewerAvatar.ImageSource = null;
                    }
                }
                else
                {
                    ProfileViewerAvatar.ImageSource = null;
                }
            }

            // Name - use V2 unified display name (leaderboard name), never raw provider names
            if (TxtProfileViewerName != null)
                TxtProfileViewerName.Text = App.Settings?.Current?.UserDisplayName
                    ?? App.Discord?.CustomDisplayName ?? App.Patreon?.DisplayName ?? "You";

            // Show edit name button for own profile (only if logged in with unified ID)
            if (BtnChangeDisplayName != null)
                BtnChangeDisplayName.Visibility = !string.IsNullOrEmpty(App.Settings?.Current?.UnifiedId)
                    ? Visibility.Visible : Visibility.Collapsed;

            // Show delete profile button for own profile (only if logged in with unified ID)
            if (BtnDeleteProfile != null)
                BtnDeleteProfile.Visibility = !string.IsNullOrEmpty(App.Settings?.Current?.UnifiedId)
                    ? Visibility.Visible : Visibility.Collapsed;

            // Online status
            if (TxtProfileViewerOnline != null)
            {
                TxtProfileViewerOnline.Text = Loc.Get("label_online");
                TxtProfileViewerOnline.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#43B581"));
            }
            if (ProfileOnlineIndicator != null)
                ProfileOnlineIndicator.Fill = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#43B581"));

            // Discord button
            if (BtnProfileDiscord != null && TxtProfileDiscordId != null)
            {
                if (App.Settings?.Current?.AllowDiscordDm == true && !string.IsNullOrEmpty(App.Discord?.UserId))
                {
                    BtnProfileDiscord.Visibility = Visibility.Visible;
                    // Use V2 unified name for consistency, fall back to Discord display
                    TxtProfileDiscordId.Text = App.Settings?.Current?.UserDisplayName
                        ?? App.Discord.CustomDisplayName ?? App.Discord.UserId;
                    BtnProfileDiscord.Tag = App.Discord.UserId; // Store ID for click handler
                }
                else
                {
                    BtnProfileDiscord.Visibility = Visibility.Collapsed;
                }
            }

            // Stats from local data
            var level = App.Settings?.Current?.PlayerLevel ?? 1;
            var localXp = App.Settings?.Current?.PlayerXP ?? 0;
            var xp = App.Progression?.GetTotalXP(level, localXp) ?? localXp;
            var progress = App.Achievements?.Progress;

            if (TxtProfileViewerLevel != null) TxtProfileViewerLevel.Text = level.ToString();

            // Rank (own rank from leaderboard, if available)
            if (TxtProfileViewerRank != null)
            {
                // Prefer server-provided rank (works even beyond top 200)
                var serverRank = App.Leaderboard?.YourRank;
                if (serverRank.HasValue && serverRank.Value > 0)
                {
                    TxtProfileViewerRank.Text = $"#{serverRank.Value}";
                }
                else
                {
                    // Fallback: scan local entries by unified_id or display name
                    var unifiedId = App.UnifiedUserId;
                    var displayName = App.Settings?.Current?.UserDisplayName;

                    var ownEntry = !string.IsNullOrEmpty(unifiedId)
                        ? App.Leaderboard?.Entries?.FirstOrDefault(e =>
                            e.UnifiedId == unifiedId)
                        : null;

                    ownEntry ??= !string.IsNullOrEmpty(displayName)
                        ? App.Leaderboard?.Entries?.FirstOrDefault(e =>
                            e.DisplayName?.Equals(displayName, StringComparison.OrdinalIgnoreCase) == true)
                        : null;

                    TxtProfileViewerRank.Text = ownEntry?.Rank > 0 ? $"#{ownEntry.Rank}" : "#-";
                }
            }
            if (TxtProfileViewerXp != null) TxtProfileViewerXp.Text = FormatNumber(xp);
            if (TxtProfileViewerBubbles != null) TxtProfileViewerBubbles.Text = FormatNumber(progress?.TotalBubblesPopped ?? 0);
            if (TxtProfileViewerVideos != null)
            {
                var minutes = progress?.TotalVideoMinutes ?? 0;
                TxtProfileViewerVideos.Text = minutes >= 60 ? $"{minutes / 60:F1}h" : $"{minutes:F0}m";
            }
            if (TxtProfileViewerGifs != null) TxtProfileViewerGifs.Text = FormatNumber(progress?.TotalFlashImages ?? 0);
            if (TxtProfileViewerLockCards != null) TxtProfileViewerLockCards.Text = FormatNumber(progress?.TotalLockCardsCompleted ?? 0);
            if (TxtProfileViewerAchievements != null)
            {
                // Free-only count so the patron-exclusive set is never folded into this number.
                var unlocked = App.Achievements?.GetUnlockedCount(exclusive: false) ?? 0;
                var total = App.Achievements?.GetTotalCount(exclusive: false)
                            ?? System.Linq.Enumerable.Count(Models.Achievement.All.Values, a => !a.IsExclusive && !a.IsHidden);
                TxtProfileViewerAchievements.Text = $"{unlocked} / {total}";
            }

            // Patreon badge - use settings tier (works for Discord-only login with linked Patreon)
            var patreonTier = App.Settings?.Current?.PatreonTier ?? (int)(App.Patreon?.CurrentTier ?? 0);
            var hasPatreon = patreonTier >= 1 || App.Patreon?.IsWhitelisted == true;

            if (ProfilePatreonBadge != null)
            {
                if (patreonTier > 0)
                {
                    ProfilePatreonBadge.Visibility = Visibility.Visible;
                    ProfilePatreonBadge.Source = LoadPatreonBadgeImage(patreonTier);
                }
                else
                {
                    ProfilePatreonBadge.Visibility = Visibility.Collapsed;
                }
            }

            // Patreon tier badge next to Discord button (same as leaderboard)
            if (ProfilePatreonTierBadge != null)
            {
                if (hasPatreon)
                {
                    ProfilePatreonTierBadge.Visibility = Visibility.Visible;
                    // Use tier 1 as fallback for whitelisted users with tier 0
                    ProfilePatreonTierBadge.Source = LoadPatreonBadgeImage(patreonTier > 0 ? patreonTier : 1);
                }
                else
                {
                    ProfilePatreonTierBadge.Visibility = Visibility.Collapsed;
                }
            }

            // Patreon tier banner (Pink filter / Prime subject images)
            // Shows for tier 1+, tier 2+, tier 3, OR whitelisted users
            if (ProfilePatreonTierBanner != null && ImgPatreonTierBanner != null)
            {
                if (hasPatreon)
                {
                    ProfilePatreonTierBanner.Visibility = Visibility.Visible;
                    try
                    {
                        // Tier 3 = Prime subject, everyone else = Pink filter
                        var bannerImage = patreonTier >= 3 ? "prime subject.webp" : "Pink filter.webp";
                        ImgPatreonTierBanner.Source = new System.Windows.Media.Imaging.BitmapImage(
                            new Uri($"pack://application:,,,/Resources/{bannerImage}", UriKind.Absolute));
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning(ex, "Failed to load Patreon tier banner image");
                        ProfilePatreonTierBanner.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    ProfilePatreonTierBanner.Visibility = Visibility.Collapsed;
                }
            }

            // Load achievement images for own profile
            if (progress?.UnlockedAchievements != null && progress.UnlockedAchievements.Count > 0)
            {
                LoadProfileAchievementImages(progress.UnlockedAchievements);
            }
            else
            {
                if (ProfileAchievementGrid != null) ProfileAchievementGrid.ItemsSource = null;
                if (TxtNoAchievements != null)
                {
                    TxtNoAchievements.Text = Loc.Get("label_no_achievements_yet");
                    TxtNoAchievements.Visibility = Visibility.Visible;
                }
            }
        }

        private void DisplayProfileEntry(Services.LeaderboardEntry entry)
        {
            try
            {
            if (ProfileCardWrapper != null) ProfileCardWrapper.Visibility = Visibility.Visible;
            if (NoProfileSelected != null) NoProfileSelected.Visibility = Visibility.Collapsed;

            // OG user animated border
            if (OgBorderContainer != null)
            {
                if (entry.IsSeason0Og)
                {
                    OgBorderContainer.Visibility = Visibility.Visible;
                    // Start the rotation animation
                    if (OgBorderContainer.Resources["OgBorderAnimation"] is System.Windows.Media.Animation.Storyboard storyboard)
                    {
                        storyboard.Begin(OgBorderContainer, true);
                    }
                }
                else
                {
                    OgBorderContainer.Visibility = Visibility.Collapsed;
                    // Stop any running animation
                    if (OgBorderContainer.Resources["OgBorderAnimation"] is System.Windows.Media.Animation.Storyboard storyboard)
                    {
                        storyboard.Stop(OgBorderContainer);
                    }
                }
            }
            // OG GOOD GIRL banner badge next to name
            if (OgBannerBadge != null)
            {
                OgBannerBadge.Visibility = entry.IsSeason0Og ? Visibility.Visible : Visibility.Collapsed;
            }

            // Avatar - clear previous, will be loaded async
            if (ProfileViewerAvatar != null)
            {
                ProfileViewerAvatar.ImageSource = null;
            }

            // Name
            if (TxtProfileViewerName != null)
                TxtProfileViewerName.Text = entry.DisplayName ?? "Unknown";

            // Online status (from cached data initially)
            if (TxtProfileViewerOnline != null)
            {
                TxtProfileViewerOnline.Text = entry.IsOnline ? "Online" : "Offline";
                TxtProfileViewerOnline.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                        entry.IsOnline ? "#43B581" : "#747F8D"));
            }
            if (ProfileOnlineIndicator != null)
                ProfileOnlineIndicator.Fill = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                        entry.IsOnline ? "#43B581" : "#747F8D"));

            // Trigger async lookup to get fresh online status and avatar
            if (!string.IsNullOrEmpty(entry.DisplayName))
            {
                _ = RefreshProfileViewerAsync(entry.DisplayName);
            }

            // Discord button (only if they have it and allow DMs)
            if (BtnProfileDiscord != null && TxtProfileDiscordId != null)
            {
                if (entry.HasDiscord && !string.IsNullOrEmpty(entry.DiscordId))
                {
                    BtnProfileDiscord.Visibility = Visibility.Visible;
                    TxtProfileDiscordId.Text = entry.DisplayName ?? "Message on Discord";
                    BtnProfileDiscord.Tag = entry.DiscordId; // Store ID for click handler
                }
                else
                {
                    BtnProfileDiscord.Visibility = Visibility.Collapsed;
                }
            }

            // Stats
            if (TxtProfileViewerLevel != null) TxtProfileViewerLevel.Text = entry.Level.ToString();

            // Rank
            if (TxtProfileViewerRank != null)
            {
                TxtProfileViewerRank.Text = entry.Rank > 0 ? $"#{entry.Rank}" : "#-";
            }
            if (TxtProfileViewerXp != null) TxtProfileViewerXp.Text = entry.XpDisplay;
            if (TxtProfileViewerBubbles != null) TxtProfileViewerBubbles.Text = entry.BubblesPoppedDisplay;
            if (TxtProfileViewerVideos != null)
            {
                var hours = entry.VideoMinutes / 60.0;
                TxtProfileViewerVideos.Text = hours >= 1 ? $"{hours:F1}h" : $"{entry.VideoMinutes:F0}m";
            }
            if (TxtProfileViewerGifs != null) TxtProfileViewerGifs.Text = entry.GifsSpawnedDisplay;
            if (TxtProfileViewerLockCards != null) TxtProfileViewerLockCards.Text = entry.LockCardsCompleted.ToString();
            if (TxtProfileViewerAchievements != null) TxtProfileViewerAchievements.Text = entry.AchievementsDisplay;

            // Check if this is the current user's profile - if so, use local Patreon data
            // which is more accurate than leaderboard cache
            var isOwnProfile = entry.DisplayName?.Equals(
                App.Settings?.Current?.UserDisplayName, StringComparison.OrdinalIgnoreCase) == true;

            // Edit name button - only visible on own profile
            if (BtnChangeDisplayName != null)
                BtnChangeDisplayName.Visibility = isOwnProfile && !string.IsNullOrEmpty(App.Settings?.Current?.UnifiedId)
                    ? Visibility.Visible : Visibility.Collapsed;

            // Delete profile button - only visible on own profile
            if (BtnDeleteProfile != null)
                BtnDeleteProfile.Visibility = isOwnProfile && !string.IsNullOrEmpty(App.Settings?.Current?.UnifiedId)
                    ? Visibility.Visible : Visibility.Collapsed;

            int tierToUse;
            bool hasPatreonAccess;

            if (isOwnProfile)
            {
                // Use local Patreon data for own profile
                tierToUse = App.Settings?.Current?.PatreonTier ?? (int)(App.Patreon?.CurrentTier ?? 0);
                hasPatreonAccess = tierToUse >= 1 || App.Patreon?.IsWhitelisted == true;
            }
            else
            {
                // Use leaderboard entry data for other users
                tierToUse = entry.PatreonTier;
                hasPatreonAccess = entry.IsPatreon && entry.PatreonTier >= 1;
            }

            // Patreon badge (next to Level/Rank)
            if (ProfilePatreonBadge != null)
            {
                if (hasPatreonAccess && tierToUse > 0)
                {
                    ProfilePatreonBadge.Visibility = Visibility.Visible;
                    ProfilePatreonBadge.Source = LoadPatreonBadgeImage(tierToUse);
                }
                else
                {
                    ProfilePatreonBadge.Visibility = Visibility.Collapsed;
                }
            }

            // Patreon tier badge next to Discord button (same as leaderboard)
            if (ProfilePatreonTierBadge != null)
            {
                if (hasPatreonAccess)
                {
                    ProfilePatreonTierBadge.Visibility = Visibility.Visible;
                    // Use tier 1 as fallback for whitelisted users with tier 0
                    ProfilePatreonTierBadge.Source = LoadPatreonBadgeImage(tierToUse > 0 ? tierToUse : 1);
                }
                else
                {
                    ProfilePatreonTierBadge.Visibility = Visibility.Collapsed;
                }
            }

            // Patreon tier banner (Pink filter / Prime subject images)
            // Shows for any Patreon supporter (tier 1+)
            if (ProfilePatreonTierBanner != null && ImgPatreonTierBanner != null)
            {
                if (hasPatreonAccess)
                {
                    ProfilePatreonTierBanner.Visibility = Visibility.Visible;
                    try
                    {
                        // Tier 3 = Prime subject, everyone else = Pink filter
                        var bannerImage = tierToUse >= 3 ? "prime subject.webp" : "Pink filter.webp";
                        ImgPatreonTierBanner.Source = new System.Windows.Media.Imaging.BitmapImage(
                            new Uri($"pack://application:,,,/Resources/{bannerImage}", UriKind.Absolute));
                    }
                    catch
                    {
                        ProfilePatreonTierBanner.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    ProfilePatreonTierBanner.Visibility = Visibility.Collapsed;
                }
            }

            // We don't have detailed achievement list from leaderboard, just the count
            // So hide the achievement grid for other users or show placeholder
            if (ProfileAchievementGrid != null)
            {
                ProfileAchievementGrid.ItemsSource = null;
            }
            if (TxtNoAchievements != null)
            {
                TxtNoAchievements.Text = $"{entry.AchievementsCount} achievements unlocked";
                TxtNoAchievements.Visibility = Visibility.Visible;
            }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "DisplayProfileEntry failed for {Name}", entry?.DisplayName);
            }
        }

        /// <summary>
        /// Refresh profile viewer with fresh data from server (online status, avatar)
        /// </summary>
        private async Task RefreshProfileViewerAsync(string displayName)
        {
            try
            {
                var lookup = await App.Leaderboard?.LookupUserAsync(displayName);
                if (lookup == null) return;

                // Update on UI thread
                await Dispatcher.InvokeAsync(() =>
                {
                    // Verify we're still showing this user (user may have clicked away)
                    if (TxtProfileViewerName?.Text != displayName) return;

                    // Update online status
                    if (TxtProfileViewerOnline != null)
                    {
                        TxtProfileViewerOnline.Text = lookup.IsOnline ? "Online" : "Offline";
                        TxtProfileViewerOnline.Foreground = new System.Windows.Media.SolidColorBrush(
                            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                                lookup.IsOnline ? "#43B581" : "#747F8D"));
                    }
                    if (ProfileOnlineIndicator != null)
                    {
                        ProfileOnlineIndicator.Fill = new System.Windows.Media.SolidColorBrush(
                            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                                lookup.IsOnline ? "#43B581" : "#747F8D"));
                    }

                    // Load avatar if available
                    if (ProfileViewerAvatar != null)
                    {
                        string? avatarUrl = lookup.AvatarUrl;

                        // Fallback: if viewing own profile and server didn't return avatar, use local Discord avatar
                        // BUT only if user has ShareProfilePicture enabled (respect their privacy setting)
                        if (string.IsNullOrEmpty(avatarUrl) && App.Settings?.Current?.ShareProfilePicture == true)
                        {
                            var ownDisplayName = App.Settings?.Current?.UserDisplayName
                                               ?? App.Discord?.CustomDisplayName
                                               ?? App.Discord?.DisplayName
                                               ?? App.Patreon?.DisplayName;
                            if (displayName.Equals(ownDisplayName, StringComparison.OrdinalIgnoreCase) && App.Discord?.IsAuthenticated == true)
                            {
                                avatarUrl = App.Discord.GetAvatarUrl(256);
                            }
                        }

                        if (!string.IsNullOrEmpty(avatarUrl))
                        {
                            try
                            {
                                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                                bitmap.BeginInit();
                                bitmap.UriSource = new Uri(avatarUrl);
                                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                                bitmap.EndInit();
                                ProfileViewerAvatar.ImageSource = bitmap;
                            }
                            catch (Exception ex)
                            {
                                App.Logger?.Warning(ex, "Failed to load profile avatar from {Url}", avatarUrl);
                                ProfileViewerAvatar.ImageSource = null;
                            }
                        }
                        else
                        {
                            // No avatar URL - clear any previous image
                            ProfileViewerAvatar.ImageSource = null;
                        }
                    }

                    // Load achievements from lookup result (for other users)
                    if (lookup.Achievements != null && lookup.Achievements.Count > 0)
                    {
                        var achievementSet = new HashSet<string>(lookup.Achievements);
                        LoadProfileAchievementImages(achievementSet);
                    }
                    else if (lookup.AchievementsCount > 0)
                    {
                        // Fallback: server returned count but no list (shouldn't happen with updated server)
                        if (TxtNoAchievements != null)
                        {
                            TxtNoAchievements.Text = $"{lookup.AchievementsCount} achievements unlocked";
                            TxtNoAchievements.Visibility = Visibility.Visible;
                        }
                        if (ProfileAchievementGrid != null)
                        {
                            ProfileAchievementGrid.ItemsSource = null;
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to refresh profile viewer for {Name}", displayName);
            }
        }

        private System.Windows.Media.Imaging.BitmapImage? LoadPatreonBadgeImage(int tier)
        {
            try
            {
                var imageName = tier switch
                {
                    1 => "Patreon tier1.png",
                    2 => "Patreon tier2.png",
                    3 => "Patreon tier3.png",
                    _ => "Patreon tier1.png"
                };
                return new System.Windows.Media.Imaging.BitmapImage(
                    new Uri($"pack://application:,,,/Resources/{imageName}", UriKind.Absolute));
            }
            catch
            {
                return null;
            }
        }

        private void LoadProfileAchievementImages(HashSet<string>? unlockedAchievements)
        {
            if (ProfileAchievementGrid == null) return;

            if (unlockedAchievements == null || unlockedAchievements.Count == 0)
            {
                ProfileAchievementGrid.ItemsSource = null;
                if (TxtNoAchievements != null) TxtNoAchievements.Visibility = Visibility.Visible;
                return;
            }

            if (TxtNoAchievements != null) TxtNoAchievements.Visibility = Visibility.Collapsed;

            var achievementItems = new List<object>();
            foreach (var achievementId in unlockedAchievements)
            {
                var achievement = Models.Achievement.All.Values.FirstOrDefault(a => a.Id == achievementId);
                if (achievement != null)
                {
                    var image = LoadAchievementImage(achievement.ImageName);
                    if (image != null)
                    {
                        achievementItems.Add(new { Name = App.Mods?.MakeModAware(achievement.Name) ?? achievement.Name, Image = image });
                    }
                }
            }

            ProfileAchievementGrid.ItemsSource = achievementItems;
        }

        private string FormatNumber(double number)
        {
            if (number >= 1_000_000) return $"{number / 1_000_000:F1}M";
            if (number >= 1_000) return $"{number / 1_000:F1}k";
            return number.ToString("N0");
        }

        #endregion

        private async void BtnPopOutBrowser_Click(object sender, RoutedEventArgs e)
        {
            // Block in offline mode
            if (App.Settings?.Current?.OfflineMode == true) return;

            // Lazy-load browser on first pop-out
            if (!_browserInitialized)
            {
                await InitializeBrowserAsync();
            }

            if (_browser?.WebView == null) return;

            // If already popped out, bring the window to front
            if (_browserPopoutWindow != null)
            {
                _browserPopoutWindow.Activate();
                return;
            }

            try
            {
                // Remove WebView from embedded container
                if (BrowserContainer.Children.Contains(_browser.WebView))
                {
                    BrowserContainer.Children.Remove(_browser.WebView);
                }

                // Show placeholder in the embedded container
                BrowserLoadingText.Text = Loc.Get("label_browser_popped_out_nclick_to_focus_window");
                BrowserLoadingText.Visibility = Visibility.Visible;

                // Create popup window
                _browserPopoutWindow = new Window
                {
                    Title = Loc.Get("title_browser_window"),
                    Width = 1024,
                    Height = 768,
                    MinWidth = 400,
                    MinHeight = 300,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1A, 0x1A, 0x2E)),
                    Content = _browser.WebView
                };

                // Handle window CLOSING (before close) - detach WebView to prevent parent/child errors
                _browserPopoutWindow.Closing += (s, args) =>
                {
                    // Exit browser fullscreen first if the popout is being closed while fullscreen
                    if (_isBrowserFullscreen && _browserFullscreenWasPopout)
                    {
                        _isBrowserFullscreen = false;
                        _browserFullscreenWasPopout = false;
                        if (_browser != null)
                            _browser.ZoomFactor = _browserPreFullscreenZoom;
                    }

                    if (_browserPopoutWindow != null)
                    {
                        // CRITICAL: Remove WebView from window content BEFORE closing
                        // This prevents "window is a parent/child of another" errors
                        _browserPopoutWindow.Content = null;
                    }
                };

                // Handle window CLOSED (after close) - return browser to embedded container
                _browserPopoutWindow.Closed += (s, args) =>
                {
                    if (_browser?.WebView != null)
                    {
                        // Add back to embedded container
                        if (!BrowserContainer.Children.Contains(_browser.WebView))
                        {
                            BrowserContainer.Children.Add(_browser.WebView);
                        }
                        BrowserLoadingText.Visibility = Visibility.Collapsed;
                    }
                    _browserPopoutWindow = null;
                    BtnPopOutBrowser.Content = Loc.Get("btn_pop_out");
                    BtnPopOutBrowser.ToolTip = Loc.Get("tooltip_pop_out_browser_to_resizable_window");
                };

                // Update button to show it's popped out
                BtnPopOutBrowser.Content = Loc.Get("btn_focus");
                BtnPopOutBrowser.ToolTip = Loc.Get("tooltip_browser_is_popped_out_click_to_focus");

                _browserPopoutWindow.Show();
                App.Logger?.Information("Browser popped out to separate window");
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to pop out browser");
                // Try to restore browser to container
                if (_browser?.WebView != null && !BrowserContainer.Children.Contains(_browser.WebView))
                {
                    BrowserContainer.Children.Add(_browser.WebView);
                    BrowserLoadingText.Visibility = Visibility.Collapsed;
                }
                _browserPopoutWindow = null;
            }
        }

        private void HandleBrowserFullscreenChanged(bool isFullscreen)
        {
            if (_browser?.WebView == null) return;

            if (isFullscreen)
            {
                var screens = App.GetAllScreensCached();
                var useDualMonitor = App.Settings.Current.DualMonitorEnabled && screens.Length > 1;

                if (useDualMonitor)
                {
                    _isDualMonitorPlaybackActive = App.ScreenMirror.EnableMirror();
                    if (_isDualMonitorPlaybackActive)
                    {
                        App.Logger?.Information("Screen mirroring enabled for fullscreen video");
                    }
                }

                // Always reparent — single-monitor users still need real
                // full-monitor fullscreen, otherwise HT's HTML5 fullscreen
                // just renders inside the dashboard cell. The dblclick exit
                // works via the JS click-pair detector + ccp_exit_fullscreen
                // WebMessage path (window._ccpForcedFs flag covers the case
                // where the page lost HTML5 fullscreen during reparent).
                EnterBrowserFullscreen();
            }
            else
            {
                if (_isDualMonitorPlaybackActive)
                {
                    App.ScreenMirror.DisableMirror();
                    _isDualMonitorPlaybackActive = false;
                    App.Logger?.Information("Screen mirroring disabled");
                }

                ExitBrowserFullscreen();
            }
        }

        public void EnterBrowserFullscreen()
        {
            if (_browser?.WebView == null || _isBrowserFullscreen) return;

            try
            {
                // Save avatar attached state before entering fullscreen
                _avatarWasAttachedBeforeBrowserFullscreen = _avatarTubeWindow != null && !_avatarTubeWindow.IsDetached;
                _browserPreFullscreenZoom = _browser.ZoomFactor;
                _browser.ZoomFactor = 1.0;
                _isBrowserFullscreen = true;

                if (_browserPopoutWindow != null)
                {
                    // === POPOUT MODE: user already had browser popped out ===
                    _browserFullscreenWasPopout = true;

                    // Save popout window state for restore
                    _popoutPreFsStyle = _browserPopoutWindow.WindowStyle;
                    _popoutPreFsResize = _browserPopoutWindow.ResizeMode;
                    _popoutPreFsState = _browserPopoutWindow.WindowState;
                    _popoutPreFsLeft = _browserPopoutWindow.Left;
                    _popoutPreFsTop = _browserPopoutWindow.Top;
                    _popoutPreFsWidth = _browserPopoutWindow.Width;
                    _popoutPreFsHeight = _browserPopoutWindow.Height;
                    _popoutPreFsTopmost = _browserPopoutWindow.Topmost;

                    // Go fullscreen in-place
                    if (_browserPopoutWindow.WindowState == WindowState.Maximized)
                        _browserPopoutWindow.WindowState = WindowState.Normal;

                    _browserPopoutWindow.WindowStyle = WindowStyle.None;
                    _browserPopoutWindow.ResizeMode = ResizeMode.NoResize;
                    _browserPopoutWindow.Topmost = true;
                    _browserPopoutWindow.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                    _browserPopoutWindow.WindowState = WindowState.Maximized;
                }
                else
                {
                    // === EMBEDDED MODE: create fullscreen window directly ===
                    // Same approach as the mandatory video windows which work correctly:
                    // Create Window with WindowStyle.None from the start, Show, then Maximize.
                    _browserFullscreenWasPopout = false;

                    // Remove WebView from embedded container
                    if (BrowserContainer.Children.Contains(_browser.WebView))
                    {
                        BrowserContainer.Children.Remove(_browser.WebView);
                    }
                    BrowserLoadingText.Text = "\ud83c\udf10 Browser in fullscreen";
                    BrowserLoadingText.Visibility = Visibility.Visible;

                    var screen = System.Windows.Forms.Screen.FromHandle(
                        new System.Windows.Interop.WindowInteropHelper(this).Handle);

                    // Create window with fullscreen properties from the start (like video windows)
                    _browserPopoutWindow = new Window
                    {
                        WindowStyle = WindowStyle.None,
                        ResizeMode = ResizeMode.NoResize,
                        ShowInTaskbar = false,
                        Topmost = true,
                        Background = System.Windows.Media.Brushes.Black,
                        WindowStartupLocation = WindowStartupLocation.Manual,
                        Left = screen.Bounds.X + 100,
                        Top = screen.Bounds.Y + 100,
                        Width = 400,
                        Height = 300,
                        Content = _browser.WebView
                    };

                    _browserPopoutWindow.Closing += (s, args) =>
                    {
                        if (_isBrowserFullscreen)
                        {
                            _isBrowserFullscreen = false;
                            if (_browser != null)
                                _browser.ZoomFactor = _browserPreFullscreenZoom;
                        }
                        if (_browserPopoutWindow != null)
                            _browserPopoutWindow.Content = null;
                    };

                    _browserPopoutWindow.Closed += (s, args) =>
                    {
                        if (_browser?.WebView != null && !BrowserContainer.Children.Contains(_browser.WebView))
                        {
                            BrowserContainer.Children.Add(_browser.WebView);
                            BrowserLoadingText.Visibility = Visibility.Collapsed;
                        }
                        _browserPopoutWindow = null;
                    };

                    // Show small first, pump render queue, then maximize — exactly like video windows
                    _browserPopoutWindow.Show();
                    _browserPopoutWindow.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                    _browserPopoutWindow.WindowState = WindowState.Maximized;
                }

                // (Removed: ReRequestVideoFullscreenAsync.) That stacked a
                // second HTML5 fullscreen entry on top of HT's wrapper-level
                // one, and document.exitFullscreen() only pops one stack
                // entry per call — so HT's minimize button and dblclick
                // appeared to do nothing. Letting HT's original wrapper
                // fullscreen ride through the transition gives a single-layer
                // exit that pops cleanly on one exitFullscreen call.

                // Flag the page so the JS click-pair / dblclick handlers
                // (injected in BrowserService) fire even if the page lost
                // HTML5 fullscreen state during the reparent. The user can
                // always exit our WPF "forced fullscreen" by double-clicking
                // the video — same as Esc.
                try { _ = _browser.WebView.CoreWebView2.ExecuteScriptAsync("window._ccpForcedFs = true;"); }
                catch { }

                App.Logger?.Information("Browser entered fullscreen");
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to enter browser fullscreen");
                ExitBrowserFullscreen();
            }
        }

        private void ExitBrowserFullscreen()
        {
            if (!_isBrowserFullscreen) return;

            try
            {
                // Clear the JS flag and best-effort exit any lingering HTML5
                // fullscreen on the page side before we restore window state.
                try
                {
                    if (_browser?.WebView?.CoreWebView2 != null)
                    {
                        _ = _browser.WebView.CoreWebView2.ExecuteScriptAsync(
                            "window._ccpForcedFs = false; try { if (document.exitFullscreen && document.fullscreenElement) document.exitFullscreen(); } catch (_) {}");
                    }
                }
                catch { }

                if (_browserPopoutWindow != null)
                {
                    if (_browserFullscreenWasPopout)
                    {
                        // === Was already popped out by user — restore popout window state ===
                        _browserPopoutWindow.WindowStyle = _popoutPreFsStyle;
                        _browserPopoutWindow.ResizeMode = _popoutPreFsResize;
                        _browserPopoutWindow.Topmost = _popoutPreFsTopmost;
                        _browserPopoutWindow.Left = _popoutPreFsLeft;
                        _browserPopoutWindow.Top = _popoutPreFsTop;
                        _browserPopoutWindow.Width = _popoutPreFsWidth;
                        _browserPopoutWindow.Height = _popoutPreFsHeight;
                        _browserPopoutWindow.WindowState = _popoutPreFsState;
                    }
                    else
                    {
                        // === Was embedded — close the auto-popout to return to embedded ===
                        _browserPopoutWindow.Close();
                        // The Closed handler returns the WebView to BrowserContainer
                    }
                }

                // Restore zoom
                if (_browser != null)
                    _browser.ZoomFactor = _browserPreFullscreenZoom;

                _isBrowserFullscreen = false;
                _avatarWasAttachedBeforeBrowserFullscreen = false;

                App.Logger?.Information("Browser exited fullscreen");
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to exit browser fullscreen");
            }
        }

        // True while a remote controller's "play_hypnotube" video is showing in the embedded
        // browser. Gates StopBrowserVideoFromRemote so panic/session-end only touches the
        // browser when the controller actually started a video here — never the page the user
        // was browsing themselves.
        private bool _remoteBrowserVideoActive;

        /// <summary>
        /// Play a controller-supplied HypnoTube URL in the embedded browser (remote-control
        /// "play_hypnotube" command). Marks the browser video as remote-active so a later panic
        /// / session-end can stop it. The URL has already been allowlist-validated by
        /// RemoteControlService (HtUrlHelper.IsEligibleHtUrl).
        /// </summary>
        public void PlayHypnotubeFromRemote(string url)
        {
            _remoteBrowserVideoActive = true;
            NavigateToUrlInBrowser(url, autoPlayFullscreen: true);
        }

        /// <summary>
        /// Stop a video a remote controller started in the embedded browser (panic /
        /// session-end / controller-disconnect path). Exits forced fullscreen and navigates
        /// back to the currently-selected site's homepage — this tears down the playing
        /// &lt;video&gt; (halting playback) while leaving the browser on a usable page, rather
        /// than a dead-end about:blank. No-op unless a remote video was actually playing.
        /// </summary>
        public void StopBrowserVideoFromRemote()
        {
            if (!_remoteBrowserVideoActive) return;
            _remoteBrowserVideoActive = false;
            try
            {
                if (_isBrowserFullscreen) ExitBrowserFullscreen();
                NavigateBrowserToCurrentSiteHome();
                App.Logger?.Information("[RemoteControl] Stopped remote browser video, restored site homepage");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("StopBrowserVideoFromRemote failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Navigate the embedded browser to the homepage of whichever site (HypnoTube /
        /// BambiCloud) is currently selected in the toggle. Shared by the remote video-stop
        /// path and the toolbar Reload button — re-selecting an already-checked site radio
        /// won't fire its Checked handler, so this gives a reliable way back to a live page.
        /// </summary>
        private void NavigateBrowserToCurrentSiteHome()
        {
            if (_browser?.WebView?.CoreWebView2 == null) return;
            try
            {
                var isBambiCloud = RbBambiCloud?.IsChecked == true;
                var url = isBambiCloud ? "https://bambicloud.com/" : "https://hypnotube.com/";
                _browser.Navigate(url);
                App.Logger?.Information("Browser navigated to current site home: {Url}", url);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("NavigateBrowserToCurrentSiteHome failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Toolbar Reload button: reload the browser onto the currently-selected site's
        /// homepage (or lazy-init the browser if it was never opened). Gives the user a way
        /// out of a stuck/blank page — e.g. after a remote video was stopped.
        /// </summary>
        private void BtnReloadBrowser_Click(object sender, RoutedEventArgs e)
        {
            if (!_browserInitialized)
            {
                var initialUrl = RbHypnoTube?.IsChecked == true
                    ? "https://hypnotube.com/"
                    : "https://bambicloud.com/";
                _ = InitializeBrowserAsync(initialUrl);
                return;
            }
            if (App.Settings?.Current?.OfflineMode == true) return;
            NavigateBrowserToCurrentSiteHome();
        }

        #endregion

        #region Start/Stop

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            // Don't allow manual start/stop while remote controller is connected
            if (App.RemoteControl?.ControllerConnected == true) return;

            if (_isRunning && App.Lockdown?.IsActive == true)
            {
                MessageBox.Show(Loc.Get("msg_you_are_in_lockdown_mode_nyou_cannot_stop_dur"), Loc.Get("title_lockdown"),
                    MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            if (_isRunning)
            {
                // Check if a session is running
                if (_sessionEngine != null && _sessionEngine.IsRunning)
                {
                    var session = _sessionEngine.CurrentSession;
                    var elapsed = _sessionEngine.ElapsedTime;
                    var remaining = _sessionEngine.RemainingTime;

                    // Apply level-based XP multiplier
                    var level = App.Settings?.Current?.PlayerLevel ?? 1;
                    var multiplier = App.Progression?.GetSessionXPMultiplier(level) ?? 1.0;
                    var potentialXP = (int)Math.Round((session?.BonusXP ?? 0) * multiplier);

                    var penalty = _sessionEngine.XPPenalty;
                    var finalXP = Math.Max(0, potentialXP - penalty);

                    var penaltyText = penalty > 0
                        ? $"\n(Pause penalty: -{penalty} XP, would earn: {finalXP} XP)"
                        : "";

                    var confirmed = ShowStyledDialog(
                        "⚠ Stop Session?",
                        $"You're currently in a session:\n" +
                        $"{session?.Icon} {session?.Name}\n\n" +
                        $"Time elapsed: {((int)elapsed.TotalMinutes):D2}:{elapsed.Seconds:D2}\n" +
                        $"Time remaining: {((int)remaining.TotalMinutes):D2}:{remaining.Seconds:D2}\n\n" +
                        $"If you stop now, you will lose ALL {potentialXP} XP.{penaltyText}\n\n" +
                        "Are you sure you want to quit?",
                        "Yes, stop session", "Keep going");

                    if (!confirmed) return;

                    // Stop the session without completing it
                    _sessionEngine.StopSession(completed: false);
                    if (TxtPresetsStatus != null)
                    {
                        TxtPresetsStatus.Visibility = Visibility.Collapsed;
                        TxtPresetsStatus.Text = "";
                    }
                }
                
                // Track panic press for Relapse achievement
                App.Achievements?.TrackPanicPressed();

                // User manually stopping
                if (App.Settings.Current.SchedulerEnabled && IsInScheduledTimeWindow())
                {
                    _manuallyStoppedDuringSchedule = true;
                }
                StopEngine();
            }
            else
            {
                // User manually starting - clear manual stop flag
                _manuallyStoppedDuringSchedule = false;
                StartEngine();
            }
        }

        public void StartEngine()
        {
            SaveSettings();

            // Check for Relapse achievement (restart within 10s of ESC)
            App.Achievements?.CheckRelapse();

            var settings = App.Settings.Current;

            // Track session count and start skill tree service
            settings.TotalSessions++;
            App.SkillTree?.Start();
            App.SkillTree?.TrackTimeOfDayUsage(); // For secret skill unlocks

            App.Flash.Start();
            
            if (settings.MandatoryVideosEnabled)
                App.Video.Start();
            
            if (settings.SubliminalEnabled)
                App.Subliminal.Start();
            
            // Always start overlay service (handles spiral and pink filter)
            // This allows toggling overlays on/off while engine is running
            App.Overlay.Start();

            // Start bubble service
            if (settings.BubblesEnabled)
            {
                App.Bubbles.Start();
            }

            // Start lock card service
            if (settings.LockCardEnabled)
            {
                App.LockCard.Start();
            }

            // Start bubble count game service
            if (settings.BubbleCountEnabled)
            {
                App.BubbleCount.Start();
            }

            // Start bouncing text service
            if (settings.BouncingTextEnabled)
            {
                App.BouncingText.Start();
            }
            else
            {
                // Ensure bouncing text is stopped if disabled (cleanup any leftover state)
                App.BouncingText.Stop();
            }

            // Start mind wipe service
            if (settings.MindWipeEnabled)
            {
                App.MindWipe.Start(settings.MindWipeFrequency, settings.MindWipeVolume / 100.0);

                // Start loop mode if enabled in settings
                if (settings.MindWipeLoop)
                {
                    App.MindWipe.StartLoop(settings.MindWipeVolume / 100.0);
                }
            }

            // Start brain drain service (still gated internally by Brain Drain rework flag)
            if (settings.BrainDrainEnabled)
            {
                App.BrainDrain.Start();
            }

            // Start autonomy service (requires Patreon)
            var hasPatreonAccess = settings.PatreonTier >= 1 || App.Patreon?.IsWhitelisted == true;
            if (hasPatreonAccess && settings.AutonomyModeEnabled && settings.AutonomyConsentGiven)
            {
                App.Autonomy?.Start();
            }

            // Start pop quiz if enabled
            if (settings.PopQuizEnabled)
            {
                App.PopQuiz?.Start();
            }

            // Start pop quiz service
            if (settings.PopQuizEnabled)
            {
                App.PopQuiz?.Start();
            }

            // Start ramp timer if enabled
            if (settings.IntensityRampEnabled)
            {
                StartRampTimer();
            }

            // Browser audio serves as background - no need to play separate music

            _isRunning = true;
            App.IsEngineRunning = true;
            UpdateStartButton();

            // Start conditioning time tracker
            StartConditioningTimeTracker();

            App.Logger?.Information("Engine started - Overlay: {Overlay}, Bubbles: {Bubbles}, LockCard: {LockCard}, BubbleCount: {BubbleCount}, MindWipe: {MindWipe}, BrainDrain: {BrainDrain}",
                App.Overlay.IsRunning, App.Bubbles.IsRunning, App.LockCard.IsRunning, App.BubbleCount.IsRunning, App.MindWipe.IsRunning, App.BrainDrain.IsRunning);

            // If the mandatory video folder holds enhanced videos the current
            // settings won't fully honour (enhancement off, or webcam rules but
            // webcam not running), offer to flip the missing switch(es). Fire-and-
            // forget: scans off the UI thread and never blocks engine start.
            MaybePromptMandatoryVideoEnhancement();
        }

        public void StopEngine()
        {
            // Stop flash first (safe, no complex cleanup)
            App.Flash.Stop();

            // Stop bubbles BEFORE video to avoid UI thread contention
            // Bubbles use high-priority animation timers that can interfere with video cleanup
            App.Bubbles.Stop();
            App.BouncingText.Stop();

            // Now stop video (complex LibVLC cleanup)
            App.Video.Stop();

            // Stop other services
            App.Subliminal.Stop();
            App.Overlay.Stop();
            App.LockCard.Stop();
            App.BubbleCount.Stop();
            App.MindWipe.Stop();
            App.BrainDrain.Stop();
            App.PopQuiz?.Stop();
            // Only stop autonomy if it was started by the session engine (i.e., user didn't enable it independently).
            // If the user has autonomy enabled in settings, let it keep running after session ends.
            var s = App.Settings?.Current;
            var hasPatreon = (s?.PatreonTier ?? 0) >= 1 || App.Patreon?.IsWhitelisted == true;
            if (!(hasPatreon && s != null && s.AutonomyModeEnabled && s.AutonomyConsentGiven))
            {
                App.Autonomy?.Stop();
            }
            App.SkillTree?.Stop();
            App.Audio.ForceUnduck();

            // Force close any open lock card / quiz windows (panic button should close them immediately)
            LockCardWindow.ForceCloseAll();
            BubbleCountWindow.ForceCloseAll();
            BubbleCountResultWindow.ForceCloseAll();
            QuizWindow.ForceCloseAll();
            PopQuizWindow.ForceCloseAll();

            // Stop ramp timer and reset sliders
            StopRampTimer();

            // Stop conditioning time tracker
            StopConditioningTimeTracker();

            _isRunning = false;
            App.IsEngineRunning = false;
            UpdateStartButton();

            // Fire event for avatar reaction
            EngineStopped?.Invoke(this, EventArgs.Empty);

            // Release cached images and compact the Large Object Heap.
            // Flash/overlay BitmapSources are large allocations (>85 KB) that fragment
            // the LOH during sessions. Compacting here returns memory to the OS.
            App.Flash.ClearImageCache();
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Optimized, blocking: false);

            App.Logger?.Information("Engine stopped");
        }

        private void StartRampTimer()
        {
            var settings = App.Settings.Current;
            
            // Store base values
            _rampBaseValues["FlashOpacity"] = settings.FlashOpacity;
            _rampBaseValues["SpiralOpacity"] = settings.SpiralOpacity;
            _rampBaseValues["PinkFilterOpacity"] = settings.PinkFilterOpacity;
            _rampBaseValues["MasterVolume"] = settings.MasterVolume;
            _rampBaseValues["SubAudioVolume"] = settings.SubAudioVolume;
            
            _rampStartTime = DateTime.Now;
            
            _rampTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2) // Update every 2 seconds
            };
            _rampTimer.Tick += RampTimer_Tick;
            _rampTimer.Start();
            
            App.Logger?.Information("Ramp timer started - Duration: {Duration}min, Multiplier: {Mult}x", 
                settings.RampDurationMinutes, settings.SchedulerMultiplier);
        }

        private void StopRampTimer()
        {
            _rampTimer?.Stop();
            _rampTimer = null;
            
            // Reset sliders and settings to base values
            if (_rampBaseValues.Count > 0)
            {
                var settings = App.Settings.Current;
                
                if (_rampBaseValues.TryGetValue("FlashOpacity", out var flashOp))
                {
                    SliderOpacity.Value = flashOp;
                    TxtOpacity.Text = $"{(int)flashOp}%";
                    settings.FlashOpacity = (int)flashOp;
                }
                if (_rampBaseValues.TryGetValue("SpiralOpacity", out var spiralOp))
                {
                    SliderSpiralOpacity.Value = spiralOp;
                    TxtSpiralOpacity.Text = $"{(int)spiralOp}%";
                    settings.SpiralOpacity = (int)spiralOp;
                }
                if (_rampBaseValues.TryGetValue("PinkFilterOpacity", out var pinkOp))
                {
                    SliderPinkOpacity.Value = pinkOp;
                    TxtPinkOpacity.Text = $"{(int)pinkOp}%";
                    settings.PinkFilterOpacity = (int)pinkOp;
                }
                if (_rampBaseValues.TryGetValue("MasterVolume", out var masterVol))
                {
                    SliderMaster.Value = masterVol;
                    TxtMaster.Text = $"{(int)masterVol}%";
                    settings.MasterVolume = (int)masterVol;
                }
                if (_rampBaseValues.TryGetValue("SubAudioVolume", out var subVol))
                {
                    SliderWhisperVol.Value = subVol;
                    TxtWhisperVol.Text = $"{(int)subVol}%";
                    settings.SubAudioVolume = (int)subVol;
                }
                
                _rampBaseValues.Clear();
                App.Logger?.Information("Ramp timer stopped - values reset to base");
            }
        }

        private void RampTimer_Tick(object? sender, EventArgs e)
        {
            var settings = App.Settings.Current;
            var elapsed = (DateTime.Now - _rampStartTime).TotalMinutes;
            var duration = settings.RampDurationMinutes;
            var multiplier = settings.SchedulerMultiplier;

            // Skip visual effect ramping if a session is active - sessions have their own built-in ramping
            // This prevents the two systems from fighting and causing values to jump around
            var sessionActive = _sessionEngine?.IsRunning == true;

            // Calculate progress (0.0 to 1.0)
            var progress = Math.Min(elapsed / duration, 1.0);

            // Calculate current multiplier based on progress (linear interpolation from 1.0 to max)
            var currentMult = 1.0 + (multiplier - 1.0) * progress;

            // Update linked sliders and settings
            Dispatcher.Invoke(() =>
            {
                // Only apply visual effect ramps when no session is active
                if (!sessionActive && settings.RampLinkFlashOpacity && _rampBaseValues.TryGetValue("FlashOpacity", out var flashBase))
                {
                    var newVal = (int)Math.Min(flashBase * currentMult, 100);
                    SliderOpacity.Value = newVal;
                    TxtOpacity.Text = $"{newVal}%";
                    settings.FlashOpacity = newVal;
                }

                if (!sessionActive && settings.RampLinkSpiralOpacity && _rampBaseValues.TryGetValue("SpiralOpacity", out var spiralBase))
                {
                    var newVal = (int)Math.Min(spiralBase * currentMult, 50);
                    SliderSpiralOpacity.Value = newVal;
                    TxtSpiralOpacity.Text = $"{newVal}%";
                    settings.SpiralOpacity = newVal;
                }
                
                if (!sessionActive && settings.RampLinkPinkFilterOpacity && _rampBaseValues.TryGetValue("PinkFilterOpacity", out var pinkBase))
                {
                    var newVal = (int)Math.Min(pinkBase * currentMult, 50);
                    SliderPinkOpacity.Value = newVal;
                    TxtPinkOpacity.Text = $"{newVal}%";
                    settings.PinkFilterOpacity = newVal;
                }
                
                if (settings.RampLinkMasterAudio && _rampBaseValues.TryGetValue("MasterVolume", out var masterBase))
                {
                    var newVal = (int)Math.Min(masterBase * currentMult, 100);
                    SliderMaster.Value = newVal;
                    TxtMaster.Text = $"{newVal}%";
                    settings.MasterVolume = newVal;
                }
                
                if (settings.RampLinkSubliminalAudio && _rampBaseValues.TryGetValue("SubAudioVolume", out var subBase))
                {
                    var newVal = (int)Math.Min(subBase * currentMult, 100);
                    SliderWhisperVol.Value = newVal;
                    TxtWhisperVol.Text = $"{newVal}%";
                    settings.SubAudioVolume = newVal;
                }
            });
            
            // Check if ramp is complete and should end session
            if (progress >= 1.0 && settings.EndSessionOnRampComplete)
            {
                App.Logger?.Information("Ramp complete - ending session");
                Dispatcher.Invoke(() =>
                {
                    _trayIcon?.ShowNotification("Session Complete", "Intensity ramp finished. Stopping...", System.Windows.Forms.ToolTipIcon.Info);
                    StopEngine();
                });
            }
        }

        #endregion

        #region Scheduler

        private void CheckSchedulerOnStartup()
        {
            var settings = App.Settings.Current;
            App.Logger?.Information("Scheduler startup check: Enabled={Enabled}, InWindow={InWindow}",
                settings.SchedulerEnabled, IsInScheduledTimeWindow());

            if (!settings.SchedulerEnabled) return;

            if (IsInScheduledTimeWindow())
            {
                App.Logger?.Information("Scheduler: App started within scheduled time window - auto-starting");

                // Minimize to tray and start engine
                _trayIcon?.MinimizeToTray();
                _trayIcon?.ShowNotification("Scheduler Active", "Session auto-started based on schedule.", System.Windows.Forms.ToolTipIcon.Info);

                StartEngine();
                _schedulerAutoStarted = true;
            }
        }

        private void CheckSchedulerAfterSettingsChange()
        {
            var settings = App.Settings.Current;
            if (!settings.SchedulerEnabled) return;

            App.Logger?.Information("Scheduler settings changed - checking time window");

            if (IsInScheduledTimeWindow() && !_isRunning)
            {
                App.Logger?.Information("Scheduler: In time window after settings change - auto-starting");

                _trayIcon?.ShowNotification("Scheduler Active", "Session auto-started based on schedule.", System.Windows.Forms.ToolTipIcon.Info);

                StartEngine();
                _schedulerAutoStarted = true;
            }
        }

        private void SchedulerTimer_Tick(object? sender, EventArgs e)
        {
            var settings = App.Settings.Current;
            if (!settings.SchedulerEnabled) return;
            
            bool inWindow = IsInScheduledTimeWindow();
            
            if (inWindow && !_isRunning && !_schedulerAutoStarted && !_manuallyStoppedDuringSchedule)
            {
                // Time to start!
                App.Logger?.Information("Scheduler: Entering scheduled time window - auto-starting");
                
                Dispatcher.Invoke(() =>
                {
                    _trayIcon?.MinimizeToTray();
                    _trayIcon?.ShowNotification("Scheduler Active", "Session auto-started based on schedule.", System.Windows.Forms.ToolTipIcon.Info);

                    StartEngine();
                    _schedulerAutoStarted = true;
                });
            }
            else if (!inWindow && _isRunning && _schedulerAutoStarted)
            {
                // Time to stop!
                App.Logger?.Information("Scheduler: Exiting scheduled time window - auto-stopping");
                
                Dispatcher.Invoke(() =>
                {
                    StopEngine();
                    _schedulerAutoStarted = false;
                    _trayIcon?.ShowNotification("Scheduler", "Scheduled session ended.", System.Windows.Forms.ToolTipIcon.Info);
                });
            }
            else if (!inWindow)
            {
                // Outside window - reset flags for next window
                _schedulerAutoStarted = false;
                _manuallyStoppedDuringSchedule = false;
            }
        }

        private bool IsInScheduledTimeWindow()
        {
            var settings = App.Settings.Current;
            var now = DateTime.Now;

            // Check if today is an active day
            bool isDayActive = now.DayOfWeek switch
            {
                DayOfWeek.Monday => settings.SchedulerMonday,
                DayOfWeek.Tuesday => settings.SchedulerTuesday,
                DayOfWeek.Wednesday => settings.SchedulerWednesday,
                DayOfWeek.Thursday => settings.SchedulerThursday,
                DayOfWeek.Friday => settings.SchedulerFriday,
                DayOfWeek.Saturday => settings.SchedulerSaturday,
                DayOfWeek.Sunday => settings.SchedulerSunday,
                _ => false
            };

            if (!isDayActive)
            {
                App.Logger?.Debug("Scheduler: {Day} is not an active day", now.DayOfWeek);
                return false;
            }

            // Parse start and end times
            if (!TimeSpan.TryParse(settings.SchedulerStartTime, out var startTime))
            {
                App.Logger?.Warning("Scheduler: Could not parse start time '{Time}', using default 16:00", settings.SchedulerStartTime);
                startTime = new TimeSpan(16, 0, 0);
            }

            if (!TimeSpan.TryParse(settings.SchedulerEndTime, out var endTime))
            {
                App.Logger?.Warning("Scheduler: Could not parse end time '{Time}', using default 22:00", settings.SchedulerEndTime);
                endTime = new TimeSpan(22, 0, 0);
            }

            var currentTime = now.TimeOfDay;

            bool inWindow;
            // Handle case where end time is after midnight (e.g., 22:00 - 02:00)
            if (endTime < startTime)
            {
                // Overnight schedule
                inWindow = currentTime >= startTime || currentTime < endTime;
            }
            else
            {
                // Same-day schedule
                inWindow = currentTime >= startTime && currentTime < endTime;
            }

            App.Logger?.Debug("Scheduler: Current={Current}, Start={Start}, End={End}, InWindow={InWindow}",
                currentTime.ToString(@"hh\:mm"), startTime.ToString(@"hh\:mm"), endTime.ToString(@"hh\:mm"), inWindow);

            return inWindow;
        }

        #endregion

        #region Engine Helpers

        /// <summary>
        /// Apply current UI values to settings immediately (for live updates)
        /// </summary>
        private void ApplySettingsLive()
        {
            if (_isLoading) return;

            var s = App.Settings.Current;

            // Track previous values to detect changes
            var oldFlashFreq = s.FlashFrequency;
            var wasFlashEnabled = s.FlashEnabled;
            var wasVideoEnabled = s.MandatoryVideosEnabled;
            var wasSubliminalEnabled = s.SubliminalEnabled;

            // Flash settings
            s.FlashEnabled = ChkFlashEnabled.IsChecked ?? true;
            s.FlashClickable = ChkClickable.IsChecked ?? true;
            s.CorruptionMode = ChkCorruption.IsChecked ?? false;
            s.HydraLinkedTiming = ChkHydraLinked.IsChecked ?? true;
            s.FlashGlowEnabled = ChkFlashGlow.IsChecked ?? true;
            s.FlashFrequency = (int)SliderPerMin.Value;
            s.SimultaneousImages = (int)SliderImages.Value;
            s.HydraLimit = (int)SliderMaxOnScreen.Value;
            s.ImageScale = (int)SliderSize.Value;
            s.FlashOpacity = (int)SliderOpacity.Value;
            s.FadeDuration = (int)SliderFade.Value;

            // Video settings
            s.MandatoryVideosEnabled = ChkVideoEnabled.IsChecked ?? false;
            s.VideosPerHour = (int)SliderPerHour.Value;
            s.StrictLockEnabled = ChkStrictLock.IsChecked ?? false;
            s.AttentionChecksEnabled = ChkMiniGameEnabled.IsChecked ?? false;
            s.AttentionDensity = (int)SliderTargets.Value;
            s.RandomizeAttentionTargets = ChkRandomizeTargets.IsChecked ?? false;
            s.AttentionLifespan = (int)SliderDuration.Value;
            s.AttentionSize = (int)SliderTargetSize.Value;

            // Subliminal settings
            s.SubliminalEnabled = ChkSubliminalEnabled.IsChecked ?? false;
            s.SubliminalFrequency = (int)SliderSubPerMin.Value;
            s.SubliminalDuration = (int)SliderFrames.Value;
            s.SubliminalOpacity = (int)SliderSubOpacity.Value;
            s.SubAudioEnabled = ChkAudioWhispers.IsChecked ?? false;
            s.SubAudioVolume = (int)SliderWhisperVol.Value;

            // Audio settings
            s.MasterVolume = (int)SliderMaster.Value;
            s.AudioDuckingEnabled = ChkAudioDuck.IsChecked ?? true;
            s.DuckingLevel = (int)SliderDuck.Value;
            s.ExcludeBambiCloudFromDucking = ChkExcludeBambiCloudDucking.IsChecked ?? true;

            // Overlay settings
            s.SpiralOpacity = (int)SliderSpiralOpacity.Value;
            s.PinkFilterOpacity = (int)SliderPinkOpacity.Value;

            // Refresh services if running
            if (_isRunning)
            {
                // Handle Flash service toggle
                if (s.FlashEnabled != wasFlashEnabled)
                {
                    if (s.FlashEnabled)
                        App.Flash.Start();
                    else
                        App.Flash.Stop();
                    App.Logger?.Information("Flash images toggled via ApplySettingsLive: {Enabled}", s.FlashEnabled);
                }
                // Reschedule flash timer if frequency changed
                else if (s.FlashFrequency != oldFlashFreq)
                {
                    App.Flash.RefreshSchedule();
                }

                // Handle Video service toggle
                if (s.MandatoryVideosEnabled != wasVideoEnabled)
                {
                    if (s.MandatoryVideosEnabled)
                        App.Video.Start();
                    else
                        App.Video.Stop();
                    App.Logger?.Information("Mandatory videos toggled via ApplySettingsLive: {Enabled}", s.MandatoryVideosEnabled);
                }

                // Handle Subliminal service toggle
                if (s.SubliminalEnabled != wasSubliminalEnabled)
                {
                    if (s.SubliminalEnabled)
                        App.Subliminal.Start();
                    else
                        App.Subliminal.Stop();
                    App.Logger?.Information("Subliminals toggled via ApplySettingsLive: {Enabled}", s.SubliminalEnabled);
                }

                // Refresh overlays (spiral, pink filter)
                App.Overlay.RefreshOverlays();
            }

            // Save settings to disk
            App.Settings.Save();
        }

        private void UpdateStartButton()
        {
            // Don't overwrite the remote control label while controller is connected
            if (App.RemoteControl?.ControllerConnected == true) return;

            if (_isRunning)
            {
                BtnStart.Background = new SolidColorBrush(Color.FromRgb(255, 107, 107)); // Red
                BtnStart.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Height = 24,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = "■", FontSize = 16, Width = 20, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center },
                        new TextBlock { Text = "STOP", FontSize = 18, Width = 60, VerticalAlignment = VerticalAlignment.Center }
                    }
                };

                // Also update Presets tab button using direct reference
                if (TxtPresetsStatus != null)
                {
                    TxtPresetsStatus.Text = Loc.Get("label_running");
                }
            }
            else
            {
                BtnStart.Background = FindResource("PinkBrush") as SolidColorBrush;
                BtnStart.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Height = 24,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = "▶", FontSize = 16, Width = 20, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center },
                        new TextBlock { Text = "START", FontSize = 18, Width = 60, VerticalAlignment = VerticalAlignment.Center }
                    }
                };

                // Also update Presets tab button
                if (TxtPresetsStatus != null)
                {
                    TxtPresetsStatus.Text = "";
                }
            }
        }

        private void UpdateStartButtonForRemoteControl(bool connected)
        {
            if (connected)
            {
                BtnStart.IsEnabled = false;
                BtnStart.Background = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x88)); // Green
                BtnStart.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Height = 24,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = "\U0001F3AE", FontSize = 16, Width = 24, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center },
                        new TextBlock { Text = "REMOTE CONNECTED", FontSize = 14, VerticalAlignment = VerticalAlignment.Center }
                    }
                };
            }
            else
            {
                BtnStart.IsEnabled = true;
                // Directly set button state — don't delegate to UpdateStartButton() which has a
                // ControllerConnected guard that can prevent restoration due to event timing
                if (_isRunning)
                {
                    BtnStart.Background = new SolidColorBrush(Color.FromRgb(255, 107, 107)); // Red
                    BtnStart.Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Height = 24,
                        VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            new TextBlock { Text = "■", FontSize = 16, Width = 20, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center },
                            new TextBlock { Text = "STOP", FontSize = 18, Width = 60, VerticalAlignment = VerticalAlignment.Center }
                        }
                    };
                }
                else
                {
                    BtnStart.Background = FindResource("PinkBrush") as SolidColorBrush;
                    BtnStart.Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Height = 24,
                        VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            new TextBlock { Text = "▶", FontSize = 16, Width = 20, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center },
                            new TextBlock { Text = "START", FontSize = 18, Width = 60, VerticalAlignment = VerticalAlignment.Center }
                        }
                    };
                }
            }
        }

        /// <summary>
        /// Find a visual child by name in the visual tree
        /// </summary>
        private static T? FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T element && element.Name == name)
                    return element;
                var result = FindVisualChild<T>(child, name);
                if (result != null)
                    return result;
            }
            return null;
        }

        #endregion

        #region Settings Load/Save

        private void LoadSettings()
        {
            var s = App.Settings.Current;

            // Flash
            ChkFlashEnabled.IsChecked = s.FlashEnabled;
            ChkClickable.IsChecked = s.FlashClickable;
            ChkCorruption.IsChecked = s.CorruptionMode;
            ChkHydraLinked.IsChecked = s.HydraLinkedTiming;
            ChkFlashGlow.IsChecked = s.FlashGlowEnabled;
            SliderPerMin.Value = s.FlashFrequency;
            SliderImages.Value = s.SimultaneousImages;
            SliderMaxOnScreen.Value = s.HydraLimit;

            // Visuals
            SliderSize.Value = s.ImageScale;
            SliderOpacity.Value = s.FlashOpacity;
            SliderFade.Value = s.FadeDuration;
            SliderFlashDuration.Value = s.FlashDuration;
            ChkFlashAudio.IsChecked = s.FlashAudioEnabled;
            SliderFlashDuration.IsEnabled = !s.FlashAudioEnabled;
            SliderFlashDuration.Opacity = s.FlashAudioEnabled ? 0.5 : 1.0;
            
            // Set audio link state based on frequency
            _isLoading = false;
            UpdateAudioLinkState();
            _isLoading = true;

            // Video
            ChkVideoEnabled.IsChecked = s.MandatoryVideosEnabled;
            SliderPerHour.Value = s.VideosPerHour;
            ChkStrictLock.IsChecked = s.StrictLockEnabled;
            ChkMiniGameEnabled.IsChecked = s.AttentionChecksEnabled;
            SliderTargets.Value = s.AttentionDensity;
            ChkRandomizeTargets.IsChecked = s.RandomizeAttentionTargets;
            SliderDuration.Value = s.AttentionLifespan;
            SliderTargetSize.Value = s.AttentionSize;

            // Subliminals
            ChkSubliminalEnabled.IsChecked = s.SubliminalEnabled;
            SliderSubPerMin.Value = s.SubliminalFrequency;
            SliderFrames.Value = s.SubliminalDuration;
            SliderSubOpacity.Value = s.SubliminalOpacity;
            ChkAudioWhispers.IsChecked = s.SubAudioEnabled;
            SliderWhisperVol.Value = s.SubAudioVolume;

            // System
            ChkDualMon.IsChecked = s.DualMonitorEnabled;
            ChkWinStart.IsChecked = s.RunOnStartup;
            ChkVidLaunch.IsChecked = s.ForceVideoOnLaunch;
            ChkAutoRun.IsChecked = s.AutoStartEngine;
            ChkStartHidden.IsChecked = s.StartMinimized;
            ChkNoPanic.IsChecked = !s.PanicKeyEnabled;
            ChkOfflineMode.IsChecked = s.OfflineMode;
            if (ChkPerformanceMode != null) ChkPerformanceMode.IsChecked = s.PerformanceMode;
            if (ChkAutoPerformance != null) ChkAutoPerformance.IsChecked = s.AutoPerformanceMode;
            if (ChkVideoHwDecode != null) ChkVideoHwDecode.IsChecked = s.VideoHardwareDecoding;
            ChkStopEffectsOnRemoteDisconnect.IsChecked = s.StopEffectsOnRemoteDisconnect;
            if (ChkRemoteShareAvatar != null) ChkRemoteShareAvatar.IsChecked = s.RemoteShareAvatar;

            // Emote picker preset list (bound here so OnDeserialized normalization
            // has already run and the ItemsControl always sees exactly 5 entries).
            if (LstEmotePresets != null) LstEmotePresets.ItemsSource = s.RemoteEmotePresets;

            // Splash-overlay (big) picker — same source list, split into two rows
            // around the End Session button via index-keyed ListCollectionView filters.
            // Items are the SAME EmotePreset references as the small picker, so edits
            // in the small picker propagate via INotifyPropertyChanged.
            if (LstEmotePresetsBigTop != null)
            {
                var topView = new System.Windows.Data.ListCollectionView(s.RemoteEmotePresets)
                {
                    Filter = item => s.RemoteEmotePresets.IndexOf((Models.EmotePreset)item) < 3
                };
                LstEmotePresetsBigTop.ItemsSource = topView;
            }
            if (LstEmotePresetsBigBottom != null)
            {
                var bottomView = new System.Windows.Data.ListCollectionView(s.RemoteEmotePresets)
                {
                    Filter = item => s.RemoteEmotePresets.IndexOf((Models.EmotePreset)item) >= 3
                };
                LstEmotePresetsBigBottom.ItemsSource = bottomView;
            }

            // Deeper
            if (ChkEnableDeeper != null) ChkEnableDeeper.IsChecked = s.EnableDeeper;
            if (BtnDeeper != null) BtnDeeper.Visibility = s.EnableDeeper ? Visibility.Visible : Visibility.Collapsed;

            // Update UI for offline mode state (disable login buttons, browser, etc.)
            if (s.OfflineMode)
            {
                UpdateOfflineModeUI(true);
            }

            // Startup video display
            if (!string.IsNullOrEmpty(s.StartupVideoPath) && System.IO.File.Exists(s.StartupVideoPath))
            {
                TxtStartupVideo.Text = System.IO.Path.GetFileName(s.StartupVideoPath);
            }
            else
            {
                TxtStartupVideo.Text = Loc.Get("label_random");
            }

            // Audio
            SliderMaster.Value = s.MasterVolume;
            SliderVideoVolume.Value = s.VideoVolume;
            ChkAudioDuck.IsChecked = s.AudioDuckingEnabled;
            SliderDuck.Value = s.DuckingLevel;
            ChkExcludeBambiCloudDucking.IsChecked = s.ExcludeBambiCloudFromDucking;
            PopulateAudioOutputDevices();

            // Progression
            ChkSpiralEnabled.IsChecked = s.SpiralEnabled;
            SliderSpiralOpacity.Value = s.SpiralOpacity;
            ChkPinkFilterEnabled.IsChecked = s.PinkFilterEnabled;
            SliderPinkOpacity.Value = s.PinkFilterOpacity;
            ChkBubblesEnabled.IsChecked = s.BubblesEnabled;
            SliderBubbleFreq.Value = s.BubblesFrequency;
            SliderBubbleVolume.Value = s.BubblesVolume;
            ChkLockCardEnabled.IsChecked = s.LockCardEnabled;
            SliderLockCardFreq.Value = s.LockCardFrequency;
            SliderLockCardRepeats.Value = s.LockCardRepeats;
            ChkLockCardStrict.IsChecked = s.LockCardStrict;
            ChkBubbleCountEnabled.IsChecked = s.BubbleCountEnabled;
            ChkBubbleCountStrict.IsChecked = s.BubbleCountStrictLock;
            SliderBubbleCountFreq.Value = s.BubbleCountFrequency;
            TxtBubbleCountFreq.Text = s.BubbleCountFrequency.ToString();
            CmbBubbleCountDifficulty.SelectedIndex = s.BubbleCountDifficulty;
            ChkBouncingTextEnabled.IsChecked = s.BouncingTextEnabled;
            ChkBouncingTextAlwaysOnTop.IsChecked = s.BouncingTextAlwaysOnTop;

            // Mind Wipe
            ChkMindWipeEnabled.IsChecked = s.MindWipeEnabled;
            SliderMindWipeFreq.Value = s.MindWipeFrequency;
            SliderMindWipeVolume.Value = s.MindWipeVolume;
            ChkMindWipeLoop.IsChecked = s.MindWipeLoop;

            // Brain Drain
            ChkBrainDrainEnabled.IsChecked = s.BrainDrainEnabled;
            SliderBrainDrainIntensity.Value = s.BrainDrainIntensity;
            ChkBrainDrainHighRefresh.IsChecked = s.BrainDrainHighRefresh;

            // Autonomy Mode
            ChkAutonomyEnabled.IsChecked = s.AutonomyModeEnabled;
            UpdateAutonomyButtonState(s.AutonomyModeEnabled);
            SliderAutonomyIntensity.Value = s.AutonomyIntensity;
            SliderAutonomyCooldown.Value = s.AutonomyCooldownSeconds;
            SliderAutonomyInterval.Value = s.AutonomyRandomIntervalSeconds;
            ChkAutonomyIdle.IsChecked = s.AutonomyIdleTriggerEnabled;
            ChkAutonomyRandom.IsChecked = s.AutonomyRandomTriggerEnabled;
            ChkAutonomyTimeAware.IsChecked = s.AutonomyTimeAwareEnabled;
            ChkAutonomyFlash.IsChecked = s.AutonomyCanTriggerFlash;
            ChkAutonomyVideo.IsChecked = s.AutonomyCanTriggerVideo;
            ChkAutonomyWebVideo.IsChecked = s.AutonomyCanTriggerWebVideo;
            ChkAutonomySubliminal.IsChecked = s.AutonomyCanTriggerSubliminal;
            ChkAutonomyBubbles.IsChecked = s.AutonomyCanTriggerBubbles;
            ChkAutonomyComment.IsChecked = s.AutonomyCanComment;
            ChkAutonomyMindWipe.IsChecked = s.AutonomyCanTriggerMindWipe;
            ChkAutonomyLockCard.IsChecked = s.AutonomyCanTriggerLockCard;
            ChkAutonomySpiral.IsChecked = s.AutonomyCanTriggerSpiral;
            ChkAutonomyPinkFilter.IsChecked = s.AutonomyCanTriggerPinkFilter;
            ChkAutonomyBouncingText.IsChecked = s.AutonomyCanTriggerBouncingText;
            ChkAutonomyBubbleCount.IsChecked = s.AutonomyCanTriggerBubbleCount;
            SliderAutonomyAnnounce.Value = s.AutonomyAnnouncementChance;

            // Bouncing Text Size (add if not already loaded above)
            SliderBouncingTextSize.Value = s.BouncingTextSize;

            // Scheduler
            ChkSchedulerEnabled.IsChecked = s.SchedulerEnabled;
            TxtStartTime.Text = s.SchedulerStartTime;
            TxtEndTime.Text = s.SchedulerEndTime;
            ChkMon.IsChecked = s.SchedulerMonday;
            ChkTue.IsChecked = s.SchedulerTuesday;
            ChkWed.IsChecked = s.SchedulerWednesday;
            ChkThu.IsChecked = s.SchedulerThursday;
            ChkFri.IsChecked = s.SchedulerFriday;
            ChkSat.IsChecked = s.SchedulerSaturday;
            ChkSun.IsChecked = s.SchedulerSunday;
            ChkRampEnabled.IsChecked = s.IntensityRampEnabled;
            SliderRampDuration.Value = s.RampDurationMinutes;
            SliderMultiplier.Value = s.SchedulerMultiplier;
            
            // Ramp Links
            ChkRampLinkFlash.IsChecked = s.RampLinkFlashOpacity;
            ChkRampLinkSpiral.IsChecked = s.RampLinkSpiralOpacity;
            ChkRampLinkPink.IsChecked = s.RampLinkPinkFilterOpacity;
            ChkRampLinkMaster.IsChecked = s.RampLinkMasterAudio;
            ChkRampLinkSubAudio.IsChecked = s.RampLinkSubliminalAudio;
            ChkEndAtRamp.IsChecked = s.EndSessionOnRampComplete;

            // Haptics
            ChkHapticsEnabled.IsChecked = s.Haptics.Enabled;
            SliderHapticIntensity.Value = s.Haptics.GlobalIntensity * 100;

            // Set provider combo box first
            foreach (System.Windows.Controls.ComboBoxItem item in CmbHapticProvider.Items)
            {
                if (item.Tag?.ToString() == s.Haptics.Provider.ToString())
                {
                    CmbHapticProvider.SelectedItem = item;
                    break;
                }
            }

            // Then set URL based on provider
            TxtHapticUrl.Text = s.Haptics.Provider switch
            {
                Services.Haptics.HapticProviderType.Lovense => s.Haptics.LovenseUrl,
                Services.Haptics.HapticProviderType.Buttplug => s.Haptics.ButtplugUrl,
                _ => s.Haptics.LovenseUrl
            };

            // Set hint text based on provider
            TxtHapticUrlHint.Text = s.Haptics.Provider switch
            {
                Services.Haptics.HapticProviderType.Lovense => "Lovense: Enter IP from Lovense Remote → Settings → Game Mode (http://IP:30010)",
                Services.Haptics.HapticProviderType.Buttplug => "Buttplug: Start Intiface Central, use default ws://localhost:12345",
                _ => "Lovense: Enter IP from Lovense Remote → Settings → Game Mode (http://IP:30010)"
            };

            // Auto-connect setting
            ChkHapticAutoConnect.IsChecked = s.Haptics.AutoConnect;

            // Per-feature haptic settings
            ChkHapticBubble.IsChecked = s.Haptics.BubblePopEnabled;
            SliderHapticBubble.Value = s.Haptics.BubblePopIntensity * 100;
            ChkHapticFlashDisplay.IsChecked = s.Haptics.FlashDisplayEnabled;
            SliderHapticFlashDisplay.Value = s.Haptics.FlashDisplayIntensity * 100;
            ChkHapticFlashClick.IsChecked = s.Haptics.FlashClickEnabled;
            SliderHapticFlashClick.Value = s.Haptics.FlashClickIntensity * 100;
            ChkHapticVideo.IsChecked = s.Haptics.VideoEnabled;
            SliderHapticVideo.Value = s.Haptics.VideoIntensity * 100;
            ChkHapticTargetHit.IsChecked = s.Haptics.TargetHitEnabled;
            SliderHapticTargetHit.Value = s.Haptics.TargetHitIntensity * 100;
            ChkHapticSubliminal.IsChecked = s.Haptics.SubliminalEnabled;
            SliderHapticSubliminal.Value = s.Haptics.SubliminalIntensity * 100;
            ChkHapticLevelUp.IsChecked = s.Haptics.LevelUpEnabled;
            SliderHapticLevelUp.Value = s.Haptics.LevelUpIntensity * 100;
            ChkHapticAchievement.IsChecked = s.Haptics.AchievementEnabled;
            SliderHapticAchievement.Value = s.Haptics.AchievementIntensity * 100;
            ChkHapticBouncingText.IsChecked = s.Haptics.BouncingTextEnabled;
            SliderHapticBouncingText.Value = s.Haptics.BouncingTextIntensity * 100;

            // Per-feature haptic mode dropdowns
            CmbHapticBubbleMode.SelectedIndex = (int)s.Haptics.BubblePopMode;
            CmbHapticFlashDisplayMode.SelectedIndex = (int)s.Haptics.FlashDisplayMode;
            CmbHapticFlashClickMode.SelectedIndex = (int)s.Haptics.FlashClickMode;
            CmbHapticVideoMode.SelectedIndex = (int)s.Haptics.VideoMode;
            CmbHapticTargetHitMode.SelectedIndex = (int)s.Haptics.TargetHitMode;
            CmbHapticSubliminalMode.SelectedIndex = (int)s.Haptics.SubliminalMode;
            CmbHapticLevelUpMode.SelectedIndex = (int)s.Haptics.LevelUpMode;
            CmbHapticAchievementMode.SelectedIndex = (int)s.Haptics.AchievementMode;
            CmbHapticBouncingTextMode.SelectedIndex = (int)s.Haptics.BouncingTextMode;

            // Keyword Triggers
            {
                SliderKeywordBufferTimeout.Value = s.KeywordBufferTimeoutMs;
                SliderKeywordGlobalCooldown.Value = s.KeywordGlobalCooldownSeconds;
                SliderKeywordSessionMultiplier.Value = s.KeywordSessionMultiplier;

                var hasKeywordAccess = KeywordTriggerService.HasAccess();

                // Show/hide lock indicator
                if (TxtKeywordTriggersLocked != null)
                    TxtKeywordTriggersLocked.Visibility = hasKeywordAccess ? Visibility.Collapsed : Visibility.Visible;
                if (BtnKeywordTriggersStartStop != null)
                    BtnKeywordTriggersStartStop.IsEnabled = hasKeywordAccess;

                UpdateKeywordTriggersButtonState();
                RefreshKeywordTriggerList();

                // Screen OCR
                if (ChkScreenOcrEnabled != null)
                {
                    ChkScreenOcrEnabled.IsChecked = s.ScreenOcrEnabled;
                    ChkScreenOcrEnabled.IsEnabled = hasKeywordAccess;
                    SliderScreenOcrInterval.Value = s.ScreenOcrIntervalMs / 1000.0;
                    ScreenOcrIntervalPanel.Visibility = s.ScreenOcrEnabled && hasKeywordAccess ? Visibility.Visible : Visibility.Collapsed;
                    if (CmbOcrConfirmation != null)
                        CmbOcrConfirmation.SelectedIndex = Math.Clamp(s.OcrConfirmationScans - 1, 0, 2);
                }
                if (ChkKeywordHighlightEnabled != null)
                {
                    ChkKeywordHighlightEnabled.IsChecked = s.KeywordHighlightEnabled;
                    if (HighlightDurationPanel != null)
                    {
                        HighlightDurationPanel.Visibility = s.KeywordHighlightEnabled ? Visibility.Visible : Visibility.Collapsed;
                        SliderKeywordHighlightDuration.Value = s.KeywordHighlightDurationMs / 1000.0;
                        TxtKeywordHighlightDuration.Text = $"{s.KeywordHighlightDurationMs / 1000.0:0.0}s";
                        if (CmbOcrHighlightMode != null)
                            CmbOcrHighlightMode.SelectedIndex = s.OcrHighlightAll ? 0 : 1;
                        if (ChkHighlightVisibleInCapture != null)
                            ChkHighlightVisibleInCapture.IsChecked = s.OcrHighlightVisibleInCapture;
                    }
                }
            }

            // Discord Sharing Settings
            if (ChkDiscordTabShowOnline != null) ChkDiscordTabShowOnline.IsChecked = s.ShowOnlineStatus;

            // Update Discord UI (both main tab and Patreon tab)
            UpdateQuickDiscordUI();

            // Update level display
            UpdateLevelDisplay();

            // Update all slider text displays
            UpdateSliderTexts();

            // Start autonomy service if it was enabled (works independently of engine)
            var hasPatreonAccess = s.PatreonTier >= 1 || App.Patreon?.IsWhitelisted == true;
            if (hasPatreonAccess && s.AutonomyModeEnabled && s.AutonomyConsentGiven)
            {
                App.Autonomy?.Start();
                App.Logger?.Debug("MainWindow: Started autonomy service on settings load");
            }
        }

        /// <summary>
        /// Updates all slider text displays to match current slider values
        /// Called after loading settings since the value changed events are suppressed during load
        /// </summary>
        private void UpdateSliderTexts()
        {
            // Flash sliders
            if (TxtPerMin != null) TxtPerMin.Text = ((int)SliderPerMin.Value).ToString();
            if (TxtImages != null) TxtImages.Text = ((int)SliderImages.Value).ToString();
            if (TxtMaxOnScreen != null) TxtMaxOnScreen.Text = ((int)SliderMaxOnScreen.Value).ToString();
            if (TxtSize != null) TxtSize.Text = $"{(int)SliderSize.Value}%";
            if (TxtOpacity != null) TxtOpacity.Text = $"{(int)SliderOpacity.Value}%";
            if (TxtFade != null) TxtFade.Text = $"{(int)SliderFade.Value}%";
            
            // Video sliders
            if (TxtPerHour != null) TxtPerHour.Text = ((int)SliderPerHour.Value).ToString();
            if (TxtTargets != null) TxtTargets.Text = ((int)SliderTargets.Value).ToString();
            if (TxtDuration != null) TxtDuration.Text = $"{(int)SliderDuration.Value}s";
            if (TxtTargetSize != null) TxtTargetSize.Text = $"{(int)SliderTargetSize.Value}px";
            
            // Subliminal sliders
            if (TxtSubPerMin != null) TxtSubPerMin.Text = ((int)SliderSubPerMin.Value).ToString();
            if (TxtFrames != null) TxtFrames.Text = ((int)SliderFrames.Value).ToString();
            if (TxtSubOpacity != null) TxtSubOpacity.Text = $"{(int)SliderSubOpacity.Value}%";
            if (TxtWhisperVol != null) TxtWhisperVol.Text = $"{(int)SliderWhisperVol.Value}%";
            
            // Audio sliders
            if (TxtMaster != null) TxtMaster.Text = $"{(int)SliderMaster.Value}%";
            if (TxtVideoVolume != null) TxtVideoVolume.Text = $"{(int)SliderVideoVolume.Value}%";
            if (TxtDuck != null) TxtDuck.Text = $"{(int)SliderDuck.Value}%";
            
            // Progression sliders
            if (TxtSpiralOpacity != null) TxtSpiralOpacity.Text = $"{(int)SliderSpiralOpacity.Value}%";
            if (TxtPinkOpacity != null) TxtPinkOpacity.Text = $"{(int)SliderPinkOpacity.Value}%";
            if (TxtBubbleFreq != null) TxtBubbleFreq.Text = ((int)SliderBubbleFreq.Value).ToString();
            if (TxtBubbleVolume != null) TxtBubbleVolume.Text = $"{(int)SliderBubbleVolume.Value}%";
            if (TxtLockCardFreq != null) TxtLockCardFreq.Text = ((int)SliderLockCardFreq.Value).ToString();
            if (TxtLockCardRepeats != null) TxtLockCardRepeats.Text = $"{(int)SliderLockCardRepeats.Value}x";
            if (TxtBouncingTextSize != null) TxtBouncingTextSize.Text = $"{(int)SliderBouncingTextSize.Value}%";
            if (TxtMindWipeFreq != null) TxtMindWipeFreq.Text = $"{(int)SliderMindWipeFreq.Value}/h";
            if (TxtMindWipeVolume != null) TxtMindWipeVolume.Text = $"{(int)SliderMindWipeVolume.Value}%";
            if (TxtBrainDrainIntensity != null) TxtBrainDrainIntensity.Text = $"{(int)SliderBrainDrainIntensity.Value}%";
            
            // Scheduler sliders
            if (TxtRampDuration != null) TxtRampDuration.Text = $"{(int)SliderRampDuration.Value} min";
            if (TxtMultiplier != null) TxtMultiplier.Text = $"{SliderMultiplier.Value:F1}x";

            // Haptic sliders
            if (TxtHapticIntensity != null) TxtHapticIntensity.Text = $"{(int)SliderHapticIntensity.Value}%";
            if (TxtHapticBubble != null) TxtHapticBubble.Text = $"{(int)SliderHapticBubble.Value}%";
            if (TxtHapticFlashDisplay != null) TxtHapticFlashDisplay.Text = $"{(int)SliderHapticFlashDisplay.Value}%";
            if (TxtHapticFlashClick != null) TxtHapticFlashClick.Text = $"{(int)SliderHapticFlashClick.Value}%";
            if (TxtHapticVideo != null) TxtHapticVideo.Text = $"{(int)SliderHapticVideo.Value}%";
            if (TxtHapticTargetHit != null) TxtHapticTargetHit.Text = $"{(int)SliderHapticTargetHit.Value}%";
            if (TxtHapticSubliminal != null) TxtHapticSubliminal.Text = $"{(int)SliderHapticSubliminal.Value}%";
            if (TxtHapticLevelUp != null) TxtHapticLevelUp.Text = $"{(int)SliderHapticLevelUp.Value}%";
            if (TxtHapticAchievement != null) TxtHapticAchievement.Text = $"{(int)SliderHapticAchievement.Value}%";
        }

        private void SaveSettings()
        {
            // velvet-mosaic: feature popups write to App.Settings.Current on every edit,
            // so the settings object is already the source of truth. The legacy dashboard
            // controls (now inside LegacyDashboardHost, Collapsed) can be stale. Re-sync
            // them from settings before this method reads them, otherwise stale control
            // values would clobber the popup changes.
            var wasLoading = _isLoading;
            _isLoading = true;
            try { LoadSettings(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "SaveSettings: legacy control refresh failed"); }
            finally { _isLoading = wasLoading; }

            var s = App.Settings.Current;

            // Flash
            s.FlashEnabled = ChkFlashEnabled.IsChecked ?? true;
            s.FlashClickable = ChkClickable.IsChecked ?? true;
            s.CorruptionMode = ChkCorruption.IsChecked ?? false;
            s.HydraLinkedTiming = ChkHydraLinked.IsChecked ?? true;
            s.FlashGlowEnabled = ChkFlashGlow.IsChecked ?? true;
            s.FlashFrequency = (int)SliderPerMin.Value;
            s.SimultaneousImages = (int)SliderImages.Value;
            s.HydraLimit = (int)SliderMaxOnScreen.Value;

            // Visuals
            s.ImageScale = (int)SliderSize.Value;
            s.FlashOpacity = (int)SliderOpacity.Value;
            s.FadeDuration = (int)SliderFade.Value;

            // Video
            s.MandatoryVideosEnabled = ChkVideoEnabled.IsChecked ?? false;
            s.VideosPerHour = (int)SliderPerHour.Value;
            s.StrictLockEnabled = ChkStrictLock.IsChecked ?? false;
            s.AttentionChecksEnabled = ChkMiniGameEnabled.IsChecked ?? false;
            s.AttentionDensity = (int)SliderTargets.Value;
            s.RandomizeAttentionTargets = ChkRandomizeTargets.IsChecked ?? false;
            s.AttentionLifespan = (int)SliderDuration.Value;
            s.AttentionSize = (int)SliderTargetSize.Value;

            // Subliminals
            s.SubliminalEnabled = ChkSubliminalEnabled.IsChecked ?? false;
            s.SubliminalFrequency = (int)SliderSubPerMin.Value;
            s.SubliminalDuration = (int)SliderFrames.Value;
            s.SubliminalOpacity = (int)SliderSubOpacity.Value;
            s.SubAudioEnabled = ChkAudioWhispers.IsChecked ?? false;
            s.SubAudioVolume = (int)SliderWhisperVol.Value;

            // System
            s.DualMonitorEnabled = ChkDualMon.IsChecked ?? true;
            s.RunOnStartup = ChkWinStart.IsChecked ?? false;
            s.ForceVideoOnLaunch = ChkVidLaunch.IsChecked ?? false;
            s.AutoStartEngine = ChkAutoRun.IsChecked ?? false;
            s.StartMinimized = ChkStartHidden.IsChecked ?? false;
            s.PanicKeyEnabled = !(ChkNoPanic.IsChecked ?? false);
            s.OfflineMode = ChkOfflineMode.IsChecked ?? false;
            if (ChkPerformanceMode != null) s.PerformanceMode = ChkPerformanceMode.IsChecked ?? false;
            if (ChkAutoPerformance != null) s.AutoPerformanceMode = ChkAutoPerformance.IsChecked ?? true;
            if (ChkVideoHwDecode != null) s.VideoHardwareDecoding = ChkVideoHwDecode.IsChecked ?? true;

            // Deeper
            if (ChkEnableDeeper != null) s.EnableDeeper = ChkEnableDeeper.IsChecked ?? true;

            // Audio
            s.MasterVolume = (int)SliderMaster.Value;
            s.AudioDuckingEnabled = ChkAudioDuck.IsChecked ?? true;
            s.DuckingLevel = (int)SliderDuck.Value;
            s.ExcludeBambiCloudFromDucking = ChkExcludeBambiCloudDucking.IsChecked ?? true;

            // Progression
            s.SpiralEnabled = ChkSpiralEnabled.IsChecked ?? false;
            s.SpiralOpacity = (int)SliderSpiralOpacity.Value;
            s.PinkFilterEnabled = ChkPinkFilterEnabled.IsChecked ?? false;
            s.PinkFilterOpacity = (int)SliderPinkOpacity.Value;
            s.BubblesEnabled = ChkBubblesEnabled.IsChecked ?? false;
            s.BubblesFrequency = (int)SliderBubbleFreq.Value;
            s.LockCardEnabled = ChkLockCardEnabled.IsChecked ?? false;
            s.LockCardFrequency = (int)SliderLockCardFreq.Value;
            s.LockCardRepeats = (int)SliderLockCardRepeats.Value;
            s.LockCardStrict = ChkLockCardStrict.IsChecked ?? false;

            // Brain Drain
            s.BrainDrainEnabled = ChkBrainDrainEnabled.IsChecked ?? false;
            s.BrainDrainIntensity = (int)SliderBrainDrainIntensity.Value;
            s.BrainDrainHighRefresh = ChkBrainDrainHighRefresh.IsChecked ?? false;

            // Scheduler - track if settings changed
            var schedulerWasEnabled = s.SchedulerEnabled;
            s.SchedulerEnabled = ChkSchedulerEnabled.IsChecked ?? false;
            s.SchedulerStartTime = TxtStartTime.Text;
            s.SchedulerEndTime = TxtEndTime.Text;
            s.SchedulerMonday = ChkMon.IsChecked ?? true;
            s.SchedulerTuesday = ChkTue.IsChecked ?? true;
            s.SchedulerWednesday = ChkWed.IsChecked ?? true;
            s.SchedulerThursday = ChkThu.IsChecked ?? true;
            s.SchedulerFriday = ChkFri.IsChecked ?? true;
            s.SchedulerSaturday = ChkSat.IsChecked ?? true;
            s.SchedulerSunday = ChkSun.IsChecked ?? true;

            // If scheduler was just enabled or settings changed, reset flags and check immediately
            if (s.SchedulerEnabled && !schedulerWasEnabled)
            {
                _schedulerAutoStarted = false;
                _manuallyStoppedDuringSchedule = false;
                // Check scheduler immediately after save completes
                Dispatcher.BeginInvoke(new Action(() => CheckSchedulerAfterSettingsChange()), System.Windows.Threading.DispatcherPriority.Background);
            }
            s.IntensityRampEnabled = ChkRampEnabled.IsChecked ?? false;
            s.RampDurationMinutes = (int)SliderRampDuration.Value;
            s.SchedulerMultiplier = SliderMultiplier.Value;
            
            // Ramp Links
            s.RampLinkFlashOpacity = ChkRampLinkFlash.IsChecked ?? false;
            s.RampLinkSpiralOpacity = ChkRampLinkSpiral.IsChecked ?? false;
            s.RampLinkPinkFilterOpacity = ChkRampLinkPink.IsChecked ?? false;
            s.RampLinkMasterAudio = ChkRampLinkMaster.IsChecked ?? false;
            s.RampLinkSubliminalAudio = ChkRampLinkSubAudio.IsChecked ?? false;
            s.EndSessionOnRampComplete = ChkEndAtRamp.IsChecked ?? false;

            App.Settings.Save();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // First, apply current settings to the settings object
            SaveSettings();

            // Find current preset
            var currentPresetName = App.Settings.Current.CurrentPresetName;
            var currentPreset = _allPresets.FirstOrDefault(p => p.Name == currentPresetName);

            // Determine if we should create new or overwrite
            if (currentPreset == null || currentPreset.IsDefault || string.IsNullOrEmpty(currentPresetName))
            {
                // No preset, default preset, or unknown - ask to create new
                var result = MessageBox.Show(
                    "Would you like to save your current settings as a new preset?\n\n" +
                    "This will create a custom preset that you can load later.",
                    "Save as Preset",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    PromptSaveNewPreset();
                }
                else
                {
                    MessageBox.Show(Loc.Get("msg_settings_saved"), Loc.Get("title_success"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                // Custom user preset - ask to overwrite
                var result = MessageBox.Show(
                    $"Do you want to overwrite preset '{currentPreset.Name}' with your current settings?\n\n" +
                    "Click 'Yes' to overwrite, 'No' to save as new preset, or 'Cancel' to just save settings.",
                    "Overwrite Preset?",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // Overwrite existing preset
                    var updated = Models.Preset.FromSettings(App.Settings.Current, currentPreset.Name, currentPreset.Description);
                    updated.Id = currentPreset.Id;
                    updated.CreatedAt = currentPreset.CreatedAt;

                    var index = App.Settings.Current.UserPresets.FindIndex(p => p.Id == currentPreset.Id);
                    if (index >= 0)
                    {
                        App.Settings.Current.UserPresets[index] = updated;
                        App.Settings.Save();
                        RefreshPresetsList();

                        App.Logger?.Information("Overwritten preset: {Name}", updated.Name);
                        MessageBox.Show(Loc.GetF("msg_preset_0_updated", updated.Name), Loc.Get("title_preset_saved"),
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else if (result == MessageBoxResult.No)
                {
                    // Save as new preset
                    PromptSaveNewPreset();
                }
                else
                {
                    // Cancel - just show settings saved message
                    MessageBox.Show(Loc.Get("msg_settings_saved"), Loc.Get("title_success"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            if (App.Lockdown?.IsActive == true)
            {
                MessageBox.Show(Loc.Get("msg_you_are_in_lockdown_mode_nthere_is_no_escape"), Loc.Get("title_lockdown"),
                    MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            if (_isRunning)
            {
                var result = MessageBox.Show(Loc.Get("msg_engine_is_running_stop_and_exit"), Loc.Get("title_confirm_exit"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
                    return;
                StopEngine();
            }
            _exitRequested = true;
            EnsureSessionRestoredForExit();
            SaveSettings();
            // Under ShutdownMode=OnLastWindowClose, Close()ing only the main window leaves the
            // avatar tube and pooled keep-alive overlay windows (Flash/Subliminal/Chaos) alive —
            // especially right after a Chaos run — so the app lingered headless and never reached
            // App.OnExit/Environment.Exit. Shutdown() closes ALL windows (this window still runs
            // its _exitRequested cleanup via OnClosing) and fires OnExit. Matches the tray Exit path.
            Application.Current.Shutdown();
        }

        private void BtnMainHelp_Click(object sender, RoutedEventArgs e)
        {
            // Hide browser (WebView2 doesn't respect WPF z-order)
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Hidden;
            MainTutorialOverlay.Visibility = Visibility.Visible;
        }

        private void BtnReportBug_Click(object sender, RoutedEventArgs e)
        {
            OpenBugReportWindow();
        }

        private void BtnTutorialReportBug_Click(object sender, RoutedEventArgs e)
        {
            // Close the tutorial overlay first, then open the bug report dialog
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            OpenBugReportWindow();
        }

        private void OpenBugReportWindow()
        {
            try
            {
                var dialog = new BugReportWindow { Owner = this };
                dialog.ShowDialog();
            }
            catch (System.Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open BugReportWindow");
                MessageBox.Show(this, Loc.Get("bug_report_error_toast") + "\n\n" + ex.Message,
                    Loc.Get("bug_report_title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MainTutorial_Close(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
        }

        private void MainTutorial_Close(object sender, MouseButtonEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
        }

        private void MainTutorial_ContentClick(object sender, MouseButtonEventArgs e)
        {
            // Prevent closing when clicking on the content
            e.Handled = true;
        }

        private TutorialOverlay? _tutorialOverlay;

        private void BtnStartTutorial_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial();
        }

        public void StartTutorial(TutorialType type = TutorialType.FullTour)
        {
            if (_tutorialOverlay != null) return;

            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Hidden;

            // Configure tutorial callbacks for tab switching
            App.Tutorial.ConfigureCallbacks(
                showSettings: () => ShowTab("settings"),
                showPresets: () => { ShowTab("presets"); RefreshPresetsList(); },
                showProgression: () => ShowTab("progression"),
                showAchievements: () => ShowTab("achievements"),
                showCompanion: () => ShowTab("companion"),
                // Exclusives tab eliminated — route tutorial's "patreon" step to the
                // App Info & Data popup which hosts the login/data sections.
                showPatreon: () => ShowAppInfoPopup(),
                showAwareness: () => ShowTab("awareness"),
                showDeeper: () => ShowTab("deeper")
            );

            App.Tutorial.Start(type);
            _tutorialOverlay = new TutorialOverlay(this, App.Tutorial);
            _tutorialOverlay.Closed += (s, e) =>
            {
                _tutorialOverlay = null;
                if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            };
            _tutorialOverlay.Show();
        }

        #region Feature Tutorial Button Handlers

        private void BtnTutorialGettingStarted_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial(TutorialType.GettingStarted);
        }

        private void BtnTutorialSettings_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial(TutorialType.Settings);
        }

        private void BtnTutorialPresets_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial(TutorialType.Presets);
        }

        private void BtnTutorialProgression_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial(TutorialType.Progression);
        }

        private void BtnTutorialAchievements_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial(TutorialType.Achievements);
        }

        private void BtnTutorialCompanion_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial(TutorialType.Companion);
        }

        private void BtnTutorialPatreon_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial(TutorialType.Patreon);
        }

        private void BtnTutorialAvatar_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial(TutorialType.Avatar);
        }

        private void BtnTutorialAwareness_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            StartAwarenessTutorial();
        }

        // Same tour, but launched directly from the in-tab "Tutorial" button rather
        // than via the help-menu overlay (so we don't toggle MainTutorialOverlay).
        private void BtnAwarenessTutorial_Click(object sender, RoutedEventArgs e)
        {
            StartAwarenessTutorial();
        }

        private void BtnCompanionTutorial_Click(object sender, RoutedEventArgs e)
        {
            StartTutorial(TutorialType.Companion);
        }

        private void StartAwarenessTutorial()
        {
            // One-shot: when the Awareness tour finishes naturally (user reached the
            // last step), pop the Puppy preset editor so they have something concrete
            // to play with while the walkthrough is fresh. Skipping mid-tour does not
            // open the editor — skip means "I'm done with this".
            EventHandler? onCompleted = null;
            onCompleted = (s, args) =>
            {
                App.Tutorial.TutorialCompleted -= onCompleted;
                if (App.Tutorial.CurrentTutorialType != TutorialType.Awareness) return;
                if (App.Tutorial.CurrentStepIndex != App.Tutorial.TotalSteps - 1) return;

                try
                {
                    var puppy = App.KeywordPresets?.GetPreset("builtin.puppy");
                    if (puppy == null) return;
                    var dlg = new AwarenessPresetDetailDialog(puppy) { Owner = this };
                    dlg.Show();
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Awareness tutorial editor-open failed: {Error}", ex.Message);
                }
            };
            App.Tutorial.TutorialCompleted += onCompleted;

            StartTutorial(TutorialType.Awareness);
        }

        private void BtnTutorialModding_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            var modCreator = new ModCreatorWindow(startWithTutorial: true) { Owner = this };
            modCreator.Show();
        }

        #endregion

        private void OpenLinktree()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://linktr.ee/CodeBambi",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        #endregion

        #region UI Updates

        private void UpdateUI()
        {
            // Update all value labels
            TxtPerMin.Text = ((int)SliderPerMin.Value).ToString();
            TxtImages.Text = ((int)SliderImages.Value).ToString();
            TxtMaxOnScreen.Text = ((int)SliderMaxOnScreen.Value).ToString();
            TxtSize.Text = $"{(int)SliderSize.Value}%";
            TxtOpacity.Text = $"{(int)SliderOpacity.Value}%";
            TxtFade.Text = $"{(int)SliderFade.Value}%";
            TxtPerHour.Text = ((int)SliderPerHour.Value).ToString();
            TxtTargets.Text = ((int)SliderTargets.Value).ToString();
            TxtDuration.Text = ((int)SliderDuration.Value).ToString();
            TxtTargetSize.Text = ((int)SliderTargetSize.Value).ToString();
            TxtSubPerMin.Text = ((int)SliderSubPerMin.Value).ToString();
            TxtFrames.Text = ((int)SliderFrames.Value).ToString();
            TxtSubOpacity.Text = $"{(int)SliderSubOpacity.Value}%";
            TxtWhisperVol.Text = $"{(int)SliderWhisperVol.Value}%";
            TxtMaster.Text = $"{(int)SliderMaster.Value}%";
            TxtDuck.Text = $"{(int)SliderDuck.Value}%";
            TxtSpiralOpacity.Text = $"{(int)SliderSpiralOpacity.Value}%";
            TxtPinkOpacity.Text = $"{(int)SliderPinkOpacity.Value}%";
            TxtBubbleFreq.Text = ((int)SliderBubbleFreq.Value).ToString();
            TxtRampDuration.Text = $"{(int)SliderRampDuration.Value} min";
            TxtMultiplier.Text = $"{SliderMultiplier.Value:F1}x";
        }

        private void UpdateLevelDisplay()
        {
            var s = App.Settings.Current;
            var level = s.PlayerLevel;
            var xp = s.PlayerXP;
            var xpNeeded = App.Progression.GetXPForLevel(level);

            TxtLevel.Text = $"Lvl {level}";
            TxtLevelLabel.Text = $"LVL {level}";
            TxtXP.Text = $"{(int)xp} / {(int)xpNeeded} XP";

            // Update XP bar width.
            // XPBar.Parent is the wrapping Grid (not the outer Border with rounded corners),
            // so we read the container's ActualWidth directly. Casting Parent to Border made
            // the expression always null, falling back to 100 px regardless of progress —
            // visible bug: bar appeared frozen at 100 px after install.
            var progress = Math.Min(1.0, xp / xpNeeded);
            var container = XPBar.Parent as FrameworkElement;
            var available = container?.ActualWidth ?? 0;
            if (available > 0) XPBar.Width = progress * available;

            // Update title based on level
            var rankTitle = level switch
            {
                < 20 => "BASIC BIMBO",
                < 50 => "DUMB AIRHEAD",
                < 100 => "SYNTHETIC BLOWDOLL",
                _ => "PERFECT FUCKPUPPET"
            };
            TxtPlayerTitle.Text = App.Mods?.MakeModAware(rankTitle) ?? rankTitle;

            // Update unlockables visibility based on level
            UpdateUnlockablesVisibility(level);

            // Update XP bar login state
            UpdateXPBarLoginState();

            // Update stat pills visibility and values
            UpdateStatPills();
        }

        /// <summary>
        /// Applies mod text replacements to all hardcoded feature/section labels in the XAML.
        /// Called on startup and when the active mod changes.
        /// </summary>
        private void ApplyModFeatureNames()
        {
            // If a mod is active, use mod-aware text; otherwise use localized text
            string ML(string englishText, string locKey) => App.Mods?.MakeModAware(englishText) is string modText && modText != englishText
                ? modText : Loc.Get(locKey);

            // Main section headers
            if (TxtFeatureFlash != null) TxtFeatureFlash.Text = ML("⚡ Flash Images", "section_flash_images");
            if (TxtFeatureVideo != null) TxtFeatureVideo.Text = ML("🎬 Mandatory Video", "section_mandatory_video");
            if (TxtFeatureSubliminal != null) TxtFeatureSubliminal.Text = ML("💭 Subliminals", "section_subliminals");
            if (TxtFeatureWhispers != null) TxtFeatureWhispers.Text = ML("📊 Audio Whispers", "label_audio_whispers");

            // Enhancement locked/unlocked pairs
            if (TxtFeatureSpiralLocked != null) TxtFeatureSpiralLocked.Text = ML("🌀 Spiral Overlay", "label_spiral_overlay");
            if (TxtFeatureSpiral != null) TxtFeatureSpiral.Text = ML("🌀 Spiral Overlay", "label_spiral_overlay");
            if (TxtFeaturePinkFilterLocked != null) TxtFeaturePinkFilterLocked.Text = ML("💗 Pink Filter", "label_pink_filter");
            if (TxtFeaturePinkFilter != null) TxtFeaturePinkFilter.Text = ML("💗 Pink Filter", "label_pink_filter");
            if (TxtFeatureBubblePopLocked != null) TxtFeatureBubblePopLocked.Text = ML("🫧 Bubble Pop", "label_bubble_pop");
            if (TxtFeatureBubblePop != null) TxtFeatureBubblePop.Text = ML("🫧 Bubble Pop", "label_bubble_pop");
            if (TxtFeatureLockCardLocked != null) TxtFeatureLockCardLocked.Text = ML("📐 Lock Card", "label_lock_card");
            if (TxtFeatureLockCard != null) TxtFeatureLockCard.Text = ML("📐 Lock Card", "label_lock_card");
            if (TxtFeatureBubbleCountLocked != null) TxtFeatureBubbleCountLocked.Text = ML("🫧 Bubble Count", "label_bubble_count");
            if (TxtFeatureBubbleCount != null) TxtFeatureBubbleCount.Text = ML("🫧 Bubble Count", "label_bubble_count");
            if (TxtFeatureBouncingLocked != null) TxtFeatureBouncingLocked.Text = ML("📺 Bouncing Text", "label_bouncing_text");
            if (TxtFeatureBouncing != null) TxtFeatureBouncing.Text = ML("📺 Bouncing Text", "label_bouncing_text");
            if (TxtFeatureBrainDrain != null) TxtFeatureBrainDrain.Text = ML("💧 Brain Drain", "label_brain_drain");
            if (TxtFeatureMindWipeLocked != null) TxtFeatureMindWipeLocked.Text = ML("🧠 Mind Wipe", "label_mind_wipe");
            if (TxtFeatureMindWipe != null) TxtFeatureMindWipe.Text = ML("🧠 Mind Wipe", "label_mind_wipe");
            if (TxtFeatureCornerGif != null) TxtFeatureCornerGif.Text = ML("🖼 Corner GIF", "label_corner_gif");

            // Preset/session detail labels
            if (TxtDetailFlashLabel != null) TxtDetailFlashLabel.Text = ML("⚡ Flash Images", "section_flash_images");
            if (TxtDetailVideoLabel != null) TxtDetailVideoLabel.Text = ML("🎬 Mandatory Videos", "label_mandatory_videos");
            if (TxtDetailSubLabel != null) TxtDetailSubLabel.Text = ML("💭 Subliminals", "section_subliminals");
            if (TxtSessionFlashLabel != null) TxtSessionFlashLabel.Text = ML("⚡ Flash Images", "section_flash_images");
            if (TxtSessionSubLabel != null) TxtSessionSubLabel.Text = ML("💭 Subliminals", "section_subliminals");

            // Autonomy toggle labels
            if (TxtAutoFlash != null) TxtAutoFlash.Text = ML("Flashes", "tab_flashes");
            if (TxtAutoVideo != null) TxtAutoVideo.Text = ML("Videos", "tab_videos");
            if (TxtAutoSubliminal != null) TxtAutoSubliminal.Text = ML("Subliminals", "tab_subliminals");
            if (TxtAutoBubbles != null) TxtAutoBubbles.Text = ML("Bubbles", "label_bubbles");
            if (TxtAutoPinkFilter != null) TxtAutoPinkFilter.Text = ML("Pink Filter", "label_pink_filter");
            if (TxtAutoLockCards != null) TxtAutoLockCards.Text = ML("Lock Cards", "label_lock_card");
            if (TxtAutoBouncing != null) TxtAutoBouncing.Text = ML("Bouncing", "label_bouncing_text");
            if (TxtAutoMindwipe != null) TxtAutoMindwipe.Text = ML("Mindwipe", "label_mind_wipe");

            // Enhancement tab tooltip
            if (BtnEnhancements != null)
                BtnEnhancements.ToolTip = App.Mods?.GetTabTooltip() ?? Loc.Get("tooltip_enhancement_tree");

            // Stat pill tooltips
            if (PillConditioningTime != null)
                PillConditioningTime.ToolTip = App.Mods?.GetStatPillTooltip("pink_hours")
                    ?? ML("Total conditioning time (Pink Hours skill)", "tooltip_total_conditioning_time_pink_hours_skill");
            if (PillOnlineUsers != null)
                PillOnlineUsers.ToolTip = App.Mods?.GetStatPillTooltip("hive_mind")
                    ?? ML("Bimbos online now (Hive Mind skill)", "tooltip_bimbos_online_now_hive_mind_skill");
            if (PillRankPercentile != null)
                PillRankPercentile.ToolTip = App.Mods?.GetStatPillTooltip("popular_girl")
                    ?? ML("Your rank percentile (Popular Girl skill)", "tooltip_your_rank_percentile_popular_girl_skill");

            // Mod-aware Bambi Takeover header + side-nav button label
            // (Drone mod → "Drone Takeover", SissyHypno → "Sissy Takeover", etc.)
            var takeoverLabel = App.Mods?.GetTakeoverLabel() ?? Loc.Get("tab_takeover");
            if (TxtBambiTakeoverHeader != null) TxtBambiTakeoverHeader.Text = takeoverLabel;
            if (TxtSubBambiTakeover != null) TxtSubBambiTakeover.Text = takeoverLabel;

            // Refresh bonus chips with updated names
            RefreshXPBarBonuses();

            // Also refresh rank title
            UpdateLevelDisplay();

            // Show/hide the Bimbo Journal sub-tab based on the active mod.
            ApplyBimboJournalModVisibility();
        }

        /// <summary>
        /// The Bimbo Journal is built around bimbofication photo tracks, so it only
        /// fits the CCP Default, Bambi Sleep, and Sissy Hypno mods. For any other mod
        /// (Dronification, Locked, community mods) we hide its sub-tab entry point —
        /// and, if it happens to be open, fall back to the Daily/Weekly panel.
        /// Re-run whenever the active mod changes (via ApplyModFeatureNames).
        /// </summary>
        private void ApplyBimboJournalModVisibility()
        {
            if (BtnQuestSubRoadmap == null) return;

            var modId = App.Mods?.ActiveModId;
            bool supported = modId == Models.BuiltInMods.CCPDefaultId
                          || modId == Models.BuiltInMods.BambiSleepId
                          || modId == Models.BuiltInMods.SissyHypnoId;

            BtnQuestSubRoadmap.Visibility = supported ? Visibility.Visible : Visibility.Collapsed;

            // If the journal is hidden out from under the user, snap back to Daily/Weekly.
            if (!supported && RoadmapPanel?.Visibility == Visibility.Visible)
            {
                RoadmapPanel.Visibility = Visibility.Collapsed;
                if (DailyWeeklyPanel != null) DailyWeeklyPanel.Visibility = Visibility.Visible;
                if (BtnQuestSubDaily != null) BtnQuestSubDaily.Style = (Style)FindResource("TabButtonActive");
            }
        }

        /// <summary>
        /// Updates the XP bar visibility based on login status.
        /// Shows a login prompt overlay when user is not logged in.
        /// </summary>
        private void UpdateXPBarLoginState()
        {
            var isLoggedIn = App.IsLoggedIn;

            if (XPBarLoginOverlay != null && XPBarContent != null)
            {
                if (isLoggedIn)
                {
                    // User is logged in - show normal XP bar
                    XPBarLoginOverlay.Visibility = Visibility.Collapsed;
                    XPBarContent.Opacity = 1.0;
                }
                else
                {
                    // User is not logged in - show overlay and gray out XP bar
                    XPBarLoginOverlay.Visibility = Visibility.Visible;
                    XPBarContent.Opacity = 0.3;
                }
            }
        }

        /// <summary>
        /// Updates the stat pill visibility and values based on unlocked skills.
        /// Pills only show when their respective skills are unlocked.
        /// </summary>
        private void UpdateStatPills()
        {
            if (App.SkillTree == null) return;

            // Pink Hours: Total Conditioning Time (5 points - tier 1)
            if (PillConditioningTime != null)
            {
                bool hasPinkHours = App.SkillTree.HasSkill("pink_hours");
                PillConditioningTime.Visibility = hasPinkHours ? Visibility.Visible : Visibility.Collapsed;

                if (hasPinkHours && TxtPillConditioningTime != null)
                {
                    double totalMinutes;

                    if (_isRunning && _conditioningTimeTimer != null)
                    {
                        // Use baseline + session elapsed to avoid double-counting
                        // (storedMinutes gets incremented every 60s by the tracker, so adding
                        // sessionElapsed on top would count those minutes twice)
                        var sessionElapsed = DateTime.Now - _conditioningStartTime;
                        totalMinutes = _conditioningBaselineMinutes + sessionElapsed.TotalMinutes;
                    }
                    else
                    {
                        totalMinutes = App.Settings?.Current?.TotalConditioningMinutes ?? 0;
                    }

                    // Format as hours, minutes, and seconds
                    var totalSeconds = totalMinutes * 60;
                    var hours = (int)(totalSeconds / 3600);
                    var minutes = (int)((totalSeconds % 3600) / 60);
                    var seconds = (int)(totalSeconds % 60);
                    TxtPillConditioningTime.Text = $"{hours}h {minutes}m {seconds}s";
                }
            }

            // Hive Mind: Online Users Count (60 points total - tier 3)
            if (PillOnlineUsers != null)
            {
                bool hasHiveMind = App.SkillTree.HasSkill("hive_mind");
                PillOnlineUsers.Visibility = hasHiveMind ? Visibility.Visible : Visibility.Collapsed;

                if (hasHiveMind && TxtPillOnlineUsers != null)
                {
                    // Get online user count from leaderboard service
                    var onlineCount = App.Leaderboard?.OnlineUsers ?? 0;
                    TxtPillOnlineUsers.Text = onlineCount.ToString();
                }
            }

            // Popular Girl: Rank Percentile (130 points total - tier 4)
            if (PillRankPercentile != null)
            {
                bool hasPopularGirl = App.SkillTree.HasSkill("popular_girl");
                PillRankPercentile.Visibility = hasPopularGirl ? Visibility.Visible : Visibility.Collapsed;

                if (hasPopularGirl && TxtPillRankPercentile != null)
                {
                    // Get rank percentile from leaderboard
                    var percentile = App.Leaderboard?.GetPlayerPercentile() ?? 0;

                    if (percentile > 0)
                    {
                        TxtPillRankPercentile.Text = $"Top {percentile}%";
                    }
                    else if (App.Leaderboard?.Entries?.Count > 0)
                    {
                        // Leaderboard loaded but player not found - might be unranked or need to sync
                        TxtPillRankPercentile.Text = Loc.Get("label_unranked");
                    }
                    else
                    {
                        // Leaderboard not loaded yet
                        TxtPillRankPercentile.Text = Loc.Get("label_loading_2");
                    }
                }
            }

            // Good Girl Streak: Fire icon with current streak count (tier 2)
            if (StreakFirePill != null)
            {
                bool hasGoodGirlStreak = App.SkillTree?.HasSkill("good_girl_streak") == true;
                StreakFirePill.Visibility = hasGoodGirlStreak ? Visibility.Visible : Visibility.Collapsed;

                if (hasGoodGirlStreak)
                {
                    if (TxtStreakFireCount != null)
                    {
                        var streak = App.Achievements?.Progress?.ConsecutiveDays ?? 0;
                        TxtStreakFireCount.Text = streak.ToString();
                    }

                    // Show shield icon if shields are available
                    if (TxtStreakShieldIcon != null)
                    {
                        var shieldsRemaining = App.Settings?.Current?.StreakShieldsRemaining ?? 0;
                        TxtStreakShieldIcon.Visibility = shieldsRemaining > 0 ? Visibility.Visible : Visibility.Collapsed;
                        TxtStreakShieldIcon.ToolTip = shieldsRemaining > 0
                            ? "Streak shield available — protects your streak if you miss a day"
                            : "Streak shield used — resets weekly";
                    }
                }
            }

            RefreshXPBarBonuses();
        }

        private void RefreshXPBarBonuses()
        {
            if (XPBarBonusList == null) return;

            var breakdown = App.SkillTree?.GetMultiplierBreakdown() ?? new List<(string, double)>();
            XPBarBonusList.Children.Clear();

            foreach (var (source, value) in breakdown)
            {
                if (source == "Base") continue;

                var displaySource = App.Mods?.MakeModAware(source) ?? source;
                var chip = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(42, 42, 74)), // #2A2A4A - matches stat pills
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(6, 3, 6, 3),
                    Margin = new Thickness(0, 0, 8, 0),
                    ToolTip = GetBonusChipTooltip(source)
                };

                chip.Child = new TextBlock
                {
                    Text = $"+{value:P0} {displaySource}",
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentLightColorHex() ?? "#FFB6C1")),
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center
                };

                XPBarBonusList.Children.Add(chip);
            }
        }

        private static string? GetBonusChipTooltip(string source)
        {
            string M(string text) => App.Mods?.MakeModAware(text) ?? text;

            // Check for explicit mod override first
            string? modTip = null;
            if (source.StartsWith("Streak Power"))
                modTip = App.Mods?.GetBoostTooltip("streak_power");
            else
            {
                var skillId = source switch
                {
                    "Sparkle Boost" => "sparkle_boost_1",
                    "Extra Sparkly" => "sparkle_boost_2",
                    "Maximum Sparkle" => "sparkle_boost_3",
                    "Night Shift" => "night_shift",
                    "Early Bird Bimbo" => "early_bird_bimbo",
                    "PINK RUSH ACTIVE!" => "pink_rush",
                    _ => null
                };
                if (skillId != null)
                    modTip = App.Mods?.GetBoostTooltip(skillId);
            }
            if (modTip != null) return modTip;

            // Fall back to defaults with MakeModAware
            if (source.StartsWith("Streak Power")) return M("Skill tree bonus: +0.5% XP per day of consecutive use (max 15%)");
            return source switch
            {
                "Sparkle Boost" => M("Skill tree bonus: +10% XP from Sparkle Boost"),
                "Extra Sparkly" => M("Skill tree bonus: +15% XP from Extra Sparkly (stacks with Sparkle Boost)"),
                "Maximum Sparkle" => M("Skill tree bonus: +20% XP from Maximum Sparkle (stacks with other Sparkle skills)"),
                "Night Shift" => M("Skill tree bonus: +50% XP for conditioning between 11 PM and 5 AM"),
                "Early Bird Bimbo" => M("Skill tree bonus: +50% XP for conditioning between 5 AM and 8 AM"),
                "PINK RUSH ACTIVE!" => M("Skill tree bonus: 3x XP multiplier! Random 60-second windows of boosted XP"),
                _ => null
            };
        }

        /// <summary>
        /// Start a timer to periodically update stat pill values (conditioning time, online users, rank).
        /// Updates every 30 seconds.
        /// </summary>
        private void StartStatPillUpdateTimer()
        {
            if (_statPillUpdateTimer != null) return; // Already started

            _statPillUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };

            _statPillUpdateTimer.Tick += (s, e) =>
            {
                try
                {
                    UpdateStatPills();
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Error updating stat pills");
                }
            };

            _statPillUpdateTimer.Start();
            App.Logger?.Debug("Stat pill update timer started (30s interval)");
        }

        /// <summary>
        /// Start tracking conditioning time (updates live while engine is running).
        /// Updates display every second, saves to storage every minute, syncs to server every 15 minutes.
        /// </summary>
        private void StartConditioningTimeTracker()
        {
            if (_conditioningTimeTimer != null) return; // Already started

            _conditioningStartTime = DateTime.Now;
            _conditioningBaselineMinutes = App.Settings?.Current?.TotalConditioningMinutes ?? 0;
            _conditioningTimeSecondCounter = 0;

            // Update display every second for LIVE tracking
            _conditioningTimeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };

            _conditioningTimeTimer.Tick += (s, e) =>
            {
                try
                {
                    _conditioningTimeSecondCounter++;

                    // Update stat pill display every second (live update)
                    UpdateStatPills();

                    // Debug log every 10 seconds to verify timer is working
                    if (_conditioningTimeSecondCounter % 10 == 0)
                    {
                        var elapsed = DateTime.Now - _conditioningStartTime;
                        App.Logger?.Debug("Conditioning time tracker tick: {Seconds}s elapsed, stored: {Minutes}m",
                            (int)elapsed.TotalSeconds, App.Settings?.Current?.TotalConditioningMinutes ?? 0);
                    }

                    // Save to local storage every minute (avoid excessive disk writes)
                    if (_conditioningTimeSecondCounter >= 60)
                    {
                        var elapsed = DateTime.Now - _conditioningStartTime;
                        App.SkillTree?.AddConditioningTime(1.0); // Add 1 minute
                        _conditioningTimeSecondCounter = 0;
                        App.Logger?.Debug("Conditioning time saved to storage: {Time}", App.SkillTree?.GetFormattedConditioningTime());
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Error tracking conditioning time");
                }
            };

            _conditioningTimeTimer.Start();

            // Start server sync timer (every 15 minutes)
            _conditioningTimeSyncTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(15)
            };
            _conditioningTimeSyncTimer.Tick += async (s, e) =>
            {
                await SyncConditioningTimeToServerAsync();
            };
            _conditioningTimeSyncTimer.Start();

            App.Logger?.Debug("Conditioning time tracker started (live updates every second, server sync every 15 minutes)");
        }

        /// <summary>
        /// Stop tracking conditioning time and sync to server.
        /// </summary>
        private void StopConditioningTimeTracker()
        {
            if (_conditioningTimeTimer == null) return;

            _conditioningTimeTimer.Stop();
            _conditioningTimeTimer = null;

            // Stop server sync timer
            if (_conditioningTimeSyncTimer != null)
            {
                _conditioningTimeSyncTimer.Stop();
                _conditioningTimeSyncTimer = null;
            }

            // Add any remaining partial minutes not yet saved by the 60-second tracker
            try
            {
                var elapsed = DateTime.Now - _conditioningStartTime;
                var expectedTotal = _conditioningBaselineMinutes + elapsed.TotalMinutes;
                var currentStored = App.Settings?.Current?.TotalConditioningMinutes ?? 0;
                var remainingMinutes = expectedTotal - currentStored;

                if (remainingMinutes > 0)
                {
                    App.SkillTree?.AddConditioningTime(remainingMinutes);
                    App.Logger?.Debug("Added remaining {Minutes:F2} minutes on stop", remainingMinutes);
                }

                // Final update to stat pills
                UpdateStatPills();

                // Sync to server on stop
                _ = SyncConditioningTimeToServerAsync();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Error finalizing conditioning time");
            }

            App.Logger?.Debug("Conditioning time tracker stopped");
        }

        /// <summary>
        /// Sync conditioning time to server.
        /// Called every 15 minutes during session and on session end.
        /// </summary>
        private async Task SyncConditioningTimeToServerAsync()
        {
            try
            {
                // Only sync if user is authenticated (Patreon or Discord)
                if (App.ProfileSync == null)
                {
                    App.Logger?.Debug("Skipping conditioning time sync - ProfileSync not available");
                    return;
                }

                // Only sync if user is authenticated
                if (App.Patreon?.IsAuthenticated != true && App.Discord?.IsAuthenticated != true)
                {
                    App.Logger?.Debug("Skipping conditioning time sync - user not authenticated");
                    return;
                }

                App.Logger?.Information("Syncing conditioning time to server ({Minutes:F1} minutes)",
                    App.Settings?.Current?.TotalConditioningMinutes ?? 0);

                // ProfileSyncService will automatically include conditioning time in the sync
                await App.ProfileSync.SyncProfileAsync();

                App.Logger?.Information("Conditioning time synced successfully");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to sync conditioning time to server");
            }
        }

        private void UpdateUnlockablesVisibility(int level)
        {
            try
            {
                // Feature level gating has been removed — every feature is available from level 1.
                // The legacy Locked/Unlocked panels below live inside the collapsed LegacyDashboardHost,
                // but we still flip them to the unlocked state so nothing appears locked if anything
                // ever ends up rendering them.
                if (SpiralLocked != null) SpiralLocked.Visibility = Visibility.Collapsed;
                if (SpiralUnlocked != null) SpiralUnlocked.Visibility = Visibility.Visible;
                if (PinkFilterLocked != null) PinkFilterLocked.Visibility = Visibility.Collapsed;
                if (PinkFilterUnlocked != null) PinkFilterUnlocked.Visibility = Visibility.Visible;
                if (SpiralFeatureImage != null) SetFeatureImageBlur(SpiralFeatureImage, false);
                if (PinkFilterFeatureImage != null) SetFeatureImageBlur(PinkFilterFeatureImage, false);

                if (BubblesLocked != null) BubblesLocked.Visibility = Visibility.Collapsed;
                if (BubblesUnlocked != null) BubblesUnlocked.Visibility = Visibility.Visible;
                if (BubblePopFeatureImage != null) SetFeatureImageBlur(BubblePopFeatureImage, false);

                if (LockCardLocked != null) LockCardLocked.Visibility = Visibility.Collapsed;
                if (LockCardUnlocked != null) LockCardUnlocked.Visibility = Visibility.Visible;
                if (LockCardFeatureImage != null) SetFeatureImageBlur(LockCardFeatureImage, false);

                if (Level50Locked != null) Level50Locked.Visibility = Visibility.Collapsed;
                if (Level50Unlocked != null) Level50Unlocked.Visibility = Visibility.Visible;
                if (BubbleCountFeatureImage != null) SetFeatureImageBlur(BubbleCountFeatureImage, false);

                if (Level60Locked != null) Level60Locked.Visibility = Visibility.Collapsed;
                if (Level60Unlocked != null) Level60Unlocked.Visibility = Visibility.Visible;
                if (BouncingTextFeatureImage != null) SetFeatureImageBlur(BouncingTextFeatureImage, false);

                if (MindWipeLocked != null) MindWipeLocked.Visibility = Visibility.Collapsed;
                if (MindWipeUnlocked != null) MindWipeUnlocked.Visibility = Visibility.Visible;
                if (MindWipeFeatureImage != null) SetFeatureImageBlur(MindWipeFeatureImage, false);

                if (BrainDrainLocked != null) BrainDrainLocked.Visibility = Visibility.Collapsed;
                if (BrainDrainUnlocked != null) BrainDrainUnlocked.Visibility = Visibility.Visible;
                if (BrainDrainFeatureImage != null) SetFeatureImageBlur(BrainDrainFeatureImage, false);

                // velvet-mosaic dashboard cards are never locked anymore.
                if (CardSpiral != null) CardSpiral.IsLocked = false;
                if (CardPinkFilter != null) CardPinkFilter.IsLocked = false;
                if (CardBubblePop != null) CardBubblePop.IsLocked = false;
                if (CardLockCard != null) CardLockCard.IsLocked = false;
                if (CardBubbleCount != null) CardBubbleCount.IsLocked = false;
                if (CardBouncingText != null) CardBouncingText.IsLocked = false;
                if (CardMindWipe != null) CardMindWipe.IsLocked = false;

                // Lab Tab: Requires Patreon T2 / whitelist
                var labUnlocked = App.Patreon?.CurrentTier >= PatreonTier.Level2 || (App.Settings?.Current?.PatreonTier ?? 0) >= 2;
                if (LabSmokescreen != null) LabSmokescreen.Visibility = labUnlocked ? Visibility.Collapsed : Visibility.Visible;

                // AI effect control lives in the Lab — force-disable for non-T2 users so settings can't outlive the entitlement.
                if (!labUnlocked)
                {
                    var cp = App.Settings?.Current?.CompanionPrompt;
                    if (cp != null && cp.AllowAiToControlEffects)
                    {
                        cp.AllowAiToControlEffects = false;
                        App.Settings?.Save();
                    }
                    if (ChkCapEffects != null && ChkCapEffects.IsChecked == true)
                        ChkCapEffects.IsChecked = false;
                    if (EffectPermsPanel != null)
                        EffectPermsPanel.Visibility = Visibility.Collapsed;
                }

                // Bambi Takeover: Requires Patreon (any tier)
                var autonomyUnlocked = App.Patreon?.HasPremiumAccess == true;
                if (AutonomyLocked != null) AutonomyLocked.Visibility = autonomyUnlocked ? Visibility.Collapsed : Visibility.Visible;
                if (AutonomyUnlocked != null) AutonomyUnlocked.Visibility = autonomyUnlocked ? Visibility.Visible : Visibility.Collapsed;

                // Update lock message
                if (TxtAutonomyLockStatus != null && TxtAutonomyLockMessage != null)
                {
                    TxtAutonomyLockStatus.Text = Loc.Get("label_patreon_only");
                    TxtAutonomyLockMessage.Text = Loc.Get("label_support_on_patreon_to_unlock");
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error("UpdateUnlockablesVisibility: Error updating unlockables visibility: {Error}", ex.Message);
            }
        }
        
        /// <summary>
        /// Applies or removes blur effect on feature images based on lock state
        /// </summary>
        private void SetFeatureImageBlur(Rectangle? featureImageRect, bool blur)
        {
            try
            {
                if (featureImageRect == null)
                {
                    App.Logger?.Warning("SetFeatureImageBlur: featureImageRect is null.");
                    return;
                }

                if (blur)
                {
                    featureImageRect.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 15 };
                    App.Logger?.Debug("SetFeatureImageBlur: Applied blur to {ElementName}", featureImageRect.Name);
                }
                else
                {
                    featureImageRect.Effect = null;
                    App.Logger?.Debug("SetFeatureImageBlur: Removed blur from {ElementName}", featureImageRect.Name);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error("SetFeatureImageBlur: Error setting blur effect for {ElementName}: {Error}", featureImageRect?.Name, ex.Message);
            }
        }

        #endregion

        #region Slider Events

        private void SliderPerMin_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtPerMin == null) return;
            TxtPerMin.Text = ((int)e.NewValue).ToString();
            UpdateAudioLinkState();
            ApplySettingsLive();
        }

        private void SliderImages_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtImages == null) return;
            TxtImages.Text = ((int)e.NewValue).ToString();
            ApplySettingsLive();
        }

        private void SliderMaxOnScreen_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtMaxOnScreen == null) return;
            TxtMaxOnScreen.Text = ((int)e.NewValue).ToString();
            ApplySettingsLive();
        }

        private void SliderSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtSize == null) return;
            TxtSize.Text = $"{(int)e.NewValue}%";
            ApplySettingsLive();
        }

        private void SliderOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtOpacity == null) return;
            TxtOpacity.Text = $"{(int)e.NewValue}%";
            ApplySettingsLive();
        }

        private void SliderFade_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtFade == null) return;
            TxtFade.Text = $"{(int)e.NewValue}%";
            ApplySettingsLive();
        }

        private void SliderFlashDuration_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtFlashDuration == null) return;
            TxtFlashDuration.Text = $"{(int)e.NewValue}s";
            App.Settings.Current.FlashDuration = (int)e.NewValue;
        }

        private void ChkFlashAudio_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            
            var isEnabled = ChkFlashAudio.IsChecked ?? true;
            App.Settings.Current.FlashAudioEnabled = isEnabled;
            
            // Enable/disable duration slider based on audio link
            SliderFlashDuration.IsEnabled = !isEnabled;
            SliderFlashDuration.Opacity = isEnabled ? 0.5 : 1.0;
            
            // Show/hide warning
            TxtAudioWarning.Visibility = isEnabled ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateAudioLinkState()
        {
            if (_isLoading) return;
            
            var flashFreq = (int)SliderPerMin.Value;
            
            // If flashes > 60, force audio OFF and disable checkbox
            if (flashFreq > 60)
            {
                ChkFlashAudio.IsChecked = false;
                ChkFlashAudio.IsEnabled = false;
                App.Settings.Current.FlashAudioEnabled = false;
                SliderFlashDuration.IsEnabled = true;
                SliderFlashDuration.Opacity = 1.0;
                TxtAudioWarning.Visibility = Visibility.Visible;
                TxtAudioWarning.Text = Loc.Get("label_audio_off_60_h");
            }
            else
            {
                ChkFlashAudio.IsEnabled = true;
                TxtAudioWarning.Text = Loc.Get("label_max_60_h");
                TxtAudioWarning.Visibility = (ChkFlashAudio.IsChecked ?? true) ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private void SliderPerHour_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtPerHour == null) return;
            TxtPerHour.Text = ((int)e.NewValue).ToString();
            ApplySettingsLive();
        }

        private void SliderTargets_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtTargets == null) return;
            TxtTargets.Text = ((int)e.NewValue).ToString();
            ApplySettingsLive();
        }

        private void ChkRandomizeTargets_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            ApplySettingsLive();
        }

        private void SliderDuration_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtDuration == null) return;
            TxtDuration.Text = ((int)e.NewValue).ToString();
            ApplySettingsLive();
        }

        private void SliderTargetSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtTargetSize == null) return;
            TxtTargetSize.Text = ((int)e.NewValue).ToString();
            ApplySettingsLive();
        }

        private void SliderSubPerMin_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtSubPerMin == null) return;
            TxtSubPerMin.Text = ((int)e.NewValue).ToString();
            ApplySettingsLive();
        }

        private void SliderFrames_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtFrames == null) return;
            TxtFrames.Text = ((int)e.NewValue).ToString();
            ApplySettingsLive();
        }

        private void SliderSubOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtSubOpacity == null) return;
            TxtSubOpacity.Text = $"{(int)e.NewValue}%";
            ApplySettingsLive();
        }

        private void SliderWhisperVol_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtWhisperVol == null) return;
            TxtWhisperVol.Text = $"{(int)e.NewValue}%";
            ApplySettingsLive();
        }

        private void SliderMaster_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtMaster == null) return;
            TxtMaster.Text = $"{(int)e.NewValue}%";
            ApplySettingsLive();

            // Update volume on all currently playing audio
            var volume = (int)e.NewValue;
            App.Video?.UpdateMasterVolume(volume);
            App.BrainDrain?.UpdateMasterVolume(volume);
        }

        private void SliderVideoVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtVideoVolume == null) return;
            TxtVideoVolume.Text = $"{(int)e.NewValue}%";
            App.Settings.Current.VideoVolume = (int)e.NewValue;
            App.Video?.UpdateVideoVolume((int)e.NewValue);
        }

        private void SliderDuck_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtDuck == null) return;
            TxtDuck.Text = $"{(int)e.NewValue}%";
            ApplySettingsLive();
        }

        private void ChkAudioDuck_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            // If ducking was just disabled, immediately restore audio for any ducked sessions
            if (ChkAudioDuck.IsChecked == false)
            {
                App.Audio?.ForceUnduck();
            }

            ApplySettingsLive();
        }

        private void ChkExcludeBambiCloudDucking_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            ApplySettingsLive();
        }

        private void BtnTestAudio_Click(object sender, RoutedEventArgs e)
        {
            var result = App.Audio?.TestAudioPlayback() ?? "Audio service not initialized";
            App.Logger?.Information("[AudioDiag] Test requested:\n{Result}", result);
            System.Windows.MessageBox.Show(result, "Audio Diagnostics", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // Set during PopulateAudioOutputDevices to suppress the SelectionChanged save while
        // we're rebuilding the list (otherwise restoring the persisted selection writes itself
        // back, which is harmless but logs a redundant info line).
        private bool _populatingAudioOutputs;

        private void PopulateAudioOutputDevices()
        {
            if (CmbAudioOutputDevice == null || App.Audio == null) return;
            try
            {
                _populatingAudioOutputs = true;
                var devices = App.Audio.EnumerateOutputDevices();
                CmbAudioOutputDevice.ItemsSource = devices;
                CmbAudioOutputDevice.DisplayMemberPath = nameof(Services.AudioService.AudioOutputDevice.Name);

                // Restore persisted selection: prefer ID match, fall back to name (handles ID
                // changes after driver reinstall / device reorder).
                var savedId = App.Settings?.Current?.AudioOutputDeviceId ?? "";
                var savedName = App.Settings?.Current?.AudioOutputDeviceName ?? "";
                Services.AudioService.AudioOutputDevice? pick = null;
                foreach (var d in devices)
                {
                    if (!string.IsNullOrEmpty(savedId) && d.Id == savedId) { pick = d; break; }
                }
                if (pick == null && !string.IsNullOrEmpty(savedName))
                {
                    foreach (var d in devices)
                    {
                        if (string.Equals(d.Name, savedName, StringComparison.OrdinalIgnoreCase)) { pick = d; break; }
                    }
                }
                CmbAudioOutputDevice.SelectedItem = pick ?? devices[0]; // index 0 = "System default"
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("PopulateAudioOutputDevices failed: {Error}", ex.Message);
            }
            finally
            {
                _populatingAudioOutputs = false;
            }
        }

        private void CmbAudioOutputDevice_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_isLoading || _populatingAudioOutputs) return;
            if (CmbAudioOutputDevice?.SelectedItem is not Services.AudioService.AudioOutputDevice dev) return;
            if (App.Settings?.Current == null) return;

            App.Settings.Current.AudioOutputDeviceId = dev.Id ?? "";
            App.Settings.Current.AudioOutputDeviceName = dev.Name ?? "";
            App.Settings.Save();

            // Invalidate cached device-number resolution + drain pooled WaveOuts (their
            // DeviceNumber is locked once Init() ran, so they need to be re-created).
            App.Audio?.InvalidateOutputDeviceCache();
            try { Services.BubbleService.DrainAudioDevicePool(); } catch { }
            try { QuizWindow.DrainAudioDevicePool(); } catch { }

            App.Logger?.Information("Audio output device set to '{Name}' (id={Id})", dev.Name, string.IsNullOrEmpty(dev.Id) ? "(default)" : dev.Id);
        }

        private void BtnAudioOutputRefresh_Click(object sender, RoutedEventArgs e)
        {
            PopulateAudioOutputDevices();
        }

        private void SliderSpiralOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtSpiralOpacity == null) return;
            TxtSpiralOpacity.Text = $"{(int)e.NewValue}%";
            ApplySettingsLive();
        }

        private void SliderPinkOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtPinkOpacity == null) return;
            TxtPinkOpacity.Text = $"{(int)e.NewValue}%";
            ApplySettingsLive();
        }

        private void SliderBubbleFreq_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtBubbleFreq == null) return;
            TxtBubbleFreq.Text = ((int)e.NewValue).ToString();
            App.Settings.Current.BubblesFrequency = (int)e.NewValue;

            if (_isRunning)
            {
                App.Bubbles.RefreshFrequency();
            }

            App.Settings.Save();
        }

        private void SliderBubbleVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtBubbleVolume == null) return;
            TxtBubbleVolume.Text = $"{(int)e.NewValue}%";
            App.Settings.Current.BubblesVolume = (int)e.NewValue;
            App.Settings.Save();
        }

        private void ChkSpiralEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            
            var isEnabled = ChkSpiralEnabled.IsChecked ?? false;
            App.Settings.Current.SpiralEnabled = isEnabled;
            
            // Immediately update overlay if engine is running
            if (_isRunning)
            {
                App.Overlay.RefreshOverlays();
                App.Logger?.Information("Spiral overlay toggled: {Enabled}", isEnabled);
            }
            
            App.Settings.Save();
        }

        private void ChkPinkFilterEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            
            var isEnabled = ChkPinkFilterEnabled.IsChecked ?? false;
            App.Settings.Current.PinkFilterEnabled = isEnabled;
            
            // Immediately update overlay if engine is running
            if (_isRunning)
            {
                App.Overlay.RefreshOverlays();
                App.Logger?.Information("Pink filter toggled: {Enabled}", isEnabled);
            }
            
            App.Settings.Save();
        }

        private void ChkBubblesEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = ChkBubblesEnabled.IsChecked ?? false;
            App.Settings.Current.BubblesEnabled = isEnabled;

            // Immediately update bubbles if engine is running
            if (_isRunning)
            {
                if (isEnabled)
                {
                    App.Bubbles.Start();
                }
                else
                {
                    App.Bubbles.Stop();
                }
                App.Logger?.Information("Bubbles toggled: {Enabled}", isEnabled);
            }
            
            App.Settings.Save();
        }

        private void ChkLockCardEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            
            var isEnabled = ChkLockCardEnabled.IsChecked ?? false;
            App.Settings.Current.LockCardEnabled = isEnabled;
            
            // Immediately update lock card service if engine is running
            if (_isRunning)
            {
                if (isEnabled)
                {
                    App.LockCard.Start();
                }
                else
                {
                    App.LockCard.Stop();
                }
                App.Logger?.Information("Lock Card toggled: {Enabled}", isEnabled);
            }
            
            App.Settings.Save();
        }

        private void ChkFlashEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = ChkFlashEnabled.IsChecked ?? true;
            App.Settings.Current.FlashEnabled = isEnabled;

            // Immediately start/stop flash service if engine is running
            if (_isRunning)
            {
                if (isEnabled)
                {
                    App.Flash.Start();
                }
                else
                {
                    App.Flash.Stop();
                }
                App.Logger?.Information("Flash images toggled: {Enabled}", isEnabled);
            }

            App.Settings.Save();
        }

        private void ChkClickable_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isClickable = ChkClickable.IsChecked ?? true;
            App.Settings.Current.FlashClickable = isClickable;
            App.Logger?.Information("Flash clickable toggled: {Enabled}", isClickable);
            App.Settings.Save();
        }

        private void ChkCorruption_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = ChkCorruption.IsChecked ?? false;
            App.Settings.Current.CorruptionMode = isEnabled;
            App.Logger?.Information("Hydra mode toggled: {Enabled}", isEnabled);
            App.Settings.Save();
        }

        private void ChkFlashGlow_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = ChkFlashGlow.IsChecked ?? true;
            App.Settings.Current.FlashGlowEnabled = isEnabled;
            App.Logger?.Information("Flash glow toggled: {Enabled}", isEnabled);
            App.Settings.Save();
        }

        /// <summary>
        /// Toggles linked vs independent timing for hydra spawns~ 🔗✨
        /// Linked = hydra children share the parent's remaining timer.
        /// Independent = each hydra spawn gets a fresh full-duration lifetime.
        /// </summary>
        private void ChkHydraLinked_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isLinked = ChkHydraLinked.IsChecked ?? true;
            App.Settings.Current.HydraLinkedTiming = isLinked;
            App.Logger?.Information("Hydra linked timing toggled: {Linked}", isLinked);
            App.Settings.Save();
        }

        private void ChkVideoEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = ChkVideoEnabled.IsChecked ?? false;
            App.Settings.Current.MandatoryVideosEnabled = isEnabled;

            // Immediately start/stop video service if engine is running
            if (_isRunning)
            {
                if (isEnabled)
                {
                    App.Video.Start();
                }
                else
                {
                    App.Video.Stop();
                }
                App.Logger?.Information("Mandatory videos toggled: {Enabled}", isEnabled);
            }

            App.Settings.Save();
        }

        private void ChkSubliminalEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = ChkSubliminalEnabled.IsChecked ?? false;
            App.Settings.Current.SubliminalEnabled = isEnabled;

            // Immediately start/stop subliminal service if engine is running
            if (_isRunning)
            {
                if (isEnabled)
                {
                    App.Subliminal.Start();
                }
                else
                {
                    App.Subliminal.Stop();
                }
                App.Logger?.Information("Subliminals toggled: {Enabled}", isEnabled);
            }

            App.Settings.Save();
        }

        private void ChkAudioWhispers_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = ChkAudioWhispers.IsChecked ?? false;
            App.Settings.Current.SubAudioEnabled = isEnabled;
            App.Logger?.Information("Audio whispers toggled: {Enabled}", isEnabled);
            App.Settings.Save();
        }

        private void ChkMiniGameEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = ChkMiniGameEnabled.IsChecked ?? false;
            App.Settings.Current.AttentionChecksEnabled = isEnabled;
            App.Logger?.Information("Attention checks toggled: {Enabled}", isEnabled);
            App.Settings.Save();
        }

        private void SliderLockCardFreq_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtLockCardFreq == null) return;
            TxtLockCardFreq.Text = ((int)e.NewValue).ToString();
            App.Settings.Current.LockCardFrequency = (int)e.NewValue;
            App.Settings.Save();
        }

        private void SliderLockCardRepeats_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtLockCardRepeats == null) return;
            TxtLockCardRepeats.Text = $"{(int)e.NewValue}x";
            App.Settings.Current.LockCardRepeats = (int)e.NewValue;
            App.Settings.Save();
        }






        private void SliderRampDuration_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtRampDuration == null) return;
            TxtRampDuration.Text = $"{(int)e.NewValue} min";
            ApplySettingsLive();
        }

        private void SliderMultiplier_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtMultiplier == null) return;
            TxtMultiplier.Text = $"{e.NewValue:F1}x";
            ApplySettingsLive();
        }

        #endregion

        #region Button Events
        
        private void ImgLogo_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Track for Neon Obsession achievement (20 rapid clicks on the avatar/logo)
            App.Achievements?.TrackAvatarClick();

            // Bark hook: rolling 60s click count drives the click-escalation eggs.
            try { App.Bark?.NotifyAvatarClicked(); } catch { }

            // Log click count for debugging
            var clickCount = App.Achievements?.Progress.AvatarClickCount ?? 0;
            App.Logger?.Debug("Logo/Avatar clicked! Count: {Count}/20", clickCount);

            // Easter egg tracking (100 clicks in 60 seconds)
            if (!_easterEggTriggered)
            {
                var now = DateTime.Now;
                if (_easterEggFirstClick == DateTime.MinValue || (now - _easterEggFirstClick).TotalSeconds > 60)
                {
                    // Reset if more than 60 seconds passed
                    _easterEggFirstClick = now;
                    _easterEggClickCount = 1;
                }
                else
                {
                    _easterEggClickCount++;
                    if (_easterEggClickCount >= 100)
                    {
                        _easterEggTriggered = true;
                        ShowEasterEgg();
                    }
                }
            }

            // Visual feedback - quick pulse effect
            if (ImgLogo != null)
            {
                var pulse = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 1.0,
                    To = 1.05,
                    Duration = TimeSpan.FromMilliseconds(80),
                    AutoReverse = true
                };

                var scaleTransform = ImgLogo.RenderTransform as System.Windows.Media.ScaleTransform;
                if (scaleTransform == null)
                {
                    scaleTransform = new System.Windows.Media.ScaleTransform(1, 1);
                    ImgLogo.RenderTransformOrigin = new Point(0.5, 0.5);
                    ImgLogo.RenderTransform = scaleTransform;
                }

                scaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, pulse);
                scaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, pulse);
            }
        }

        #region Marquee Banner

        private void InitializeMarqueeBanner()
        {
            try
            {
                // Migrate old message to new default if needed
                var currentSaved = App.Settings.Current.MarqueeMessage;
                if (string.IsNullOrWhiteSpace(currentSaved) ||
                    currentSaved.Contains("WELCOME TO YOUR CONDITIONING") ||
                    currentSaved.Contains("RELAX AND SUBMIT"))
                {
                    App.Settings.Current.MarqueeMessage = "GOOD GIRLS CONDITION DAILY     ❤️🔒";
                }

                // Need to wait for layout to measure text width
                MarqueeText.Loaded += (s, e) => StartMarqueeAnimation();
                MarqueeCanvas.SizeChanged += (s, e) => StartMarqueeAnimation();

                // Start immediately if already loaded
                if (MarqueeText.IsLoaded)
                {
                    Dispatcher.BeginInvoke(new Action(StartMarqueeAnimation), System.Windows.Threading.DispatcherPriority.Loaded);
                }

                // Fetch from server on startup (with short delay)
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _ = Task.Delay(3000).ContinueWith(_ =>
                    {
                        if (Application.Current?.Dispatcher?.HasShutdownStarted == true) return;
                        Dispatcher.Invoke(RefreshMarqueeFromSettings);
                    });
                }));

                // Check for server-controlled update banner (fallback for when auto-update fails)
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _ = Task.Delay(5000).ContinueWith(_ =>
                    {
                        if (Application.Current?.Dispatcher?.HasShutdownStarted == true) return;
                        Dispatcher.Invoke(CheckServerUpdateBanner);
                    });
                }));

                // Check for server-triggered announcement popup
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _ = Task.Delay(7000).ContinueWith(_ =>
                    {
                        if (Application.Current?.Dispatcher?.HasShutdownStarted == true) return;
                        Dispatcher.Invoke(CheckServerAnnouncement);
                    });
                }));

                // Start 5-minute refresh timer to check for server-side message updates
                _marqueeRefreshTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMinutes(5)
                };
                _marqueeRefreshTimer.Tick += (s, e) => RefreshMarqueeFromSettings();
                _marqueeRefreshTimer.Start();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to initialize marquee banner: {Error}", ex.Message);
            }
        }

        private async void RefreshMarqueeFromSettings()
        {
            try
            {
                // Fetch marquee message from server
                using var httpClient = new System.Net.Http.HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(10)
                };

                var response = await httpClient.GetAsync("https://codebambi-proxy.vercel.app/config/marquee");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = System.Text.Json.JsonSerializer.Deserialize<MarqueeResponse>(json);
                    var newMessage = result?.message;

                    if (!string.IsNullOrWhiteSpace(newMessage) && newMessage != _currentMarqueeMessage)
                    {
                        App.Logger?.Information("Marquee message updated from server: {Message}", newMessage);
                        App.Settings.Current.MarqueeMessage = newMessage;
                        Dispatcher.Invoke(() => StartMarqueeAnimation());
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to refresh marquee from server: {Error}", ex.Message);
            }
        }

        private class MarqueeResponse
        {
            public string? message { get; set; }
        }

        #endregion

        #region Server-Controlled Update Banner

        private class UpdateBannerResponse
        {
            public bool enabled { get; set; }
            public string? version { get; set; }
            public string? message { get; set; }
            public string? url { get; set; }
        }

        // Store the server-provided update URL for redirect
        private string? _serverUpdateUrl;

        /// <summary>
        /// Check server for forced update banner configuration.
        /// This is a fallback when automatic update detection fails.
        /// </summary>
        private async void CheckServerUpdateBanner()
        {
            try
            {
                using var httpClient = new System.Net.Http.HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(10)
                };

                var response = await httpClient.GetAsync("https://codebambi-proxy.vercel.app/config/update-banner");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = System.Text.Json.JsonSerializer.Deserialize<UpdateBannerResponse>(json);

                    if (result?.enabled == true && !string.IsNullOrWhiteSpace(result.version))
                    {
                        // Check if user is on an older version than the one in the banner
                        var currentVersion = Services.UpdateService.GetCurrentVersion();
                        if (Version.TryParse(result.version, out var bannerVersion) && bannerVersion > currentVersion)
                        {
                            App.Logger?.Information("Server update banner enabled: version={Version}, message={Message}",
                                result.version, result.message);

                            // Store the URL if provided
                            _serverUpdateUrl = result.url;

                            // Update the button on UI thread
                            Dispatcher.Invoke(() =>
                            {
                                if (BtnUpdateAvailable != null)
                                {
                                    BtnUpdateAvailable.Tag = "UrgentUpdate";
                                    BtnUpdateAvailable.Content = $"UPDATE AVAILABLE v{result.version}";
                                    BtnUpdateAvailable.ToolTip = !string.IsNullOrEmpty(result.url)
                                        ? $"Version {result.version} is available - Click to visit download page!"
                                        : $"Version {result.version} is available - Click to update!";
                                }
                            });
                        }
                        else
                        {
                            App.Logger?.Debug("Server update banner: user already on version {Current}, banner is for {Banner}",
                                currentVersion, result.version);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to check server update banner: {Error}", ex.Message);
            }
        }

        #endregion

        #region Server-Triggered Announcement

        private class AnnouncementResponse
        {
            public bool enabled { get; set; }
            public string? id { get; set; }
            public string? title { get; set; }
            public string? message { get; set; }
            public string? image_url { get; set; }
            public string? link_url { get; set; }
            public string? theme { get; set; }
        }

        /// <summary>
        /// Check server for a triggered announcement popup. Shows once per unique announcement ID.
        /// </summary>
        private async void CheckServerAnnouncement()
        {
            try
            {
                using var httpClient = new System.Net.Http.HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(10)
                };

                var url = "https://codebambi-proxy.vercel.app/config/announcement";
                var unifiedId = App.Settings?.Current?.UnifiedId;
                if (!string.IsNullOrWhiteSpace(unifiedId))
                {
                    url += $"?unified_id={Uri.EscapeDataString(unifiedId)}";
                }

                var response = await httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = System.Text.Json.JsonSerializer.Deserialize<AnnouncementResponse>(json);

                    if (result?.enabled == true
                        && !string.IsNullOrWhiteSpace(result.id)
                        && !string.IsNullOrWhiteSpace(result.title)
                        && result.id != App.Settings?.Current?.DismissedAnnouncementId)
                    {
                        App.Logger?.Information("Server announcement received: id={Id}, title={Title}", result.id, result.title);

                        Dispatcher.Invoke(() =>
                        {
                            var popup = new AnnouncementPopup(
                                result.id!,
                                result.title!,
                                result.message ?? "",
                                result.image_url,
                                result.link_url,
                                result.theme);
                            popup.Show();
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to check server announcement: {Error}", ex.Message);
            }
        }

        #endregion

        #region Marquee Animation

        private void StartMarqueeAnimation()
        {
            try
            {
                // Stop existing animation
                _marqueeStoryboard?.Stop();

                var canvasWidth = MarqueeCanvas.ActualWidth;
                if (canvasWidth <= 0) return;

                // Get the original message
                var message = App.Settings.Current.MarqueeMessage;
                if (string.IsNullOrWhiteSpace(message))
                {
                    message = "GOOD GIRLS CONDITION DAILY     ❤️🔒";
                }
                message = message.ToUpperInvariant();

                // Track current message for refresh detection
                _currentMarqueeMessage = message;

                // Create single segment with separator (doubled message + spacing)
                var separator = "          "; // 10 spaces between repetitions
                var singleSegment = message + separator + message + separator;

                // Measure single segment width
                var tempBlock = new TextBlock
                {
                    Text = singleSegment,
                    FontFamily = MarqueeText.FontFamily,
                    FontSize = MarqueeText.FontSize,
                    FontWeight = MarqueeText.FontWeight
                };
                tempBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var segmentWidth = tempBlock.DesiredSize.Width;

                if (segmentWidth <= 0) return;

                // Calculate how many segments needed to fill canvas + one extra for seamless loop
                var segmentsNeeded = (int)Math.Ceiling(canvasWidth / segmentWidth) + 2;
                var fullText = string.Concat(Enumerable.Repeat(singleSegment, segmentsNeeded));
                MarqueeText.Text = fullText;

                // Animation: scroll exactly one segment width, then loop back seamlessly
                // From 0 to -segmentWidth creates perfect loop since next segment is identical
                var animation = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0,
                    To = -segmentWidth,
                    Duration = TimeSpan.FromSeconds(segmentWidth / 80), // Speed: 80 pixels per second
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
                };

                _marqueeStoryboard = new System.Windows.Media.Animation.Storyboard();
                _marqueeStoryboard.Children.Add(animation);
                System.Windows.Media.Animation.Storyboard.SetTarget(animation, MarqueeText);
                System.Windows.Media.Animation.Storyboard.SetTargetProperty(animation,
                    new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.X)"));

                _marqueeStoryboard.Begin();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to start marquee animation: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Updates the marquee message from server/external source.
        /// Call this method when receiving a new message from the server.
        /// </summary>
        public void UpdateMarqueeMessage(string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message)) return;

                var newMessage = message.Trim().ToUpperInvariant();
                if (!newMessage.EndsWith("•") && !newMessage.EndsWith(" "))
                {
                    newMessage += " • ";
                }

                App.Settings.Current.MarqueeMessage = newMessage;
                Dispatcher.Invoke(() =>
                {
                    MarqueeText.Text = newMessage;
                    StartMarqueeAnimation();
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to update marquee message: {Error}", ex.Message);
            }
        }

        #endregion

        private async void ShowEasterEgg()
        {
            int readerCount = -1;
            try
            {
                if (App.ProfileSync != null)
                    readerCount = await App.ProfileSync.RecordEasterEggReadAsync();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to fetch easter egg reader count");
            }

            if (Application.Current?.Dispatcher == null || Application.Current.Dispatcher.HasShutdownStarted)
                return;

            // Once ever: add the companion's recorded voice on top of the written note (additive —
            // the note dialog still shows as before). The recording is bundled later; PlayNoteClip
            // no-ops if the file is missing, and we only latch the flag once it actually plays, so it
            // still fires the first time the clip exists. Start it before the modal note so the voice
            // plays while the note is read.
            if (App.Settings?.Current?.NewYearNoteReactionSeen != true)
            {
                var notePath = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "note_newyear.wav");
                if (App.AvatarWindow?.PlayNoteClip(notePath) == true)
                    App.Settings.Current.NewYearNoteReactionSeen = true;
            }

            var easterEggWindow = new EasterEggWindow(readerCount);
            easterEggWindow.Owner = this;
            easterEggWindow.ShowDialog();
        }

        private void BtnTestVideo_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("test_video"); } catch { }
            try
            {
                // Check if video is already playing - offer force reset if stuck
                if (App.Video.IsPlaying)
                {
                    var result = MessageBox.Show(
                        "A video appears to be playing.\n\nIf you don't see a video, it may be stuck. Click Yes to force reset and try again.",
                        "Video Playing",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        App.Logger?.Warning("User requested force reset of stuck video state");
                        App.Video.ForceCleanup();
                        App.InteractionQueue?.ForceReset();
                        // Continue to trigger video below
                    }
                    else
                    {
                        return;
                    }
                }

                // Check if another interaction is blocking - offer force reset if stuck
                if (App.InteractionQueue != null && !App.InteractionQueue.CanStart)
                {
                    var result = MessageBox.Show(
                        $"Another interaction is in progress ({App.InteractionQueue.CurrentInteraction}).\n\nIf this seems stuck, click Yes to force reset and try again.",
                        "Please Wait",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        App.Logger?.Warning("User requested force reset of stuck interaction queue");
                        App.Video.ForceCleanup();
                        App.InteractionQueue.ForceReset();
                        // Continue to trigger video below
                    }
                    else
                    {
                        return;
                    }
                }

                App.Video.TriggerVideo();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Error in BtnTestVideo_Click");
                MessageBox.Show($"Error triggering video: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TriggerStartupVideo()
        {
            var startupPath = App.Settings.Current.StartupVideoPath;

            // If a specific video is configured, play that one
            if (!string.IsNullOrEmpty(startupPath) && System.IO.File.Exists(startupPath))
            {
                App.Logger?.Information("Playing startup video: {Path}", startupPath);
                App.Video.PlaySpecificVideo(startupPath, App.Settings.Current.StrictLockEnabled);
            }
            else
            {
                // Play a random video
                App.Logger?.Information("Playing random startup video");
                App.Video.TriggerVideo(silentIfEmpty: true);
            }
        }

        private void BtnSelectStartupVideo_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = Loc.Get("title_select_startup_video"),
                Filter = "Video Files|*.mp4;*.mov;*.avi;*.wmv;*.mkv;*.webm|All Files|*.*",
                InitialDirectory = System.IO.Path.Combine(App.EffectiveAssetsPath, "videos")
            };

            if (dialog.ShowDialog() == true)
            {
                App.Settings.Current.StartupVideoPath = dialog.FileName;
                TxtStartupVideo.Text = System.IO.Path.GetFileName(dialog.FileName);
                App.Settings.Save();
                App.Logger?.Information("Startup video set to: {Path}", dialog.FileName);
            }
        }

        private void BtnClearStartupVideo_Click(object sender, RoutedEventArgs e)
        {
            App.Settings.Current.StartupVideoPath = null;
            TxtStartupVideo.Text = Loc.Get("label_random");
            App.Settings.Save();
            App.Logger?.Information("Startup video cleared - will use random");
        }

        private void BtnManageAttention_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new TextEditorDialog("Attention Targets", App.Settings.Current.AttentionPool);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true && dialog.ResultData != null)
            {
                App.Settings.Current.AttentionPool = dialog.ResultData;
                App.Settings.Save();
                App.Logger?.Information("Attention pool updated: {Count} items", dialog.ResultData.Count);
            }
        }

        private void BtnAttentionStyle_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AttentionTargetEditorDialog();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void BtnSubliminalSettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ColorEditorDialog();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void BtnManageMessages_Click(object sender, RoutedEventArgs e)
        {
            var oldKeys = new HashSet<string>(App.Settings.Current.SubliminalPool.Keys);
            var defaults = App.Mods?.GetDefaultSubliminalPool() ?? Models.BuiltInMods.BambiSleep.SubliminalPool ?? new Dictionary<string, bool>();

            var dialog = new TextEditorDialog("Subliminal Messages", App.Settings.Current.SubliminalPool);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true && dialog.ResultData != null)
            {
                // Track default triggers the user explicitly removed
                var newKeys = new HashSet<string>(dialog.ResultData.Keys);
                foreach (var key in oldKeys)
                {
                    if (!newKeys.Contains(key) && defaults.ContainsKey(key))
                        App.Settings.Current.RemovedDefaultSubliminals.Add(key);
                }

                // If user re-adds a previously removed default, un-track it
                foreach (var key in newKeys)
                {
                    App.Settings.Current.RemovedDefaultSubliminals.Remove(key);
                }

                // Remember phrases the user added by hand so the cross-mod prune never deletes
                // them (a custom phrase can legitimately collide with another mod's default).
                foreach (var key in newKeys)
                {
                    if (!oldKeys.Contains(key))
                        App.Settings.Current.UserAddedSubliminals.Add(key);
                }
                // Forget any user-added phrase they just removed.
                foreach (var key in oldKeys)
                {
                    if (!newKeys.Contains(key))
                        App.Settings.Current.UserAddedSubliminals.Remove(key);
                }

                App.Settings.Current.SubliminalPool = dialog.ResultData;
                App.Settings.Save();
                App.Logger?.Information("Subliminal pool updated: {Count} items", dialog.ResultData.Count);
            }
        }

        private void BtnManageLockCardPhrases_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new TextEditorDialog("Lock Card Phrases", App.Settings.Current.LockCardPhrases);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true && dialog.ResultData != null)
            {
                App.Settings.Current.LockCardPhrases = dialog.ResultData;
                App.Settings.Save();
                App.Logger?.Information("Lock card phrases updated: {Count} items", dialog.ResultData.Count);
            }
        }

        private void BtnTestLockCard_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("test_lockcard"); } catch { }
            var phrases = App.Settings.Current.LockCardPhrases;
            var enabledPhrases = phrases.Where(p => p.Value).Select(p => p.Key).ToList();
            
            if (enabledPhrases.Count == 0)
            {
                MessageBox.Show(Loc.Get("msg_no_phrases_enabled_add_some_phrases_first"), "No Phrases", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            // Show the actual lock card
            App.LockCard.TestLockCard();
        }

        private void BtnLockCardSettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new LockCardColorDialog();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void ChkLockCardStrict_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = ChkLockCardStrict.IsChecked ?? false;

            // Show warning when enabling strict mode
            if (isEnabled)
            {
                var confirmed = WarningDialog.ShowDoubleWarning(this,
                    "Strict Lock Card",
                    "• You will NOT be able to escape lock cards with ESC\n" +
                    "• You MUST type the phrase the required number of times\n" +
                    "• This can be very restrictive!");

                if (!confirmed)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _isLoading = true;
                        ChkLockCardStrict.IsChecked = false;
                        _isLoading = false;
                    }));
                    return;
                }
            }

            App.Settings.Current.LockCardStrict = isEnabled;
            App.Settings?.Save();
        }

        private void BtnSelectSpiral_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "GIF Files (*.gif)|*.gif|All Image Files|*.gif;*.png;*.jpg;*.jpeg",
                Title = Loc.Get("title_select_spiral_gif")
            };
            
            // Start in last used directory if available
            var currentPath = App.Settings.Current.SpiralPath;
            if (!string.IsNullOrEmpty(currentPath) && File.Exists(currentPath))
            {
                dialog.InitialDirectory = Path.GetDirectoryName(currentPath);
            }

            if (dialog.ShowDialog() == true)
            {
                App.Settings.Current.SpiralPath = dialog.FileName;
                App.Settings.Save();
                
                // Refresh overlays if running
                if (_isRunning)
                {
                    App.Overlay.RefreshOverlays();
                }
                
                MessageBox.Show($"Selected: {Path.GetFileName(dialog.FileName)}", "Spiral Selected");
            }
        }

        private void BtnPrevImage_Click(object sender, RoutedEventArgs e)
        {
            // Image carousel navigation
        }

        private void BtnNextImage_Click(object sender, RoutedEventArgs e)
        {
            // Image carousel navigation
        }


        private void BtnPickAssetsFolder_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("pick_assets"); } catch { }
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select a folder for your custom assets (images and videos).\nTwo subfolders 'images' and 'videos' will be created.",
                ShowNewFolderButton = true,
                UseDescriptionForTitle = true
            };

            // Start from current custom path if set, otherwise default
            var currentPath = App.Settings?.Current?.CustomAssetsPath;
            var oldEffectivePath = App.EffectiveAssetsPath;
            if (!string.IsNullOrWhiteSpace(currentPath) && Directory.Exists(currentPath))
            {
                dialog.SelectedPath = currentPath;
            }
            else
            {
                dialog.SelectedPath = App.UserAssetsPath;
            }

            // Own the dialog to the active popup if one is open — otherwise the dialog
            // renders behind the popup. If no popup, fall back to MainWindow.
            var ownerWindow = (_activeFeaturePopup != null && _activeFeaturePopup.IsVisible)
                ? (Window)_activeFeaturePopup
                : this;
            var owner = new Win32WindowWrapper(new System.Windows.Interop.WindowInteropHelper(ownerWindow).Handle);
            if (dialog.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK)
            {
                var selectedPath = dialog.SelectedPath;
                var newPacksFolder = Path.Combine(selectedPath, ".packs");
                var shouldMovePacks = false;
                var packFoldersToMove = new List<(string SourceFolder, string PackName)>();
                long totalBytes = 0;

                // Check multiple locations for existing packs (retrocompatibility)
                // 1. Current effective path (where user currently has assets)
                // 2. Default path (in case packs were stranded there from before)
                var locationsToCheck = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    Path.Combine(oldEffectivePath, ".packs"),
                    Path.Combine(App.UserAssetsPath, ".packs")
                };

                // Don't check the new location (we're moving TO there)
                locationsToCheck.Remove(newPacksFolder);

                App.Logger?.Information("Asset folder change: checking {Count} locations for packs: {Locations}",
                    locationsToCheck.Count, string.Join(", ", locationsToCheck));

                foreach (var packsFolder in locationsToCheck)
                {
                    if (!Directory.Exists(packsFolder)) continue;

                    foreach (var dir in Directory.GetDirectories(packsFolder))
                    {
                        var manifestPath = Path.Combine(dir, ".manifest.enc");
                        if (!File.Exists(manifestPath)) continue;

                        // Try to read pack name from manifest
                        string packName = Path.GetFileName(dir); // Default to GUID if we can't read name
                        try
                        {
                            var json = Services.PackEncryptionService.LoadEncryptedManifest(manifestPath);
                            var manifest = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(json);
                            if (manifest?.PackName != null)
                            {
                                packName = (string)manifest.PackName;
                            }
                        }
                        catch { }

                        packFoldersToMove.Add((dir, packName));

                        // Calculate folder size
                        try
                        {
                            foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                            {
                                totalBytes += new FileInfo(file).Length;
                            }
                        }
                        catch { }
                    }
                }

                App.Logger?.Information("Found {Count} packs to potentially move, total size: {Size} bytes",
                    packFoldersToMove.Count, totalBytes);

                if (packFoldersToMove.Count > 0)
                {
                    var sizeText = FormatFileSize(totalBytes);
                    var packNames = string.Join("\n• ", packFoldersToMove.Select(p => p.PackName));

                    var moveResult = MessageBox.Show(
                        Loc.GetF("msg_move_packs_confirm", packFoldersToMove.Count, sizeText, packNames,
                            totalBytes > 500_000_000 ? Loc.Get("msg_may_take_a_moment") : ""),
                        Loc.Get("title_move_downloaded_packs"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    shouldMovePacks = moveResult == MessageBoxResult.Yes;
                }

                // Create subfolders
                Directory.CreateDirectory(Path.Combine(selectedPath, "images"));
                Directory.CreateDirectory(Path.Combine(selectedPath, "videos"));

                // Move packs if requested
                if (shouldMovePacks && packFoldersToMove.Count > 0)
                {
                    try
                    {
                        // Create new packs folder if needed
                        if (!Directory.Exists(newPacksFolder))
                        {
                            var di = Directory.CreateDirectory(newPacksFolder);
                            di.Attributes |= FileAttributes.Hidden;
                        }

                        var movedCount = 0;
                        var registeredCount = 0;
                        foreach (var (sourceFolder, packName) in packFoldersToMove)
                        {
                            var guid = Path.GetFileName(sourceFolder);
                            var destDir = Path.Combine(newPacksFolder, guid);
                            if (!Directory.Exists(destDir))
                            {
                                // Use copy+delete instead of Directory.Move to support
                                // moving packs across different drive volumes
                                CopyDirectoryRecursive(sourceFolder, destDir);
                                Directory.Delete(sourceFolder, recursive: true);
                                movedCount++;
                                App.Logger?.Information("Moved pack '{PackName}' from {Source} to {Dest}", packName, sourceFolder, destDir);
                            }
                            else
                            {
                                App.Logger?.Warning("Pack folder already exists at destination, skipping: {Dest}", destDir);
                            }

                            // Register pack in settings (fix for packs not being detected after move)
                            var manifestPath = Path.Combine(destDir, ".manifest.enc");
                            if (File.Exists(manifestPath))
                            {
                                try
                                {
                                    var json = Services.PackEncryptionService.LoadEncryptedManifest(manifestPath);
                                    var manifest = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(json);
                                    var packId = (string?)manifest?.PackId;

                                    if (!string.IsNullOrEmpty(packId))
                                    {
                                        // Ensure settings collections exist
                                        App.Settings.Current.InstalledPackIds ??= new List<string>();
                                        App.Settings.Current.PackGuidMap ??= new Dictionary<string, string>();
                                        App.Settings.Current.ActivePackIds ??= new List<string>();

                                        // Add to InstalledPackIds if not present
                                        if (!App.Settings.Current.InstalledPackIds.Contains(packId))
                                        {
                                            App.Settings.Current.InstalledPackIds.Add(packId);
                                        }

                                        // Update PackGuidMap (overwrite if different GUID was stored)
                                        App.Settings.Current.PackGuidMap[packId] = guid;

                                        // Auto-activate pack so it shows immediately
                                        if (!App.Settings.Current.ActivePackIds.Contains(packId))
                                        {
                                            App.Settings.Current.ActivePackIds.Add(packId);
                                        }

                                        registeredCount++;
                                        App.Logger?.Information("Registered pack in settings: {PackId} -> {Guid}", packId, guid);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    App.Logger?.Warning(ex, "Failed to register pack from manifest: {Path}", manifestPath);
                                }
                            }
                        }

                        App.Logger?.Information("Moved {MovedCount}/{Total} packs, registered {RegCount} in settings",
                            movedCount, packFoldersToMove.Count, registeredCount);
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Error(ex, "Failed to move packs to new location");
                        MessageBox.Show(
                            Loc.GetF("msg_could_not_move_packs_0", ex.Message),
                            Loc.Get("label_warning"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }

                // Save to settings
                App.Settings.Current.CustomAssetsPath = selectedPath;
                App.Settings.Save();

                // Refresh all services to use new path
                App.Flash?.RefreshImagesPath();
                App.Video?.RefreshVideosPath();
                App.BubbleCount?.RefreshVideosPath();
                App.ContentPacks?.RefreshPacksPath();

                // Refresh the asset tree to show new location
                RefreshAssetTree();

                MessageBox.Show(
                    Loc.GetF("msg_custom_assets_folder_set_0", selectedPath) +
                    (shouldMovePacks ? "\n\n" + Loc.Get("msg_packs_have_been_moved") : ""),
                    Loc.Get("title_assets_folder_set"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                App.Logger?.Information("Custom assets path set to: {Path}", selectedPath);
            }
        }

        /// <summary>
        /// Recursively copies a directory. Works across drive volumes unlike Directory.Move.
        /// </summary>
        private static void CopyDirectoryRecursive(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), overwrite: true);
            }
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                CopyDirectoryRecursive(dir, Path.Combine(destinationDir, Path.GetFileName(dir)));
            }
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
            if (bytes >= 1024 * 1024)
                return $"{bytes / (1024.0 * 1024.0):F1} MB";
            if (bytes >= 1024)
                return $"{bytes / 1024.0:F1} KB";
            return $"{bytes} bytes";
        }

        private void BtnRefreshAssets_Click(object sender, RoutedEventArgs e)
        {
            // Rescan every asset consumer so newly added/removed files are picked up without a restart
            // (#336 — BUG-BWJ7EGRTUP: the Assets help tip referenced a Refresh button that didn't exist).
            App.Flash?.RefreshImagesPath();
            App.Video?.RefreshVideosPath();
            App.BubbleCount?.RefreshVideosPath();
            RefreshAssetTree();
            MessageBox.Show(Loc.Get("msg_assets_refreshed"), Loc.Get("title_success"));
        }

        private void BtnViewLog_Click(object sender, RoutedEventArgs e)
        {
            var logPath = Path.Combine(App.UserDataPath, "logs");
            if (Directory.Exists(logPath))
            {
                Process.Start("explorer.exe", logPath);
            }
            else
            {
                MessageBox.Show(Loc.Get("msg_no_logs_found"), "Info");
            }
        }

        private void BtnPanicKey_Click(object sender, RoutedEventArgs e)
        {
            // Don't show a blocking MessageBox: the global keyboard hook fires through
            // it, so the next keypress would set the panic key AND immediately trigger
            // a panic. Instead, just enter capture mode — both this window's button and
            // the SystemFeatureControl popup button show "Press any key..." until the
            // hook captures the next key.
            _isCapturingPanicKey = true;
            UpdatePanicKeyButton();
        }

        private void ChkStrictLock_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = ChkStrictLock.IsChecked ?? false;

            // Show warning when enabling strict mode
            if (isEnabled)
            {
                var confirmed = WarningDialog.ShowDoubleWarning(this,
                    "Strict Lock",
                    "• You will NOT be able to skip or close videos\n" +
                    "• Videos MUST be watched to completion\n" +
                    "• The only way out is the panic key (if enabled)\n" +
                    "• This can be very intense and restrictive");

                if (!confirmed)
                {
                    // Defer revert so it runs after the dialog's event stack fully unwinds,
                    // preventing WPF toggle animation from getting stuck in the ON position.
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _isLoading = true;
                        ChkStrictLock.IsChecked = false;
                        _isLoading = false;
                    }));
                    return;
                }
            }

            App.Settings.Current.StrictLockEnabled = isEnabled;
            App.Settings?.Save();
        }

        private void ChkNoPanic_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isNoPanic = ChkNoPanic.IsChecked ?? false;

            // Show warning when enabling no-panic mode
            if (isNoPanic)
            {
                var confirmed = WarningDialog.ShowDoubleWarning(this,
                    "Disable Panic Key",
                    "• You will have NO emergency escape option\n" +
                    "• The ONLY way to exit will be the Exit button\n" +
                    "• Combined with Strict Lock, this is VERY restrictive\n" +
                    "• Make sure you know what you're doing!");

                if (!confirmed)
                {
                    // Defer revert so it runs after the dialog's event stack fully unwinds,
                    // preventing WPF toggle animation from getting stuck in the ON position.
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _isLoading = true;
                        ChkNoPanic.IsChecked = false;
                        _isLoading = false;
                    }));
                    return;
                }

                // Stop keyboard hook when panic key is disabled (privacy improvement)
                // But keep it running if keyword triggers need it
                if (App.Settings.Current.KeywordTriggersEnabled != true)
                    _keyboardHook?.Stop();
                App.Settings.Current.PanicKeyEnabled = false;
                App.Settings?.Save();
                App.Logger?.Information("Keyboard hook stopped - panic key disabled");
            }
            else
            {
                // Start keyboard hook when panic key is re-enabled
                _keyboardHook?.Start();
                App.Settings.Current.PanicKeyEnabled = true;
                App.Settings?.Save();
                App.Logger?.Information("Keyboard hook started - panic key enabled");
            }
        }

        private void ChkPerformanceMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            App.Settings.Current.PerformanceMode = ChkPerformanceMode.IsChecked ?? false;
            App.Logger?.Information("Performance mode set to {Enabled}", App.Settings.Current.PerformanceMode);
        }

        private void ChkAutoPerformance_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            App.Settings.Current.AutoPerformanceMode = ChkAutoPerformance.IsChecked ?? true;
            App.Logger?.Information("Auto performance mode set to {Enabled}", App.Settings.Current.AutoPerformanceMode);
        }

        private void ChkVideoHwDecode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            App.Settings.Current.VideoHardwareDecoding = ChkVideoHwDecode.IsChecked ?? true;
            App.Logger?.Information("Video hardware decoding set to {Enabled}", App.Settings.Current.VideoHardwareDecoding);
        }

        private void ChkOfflineMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = ChkOfflineMode.IsChecked ?? false;

            if (isEnabled)
            {
                // Enabling offline mode - prompt for username if not set
                if (string.IsNullOrWhiteSpace(App.Settings.Current.OfflineUsername))
                {
                    var dialog = new OfflineUsernameDialog();
                    dialog.Owner = this;

                    if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Username))
                    {
                        App.Settings.Current.OfflineUsername = dialog.Username;
                    }
                    else
                    {
                        // User cancelled - revert checkbox
                        ChkOfflineMode.IsChecked = false;
                        return;
                    }
                }

                // Set offline mode
                App.Settings.Current.OfflineMode = true;

                // Disconnect all network services
                DisconnectNetworkServices();

                App.Logger?.Information("Offline mode enabled with username '{Username}'",
                    App.Settings.Current.OfflineUsername);
            }
            else
            {
                // Disabling offline mode
                App.Settings.Current.OfflineMode = false;
                App.Logger?.Information("Offline mode disabled");
            }

            // Update UI to reflect offline mode state
            UpdateOfflineModeUI(isEnabled);

            App.Settings.Save();
        }

        /// <summary>
        /// Updates UI elements based on offline mode state.
        /// Disables/enables login buttons, browser, and updates banner.
        /// </summary>
        private void UpdateOfflineModeUI(bool isOffline)
        {
            try
            {
                // === LOGIN BUTTONS (disable all of them) ===

                // Patreon login button (in Patreon Exclusives tab)
                if (BtnPatreonLogin != null)
                {
                    BtnPatreonLogin.IsEnabled = !isOffline;
                    BtnPatreonLogin.Opacity = isOffline ? 0.5 : 1.0;
                    if (isOffline)
                        BtnPatreonLogin.ToolTip = Loc.Get("tooltip_disabled_in_offline_mode");
                    else
                        BtnPatreonLogin.ToolTip = null;
                }

                // Discord login button (in Patreon Exclusives tab)
                if (BtnDiscordLogin != null)
                {
                    BtnDiscordLogin.IsEnabled = !isOffline;
                    BtnDiscordLogin.Opacity = isOffline ? 0.5 : 1.0;
                    if (isOffline)
                        BtnDiscordLogin.ToolTip = Loc.Get("tooltip_disabled_in_offline_mode");
                    else
                        BtnDiscordLogin.ToolTip = null;
                }

                // Unified login button (in main area)
                if (BtnUnifiedLogin != null)
                {
                    BtnUnifiedLogin.IsEnabled = !isOffline;
                    BtnUnifiedLogin.Opacity = isOffline ? 0.5 : 1.0;
                    if (isOffline)
                        BtnUnifiedLogin.ToolTip = Loc.Get("tooltip_disabled_in_offline_mode");
                }

                // Discord tab login button (in Profile/Discord tab)
                if (BtnDiscordTabLogin != null)
                {
                    BtnDiscordTabLogin.IsEnabled = !isOffline;
                    BtnDiscordTabLogin.Opacity = isOffline ? 0.5 : 1.0;
                    if (isOffline)
                        BtnDiscordTabLogin.ToolTip = Loc.Get("tooltip_disabled_in_offline_mode");
                }

                // === BROWSER SECTION ===

                // Disable browser controls
                if (RbBambiCloud != null)
                {
                    RbBambiCloud.IsEnabled = !isOffline;
                    RbBambiCloud.Opacity = isOffline ? 0.5 : 1.0;
                }
                if (RbHypnoTube != null)
                {
                    RbHypnoTube.IsEnabled = !isOffline;
                    RbHypnoTube.Opacity = isOffline ? 0.5 : 1.0;
                }
                if (BtnPopOutBrowser != null)
                {
                    BtnPopOutBrowser.IsEnabled = !isOffline;
                    BtnPopOutBrowser.Opacity = isOffline ? 0.5 : 1.0;
                }
                if (TxtBrowserStatus != null)
                {
                    TxtBrowserStatus.Text = isOffline ? "● Offline" : "● Ready";
                    TxtBrowserStatus.Foreground = isOffline
                        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(128, 128, 128))
                        : (System.Windows.Media.Brush)FindResource("PinkBrush");
                }

                // Navigate browser to blank page and show offline message
                if (isOffline)
                {
                    // Navigate to blank page to stop any loading content
                    if (_browser?.WebView?.CoreWebView2 != null)
                    {
                        try
                        {
                            _browser.WebView.CoreWebView2.Navigate("about:blank");
                        }
                        catch (Exception ex)
                        {
                            App.Logger?.Debug("Could not navigate browser to blank: {Error}", ex.Message);
                        }
                    }

                    // Show offline message over browser
                    if (BrowserLoadingText != null)
                    {
                        BrowserLoadingText.Visibility = Visibility.Visible;
                        BrowserLoadingText.Text = Loc.Get("label_browser_disabled_in_offline_mode");
                    }
                    if (BrowserContainer != null)
                    {
                        BrowserContainer.Opacity = 0.3;
                    }
                }
                else
                {
                    // Hide offline message and restore browser
                    if (BrowserLoadingText != null)
                    {
                        BrowserLoadingText.Visibility = Visibility.Collapsed;
                    }
                    if (BrowserContainer != null)
                    {
                        BrowserContainer.Opacity = 1.0;
                    }

                    // Reload the browser with the currently selected site
                    if (_browser?.WebView?.CoreWebView2 != null)
                    {
                        try
                        {
                            var isBambiCloud = RbBambiCloud?.IsChecked == true;
                            var url = isBambiCloud
                                ? "https://bambicloud.com/"
                                : "https://hypnotube.com/";
                            _browser.Navigate(url);
                            App.Logger?.Information("Browser reloaded after exiting offline mode: {Url}", url);
                        }
                        catch (Exception ex)
                        {
                            App.Logger?.Debug("Could not reload browser: {Error}", ex.Message);
                        }
                    }
                }

                // Update welcome banner
                UpdateBannerWelcomeMessage();

                App.Logger?.Debug("Offline mode UI updated: {State}", isOffline ? "disabled" : "enabled");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Error updating offline mode UI");
            }
        }

        /// <summary>
        /// Disconnects all network services when entering offline mode.
        /// This ensures no external connections are maintained.
        /// </summary>
        private void DisconnectNetworkServices()
        {
            try
            {
                // Stop profile sync heartbeat (server pings)
                App.ProfileSync?.StopHeartbeat();

                // Disconnect Discord Rich Presence (IPC connection)
                if (App.DiscordRpc?.IsEnabled == true)
                {
                    App.DiscordRpc.IsEnabled = false;
                }

                App.Logger?.Debug("Network services disconnected for offline mode");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Error disconnecting network services");
            }
        }

        private void ChkDualMon_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = ChkDualMon.IsChecked ?? true;
            App.Settings.Current.DualMonitorEnabled = isEnabled;

            // Refresh all services if engine is running
            if (_isRunning)
            {
                // Refresh overlays (pink filter, spiral, brain drain) - restart to add/remove monitor windows
                App.Overlay.RefreshForDualMonitorChange();

                // Bouncing text needs restart
                App.BouncingText.Stop();
                if (App.Settings.Current.BouncingTextEnabled)
                {
                    App.BouncingText.Start();
                }

                App.Logger?.Information("Dual monitor toggled: {Enabled} - services refreshed", isEnabled);
            }

            App.Settings.Save();
        }

        private void ChkWinStart_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = ChkWinStart.IsChecked ?? false;
            var isHidden = ChkStartHidden.IsChecked ?? false;

            if (isEnabled && isHidden)
            {
                // Show warning when both startup and hidden are enabled
                var result = MessageBox.Show(this,
                    Loc.Get("msg_startup_hidden_warning"),
                    Loc.Get("title_startup_warning"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    ChkWinStart.IsChecked = false;
                    return;
                }
            }

            // Apply the startup setting
            if (!StartupManager.SetStartupState(isEnabled))
            {
                MessageBox.Show(this,
                    Loc.Get("msg_failed_to_update_startup"),
                    Loc.Get("title_startup_error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                ChkWinStart.IsChecked = StartupManager.IsRegistered();
                App.Settings.Current.RunOnStartup = ChkWinStart.IsChecked ?? false;
                App.Settings.Save();
                return;
            }

            // Persist to settings so any subsequent LoadSettings() (e.g. from saving a
            // preset) doesn't reset the checkbox to a stale value (#150).
            App.Settings.Current.RunOnStartup = isEnabled;
            App.Settings.Save();
        }

        private void ChkStartHidden_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isStartup = ChkWinStart.IsChecked ?? false;
            var isHidden = ChkStartHidden.IsChecked ?? false;

            if (isStartup && isHidden)
            {
                // Show warning when enabling hidden while startup is already enabled
                var result = MessageBox.Show(this,
                    Loc.Get("msg_startup_hidden_warning"),
                    Loc.Get("title_startup_warning"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    ChkStartHidden.IsChecked = false;
                }
            }
        }

        #endregion

        #region Window Events

        /// <summary>
        /// Stops an in-progress preset session so SessionEngine.RestoreSettings() runs BEFORE we
        /// persist settings on exit. Without this, quitting mid-session saves the session's
        /// overridden pools (every user phrase disabled + session phrases injected), so the
        /// subliminal/bouncing-text message sets look wiped on the next launch. StopSession()
        /// is a no-op when no session is running, so this is always safe to call.
        /// </summary>
        private void EnsureSessionRestoredForExit()
        {
            try
            {
                if (_sessionEngine != null && _sessionEngine.IsRunning)
                    _sessionEngine.StopSession(completed: false);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to restore session settings on exit");
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Lockdown mode: block all close attempts
            if (App.Lockdown?.IsActive == true)
            {
                e.Cancel = true;
                return;
            }

            // Closing (real exit OR minimize-to-tray) always ends a Chaos run so it
            // can't keep spawning bubbles / pinning the app alive behind the scenes.
            try { App.Chaos?.ForceShutdown(); } catch { }

            // Only allow actual close if exit was explicitly requested
            if (_exitRequested)
            {
                // Kill all audio and effects first - ensures clean exit
                App.KillAllAudio();

                // Sync conditioning time to server before exit
                try
                {
                    _ = SyncConditioningTimeToServerAsync();
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Failed to sync conditioning time on exit");
                }

                // Restore any active session's settings before persisting (else the overridden
                // pools get saved and the message sets look wiped next launch).
                EnsureSessionRestoredForExit();

                // Actually closing - clean up
                SaveSettings();

                // Stop ALL timers to prevent post-close dispatcher crashes
                _schedulerTimer?.Stop();
                _rampTimer?.Stop();
                _packPreviewTimer?.Stop();
                _remoteNotificationTimer?.Stop();
                _remoteSessionInfoTimer?.Stop();
                _bannerRotationTimer?.Stop();
                _marqueeRefreshTimer?.Stop();
                _statPillUpdateTimer?.Stop();
                _conditioningTimeTimer?.Stop();
                _conditioningTimeSyncTimer?.Stop();

                // Unsubscribe service events to allow GC of this window
                UnsubscribeWebcamDebug();
                if (_onPillStateChanged != null && App.Webcam != null)
                {
                    App.Webcam.OnTrackingStateChanged -= _onPillStateChanged;
                    _onPillStateChanged = null;
                }
                if (_onRapidBlinkRecal != null && App.Webcam != null)
                {
                    App.Webcam.OnBlink -= _onRapidBlinkRecal;
                    _onRapidBlinkRecal = null;
                }
                if (App.Progression != null)
                {
                    App.Progression.XPChanged -= OnXPChanged;
                    App.Progression.LevelUp -= OnLevelUp;
                }
                if (App.Companion != null)
                {
                    App.Companion.XPAwarded -= OnCompanionXPAwarded;
                    App.Companion.CompanionLevelUp -= OnCompanionLevelUp;
                    App.Companion.XPDrained -= OnCompanionXPDrained;
                    App.Companion.CompanionSwitched -= OnCompanionSwitched;
                }
                if (App.ProfileSync != null)
                {
                    App.ProfileSync.ProfileLoaded -= OnProfileLoaded;
                    App.ProfileSync.SyncHealthChanged -= OnSyncHealthChanged;
                }
                if (App.Achievements != null)
                {
                    App.Achievements.AchievementUnlocked -= OnAchievementUnlockedInMainWindow;
                }
                if (App.Quests != null)
                {
                    App.Quests.QuestCompleted -= OnQuestCompleted;
                    App.Quests.QuestProgressChanged -= OnQuestProgressChanged;
                }
                if (App.SkillTree != null)
                {
                    App.SkillTree.PinkRushStarted -= OnPinkRushStarted;
                    App.SkillTree.PinkRushEnded -= OnPinkRushEnded;
                }
                if (App.Roadmap != null)
                {
                    App.Roadmap.StepCompleted -= OnRoadmapStepCompleted;
                    App.Roadmap.TrackUnlocked -= OnRoadmapTrackUnlocked;
                }
                if (App.EnhancementLibrary != null)
                {
                    App.EnhancementLibrary.LibraryChanged -= OnDeeperLibraryChanged;
                }

                _keyboardHook?.Dispose();
                _trayIcon?.Dispose();
                _browser?.Dispose();
                _avatarTubeWindow?.Close();

                // Close any quiz windows (topmost/fullscreen, would keep app alive)
                try
                {
                    foreach (var quiz in Application.Current.Windows.OfType<PopQuizWindow>().ToList())
                        quiz.Close();
                    foreach (var quiz in Application.Current.Windows.OfType<QuizWindow>().ToList())
                        quiz.Close();
                }
                catch { }

                // Close any open Deeper editors / players / overlays. WebView2,
                // LibVLCSharp media players, and the on-rails tutorial overlay
                // each pin the dispatcher (native handles + animations + event
                // subscriptions), so the owner-cascade alone isn't enough to
                // let the process exit. Force-close them here.
                try
                {
                    foreach (var w in Application.Current.Windows
                                          .OfType<Views.Deeper.DeeperEditorWindow>().ToList())
                        w.ForceClose();
                    foreach (var w in Application.Current.Windows
                                          .OfType<Views.Deeper.EnhancementPlayerWindow>().ToList())
                        w.Close();
                    foreach (var w in Application.Current.Windows
                                          .OfType<TutorialOverlay>().ToList())
                        w.Close();
                }
                catch { }

                // Stop and dispose session engine (closes corner GIF window)
                try
                {
                    _sessionEngine?.Dispose();
                }
                catch { }

                // Explicitly dispose overlay services (their windows prevent WPF shutdown)
                try
                {
                    App.ScreenOcr?.Dispose();
                    App.KeywordTriggers?.Dispose();
                    App.KeywordHighlight?.Dispose();
                    App.Overlay?.Dispose();
                }
                catch { }

                // Stop the webcam pipeline immediately on close. Critical: the
                // gaze debug cursor is an unowned visible window that keeps WPF
                // alive past MainWindow close — if we don't tear this down here,
                // App.OnExit never runs, the capture loop keeps holding the
                // camera handle, and the cursor keeps moving with the app
                // "closed". Order matters: stop dependents (cursor + focus
                // gaze) before stopping Webcam, then kill the camera handle.
                try
                {
                    App.GazeFocus?.Dispose();
                    App.GazeCursor?.Dispose();
                    App.Webcam?.Dispose();
                }
                catch { }
            }
            else
            {
                // Always minimize to tray instead of closing
                e.Cancel = true;

                // Close any quiz windows (topmost/fullscreen, would stay visible while app is in tray)
                try
                {
                    foreach (var quiz in Application.Current.Windows.OfType<PopQuizWindow>().ToList())
                        quiz.Close();
                    foreach (var quiz in Application.Current.Windows.OfType<QuizWindow>().ToList())
                        quiz.Close();
                }
                catch { }

                _trayIcon?.MinimizeToTray();
                HideAvatarTube();

                // Stop bouncing text when minimizing to tray (user expects app to be "closed")
                App.BouncingText?.Stop();
            }

            // Make sure the Blink Trainer demo timer + live subscription are
            // released. Idempotent; also called from the tab hide-all path,
            // so this is just a safety net for the close-window-while-tab-
            // visible case.
            try { StopBlinkTrainerDemoLoop(); } catch { }
            try { UnsubscribeBlinkTrainerLiveBlink(); } catch { }

            base.OnClosing(e);
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);

            // Handle restoring from tray or maximizing
            if (WindowState == WindowState.Normal)
            {
                ShowAvatarTube();

                // Re-attach avatar if it was attached before maximizing
                if (_avatarWasAttachedBeforeMaximize && _avatarTubeWindow != null && _avatarTubeWindow.IsDetached)
                {
                    _avatarTubeWindow.Attach();
                    _avatarWasAttachedBeforeMaximize = false;
                }

                // Restore autonomy and avatar mute state if we paused them on minimize
                if (_autonomyWasPausedOnMinimize && _wasAutonomyRunningBeforeMinimize)
                {
                    App.Autonomy?.Start();
                    _autonomyWasPausedOnMinimize = false;
                    _wasAutonomyRunningBeforeMinimize = false;
                    App.Logger?.Debug("MainWindow: Restored autonomy mode after restore from minimize");
                }

                if (_avatarWasMutedOnMinimize && _wasAvatarUnmutedBeforeMinimize)
                {
                    _avatarTubeWindow?.SetMuteAvatar(false);
                    _avatarWasMutedOnMinimize = false;
                    _wasAvatarUnmutedBeforeMinimize = false;
                    App.Logger?.Debug("MainWindow: Restored avatar unmuted state after restore from minimize");
                }

                // Update maximize button icon
                BtnMaximize.Content = "☐";
            }
            else if (WindowState == WindowState.Maximized)
            {
                // Detach avatar when maximizing (it would be in wrong position otherwise)
                if (_avatarTubeWindow != null && !_avatarTubeWindow.IsDetached)
                {
                    _avatarWasAttachedBeforeMaximize = true;
                    _avatarTubeWindow.Detach();
                }

                ShowAvatarTube();

                // Update maximize button icon
                BtnMaximize.Content = "❐";
            }
            else if (WindowState == WindowState.Minimized)
            {
                // Auto-pause autonomy and mute avatar when minimized with attached avatar
                // (no point running effects when user can't see them)
                if (_avatarTubeWindow != null && !_avatarTubeWindow.IsDetached)
                {
                    // Pause autonomy if it's running
                    if (App.Autonomy?.IsEnabled == true)
                    {
                        _wasAutonomyRunningBeforeMinimize = true;
                        _autonomyWasPausedOnMinimize = true;
                        App.Autonomy.Stop();
                        App.Logger?.Debug("MainWindow: Auto-paused autonomy mode on minimize (attached avatar)");
                    }

                    // Mute avatar if it's not already muted
                    if (_avatarTubeWindow.IsMuted == false)
                    {
                        _wasAvatarUnmutedBeforeMinimize = true;
                        _avatarWasMutedOnMinimize = true;
                        _avatarTubeWindow.SetMuteAvatar(true);
                        App.Logger?.Debug("MainWindow: Auto-muted avatar on minimize (attached avatar)");
                    }
                }
            }
        }

        #endregion
    }

    /// <summary>Thin IWin32Window wrapper so WinForms dialogs get a proper owner handle.</summary>
    internal sealed class Win32WindowWrapper : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle { get; }
        public Win32WindowWrapper(IntPtr handle) => Handle = handle;
    }

    /// <summary>DTO bound to the top-bar mod-switcher ComboBox.</summary>
    public sealed class ModSelectorItem
    {
        public string Id { get; }
        public string Name { get; }
        public Brush AccentBrush { get; }

        public ModSelectorItem(string id, string name, Brush accentBrush)
        {
            Id = id;
            Name = name;
            AccentBrush = accentBrush;
        }
    }
}
