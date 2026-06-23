using System;
using System.IO;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Avalonia.Services.Lockdown;

/// <summary>
/// Avalonia implementation of <see cref="ILockdownService"/>.
/// Timed session that forces StrictLockEnabled=true and PanicKeyEnabled=false,
/// persists recovery state to disk, and restores previous settings on exit.
/// </summary>
public sealed class AvaloniaLockdownService : ILockdownService
{
    private readonly ISettingsService _settingsService;
    private readonly IAppEnvironment _appEnvironment;
    private readonly ILogger<AvaloniaLockdownService>? _logger;
    private readonly object _sync = new();

    private bool _isActive;
    private DateTime _activationTime;
    private TimeSpan _requestedDuration;
    private TimeSpan _remaining;
    private TimeSpan _lastActiveDuration;
    private bool _previousStrictLockEnabled;
    private bool _previousPanicKeyEnabled;
    private DispatcherTimer? _timer;
    private bool _isDisposed;

    private const string RecoveryFileName = "lockdown_recovery.json";
    private const string ExitPhrase = "let me out";

    public AvaloniaLockdownService(
        ISettingsService settingsService,
        IAppEnvironment appEnvironment,
        ILogger<AvaloniaLockdownService>? logger = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _appEnvironment = appEnvironment ?? throw new ArgumentNullException(nameof(appEnvironment));
        _logger = logger;
    }

    public bool IsActive
    {
        get { lock (_sync) return _isActive; }
        private set { lock (_sync) _isActive = value; }
    }

    public TimeSpan Remaining
    {
        get { lock (_sync) return _remaining; }
        private set { lock (_sync) _remaining = value; }
    }

    public TimeSpan LastActiveDuration
    {
        get { lock (_sync) return _lastActiveDuration; }
        private set { lock (_sync) _lastActiveDuration = value; }
    }

    public event Action? LockdownActivated;
    public event Action? LockdownDeactivated;
    public event Action<TimeSpan>? CountdownTick;

    public void Activate(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be positive.");

        lock (_sync)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(AvaloniaLockdownService));
            if (_isActive) return;

            var settings = _settingsService.Current;
            _previousStrictLockEnabled = settings.StrictLockEnabled;
            _previousPanicKeyEnabled = settings.PanicKeyEnabled;

            settings.StrictLockEnabled = true;
            settings.PanicKeyEnabled = false;

            _activationTime = DateTime.UtcNow;
            _requestedDuration = duration;
            _remaining = duration;
            _isActive = true;

            WriteRecoveryFile(new LockdownRecoveryState
            {
                ActivationTimeUtc = _activationTime,
                Duration = _requestedDuration,
                PreviousStrictLockEnabled = _previousStrictLockEnabled,
                PreviousPanicKeyEnabled = _previousPanicKeyEnabled
            });

            _settingsService.SaveImmediate();

