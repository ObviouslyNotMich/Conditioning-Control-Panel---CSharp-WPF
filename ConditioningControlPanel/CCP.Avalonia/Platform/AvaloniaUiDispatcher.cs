using Avalonia.Threading;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Avalonia implementation of <see cref="IUiDispatcher"/> using <see cref="Dispatcher.UIThread"/>.
/// </summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    public void Post(Action action) => Dispatcher.UIThread.Post(action);

    public void Invoke(Action action) => Dispatcher.UIThread.Invoke(action);

    public Task InvokeAsync(Action action) => Dispatcher.UIThread.InvokeAsync(action).GetTask();

    public T Invoke<T>(Func<T> func) => Dispatcher.UIThread.Invoke(func);
}
