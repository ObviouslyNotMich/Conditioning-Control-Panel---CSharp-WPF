using System;
using Avalonia;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Desktop;
using ConditioningControlPanel.Avalonia.Desktop.Platform;
using ConditioningControlPanel.Avalonia.Desktop.Windows.Platform;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        using var singleInstance = new DesktopSingleInstanceService();
        if (!singleInstance.IsFirstInstance)
        {
            singleInstance.SignalFirstInstance(args);
            return;
        }

        App.ConfigurePlatformServices = services =>
        {
            services.AddSingleton<IWallpaperProvider, WpfWallpaperProvider>();
            services.AddSingleton<IHotkeyProvider, WpfHotkeyProvider>();
            services.AddSingleton<IInputHook, WpfInputHook>();
            services.AddTransient<IOverlaySurface, WindowsOverlaySurface>();
            services.AddSingleton<IFrameSource, WindowsFrameSource>();
            services.AddSingleton<ISystemAudioDucker, WindowsSystemAudioDucker>();
            services.AddSingleton<IUpdateInstaller, WindowsUpdateInstaller>();
            services.AddSingleton<IWindowChrome, WindowsWindowChrome>();
            services.AddSingleton<IAudioDeviceService, WindowsAudioDeviceService>();
            services.AddSingleton<IBrowserHost, WebView2BrowserHost>();
            services.AddDesktopSecretStore();
            services.AddSingleton<ISingleInstanceService>(singleInstance);

            // Use desktop LibVLC registration so Windows also benefits from explicit
            // native discovery (e.g. published runtimes folder), while still falling
            // back to the VideoLAN.LibVLC.Windows package copy in the output folder.
            services.AddDesktopLibVLC();
        };

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => ProgramShared.BuildAvaloniaApp();
}
