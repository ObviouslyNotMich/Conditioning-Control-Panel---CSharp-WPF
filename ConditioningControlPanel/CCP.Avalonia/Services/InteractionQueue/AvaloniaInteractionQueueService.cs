using System;
using System.Collections.Generic;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Services.InteractionQueue;

/// <summary>
/// Avalonia implementation of <see cref="IInteractionQueueService"/>.
/// Coordinates fullscreen interactions (videos, bubble counts, lock cards, pop quizzes)
/// so only one runs at a time; queued items start when the active one completes.
/// </summary>
public sealed class AvaloniaInteractionQueueService : IInteractionQueueService
{
    private readonly IUiDispatcher _dispatcher;
    private readonly IScheduler _scheduler;
    private readonly IAppLogger _logger;
    private readonly object _sync = new();
    private readonly Queue<(string Type, Action Trigger)> _queue = new();

    private string? _currentInteraction;
    private DateTime _interactionStartTime;
    private IDisposable? _stuckTimer;

    private const int DefaultMaxInteractionMinutes = 5;

    public AvaloniaInteractionQueueService(IUiDispatcher dispatcher, IScheduler scheduler, IAppLogger logger)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsBusy
    {
        get { lock (_sync) return _currentInteraction != null; }
    }

    public bool TryStart(string interactionType, Action triggerAction, bool queue = true)
    {
        if (string.IsNullOrWhiteSpace(interactionType)) throw new ArgumentException("Interaction type is required.", nameof(interactionType));
        if (triggerAction == null) throw new ArgumentNullException(nameof(triggerAction));

        lock (_sync)
        {
            if (_currentInteraction == null)
            {
                _currentInteraction = interactionType;
                _interactionStartTime = DateTime.Now;
                StartStuckDetectionTimer();
                _logger.Information("InteractionQueue: Starting {Type}", interactionType);
                triggerAction();
                return true;
            }

            var activeDuration = DateTime.Now - _interactionStartTime;
            _logger.Debug("InteractionQueue: {Type} blocked by {Current} (active for {Duration:F1}s, queue: {Count})",
                interactionType, _currentInteraction, activeDuration.TotalSeconds, _queue.Count);

            if (queue)
            {
                foreach (var item in _queue)
                {
                    if (item.Type == interactionType)
                    {
                        _logger.Debug("InteractionQueue: {Type} already queued, skipping duplicate", interactionType);
                        return false;
                    }
                }

                _queue.Enqueue((interactionType, triggerAction));
                _logger.Information("InteractionQueue: Queued {Type} (queue size: {Count})", interactionType, _queue.Count);
            }
            else
            {
                _logger.Debug("InteractionQueue: Discarded {Type} (busy with {Current})", interactionType, _currentInteraction);
            }

            return false;
        }
    }

    public void Complete(string interactionType)
    {
        lock (_sync)
        {
            StopStuckDetectionTimer();

            if (_currentInteraction != interactionType)
            {
                if (_currentInteraction == null)
                {
                    _logger.Debug("InteractionQueue: Complete({Type}) called but queue already clear", interactionType);
                    return;
                }

                var activeDuration = DateTime.Now - _interactionStartTime;
                _logger.Warning("InteractionQueue: Complete called for {Type} but current is {Current} (active {Duration:F1}s). Clearing anyway to prevent stuck state.",
                    interactionType, _currentInteraction, activeDuration.TotalSeconds);
            }

            _logger.Information("InteractionQueue: Completed {Type}", interactionType);
            _currentInteraction = null;

            if (_queue.Count > 0)
            {
                var next = _queue.Dequeue();
                _currentInteraction = next.Type;
                _interactionStartTime = DateTime.Now;
                StartStuckDetectionTimer();
                _logger.Information("InteractionQueue: Starting queued {Type} (remaining: {Count})", next.Type, _queue.Count);

                _dispatcher.Post(() =>
                {
                    try { next.Trigger(); }
                    catch (Exception ex)
                    {
                        _logger.Warning(ex, "InteractionQueue: queued {Type} trigger failed", next.Type);
                        Complete(next.Type);
                    }
                });
            }
        }
    }

    public void ForceReset()
    {
        lock (_sync)
        {
            StopStuckDetectionTimer();
            var was = _currentInteraction;
            _currentInteraction = null;
            _queue.Clear();
            if (was != null)
                _logger.Information("InteractionQueue: Force reset (was {Type})", was);
        }
    }

    public void ExtendTimeout(TimeSpan duration)
    {
        lock (_sync)
        {
            if (_currentInteraction == null) return;
            StopStuckDetectionTimer();
            _interactionStartTime = DateTime.Now;
            _stuckTimer = _scheduler.StartOneShotTimer(duration, () =>
            {
                lock (_sync)
                {
                    if (_currentInteraction == null) return;
                    var activeDuration = DateTime.Now - _interactionStartTime;
                    _logger.Warning("InteractionQueue: {Type} appears stuck (active {Duration:F1}s). Force-completing.",
                        _currentInteraction, activeDuration.TotalSeconds);
                    Complete(_currentInteraction);
                }
            });
            _logger.Debug("InteractionQueue: Extended stuck-detection timeout to {Duration:F0}s for {Type}",
                duration.TotalSeconds, _currentInteraction);
        }
    }

    private void StartStuckDetectionTimer()
    {
        StopStuckDetectionTimer();
        _stuckTimer = _scheduler.StartOneShotTimer(TimeSpan.FromMinutes(DefaultMaxInteractionMinutes), () =>
        {
            lock (_sync)
            {
                if (_currentInteraction == null) return;
                var activeDuration = DateTime.Now - _interactionStartTime;
                _logger.Warning("InteractionQueue: {Type} appears stuck (active {Duration:F1}s). Force-completing.",
                    _currentInteraction, activeDuration.TotalSeconds);
                Complete(_currentInteraction);
            }
        });
    }

    private void StopStuckDetectionTimer()
    {
        _stuckTimer?.Dispose();
        _stuckTimer = null;
    }
}
