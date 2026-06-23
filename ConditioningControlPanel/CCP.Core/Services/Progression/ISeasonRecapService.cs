namespace ConditioningControlPanel.Core.Services.Progression;

/// <summary>
/// Records season-rank samples for the end-of-season recap feature.
/// </summary>
public interface ISeasonRecapService
{
    /// <summary>
    /// Samples the player's current rank and total for the active season.
    /// </summary>
    void SampleRank(int rank, int total);
}
