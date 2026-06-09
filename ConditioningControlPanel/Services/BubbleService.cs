using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NAudio.Wave;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Bubble popping game - bubbles float up from bottom of screen, user pops them by clicking
/// </summary>
public class BubbleService : IDisposable
{
    private const int MAX_BUBBLES = 3;
    private readonly List<Bubble> _bubbles = new();
    private readonly Random _random = new();
    private DispatcherTimer? _spawnTimer;
    private DispatcherTimer? _animationTimer; // Single shared animation timer for all bubbles
    private bool _isRunning;
    private BitmapImage? _bubbleImage;
    private string _assetsPath = "";
    // Per-screen DPI is now computed on demand via Bubble.GetDpiForScreen()

    public bool IsRunning => _isRunning;
    public bool IsPaused => _isPaused;
    public int ActiveBubbles => _bubbles.Count;

    /// <summary>
    /// Snapshot of currently-poppable bubbles for Focus Gaze hit-testing.
    /// Caller iterates in reverse for topmost-first selection.
    /// </summary>
    internal IReadOnlyList<Bubble> GetGazeTargets()
    {
        // Defensive copy so callers can iterate without worrying about
        // _bubbles mutation from the spawn/animation timers.
        var list = new List<Bubble>(_bubbles.Count);
        foreach (var b in _bubbles)
        {
            if (b.CanGazePop) list.Add(b);
        }
        return list;
    }

    private bool _isPaused;

    // ---- Chaos Mode hooks (set by ChaosModeService via BeginChaosMode) ----
    private Action<EffectBubbleSpec>? _chaosOnBenignPop;
    private Action<EffectBubbleSpec>? _chaosOnDefuse;
    private Action<EffectBubbleSpec>? _chaosOnDetonate;
    private Action<EffectBubbleSpec, bool>? _chaosOnDarterCaught;
    private bool _chaosActive;
    private bool _chaosFrozen;   // pause / freeze power-up: halts all bubble motion + fuses

    public event Action? OnBubblePopped;
    public event Action? OnBubbleMissed;

    public void Start(bool bypassLevelCheck = false, int? frequency = null)
    {
        if (_isRunning) return;

        var settings = App.Settings.Current;

        _isRunning = true;

        _assetsPath = App.UserAssetsPath;

        // Pre-load bubble image
        LoadBubbleImage();

        // Start spawning bubbles based on frequency setting
        var intervalMs = 60000.0 / Math.Max(1, frequency ?? settings.BubblesFrequency); // frequency per minute
        
        _spawnTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(intervalMs)
        };
        _spawnTimer.Tick += (s, e) => SpawnBubble();
        _spawnTimer.Start();

