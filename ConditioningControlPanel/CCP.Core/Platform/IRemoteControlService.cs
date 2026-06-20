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
    string? SessionCode { get; }
    string? ConnectPin { get; }

    event EventHandler? ControllerConnectedChanged;
    event EventHandler? SessionStarted;
    event EventHandler? SessionEnded;

    Task<string?> StartSessionAsync(string tier);
    Task StopSessionAsync();
    Task OptInToDirectoryAsync(List<string> tags, string statusText);
    Task DisconnectControllerAsync();
}
