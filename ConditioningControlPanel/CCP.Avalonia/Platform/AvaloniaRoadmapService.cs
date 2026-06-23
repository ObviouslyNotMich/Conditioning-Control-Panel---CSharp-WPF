using System;
using System.IO;
using System.Text.Json;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Core.Services.Roadmap;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Avalonia roadmap service. Mirrors the WPF <c>RoadmapService</c> behavior:
/// loads/saves <c>roadmap.json</c> under LocalApplicationData, auto-saves every
/// 30 seconds when dirty, and persists immediately on note updates.
/// </summary>
public sealed class AvaloniaRoadmapService : IRoadmapService, IDisposable
{
    private readonly string _progressPath;
    private readonly ILogger<AvaloniaRoadmapService>? _logger;
    private readonly DispatcherTimer? _saveTimer;
    private bool _isDirty;
    private bool _disposed;

    private RoadmapProgress _progress;

    public RoadmapProgress Progress => _progress;

    public event EventHandler<RoadmapStepCompletedEventArgs>? StepCompleted;
    public event EventHandler<RoadmapTrack>? TrackUnlocked;

    public AvaloniaRoadmapService(ILogger<AvaloniaRoadmapService>? logger = null)
    {
        _logger = logger;
        _progressPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConditioningControlPanel",
            "roadmap.json");

        _progress = LoadProgress();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _saveTimer.Tick += (_, _) => SaveIfDirty();
        _saveTimer.Start();

        _logger?.LogInformation("AvaloniaRoadmapService initialized. Track1: {T1}, Track2: {T2}, Track3: {T3}",
            _progress.Track1Unlocked, _progress.Track2Unlocked, _progress.Track3Unlocked);
    }

    public bool IsTrackUnlocked(RoadmapTrack track) => _progress.IsTrackUnlocked(track);

    public bool IsStepCompleted(string stepId) => _progress.IsStepCompleted(stepId);

    public bool IsStepActive(string stepId)
    {
        var step = RoadmapStepDefinition.GetById(stepId);
        if (step == null) return false;
        if (!IsTrackUnlocked(step.Track)) return false;
        if (_progress.IsStepCompleted(stepId)) return false;

        var active = step.Track switch
        {
            RoadmapTrack.EmptyDoll => _progress.ActiveTrack1Step,
            RoadmapTrack.ObedientPuppet => _progress.ActiveTrack2Step,
            RoadmapTrack.SluttyBlowdoll => _progress.ActiveTrack3Step,
            _ => null
        };
        return active == stepId;
    }

    public RoadmapStepProgress? GetStepProgress(string stepId)
        => _progress.GetStepProgress(stepId);

    public (int completed, int total) GetTrackProgress(RoadmapTrack track)
        => _progress.GetTrackStats(track);

    public string? GetFullPhotoPath(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return null;
        if (Path.IsPathRooted(relativePath)) return relativePath;
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConditioningControlPanel", "Roadmap", relativePath);
    }

    public void StartStep(string stepId)
    {
        if (!_progress.CompletedSteps.TryGetValue(stepId, out var progress))
        {
            progress = new RoadmapStepProgress(stepId) { StartedAt = DateTime.UtcNow };
            _progress.CompletedSteps[stepId] = progress;
        }
        else if (progress.StartedAt == null)
        {
            progress.StartedAt = DateTime.UtcNow;
        }

        if (_progress.JourneyStartedAt == null) _progress.JourneyStartedAt = DateTime.UtcNow;
        _isDirty = true;
    }

    public void SubmitPhoto(string stepId, string photoPath, string? note)
    {
        var step = RoadmapStepDefinition.GetById(stepId);
        if (step == null) return;

        if (!_progress.CompletedSteps.TryGetValue(stepId, out var progress))
        {
            progress = new RoadmapStepProgress(stepId);
            _progress.CompletedSteps[stepId] = progress;
        }

        progress.IsCompleted = true;
        progress.CompletedAt = DateTime.UtcNow;
        progress.PhotoPath = photoPath;
        progress.UserNote = note;
        progress.TimeToCompleteMinutes = progress.StartedAt.HasValue
            ? (int)(DateTime.UtcNow - progress.StartedAt.Value).TotalMinutes
            : 0;

        _progress.TotalStepsCompleted++;
        _progress.TotalPhotosSubmitted++;
        if (_progress.JourneyStartedAt == null) _progress.JourneyStartedAt = DateTime.UtcNow;

        // Advance active step for this track
        var steps = RoadmapStepDefinition.GetStepsForTrack(step.Track);
        var next = steps.FirstOrDefault(s => s.StepNumber > step.StepNumber);
        _progress.SetActiveStep(step.Track, next?.Id);

        var unlockedNewTrack = false;
        if (step.StepType == RoadmapStepType.Boss)
        {
            var nextTrack = step.Track switch
            {
                RoadmapTrack.EmptyDoll => RoadmapTrack.ObedientPuppet,
                RoadmapTrack.ObedientPuppet => RoadmapTrack.SluttyBlowdoll,
                _ => (RoadmapTrack?)null
            };
            if (nextTrack.HasValue && !_progress.IsTrackUnlocked(nextTrack.Value))
            {
                _progress.UnlockTrack(nextTrack.Value);
                unlockedNewTrack = true;
                TrackUnlocked?.Invoke(this, nextTrack.Value);
            }
        }

        var earnedBadge = step.Track == RoadmapTrack.SluttyBlowdoll && step.StepType == RoadmapStepType.Boss;
        if (earnedBadge) _progress.HasCertifiedBlowdollBadge = true;

        StepCompleted?.Invoke(this, new RoadmapStepCompletedEventArgs(step, progress, unlockedNewTrack, earnedBadge));
        _isDirty = true;
        Save();
    }

    public void UpdateStepNote(string stepId, string? note)
    {
        if (_progress.CompletedSteps.TryGetValue(stepId, out var progress))
        {
            progress.UserNote = note;
            _isDirty = true;
            Save();
            _logger?.LogInformation("Roadmap note updated for step {StepId}", stepId);
        }
    }

    private RoadmapProgress LoadProgress()
    {
        try
        {
            if (File.Exists(_progressPath))
            {
                var json = File.ReadAllText(_progressPath);
                return JsonSerializer.Deserialize<RoadmapProgress>(json) ?? new RoadmapProgress();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load roadmap progress");
        }

        return new RoadmapProgress();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_progressPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_progress, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_progressPath, json);
            _isDirty = false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save roadmap progress");
        }
    }

    private void SaveIfDirty()
    {
        if (_isDirty) Save();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _saveTimer?.Stop(); } catch { }
        SaveIfDirty();
        _logger?.LogInformation("AvaloniaRoadmapService disposed");
    }
}
