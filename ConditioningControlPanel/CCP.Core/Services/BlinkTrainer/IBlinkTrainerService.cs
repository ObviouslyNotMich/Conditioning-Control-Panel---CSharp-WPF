namespace ConditioningControlPanel.Core.Services.BlinkTrainer;

/// <summary>
/// Cross-platform seam for the Blink Trainer overlay engine.
/// Displays a full-screen asset on every detected blink while the service is running.
/// </summary>
public interface IBlinkTrainerService
{
    /// <summary>True while the overlay engine is active.</summary>
    bool IsRunning { get; }

    /// <summary>Localized error from the last failed <see cref="Start"/>, or empty.</summary>
    string LastError { get; }

    /// <summary>Time remaining in the current session. Zero when not running.</summary>
    TimeSpan Remaining { get; }

    /// <summary>
    /// Attempts to start the session. Returns false if prerequisites are missing.
    /// </summary>
    bool Start();

    /// <summary>Stops the session and tears down overlays.</summary>
    void Stop();

    /// <summary>Fires when <see cref="IsRunning"/> flips.</summary>
    event Action? StateChanged;
}
