using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Avalonia desktop / mobile application environment paths.
/// Uses explicit platform conventions: Windows LocalAppData, macOS Library/Application Support,
/// and Linux XDG_DATA_HOME / ~/.local/share.
/// </summary>
public sealed class AvaloniaAppEnvironment : IAppEnvironment
{
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
            // TODO: respect CustomAssetsPath from settings once the Avalonia UI exposes it.
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
