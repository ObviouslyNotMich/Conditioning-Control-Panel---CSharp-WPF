using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Core.Platform;
using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Android;

[Activity(
    Label = "Conditioning Control Panel",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.SmallestScreenSize)]
public class MainActivity : AvaloniaMainActivity<ConditioningControlPanel.Avalonia.App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        // Mobile apps use ISingleViewApplicationLifetime; replace the desktop LibVLC
        // registration with the mobile package's auto-discovery initialization.
        App.ConfigurePlatformServices = services =>
        {
            services.AddSingleton<LibVLC>(_ =>
            {
                LibVLCSharp.Shared.Core.Initialize();
                return new LibVLC();
            });

            services.AddSingleton<IBrowserHost, MobileBrowserHost>();
            services.AddSingleton<IFilePickerService, MobileFilePicker>();
        };

        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
