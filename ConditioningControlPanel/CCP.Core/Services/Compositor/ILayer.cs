using System;

namespace ConditioningControlPanel.Core.Services.Compositor;

/// <summary>
/// Minimal portable contract for a compositor layer. Avalonia-specific layers implement
/// <c>IAvaloniaLayer</c> in <c>CCP.Avalonia</c> which adds the strongly-typed render method.
/// </summary>
public interface ILayer
{
    /// <summary>The z-index of this layer. Lower values are rendered first (behind).</summary>
    int ZIndex { get; }

    /// <summary>Whether the layer currently has visible content to render.</summary>
    bool IsActive { get; }

    /// <summary>Called when the layer is first registered with the compositor.</summary>
    void OnActivated();

    /// <summary>Called when the layer is unregistered from the compositor.</summary>
    void OnDeactivated();
}
