using System.Collections.Generic;

namespace ConditioningControlPanel.Core.Services.RemoteControl;

/// <summary>
/// Provides runtime status information for the remote-control status push endpoint.
/// Implementations live in the UI head so they can read live service state.
/// </summary>
public interface IRemoteStatusProvider
{
    /// <summary>
    /// Returns the list of currently active service identifiers (e.g. "flash_loop", "pink_filter").
    /// </summary>
    IReadOnlyList<string> GetActiveServices();

    /// <summary>
    /// Returns a serializable object describing the current session progress, or null.
    /// </summary>
    object? GetSessionProgress();

    /// <summary>
    /// Returns a list of available sessions for the controller to pick from, or null.
    /// </summary>
    IReadOnlyList<object>? GetAvailableSessions();
}
