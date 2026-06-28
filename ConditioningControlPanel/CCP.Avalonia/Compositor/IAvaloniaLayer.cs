using System;
using Avalonia;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Compositor;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor;

/// <summary>
/// Avalonia-specific extension of <see cref="ILayer"/> with strongly-typed SkiaSharp
/// render and update methods.
/// </summary>
public interface IAvaloniaLayer : ILayer
{
    /// <summary>Called once per frame before <see cref="Render"/>. Use for animation state updates.</summary>
    void Update(TimeSpan deltaTime);

    /// <summary>Render the layer's content onto the shared Skia canvas.</summary>
    void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, TimeSpan deltaTime);
}
