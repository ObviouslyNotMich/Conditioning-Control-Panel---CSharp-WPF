using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.Services.Companion;

/// <summary>
/// Avalonia implementation of companion phrase category discovery.
/// </summary>
public sealed class AvaloniaCompanionPhraseService : ICompanionPhraseService
{
    private readonly ISettingsService _settings;

    public AvaloniaCompanionPhraseService(ISettingsService settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <inheritdoc />
    public IEnumerable<string> GetCategoryNames()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var cat in DefaultCategories)
        {
            if (seen.Add(cat))
                result.Add(cat);
        }

        var custom = _settings.Current?.CustomCompanionPhrases;
        if (custom != null)
        {
            foreach (var phrase in custom)
            {
                if (string.IsNullOrEmpty(phrase?.Category)) continue;
                if (seen.Add(phrase.Category))
                    result.Add(phrase.Category);
            }
        }

        return result;
    }

    private static readonly string[] DefaultCategories = new[]
    {
        "Greeting",
        "Idle",
        "Praise",
        "Mantra",
        "Misc"
    };
}
