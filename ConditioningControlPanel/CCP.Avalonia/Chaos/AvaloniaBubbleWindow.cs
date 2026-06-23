using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Borderless topmost window that hosts a single <see cref="AvaloniaBubble"/>.
/// </summary>
public sealed partial class AvaloniaBubbleWindow : Window
{
    private readonly AvaloniaBubble _bubble;

    public AvaloniaBubbleWindow(Bitmap? bitmap, double size)
    {
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = size;
        Height = size;

        _bubble = new AvaloniaBubble(bitmap, size);
        _bubble.Click += (_, _) => Click?.Invoke(this, EventArgs.Empty);
        Content = _bubble;
    }

    /// <summary>The hosted bubble visual.</summary>
    public AvaloniaBubble Bubble => _bubble;

    /// <summary>Scaling factor of the screen hosting this bubble, used for gaze hit-tests.</summary>
    public double Scaling { get; set; } = 1.0;

    /// <summary>Raised when the bubble inside this window is clicked.</summary>
    public event EventHandler? Click;

    /// <summary>
    /// Positions the window and updates the bubble's visual state.
    /// </summary>
    public void Place(PixelPoint position, double width, double height, double bubbleScale = 1.0, double bubbleOpacity = 1.0,
        string? label = null, (byte r, byte g, byte b)? tint = null, double fuseFraction = 1.0)
    {
        if (position.X < -30000 || position.Y < -30000) return;

        Position = position;
        Width = width;
        Height = height;
        _bubble.SetVisual(bubbleScale, bubbleOpacity);

        if (label != null)
            _bubble.SetLabel(label);

        if (tint.HasValue)
            _bubble.SetTint(tint.Value.r, tint.Value.g, tint.Value.b);

        _bubble.SetFuse(fuseFraction);
    }

    /// <summary>Closes the window on the UI thread.</summary>
    public void CloseWindow()
    {
        try
        {
            if (Dispatcher.UIThread.CheckAccess())
                Close();
            else
                Dispatcher.UIThread.Post(Close);
        }
        catch
        {
            // swallow; teardown must never break the engine
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ApplyPlatformStyles();
    }

    private partial void ApplyPlatformStyles();
}
