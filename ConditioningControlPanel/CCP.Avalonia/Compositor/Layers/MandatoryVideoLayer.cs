using LibVLCSharp.Shared;
using Microsoft.Extensions.Logging;

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
    { }
}
