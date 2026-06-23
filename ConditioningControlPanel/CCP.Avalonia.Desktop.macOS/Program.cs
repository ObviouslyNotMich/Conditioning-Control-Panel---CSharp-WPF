using System;
using Avalonia;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Desktop;
using ConditioningControlPanel.Avalonia.Desktop.macOS.Platform;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Desktop.macOS;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Console.WriteLine("[CCP macOS] Process started.");
        try
        {
            ProgramShared.Run(
                args,
                services => services.AddSingleton<IBrowserHost, WebKitBrowserHost>());

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
        => ProgramShared.BuildAvaloniaApp();
}
