namespace ConditioningControlPanel.Core.Services.Video;

/// <summary>
/// Cross-platform seam for fullscreen video playback mirrored across all connected monitors
/// using a single decoded stream. This is used for one-off URL/file playback that should fill
/// every screen (e.g. browser/remote/autonomy fullscreen video flows), distinct from the
/// scheduled mandatory <see cref="IVideoService"/> engine.
/// </summary>
public interface IMultiMonitorVideoService
{
    /// <summary>Whether a multi-monitor fullscreen video is currently playing.</summary>
    bool IsPlaying { get; }

    /// <summary>Play a direct video URL on all monitors simultaneously.</summary>
    /// <param name="url">Direct media URL (mp4, webm, m3u8, etc.).</param>
    void PlayUrl(string url);

    /// <summary>Play a local video file on all monitors simultaneously.</summary>
    /// <param name="filePath">Path to the local video file.</param>
    void PlayFile(string filePath);

    /// <summary>Stop playback and close all fullscreen windows.</summary>
    void Stop();

    /// <summary>Set the playback volume (0-100).</summary>
    void SetVolume(int volume);

    /// <summary>Route audio to the given LibVLC output device, or default if null/empty.</summary>
    void SetAudioOutputDevice(string? deviceId);

    /// <summary>Raised when playback has started.</summary>
    event EventHandler? PlaybackStarted;

    /// <summary>Raised when playback has ended.</summary>
    event EventHandler? PlaybackEnded;

    /// <summary>Raised when playback encounters an error.</summary>
    event EventHandler<string>? PlaybackError;
}
