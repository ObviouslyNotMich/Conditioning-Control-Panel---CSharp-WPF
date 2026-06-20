using Avalonia.Threading;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Avalonia <see cref="IScheduler"/> implementation backed by <see cref="DispatcherTimer"/>.
/// </summary>
public sealed class AvaloniaScheduler : IScheduler
{
    public IDisposable StartPeriodicTimer(TimeSpan interval, Action callback)
    {
        var timer = new DispatcherTimer { Interval = interval };
        EventHandler? handler = null;
        handler = (_, _) => callback();
        timer.Tick += handler;
        timer.Start();
        return new DisposableAction(() =>
        {
            timer.Stop();
            timer.Tick -= handler;
        });
    }

    public IDisposable StartOneShotTimer(TimeSpan dueTime, Action callback)
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
        return new DisposableAction(() =>
        {
            timer.Stop();
            timer.Tick -= handler;
        });
    }
}
