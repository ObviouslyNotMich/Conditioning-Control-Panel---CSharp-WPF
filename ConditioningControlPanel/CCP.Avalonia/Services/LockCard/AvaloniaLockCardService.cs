using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Windows;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.LockCard;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.Services.LockCard;

/// <summary>
/// Avalonia implementation of <see cref="ILockCardService"/>.
/// Schedules periodic lock-card popups and delegates display to the existing
/// <see cref="LockCardWindow"/>, which handles multi-monitor sync, strict mode,
/// and completion reporting.
/// </summary>
public sealed class AvaloniaLockCardService : ILockCardService, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IUiDispatcher _dispatcher;
    private readonly IInteractionQueueService _interactionQueue;
    private readonly IAppLogger? _logger;
    private readonly Random _random = new();
    private readonly DispatcherTimer _timer = new();

    private bool _isRunning;
    private bool _isDisposed;
    private DateTime _lastShown = DateTime.MinValue;

    public AvaloniaLockCardService(
        ISettingsService settings,
        IUiDispatcher dispatcher,
        IInteractionQueueService interactionQueue,
        IAppLogger? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _interactionQueue = interactionQueue ?? throw new ArgumentNullException(nameof(interactionQueue));
        _logger = logger;

        _timer.Tick += OnTimerTick;
    }

    public bool IsRunning => _isRunning;

    public event EventHandler<LockCardCompletedEventArgs>? LockCardCompleted;

    public void Start()
    {
        if (_isRunning || _isDisposed) return;

        var settings = _settings.Current;
        if (settings == null || !settings.LockCardEnabled)
        {
            _logger?.Information("AvaloniaLockCardService: disabled in settings");
            return;
        }

        _isRunning = true;
        _timer.Interval = CalculateNextInterval(settings.LockCardFrequency);
        _timer.Start();

        _logger?.Information("AvaloniaLockCardService started - approximately {PerHour}/hour", settings.LockCardFrequency);
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;

        _timer.Stop();
        _dispatcher.Invoke(() =>
        {
            try { LockCardWindow.ForceCloseAll(); } catch { }
        });

        _logger?.Information("AvaloniaLockCardService stopped");
    }

    public void ShowLockCard(string? customPhrase = null, int customRepeats = -1, bool customStrict = false, bool isTest = false)
    {
        _dispatcher.Invoke(() =>
        {
            try
            {
                if (LockCardWindow.IsAnyOpen())
                {
                    _logger?.Information("AvaloniaLockCardService: a lock card is already open; skipping");
                    return;
                }

                _interactionQueue.TryStart("LockCard", () =>
                {
                    try
                    {
                        var settings = _settings.Current;
                        if (settings == null)
                        {
                            _interactionQueue.Complete("LockCard");
                            return;
                        }

                        var enabledPhrases = settings.LockCardPhrases?
                            .Where(p => p.Value)
                            .Select(p => p.Key)
                            .ToList() ?? new List<string>();

                        if (enabledPhrases.Count == 0 && string.IsNullOrEmpty(customPhrase))
                        {
                            _logger?.Warning("AvaloniaLockCardService: no phrases enabled");
                            _interactionQueue.Complete("LockCard");
                            return;
                        }

                        var phrase = !string.IsNullOrEmpty(customPhrase)
                            ? customPhrase
                            : enabledPhrases[_random.Next(enabledPhrases.Count)];

                        var repeats = customRepeats >= 0 ? customRepeats : settings.LockCardRepeats;
                        var strict = customStrict || settings.LockCardStrict;

                        LockCardWindow.ShowOnAllMonitors(phrase, repeats, strict, isTest);
                        _lastShown = DateTime.Now;

                        _logger?.Information("AvaloniaLockCardService: lock card shown - Phrase: {Phrase}, Repeats: {Repeats}, Strict: {Strict}, Test: {IsTest}",
                            phrase, repeats, strict, isTest);
                    }
                    catch (Exception ex)
                    {
                        _logger?.Error(ex, "AvaloniaLockCardService: failed to show lock card");
                        _interactionQueue.Complete("LockCard");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "AvaloniaLockCardService: failed to show lock card");
            }
        });
    }

    public void TestLockCard()
    {
        ShowLockCard(isTest: true);
    }

    public void NotifyCompleted(string phrase, int totalErrors, int requiredRepeats)
    {
        _logger?.Information(
            "AvaloniaLockCardService.NotifyCompleted: {Phrase} ({Errors} errors, {Repeats} repeats)",
            phrase, totalErrors, requiredRepeats);

        LockCardCompleted?.Invoke(this, new LockCardCompletedEventArgs
        {
            Phrase = phrase,
            Mistakes = totalErrors,
            Repeats = requiredRepeats
        });
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Stop();
        _timer.Tick -= OnTimerTick;
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        var settings = _settings.Current;
        if (settings == null || !settings.LockCardEnabled || !_isRunning) return;

        _timer.Interval = CalculateNextInterval(settings.LockCardFrequency);
        ShowLockCard();
    }

    private TimeSpan CalculateNextInterval(int perHour)
    {
        var intervalMinutes = 60.0 / Math.Max(1, perHour);
        var minInterval = intervalMinutes * 0.7;
        var maxInterval = intervalMinutes * 1.3;
        return TimeSpan.FromMinutes(_random.NextDouble() * (maxInterval - minInterval) + minInterval);
    }
}
