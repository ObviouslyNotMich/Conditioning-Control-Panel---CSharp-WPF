using System.Text.Json.Serialization;

namespace ConditioningControlPanel.Core.Models.CommandData
{
    public record Bubbles(
        [property: JsonPropertyName("On")] bool On,
        int Frequency
    ) : IAiCommandData;
}
