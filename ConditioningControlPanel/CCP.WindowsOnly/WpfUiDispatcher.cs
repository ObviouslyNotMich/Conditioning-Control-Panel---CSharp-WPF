using System;
using System.Threading.Tasks;
using System.Windows.Threading;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.WindowsOnly;

/// <summary>
/// WPF dispatcher shim for <see cref="IUiDispatcher"/>.
/// </summary>
public sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    public WpfUiDispatcher()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    public bool CheckAccess() => _dispatcher.CheckAccess();

    public void Post(Action action) => _dispatcher.BeginInvoke(action);

    public void Invoke(Action action) => _dispatcher.Invoke(action);

    public Task InvokeAsync(Action action) => _dispatcher.InvokeAsync(action).Task;

    public T Invoke<T>(Func<T> func) => _dispatcher.Invoke(func);
}
