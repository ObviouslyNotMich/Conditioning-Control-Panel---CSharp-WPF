using System.Text.Json.Serialization;

namespace ConditioningControlPanel.Core.Models.CommandData
{
    public record SpiralPinkFiler(
        [property: JsonPropertyName("On")] bool On,
        int Intensity
    ) : IAiCommandData;
}
