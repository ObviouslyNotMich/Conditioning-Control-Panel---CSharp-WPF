using System.Security.Cryptography;
using System.Text;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Cross-platform local-file settings backup provider for the Avalonia head.
/// Stores timestamped copies of <c>settings.json</c> under the user's data path
/// so the App Info "Back Up Now" button actually persists a recovery copy.
/// This is a stopgap until full cloud profile sync is ported from WPF.
/// </summary>
public sealed class AvaloniaSettingsBackupProvider : ISettingsBackupProvider
{
    private readonly IAppEnvironment _environment;
    private readonly IServiceProvider _services;
    private readonly IAppLogger? _logger;

    /// <summary>
    /// Number of backups to retain. Older backups are deleted after a new one is written.
    /// </summary>
    private const int MaxRetainedBackups = 20;

    public AvaloniaSettingsBackupProvider(
        IAppEnvironment environment,
        IServiceProvider services,
        IAppLogger? logger = null)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger;
    }

    private ISettingsService SettingsService => _services.GetRequiredService<ISettingsService>();

    /// <inheritdoc />
    public bool HasCloudIdentity => !string.IsNullOrEmpty(SettingsService.Current?.UnifiedId);

    /// <inheritdoc />
    public Task BackupSettingsAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => BackupCore(cancellationToken), cancellationToken);

    private void BackupCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sourcePath = Path.Combine(_environment.UserDataPath, "settings.json");
        if (!File.Exists(sourcePath))
        {
            _logger?.Debug("AvaloniaSettingsBackupProvider: no settings.json to back up");
            return;
        }

        try
        {
            var backupDir = Path.Combine(_environment.UserDataPath, "settings-backups");
            Directory.CreateDirectory(backupDir);

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var hash = ComputeFileHash(sourcePath);
            var fileName = $"settings-{timestamp}-{hash}.json";
            var destPath = Path.Combine(backupDir, fileName);

            File.Copy(sourcePath, destPath, overwrite: true);
            _logger?.Debug("AvaloniaSettingsBackupProvider: backed up settings to {BackupPath}", destPath);

            PruneOldBackups(backupDir);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "AvaloniaSettingsBackupProvider: local backup failed");
        }
    }

    private static void PruneOldBackups(string backupDir)
    {
        try
        {
            var files = Directory
                .EnumerateFiles(backupDir, "settings-*.json")
                .Select(f => new FileInfo(f))
                .OrderByDescending(fi => fi.LastWriteTimeUtc)
                .ToList();

            foreach (var file in files.Skip(MaxRetainedBackups))
            {
                try { file.Delete(); }
                catch { /* best-effort cleanup */ }
            }
        }
        catch
        {
            // Pruning failures should not break the backup operation.
        }
    }

    private static string ComputeFileHash(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var hash = SHA256.HashData(fs);
            var sb = new StringBuilder(8);
            for (int i = 0; i < 4; i++) sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }
        catch
        {
            return "00000000";
        }
    }
}