            _timer?.Stop();
            _timer = StartPeriodicTimer(TimeSpan.FromSeconds(1), OnTick);
        }

        _logger?.LogInformation("Lockdown activated for {Duration:mm\\:ss}", duration);
        LockdownActivated?.Invoke();
        CountdownTick?.Invoke(Remaining);
    }

    public void Deactivate()
    {
        bool wasActive;
        TimeSpan elapsed;

        lock (_sync)
        {
            wasActive = _isActive;
            if (!wasActive) return;

            _isActive = false;
            _timer?.Stop();
            _timer = null;

            elapsed = _requestedDuration - _remaining;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            if (elapsed > _requestedDuration) elapsed = _requestedDuration;
            LastActiveDuration = elapsed;

            var settings = _settingsService.Current;
            settings.StrictLockEnabled = _previousStrictLockEnabled;
            settings.PanicKeyEnabled = _previousPanicKeyEnabled;

            DeleteRecoveryFile();
            _settingsService.SaveImmediate();
        }

        _logger?.LogInformation("Lockdown deactivated after {Elapsed:mm\\:ss}", elapsed);
        LockdownDeactivated?.Invoke();
    }

    public bool TryExitWithPhrase(string phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase))
            return false;

        if (!string.Equals(phrase.Trim(), ExitPhrase, StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogInformation("Lockdown exit phrase attempted but did not match");
            return false;
        }

        Deactivate();
        return true;
    }

    public void RecoverIfNeeded()
    {
        try
        {
            var path = RecoveryFilePath;
            if (!File.Exists(path))
                return;

            var json = File.ReadAllText(path);
            var state = JsonConvert.DeserializeObject<LockdownRecoveryState>(json);
            if (state == null)
            {
                DeleteRecoveryFile();
                return;
            }

            var now = DateTime.UtcNow;
            var remaining = state.Duration - (now - state.ActivationTimeUtc);
            if (remaining <= TimeSpan.Zero)
            {
                _logger?.LogInformation("Lockdown recovery file found but session already expired");
                DeleteRecoveryFile();
                return;
            }

            lock (_sync)
            {
                if (_isActive) return;
                if (_isDisposed) return;

                _previousStrictLockEnabled = state.PreviousStrictLockEnabled;
                _previousPanicKeyEnabled = state.PreviousPanicKeyEnabled;
                _activationTime = state.ActivationTimeUtc;
                _requestedDuration = state.Duration;
                _remaining = remaining;
                _isActive = true;

                var settings = _settingsService.Current;
                settings.StrictLockEnabled = true;
                settings.PanicKeyEnabled = false;

                _timer?.Stop();
                _timer = StartPeriodicTimer(TimeSpan.FromSeconds(1), OnTick);
            }

            _logger?.LogInformation("Lockdown recovered with {Remaining:mm\\:ss} remaining", remaining);
            LockdownActivated?.Invoke();
            CountdownTick?.Invoke(Remaining);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to recover lockdown session");
            DeleteRecoveryFile();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _timer?.Stop();
            _timer = null;
        }
    }

    private void OnTick()
    {
        bool shouldDeactivate = false;

        lock (_sync)
        {
            if (!_isActive || _isDisposed) return;

            var elapsed = DateTime.UtcNow - _activationTime;
            _remaining = _requestedDuration - elapsed;

            if (_remaining <= TimeSpan.Zero)
            {
                _remaining = TimeSpan.Zero;
                shouldDeactivate = true;
            }
        }

        CountdownTick?.Invoke(Remaining);

        if (shouldDeactivate)
        {
            Deactivate();
        }
    }

    private string RecoveryFilePath => Path.Combine(_appEnvironment.UserDataPath, RecoveryFileName);

    private void WriteRecoveryFile(LockdownRecoveryState state)
    {
        try
        {
            Directory.CreateDirectory(_appEnvironment.UserDataPath);
            var json = JsonConvert.SerializeObject(state, Formatting.Indented);
            File.WriteAllText(RecoveryFilePath, json);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to write lockdown recovery file");
        }
    }

    private void DeleteRecoveryFile()
    {
        try
        {
            var path = RecoveryFilePath;
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to delete lockdown recovery file");
        }
    }

    private static DispatcherTimer StartPeriodicTimer(TimeSpan interval, Action callback)
    {
        var timer = new DispatcherTimer { Interval = interval };
        timer.Tick += (_, _) => callback();
        timer.Start();
        return timer;
    }

    private static DispatcherTimer StartOneShotTimer(TimeSpan dueTime, Action callback)
    {
        var timer = new DispatcherTimer { Interval = dueTime };
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            timer.Stop();
            timer.Tick -= handler;
            callback();
        };
        timer.Tick += handler;
        timer.Start();
        return timer;
    }

    private sealed class LockdownRecoveryState
    {
        public DateTime ActivationTimeUtc { get; set; }
        public TimeSpan Duration { get; set; }
        public bool PreviousStrictLockEnabled { get; set; }
        public bool PreviousPanicKeyEnabled { get; set; }
    }
}
