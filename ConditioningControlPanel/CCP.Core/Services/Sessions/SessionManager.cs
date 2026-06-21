using System.Collections.ObjectModel;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Core.Services.Sessions;

/// <summary>
/// Cross-platform session manager.
/// </summary>
public sealed class SessionManager : ISessionManager
{
    private readonly SessionFileService _fileService;
    private readonly ISessionPlatformBridge? _platformBridge;
    private readonly List<Session> _sessions = new();

    public ObservableCollection<Session> AllSessions { get; } = new();

    public IEnumerable<Session> BuiltInSessions => _sessions.Where(s => s.Source == SessionSource.BuiltIn);

    public IEnumerable<Session> CustomSessions => _sessions.Where(s => s.Source != SessionSource.BuiltIn);

    public event Action<Session>? SessionAdded;
    public event Action<Session>? SessionRemoved;
    public event Action? SessionsReloaded;

    public SessionManager(SessionFileService fileService, ISessionPlatformBridge? platformBridge = null)
    {
        _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
        _platformBridge = platformBridge;
    }

    public void LoadAllSessions()
    {
        _sessions.Clear();
        AllSessions.Clear();

        var builtInFromFiles = _fileService.LoadBuiltInSessions();
        foreach (var def in builtInFromFiles)
        {
            _sessions.Add(def.ToSession());
        }

        if (!builtInFromFiles.Any())
        {
            var hardcodedSessions = Session.GetAllSessions();
            foreach (var session in hardcodedSessions)
            {
                session.Source = SessionSource.BuiltIn;
                _sessions.Add(session);
            }
        }

        var customSessions = _fileService.LoadCustomSessions();
        foreach (var def in customSessions)
        {
            var session = def.ToSession();
            Log.Debug("Loaded custom session: {Name}, BonusXP={XP}, Source={Source}",
                session.Name, session.BonusXP, session.Source);
            _sessions.Add(session);
        }

        foreach (var session in _sessions)
        {
            AllSessions.Add(session);
        }

        SessionsReloaded?.Invoke();
    }

    public (bool success, string message, Session? session) ImportSession(string filePath)
    {
        if (!_fileService.ValidateSessionFile(filePath, out var errorMessage))
        {
            return (false, errorMessage, null);
        }

        var definition = _fileService.ImportSession(filePath);
        if (definition == null)
        {
            return (false, "Failed to import session", null);
        }

        if (_sessions.Any(s => s.Id == definition.Id))
        {
            var baseId = definition.Id;
            var counter = 1;
            while (_sessions.Any(s => s.Id == definition.Id))
            {
                definition.Id = $"{baseId}_{counter}";
                counter++;
            }
        }

        var savedPath = _fileService.CopyToCustomSessions(filePath, definition);
        definition.SourceFilePath = savedPath;
        definition.Source = SessionSource.Custom;

        var session = definition.ToSession();
        _sessions.Add(session);
        AllSessions.Add(session);

        SessionAdded?.Invoke(session);

        return (true, $"Imported '{session.Name}'", session);
    }

    public void ExportSession(Session session, string filePath)
    {
        _fileService.ExportSession(session, filePath);
    }

    public void UpdateCustomSession(Session updatedSession)
    {
        var definition = SessionDefinition.FromSession(updatedSession);
        _fileService.SaveCustomSession(definition);

        var existingSession = _sessions.FirstOrDefault(s => s.Id == updatedSession.Id);
        if (existingSession != null)
        {
            _sessions.Remove(existingSession);
            AllSessions.Remove(existingSession);
        }

        _sessions.Add(updatedSession);
        AllSessions.Add(updatedSession);

        if (existingSession != null)
            SessionRemoved?.Invoke(existingSession);

        SessionAdded?.Invoke(updatedSession);
    }

    public void AddNewSession(Session session, string filePath)
    {
        _fileService.EnsureCustomFolderExists();
        session.Source = SessionSource.Custom;
        session.SourceFilePath = filePath;

        if (_sessions.Any(s => s.Id == session.Id))
        {
            session.Id = Guid.NewGuid().ToString();
        }

        var definition = SessionDefinition.FromSession(session);
        _fileService.ExportSession(definition, filePath);

        _sessions.Add(session);
        AllSessions.Add(session);

        SessionAdded?.Invoke(session);
    }

    public bool DeleteSession(Session session)
    {
        if (session.Source == SessionSource.BuiltIn)
            return false;

        if (!string.IsNullOrEmpty(session.SourceFilePath))
        {
            _fileService.DeleteCustomSession(session.SourceFilePath);
        }

        _sessions.Remove(session);
        AllSessions.Remove(session);

        SessionRemoved?.Invoke(session);

        return true;
    }

    public Session? GetSession(string id)
    {
        return _sessions.FirstOrDefault(s => s.Id == id);
    }

    public bool CanDelete(Session session)
    {
        return session.Source != SessionSource.BuiltIn;
    }

    public void OpenCustomSessionsFolder()
    {
        _fileService.EnsureCustomFolderExists();
        _platformBridge?.OpenCustomSessionsFolder(_fileService.CustomSessionsFolder);
    }

    public string GetExportFileName(Session session)
    {
        return SessionFileService.GetExportFileName(session);
    }
}
