using System;
using System.Threading;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.WindowsOnly;

/// <summary>
/// Mutex-based single-instance shim for <see cref="ISingleInstanceService"/>.
/// </summary>
public sealed class WpfSingleInstanceService : ISingleInstanceService
{
    private const string MutexName = "ConditioningControlPanel_SingleInstance_v1";

    private readonly Mutex _mutex;

    public bool IsFirstInstance { get; }

    public event EventHandler<string[]>? ArgumentsReceived;

    public WpfSingleInstanceService()
    {
        _mutex = new Mutex(true, MutexName, out var created);
        IsFirstInstance = created;

        if (!IsFirstInstance)
        {
            _mutex.Dispose();
        }
    }

    public void SignalFirstInstance(string[] args)
    {
        // Stub: a full implementation would send the args over a named pipe
        // to the already-running process and raise ArgumentsReceived there.
        if (IsFirstInstance)
        {
            ArgumentsReceived?.Invoke(this, args);
        }
    }
}
