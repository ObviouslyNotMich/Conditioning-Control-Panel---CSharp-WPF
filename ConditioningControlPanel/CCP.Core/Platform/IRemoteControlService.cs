namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Cross-platform abstraction for the remote-control server session.
/// Implementations own the network/server lifecycle; consumers bind to
/// <see cref="IsActive"/>, <see cref="ControllerConnected"/>, and the
/// session identifiers exposed by this interface.
/// </summary>
public interface IRemoteControlService
{
    bool IsActive { get; }
    bool ControllerConnected { get; }
    bool ControllerIdle { get; }
    string? SessionCode { get; }
    string? ConnectPin { get; }
    string? Tier { get; }

    event EventHandler? ControllerConnectedChanged;
    event EventHandler? ControllerIdleChanged;
    event EventHandler? SessionStarted;
    event EventHandler? SessionEnded;

    /// <summary>
    /// Raised when a remote command is received from the controller.
    /// The event argument is the command action name.
    /// </summary>
    event EventHandler<string>? CommandReceived;

    Task<string?> StartSessionAsync(string tier);
    Task StopSessionAsync();
    Task OptInToDirectoryAsync(List<string> tags, string statusText);
    Task DisconnectControllerAsync();

    /// <summary>
    /// Pushes the current status to the server immediately. No-op if no session is active.
    /// </summary>
    Task PushStatusNowAsync();

    /// <summary>
    /// Sends a short text emote to the connected controller(s).
    /// Returns (ok, error, retryAfterSeconds).
    /// </summary>
    Task<(bool ok, string? error, int? retryAfterSeconds)> SendEmoteAsync(string text, string icon, string kind);
}
