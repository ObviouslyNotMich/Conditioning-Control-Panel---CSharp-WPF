using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// No-op cloud backup provider until profile sync is wired to the Avalonia head.
/// </summary>
public sealed class AvaloniaSettingsBackupProvider : ISettingsBackupProvider
{
    public bool HasCloudIdentity => false;

    public Task BackupSettingsAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
