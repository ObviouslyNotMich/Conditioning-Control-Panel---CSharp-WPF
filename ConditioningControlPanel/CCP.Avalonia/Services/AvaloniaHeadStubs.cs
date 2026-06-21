using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Chaos;
using ConditioningControlPanel.Avalonia.Services.Video;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Services;

/// <summary>Stub Discord auth provider for the Avalonia head.</summary>
public sealed class AvaloniaDiscordProvider : IAuthProvider
{
    public string ProviderName => "discord";
    public bool IsLoggedIn => !string.IsNullOrEmpty(UnifiedUserId);
    public bool HasPremiumAccess => false;
    public string? UnifiedUserId { get; set; }
    public string? DisplayName { get; set; }

    public Task StartOAuthFlowAsync() => Task.CompletedTask;
    public string? GetAccessToken() => null;
    public void Logout() => UnifiedUserId = null;
}

/// <summary>Stub Patreon auth provider for the Avalonia head.</summary>
public sealed class AvaloniaPatreonProvider : IAuthProvider
{
    public string ProviderName => "patreon";
    public bool IsLoggedIn => !string.IsNullOrEmpty(UnifiedUserId);
    public bool HasPremiumAccess => false;
    public string? UnifiedUserId { get; set; }
    public string? DisplayName { get; set; }

    public Task StartOAuthFlowAsync() => Task.CompletedTask;
    public string? GetAccessToken() => null;
    public void Logout() => UnifiedUserId = null;
}

/// <summary>Stub SubscribeStar auth provider for the Avalonia head.</summary>
public sealed class AvaloniaSubscribeStarProvider : IAuthProvider
{
    public string ProviderName => "substar";
    public bool IsLoggedIn => !string.IsNullOrEmpty(UnifiedUserId);
    public bool HasPremiumAccess => false;
    public string? UnifiedUserId { get; set; }
    public string? DisplayName { get; set; }

    public Task StartOAuthFlowAsync() => Task.CompletedTask;
    public string? GetAccessToken() => null;
    public void Logout() => UnifiedUserId = null;
}

/// <summary>In-memory unified user ID store for the Avalonia head.</summary>
public sealed class AvaloniaUnifiedUserService : IUnifiedUserService
{
    public string? UnifiedUserId { get; set; }
}

/// <summary>
/// Avalonia Chaos engine service. Owns the run lifecycle: countdown, spawn loop, scoring,
/// combo/heat, boon draft between waves, and results. It wires into the ported overlay windows
/// and the cross-platform <see cref="IBubbleService"/> chaos hooks.
/// </summary>
public sealed class AvaloniaChaosService : IChaosService
{
    private readonly IBubbleService _bubbles;
    private readonly ISettingsService _settings;
    private readonly IAppLogger? _logger;
    private readonly IScheduler? _scheduler;
    private readonly IUiDispatcher? _dispatcher;
    private readonly Random _rng = new();

    private bool _active;
    private bool _spawning;
    private bool _paused;
    private bool _manualPaused;
    private bool _ending;
    private ChaosRunState? _state;
    private ChaosOverlayWindow? _overlay;
    private ChaosHudWindow? _hud;

    private IDisposable? _runTimer;
    private IDisposable? _spawnTimer;
    private int _chromeRaiseTick;
    private int _waveIndex;
    private int _waveCount;

    public AvaloniaChaosService(IBubbleService bubbles, ISettingsService settings, IAppLogger? logger = null)
    {
        _bubbles = bubbles;
        _settings = settings;
        _logger = logger;
        _scheduler = App.Services?.GetService<IScheduler>();
        _dispatcher = App.Services?.GetService<IUiDispatcher>();
        AvaloniaChaosCatalogs.EnsureInitialized();
    }

    public bool IsRunning => _active;
    public bool IsManuallyPaused => _manualPaused;

    public void ShowLoadoutSidebar() { }
    public void CloseLoadoutSidebar() { }
    public void NotifyLoadoutChanged() { }

