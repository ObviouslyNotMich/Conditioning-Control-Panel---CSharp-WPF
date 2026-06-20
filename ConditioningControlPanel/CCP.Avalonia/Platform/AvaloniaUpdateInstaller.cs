using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Cross-platform update installer for the Avalonia head.
/// Reports the running assembly version and can download an update package to a temporary location.
/// Actual installation is platform-specific and is deferred to per-platform desktop heads.
/// </summary>
public class AvaloniaUpdateInstaller : IUpdateInstaller
{
    private readonly HttpClient _httpClient = new();
    private string? _downloadedPackagePath;

    public string? GetInstalledVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        if (version != null)
            return $"{version.Major}.{version.Minor}.{version.Build}";

        var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(infoVersion))
        {
            var plusIndex = infoVersion.IndexOf('+');
            return plusIndex > 0 ? infoVersion[..plusIndex] : infoVersion;
        }

        var fileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        return fileVersion;
    }

    public async Task<bool> DownloadUpdateAsync(Uri downloadUri, IProgress<double> progress, CancellationToken cancellationToken = default)
    {
        try
        {
            _downloadedPackagePath = null;
            using var response = await _httpClient.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            var tempPath = Path.Combine(Path.GetTempPath(), $"CCP-update-{Guid.NewGuid()}{Path.GetExtension(downloadUri.LocalPath)}");
            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            if (totalBytes.HasValue)
            {
                var buffer = new byte[81920];
                long readBytes = 0;
                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    readBytes += bytesRead;
                    progress?.Report(readBytes / (double)totalBytes.Value);
                }
            }
            else
            {
                await contentStream.CopyToAsync(fileStream, cancellationToken);
                progress?.Report(1.0);
            }

            _downloadedPackagePath = tempPath;
            return true;
        }
        catch (Exception)
        {
            progress?.Report(0);
            return false;
        }
    }

    public virtual Task InstallUpdateAsync()
    {
        // Cross-platform installation is deferred to per-platform heads (Windows installer, macOS DMG, Linux AppImage, etc.).
        // The downloaded package is available at _downloadedPackagePath.
        return Task.CompletedTask;
    }

    public string? GetDownloadedPackagePath() => _downloadedPackagePath;
}
