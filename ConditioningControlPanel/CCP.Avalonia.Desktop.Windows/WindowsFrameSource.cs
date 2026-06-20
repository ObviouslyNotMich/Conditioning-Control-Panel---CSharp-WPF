using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows;

/// <summary>
/// Windows desktop frame capture using GDI (via <see cref="Graphics.CopyFromScreen"/>,
/// which internally uses BitBlt). Returns 32-bit BGRA data suitable for CPU-side effects.
/// </summary>
/// <remarks>
/// TODO: Account for per-monitor DPI scaling if <see cref="ScreenInfo.Bounds"/> is provided
/// in device-independent pixels rather than physical pixels.
/// </remarks>
public sealed class WindowsFrameSource : IFrameSource
{
    public Task<RawFrame> CaptureAsync(ScreenInfo screen, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var bounds = screen.Bounds;
        var x = (int)bounds.X;
        var y = (int)bounds.Y;
        var width = Math.Max(1, (int)bounds.Width);
        var height = Math.Max(1, (int)bounds.Height);

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var rect = new Rectangle(0, 0, width, height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride;
            var bytes = new byte[stride * height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            return Task.FromResult(new RawFrame(width, height, bytes));
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
