using System;
using Avalonia;
using Avalonia.Logging;
#if DEBUG
using AvaloniaUI.DiagnosticsSupport;
#endif
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Desktop.Platform;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Desktop;

public static class ProgramShared
{
    [STAThread]
    public static void Main(string[] args)
    {
        Run(args);
    }

    /// <summary>
    /// Shared entry point for Linux and macOS desktop heads. Handles single-instance
    /// enforcement and wires up the common desktop platform services, then lets the
    /// head inject any additional services via <paramref name="configurePlatformServices"/>.
    /// </summary>
    public static void Run(string[] args, Action<IServiceCollection>? configurePlatformServices = null)
    {
        using var singleInstance = new DesktopSingleInstanceService();
        if (!singleInstance.IsFirstInstance)
        {
            singleInstance.SignalFirstInstance(args);
            return;
        }

        App.ConfigurePlatformServices = services =>
        {
            services.AddDesktopLibVLC();
            services.AddDesktopSecretStore();
            services.AddSingleton<IWallpaperProvider, DesktopWallpaperProvider>();
            configurePlatformServices?.Invoke(services);
            services.AddSingleton<ISingleInstanceService>(singleInstance);
        };

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace(LogEventLevel.Debug)
#if DEBUG
            .WithDeveloperTools()
#endif
            ;
    }
}
