using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Models;

/// <summary>
/// A single word detected on screen by the OCR engine, with its bounding
/// rectangle in screen coordinates and the screen it was captured from.
/// </summary>
public sealed class OcrWordHit
{
    public string Text { get; set; } = "";

    /// <summary>
    /// Bounding rectangle in screen (pixel) coordinates.
    /// </summary>
    public PixelRect ScreenRect { get; set; }

    /// <summary>
    /// The screen this word was detected on. May be null for text-only matches
    /// (clipboard, keyboard buffer) that have no screen position.
    /// </summary>
    public ScreenInfo? Screen { get; set; }
}