    public void StartRun(object cfg)
    {
        if (_active) return;

        try
        {
            var config = cfg as ChaosRunConfig ?? ChaosRunConfig.FromSettings();
            AvaloniaChaosMode.ActiveMode = config.PlayMode;

            _bubbles.PauseAndClear();
            _state = new ChaosRunState()
            {
                Config = config,
                RunDurationSec = config.RunDurationSec,
                ElapsedSec = 0,
                WaveIndex = 1,
                ActIndex = 1,
                Shields = Math.Max(0, config.StartingShields),
                Focus = Math.Clamp(config.StartingFocus, 0, 100),
                FocusMax = 100,
                Combo = 0,
                ComboMult = 1.0,
                Heat = 0,
                HeatMult = 1.0,
                BoonMult = 1.0,
                DifficultyMult = config.DifficultyMult,
                Score = 0,
                Defused = 0,
                Detonated = 0,
                EffectsFired = 0,
            };
            _waveCount = Math.Max(1, config.WaveCount);
            _waveIndex = 1;
            _active = true;
            _spawning = false;
            _paused = false;
            _manualPaused = false;
            _ending = false;
            _chromeRaiseTick = 0;

            RunOnUi(() =>
            {
                try
                {
                    _hud = new ChaosHudWindow(_state, this);
                    _hud.Show();

                    _overlay = new ChaosOverlayWindow();
                    _overlay.OnRunAgain = () =>
                    {
                        var previous = _state?.Config;
                        RequestStop();
                        if (previous != null) StartRun(previous);
                    };
                    _overlay.OnDismissed = OnOverlayClosed;
                    _overlay.Show();
                    _overlay.ShowCountdown(BeginRun);

                    ChaosEffectBannerOverlay.EnsureCreated();
                    ChaosFieldFxOverlay.EnsureCreated();
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, "AvaloniaChaosService StartRun UI init failed");
                    CleanupAfterRun();
                }
            });

            _logger?.Information("AvaloniaChaosService run started ({Difficulty}, {Duration}s, {Waves} waves)",
                config.Difficulty, config.RunDurationSec, _waveCount);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "AvaloniaChaosService StartRun failed");
            CleanupAfterRun();
        }
    }

    public void StartRunFromSidebar() => StartRun(ChaosRunConfig.FromSettings());

    public void ToggleManualPause()
    {
        if (!_spawning || _ending) return;
        _manualPaused = !_manualPaused;
        if (_manualPaused)
        {
            _paused = true;
            _bubbles.SetChaosFrozen(true);
            _bubbles.SetChaosInputLocked(true);
            _state?.PushEvent("⏸ held. the hole waits.");
        }
        else
        {
            _paused = false;
            _bubbles.SetChaosInputLocked(false);
            _bubbles.SetChaosFrozen(false);
            _state?.PushEvent("▶ sinking again");
        }
        RunOnUi(() => _hud?.SetPausedUi(_manualPaused));
    }

    public void RequestStop()
    {
        if (!_active || _ending) return;
        EndRun();
    }

    public void CloseWarrenPhase() => RequestStop();
    public void OpenWarrenAt(string tag) { }
    public void UnequipFromSidebar(string id) { }
    public void UseToyById(string id) { }

    private void BeginRun()
    {
        if (!_active || _state == null) return;

        _bubbles.BeginChaosMode(OnBenignPopped, OnDefused, OnDetonated);
        _spawning = true;
        _state.PushEvent("🐇 the descent begins");

        RunOnUi(() =>
        {
            _hud?.SetClockVisible(true);
            _hud?.SetHeroMode(preRun: false);
            _hud?.SetPreRunExpanded(false);
        });

        StartTimers();
    }

    private void StartTimers()
    {
        StopTimers();
        if (_scheduler != null)
        {
            _runTimer = _scheduler.StartPeriodicTimer(TimeSpan.FromMilliseconds(250), RunTick);
            _spawnTimer = _scheduler.StartPeriodicTimer(TimeSpan.FromMilliseconds(900), SpawnTick);
        }
        else
        {
            // Fallback to Avalonia dispatcher timer if the scheduler seam is missing.
            var rt = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            rt.Tick += (_, _) => RunTick();
            rt.Start();
            _runTimer = new DisposableAction(rt.Stop);

            var st = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            st.Tick += (_, _) => SpawnTick();
            st.Start();
            _spawnTimer = new DisposableAction(st.Stop);
        }
    }

    private void StopTimers()
    {
        _runTimer?.Dispose();
        _spawnTimer?.Dispose();
        _runTimer = null;
        _spawnTimer = null;
    }

    private void RunTick()
    {
        if (!_spawning || _state == null || _paused || _manualPaused || _ending) return;

        if (++_chromeRaiseTick >= 4)
        {
            _chromeRaiseTick = 0;
            KeepChromeTopmost();
            if (AvaloniaChaosApp.Video?.IsPlaying == true)
                RaiseGameLayerAboveVideo();
        }

        double dt = 0.25;
        _state.ElapsedSec += dt;
        _state.Heat = Math.Max(0, _state.Heat - 0.0015);
        UpdateStateText();

        double waveDuration = _state.RunDurationSec / Math.Max(1, _waveCount);
        if (_state.ElapsedSec >= waveDuration * _waveIndex)
        {
            if (_waveIndex < _waveCount && _state.Config.BoonDraftEnabled)
            {
                ShowDraft();
            }
            else if (_waveIndex >= _waveCount)
            {
                EndRun();
            }
            else
            {
                _waveIndex++;
                _state.WaveIndex = _waveIndex;
            }
        }
    }

    private void SpawnTick()
    {
        if (!_spawning || _state == null || _paused || _manualPaused || _ending) return;
        SpawnRandomBubble();
    }

    private void SpawnRandomBubble()
    {
        try
        {
            var pool = _state?.Config.EnabledVariants?.Count > 0
                ? _state.Config.EnabledVariants
                : new List<string> { "flash", "pink", "subliminal" };
            string variant = pool[_rng.Next(pool.Count)];
            bool live = _rng.NextDouble() < 0.45;

            var motion = (_state?.Config.MotionMode ?? "Mixed") switch
            {
                "FloatUp" => ChaosMotion.FloatUp,
                "RainDown" => ChaosMotion.RainDown,
                "RoamBounce" => ChaosMotion.RoamBounce,
                _ => _rng.Next(3) switch { 0 => ChaosMotion.FloatUp, 1 => ChaosMotion.RainDown, _ => ChaosMotion.RoamBounce },
            };

            var spec = new ChaosBubbleSpec
            {
                VariantId = variant,
                PayloadKind = variant,
                SizePx = 80 + _rng.Next(80),
                IsLive = live,
                FuseMs = live ? (int)(4000 + _rng.NextDouble() * 4000) : 0,
                Motion = motion,
                SpeedMult = 1.0 + _rng.NextDouble(),
            };
            _bubbles.SpawnChaosBubble(spec);
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "AvaloniaChaosService spawn failed");
        }
    }

    private void OnBenignPopped(ChaosBubbleSpec spec)
    {
        if (_state == null) return;
        double basePay = 100 * (_state.DifficultyMult) * (1 + _state.Heat);
        double pay = basePay * _state.ComboMult * _state.BoonMult;
        _state.Score += pay;
        _state.Combo++;
        _state.EffectsFired++;
        _state.Focus = Math.Min(_state.FocusMax, _state.Focus + 10);
        _state.Heat = Math.Min(1.0, _state.Heat + 0.02);
        UpdateStateText();
    }

    private void OnDefused(ChaosBubbleSpec spec, double fuseSec, bool viaChannel)
    {
        if (_state == null) return;
        double basePay = 250 * _state.DifficultyMult * (1 + _state.Heat);
        double pay = basePay * _state.ComboMult * _state.BoonMult;
        _state.Score += pay;
        _state.Combo++;
        _state.Defused++;
        _state.Focus = Math.Min(_state.FocusMax, _state.Focus + 15);
        _state.Heat = Math.Min(1.0, _state.Heat + 0.03);
        UpdateStateText();
    }

    private void OnDetonated(ChaosBubbleSpec spec)
    {
        if (_state == null) return;
        _state.Detonated++;
        _state.Combo = 0;
        _state.ComboMult = 1.0;
        _state.Heat = Math.Max(0, _state.Heat - 0.15);
        if (_state.Shields > 0) _state.Shields--;
        UpdateStateText();
    }

    private void UpdateStateText()
    {
        if (_state == null) return;
        _state.BestCombo = Math.Max(_state.BestCombo, _state.Combo);
        _state.ComboMult = 1.0 + Math.Min(2.0, _state.Combo * 0.02);
        _state.HeatMult = 1.0 + _state.Heat;
        _state.TotalMultText = $"x{_state.ComboMult * _state.BoonMult * _state.HeatMult:0.0}";
        _state.ScoreText = ((int)_state.Score).ToString("N0");
        _state.ShieldText = $"{_state.Shields} ♥";
        _state.FocusText = $"{(int)_state.Focus} / {(int)_state.FocusMax}";

        var remaining = Math.Max(0, _state.RunDurationSec - _state.ElapsedSec);
        _state.ClockText = $"{(int)remaining / 60}:{(int)remaining % 60:00}";
        _state.RunTimeText = $"{(int)_state.ElapsedSec / 60}:{(int)_state.ElapsedSec % 60:00}";
        _state.ActWaveText = $"I · {_waveIndex}";
        _state.RunProgress = _state.ElapsedSec / _state.RunDurationSec;
    }

    private void ShowDraft()
    {
        if (_overlay == null || _state == null || _ending) return;
        _paused = true;
        _bubbles.SetChaosFrozen(true);
        _bubbles.SetChaosInputLocked(true);

        var options = PickDraftOptions(_state.Config.AllowCurses);
        RunOnUi(() => _overlay?.ShowBoonDraft(_waveIndex, options, OnBoonPicked, autoResumeSec: 12));
    }

    private List<ChaosBoon> PickDraftOptions(bool allowCurses)
    {
        var pool = ChaosBoonPool.All.ToList();
        if (!allowCurses) pool = pool.Where(b => !b.IsCurse).ToList();
        if (pool.Count == 0) pool = ChaosBoonPool.All.ToList();
        var picked = new List<ChaosBoon>();
        for (int i = 0; i < 3 && pool.Count > 0; i++)
        {
            int idx = _rng.Next(pool.Count);
            picked.Add(pool[idx]);
            pool.RemoveAt(idx);
        }
        return picked;
    }

    private void OnBoonPicked(ChaosBoon? boon)
    {
        if (_state == null || !_active) return;
        if (boon != null)
        {
            _state.RunPickTiles.Add(new ChaosSidebarBoon { Id = boon.Id, Name = boon.Name, Glyph = boon.IsCurse ? "☠" : "◈" });
            if (boon.IsCurse)
            {
                _state.BoonMult += 0.05;
                _state.PushEvent($"accepted {boon.Name}");
            }
            else
            {
                _state.BoonMult += 0.10;
                _state.PushEvent($"chose {boon.Name}");
            }
        }
        _waveIndex++;
        _state.WaveIndex = _waveIndex;
        _paused = false;
        _bubbles.SetChaosInputLocked(false);
        _bubbles.SetChaosFrozen(false);
        UpdateStateText();
    }

    private void EndRun()
    {
        if (!_active || _ending) return;
        _ending = true;
        _spawning = false;
        StopTimers();
        _bubbles.EndChaosMode();

        var state = _state;
        if (state != null)
        {
            double baseXp = Math.Sqrt(Math.Max(0, state.Score)) * 1.5 + state.RunDurationSec / 60.0 * 35.0 * state.DifficultyMult;
            double skillMult = 1.0;
            double finalXp = baseXp * skillMult;
            int sparks = (int)Math.Round(finalXp);
            long previousBest = (long)ChaosMeta.State.BestScore;
            ChaosMeta.State.Sparks += Math.Max(0, sparks);
            ChaosMeta.State.RunsCompleted++;
            ChaosMeta.State.BestScore = Math.Max(ChaosMeta.State.BestScore, state.Score);
            ChaosMeta.State.BestCombo = Math.Max(ChaosMeta.State.BestCombo, state.BestCombo);
            ChaosMeta.State.TotalDefused += state.Defused;
            ChaosMeta.State.TotalRunSeconds += state.ElapsedSec;
            ChaosMeta.Save();

            RunOnUi(() =>
            {
                _hud?.Close();
                _hud = null;
                _overlay?.ShowResults(state, baseXp, skillMult, finalXp, previousBest, sparks);
            });
        }

        _logger?.Information("AvaloniaChaosService run ended");
    }

    private void CleanupAfterRun()
    {
        _ending = false;
        _active = false;
        _spawning = false;
        _paused = false;
        _manualPaused = false;
        StopTimers();
        try { _bubbles.EndChaosMode(); } catch { }
        RunOnUi(() =>
        {
            try { _hud?.Close(); } catch { }
            _hud = null;
            try { _overlay?.Close(); } catch { }
            _overlay = null;
        });
        _state = null;
        AvaloniaChaosMode.ActiveMode = ChaosPlayMode.Story;
    }

    private void OnOverlayClosed()
    {
        CleanupAfterRun();
        AvaloniaChaosApp.Avatar?.SetChaosRunActive(false);
    }

    private void KeepChromeTopmost()
    {
        if (!_spawning) return;
        RunOnUi(() =>
        {
            try { _hud?.RaiseToTopmost(); } catch { }
            try { ChaosBoonBarOverlay.RaiseActive(); } catch { }
            try { ChaosEffectBannerOverlay.RaiseActive(); } catch { }
        });
    }

    private void RaiseGameLayerAboveVideo()
    {
        if (!_spawning) return;
        RunOnUi(() =>
        {
            try { ChaosFieldFxOverlay.RaiseActive(); } catch { }
            try { ChaosPopText.RaiseActive(); } catch { }
            try { ChaosEffectBannerOverlay.RaiseActive(); } catch { }
            try { ChaosAnnouncerOverlay.RaiseActive(); } catch { }
            try { _hud?.RaiseToTopmost(); } catch { }
        });
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher != null)
        {
            _dispatcher.Post(action);
            return;
        }
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    private sealed class DisposableAction : IDisposable
    {
        private readonly Action _action;
        public DisposableAction(Action action) => _action = action;
        public void Dispose() => _action();
    }
}

