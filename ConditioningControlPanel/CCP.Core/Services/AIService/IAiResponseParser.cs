using System.Collections.Generic;
using ConditioningControlPanel.Core.Models;

namespace ConditioningControlPanel.Core.Services.AIService
{
    public interface IAiResponseParser
    {
        ParsedAiResponse Parse(string response);
    }

    public class ParsedAiResponse
    {
        public string CleanText { get; set; } = string.Empty;
        public List<AiCommandData> Commands { get; set; } = new();
    }
}
