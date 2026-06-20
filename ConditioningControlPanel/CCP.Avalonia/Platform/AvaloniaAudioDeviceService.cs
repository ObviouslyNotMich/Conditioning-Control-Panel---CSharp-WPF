using System;
using System.Collections.Generic;
using ConditioningControlPanel.Core.Platform;
using LibVLCSharp.Shared;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Cross-platform audio device enumeration using LibVLC's audio output device APIs.
/// Device selection is stored but not applied to playback here; consumers should pass
/// the chosen <see cref="AudioDeviceInfo.Id"/> to the audio player.
/// </summary>
public sealed class AvaloniaAudioDeviceService : IAudioDeviceService
{
    private readonly LibVLC _libVlc;
    private string? _preferredDeviceId;

    public AvaloniaAudioDeviceService(LibVLC libVlc)
    {
        _libVlc = libVlc;
    }

    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices()
    {
        var devices = new List<AudioDeviceInfo>();

        try
        {
            var outputs = _libVlc.AudioOutputs;
            if (outputs is { Length: > 0 })
            {
                foreach (var output in outputs)
                {
                    if (string.IsNullOrWhiteSpace(output.Name))
                        continue;

                    TryAddDevices(output.Name, devices);
                }
            }
            else
            {
                // Some platforms expose devices only under a known module name.
                TryAddDevices("mmdevice", devices);
                TryAddDevices("wasapi", devices);
                TryAddDevices("directsound", devices);
            }
        }
        catch
        {
            // LibVLC may not be fully initialized or may have been disposed. Fail open.
        }

        return devices;
    }

    private void TryAddDevices(string outputModuleName, List<AudioDeviceInfo> devices)
    {
        try
        {
            var list = _libVlc.AudioOutputDevices(outputModuleName);
            if (list is null)
                return;

            foreach (var device in list)
            {
                var id = device.DeviceIdentifier ?? string.Empty;
                var name = device.Description ?? id;
                devices.Add(new AudioDeviceInfo(id, name, IsDefault: false));
            }
        }
        catch
        {
            // Not every output module supports device enumeration; skip gracefully.
        }
    }

    public string? GetDefaultOutputDeviceId()
    {
        // LibVLC does not expose a reliable cross-platform default-device identifier.
        // Returning the user-preferred device keeps the UI selection coherent.
        // TODO: Detect the actual system default device where possible (e.g. via a
        // platform-specific default-endpoint query on Windows).
        return _preferredDeviceId;
    }

    public void SetPreferredDevice(string? deviceId)
    {
        _preferredDeviceId = deviceId;
        // TODO: Propagate the preferred device to AvaloniaAudioPlayer so playback
        // honors the user's choice (e.g. --aout-device <id> or MediaPlayer output).
    }
}
