namespace ConditioningControlPanel.Core.Services.Settings;

/// <summary>
/// Optional cloud backup provider invoked by <see cref="ISettingsService.SaveImmediate"/>.
/// </summary>
public interface ISettingsBackupProvider
{
    /// <summary>
    /// True when the user has a cloud identity and settings backup is allowed.
    /// </summary>
    bool HasCloudIdentity { get; }

    /// <summary>
    /// Asynchronously back up the current settings to cloud storage.
    /// Implementations should swallow their own exceptions; callers only log failures.
    /// </summary>
    Task BackupSettingsAsync(CancellationToken cancellationToken = default);
}
