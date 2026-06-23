using System;
using Avalonia;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Desktop;
using ConditioningControlPanel.Avalonia.Desktop.Linux.Platform;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Desktop.Linux;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Console.WriteLine("[CCP Linux] Process started.");
        try
        {
            ProgramShared.Run(
                args,
                services => services.AddSingleton<IBrowserHost, WebKitGtkBrowserHost>());

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
        => ProgramShared.BuildAvaloniaApp();
}
