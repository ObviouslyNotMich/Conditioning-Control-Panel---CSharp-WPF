namespace ConditioningControlPanel.Core.Services.Autonomy;

/// <summary>
/// Cross-platform service that enables autonomous companion behavior.
/// The avatar can trigger effects on her own based on idle time, random intervals,
/// context awareness, and time of day.
/// </summary>
public interface IAutonomyService
{
    bool IsEnabled { get; }
    bool IsActionInProgress { get; }
    bool IsIdleTimerRunning { get; }
    bool IsRandomTimerRunning { get; }

    /// <summary>Raised when an autonomous action is selected and about to run.</summary>
    event EventHandler<AutonomyActionEventArgs>? ActionTriggered;

    /// <summary>Raised when the companion makes a verbal announcement.</summary>
    event EventHandler<string>? AnnouncementMade;

    /// <summary>
    /// Starts autonomous mode if requirements are met (Patreon access, consent, enabled).
    /// </summary>
    void Start();

    /// <summary>Stops autonomous mode and cancels active pulses.</summary>
    void Stop();

    /// <summary>Force-starts autonomous mode in test mode (short intervals), bypassing checks.</summary>
    void ForceStart();

    /// <summary>Manually triggers a single autonomous action for testing.</summary>
    void TestTrigger();

    /// <summary>Cancels any active spiral/pink/bubbles/bouncing-text pulses and restores settings.</summary>
    void CancelActivePulses();

    /// <summary>Returns a diagnostic status string for debugging.</summary>
    string GetDiagnosticStatus();
}
