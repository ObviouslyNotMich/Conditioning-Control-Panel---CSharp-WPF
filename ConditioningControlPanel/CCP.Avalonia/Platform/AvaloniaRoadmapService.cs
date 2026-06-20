using ConditioningControlPanel.Core.Models;
using ConditioningControlPanel.Core.Services.Roadmap;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// In-memory roadmap service for the Avalonia head until server-side sync is ported.
/// Persists progress in memory only for the current process.
/// </summary>
public sealed class AvaloniaRoadmapService : IRoadmapService
{
    private readonly RoadmapProgress _progress = new();

    public RoadmapProgress Progress => _progress;

    public event EventHandler<RoadmapStepCompletedEventArgs>? StepCompleted;
    public event EventHandler<RoadmapTrack>? TrackUnlocked;

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
    }
}
