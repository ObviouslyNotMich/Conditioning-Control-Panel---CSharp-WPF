using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Single-instance service stub. Mobile OSs control instance lifetime; desktop heads can
/// replace this with a platform-specific implementation.
/// </summary>
public sealed class AvaloniaSingleInstanceService : ISingleInstanceService
{
    public bool IsFirstInstance => true;

    public event EventHandler<string[]>? ArgumentsReceived { add { } remove { } }

    public void SignalFirstInstance(string[] args)
    {
    }
}
