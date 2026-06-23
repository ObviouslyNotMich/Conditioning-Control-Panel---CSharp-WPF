namespace ConditioningControlPanel.Core.Services.Awareness;

/// <summary>
/// Fine-grained app/window classification for the awareness-gated bark rules: maps a window-title
/// substring to an <c>app_cluster</c> id (game_competitive, site_doomscroll, …) or, for bespoke
/// single titles, to an <c>app</c> id (hades, obs, discord). This is a layer ON TOP of the broad
/// <see cref="ActivityCategory"/> produced by <see cref="IAwarenessService"/>.
/// </summary>
public interface IAppClusterMap
{
    /// <summary>
    /// Classify a raw window title into (cluster, app) ids. Either may be null. Bespoke apps win over
    /// clusters; within each, the longest matching substring wins (so "youtube music" beats "youtube").
    /// </summary>
    (string? cluster, string? app) Classify(string? windowTitle);
}
