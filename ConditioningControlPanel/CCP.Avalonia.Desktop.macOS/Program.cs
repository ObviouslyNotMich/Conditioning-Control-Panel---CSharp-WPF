using System;
using Avalonia;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Desktop;
using ConditioningControlPanel.Avalonia.Desktop.macOS.Platform;
using ConditioningControlPanel.Avalonia.Desktop.Platform;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Desktop.macOS;

class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("[CCP macOS] Process started.");
        try
        {
            using var singleInstance = new DesktopSingleInstanceService();
            if (!singleInstance.IsFirstInstance)
            {
                singleInstance.SignalFirstInstance(args);
                return;
            }

            var builder = BuildAvaloniaApp();
            var previousConfigure = App.ConfigurePlatformServices;
            App.ConfigurePlatformServices = services =>
            {
                previousConfigure?.Invoke(services);
                services.AddSingleton<ISingleInstanceService>(singleInstance);
            };

            builder.StartWithClassicDesktopLifetime(args);
            Console.WriteLine("[CCP macOS] StartWithClassicDesktopLifetime returned cleanly.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CCP macOS] FATAL: {ex.GetType().FullName}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        App.ConfigurePlatformServices = services =>
        {
            // Register LibVLC with macOS native discovery. The VideoLAN.LibVLC.Mac
            // package ships x64 libraries; ARM64 machines can also use Homebrew/
            // MacPorts installs that LibVLCNativeDiscovery locates at runtime.
            services.AddDesktopLibVLC();
            services.AddDesktopSecretStore();
            services.AddDesktopSingleInstance();
            services.AddDesktopWallpaper();
            services.AddSingleton<IBrowserHost, WebKitBrowserHost>();
        };

        return ProgramShared.BuildAvaloniaApp();
    }
}
