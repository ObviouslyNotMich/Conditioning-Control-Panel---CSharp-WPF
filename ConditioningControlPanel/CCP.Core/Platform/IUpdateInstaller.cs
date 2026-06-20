namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Platform-specific update installation.
/// </summary>
public interface IUpdateInstaller
{
    Task<bool> DownloadUpdateAsync(Uri downloadUri, IProgress<double> progress, CancellationToken cancellationToken = default);
    Task InstallUpdateAsync();
    string? GetInstalledVersion();
}
