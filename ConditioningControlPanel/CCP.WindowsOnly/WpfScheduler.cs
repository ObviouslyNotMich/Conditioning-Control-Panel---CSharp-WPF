using System;
using System.Threading;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.WindowsOnly;

/// <summary>
/// Thread-pool based scheduler shim for <see cref="IScheduler"/>.
/// </summary>
public sealed class WpfScheduler : IScheduler
{
    public IDisposable StartPeriodicTimer(TimeSpan interval, Action callback)
    {
        var timer = new Timer(_ => callback(), null, interval, interval);
        return new DisposableAction(() => timer.Dispose());
    }

    public IDisposable StartOneShotTimer(TimeSpan dueTime, Action callback)
    {
        var timer = new Timer(_ => callback(), null, dueTime, Timeout.InfiniteTimeSpan);
        return new DisposableAction(() => timer.Dispose());
    }

    private sealed class DisposableAction : IDisposable
    {
        private readonly Action _dispose;
        public DisposableAction(Action dispose) => _dispose = dispose;
        public void Dispose() => _dispose();
    }
}
