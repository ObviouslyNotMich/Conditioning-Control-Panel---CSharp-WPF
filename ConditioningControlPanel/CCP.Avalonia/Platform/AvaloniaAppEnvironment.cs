using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Avalonia desktop / mobile application environment paths.
/// Uses explicit platform conventions: Windows LocalAppData, macOS Library/Application Support,
/// and Linux XDG_DATA_HOME / ~/.local/share.
/// </summary>
public sealed class AvaloniaAppEnvironment : IAppEnvironment
{
    private readonly IServiceProvider? _services;

    public AvaloniaAppEnvironment(IServiceProvider? services = null)
    {
        _services = services;
    }

    private ISettingsService? SettingsService => _services?.GetService<ISettingsService>();

    public string BaseDirectory => AppContext.BaseDirectory;

    public string UserDataPath => GetUserDataPath();

    // ponytail: one user-data path; legacy WPF and Core services must share the
    // same Local folder. Roaming was a drift bug that split session logs / custom
    // sessions / moderation counter away from existing user data.
    public string ApplicationDataPath => UserDataPath;

    public string EffectiveAssetsPath
    {
        get
        {
            var customPath = SettingsService?.Current?.CustomAssetsPath;
            if (!string.IsNullOrWhiteSpace(customPath))
            {
                try
                {
                    if (Directory.Exists(customPath))
                        return customPath;
                }
                catch
                {
                    // Fall back to default if the custom path is invalid.
                }
            }
            return Path.Combine(UserDataPath, "assets");
        }
    }

    private static string GetUserDataPath()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ConditioningControlPanel");
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "Application Support",
                "ConditioningControlPanel");
        }

        // Linux: prefer XDG_DATA_HOME, then fall back to ~/.local/share.
        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrEmpty(xdgDataHome))
        {
            return Path.Combine(xdgDataHome, "ConditioningControlPanel");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local",
            "share",
            "ConditioningControlPanel");
    }

    /// <summary>
    /// One-time migration: move anything the Avalonia head previously wrote to the
    /// Windows Roaming folder into the Local folder so it shares the legacy WPF path.
    /// Skipped if a sentinel file already exists or the Roaming folder is absent.
    /// </summary>
    public static void MigrateFromLegacyRoamingPath()
    {
        if (!OperatingSystem.IsWindows()) return;

        var roaming = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ConditioningControlPanel");
        var local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConditioningControlPanel");

        if (string.Equals(roaming, local, StringComparison.OrdinalIgnoreCase)) return;
        if (!Directory.Exists(roaming)) return;

        Directory.CreateDirectory(local);
        var sentinel = Path.Combine(local, ".roaming-migrated");
        if (File.Exists(sentinel)) return;

        foreach (var entry in Directory.EnumerateFileSystemEntries(roaming))
        {
            var name = Path.GetFileName(entry);
            var dest = Path.Combine(local, name);
            if (File.Exists(dest) || Directory.Exists(dest))
            {
                dest = Path.Combine(local, $"{name}.roaming-merge-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}");
            }

            try
            {
                if (Directory.Exists(entry))
                    Directory.Move(entry, dest);
                else
                    File.Move(entry, dest);
            }
            catch
            {
                // Cross-volume or locked: copy then delete. If that also fails,
                // leave the original in place for manual cleanup rather than crash.
                try
                {
                    CopyRecursive(entry, dest);
                }
                catch { /* best effort */ }
            }
        }

        try { File.WriteAllText(sentinel, DateTimeOffset.UtcNow.ToString("O")); }
        catch { /* best effort */ }
    }

    private static void CopyRecursive(string source, string destination)
    {
        if (File.Exists(source))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
            File.Delete(source);
            return;
        }

        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var destFile = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            File.Copy(file, destFile, overwrite: true);
        }
        Directory.Delete(source, recursive: true);
    }
}
