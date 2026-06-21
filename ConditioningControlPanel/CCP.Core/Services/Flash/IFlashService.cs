namespace ConditioningControlPanel.Core.Services.Flash;

/// <summary>
/// Cross-platform seam for the flash-image effect engine.
/// The WPF implementation is feature-rich (GIF support, window pool, audio, gaze);
/// the Avalonia head starts with a no-op stub so the feature control can toggle
/// live state without the full engine port blocking the UI.
/// </summary>
public interface IFlashService
{
    /// <summary>Whether the flash engine is currently running.</summary>
    bool IsRunning { get; }

    /// <summary>Starts the flash scheduler/effect engine.</summary>
    void Start();

    /// <summary>Stops the flash scheduler and clears active flashes.</summary>
    void Stop();

    /// <summary>Re-evaluates the display schedule after frequency changes.</summary>
    void RefreshSchedule();

    /// <summary>Refreshes the image search path after asset/mod changes.</summary>
    void RefreshImagesPath();

    /// <summary>Clears the cached file list so the next flash rescans disk.</summary>
    void ClearFileCache();

    /// <summary>Reloads assets and clears caches (used after asset selection changes).</summary>
    void LoadAssets();

    /// <summary>
    /// Fire a single flash image overlay. A null <paramref name="imagePath"/> selects
    /// a random image from the configured pool. Used by the Deeper enhancement engine.
    /// </summary>
    void TriggerFlashOnce(string? imagePath, int durationMs, bool playSound, bool suppressHaptic);
}
