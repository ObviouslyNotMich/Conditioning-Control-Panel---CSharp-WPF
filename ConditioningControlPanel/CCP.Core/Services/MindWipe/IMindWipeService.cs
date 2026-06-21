namespace ConditioningControlPanel.Core.Services.MindWipe;

/// <summary>
/// Cross-platform seam for the mind-wipe audio effect engine.
/// The WPF implementation plays layered wipe phrases on a schedule or in a loop;
/// the Avalonia head begins with a no-op stub so the feature control can toggle
/// live state without the full engine port blocking the UI.
/// </summary>
public interface IMindWipeService
{
    /// <summary>Whether the mind-wipe scheduler/loop is currently running.</summary>
    bool IsRunning { get; }

    /// <summary>Starts scheduled mind-wipe playback.</summary>
    void Start(double frequencyPerHour, double volume);

    /// <summary>Stops scheduled or looped mind-wipe playback.</summary>
    void Stop();

    /// <summary>Starts continuous loop playback at the given volume.</summary>
    void StartLoop(double volume);

    /// <summary>Stops continuous loop playback.</summary>
    void StopLoop();

    /// <summary>Triggers a single mind-wipe phrase immediately.</summary>
    void TriggerOnce();
}
