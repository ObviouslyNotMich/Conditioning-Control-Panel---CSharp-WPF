namespace ConditioningControlPanel.Core.Services.Video;

/// <summary>
/// Cross-platform seam for the mandatory-video effect engine.
/// The WPF implementation schedules and plays full-screen attention-check videos;
/// the Avalonia head begins with a no-op stub so the feature control can toggle
/// live state without the full engine port blocking the UI.
/// </summary>
public interface IVideoService
{
    /// <summary>Whether the mandatory video scheduler is currently running.</summary>
    bool IsRunning { get; }

    /// <summary>Starts the mandatory video scheduler.</summary>
    void Start();

    /// <summary>Stops the scheduler and closes active video windows.</summary>
    void Stop();

    /// <summary>Refreshes the video search path after asset/mod changes.</summary>
    void RefreshVideosPath();

    /// <summary>Immediately plays the specified video file in strict mode.</summary>
    void PlaySpecificVideo(string videoPath, bool strictMode);

    /// <summary>Immediately plays a video from a URL.</summary>
    void PlayUrl(string url);

    /// <summary>
    /// The file path of the video most recently started by the scheduler.
    /// Used by the session log to record media played during a session.
    /// </summary>
    string? LastVideoPath { get; }

    /// <summary>Raised when a video is about to start playing.</summary>
    event EventHandler? VideoAboutToStart;

    /// <summary>Raised when a video has started playing.</summary>
    event EventHandler? VideoStarted;

    /// <summary>Raised when a video has finished playing.</summary>
    event EventHandler? VideoEnded;
}
