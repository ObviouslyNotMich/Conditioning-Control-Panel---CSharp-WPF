using LibVLCSharp.Shared;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor.Layers;

/// <summary>
/// Video layer for mandatory attention-check videos. Same rendering as <see cref="VideoLayer"/>
/// but sits at Z=15 so it renders above lock cards (Z=20 is wrong — lock cards should be ABOVE
/// video, so actually mandatory video is below lock cards; Z=15 puts it between regular video
/// and lock cards, which is correct for attention checks).
/// </summary>
public sealed class MandatoryVideoLayer : VideoLayer
{
    public override int ZIndex => CompositorLayers.MandatoryVideo;

    public MandatoryVideoLayer(LibVLC libVlc, ILogger? logger = null)
        : base(libVlc, logger)
    {
        // A mandatory video must fully occlude the screen so the user cannot reach the desktop
        // through the letterbox bars when the monitor's aspect ratio differs from the clip
        // (e.g. a landscape clip on a portrait monitor). White fills the bars opaquely and reads
        // as an attention-grabbing backdrop.
        BackgroundColor = SKColors.White;
    }
}
