using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.CoreAudioApi;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.WindowsOnly;

/// <summary>
/// NAudio/MMDevice-based shim for <see cref="IAudioDeviceService"/>.
/// </summary>
public sealed class WpfAudioDeviceService : IAudioDeviceService
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private string? _preferredDeviceId;

    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices()
    {
        string? defaultId = null;
        try
        {
            using var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            defaultId = defaultDevice?.ID;
        }
        catch
        {
            // Ignore and mark none as default.
        }

        return _enumerator
            .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(d => new AudioDeviceInfo(d.ID, d.FriendlyName, d.ID == defaultId))
            .ToList();
    }

    public string? GetDefaultOutputDeviceId()
    {
        try
        {
            using var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return device?.ID;
        }
        catch
        {
            return null;
        }
    }

    public void SetPreferredDevice(string? deviceId)
    {
        _preferredDeviceId = deviceId;
    }
}
