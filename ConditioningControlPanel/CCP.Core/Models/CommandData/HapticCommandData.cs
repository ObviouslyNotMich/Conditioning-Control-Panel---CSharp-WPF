namespace ConditioningControlPanel.Core.Models.CommandData
{
    public record HapticCommandData(
        double Intensity,
        int Duration
    ) : IAiCommandData;
}
