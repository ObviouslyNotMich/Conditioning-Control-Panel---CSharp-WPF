using ConditioningControlPanel.Avalonia.Desktop.Platform;
using ConditioningControlPanel.Core.Platform;
using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Desktop;

/// <summary>
/// Desktop-specific dependency injection extensions.
/// </summary>
public static class DesktopServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared <see cref="LibVLC"/> singleton. This overrides the default
    /// registration in <see cref="CCP.Avalonia.ServiceCollectionExtensions.ConfigureCoreServices"/>
    /// so desktop heads can rely on LibVLCSharp's own runtime discovery.
    /// </summary>
    public static IServiceCollection AddDesktopLibVLC(this IServiceCollection services)
    {
        return services.AddSingleton<LibVLC>(_ =>
        {
            global::LibVLCSharp.Shared.Core.Initialize();
            return new LibVLC();
        });
    }

    /// <summary>
    /// Registers the desktop <see cref="ISecretStore"/> implementation, replacing the
    /// in-memory fallback registered by
    /// <see cref="CCP.Avalonia.ServiceCollectionExtensions.ConfigureCoreServices"/>.
    /// </summary>
    public static IServiceCollection AddDesktopSecretStore(this IServiceCollection services)
    {
        return services.AddSingleton<ISecretStore, DesktopSecretStore>();
    }

    /// <summary>
    /// Registers the desktop single-instance service, replacing the no-op fallback
    /// registered by <see cref="CCP.Avalonia.ServiceCollectionExtensions.ConfigureCoreServices"/>.
    /// </summary>
    public static IServiceCollection AddDesktopSingleInstance(this IServiceCollection services)
    {
        return services.AddSingleton<ISingleInstanceService, DesktopSingleInstanceService>();
    }

    /// <summary>
    /// Registers the desktop wallpaper provider (Linux/macOS), replacing the no-op
    /// fallback registered by
    /// <see cref="CCP.Avalonia.ServiceCollectionExtensions.ConfigureCoreServices"/>.
    /// </summary>
    public static IServiceCollection AddDesktopWallpaper(this IServiceCollection services)
    {
        return services.AddSingleton<IWallpaperProvider, DesktopWallpaperProvider>();
    }
}
