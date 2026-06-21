using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Services.Deeper;
using NAudio.Wave;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows.Services;

/// <summary>
/// Windows desktop waveform decoder backed by NAudio.
/// </summary>
public sealed class NAudioWaveformProvider : IAudioWaveformProvider
{
    public bool CanDecode(string audioPath)
    {
        if (string.IsNullOrWhiteSpace(audioPath) || !File.Exists(audioPath))
            return false;

        var ext = Path.GetExtension(audioPath).ToLowerInvariant();
        return ext is ".mp3" or ".wav" or ".ogg" or ".flac" or ".m4a" or ".aac" or ".wma";
    }

    public Task<AudioWaveformResult> DecodeAsync(string audioPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Decode(audioPath), cancellationToken);
    }

    private static AudioWaveformResult Decode(string audioPath)
    {
        using var reader = new MediaFoundationReader(audioPath);
        var channels = reader.WaveFormat.Channels;
        var sampleRate = reader.WaveFormat.SampleRate;
        var durationSec = reader.TotalTime.TotalSeconds;

        int peakCount = Math.Clamp((int)(durationSec * 6), 64, 4096);
        var peaks = new float[peakCount];

        if (durationSec <= 0 || sampleRate <= 0 || channels <= 0)
            return new AudioWaveformResult { Peaks = peaks, DurationSeconds = durationSec };

        double framesPerBucket = (durationSec * sampleRate) / peakCount;
        if (framesPerBucket <= 0) framesPerBucket = 1;

        var sp = reader.ToSampleProvider();
        var buf = new float[8192];
        int currentBucket = 0;
        double frameInBucket = 0;
        float currentMax = 0;

        while (currentBucket < peakCount)
        {
            int read = sp.Read(buf, 0, buf.Length);
            if (read == 0) break;
            int frames = read / channels;
            for (int f = 0; f < frames; f++)
            {
                float frameMax = 0;
                int baseIx = f * channels;
                for (int c = 0; c < channels; c++)
                {
                    var v = Math.Abs(buf[baseIx + c]);
                    if (v > frameMax) frameMax = v;
                }
                if (frameMax > currentMax) currentMax = frameMax;
                frameInBucket++;
                if (frameInBucket >= framesPerBucket)
                {
                    peaks[currentBucket++] = currentMax;
                    currentMax = 0;
                    frameInBucket -= framesPerBucket;
                    if (currentBucket >= peakCount) break;
                }
            }
        }
        while (currentBucket < peakCount) peaks[currentBucket++] = currentMax;

        return new AudioWaveformResult { Peaks = peaks, DurationSeconds = durationSec };
    }
}
