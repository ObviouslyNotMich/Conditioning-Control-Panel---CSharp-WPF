using System;
using System.Collections.Generic;
using ConditioningControlPanel.Core.Platform;
using NAudio.CoreAudioApi;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows;

/// <summary>
/// Windows audio device enumeration using NAudio/CoreAudioAPI (WASAPI).
/// Provides accurate playback endpoint names and the system default device.
/// </summary>
public sealed class WindowsAudioDeviceService : IAudioDeviceService
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private string? _preferredDeviceId;

    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices()
    {
        var devices = new List<AudioDeviceInfo>();

        try
        {
            var collection = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var device in collection)
            {
                using (device)
                {
                    devices.Add(new AudioDeviceInfo(device.ID, device.FriendlyName, IsDefault: false));
                }
            }
        }
        catch
        {
            // CoreAudio may be unavailable in some Windows sandboxes; fail open.
        }

        return devices;
    }

    public string? GetDefaultOutputDeviceId()
    {
        try
        {
            using var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return defaultDevice?.ID ?? _preferredDeviceId;
        }
        catch
        {
            return _preferredDeviceId;
        }
    }

    public void SetPreferredDevice(string? deviceId)
    {
        _preferredDeviceId = deviceId;
    }
}
