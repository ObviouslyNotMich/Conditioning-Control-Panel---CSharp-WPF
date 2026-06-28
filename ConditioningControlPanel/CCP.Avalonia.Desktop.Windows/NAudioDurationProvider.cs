using System;
using System.IO;
using ConditioningControlPanel.Core.Platform;
using NAudio.Wave;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows;

/// <summary>
/// Windows audio-duration provider (NAudio). Reads mp3/wav clip lengths so the voice layer can wait
/// for the companion to finish speaking before opening the mic. Ported from the WPF
/// MantraVoiceService.GetAudioDuration. Best-effort — returns null on any read failure.
/// </summary>
public sealed class NAudioDurationProvider : IAudioDurationProvider
{
    public TimeSpan? GetDuration(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath)) return null;
        try
        {
            var ext = Path.GetExtension(fullPath).ToLowerInvariant();
            if (ext == ".wav")
            {
                using var r = new WaveFileReader(fullPath);
                return r.TotalTime;
            }
            using var mp3 = new Mp3FileReader(fullPath);
            return mp3.TotalTime;
        }
        catch
        {
            return null;
        }
    }
}