/// <summary>Avalonia avatar-window service. Lazily creates the avatar tube window
/// and exposes chat/hotkey integration.</summary>
public sealed class AvaloniaAvatarWindowService : IAvatarWindowService
{
    private readonly global::ConditioningControlPanel.IAppLogger? _logger;
    private readonly Window? _parentWindow;
    private AvatarTube.AvatarTubeWindow? _window;
    private bool _isMuted;
    private bool _chaosRunActive;

    public AvaloniaAvatarWindowService()
    {
        _logger = global::ConditioningControlPanel.Avalonia.App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>();

        if (global::Avalonia.Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            _parentWindow = desktop.MainWindow;
        }
    }

    public bool IsMuted => _isMuted;

    public void SetMuteAvatar(bool muted)
    {
        _isMuted = muted;
        if (_window != null)
        {
            _window.SetMuted(muted);
        }
    }

    public void SetChaosRunActive(bool active)
    {
        _chaosRunActive = active;
        if (_window != null)
        {
            _window.SetChaosRunActive(active);
        }
    }

    public void OpenChatWindow()
    {
        try
        {
            if (_window == null)
            {
                _window = new AvatarTube.AvatarTubeWindow(_parentWindow);
                _window.Closed += (_, _) => _window = null;
                _window.SetMuted(_isMuted);
                _window.SetChaosRunActive(_chaosRunActive);
            }

            _window.ShowTube();
            _window.OpenChatInput();
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to open avatar chat window");
        }
    }
}

