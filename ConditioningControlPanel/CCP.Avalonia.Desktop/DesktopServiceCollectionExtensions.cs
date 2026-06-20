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
    /// Registers the shared <see cref="LibVLC"/> singleton using the desktop native
    /// discovery helper. This overrides the default registration in
    /// <see cref="CCP.Avalonia.ServiceCollectionExtensions.ConfigureCoreServices"/>
    /// so Linux and macOS can locate their native LibVLC libraries explicitly.
    /// </summary>
    public static IServiceCollection AddDesktopLibVLC(this IServiceCollection services)
    {
        // Replace the LibVLC registration from ConfigureCoreServices with one that
        // runs platform-specific discovery first.
        return services.AddSingleton<LibVLC>(_ =>
        {
            LibVLCNativeDiscovery.Initialize();
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
