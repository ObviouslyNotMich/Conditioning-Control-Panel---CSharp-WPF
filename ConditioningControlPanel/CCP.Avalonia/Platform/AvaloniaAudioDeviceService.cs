using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

    public event EventHandler? PreferredDeviceChanged;

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
        // Prefer the user's explicit selection first.
        if (!string.IsNullOrEmpty(_preferredDeviceId))
            return _preferredDeviceId;

        // The Windows head replaces this service with WindowsAudioDeviceService (NAudio),
        // so the shared fallback is for Linux / macOS / mobile.
        if (OperatingSystem.IsLinux())
        {
            var defaultSink = TryGetPulseAudioDefaultSink();
            if (!string.IsNullOrEmpty(defaultSink))
                return defaultSink;
        }

        if (OperatingSystem.IsMacOS())
        {
            var defaultDevice = TryGetMacDefaultOutputDevice();
            if (!string.IsNullOrEmpty(defaultDevice))
                return defaultDevice;
        }

        // Last resort: use the first enumerated device so playback still targets
        // a concrete endpoint instead of leaving the decision ambiguous.
        try
        {
            return GetOutputDevices().FirstOrDefault(static d => !string.IsNullOrEmpty(d.Id)).Id;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetPulseAudioDefaultSink()
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "pactl",
                Arguments = "info",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (proc is null) return null;
            string? line;
            while ((line = proc.StandardOutput.ReadLine()) != null)
            {
                const string prefix = "Default Sink:";
                if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var sink = line.Substring(prefix.Length).Trim();
                    return string.IsNullOrEmpty(sink) ? null : sink;
                }
            }
            proc.WaitForExit();
        }
        catch
        {
            // pactl may not be installed; fail open.
        }
        return null;
    }

    private static string? TryGetMacDefaultOutputDevice()
    {
        // SwitchAudioSource is commonly available on macOS via Homebrew.
        // It returns the user-visible name, which is the best we can do without
        // CoreAudio bindings; LibVLC may or may not accept it as an output device.
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "SwitchAudioSource",
                Arguments = "-c",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (proc is null) return null;
            var name = proc.StandardOutput.ReadLine()?.Trim();
            proc.WaitForExit();
            return string.IsNullOrEmpty(name) ? null : name;
        }
        catch
        {
            return null;
        }
    }

    public void SetPreferredDevice(string? deviceId)
    {
        _preferredDeviceId = deviceId;
        PreferredDeviceChanged?.Invoke(this, EventArgs.Empty);
    }
}
