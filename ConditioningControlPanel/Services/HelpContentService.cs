using System.Collections.Generic;
using ConditioningControlPanel.Models;
using CoreHelp = ConditioningControlPanel.Core.Services.Help.HelpContentService;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// WPF-compatible facade for help content. The actual dictionary now lives in
    /// <see cref="ConditioningControlPanel.Core.Services.Help.HelpContentService"/>.
    /// </summary>
    public static class HelpContentService
    {
        public static HelpContent GetContent(string sectionId) => CoreHelp.GetContent(sectionId);

        public static bool HasContent(string sectionId) => CoreHelp.HasContent(sectionId);

        public static IEnumerable<string> GetAllSectionIds() => CoreHelp.GetAllSectionIds();
    }
}
