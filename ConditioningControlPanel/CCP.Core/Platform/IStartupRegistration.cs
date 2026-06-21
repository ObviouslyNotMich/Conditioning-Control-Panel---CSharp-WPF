namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Cross-platform seam for registering the application to launch on OS login.
/// </summary>
public interface IStartupRegistration
{
    /// <summary>True if the app is currently registered to run on startup.</summary>
    bool IsRegistered { get; }

    /// <summary>Register or unregister the app to run on startup.</summary>
    /// <param name="value">True to register, false to unregister.</param>
    void SetRegistered(bool value);
}
