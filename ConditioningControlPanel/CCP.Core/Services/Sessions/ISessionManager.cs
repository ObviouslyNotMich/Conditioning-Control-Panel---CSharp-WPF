using System.Collections.ObjectModel;
using ConditioningControlPanel.Core.Models;

namespace ConditioningControlPanel.Core.Services.Sessions;

/// <summary>
/// Cross-platform manager for built-in and custom sessions.
/// </summary>
public interface ISessionManager
{
    ObservableCollection<Session> AllSessions { get; }
    IEnumerable<Session> BuiltInSessions { get; }
    IEnumerable<Session> CustomSessions { get; }

    event Action<Session>? SessionAdded;
    event Action<Session>? SessionRemoved;
    event Action? SessionsReloaded;

    void LoadAllSessions();
    (bool success, string message, Session? session) ImportSession(string filePath);
    void ExportSession(Session session, string filePath);
    void UpdateCustomSession(Session updatedSession);
    void AddNewSession(Session session, string filePath);
    bool DeleteSession(Session session);
    Session? GetSession(string id);
    bool CanDelete(Session session);
    void OpenCustomSessionsFolder();
    string GetExportFileName(Session session);
}
