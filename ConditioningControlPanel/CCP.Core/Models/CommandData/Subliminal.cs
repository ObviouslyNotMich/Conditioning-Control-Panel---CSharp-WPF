namespace ConditioningControlPanel.Core.Models.CommandData
{
    public record Subliminal(
        string Text,
        int Opacity
    ) : IAiCommandData;
}
