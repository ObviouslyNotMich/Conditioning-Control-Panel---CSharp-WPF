namespace ConditioningControlPanel.Core.Services.BlinkTrainer;

/// <summary>
/// Cross-platform seam for gaze dwell / blink-pop interaction with bubbles and flashes.
/// </summary>
public interface IGazeFocusService
{
    /// <summary>True while gaze processing is active.</summary>
    bool IsActive { get; }

    /// <summary>How long gaze must linger before a dwell pop fires, in milliseconds.</summary>
    int DwellMs { get; set; }

    /// <summary>Starts gaze processing. Returns false if the webcam cannot be started.</summary>
    bool Start();

    /// <summary>Stops gaze processing without stopping the shared webcam.</summary>
    void Stop();

    /// <summary>Fires when <see cref="IsActive"/> flips.</summary>
    event Action<bool>? OnActiveChanged;

    /// <summary>Fires when a bubble is popped by gaze (dwell or blink).</summary>
    event Action? GazePopped;
}
