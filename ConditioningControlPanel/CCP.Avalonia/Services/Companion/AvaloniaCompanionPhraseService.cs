using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Services.Companion;

/// <summary>
/// Avalonia implementation of companion phrase management.
/// Mirrors the legacy WPF <c>CompanionPhraseService</c> but resolves dependencies
/// through the Core DI seams instead of static <c>App.*</c> accessors.
/// </summary>
public sealed class AvaloniaCompanionPhraseService : ICompanionPhraseService
{
    private readonly ISettingsService _settings;
    private readonly IModService _modService;
    private readonly IAppEnvironment _environment;
    private readonly ILogger<AvaloniaCompanionPhraseService>? _logger;

    public AvaloniaCompanionPhraseService(
        ISettingsService settings,
        IModService modService,
        IAppEnvironment environment,
        ILogger<AvaloniaCompanionPhraseService>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _modService = modService ?? throw new ArgumentNullException(nameof(modService));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger;
    }

    /// <inheritdoc />
    public string VoiceLineFolder => ResolveVoiceLineFolder();

    /// <inheritdoc />
    public IEnumerable<string> GetCategoryNames()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var cat in BuiltInCategories)
        {
            if (seen.Add(cat))
                result.Add(cat);
        }

        // Custom phrases may introduce new categories.
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

    /// <inheritdoc />
    public IReadOnlyList<CompanionPhrase> GetAllPhrases()
    {
        var settings = _settings.Current;
        var disabledIds = settings?.DisabledPhraseIds ?? new HashSet<string>();
        var removedIds = settings?.RemovedPhraseIds ?? new HashSet<string>();
        var audioOverrides = settings?.PhraseAudioOverrides ?? new Dictionary<string, string>();
        var result = new List<CompanionPhrase>();

        // Built-in phrases from the active mod.
        foreach (var category in BuiltInCategories)
        {
            var phrases = _modService.GetPhrases(category) ?? Array.Empty<string>();
            for (int i = 0; i < phrases.Length; i++)
            {
                var id = $"{category}:{i}";
                if (removedIds.Contains(id)) continue;

                result.Add(new CompanionPhrase
                {
                    Id = id,
                    Text = phrases[i],
                    Category = category,
                    IsBuiltIn = true,
                    IsEnabled = !disabledIds.Contains(id),
                    AudioFileName = audioOverrides.TryGetValue(id, out var audio) ? audio : null
                });
            }
        }

        // Voice line phrases (audio filenames in the flashes_audio folder).
        var voiceLines = GetVoiceLineFiles();
        var voiceLineFolder = ResolveVoiceLineFolder();
        for (int i = 0; i < voiceLines.Count; i++)
        {
            var id = $"{VoiceLineCategory}:{i}";
            if (removedIds.Contains(id)) continue;

            var fileName = Path.GetFileName(voiceLines[i]);
            var text = Path.GetFileNameWithoutExtension(voiceLines[i]);

            result.Add(new CompanionPhrase
            {
                Id = id,
                Text = text,
                Category = VoiceLineCategory,
                IsBuiltIn = true,
                IsEnabled = !disabledIds.Contains(id),
                AudioFileName = fileName,
                AudioFolder = voiceLineFolder
            });
        }

        // Custom phrases.
        var customPhrases = settings?.CustomCompanionPhrases ?? new List<CustomCompanionPhrase>();
        foreach (var custom in customPhrases)
        {
            result.Add(new CompanionPhrase
            {
                Id = custom.Id,
                Text = custom.Text,
                Category = custom.Category,
                IsBuiltIn = false,
                IsEnabled = custom.Enabled,
                AudioFileName = custom.AudioFileName
            });
        }

        return result;
    }

    /// <inheritdoc />
    public string? CopyAudioToFolder(string sourcePath, string phraseText)
    {
        try
        {
            EnsureAudioFolderExists();
            var ext = Path.GetExtension(sourcePath);
            var sanitized = SanitizeFileName(phraseText);
            var fileName = $"{sanitized}{ext}";
            var destPath = Path.Combine(CompanionPhrase.DefaultAudioFolder, fileName);

            int counter = 1;
            while (File.Exists(destPath))
            {
                fileName = $"{sanitized}_{counter}{ext}";
                destPath = Path.Combine(CompanionPhrase.DefaultAudioFolder, fileName);
                counter++;
            }

            File.Copy(sourcePath, destPath);
            return fileName;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to copy companion audio file from {Source}", sourcePath);
            return null;
        }
    }

    private void EnsureAudioFolderExists()
    {
        try
        {
            if (!Directory.Exists(CompanionPhrase.DefaultAudioFolder))
                Directory.CreateDirectory(CompanionPhrase.DefaultAudioFolder);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to create companion audio folder");
        }
    }

    private static string SanitizeFileName(string text)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(text.Where(c => !invalid.Contains(c)).ToArray());
        sanitized = sanitized.Replace(' ', '_');
        if (sanitized.Length > 50) sanitized = sanitized[..50];
        if (string.IsNullOrWhiteSpace(sanitized)) sanitized = "phrase";
        return sanitized;
    }

    private string ResolveVoiceLineFolder()
    {
        var activeMod = _modService.ActiveMod;
        if (!string.IsNullOrEmpty(activeMod.InstalledPath))
        {
            var modVoiceDir = Path.Combine(activeMod.InstalledPath, "resources", "sounds", "flashes_audio");
            if (Directory.Exists(modVoiceDir))
                return modVoiceDir;
        }

        var baseDir = _environment.BaseDirectory;
        if (!string.IsNullOrEmpty(activeMod.Id))
        {
            var embeddedModVoiceDir = Path.Combine(baseDir, "Resources", "sounds", "companion_audio", "mods", activeMod.Id, "flashes_audio");
            if (Directory.Exists(embeddedModVoiceDir))
                return embeddedModVoiceDir;
        }

        return Path.Combine(baseDir, "Resources", "sounds", "flashes_audio");
    }

    private List<string> GetVoiceLineFiles()
    {
        try
        {
            var folder = ResolveVoiceLineFolder();
            if (!Directory.Exists(folder))
                return new List<string>();

            var extensions = new[] { "*.mp3", "*.wav", "*.ogg", "*.flac" };
            var files = new List<string>();
            foreach (var ext in extensions)
                files.AddRange(Directory.GetFiles(folder, ext));

            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to enumerate companion voice line files");
            return new List<string>();
        }
    }

    private const string VoiceLineCategory = "VoiceLine";

    private static readonly string[] BuiltInCategories = new[]
    {
        "Greeting", "StartupGreeting", "Idle", "RandomFloating", "Generic",
        "Gaming", "Browsing", "Shopping", "Social", "Discord",
        "TrainingSite", "HypnoContent", "Working", "Media", "Learning",
        "WindowAwarenessIdle", "EngineStop", "FlashPre", "SubliminalAck",
        "RandomBubble", "BubbleCountMercy", "BubblePop", "GameFailed",
        "BubbleMissed", "FlashClicked", "LevelUp", "MindWipe", "BrainDrain"
    };
}
