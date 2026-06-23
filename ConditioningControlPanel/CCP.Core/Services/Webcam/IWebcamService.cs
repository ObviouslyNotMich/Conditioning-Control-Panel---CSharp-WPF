using ConditioningControlPanel.Core.Platform;

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

    /// <summary>
    /// Returns the monitor the current calibration was performed on, or null when
    /// no calibration is loaded or the calibrated monitor is no longer connected.
    /// Implementations that do not support calibration should return null.
    /// </summary>
    ScreenInfo? GetCalibratedScreen() => null;

    // ---- Events consumed by the Deeper enhancement engine ----

    /// <summary>Fires when a blink is detected (already marshalled to the UI thread by the provider).</summary>
    event Action? OnBlink;

    /// <summary>Fires when the mouth-open gesture is detected.</summary>
    event Action? OnMouthOpen;

    /// <summary>Fires when gaze moves; argument is in physical screen pixels.</summary>
    event Action<Point>? OnGazeMove;

    /// <summary>Fires when a tracked face is lost.</summary>
    event Action? OnFaceLost;

    /// <summary>Fires when a tracked face is found again.</summary>
    event Action? OnFaceFound;
}