/// <summary>Stub bark/notification service for the Avalonia head.</summary>
public sealed class AvaloniaBarkService : IBarkService
{
    public void NotifyChaosDollhouseFirstOpen() { }
    public void NotifyChaosRevealFlash(string id) { }
    public void NotifyChaosResultsShown(double score, double best, double delta, bool pb,
                                        int defused, int detonated, int bestCombo, string difficulty) { }
    public void NotifyChaosRankUp(string rankName) { }
    public void NotifyChaosGiftGiven() { }
    public void NotifyChaosDraftAutopick() { }
}

/// <summary>Video state for the Avalonia head, backed by the dual-monitor video service.</summary>
public sealed class AvaloniaVideoInfo : IVideoInfo
{
    private readonly AvaloniaDualMonitorVideoService? _videoService;

    public AvaloniaVideoInfo(AvaloniaDualMonitorVideoService? videoService = null)
    {
        _videoService = videoService;
    }

    public bool IsPlaying => _videoService?.IsPlaying ?? false;
}

/// <summary>Exposes the Avalonia desktop main window without coupling Core to Avalonia.</summary>
public sealed class AvaloniaMainWindowService : IMainWindowService
{
    public object? MainWindow =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
}

/// <summary>Stub session-log service for the Avalonia head.</summary>
public sealed class AvaloniaSessionLogService : ISessionLogService
{
    public IReadOnlyList<SessionLog> LoadRecentLogs()
    {
        return new List<SessionLog>
        {
            new()
            {
                SessionName = "Morning Drift",
                SessionIcon = "🌅",
                StartedAt = DateTime.Now.AddDays(-1),
                Duration = TimeSpan.FromMinutes(30),
                Completed = true,
                XPEarned = 400,
                Media = new List<MediaLogEntry>()
            },
            new()
            {
                SessionName = "Gamer Girl",
                SessionIcon = "🎮",
                StartedAt = DateTime.Now.AddDays(-2),
                Duration = TimeSpan.FromMinutes(45),
                Completed = false,
                XPEarned = 0,
                Media = new List<MediaLogEntry>()
            }
        };
    }
}
