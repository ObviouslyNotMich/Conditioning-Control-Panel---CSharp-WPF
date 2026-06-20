using System;
using Avalonia;
using Avalonia.Logging;
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
        App.ConfigurePlatformServices = services =>
        {
            services.AddDesktopLibVLC();
            services.AddDesktopSecretStore();
            services.AddSingleton<IWallpaperProvider, DesktopWallpaperProvider>();
        };

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        // Route Avalonia logs to stdout so smoke-test logs capture crashes.
        Logger.Sink = new ConsoleLogSink(LogEventLevel.Debug);

        return AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>()
            .UsePlatformDetect()
            .WithInterFont();
    }
}

/// <summary>
/// Simple Avalonia log sink that writes to the console. Avoids relying on
/// AppBuilder extension methods that change between Avalonia versions.
/// </summary>
internal sealed class ConsoleLogSink : ILogSink
{
    private readonly LogEventLevel _minimumLevel;

    public ConsoleLogSink(LogEventLevel minimumLevel = LogEventLevel.Warning)
    {
        _minimumLevel = minimumLevel;
    }

    public bool IsEnabled(LogEventLevel level, string area)
        => level >= _minimumLevel;

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
    {
        if (!IsEnabled(level, area)) return;
        Console.WriteLine($"[{level}] [{area}] {messageTemplate}");
    }

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        if (!IsEnabled(level, area)) return;
        string message;
        try
        {
            message = string.Format(messageTemplate, propertyValues);
        }
        catch (FormatException)
        {
            // Some Avalonia message templates contain braces that are not format
            // placeholders. Fall back to showing the raw template and values.
            message = $"{messageTemplate} | values: {string.Join(", ", propertyValues ?? Array.Empty<object>())}";
        }
        Console.WriteLine($"[{level}] [{area}] {message}");
    }
}
