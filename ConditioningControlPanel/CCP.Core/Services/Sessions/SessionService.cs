using System.Diagnostics;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;
using Serilog;

namespace ConditioningControlPanel.Core.Services.Sessions;

/// <summary>
/// Portable session state-machine implementation.
/// Tracks timing, phase transitions, pause count, and XP calculation.
/// UI heads subscribe to events and drive effects accordingly.
/// </summary>
public sealed class SessionService : ISessionService, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IProgressionService _progression;
    private readonly IScheduler _scheduler;
    private readonly Random _random = new();
    private readonly Stopwatch _wallClockStopwatch = new();

    private ConditioningControlPanel.Models.Session? _currentSession;
    private SessionState _state = SessionState.Idle;
    private IDisposable? _tickSubscription;
    private CancellationTokenSource? _cancellationTokenSource;

    private DateTime _startTime;
    private DateTime _pauseStartTime;
    private TimeSpan _pausedElapsedTime;
    private int _currentPhaseIndex;
    private int _pauseCount;

    private bool _sessionStartStrictLock;
    private bool _sessionStartPanicKey;

    public SessionState State => _state;
    public ConditioningControlPanel.Models.Session? CurrentSession => _currentSession;
    public int CurrentPhaseIndex => _currentPhaseIndex;
    public int PauseCount => _pauseCount;
    public int XPPenalty => _pauseCount * 100;
    public bool SessionStartStrictLock => _sessionStartStrictLock;
    public bool SessionStartPanicKey => _sessionStartPanicKey;

    public TimeSpan ElapsedTime
    {
        get
        {
            if (_state == SessionState.Idle) return TimeSpan.Zero;
            if (_state == SessionState.Paused) return _pausedElapsedTime;

            var dateTimeElapsed = _pausedElapsedTime + (DateTime.Now - _startTime);
            var stopwatchElapsed = _wallClockStopwatch.Elapsed;

            var divergence = dateTimeElapsed - stopwatchElapsed;
            if (Math.Abs(divergence.TotalSeconds) > 30)
            {
                Log.Warning("Timer integrity: DateTime elapsed {DateTimeElapsed} vs Stopwatch {StopwatchElapsed} — divergence {Divergence}s, using Stopwatch",
                    dateTimeElapsed, stopwatchElapsed, divergence.TotalSeconds);
                return stopwatchElapsed;
            }

            return dateTimeElapsed < TimeSpan.Zero ? TimeSpan.Zero : dateTimeElapsed;
        }
    }

    public TimeSpan RemainingTime
    {
        get
        {
            if (_currentSession == null) return TimeSpan.Zero;
            var remaining = TimeSpan.FromMinutes(_currentSession.DurationMinutes) - ElapsedTime;
            return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }
    }

    public double ProgressPercent => _currentSession != null
        ? Math.Min(100, (ElapsedTime.TotalMinutes / _currentSession.DurationMinutes) * 100)
        : 0;

    public event EventHandler? SessionStarted;
    public event EventHandler? SessionStopped;
    public event EventHandler<SessionCompletedEventArgs>? SessionCompleted;
    public event EventHandler<SessionPhaseChangedEventArgs>? PhaseChanged;
    public event EventHandler<SessionProgressEventArgs>? ProgressUpdated;

    public SessionService(ISettingsService settings, IProgressionService progression, IScheduler scheduler)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _progression = progression ?? throw new ArgumentNullException(nameof(progression));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
    }

    public Task StartSessionAsync(ConditioningControlPanel.Models.Session session, CancellationToken cancellationToken = default)
    {
        if (_state != SessionState.Idle)
            throw new InvalidOperationException("A session is already running. Stop it first.");

        _currentSession = session;
        _state = SessionState.Running;
        _pauseCount = 0;
        _pausedElapsedTime = TimeSpan.Zero;
        _startTime = DateTime.Now;
        _wallClockStopwatch.Restart();
        _currentPhaseIndex = 0;
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _sessionStartStrictLock = _settings.Current.StrictLockEnabled;
        _sessionStartPanicKey = _settings.Current.PanicKeyEnabled;

        RecordSeasonFeatureUse(session);
        _settings.Save();

        _tickSubscription = _scheduler.StartPeriodicTimer(TimeSpan.FromSeconds(1), OnTick);

        if (session.Phases.Count > 0)
        {
            PhaseChanged?.Invoke(this, new SessionPhaseChangedEventArgs(session.Phases[0], 0));
        }

        SessionStarted?.Invoke(this, EventArgs.Empty);
        Log.Information("Session started: {Name}", session.Name);

        return Task.CompletedTask;
    }

    public void StopSession(bool completed = false)
    {
        if (_state == SessionState.Idle) return;

        var finalElapsedTime = ElapsedTime;
        _state = SessionState.Idle;
        _wallClockStopwatch.Stop();
        _cancellationTokenSource?.Cancel();
        _tickSubscription?.Dispose();
        _tickSubscription = null;

        SessionStopped?.Invoke(this, EventArgs.Empty);

        int xpForLog = 0;
        if (completed && _currentSession != null)
        {
            int baseXP = Math.Max(0, Math.Min(2500, _currentSession.BonusXP) - XPPenalty);
            int level = _settings.Current.PlayerLevel;
            double multiplier = _progression.GetSessionXPMultiplier(level);
            double durationMinutes = Math.Max(0, finalElapsedTime.TotalMinutes - 2);
            int durationBonus = (int)Math.Round(durationMinutes * (8 + level * 0.15));
            int finalXP = Math.Max(0, (int)Math.Round(baseXP * multiplier) + durationBonus);
            xpForLog = finalXP;

            Log.Information("Session completed: {Name}, XP: {XP} (base: {Base}, multiplier: {Mult:F2}x, paused {PauseCount}x, penalty: -{Penalty})",
                _currentSession.Name, finalXP, baseXP, multiplier, _pauseCount, XPPenalty);

            try
            {
                SessionCompleted?.Invoke(this, new SessionCompletedEventArgs(
                    _currentSession, finalElapsedTime, finalXP, _pauseCount));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in SessionCompleted event handler");
            }
        }
        else
        {
            Log.Information("Session stopped early");
        }

        _currentSession = null;
    }

    public void PauseSession()
    {
        if (_state != SessionState.Running || _currentSession == null) return;

        _pausedElapsedTime = ElapsedTime;
        _state = SessionState.Paused;
        _pauseCount++;
        _pauseStartTime = DateTime.Now;
        _wallClockStopwatch.Stop();
        _tickSubscription?.Dispose();
        _tickSubscription = null;

        Log.Information("Session paused (pause #{Count}, -100 XP penalty)", _pauseCount);
    }

    public void ResumeSession()
    {
        if (_state != SessionState.Paused || _currentSession == null) return;

        _state = SessionState.Running;
        _startTime = DateTime.Now;
        _wallClockStopwatch.Start();
        _tickSubscription = _scheduler.StartPeriodicTimer(TimeSpan.FromSeconds(1), OnTick);

        Log.Information("Session resumed");
    }

    private void OnTick()
    {
        if (_state != SessionState.Running || _currentSession == null) return;

        var elapsed = ElapsedTime;
        var elapsedMinutes = elapsed.TotalMinutes;
        var totalMinutes = _currentSession.DurationMinutes;

        if (elapsedMinutes >= totalMinutes)
        {
            StopSession(completed: true);
            return;
        }

        ProgressUpdated?.Invoke(this, new SessionProgressEventArgs(
            elapsed, RemainingTime, ProgressPercent));

        CheckPhaseTransition(elapsedMinutes);
    }

    private void CheckPhaseTransition(double elapsedMinutes)
    {
        if (_currentSession?.Phases == null) return;

        int newPhaseIndex = 0;
        for (int i = _currentSession.Phases.Count - 1; i >= 0; i--)
        {
            if (elapsedMinutes >= _currentSession.Phases[i].StartMinute)
            {
                newPhaseIndex = i;
                break;
            }
        }

        if (newPhaseIndex != _currentPhaseIndex)
        {
            _currentPhaseIndex = newPhaseIndex;
            var phase = _currentSession.Phases[newPhaseIndex];
            PhaseChanged?.Invoke(this, new SessionPhaseChangedEventArgs(phase, newPhaseIndex));
            Log.Information("Phase changed: {Phase}", phase.Name);
        }
    }

    private static void RecordSeasonFeatureUse(ConditioningControlPanel.Models.Session session)
    {
        try
        {
            SeasonRecapService.IncrementSessionStarted();
            var ss = session.Settings;
            if (ss.FlashEnabled) SeasonRecapService.TrackFeature(SeasonFeatureKeys.Flash);
            if (ss.MandatoryVideosEnabled) SeasonRecapService.TrackFeature(SeasonFeatureKeys.Video);
            if (ss.SubliminalEnabled) SeasonRecapService.TrackFeature(SeasonFeatureKeys.Subliminal);
            if (ss.SpiralEnabled || ss.PinkFilterEnabled) SeasonRecapService.TrackFeature(SeasonFeatureKeys.Overlay);
            if (ss.BubblesEnabled) SeasonRecapService.TrackFeature(SeasonFeatureKeys.Bubbles);
            if (ss.BubbleCountEnabled) SeasonRecapService.TrackFeature(SeasonFeatureKeys.BubbleCount);
            if (ss.BouncingTextEnabled) SeasonRecapService.TrackFeature(SeasonFeatureKeys.BouncingText);
            if (ss.LockCardEnabled) SeasonRecapService.TrackFeature(SeasonFeatureKeys.LockCard);
            if (ss.MindWipeEnabled) SeasonRecapService.TrackFeature(SeasonFeatureKeys.MindWipe);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SeasonRecap: failed to record session feature use");
        }
    }

    public void Dispose()
    {
        StopSession(false);
        _tickSubscription?.Dispose();
        _cancellationTokenSource?.Dispose();
    }
}
