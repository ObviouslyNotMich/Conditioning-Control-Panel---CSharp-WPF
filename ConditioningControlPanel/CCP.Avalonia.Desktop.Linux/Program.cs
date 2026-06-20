using System;
using Avalonia;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Desktop;
using ConditioningControlPanel.Avalonia.Desktop.Linux.Platform;
using ConditioningControlPanel.Avalonia.Desktop.Platform;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Desktop.Linux;

class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("[CCP Linux] Process started.");
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
            Console.WriteLine("[CCP Linux] StartWithClassicDesktopLifetime returned cleanly.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CCP Linux] FATAL: {ex.GetType().FullName}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        App.ConfigurePlatformServices = services =>
        {
            // Register LibVLC with Linux/macOS native discovery so the app can find
            // system-installed or NuGet-provided LibVLC libraries at runtime.
            services.AddDesktopLibVLC();
            services.AddDesktopSecretStore();
            services.AddDesktopSingleInstance();
            services.AddDesktopWallpaper();
            services.AddSingleton<IBrowserHost, WebKitGtkBrowserHost>();
        };

        return ProgramShared.BuildAvaloniaApp();
    }
}
