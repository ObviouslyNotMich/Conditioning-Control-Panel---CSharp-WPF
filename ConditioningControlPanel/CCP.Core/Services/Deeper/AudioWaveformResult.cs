namespace ConditioningControlPanel.Core.Services.Deeper;

/// <summary>
/// Peak-bucket summary for an audio file, suitable for timeline rendering.
/// </summary>
public sealed class AudioWaveformResult
{
    public float[] Peaks { get; set; } = System.Array.Empty<float>();
    public double DurationSeconds { get; set; }
}
