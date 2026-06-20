namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Abstraction over the UI thread dispatcher.
/// </summary>
public interface IUiDispatcher
{
    bool CheckAccess();
    void Post(Action action);
    void Invoke(Action action);
    Task InvokeAsync(Action action);
    T Invoke<T>(Func<T> func);
}
