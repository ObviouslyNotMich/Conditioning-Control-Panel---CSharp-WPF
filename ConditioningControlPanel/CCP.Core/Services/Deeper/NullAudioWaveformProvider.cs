using System;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Core.Services.Deeper;

/// <summary>
/// Fallback waveform provider used on platforms where no decoder is registered.
/// Returns a flat line so the timeline can still render a neutral strip.
/// </summary>
public sealed class NullAudioWaveformProvider : IAudioWaveformProvider
{
    public bool CanDecode(string audioPath) => false;

    public Task<AudioWaveformResult> DecodeAsync(string audioPath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AudioWaveformResult
        {
            Peaks = new float[64],
            DurationSeconds = 0,
        });
    }
}
