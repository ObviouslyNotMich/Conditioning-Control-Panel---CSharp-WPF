using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Core.Services.Sessions;

/// <summary>
/// Cross-platform seam that starts and stops the active session's feature effects
/// (flash, video, bubbles, overlays, etc.) in one coordinated operation.
/// </summary>
public interface ISessionEffectOrchestrator
{
    /// <summary>Start all effects enabled by the given session.</summary>
    void StartEffects(Session session);

    /// <summary>Stop all running session effects.</summary>
    void StopEffects();
}
