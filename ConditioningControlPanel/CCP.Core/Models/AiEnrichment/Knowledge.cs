using System.Collections.Generic;

namespace ConditioningControlPanel.Core.Models.AiEnrichment
{
    public class Knowledge
    {
        public List<KnowledgeFile> Files { get; set; } = new();
        public List<KnowledgeTrigger> Triggers { get; set; } = new();
        public List<KnowledgeKink> Kinks { get; set; } = new();
    }
}
