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

    public string ApplicationDataPath => OperatingSystem.IsWindows()
        ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ConditioningControlPanel")
        : UserDataPath;

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
}
