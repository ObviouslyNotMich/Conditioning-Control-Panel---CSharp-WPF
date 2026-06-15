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
