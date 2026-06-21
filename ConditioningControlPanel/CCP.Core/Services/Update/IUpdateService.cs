using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Core.Services.Update;

/// <summary>
/// Cross-platform update discovery and orchestration.
/// Platform-specific download/installation is delegated to <see cref="Platform.IUpdateInstaller"/>.
/// </summary>
public interface IUpdateService
{
    /// <summary>Fired when a newer version is found.</summary>
    event EventHandler<UpdateInfo>? UpdateAvailable;

    /// <summary>Fired when download progress changes (0-100).</summary>
    event EventHandler<int>? DownloadProgressChanged;

    /// <summary>Fired when a check or download fails.</summary>
    event EventHandler<Exception>? UpdateFailed;

    /// <summary>True when a newer version has been discovered.</summary>
    bool IsUpdateAvailable { get; }

    /// <summary>The most recently discovered update, or null.</summary>
    UpdateInfo? LatestUpdate { get; }

    /// <summary>True while a download is in progress.</summary>
    bool IsDownloading { get; }

    /// <summary>
    /// Checks GitHub releases for a newer version.
    /// </summary>
    /// <param name="forceCheck">If true, bypasses the 24-hour skip marker.</param>
    Task<UpdateInfo?> CheckForUpdatesAsync(bool forceCheck = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the installer/asset for <see cref="LatestUpdate"/>.
    /// </summary>
    Task<bool> DownloadUpdateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Installs the downloaded update. Behavior is platform-specific.</summary>
    Task InstallUpdateAsync();

    /// <summary>Fetches release notes from GitHub for a specific version.</summary>
    Task<string?> FetchReleaseNotesFromGitHubAsync(string version, CancellationToken cancellationToken = default);
}
