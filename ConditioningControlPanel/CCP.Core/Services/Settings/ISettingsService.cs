using ConditioningControlPanel;
using ConditioningControlPanel.Core.Models;

namespace ConditioningControlPanel.Core.Services.Settings;

/// <summary>
/// Cross-platform settings service: loads, migrates, and persists <see cref="AppSettings"/>.
/// </summary>
public interface ISettingsService : IAppSettingsService
{
    /// <summary>
    /// True if the settings file did not exist when the service was initialized (fresh install).
    /// </summary>
    bool WasSettingsFileMissing { get; }

    /// <summary>
    /// Preset IDs that need a re-install pass after services are wired up.
    /// </summary>
    List<string> PendingPresetReinstalls { get; }

    /// <summary>
    /// Debounced save — coalesces rapid calls into a single disk write after 500ms of quiet.
    /// </summary>
    void Save(bool suppressCloudBackup = false);

    /// <summary>
    /// Writes settings to disk immediately (no debounce). Use for shutdown or critical paths.
    /// </summary>
    void SaveImmediate(bool suppressCloudBackup = false);

    /// <summary>
    /// Replace current settings with the given settings object and save to disk.
    /// </summary>
    void RestoreFrom(AppSettings settings);

    /// <summary>
    /// Reset settings to defaults and persist.
    /// </summary>
    void Reset();
}