        // Single shared animation timer for all bubbles (32ms = ~30 FPS, sufficient for floating bubbles)
        _animationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(32)
        };
        _animationTimer.Tick += AnimateAllBubbles;
        _animationTimer.Start();

        // Spawn first bubble immediately
        SpawnBubble();

        // Update Discord presence
        App.DiscordRpc?.SetBubbleActivity();

        App.Logger?.Information("BubbleService started - {Freq} bubbles/min", settings.BubblesFrequency);
    }

    /// <summary>Freeze/unfreeze all chaos bubble motion + fuse countdowns (pause / time-freeze power-up).</summary>
    public void SetChaosFrozen(bool frozen) => _chaosFrozen = frozen;

    private void AnimateAllBubbles(object? sender, EventArgs e)
    {
        if (_chaosFrozen) return;   // time is frozen: hold every bubble in place, fuses paused
        if (_bubbles.Count == 0) return;

        // Animate all bubbles in a single pass - iterate by index to avoid allocation
        for (int i = _bubbles.Count - 1; i >= 0; i--)
        {
            if (i < _bubbles.Count)
                _bubbles[i].AnimateFrame();
        }
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;

        _spawnTimer?.Stop();
        _spawnTimer = null;

        _animationTimer?.Stop();
        _animationTimer = null;

        // DispatcherTimer ticks are synchronous on the UI thread and won't
        // fire after Stop(), so no delay is needed here.

        // Pop all remaining bubbles
        PopAllBubbles();

        // Update Discord presence back to idle (unless another activity takes over)
        App.DiscordRpc?.SetIdleActivity();

        App.Logger?.Information("BubbleService stopped");
    }

    public void RefreshFrequency()
    {
        if (!_isRunning || _spawnTimer == null) return;

        _spawnTimer.Stop();

        var intervalMs = 60000.0 / Math.Max(1, App.Settings.Current.BubblesFrequency);
        _spawnTimer.Interval = TimeSpan.FromMilliseconds(intervalMs);

        _spawnTimer.Start();

        App.Logger?.Information("BubbleService frequency updated to {Freq} bubbles/min", App.Settings.Current.BubblesFrequency);
    }

    /// <summary>
    /// Pause bubble spawning and clear all active bubbles (for bubble count minigame)
    /// </summary>
    public void PauseAndClear()
    {
        if (!_isRunning) return;

        _isPaused = true;
        _spawnTimer?.Stop();
        PopAllBubbles();

        App.Logger?.Debug("BubbleService paused and cleared for minigame");
    }

    /// <summary>
    /// Resume bubble spawning after pause
    /// </summary>
    public void Resume()
    {
        if (!_isRunning || !_isPaused) return;

        _isPaused = false;
        _spawnTimer?.Start();

        App.Logger?.Debug("BubbleService resumed");
    }

            private void LoadBubbleImage()
            {
                try
                {
                    var resolved = ModResourceResolver.ResolveImage("bubble.png");
                    if (resolved is BitmapImage bmp)
                    {
                        _bubbleImage = bmp.IsFrozen ? bmp : bmp.Clone();
                        if (!_bubbleImage.IsFrozen) _bubbleImage.Freeze();
                    }
                    else
                    {
                        var resourceUri = new Uri("pack://application:,,,/Resources/bubble.png", UriKind.Absolute);
                        _bubbleImage = new BitmapImage();
                        _bubbleImage.BeginInit();
                        _bubbleImage.UriSource = resourceUri;
                        _bubbleImage.CacheOption = BitmapCacheOption.OnLoad;
                        _bubbleImage.EndInit();
                        _bubbleImage.Freeze();
                    }
                    App.Logger?.Debug("Bubble image loaded");
                }
                catch (Exception ex)
                {
                    App.Logger?.Error("Failed to load bubble image: {Error}", ex.Message);
                }
            }
    private void SpawnBubble()
    {
        if (!_isRunning) return;
        if (_bubbles.Count >= MAX_BUBBLES)
        {
            App.Logger?.Debug("Max bubbles reached, skipping spawn");
            return;
        }

        DispatcherHelper.RunOnUI(() =>
        {
            try
            {
                var settings = App.Settings.Current;
                // Baseline spawn: honor DualMonitorEnabled and let bubbles
                // spawn on all monitors. Gaze-pop interaction on off-cal-
                // screen bubbles is filtered by GazeFocusService.FindBestTarget;
                // mouse-click still works everywhere.
                var screens = settings.DualMonitorEnabled
                    ? App.GetAllScreensCached()
                    : new[] { System.Windows.Forms.Screen.PrimaryScreen! };

                var screen = screens[_random.Next(screens.Length)];
                // Outside sessions, bubbles are always clickable (no UI toggle exists for this setting)
                var isClickable = App.IsSessionRunning ? settings.BubblesClickable : true;
                var bubble = new Bubble(screen, _bubbleImage, _random, OnPop, OnMiss, OnDestroy, isClickable);
                _bubbles.Add(bubble);
                
                App.Logger?.Debug("Spawned bubble, total: {Count}", _bubbles.Count);
            }
            catch (Exception ex)
            {
                App.Logger?.Error("Failed to spawn bubble: {Error}", ex.Message);
            }
        });
    }

    /// <summary>
    /// Spawn a single bubble immediately (for keyword triggers).
    /// Works even when the service isn't continuously running.
    /// </summary>
    public void SpawnOnce()
    {
        DispatcherHelper.RunOnUI(() =>
        {
            try
            {
                if (_bubbleImage == null)
                    LoadBubbleImage();

                // Ensure animation timer is running to animate the spawned bubble
                if (_animationTimer == null || !_animationTimer.IsEnabled)
                {
                    _animationTimer = new DispatcherTimer(DispatcherPriority.Render)
                    {
                        Interval = TimeSpan.FromMilliseconds(32)
                    };
                    _animationTimer.Tick += AnimateAllBubbles;
                    _animationTimer.Start();
                }

                var settings = App.Settings.Current;
                // Baseline spawn: keyword-triggered bubbles follow the same
                // DualMonitorEnabled honoring as the running spawn loop. The
                // gaze-read backstop in GazeFocusService.FindBestTarget keeps
                // gaze-pop strictly on the calibrated screen.
                var screens = settings.DualMonitorEnabled
                    ? App.GetAllScreensCached()
                    : new[] { System.Windows.Forms.Screen.PrimaryScreen! };

                var screen = screens[_random.Next(screens.Length)];
                var isClickable = App.IsSessionRunning ? settings.BubblesClickable : true;
                var bubble = new Bubble(screen, _bubbleImage, _random, OnPop, OnMiss, OnDestroy, isClickable);
                _bubbles.Add(bubble);

                App.Logger?.Debug("SpawnOnce: spawned trigger bubble, total: {Count}", _bubbles.Count);
            }
            catch (Exception ex)
            {
                App.Logger?.Error("SpawnOnce: Failed to spawn bubble: {Error}", ex.Message);
            }
        });
    }

    private void OnPop(Bubble bubble)
    {
        // Roll for lucky bubble (5% chance for 10x XP if skill unlocked)
        var multiplier = App.SkillTree?.RollLuckyBubble() ?? 1;
        var isLucky = multiplier > 1;

        // Tell bubble whether it's lucky so it can show the right visual effects
        var hasSparkleBoost = (App.SkillTree?.GetSparkleBoostTier() ?? 0) > 0 && (App.Settings?.Current?.FlashGlowEnabled ?? true);
        bubble.SetLucky(isLucky, hasSparkleBoost);

        // Play appropriate sound
        PlayPopSound(isLucky);

        // Don't remove here - let the pop animation play, removal happens in OnDestroy
        OnBubblePopped?.Invoke();

        App.Progression?.AddXP(5 * multiplier, XPSource.Bubble);

        // Track for achievement
        App.Achievements?.TrackBubblePopped();

        // Haptic feedback with combo system
        _ = App.Haptics?.BubblePopAsync();
    }

    private void OnMiss(Bubble bubble)
    {
        // Bubble floated off screen - remove immediately (no animation needed)
        _bubbles.Remove(bubble);
        OnBubbleMissed?.Invoke();
        StopAnimationTimerIfIdle();
    }

    private void OnDestroy(Bubble bubble)
    {
        // Called when bubble is fully destroyed (after pop animation completes)
        _bubbles.Remove(bubble);
        StopAnimationTimerIfIdle();
    }

    /// <summary>
    /// Stop the animation timer if there are no bubbles left and the service isn't running
    /// (cleans up timers started by SpawnOnce when the service isn't actively running)
    /// </summary>
    private void StopAnimationTimerIfIdle()
    {
        if (!_isRunning && !_chaosActive && _bubbles.Count == 0 && _animationTimer != null)
        {
            _animationTimer.Stop();
            _animationTimer = null;
        }
    }

    // ======================= Chaos Mode API =======================
    // Chaos Mode reuses this service's bubble rendering (real bubble.png, shared
    // 30fps timer, DPI, pooled pop sounds, click-through windows) but spawns
    // *effect* bubbles carrying payloads with a fuse/defuse mechanic. The ambient
    // pop game above is untouched — these only run while a chaos run is active.

    /// <summary>Enter chaos mode: install effect callbacks + ensure the shared animation timer runs.</summary>
    public void BeginChaosMode(Action<EffectBubbleSpec> onBenignPop, Action<EffectBubbleSpec> onDefuse, Action<EffectBubbleSpec> onDetonate,
                               Action<EffectBubbleSpec, bool>? onDarterCaught = null)
    {
        _chaosOnBenignPop = onBenignPop;
        _chaosOnDefuse = onDefuse;
        _chaosOnDetonate = onDetonate;
        _chaosOnDarterCaught = onDarterCaught;
        _chaosActive = true;
        DispatcherHelper.RunOnUI(() =>
        {
            if (_bubbleImage == null) LoadBubbleImage();
            EnsureAnimationTimer();
        });
    }

    /// <summary>Spawn one configured effect bubble (cadence owned by ChaosModeService).</summary>
    public void SpawnChaosBubble(EffectBubbleSpec spec)
    {
        if (!_chaosActive) return;
        DispatcherHelper.RunOnUI(() =>
        {
            try
            {
                if (_bubbleImage == null) LoadBubbleImage();
                EnsureAnimationTimer();

                var settings = App.Settings.Current;
                var screens = settings.DualMonitorEnabled
                    ? App.GetAllScreensCached()
                    : new[] { System.Windows.Forms.Screen.PrimaryScreen! };
                var screen = screens[_random.Next(screens.Length)];

                var bubble = new Bubble(screen, _bubbleImage, _random, null, null, OnDestroy, isClickable: true,
                    spec: spec,
                    onBenignPop: b => { PlayPopSound(false); if (b.Spec != null) _chaosOnBenignPop?.Invoke(b.Spec); },
                    onDefuse:    b => { PlayPopSound(false); if (b.Spec != null) _chaosOnDefuse?.Invoke(b.Spec); },
                    onDetonate:  b => { if (b.Spec != null) _chaosOnDetonate?.Invoke(b.Spec); },
                    onDarterCaught: b => { PlayPopSound(false); if (b.Spec != null) _chaosOnDarterCaught?.Invoke(b.Spec, b.WasQuickCatch); },
                    isChaosFrozen: () => _chaosFrozen);
                _bubbles.Add(bubble);
            }
            catch (Exception ex)
            {
                App.Logger?.Error("SpawnChaosBubble failed: {Error}", ex.Message);
            }
        });
    }

    /// <summary>Leave chaos mode: clear effect bubbles + callbacks.</summary>
    public void EndChaosMode()
    {
        _chaosActive = false;
        _chaosFrozen = false;
        _chaosOnBenignPop = _chaosOnDefuse = _chaosOnDetonate = null;
        _chaosOnDarterCaught = null;
        PopAllBubbles();
        DispatcherHelper.RunOnUI(StopAnimationTimerIfIdle);
    }

    /// <summary>Ensure the shared 30fps animation timer is running (used by chaos + SpawnOnce).</summary>
    private void EnsureAnimationTimer()
    {
        if (_animationTimer == null || !_animationTimer.IsEnabled)
        {
            _animationTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(32)
            };
            _animationTimer.Tick += AnimateAllBubbles;
            _animationTimer.Start();
        }
    }

    private void PlayPopSound(bool isLucky = false)
    {
        try
        {
            // If lucky bubble, play a random chime sound
            if (isLucky)
            {
                var chimeFiles = new[] { "chime1.mp3", "chime2.mp3", "chime3.mp3" };
                var chimePath = ModResourceResolver.ResolveAudioPath(chimeFiles[_random.Next(chimeFiles.Length)]);
                if (File.Exists(chimePath))
                {
                    var masterVolume = App.Settings.Current.MasterVolume / 100f;
                    var bubblesVolume = App.Settings.Current.BubblesVolume / 100f;
                    var volume = (float)Math.Pow(masterVolume * bubblesVolume, 1.5) * 0.35f;
                    PlaySoundAsync(chimePath, volume);
                    App.Logger?.Information("🎉 Lucky Bubble! 20x XP!");
                    return;
                }
            }

            // Normal pop sound
            var popFiles = new[] { "Pop.mp3", "Pop2.mp3", "Pop3.mp3" };
            var chosenPop = popFiles[_random.Next(popFiles.Length)];
            var popPath = ModResourceResolver.ResolveAudioPath("bubbles/" + chosenPop);

            if (File.Exists(popPath))
            {
                var masterVolume = App.Settings.Current.MasterVolume / 100f;
                var bubblesVolume = App.Settings.Current.BubblesVolume / 100f;
                var volume = (float)Math.Pow(masterVolume * bubblesVolume, 1.5);

                PlaySoundAsync(popPath, volume);
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("Failed to play pop sound: {Error}", ex.Message);
        }
    }

    // Performance: Pool of audio devices to avoid creating new ones for each sound
    private static readonly Queue<WaveOutEvent> _audioDevicePool = new();
    private static readonly object _audioPoolLock = new();
    private const int MAX_POOLED_DEVICES = 4;

    private WaveOutEvent GetPooledAudioDevice()
    {
        lock (_audioPoolLock)
        {
            if (_audioDevicePool.Count > 0)
            {
                return _audioDevicePool.Dequeue();
            }
        }
        // Apply user's chosen output device on construction. Pool is drained when the
        // setting changes (see DrainAudioDevicePool) so we never need to reapply on Get.
        var w = new WaveOutEvent();
        App.Audio?.ApplyPreferredDevice(w);
        return w;
    }

    /// <summary>
    /// Disposes all pooled audio devices. Call after the user changes the output device
    /// setting so the next pop-sound playback re-creates devices on the new endpoint
    /// (DeviceNumber can't be changed once Init() has been called).
    /// </summary>
    public static void DrainAudioDevicePool()
    {
        lock (_audioPoolLock)
        {
            while (_audioDevicePool.Count > 0)
            {
                try { _audioDevicePool.Dequeue().Dispose(); } catch { }
            }
        }
    }

    private void ReturnAudioDevice(WaveOutEvent device)
    {
        lock (_audioPoolLock)
        {
            if (_audioDevicePool.Count < MAX_POOLED_DEVICES)
            {
                _audioDevicePool.Enqueue(device);
            }
            else
            {
                device.Dispose();
            }
        }
    }

    private void PlaySoundAsync(string path, float volume)
    {
        Task.Run(() =>
        {
            WaveOutEvent? outputDevice = null;
            AudioFileReader? audioFile = null;
            try
            {
                audioFile = new AudioFileReader(path);
                audioFile.Volume = volume;

                outputDevice = GetPooledAudioDevice();  // Performance: Reuse pooled device
                outputDevice.Init(audioFile);
                outputDevice.Play();

                while (outputDevice.PlaybackState == PlaybackState.Playing)
                {
                    Thread.Sleep(50);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Audio playback failed: {Error}", ex.Message);
            }
            finally
            {
                audioFile?.Dispose();
                if (outputDevice != null)
                {
                    try { outputDevice.Stop(); } catch { }
                    ReturnAudioDevice(outputDevice);  // Performance: Return to pool
                }
            }
        });
    }

    public void PopAllBubbles()
    {
        try
        {
            // Safety check for shutdown scenarios
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
            {
                // Direct cleanup without dispatcher - force destroy
                foreach (var bubble in _bubbles.ToArray())
                {
                    try { bubble.ForceDestroy(); } catch { }
                }
                _bubbles.Clear();
                return;
            }

            // Take a copy of bubbles to close
            var bubblesToClose = _bubbles.ToArray();
            _bubbles.Clear();

            // Close on UI thread - use Invoke for synchronous cleanup during stop
            // Since animation timer is stopped, we need to force destroy (no animation)
            dispatcher.Invoke(() =>
            {
                foreach (var bubble in bubblesToClose)
                {
                    try
                    {
                        bubble.ForceDestroy();
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("Error destroying bubble: {Error}", ex.Message);
                    }
                }
            }, System.Windows.Threading.DispatcherPriority.Send); // High priority to complete quickly
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("PopAllBubbles error during shutdown: {Error}", ex.Message);
            // Force clear the list even if popup failed
            _bubbles.Clear();
        }
    }

    public void Dispose()
    {
        Stop();

        // Drain and dispose pooled audio devices (static pool persists across service restarts)
        lock (_audioPoolLock)
        {
            while (_audioDevicePool.Count > 0)
            {
                try { _audioDevicePool.Dequeue().Dispose(); } catch { }
            }
        }
    }
}

/// <summary>
/// Individual bubble that floats upward and can be popped
/// </summary>
internal class Bubble
{
    private readonly Window _window;
    private readonly Random _random;
    private readonly Action<Bubble>? _onPop;
    private readonly Action<Bubble>? _onMiss;
    private readonly Action<Bubble>? _onDestroy;
    private readonly bool _isClickable;

    private double _posX, _posY;
    private double _startX;
    private double _speed;
    private double _timeAlive;
    private double _wobbleOffset;
    private double _angle;
    private double _scale = 1.0;
    private double _fadeAlpha = 1.0;
    private int _animType;
    private bool _isPopping;
    private bool _isAlive = true;
    private bool _isDestroyed = false;
    private bool _isLucky;
    private double _gazeDwellScale = 1.0; // multiplied into render scale during Focus Gaze dwell

    private readonly Image _bubbleImage;
    private readonly int _size;
    private readonly double _screenTop;
    private readonly Canvas _sparkleCanvas;
    private readonly Grid _grid;
    private List<SparkleParticle>? _sparkles;

    // ---- Chaos effect-bubble extensions (null/inert for ambient bubbles) ----
    private readonly EffectBubbleSpec? _spec;
    private readonly Action<Bubble>? _onBenignPop;
    private readonly Action<Bubble>? _onDefuse;
    private readonly Action<Bubble>? _onDetonate;
    private readonly Action<Bubble>? _onDarterCaught;
    private readonly Func<bool>? _isChaosFrozen;   // true while the field is frozen (pause / time-freeze)
    private double _fuseTotalMs;
    private double _fuseRemainingMs;
    private System.Windows.Shapes.Ellipse? _fuseRing;
    // Mutable (unfrozen) brush reused for the fuse ring so its colour can be tweaked every
    // frame without allocating a new SolidColorBrush per tick (GC pressure at 30fps).
    private SolidColorBrush? _fuseStrokeBrush;
    private double _vx, _vy;                                   // RoamBounce velocity (DIPs/frame)
    private double _screenBottom, _screenLeft, _screenRight;   // motion bounds (DIPs)

    private bool _hasVariantSprite;   // a per-variant sprite replaced the tinted bubble.png

    // ---- darter state (only when _spec.IsDarter) ----
    private readonly bool _isDarter;
    private double _telegraphRemainingMs;
    private double _darterActiveMs;
    private double _darterLifeRemainingMs;
    private System.Windows.Shapes.Ellipse? _telegraphRing;
    private bool _wasQuickCatch;

    /// <summary>The chaos spec this bubble carries (null for ambient pop-game bubbles).</summary>
    public EffectBubbleSpec? Spec => _spec;

    /// <summary>True if this darter was caught within its quick-catch window. Valid after Pop().</summary>
    public bool WasQuickCatch => _wasQuickCatch;

    private struct SparkleParticle
    {
        public double X, Y, VelX, VelY, Alpha, Size;
        public System.Windows.Shapes.Ellipse Shape;
    }

    public bool IsAlive => _isAlive && !_isDestroyed;

    public Bubble(System.Windows.Forms.Screen screen, BitmapImage? image, Random random,
                  Action<Bubble>? onPop, Action<Bubble>? onMiss, Action<Bubble>? onDestroy, bool isClickable = true,
                  EffectBubbleSpec? spec = null,
                  Action<Bubble>? onBenignPop = null, Action<Bubble>? onDefuse = null, Action<Bubble>? onDetonate = null,
                  Action<Bubble>? onDarterCaught = null, Func<bool>? isChaosFrozen = null)
    {
        _random = random;
        _onPop = onPop;
        _onMiss = onMiss;
        _onDestroy = onDestroy;
        _isClickable = isClickable;
        _spec = spec;
        _onBenignPop = onBenignPop;
        _onDefuse = onDefuse;
        _onDetonate = onDetonate;
        _onDarterCaught = onDarterCaught;
        _isChaosFrozen = isChaosFrozen;
        _isDarter = spec?.IsDarter == true;

        // Random properties (size/motion overridden for chaos effect bubbles)
        _size = spec != null ? Math.Max(60, (int)Math.Round(spec.SizePx)) : random.Next(150, 250);
        _speed = 1.0 + random.NextDouble() * 1.0; // 1.0 to 2.0 px/frame
        if (spec != null) // bigger chaos bubbles drift a little slower (more reachable)
            _speed *= Math.Clamp(1.4 - (_size - 150) / 220.0, 0.6, 1.4);
        _animType = random.Next(4);
        _wobbleOffset = random.NextDouble() * 100;
        _angle = random.Next(360);

        // Get DPI scale for this specific screen
        var dpiScale = GetDpiForScreen(screen);

        var area = screen.WorkingArea;
        _screenLeft = area.X / dpiScale;
        _screenRight = (area.X + area.Width) / dpiScale - _size;
        _screenTop = area.Y / dpiScale - _size - 50;
        _screenBottom = (area.Y + area.Height) / dpiScale + 50;

        // Position + initial velocity depend on motion (FloatUp is the ambient default).
        var motion = spec?.Motion ?? ChaosMotion.FloatUp;
        _startX = (area.X + random.Next(50, Math.Max(100, area.Width - _size - 50))) / dpiScale;
        _posX = _startX;
        switch (motion)
        {
            case ChaosMotion.RainDown:
                _posY = area.Y / dpiScale - _size;                 // start just above the top
                break;
            case ChaosMotion.RoamBounce:
                _posY = (area.Y + random.Next(50, Math.Max(100, area.Height - _size - 50))) / dpiScale;
                double ang = random.NextDouble() * Math.PI * 2;
                double roamSpeed = _speed * 1.4;
                _vx = Math.Cos(ang) * roamSpeed;
                _vy = Math.Sin(ang) * roamSpeed;
                break;
            default: // FloatUp
                _posY = (area.Y + area.Height) / dpiScale;          // start at the bottom
                break;
        }

        // Live chaos bubbles arm a fuse.
        if (spec != null && spec.IsLive)
            _fuseTotalMs = _fuseRemainingMs = Math.Max(1, spec.FuseMs);

        // Darters: telegraph + active lifetime, and a faster fixed-magnitude velocity.
        if (_isDarter && spec != null)
        {
            _telegraphRemainingMs = Math.Max(0, spec.TelegraphMs);
            _darterLifeRemainingMs = Math.Max(1, spec.LifetimeMs);
            double mag = Math.Sqrt(_vx * _vx + _vy * _vy);
            if (mag < 0.001) { double a = random.NextDouble() * Math.PI * 2; _vx = Math.Cos(a); _vy = Math.Sin(a); mag = 1; }
            _vx = _vx / mag * spec.DarterSpeed;
            _vy = _vy / mag * spec.DarterSpeed;
        }

        // Create bubble image (hit-testing disabled — the Ellipse behind handles clicks)
        _bubbleImage = new Image
        {
            Width = _size,
            Height = _size,
            Stretch = Stretch.Uniform,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Cursor = _isClickable ? Cursors.Hand : Cursors.Arrow,
            IsHitTestVisible = false
        };
        RenderOptions.SetBitmapScalingMode(_bubbleImage, PerformanceProfile.ScalingMode(PerformanceProfile.CurrentTier));

        // Chaos: a per-variant sprite at Assets/Chaos/bubbles/{variant}.png replaces the
        // tinted bubble.png when present (the tint overlay is then skipped). Falls back to
        // the shared bubble image otherwise.
        var variantSprite = spec != null ? ChaosArt.Resolve("bubbles", spec.VariantId) : null;
        if (variantSprite != null)
        {
            _bubbleImage.Source = variantSprite;
            _hasVariantSprite = true;
        }
        else if (image != null)
        {
            _bubbleImage.Source = image;
        }
        else
        {
            // Fallback - create simple ellipse
            var drawing = new DrawingGroup();
            using (var ctx = drawing.Open())
            {
                var gradientBrush = new RadialGradientBrush(
                    Color.FromArgb(180, 200, 220, 255),
                    Color.FromArgb(80, 255, 255, 255));
                ctx.DrawEllipse(gradientBrush, new Pen(Brushes.White, 2), 
                    new Point(_size / 2, _size / 2), _size / 2 - 5, _size / 2 - 5);
            }
            _bubbleImage.Source = new DrawingImage(drawing);
        }

        // Transform for rotation and scale
        var transformGroup = new TransformGroup();
        transformGroup.Children.Add(new ScaleTransform(1, 1));
        transformGroup.Children.Add(new RotateTransform(0));
        _bubbleImage.RenderTransform = transformGroup;

        // Create invisible hit area ellipse that covers the full bubble
        // This ensures clicks anywhere in the circular bubble area register
        var hitArea = new System.Windows.Shapes.Ellipse
        {
            Width = _size,
            Height = _size,
            Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), // Nearly invisible but captures hits on transparent windows
            IsHitTestVisible = _isClickable,
            Cursor = _isClickable ? Cursors.Hand : Cursors.Arrow
        };

        if (_isClickable)
        {
            hitArea.MouseLeftButtonDown += (s, e) =>
            {
                Pop();
                e.Handled = true;
            };
        }

        // Sparkle particle canvas (overlays the bubble, non-interactive)
        _sparkleCanvas = new Canvas
        {
            Width = _size,
            Height = _size,
            IsHitTestVisible = false
        };

        // Create container grid with hit area behind the bubble image
        _grid = new Grid
        {
            Width = _size,
            Height = _size,
            Background = Brushes.Transparent,
            IsHitTestVisible = _isClickable
        };
        _grid.Children.Add(hitArea);         // Hit area first (behind)
        _grid.Children.Add(_bubbleImage);    // Image on top
        BuildChaosLayers();                  // tint + label + fuse ring (no-op for ambient bubbles)
        _grid.Children.Add(_sparkleCanvas);  // Sparkles on top of everything

        // Grid click as backup (only if clickable)
        if (_isClickable)
        {
            _grid.MouseLeftButtonDown += (s, e) =>
            {
                Pop();
                e.Handled = true;
            };
        }

        // Single window - clickable or click-through based on setting
        _window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            Focusable = false,
            Width = _size + 40,
            Height = _size + 40,
            Left = _posX - 20,
            Top = _posY - 20,
            Content = _grid,
            Cursor = _isClickable ? Cursors.Hand : Cursors.Arrow,
            IsHitTestVisible = _isClickable
        };

        // Window click as final backup (only if clickable)
        if (_isClickable)
        {
            _window.MouseLeftButtonDown += (s, e) => Pop();
        }

        // Show window
        _window.Show();

        // Hide from Alt+Tab
        HideFromAltTab();

        // Note: Animation is now driven by shared timer in BubbleService.AnimateAllBubbles()
    }

    /// <summary>
    /// Called by BubbleService's shared animation timer (~30 FPS)
    /// </summary>
    public void AnimateFrame()
    {
        // Early exit checks - must be first to avoid any work on destroyed bubbles
        if (!_isAlive || _isDestroyed) return;

        if (_isPopping)
        {
            // Pop animation - expand and fade (scaled for 30fps)
            _scale += 0.04;
            _fadeAlpha -= _isLucky ? 0.044 : 0.066; // Lucky pops linger ~50% longer
            _angle += 2;

            // Animate sparkle particles outward
            if (_sparkles != null)
            {
                for (int i = 0; i < _sparkles.Count; i++)
                {
                    var sp = _sparkles[i];
                    sp.X += sp.VelX;
                    sp.Y += sp.VelY;
                    sp.VelY += 0.15; // Slight gravity
                    sp.Alpha -= _isLucky ? 0.04 : 0.06;

                    if (sp.Alpha > 0)
                    {
                        try
                        {
                            Canvas.SetLeft(sp.Shape, sp.X - sp.Size / 2);
                            Canvas.SetTop(sp.Shape, sp.Y - sp.Size / 2);
                            sp.Shape.Opacity = Math.Max(0, sp.Alpha);
                        }
                        catch { }
                    }

                    _sparkles[i] = sp;
                }
            }

            if (_fadeAlpha <= 0)
            {
                Destroy();
                return;
            }
        }
        else if (_isDarter)
        {
            // Darter: flare in place during the telegraph, then dart with a fast edge-bounce.
            _timeAlive += 0.02;
            if (_telegraphRemainingMs > 0)
            {
                _telegraphRemainingMs -= 32;
                if (_telegraphRing != null && _spec != null)
                {
                    double tfrac = Math.Clamp(_telegraphRemainingMs / Math.Max(1, _spec.TelegraphMs), 0, 1);
                    if (_telegraphRing.RenderTransform is ScaleTransform tst)
                    { double rs = 1.0 + 0.30 * tfrac; tst.ScaleX = rs; tst.ScaleY = rs; }
                    _telegraphRing.Opacity = 0.85 * tfrac;
                    if (_telegraphRemainingMs <= 0) _telegraphRing.Visibility = Visibility.Collapsed;
                }
                // hold position while telegraphing
            }
            else
            {
                _darterActiveMs += 32;
                _posX += _vx;
                _posY += _vy;
                if (_posX < _screenLeft) { _posX = _screenLeft; _vx = Math.Abs(_vx); }
                else if (_posX > _screenRight) { _posX = _screenRight; _vx = -Math.Abs(_vx); }
                double topB = _screenTop + _size + 50, botB = _screenBottom - _size - 50;
                if (_posY < topB) { _posY = topB; _vy = Math.Abs(_vy); }
                else if (_posY > botB) { _posY = botB; _vy = -Math.Abs(_vy); }
            }
            _darterLifeRemainingMs -= 32;
            if (_darterLifeRemainingMs <= 0) { Destroy(); return; }   // vanish harmlessly, no combo break
        }
        else
        {
            // Normal travel animation (scaled for 30fps)
            _timeAlive += 0.02;
            var motion = _spec?.Motion ?? ChaosMotion.FloatUp;

            // Horizontal wobble shared by Float/Rain (gives the lively drift)
            double offset = 0;
            switch (_animType)
            {
                case 0: offset = Math.Sin(_timeAlive * 6) * 25;  _angle = (_angle + 0.34) % 360; break;
                case 1: offset = Math.Sin(_timeAlive * 7.5) * 30; _angle = (_angle + 0.14) % 360; break;
                case 2: offset = Math.Cos(_timeAlive * 5.4) * 25; _angle = (_angle - 0.66) % 360; break;
                case 3: offset = Math.Sin(_timeAlive * 3) * 30 + Math.Cos(_timeAlive * 6) * 15; _angle = (_angle + 0.54) % 360; break;
            }

            bool exited = false;
            switch (motion)
            {
                case ChaosMotion.RainDown:
                    _posY += _speed;
                    _posX = _startX + offset;
                    if (_posY > _screenBottom) exited = true;
                    break;
                case ChaosMotion.RoamBounce:
                    _posX += _vx;
                    _posY += _vy;
                    if (_posX < _screenLeft) { _posX = _screenLeft; _vx = Math.Abs(_vx); }
                    else if (_posX > _screenRight) { _posX = _screenRight; _vx = -Math.Abs(_vx); }
                    double topB = _screenTop + _size + 50, botB = _screenBottom - _size - 50;
                    if (_posY < topB) { _posY = topB; _vy = Math.Abs(_vy); }
                    else if (_posY > botB) { _posY = botB; _vy = -Math.Abs(_vy); }
                    break;
                default: // FloatUp
                    _posY -= _speed;
                    _posX = _startX + offset;
                    if (_posY < _screenTop) exited = true;
                    break;
            }

            // Fuse countdown for live chaos bubbles.
            if (_spec != null && _spec.IsLive && _fuseRemainingMs > 0)
            {
                _fuseRemainingMs -= 32;
                double frac = Math.Clamp(_fuseRemainingMs / Math.Max(1, _fuseTotalMs), 0, 1);
                if (_fuseRing?.RenderTransform is ScaleTransform fst)
                {
                    double rs = 0.45 + 0.55 * frac;
                    fst.ScaleX = rs; fst.ScaleY = rs;
                    byte gb = (byte)(70 + 120 * frac);
                    if (_fuseStrokeBrush != null) _fuseStrokeBrush.Color = Color.FromRgb(255, gb, gb);
                }
                if (_fuseRemainingMs <= 0) { Detonate(); return; }
            }

            if (exited)
            {
                if (_spec == null) { _onMiss?.Invoke(this); Destroy(); return; }
                // Chaos: a live bubble that escaped undefused detonates; a benign one just leaves.
                if (_spec.IsLive) { Detonate(); return; }
                Destroy();
                return;
            }
        }

        // Update visuals - wrapped in try-catch to handle disposed windows gracefully
        try
        {
            // Double-check we're still alive after calculations
            if (_isDestroyed || !_isAlive) return;

            // Update scale wobble (scaled for 30fps)
            var wobble = 0.06 * Math.Sin(_timeAlive * 7.5 + _wobbleOffset);
            var currentScale = (_scale + wobble) * _gazeDwellScale;

            if (_bubbleImage.RenderTransform is TransformGroup tg && tg.Children.Count >= 2)
            {
                if (tg.Children[0] is ScaleTransform st)
                {
                    st.ScaleX = currentScale;
                    st.ScaleY = currentScale;
                }
                if (tg.Children[1] is RotateTransform rt)
                {
                    rt.Angle = _angle;
                }
            }

            _window.Opacity = _fadeAlpha;
            _window.Left = _posX - 20;
            _window.Top = _posY - 20;
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("Bubble animate error: {Error}", ex.Message);
            Destroy();
        }
    }

    public void SetLucky(bool isLucky, bool hasSparkleBoost)
    {
        _isLucky = isLucky;

        // Apply golden glow for lucky pops (skip the expensive blur under the Performance tier;
        // cap the radius otherwise so a burst of lucky pops doesn't stack many 50px blurs).
        var perfTier = PerformanceProfile.CurrentTier;
        if (isLucky && PerformanceProfile.AllowGlow(perfTier))
        {
            try
            {
                _window.Effect = new DropShadowEffect
                {
                    Color = System.Windows.Media.Color.FromRgb(0xFF, 0xD7, 0x00),
                    BlurRadius = Math.Min(50, PerformanceProfile.MaxGlowBlurRadius(perfTier)),
                    ShadowDepth = 0,
                    Opacity = 0.8
                };
            }
            catch { }
        }

        // Spawn sparkle particles if sparkle boost is unlocked
        if (hasSparkleBoost || isLucky)
        {
            SpawnSparkles(isLucky);
        }
    }

    private void SpawnSparkles(bool isGold)
    {
        var count = isGold ? 16 : 8;
        var color = isGold
            ? System.Windows.Media.Color.FromRgb(0xFF, 0xD7, 0x00)
            : System.Windows.Media.Color.FromRgb(0xFF, 0x69, 0xB4);
        var minSize = isGold ? 4.0 : 3.0;
        var maxSize = isGold ? 8.0 : 6.0;

        _sparkles = new List<SparkleParticle>(count);
        var centerX = _size / 2.0;
        var centerY = _size / 2.0;

        for (int i = 0; i < count; i++)
        {
            var angle = _random.NextDouble() * Math.PI * 2;
            var speed = 2.0 + _random.NextDouble() * 4.0;
            var size = minSize + _random.NextDouble() * (maxSize - minSize);

            var ellipse = new System.Windows.Shapes.Ellipse
            {
                Width = size,
                Height = size,
                Fill = new SolidColorBrush(color),
                IsHitTestVisible = false
            };

            Canvas.SetLeft(ellipse, centerX - size / 2);
            Canvas.SetTop(ellipse, centerY - size / 2);
            _sparkleCanvas.Children.Add(ellipse);

            _sparkles.Add(new SparkleParticle
            {
                X = centerX,
                Y = centerY,
                VelX = Math.Cos(angle) * speed,
                VelY = Math.Sin(angle) * speed,
                Alpha = 1.0,
                Size = size,
                Shape = ellipse
            });
        }
    }

    public void Pop()
    {
        if (!_isAlive || _isPopping) return;
        if (_isChaosFrozen?.Invoke() == true) return;   // field is frozen (paused): ignore clicks
        _isPopping = true;
        if (_spec != null)
        {
            // Chaos bubble: a live bubble clicked in time is a DEFUSE (reward, no payload);
            // a darter caught is its own reward path; a benign bubble is a treat.
            if (_isDarter)
            {
                _wasQuickCatch = _telegraphRemainingMs <= 0 && _darterActiveMs <= _spec.QuickWindowMs;
                _onDarterCaught?.Invoke(this);
            }
            else if (_spec.IsLive) _onDefuse?.Invoke(this);
            else _onBenignPop?.Invoke(this);
        }
        else
        {
            _onPop?.Invoke(this);
        }
        // Don't call Destroy() here - let AnimateFrame() handle the burst animation.
    }

    /// <summary>Live chaos bubble reached fuse-out / escaped undefused → fire its payload.</summary>
    private void Detonate()
    {
        if (!_isAlive || _isPopping) return;
        _isPopping = true;
        _onDetonate?.Invoke(this);
        // Pop/burst animation + Destroy handled by AnimateFrame().
    }

    /// <summary>Builds the chaos-only visual layers (tint, label, fuse ring). No-op for ambient bubbles.</summary>
    private void BuildChaosLayers()
    {
        if (_spec == null) return;

        // Tint overlay — radial so the bubble's highlight still reads through. Skipped when a
        // per-variant sprite is supplying its own art.
        if (!_hasVariantSprite)
        {
            var t = _spec.Tint;
            var tintBrush = new RadialGradientBrush(
                Color.FromArgb(150, t.R, t.G, t.B),
                Color.FromArgb(90, t.R, t.G, t.B))
            { GradientOrigin = new Point(0.35, 0.3) };
            _grid.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = _size, Height = _size,
                Fill = tintBrush,
                IsHitTestVisible = false,
                Opacity = 0.55
            });
        }

        // Label / emoji
        if (!string.IsNullOrEmpty(_spec.Label))
        {
            _grid.Children.Add(new TextBlock
            {
                Text = _spec.Label,
                Foreground = Brushes.White,
                FontSize = Math.Max(14, _size * 0.30),
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 0, Opacity = 0.8 }
            });
        }

        // Fuse ring (live only) — shrinks + reddens as the fuse runs down.
        if (_spec.IsLive)
        {
            _fuseStrokeBrush = new SolidColorBrush(Color.FromRgb(255, 190, 190));
            _fuseRing = new System.Windows.Shapes.Ellipse
            {
                Width = _size, Height = _size,
                Stroke = _fuseStrokeBrush,
                StrokeThickness = 5,
                IsHitTestVisible = false,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1)
            };
            _grid.Children.Add(_fuseRing);
        }

        // Darter: a soft glow so the fast bounce stays trackable, plus a telegraph flare
        // ring (animated down to lock-on in AnimateFrame). Kept within the window bounds.
        if (_spec.IsDarter)
        {
            var tc = Color.FromRgb(_spec.Tint.R, _spec.Tint.G, _spec.Tint.B);
            _bubbleImage.Effect = new DropShadowEffect { Color = tc, BlurRadius = 26, ShadowDepth = 0, Opacity = 0.9 };
            _telegraphRing = new System.Windows.Shapes.Ellipse
            {
                Width = _size, Height = _size,
                Stroke = new SolidColorBrush(tc),
                StrokeThickness = 4,
                IsHitTestVisible = false,
                Opacity = 0.85,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1.30, 1.30)
            };
            _grid.Children.Add(_telegraphRing);
        }
    }

    /// <summary>
    /// Whether this bubble can currently be popped via Focus Gaze.
    /// False once popping has started or the bubble has been destroyed.
    /// </summary>
    public bool CanGazePop => _isAlive && !_isPopping && !_isDestroyed && _isClickable && !(_isChaosFrozen?.Invoke() ?? false);

    /// <summary>
    /// Bubble window bounds in WPF DIPs (matches the coordinate space of
    /// WebcamTrackingService.OnGazeMove samples). Returns Rect.Empty when
    /// the window is unavailable.
    /// </summary>
    public Rect GetGazeBounds()
    {
        try
        {
            return new Rect(_window.Left, _window.Top, _window.Width, _window.Height);
        }
        catch
        {
            return Rect.Empty;
        }
    }

    /// <summary>
    /// Drives a small inflate effect during Focus Gaze dwell. t01 is the
    /// dwell progress in [0, 1]; the bubble's render scale is multiplied
    /// by 1 + t01 * 0.25.
    /// </summary>
    public void SetGazeDwellProgress(double t01)
    {
        if (_isPopping || _isDestroyed) return;
        var clamped = Math.Max(0.0, Math.Min(1.0, t01));
        _gazeDwellScale = 1.0 + clamped * 0.25;
    }

    /// <summary>
    /// Force destroy the bubble immediately without animation.
    /// Used during cleanup when animation timer is stopped.
    /// </summary>
    public void ForceDestroy()
    {
        Destroy();
    }

    private void Destroy()
    {
        if (_isDestroyed) return;
        _isDestroyed = true;
        _isAlive = false;

        try { _window.Close(); } catch { }

        // Notify service to remove from list (after animation completed)
        try { _onDestroy?.Invoke(this); } catch { }
    }

    #region Win32

    private static double GetDpiForScreen(System.Windows.Forms.Screen screen)
    {
        try
        {
            uint dpiX = 96, dpiY = 96;
            var hMonitor = MonitorFromPoint(new POINT { X = screen.Bounds.X + 1, Y = screen.Bounds.Y + 1 }, 2);

            if (hMonitor != IntPtr.Zero)
            {
                var result = GetDpiForMonitor(hMonitor, 0, out dpiX, out dpiY);
                if (result == 0)
                {
                    return dpiX / 96.0;
                }
            }

            using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
            return g.DpiX / 96.0;
        }
        catch
        {
            return 1.0;
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [System.Runtime.InteropServices.DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    private void HideFromAltTab()
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(_window).Handle;
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            var flags = exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            // Non-clickable bubbles must be truly click-through at the Win32 level;
            // WPF's IsHitTestVisible alone doesn't prevent the window from eating clicks.
            if (!_isClickable)
                flags |= WS_EX_TRANSPARENT;
            SetWindowLong(hwnd, GWL_EXSTYLE, flags);
        }
        catch { }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    #endregion
}