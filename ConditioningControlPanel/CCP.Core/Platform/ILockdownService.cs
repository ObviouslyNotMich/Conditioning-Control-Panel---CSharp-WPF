namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Cross-platform lockdown service: a timed session that forces strict lock
/// and disables the panic key until the timer expires or the secret phrase is entered.
/// </summary>
public interface ILockdownService : IDisposable
{
    /// <summary>
    /// True when a lockdown session is currently active.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Time remaining in the active lockdown session; zero when inactive.
    /// </summary>
    TimeSpan Remaining { get; }

    /// <summary>
    /// Duration of the most recently completed lockdown session.
    /// </summary>
    TimeSpan LastActiveDuration { get; }

    /// <summary>
    /// Raised when a lockdown session starts.
    /// </summary>
    event Action? LockdownActivated;

    /// <summary>
    /// Raised when a lockdown session ends (timer expiry, phrase exit, or manual abort).
    /// </summary>
    event Action? LockdownDeactivated;

    /// <summary>
    /// Raised once per second while a lockdown session is active.
    /// </summary>
    event Action<TimeSpan>? CountdownTick;

    /// <summary>
    /// Starts a lockdown session for the requested duration.
    /// </summary>
    void Activate(TimeSpan duration);

    /// <summary>
    /// Ends the active lockdown session and restores previous settings.
    /// </summary>
    void Deactivate();

    /// <summary>
    /// Attempts to exit the active session using the supplied phrase.
    /// Returns true if the phrase matched and the session was ended.
    /// </summary>
    bool TryExitWithPhrase(string phrase);

    /// <summary>
    /// Restores an interrupted lockdown session from disk if a recovery file exists.
    /// </summary>
    void RecoverIfNeeded();
}
