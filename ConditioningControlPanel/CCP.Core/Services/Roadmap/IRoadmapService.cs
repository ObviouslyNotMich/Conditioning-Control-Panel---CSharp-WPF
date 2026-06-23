using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Core.Services.Roadmap;

/// <summary>
/// Cross-platform service that drives the Transformation Roadmap:
/// track unlocks, step progress, photo submission, and completion events.
/// </summary>
public interface IRoadmapService
{
    RoadmapProgress Progress { get; }

    event EventHandler<RoadmapStepCompletedEventArgs>? StepCompleted;
    event EventHandler<RoadmapTrack>? TrackUnlocked;

    bool IsTrackUnlocked(RoadmapTrack track);
    bool IsStepCompleted(string stepId);
    bool IsStepActive(string stepId);
    RoadmapStepProgress? GetStepProgress(string stepId);
    (int completed, int total) GetTrackProgress(RoadmapTrack track);
    string? GetFullPhotoPath(string? relativePath);
    void StartStep(string stepId);
    void SubmitPhoto(string stepId, string photoPath, string? note);
    void UpdateStepNote(string stepId, string? note);
}

public sealed class RoadmapStepCompletedEventArgs : EventArgs
{
    public RoadmapStepDefinition StepDefinition { get; }
    public RoadmapStepProgress StepProgress { get; }
    public bool UnlockedNewTrack { get; }
    public bool EarnedBadge { get; }

    public RoadmapStepCompletedEventArgs(RoadmapStepDefinition stepDefinition, RoadmapStepProgress stepProgress,
        bool unlockedNewTrack, bool earnedBadge)
    {
        StepDefinition = stepDefinition;
        StepProgress = stepProgress;
        UnlockedNewTrack = unlockedNewTrack;
        EarnedBadge = earnedBadge;
    }
}
