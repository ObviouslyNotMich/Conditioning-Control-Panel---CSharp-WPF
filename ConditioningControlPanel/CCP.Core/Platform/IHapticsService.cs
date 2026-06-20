namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Cross-platform abstraction for a haptic device provider.
/// Implementations may be real device bridges (Lovense, Buttplug.io, etc.)
/// or stubs used for UI testing in the Avalonia head.
/// </summary>
public interface IHapticsService
{
    bool IsConnected { get; }
    bool IsConnecting { get; }
    IReadOnlyList<string> ConnectedDevices { get; }

    event EventHandler<bool>? ConnectionStateChanged;
    event EventHandler<string>? DeviceAdded;
    event EventHandler<string>? DeviceRemoved;

    Task<bool> ConnectAsync(string providerUrl);
    void Disconnect();
    Task<bool> TestAsync(int intensityPercent, int durationMs);
}
