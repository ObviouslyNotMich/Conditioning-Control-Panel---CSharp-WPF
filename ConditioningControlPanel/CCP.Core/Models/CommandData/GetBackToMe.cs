using System.Collections.Generic;

namespace ConditioningControlPanel.Core.Models.CommandData
{
    public record GetBackToMe(
        int Delay,
        string Token,
        List<AiCommandData>? Commands,
        string? Text,
        bool JsonOnly
    ) : IAiCommandData
    {
        string? IAiCommandData.Token => Token;
    }
}
