using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Flash;
using ConditioningControlPanel.Core.Services.Video;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using SessionLogModel = ConditioningControlPanel.Models.SessionLog;

namespace ConditioningControlPanel.Core.Services.SessionLog;

/// <summary>
/// Cross-platform implementation of <see cref="ISessionLogService"/>.
/// Captures flash images and videos shown during a session, persists the log to disk
/// (capped at <see cref="MaxRetainedLogs"/>), and raises <see cref="LogReady"/> on completion.
/// </summary>
public sealed class SessionLogService : ISessionLogService, IDisposable
{
    public const int MaxRetainedLogs = 20;

    // Sessions shorter than this with no media are not persisted.
    private static readonly TimeSpan PersistenceMinDuration = TimeSpan.FromSeconds(30);

    private readonly IAppEnvironment _environment;
    private readonly ILogger<SessionLogService>? _logger;
    private readonly IFlashService? _flash;
    private readonly IVideoService? _video;
    private readonly object _lock = new();

    private SessionLogModel? _activeLog;
    private DateTime _sessionStart;
    private bool _isSubscribed;

    public string LogsFolder { get; }

    public event EventHandler<SessionLogReadyEventArgs>? LogReady;

    public SessionLogService(IAppEnvironment environment, ILogger<SessionLogService>? logger = null, IFlashService? flash = null, IVideoService? video = null)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger;
        _flash = flash;
        _video = video;

        LogsFolder = Path.Combine(_environment.ApplicationDataPath, "session_logs");
        try { Directory.CreateDirectory(LogsFolder); }
        catch (Exception ex) { _logger?.LogWarning(ex, "SessionLogService: failed to create logs folder"); }
    }

    /// <inheritdoc />
    public void BeginSession(Session session)
    {
        if (session == null) return;

        lock (_lock)
        {
            if (_activeLog != null)
            {
                _logger?.LogWarning("SessionLogService.BeginSession called while a log was already active; discarding previous log");
                UnsubscribeUnlocked();
            }

            _sessionStart = DateTime.Now;
            _activeLog = new SessionLogModel
            {
                SessionId = session.Id,
                SessionName = session.Name,
                SessionIcon = session.Icon,
                SessionDifficulty = session.Difficulty,
                StartedAt = _sessionStart,
            };

            SubscribeUnlocked();
        }
    }

    /// <inheritdoc />
    public void EndSession(bool completed, TimeSpan duration, int xpEarned)
    {
        SessionLogModel? log;
        lock (_lock)
        {
            if (_activeLog == null) return;

            UnsubscribeUnlocked();

            log = _activeLog;
            log.EndedAt = DateTime.Now;
            log.Duration = duration;
            log.Completed = completed;
            log.XPEarned = xpEarned;
            _activeLog = null;
        }

        bool persist = log.Media.Count > 0 || duration >= PersistenceMinDuration;
        if (persist)
        {
            TryPersist(log);
            TryPrune();
        }

        try { LogReady?.Invoke(this, new SessionLogReadyEventArgs(log)); }
        catch (Exception ex) { _logger?.LogError(ex, "SessionLogService: LogReady handler threw"); }
    }

    /// <inheritdoc />
    public IReadOnlyList<SessionLogModel> LoadRecentLogs()
    {
        var result = new List<SessionLogModel>();
        if (!Directory.Exists(LogsFolder)) return result;

        string[] files;
        try { files = Directory.GetFiles(LogsFolder, "*.json"); }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "SessionLogService: failed to enumerate logs folder");
            return result;
        }

        Array.Sort(files);
        Array.Reverse(files);

        foreach (var file in files.Take(MaxRetainedLogs))
        {
            try
            {
                var json = File.ReadAllText(file);
                var log = JsonConvert.DeserializeObject<SessionLogModel>(json);
                if (log != null) result.Add(log);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "SessionLogService: failed to read log file {File}", file);
            }
        }
        return result;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            UnsubscribeUnlocked();
            _activeLog = null;
        }
    }

    private void SubscribeUnlocked()
    {
        if (_isSubscribed) return;
        try
        {
            if (_flash != null) _flash.FlashDisplayed += OnFlashDisplayed;
            if (_video != null) _video.VideoStarted += OnVideoStarted;
            _isSubscribed = true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "SessionLogService: subscribe failed");
        }
    }

    private void UnsubscribeUnlocked()
    {
        if (!_isSubscribed) return;
        try { if (_flash != null) _flash.FlashDisplayed -= OnFlashDisplayed; } catch { }
        try { if (_video != null) _video.VideoStarted -= OnVideoStarted; } catch { }
        _isSubscribed = false;
    }

    private void OnFlashDisplayed(object? sender, EventArgs e)
    {
        try
        {
            var paths = _flash?.LastDisplayedImagePaths;
            if (paths == null || paths.Count == 0) return;

            lock (_lock)
            {
                if (_activeLog == null) return;
                var now = DateTime.Now;
                var offset = now - _sessionStart;
                foreach (var path in paths)
                {
                    if (string.IsNullOrEmpty(path)) continue;
                    _activeLog.Media.Add(new MediaLogEntry
                    {
                        Timestamp = now,
                        SessionTime = offset,
                        Type = MediaType.Image,
                        FilePath = path,
                        DisplayName = SafeFileName(path),
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("SessionLogService.OnFlashDisplayed failed: {Error}", ex.Message);
        }
    }

    private void OnVideoStarted(object? sender, EventArgs e)
    {
        try
        {
            var path = _video?.LastVideoPath;
            if (string.IsNullOrEmpty(path)) return;

            lock (_lock)
            {
                if (_activeLog == null) return;
                var now = DateTime.Now;
                _activeLog.Media.Add(new MediaLogEntry
                {
                    Timestamp = now,
                    SessionTime = now - _sessionStart,
                    Type = MediaType.Video,
                    FilePath = path,
                    DisplayName = SafeFileName(path),
                });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("SessionLogService.OnVideoStarted failed: {Error}", ex.Message);
        }
    }

    private static string SafeFileName(string path)
    {
        try { return Path.GetFileName(path) ?? ""; }
        catch { return ""; }
    }

    private void TryPersist(SessionLogModel log)
    {
        try
        {
            var fileName = $"{log.StartedAt:yyyyMMdd_HHmmss}_{SanitizeId(log.SessionId)}.json";
            var path = Path.Combine(LogsFolder, fileName);
            var json = JsonConvert.SerializeObject(log, Formatting.Indented);
            File.WriteAllText(path, json);
            _logger?.LogDebug("SessionLogService: persisted {File} ({Count} media entries)", fileName, log.Media.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "SessionLogService: failed to persist session log");
        }
    }

    private void TryPrune()
    {
        try
        {
            if (!Directory.Exists(LogsFolder)) return;
            var files = Directory.GetFiles(LogsFolder, "*.json");
            if (files.Length <= MaxRetainedLogs) return;

            Array.Sort(files);
            Array.Reverse(files);
            for (int i = MaxRetainedLogs; i < files.Length; i++)
            {
                try { File.Delete(files[i]); }
                catch (Exception ex) { _logger?.LogDebug("SessionLogService: prune delete failed for {File}: {Error}", files[i], ex.Message); }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "SessionLogService: prune failed");
        }
    }

    private static string SanitizeId(string id)
    {
        if (string.IsNullOrEmpty(id)) return "session";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = id.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = '_';
        }
        return new string(chars);
    }
}
