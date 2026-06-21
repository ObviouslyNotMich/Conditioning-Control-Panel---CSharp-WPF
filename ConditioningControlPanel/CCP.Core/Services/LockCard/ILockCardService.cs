using System;

namespace ConditioningControlPanel.Core.Services.LockCard;

/// <summary>
/// Cross-platform seam for the lock-card subsystem.
/// The legacy WPF implementation lives in <c>Services/LockCard/LockCardService.cs</c>.
/// </summary>
public interface ILockCardService
{
    /// <summary>Whether the lock-card timer/service is currently running.</summary>
    bool IsRunning { get; }

    /// <summary>
    /// Raised when the user finishes typing all repeats of a real (non-test) lock card.
    /// Subscribers like the avatar use this to trigger AI reactions.
    /// </summary>
    event EventHandler<LockCardCompletedEventArgs>? LockCardCompleted;

    /// <summary>Start the periodic lock-card timer if enabled in settings.</summary>
    void Start();

    /// <summary>Stop the lock-card timer.</summary>
    void Stop();

    /// <summary>Show a lock card now, optionally with overrides.</summary>
    void ShowLockCard(string? customPhrase = null, int customRepeats = -1, bool customStrict = false, bool isTest = false);

    /// <summary>Manually trigger a test lock card.</summary>
    void TestLockCard();

    /// <summary>Notify subscribers that a lock card was completed.</summary>
    void NotifyCompleted(string phrase, int totalErrors, int requiredRepeats);
}

/// <summary>Data for <see cref="ILockCardService.LockCardCompleted"/>.</summary>
public sealed class LockCardCompletedEventArgs : EventArgs
{
    public string Phrase { get; init; } = "";
    public int Mistakes { get; init; }
    public int Repeats { get; init; }
}
