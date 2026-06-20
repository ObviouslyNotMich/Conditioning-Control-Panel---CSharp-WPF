using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.WindowsOnly;

/// <summary>
/// GDI+ screen capture shim for <see cref="IFrameSource"/>.
/// </summary>
public sealed class WpfFrameSource : IFrameSource
{
    public Task<RawFrame> CaptureAsync(ScreenInfo screen, CancellationToken cancellationToken = default)
    {
        var bounds = screen.Bounds;
        var x = (int)bounds.X;
        var y = (int)bounds.Y;
        var width = (int)bounds.Width;
        var height = (int)bounds.Height;

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        }

        var rect = new Rectangle(0, 0, width, height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var stride = data.Stride;
        var bytes = new byte[stride * height];
        Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
        bitmap.UnlockBits(data);

        return Task.FromResult(new RawFrame(width, height, bytes));
    }
}
