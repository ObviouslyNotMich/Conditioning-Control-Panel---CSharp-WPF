using System;
using System.Linq;
using Avalonia;
using Avalonia.Logging;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Desktop;
using ConditioningControlPanel.Avalonia.Desktop.Platform;
using ConditioningControlPanel.Avalonia.Desktop.Windows.Platform;
using ConditioningControlPanel.Avalonia.Desktop.Windows.Services;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Deeper;
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

        var smokeTest = args.Contains("--smoke-test");
        var smokeScreenshots = args.Contains("--smoke-screenshots");
        SmokeTestLogSink? smokeSink = null;
        SmokeTestRunner? smokeRunner = null;
        if (smokeTest)
        {
            smokeSink = new SmokeTestLogSink(LogEventLevel.Warning);
            Logger.Sink = smokeSink;
            smokeRunner = new SmokeTestRunner(smokeSink, captureScreenshots: smokeScreenshots);
            smokeRunner.Attach();
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
            services.AddSingleton<IStartupRegistration, WindowsStartupRegistration>();
            services.AddSingleton<IBrowserHost, WebView2BrowserHost>();
            services.AddSingleton<IAudioWaveformProvider, NAudioWaveformProvider>();
            services.AddDesktopSecretStore();
            services.AddSingleton<ISingleInstanceService>(singleInstance);

            // Use desktop LibVLC registration so Windows also benefits from explicit
            // native discovery (e.g. published runtimes folder), while still falling
            // back to the VideoLAN.LibVLC.Windows package copy in the output folder.
            services.AddDesktopLibVLC();
        };

        var builder = BuildAvaloniaApp();
        if (smokeRunner != null)
        {
            // ProgramShared.BuildAvaloniaApp replaces the log sink; restore our capturing sink.
            Logger.Sink = smokeSink;
            builder.AfterSetup(_ => smokeRunner.ScheduleRun());
        }

        builder.StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => ProgramShared.BuildAvaloniaApp();
}
