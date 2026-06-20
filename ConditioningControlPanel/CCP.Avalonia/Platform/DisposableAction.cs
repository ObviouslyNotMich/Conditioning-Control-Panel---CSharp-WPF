namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Simple <see cref="IDisposable"/> that invokes an action on disposal.
/// </summary>
internal sealed class DisposableAction : IDisposable
{
    private Action? _action;

    public DisposableAction(Action action) => _action = action;

    public void Dispose()
    {
        Interlocked.Exchange(ref _action, null)?.Invoke();
    }
}
