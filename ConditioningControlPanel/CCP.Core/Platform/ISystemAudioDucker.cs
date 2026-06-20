namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Ducks other applications' audio while CCP audio is playing.
/// Platform support varies; no-ops are acceptable.
/// </summary>
public interface ISystemAudioDucker
{
    void Duck();
    void Unduck();
}
