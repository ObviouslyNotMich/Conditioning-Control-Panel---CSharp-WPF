namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Cross-platform timer/scheduler abstraction.
/// </summary>
public interface IScheduler
{
    IDisposable StartPeriodicTimer(TimeSpan interval, Action callback);
    IDisposable StartOneShotTimer(TimeSpan dueTime, Action callback);
}
