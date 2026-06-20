using LibVLCSharp.Shared;

namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Platform-agnostic video surface.
/// </summary>
public interface IVideoSurface
{
    void Attach(MediaPlayer player);
    void Detach();
}
