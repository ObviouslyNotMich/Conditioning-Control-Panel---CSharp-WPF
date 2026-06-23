using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.Services.KeywordTriggers;

/// <summary>
/// Avalonia implementation of keyword-trigger preset install/uninstall/clone logic.
/// Mirrors the legacy WPF <c>KeywordTriggerPresetService</c> but operates on
/// the cross-platform <see cref="ISettingsService"/> abstraction.
/// </summary>
public sealed class AvaloniaKeywordTriggerPresetService : IKeywordTriggerPresetService
{
    private const string TriggerIdPrefix = "preset:";

    private readonly ISettingsService _settings;
    private readonly ILogger<AvaloniaKeywordTriggerPresetService> _logger;
    private readonly IAppEnvironment _environment;

    public AvaloniaKeywordTriggerPresetService(
        ISettingsService settings,
        ILogger<AvaloniaKeywordTriggerPresetService> logger,
        IAppEnvironment environment)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    /// <inheritdoc />
    public event EventHandler? PresetsChanged;

    /// <inheritdoc />
    public IReadOnlyList<KeywordTriggerPreset> VisiblePresets
    {
        get
        {
            var list = _settings.Current?.KeywordTriggerPresets;
            if (list == null || list.Count == 0) return Array.Empty<KeywordTriggerPreset>();
            return list.ToList();
        }
    }

    private KeywordTriggerPreset? GetPreset(string presetId)
    {
        if (string.IsNullOrEmpty(presetId)) return null;
        return _settings.Current?.KeywordTriggerPresets.FirstOrDefault(p => p.Id == presetId);
    }

    /// <inheritdoc />
    public bool IsInstalled(string presetId)
        => GetPreset(presetId)?.MasterEnabled == true;

    /// <inheritdoc />
    public bool InstallPreset(string presetId)
    {
        var settings = _settings.Current;
        var preset = GetPreset(presetId);
        if (settings == null || preset == null) return false;

        if (preset.MasterEnabled)
        {
            _logger.LogDebug("InstallPreset: {Id} already installed", presetId);
            return true;
        }

        var prefix = TriggerIdPrefix + presetId + ":";

        // Remove any stale clones from previous installs (defensive).
        settings.KeywordTriggers.RemoveAll(t =>
            t?.Id?.StartsWith(prefix, StringComparison.Ordinal) == true);

        foreach (var source in preset.Triggers ?? new List<KeywordTrigger>())
        {
            if (source == null) continue;
            var clone = source.Clone();

            clone.Id = prefix + (string.IsNullOrEmpty(source.Id)
                ? Guid.NewGuid().ToString("N")[..8]
                : source.Id);
            clone.LastTriggeredAt = DateTime.MinValue;

            if (clone.Actions == null || clone.Actions.Count == 0)
                KeywordTriggerMigrationHelper.RebuildActionsFromFlatFields(clone);

            settings.KeywordTriggers.Add(clone);
        }

        InstallCannedPhrases(preset);

        preset.MasterEnabled = true;
        _settings.Save();
        _logger.LogInformation("InstallPreset: {Id} installed ({Count} triggers)",
            presetId, preset.Triggers?.Count ?? 0);

        PresetsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <inheritdoc />
    public bool UninstallPreset(string presetId)
    {
        var settings = _settings.Current;
        var preset = GetPreset(presetId);
        if (settings == null || preset == null) return false;

        var prefix = TriggerIdPrefix + presetId + ":";
        int removed = settings.KeywordTriggers.RemoveAll(t =>
            t?.Id?.StartsWith(prefix, StringComparison.Ordinal) == true);

        RemoveCannedPhrases(preset);

        preset.MasterEnabled = false;
        _settings.Save();
        _logger.LogInformation("UninstallPreset: {Id} uninstalled ({Count} triggers removed)",
            presetId, removed);

        PresetsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <inheritdoc />
    public KeywordTriggerPreset? CloneToCustom(string presetId)
    {
        var settings = _settings.Current;
        var source = GetPreset(presetId);
        if (settings == null || source == null) return null;

        var copy = new KeywordTriggerPreset
        {
            Id = "custom." + Guid.NewGuid().ToString("N")[..8],
            Name = MakeCopyName(source.Name, settings),
            Icon = source.Icon,
            Description = source.Description,
            LongDescription = source.LongDescription,
            Author = "You",
            Version = 1,
            IsBuiltIn = false,
            RequiresAi = source.RequiresAi,
            AvatarPromptTemplate = source.AvatarPromptTemplate,
            PhrasePools = new List<string>(source.PhrasePools ?? new List<string>()),
            CannedPhrases = source.CannedPhrases?.ToDictionary(
                kvp => kvp.Key,
                kvp => new List<string>(kvp.Value ?? new List<string>()))
                ?? new Dictionary<string, List<string>>(),
            MasterEnabled = false,
            Triggers = new List<KeywordTrigger>(),
        };

        foreach (var src in source.Triggers ?? new List<KeywordTrigger>())
        {
            if (src == null) continue;
            var clone = src.Clone();
            clone.Id = Guid.NewGuid().ToString("N")[..8];
            clone.LastTriggeredAt = DateTime.MinValue;
            if (clone.Actions == null || clone.Actions.Count == 0)
                KeywordTriggerMigrationHelper.RebuildActionsFromFlatFields(clone);
            copy.Triggers.Add(clone);
        }

        settings.KeywordTriggerPresets.Add(copy);
        _settings.Save();
        _logger.LogInformation("CloneToCustom: created {NewId} from {SourceId} ({Count} triggers)",
            copy.Id, presetId, copy.Triggers.Count);

        PresetsChanged?.Invoke(this, EventArgs.Empty);
        return copy;
    }

    private static string MakeCopyName(string? baseName, AppSettings settings)
    {
        var root = string.IsNullOrWhiteSpace(baseName) ? "Preset" : baseName.Trim();
        var existing = settings.KeywordTriggerPresets
            .Select(p => p.Name)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidate = $"{root} (Copy)";
        int n = 2;
        while (existing.Contains(candidate))
            candidate = $"{root} (Copy {n++})";
        return candidate;
    }

    private static string MakeCustomPhraseId(string presetId, string category, int index)
        => $"preset:{presetId}:phrase:{category}:{index}";

    private void InstallCannedPhrases(KeywordTriggerPreset preset)
    {
        var settings = _settings.Current;
        if (settings == null || preset.CannedPhrases == null || preset.CannedPhrases.Count == 0) return;

        RemoveCannedPhrases(preset);

        foreach (var kvp in preset.CannedPhrases)
        {
            var category = kvp.Key;
            var phrases = kvp.Value;
            if (string.IsNullOrEmpty(category) || phrases == null) continue;

            for (int i = 0; i < phrases.Count; i++)
            {
                var text = phrases[i];
                if (string.IsNullOrWhiteSpace(text)) continue;
                settings.CustomCompanionPhrases.Add(new CustomCompanionPhrase
                {
                    Id = MakeCustomPhraseId(preset.Id, category, i),
                    Text = text,
                    Category = category,
                    Enabled = true
                });
            }
        }
    }

    private void RemoveCannedPhrases(KeywordTriggerPreset preset)
    {
        var settings = _settings.Current;
        if (settings == null) return;

        var marker = $"preset:{preset.Id}:phrase:";
        settings.CustomCompanionPhrases.RemoveAll(p =>
            p.Id?.StartsWith(marker, StringComparison.Ordinal) == true);
    }
}
