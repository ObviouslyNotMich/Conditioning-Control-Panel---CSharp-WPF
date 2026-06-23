namespace ConditioningControlPanel.Core.Platform;

public interface IAudioDeviceService
{
    /// <summary>Raised when <see cref="SetPreferredDevice"/> changes the active preferred device.</summary>
    event EventHandler? PreferredDeviceChanged;

    IReadOnlyList<AudioDeviceInfo> GetOutputDevices();
    string? GetDefaultOutputDeviceId();
    void SetPreferredDevice(string? deviceId);
}

public sealed record AudioDeviceInfo(string Id, string Name, bool IsDefault);
