using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Core.Services.Deeper;

/// <summary>
/// Platform-specific decoder that produces a peak-bucket summary for an audio file.
/// Implementations may use NAudio (Windows desktop), FFmpeg, or any other cross-platform
/// decoder available to the head.
/// </summary>
public interface IAudioWaveformProvider
{
    /// <summary>
    /// Returns true when the provider can decode the supplied file path.
    /// Callers can use this to decide whether to attempt decoding.
    /// </summary>
    bool CanDecode(string audioPath);

    /// <summary>
    /// Decodes the audio file and returns peak samples. Throws on decode failure.
    /// </summary>
    Task<AudioWaveformResult> DecodeAsync(string audioPath, CancellationToken cancellationToken = default);
}
