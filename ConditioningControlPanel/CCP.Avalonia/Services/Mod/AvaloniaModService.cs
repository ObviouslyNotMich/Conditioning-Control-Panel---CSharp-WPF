using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Avalonia.Services.Mod;

/// <summary>
/// Mod-aware theming/text service for the Avalonia head.
/// Loads built-in mods, discovers user-installed .ccpmod packages, and supports
/// install/uninstall/activate while keeping the legacy text helpers working.
/// </summary>
public sealed class AvaloniaModService : IModService
{
    private readonly ISettingsService _settings;
    private readonly IAppEnvironment _environment;
    private readonly IAppLogger? _logger;

    private readonly List<ModPackage> _builtInMods;
    private readonly List<ModPackage> _installedMods = new();
    private ModPackage _activeMod = null!;

    public AvaloniaModService(ISettingsService settings, IAppEnvironment environment, IAppLogger? logger = null)
    {
        _settings = settings;
        _environment = environment;
        _logger = logger;

        _builtInMods = new List<ModPackage>
        {
            new ModPackage(BuiltInMods.CCPDefault, null, true),
            new ModPackage(BuiltInMods.BambiSleep, null, true),
            new ModPackage(BuiltInMods.SissyHypno, null, true),
            new ModPackage(BuiltInMods.Dronification, null, true),
            new ModPackage(BuiltInMods.Locked, null, true),
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<ModPackage> InstalledMods => _installedMods;

    /// <inheritdoc />
    public ModPackage ActiveMod => _activeMod;

    /// <inheritdoc />
    public event EventHandler<ModPackage>? ActiveModChanged;

    /// <inheritdoc />
    public void Initialize(string? activeModId)
    {
        EnsureModsDirectoryExists();
        RefreshInstalledMods();

        var target = ResolveMod(activeModId);
        if (target == null)
        {
            target = ResolveMod(BuiltInMods.CCPDefaultId)!;
        }

        if (_settings.Current.ActiveModId != target.Id)
        {
            _settings.Current.ActiveModId = target.Id;
            _settings.Save();
        }

        _activeMod = target;
        _logger?.Information("AvaloniaModService initialized — active mod: {ModId} ({ModName})", _activeMod.Id, _activeMod.Name);
    }

    /// <inheritdoc />
    public bool ActivateMod(string modId)
    {
        var target = ResolveMod(modId);
        if (target == null) return false;

        if (_activeMod.Id == target.Id) return true;

        _activeMod = target;
        _settings.Current.ActiveModId = target.Id;
        _settings.Save();
        ActiveModChanged?.Invoke(this, target);
        _logger?.Information("Active mod changed to {ModId} ({ModName})", target.Id, target.Name);
        return true;
    }

    /// <inheritdoc />
    public async Task<ModInstallResult> InstallModAsync(string ccpmodPath)
    {
        if (string.IsNullOrWhiteSpace(ccpmodPath) || !File.Exists(ccpmodPath))
            return ModInstallResult.Failure(ModInstallStatus.InvalidPackage, "Mod package file not found.");

        if (!Path.GetExtension(ccpmodPath).Equals(".ccpmod", StringComparison.OrdinalIgnoreCase))
            return ModInstallResult.Failure(ModInstallStatus.InvalidPackage, "File must have a .ccpmod extension.");

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            await Task.Run(() => ZipFile.ExtractToDirectory(ccpmodPath, tempDir)).ConfigureAwait(false);

            var modJsonPath = Directory.GetFiles(tempDir, "mod.json", SearchOption.AllDirectories).FirstOrDefault();
            if (modJsonPath == null)
                return ModInstallResult.Failure(ModInstallStatus.InvalidManifest, "mod.json was not found in the package.");

            var manifest = JsonConvert.DeserializeObject<ModManifest>(await File.ReadAllTextAsync(modJsonPath).ConfigureAwait(false));
            if (manifest == null)
                return ModInstallResult.Failure(ModInstallStatus.InvalidManifest, "mod.json could not be parsed.");

            if (string.IsNullOrWhiteSpace(manifest.Id))
                return ModInstallResult.Failure(ModInstallStatus.InvalidManifest, "Mod ID is required.");

            if (!IsValidModId(manifest.Id))
                return ModInstallResult.Failure(ModInstallStatus.InvalidId, "Mod ID must be lowercase alphanumeric with hyphens and cannot start with 'builtin-'.");

            if (string.IsNullOrWhiteSpace(manifest.Name))
                return ModInstallResult.Failure(ModInstallStatus.InvalidManifest, "Mod name is required.");

            var modRoot = Path.GetDirectoryName(modJsonPath)!;
            var destDir = Path.Combine(GetModsDirectory(), manifest.Id);

            await Task.Run(() =>
            {
                if (Directory.Exists(destDir))
                    Directory.Delete(destDir, recursive: true);

                Directory.Move(modRoot, destDir);
            }).ConfigureAwait(false);

            RefreshInstalledMods();

            var installed = _installedMods.FirstOrDefault(m => m.Id == manifest.Id);
            if (installed != null)
                return ModInstallResult.Success(installed);

            return ModInstallResult.Failure(ModInstallStatus.UnknownError, "The mod was extracted but could not be discovered.");
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to install mod from {Path}", ccpmodPath);
            return ModInstallResult.Failure(ModInstallStatus.IOFailure, $"Installation failed: {ex.Message}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch { /* best effort cleanup */ }
        }
    }

    /// <inheritdoc />
    public bool UninstallMod(string modId)
    {
        if (string.IsNullOrWhiteSpace(modId)) return false;

        var existing = _installedMods.FirstOrDefault(m => m.Id == modId);
        if (existing == null || existing.IsBuiltIn) return false;

        var modDir = Path.Combine(GetModsDirectory(), modId);
        try
        {
            if (Directory.Exists(modDir))
                Directory.Delete(modDir, recursive: true);

            RefreshInstalledMods();

            if (_activeMod.Id == modId)
            {
                ActivateMod(BuiltInMods.CCPDefaultId);
            }

            _logger?.Information("Uninstalled mod {ModId}", modId);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to uninstall mod {ModId}", modId);
            return false;
        }
    }

    #region Text / theming helpers (legacy IModService contract)

    private ModManifest ActiveManifest => _activeMod?.Manifest ?? BuiltInMods.CCPDefault;

    public string GetModeDisplayName()
        => ActiveManifest.Identity?.ModeDisplayName ?? ActiveManifest.Name ?? "Conditioning Control Panel";

    public string MakeModAware(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var identity = ActiveManifest.Identity;
        var replacements = ActiveManifest.TextReplacements;

        var result = text;
        if (replacements != null)
        {
            foreach (var kv in replacements)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                result = result.Replace(kv.Key, kv.Value, StringComparison.OrdinalIgnoreCase);
            }
        }

        // Fallback identity replacements when the mod does not define explicit text replacements.
        var userTerm = identity?.UserTerm;
        if (!string.IsNullOrEmpty(userTerm))
        {
            result = result.Replace("{UserTerm}", userTerm, StringComparison.OrdinalIgnoreCase);
            result = result.Replace("Bambi", userTerm, StringComparison.OrdinalIgnoreCase);
        }

        var companion = identity?.CompanionName;
        if (!string.IsNullOrEmpty(companion))
        {
            result = result.Replace("{CompanionName}", companion, StringComparison.OrdinalIgnoreCase);
        }

        var affirmation = identity?.Affirmation;
        if (!string.IsNullOrEmpty(affirmation))
        {
            result = result.Replace("{Affirmation}", affirmation, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    public string GetAccentColorHex()
        => ActiveManifest.Theme?.AccentColor ?? "#FF69B4";

    public string GetAccentLightColorHex()
        => ActiveManifest.Theme?.AccentLightColor ?? "#FF8FAF";

    public string GetAccentDarkColorHex()
        => ActiveManifest.Theme?.AccentDarkColor ?? "#FF1493";

    public string GetSecondaryColorHex()
        => ActiveManifest.Theme?.AccentDarkColor ?? "#9B59B6";

    public string GetBackgroundColorHex()
        => ActiveManifest.Theme?.BackgroundColor ?? "#1A1A2E";

    public string GetPanelColorHex()
        => ActiveManifest.Theme?.PanelColor ?? "#252542";

    public string GetSurfaceColorHex()
        => ActiveManifest.Theme?.SurfaceColor ?? "#1E1E3A";

    public string GetFilterColorHex()
        => ActiveManifest.Theme?.FilterColor ?? GetAccentColorHex();

    public string GetPinkRushName()
        => MakeModAware(ActiveManifest.EnhancementOverrides?.PinkRushName ?? "PINK RUSH!");

    public string GetPinkRushDescription()
        => MakeModAware(ActiveManifest.EnhancementOverrides?.PinkRushDescription ?? "3x XP for 60 seconds!");

    public string[] GetPhrases(string category)
    {
        if (string.IsNullOrEmpty(category)) return Array.Empty<string>();

        var phrases = ActiveManifest.Phrases;
        if (phrases != null && phrases.TryGetValue(category, out var pool))
            return pool ?? Array.Empty<string>();

        return category.ToLowerInvariant() switch
        {
            "idle" => new[] { "Good girl~" },
            "thinking" => new[] { "Thinking..." },
            "bubblecountmercy" => new[] { "Aww, try again~" },
            _ => Array.Empty<string>()
        };
    }

    public string GetAttentionCheckFailMessage()
        => MakeModAware(ActiveManifest.Messages?.AttentionCheckFail ?? "DUMB BAMBI!\nTRY AGAIN");

    public string GetAttentionCheckMercyMessage()
        => MakeModAware(ActiveManifest.Messages?.AttentionCheckMercy ?? "BAMBI GETS MERCY");

    #endregion

    #region Discovery helpers

    private string GetModsDirectory() => Path.Combine(_environment.UserDataPath, "mods");

    private void EnsureModsDirectoryExists()
    {
        var modsDir = GetModsDirectory();
        if (!Directory.Exists(modsDir))
            Directory.CreateDirectory(modsDir);
    }

    private void RefreshInstalledMods()
    {
        _installedMods.Clear();
        _installedMods.AddRange(_builtInMods);

        var modsDir = GetModsDirectory();
        if (!Directory.Exists(modsDir)) return;

        foreach (var dir in Directory.GetDirectories(modsDir))
        {
            var modJson = Path.Combine(dir, "mod.json");
            if (!File.Exists(modJson)) continue;

            try
            {
                var json = File.ReadAllText(modJson);
                var manifest = JsonConvert.DeserializeObject<ModManifest>(json);
                if (manifest == null || string.IsNullOrWhiteSpace(manifest.Id)) continue;

                var package = new ModPackage(manifest, dir, false);
                var existingIdx = _installedMods.FindIndex(m => m.Id == package.Id);
                if (existingIdx >= 0)
                    _installedMods[existingIdx] = package; // user mod overrides built-in with the same ID
                else
                    _installedMods.Add(package);
            }
            catch (Exception ex)
            {
                _logger?.Warning(ex, "Failed to load user mod from {Path}", dir);
            }
        }
    }

    private ModPackage? ResolveMod(string? modId)
    {
        if (string.IsNullOrWhiteSpace(modId)) return null;
        return _installedMods.FirstOrDefault(m => m.Id == modId)
            ?? _builtInMods.FirstOrDefault(m => m.Id == modId);
    }

    private static bool IsValidModId(string id)
    {
        if (id.StartsWith("builtin-", StringComparison.OrdinalIgnoreCase)) return false;
        return Regex.IsMatch(id, "^[a-z0-9\\-]+$");
    }

    #endregion
}
