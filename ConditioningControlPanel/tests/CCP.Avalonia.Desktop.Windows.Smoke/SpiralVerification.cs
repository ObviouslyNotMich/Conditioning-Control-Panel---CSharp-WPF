using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Services.Overlays;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows;

/// <summary>
/// Verifies that the spiral overlay's GIF actually animates by capturing two
/// screenshots ~700 ms apart and comparing their pixels. A non-zero pixel delta
/// proves <see cref="Image.InvalidateVisual"/> is being called after each frame.
/// </summary>
internal static class SpiralVerification
{
    public static void Attach(AppBuilder builder)
    {
        builder.AfterSetup(_ =>
            Dispatcher.UIThread.Post(async () => await RunAsync(), DispatcherPriority.Background));
    }

    private static async Task RunAsync()
    {
        try
        {
            await Task.Delay(2000); // let splash/init settle

            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var mainWindow = lifetime?.MainWindow;
            if (mainWindow == null)
            {
                Console.WriteLine("[SPIRAL] Main window not available.");
                Environment.ExitCode = 2;
                Shutdown();
                return;
            }

            var overlayService = App.Services.GetRequiredService<IOverlayService>();
            overlayService.Start();
            await Task.Delay(200);

            overlayService.ShowOverlaySustained("spiral", 0.5);
            await Task.Delay(800); // let windows open + decoder start

            var spiralWindows = lifetime.Windows
                .Where(w => w.GetType().Name.Contains("SpiralOverlayWindow", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (spiralWindows.Count == 0)
            {
                Console.WriteLine("[SPIRAL] No spiral overlay window was created.");
                Environment.ExitCode = 2;
                Shutdown();
                return;
            }

            var window = spiralWindows.First();
            var path1 = await SmokeTestRunner.RenderScreenshotAsync(window, "spiral-verify-1.png");
            await Task.Delay(700);
            var path2 = await SmokeTestRunner.RenderScreenshotAsync(window, "spiral-verify-2.png");

            double diff = CompareImageDifference(path1, path2);
            Console.WriteLine($"[SPIRAL] Pixel change ratio: {diff:F4}");

            if (diff > 0.005)
            {
                Console.WriteLine("[SPIRAL] PASS: spiral overlay is animating.");
                Environment.ExitCode = 0;
            }
            else
            {
                Console.WriteLine("[SPIRAL] FAIL: spiral overlay is frozen (no pixel change detected).");
                Environment.ExitCode = 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SPIRAL] ERROR: {ex}");
            Environment.ExitCode = 2;
        }
        finally
        {
            try
            {
                var overlayService = App.Services?.GetService<IOverlayService>();
                overlayService?.Stop();
            }
            catch { }
            Shutdown();
        }
    }

    private static void Shutdown()
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                lifetime?.Shutdown();
            }
            catch { }
        });
    }

    private static double CompareImageDifference(string pathA, string pathB)
    {
        using var bitmapA = SKBitmap.Decode(pathA);
        using var bitmapB = SKBitmap.Decode(pathB);
        if (bitmapA == null || bitmapB == null)
            return 0.0;

        if (bitmapA.Width != bitmapB.Width || bitmapA.Height != bitmapB.Height)
            return 1.0;

        long totalDiff = 0;
        long samples = 0;
        int width = bitmapA.Width;
        int height = bitmapA.Height;

        for (int y = 0; y < height; y += 4)
        {
            for (int x = 0; x < width; x += 4)
            {
                var ca = bitmapA.GetPixel(x, y);
                var cb = bitmapB.GetPixel(x, y);
                totalDiff += (uint)Math.Abs(ca.Red - cb.Red)
                           + (uint)Math.Abs(ca.Green - cb.Green)
                           + (uint)Math.Abs(ca.Blue - cb.Blue)
                           + (uint)Math.Abs(ca.Alpha - cb.Alpha);
                samples++;
            }
        }

        if (samples == 0)
            return 0.0;

        // Max possible diff per sample is 4 * 255.
        double avgDiff = totalDiff / (double)samples;
        return avgDiff / (4.0 * 255.0);
    }
}
