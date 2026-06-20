namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Ensures only one app instance runs and forwards args to it.
/// </summary>
public interface ISingleInstanceService
{
    bool IsFirstInstance { get; }
    void SignalFirstInstance(string[] args);
    event EventHandler<string[]>? ArgumentsReceived;
}
