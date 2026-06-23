using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Core.Services.Content;

/// <summary>
/// Cross-platform abstraction for downloading, installing, and activating
/// encrypted community content packs.
/// </summary>
public interface IContentPackService : IDisposable
{
    /// <summary>
    /// Path to the hidden folder where packs are stored.
    /// </summary>
    string PacksFolder { get; }

    /// <summary>
    /// IDs of packs that are currently installed.
    /// </summary>
    IReadOnlyCollection<string> InstalledPacks { get; }

    /// <summary>
    /// Fetches available packs from the remote manifest, falling back to built-ins.
    /// </summary>
    Task<List<ContentPack>> GetAvailablePacksAsync();

    /// <summary>
    /// Gets built-in packs without a network request.
    /// </summary>
    List<ContentPack> GetBuiltInPacks();

    /// <summary>
    /// Downloads, encrypts, and installs a content pack.
    /// </summary>
    Task InstallPackAsync(ContentPack pack, IProgress<int>? progress = null);

    /// <summary>
    /// Gets the authenticated download URL for an external pack (e.g. Mega.nz).
    /// </summary>
    Task<string?> GetExternalPackDownloadUrlAsync(string packId);

    /// <summary>
    /// Gets download status for all packs from the server.
    /// </summary>
    Task<Dictionary<string, PackDownloadStatus>?> GetPackDownloadStatusAsync();

    /// <summary>
    /// Gets the full pack status response from the server.
    /// </summary>
    Task<PackStatusResponse?> GetFullPackStatusAsync();

    /// <summary>
    /// Activates an installed pack so its files appear in the active pool.
    /// </summary>
    void ActivatePack(string packId);

    /// <summary>
    /// Deactivates an installed pack.
    /// </summary>
    void DeactivatePack(string packId);

    /// <summary>
    /// Completely removes an installed pack.
    /// </summary>
    void UninstallPack(string packId);

    /// <summary>
    /// Gets files from an installed pack, optionally filtered by file type.
    /// </summary>
    List<PackFileEntry> GetPackFiles(string packId, string? fileType = null);

    /// <summary>
    /// Decrypts a pack file into a memory stream.
    /// </summary>
    MemoryStream? GetPackFileStream(string packId, PackFileEntry file);

    /// <summary>
    /// Creates a temporary decrypted file for video playback and returns its path.
    /// </summary>
    string? GetPackFileTempPath(string packId, PackFileEntry file);

    /// <summary>
    /// Loads preview images for an installed pack.
    /// </summary>
    List<object> GetPackPreviewImages(string packId, int count = 10, int width = 240, int height = 100);

    /// <summary>
    /// Clears the cached preview image selection for a pack.
    /// </summary>
    void ClearPreviewCache(string packId);

    /// <summary>
    /// Gets all video files from all active packs.
    /// </summary>
    List<(string PackId, PackFileEntry File)> GetAllActivePackVideos();

    /// <summary>
    /// Gets all image files from all active packs.
    /// </summary>
    List<(string PackId, PackFileEntry File)> GetAllActivePackImages();

    /// <summary>
    /// Gets IDs of packs that are both active and installed.
    /// </summary>
    List<string> GetActivePackIds();

    /// <summary>
    /// Returns true if the pack is installed and its manifest exists on disk.
    /// </summary>
    bool IsPackInstalled(string packId);

    /// <summary>
    /// Returns true if the pack is active and installed.
    /// </summary>
    bool IsPackActive(string packId);

    /// <summary>
    /// Refreshes the packs folder path when the assets directory changes.
    /// </summary>
    void RefreshPacksPath();

    /// <summary>
    /// Raised when a pack download begins.
    /// </summary>
    event EventHandler<ContentPack>? PackDownloadStarted;

    /// <summary>
    /// Raised when a pack download and installation completes.
    /// </summary>
    event EventHandler<ContentPack>? PackDownloadCompleted;

    /// <summary>
    /// Raised periodically while a pack is downloading.
    /// </summary>
    event EventHandler<(ContentPack Pack, int Progress)>? PackDownloadProgress;

    /// <summary>
    /// Raised when the installation status text changes (e.g. "Extracting...").
    /// </summary>
    event EventHandler<(ContentPack Pack, string Status)>? PackInstallStatus;

    /// <summary>
    /// Raised when a pack installation fails.
    /// </summary>
    event EventHandler<ContentPack>? PackInstallFailed;

    /// <summary>
    /// Raised when the user must authenticate before downloading.
    /// </summary>
    event EventHandler<string>? AuthenticationRequired;

    /// <summary>
    /// Raised when a pack download hits a rate limit.
    /// </summary>
    event EventHandler<(ContentPack Pack, string Message, DateTime ResetTime)>? RateLimitExceeded;
}
