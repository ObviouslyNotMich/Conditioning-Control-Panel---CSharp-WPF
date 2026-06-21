namespace ConditioningControlPanel.Core.Services.Webcam;

/// <summary>
/// Cross-platform seam for webcam / gaze tracking.
/// The legacy engine lives in the WPF head under <c>Lab/GazeMinigame</c> and related services.
/// </summary>
public interface IWebcamService
{
    /// <summary>Whether gaze/webcam tracking is currently active.</summary>
    bool IsRunning { get; }

    /// <summary>Start webcam/gaze tracking.</summary>
    void StartTracking();

    /// <summary>Stop webcam/gaze tracking.</summary>
    void StopTracking();

    /// <summary>Run a one-shot calibration routine.</summary>
    void Calibrate();

    /// <summary>Run a tracker self-test.</summary>
    void TestTracker();

    /// <summary>Refresh the list of available capture devices.</summary>
    void RefreshDevices();

    /// <summary>Revoke user consent and stop tracking.</summary>
    void RevokeConsent();
}
