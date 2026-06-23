using ConditioningControlPanel.Core.Services.Progression;

namespace ConditioningControlPanel.Avalonia.Services.Progression;

/// <summary>
/// Avalonia season-recap recorder. The season-recap window/feature is not yet
/// ported, so this implementation is currently a no-op sink that preserves the
/// Core service contract.
/// </summary>
public sealed class AvaloniaSeasonRecapService : ISeasonRecapService
{
    public void SampleRank(int rank, int total)
    {
        // No-op until the Avalonia SeasonRecapWindow is ported and wired.
    }
}
