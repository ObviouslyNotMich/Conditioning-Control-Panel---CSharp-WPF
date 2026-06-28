using System;

namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Reads the duration of an audio clip (mp3/wav) on disk. Used so the voice layer can wait for the
/// companion to finish speaking a clip before opening the mic (otherwise the recognizer hears her own
/// delivery). Windows implements this via NAudio; heads without an audio backend return null and the
/// caller falls back to a fixed delay.
/// </summary>
public interface IAudioDurationProvider
{
    /// <summary>Best-effort clip duration, or null if unknown/unreadable.</summary>
    TimeSpan? GetDuration(string? fullPath);
}

/// <summary>Default no-op: duration unknown. Overridden by the Windows head (NAudio).</summary>
public sealed class NullAudioDurationProvider : IAudioDurationProvider
{
    public TimeSpan? GetDuration(string? fullPath) => null;
}
