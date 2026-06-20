namespace ConditioningControlPanel.Core.Platform;

public interface IAudioDeviceService
{
    IReadOnlyList<AudioDeviceInfo> GetOutputDevices();
    string? GetDefaultOutputDeviceId();
    void SetPreferredDevice(string? deviceId);
}

public sealed record AudioDeviceInfo(string Id, string Name, bool IsDefault);
