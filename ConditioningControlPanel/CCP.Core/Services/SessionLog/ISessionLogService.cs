using ConditioningControlPanel.Models;
using SessionLogModel = ConditioningControlPanel.Models.SessionLog;

namespace ConditioningControlPanel.Core.Services.SessionLog;

/// <summary>
/// Cross-platform session media logger.
/// Tracks every flash image and mandatory video displayed during an active session,
/// persists the result to disk, and raises <see cref="LogReady"/> when a session ends.
/// </summary>
public interface ISessionLogService
{
    /// <summary>
    /// Raised when the active session log is finalized.
    /// </summary>
    event EventHandler<SessionLogReadyEventArgs>? LogReady;

    /// <summary>
    /// Begin tracking media for a session.
    /// </summary>
    void BeginSession(Session session);

    /// <summary>
    /// Finalize the active session log, persist it to disk, and raise <see cref="LogReady"/>.
    /// Safe to call even if <see cref="BeginSession"/> was never called.
    /// </summary>
    void EndSession(bool completed, TimeSpan duration, int xpEarned);

    /// <summary>
    /// Load the most recent persisted logs, newest first.
    /// </summary>
    IReadOnlyList<SessionLogModel> LoadRecentLogs();
}

/// <summary>
/// Event args raised by <see cref="ISessionLogService.LogReady"/>.
/// </summary>
public sealed class SessionLogReadyEventArgs : EventArgs
{
    public SessionLogModel Log { get; }

    public SessionLogReadyEventArgs(SessionLogModel log)
    {
        Log = log ?? throw new ArgumentNullException(nameof(log));
    }
}
