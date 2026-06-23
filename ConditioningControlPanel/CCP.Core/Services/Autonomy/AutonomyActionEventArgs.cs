namespace ConditioningControlPanel.Core.Services.Autonomy;

/// <summary>
/// Event args raised when an autonomous action is triggered.
/// </summary>
public class AutonomyActionEventArgs : EventArgs
{
    public AutonomyActionType ActionType { get; }
    public AutonomyTriggerSource Source { get; }
    public string? Context { get; }

    public AutonomyActionEventArgs(AutonomyActionType actionType, AutonomyTriggerSource source, string? context = null)
    {
        ActionType = actionType;
        Source = source;
        Context = context;
    }
}
