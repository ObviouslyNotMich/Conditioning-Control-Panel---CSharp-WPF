using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Companion;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Avalonia.Services.Companion;

/// <summary>
/// Avalonia/Core implementation of <see cref="ICommunityPromptService"/>.
/// Manages downloading, installing, activating, and sharing community AI personality prompts.
/// </summary>
public sealed class AvaloniaCommunityPromptService : ICommunityPromptService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _promptsFolder;
    private readonly string _manifestCachePath;
    private List<CommunityPromptManifestEntry> _availablePrompts = new();
    private bool _disposed;

    private const string PromptsManifestUrl = "https://codebambi-proxy.vercel.app/prompts/manifest";
    private const string PromptsDownloadUrl = "https://codebambi-proxy.vercel.app/prompts";

    private readonly ISettingsService _settings;
    private readonly IAppEnvironment _appEnvironment;
    private readonly ILogger<AvaloniaCommunityPromptService>? _logger;

    public AvaloniaCommunityPromptService(
        ISettingsService settings,
        IAppEnvironment appEnvironment,
        ILogger<AvaloniaCommunityPromptService>? logger = null)
    {
        _settings = settings;
        _appEnvironment = appEnvironment;
        _logger = logger;

        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);

        _promptsFolder = Path.Combine(_appEnvironment.UserDataPath, "community-prompts");
        _manifestCachePath = Path.Combine(_promptsFolder, "manifest-cache.json");

        if (!Directory.Exists(_promptsFolder))
            Directory.CreateDirectory(_promptsFolder);

        LoadCachedManifest();

        _logger?.LogInformation("AvaloniaCommunityPromptService initialized. Prompts folder: {Folder}", _promptsFolder);
    }

    public IReadOnlyList<string> InstalledPromptIds =>
        _settings.Current?.InstalledCommunityPromptIds ?? new List<string>();

    public string? ActivePromptId => _settings.Current?.ActiveCommunityPromptId;

    public event EventHandler<CommunityPrompt>? PromptInstalled;
    public event EventHandler<string>? PromptRemoved;
    public event EventHandler<Exception>? Error;

    public async Task<List<CommunityPromptManifestEntry>> GetAvailablePromptsAsync(bool forceRefresh = false)
    {
        if (_settings.Current?.OfflineMode == true)
        {
            _logger?.LogDebug("Offline mode enabled, using cached prompts only");
            return _availablePrompts;
        }

        if (!forceRefresh && _availablePrompts.Count > 0)
            return _availablePrompts;

        try
        {
            var response = await _httpClient.GetAsync(PromptsManifestUrl);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var manifest = JsonConvert.DeserializeObject<CommunityPromptsManifest>(json);
                if (manifest?.Prompts != null)
                {
                    _availablePrompts = new List<CommunityPromptManifestEntry>(manifest.Prompts);
                    await File.WriteAllTextAsync(_manifestCachePath, json);
                    _logger?.LogInformation("Fetched {Count} community prompts from server", _availablePrompts.Count);
                }
            }
            else
            {
                _logger?.LogWarning("Failed to fetch community prompts manifest: {Status}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("Error fetching community prompts manifest: {Error}", ex.Message);
            Error?.Invoke(this, ex);
        }

        return _availablePrompts;
    }

    public async Task<CommunityPrompt?> InstallPromptAsync(string promptId)
    {
        if (_settings.Current?.OfflineMode == true)
        {
            _logger?.LogInformation("Offline mode enabled, prompt download blocked");
            return null;
        }

        try
        {
            var url = $"{PromptsDownloadUrl}/{promptId}";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("Failed to download prompt {Id}: {Status}", promptId, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var prompt = JsonConvert.DeserializeObject<CommunityPrompt>(json);
            if (prompt == null)
            {
                _logger?.LogWarning("Failed to parse prompt {Id}", promptId);
                return null;
            }

            var filePath = GetPromptFilePath(promptId);
            await File.WriteAllTextAsync(filePath, json);

            var settings = _settings.Current;
            if (settings != null && !settings.InstalledCommunityPromptIds.Contains(promptId))
            {
                settings.InstalledCommunityPromptIds.Add(promptId);
                _settings.Save();
            }

            prompt.IsInstalled = true;
            PromptInstalled?.Invoke(this, prompt);
            _logger?.LogInformation("Installed community prompt: {Name} by {Author}", prompt.Name, prompt.Author);
            return prompt;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error installing prompt {Id}", promptId);
            Error?.Invoke(this, ex);
            return null;
        }
    }

    public async Task<CommunityPrompt?> ImportFromFileAsync(string filePath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var prompt = JsonConvert.DeserializeObject<CommunityPrompt>(json);
            if (prompt == null || string.IsNullOrEmpty(prompt.Id))
            {
                _logger?.LogWarning("Failed to parse prompt from {Path}", filePath);
                return null;
            }

            var destPath = GetPromptFilePath(prompt.Id);
            await File.WriteAllTextAsync(destPath, json);

            var settings = _settings.Current;
            if (settings != null && !settings.InstalledCommunityPromptIds.Contains(prompt.Id))
            {
                settings.InstalledCommunityPromptIds.Add(prompt.Id);
                _settings.Save();
            }

            prompt.IsInstalled = true;
            PromptInstalled?.Invoke(this, prompt);
            _logger?.LogInformation("Imported community prompt: {Name} by {Author} from {Path}",
                prompt.Name, prompt.Author, filePath);
            return prompt;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error importing prompt from {Path}", filePath);
            Error?.Invoke(this, ex);
            return null;
        }
    }

    public void RemovePrompt(string promptId)
    {
        try
        {
            var filePath = GetPromptFilePath(promptId);
            if (File.Exists(filePath))
                File.Delete(filePath);

            var settings = _settings.Current;
            if (settings != null)
            {
                settings.InstalledCommunityPromptIds.Remove(promptId);
                if (settings.ActiveCommunityPromptId == promptId)
                    settings.ActiveCommunityPromptId = null;
                _settings.Save();
            }

            PromptRemoved?.Invoke(this, promptId);
            _logger?.LogInformation("Removed community prompt: {Id}", promptId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error removing prompt {Id}", promptId);
            Error?.Invoke(this, ex);
        }
    }

    public CommunityPrompt? GetInstalledPrompt(string promptId)
    {
        try
        {
            var filePath = GetPromptFilePath(promptId);
            if (!File.Exists(filePath))
                return null;

            var json = File.ReadAllText(filePath);
            var prompt = JsonConvert.DeserializeObject<CommunityPrompt>(json);
            if (prompt != null)
            {
                prompt.IsInstalled = true;
                prompt.IsActive = promptId == ActivePromptId;
            }
            return prompt;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("Error loading prompt {Id}: {Error}", promptId, ex.Message);
            return null;
        }
    }

    public List<CommunityPrompt> GetInstalledPrompts()
    {
        var prompts = new List<CommunityPrompt>();
        foreach (var id in InstalledPromptIds)
        {
            var prompt = GetInstalledPrompt(id);
            if (prompt != null)
                prompts.Add(prompt);
        }
        return prompts;
    }

    public bool ActivatePrompt(string promptId)
    {
        var prompt = GetInstalledPrompt(promptId);
        if (prompt == null)
        {
            _logger?.LogWarning("Cannot activate prompt {Id} - not installed", promptId);
            return false;
        }

        var settings = _settings.Current;
        if (settings == null)
            return false;

        settings.ActiveCommunityPromptId = promptId;
        settings.CompanionPrompt.UseCustomPrompt = false;
        _settings.Save();

        _logger?.LogInformation("Activated community prompt: {Name} by {Author}", prompt.Name, prompt.Author);
        return true;
    }

    public void DeactivatePrompt()
    {
        var settings = _settings.Current;
        if (settings == null)
            return;

        settings.ActiveCommunityPromptId = null;
        _settings.Save();
        _logger?.LogInformation("Deactivated community prompt");
    }

    private string GetPromptFilePath(string promptId)
    {
        return Path.Combine(_promptsFolder, $"{promptId}.json");
    }

    private void LoadCachedManifest()
    {
        try
        {
            if (File.Exists(_manifestCachePath))
            {
                var json = File.ReadAllText(_manifestCachePath);
                var manifest = JsonConvert.DeserializeObject<CommunityPromptsManifest>(json);
                if (manifest?.Prompts != null)
                {
                    _availablePrompts = new List<CommunityPromptManifestEntry>(manifest.Prompts);
                    _logger?.LogDebug("Loaded {Count} prompts from manifest cache", _availablePrompts.Count);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("Could not load prompt manifest cache: {Error}", ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }
}
