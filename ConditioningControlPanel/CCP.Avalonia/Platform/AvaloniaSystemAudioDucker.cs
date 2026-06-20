using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Cross-platform system audio ducking fallback. Linux uses PulseAudio's pactl;
/// macOS uses AppleScript; other platforms are no-ops.
/// </summary>
public sealed class AvaloniaSystemAudioDucker : ISystemAudioDucker
{
    private const int DuckVolumePercent = 20;

    private int? _originalLinuxVolume;
    private int? _originalMacVolume;
    private string? _linuxSink;

    public void Duck()
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                _linuxSink = GetDefaultPulseAudioSink();
                if (!string.IsNullOrEmpty(_linuxSink))
                {
                    _originalLinuxVolume = GetPulseAudioSinkVolumePercent(_linuxSink);
                    SetPulseAudioSinkVolume(_linuxSink, DuckVolumePercent);
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                _originalMacVolume = GetMacOutputVolume();
                SetMacOutputVolume(DuckVolumePercent);
            }
        }
        catch
        {
            // Best-effort ducking; fail silently on platforms without the expected tooling.
        }
    }

    public void Unduck()
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                if (!string.IsNullOrEmpty(_linuxSink) && _originalLinuxVolume.HasValue)
                {
                    SetPulseAudioSinkVolume(_linuxSink, _originalLinuxVolume.Value);
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                if (_originalMacVolume.HasValue)
                {
                    SetMacOutputVolume(_originalMacVolume.Value);
                }
            }
        }
        catch
        {
            // Best-effort restore.
        }
        finally
        {
            _originalLinuxVolume = null;
            _originalMacVolume = null;
        }
    }

    private static string? GetDefaultPulseAudioSink()
    {
        var output = RunCommand("pactl", "info");
        if (string.IsNullOrEmpty(output)) return null;

        foreach (var line in output.Split('\n'))
        {
            const string prefix = "Default Sink:";
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return line[prefix.Length..].Trim();
            }
        }

        return null;
    }

    private static int? GetPulseAudioSinkVolumePercent(string sinkName)
    {
        var output = RunCommand("pactl", $"list sinks");
        if (string.IsNullOrEmpty(output)) return null;

        bool inTargetSink = false;
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();

            if (line.StartsWith("Name:", StringComparison.OrdinalIgnoreCase))
            {
                inTargetSink = line.Equals($"Name: {sinkName}", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (inTargetSink && line.StartsWith("Volume:", StringComparison.OrdinalIgnoreCase))
            {
                // Volume: front-left: 65536 / 100% / -0.00 dB ...
                var match = Regex.Match(line, @"(\d+(?:\.\d+)?)%");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var percent))
                {
                    return Math.Clamp(percent, 0, 150);
                }
            }
        }

        return null;
    }

    private static void SetPulseAudioSinkVolume(string sinkName, int percent)
    {
        RunCommand("pactl", $"set-sink-volume \"{sinkName}\" {percent}%");
    }

    private static int? GetMacOutputVolume()
    {
        var output = RunCommand("osascript", "-e \"output volume of (get volume settings)\"");
        if (int.TryParse(output?.Trim(), out var volume))
        {
            return Math.Clamp(volume, 0, 100);
        }
        return null;
    }

    private static void SetMacOutputVolume(int percent)
    {
        RunCommand("osascript", $"-e \"set volume output volume {Math.Clamp(percent, 0, 100)}\"");
    }

    private static string? RunCommand(string fileName, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
