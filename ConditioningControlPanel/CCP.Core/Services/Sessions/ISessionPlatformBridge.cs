namespace ConditioningControlPanel.Core.Services.Sessions;

/// <summary>
/// Platform-specific actions for the session manager.
/// </summary>
public interface ISessionPlatformBridge
{
    /// <summary>
    /// Open the custom sessions folder in the OS file manager.
    /// </summary>
    void OpenCustomSessionsFolder(string folderPath);
}
