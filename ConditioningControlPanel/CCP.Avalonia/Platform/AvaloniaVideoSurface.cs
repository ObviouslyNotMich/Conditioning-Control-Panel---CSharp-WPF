using LibVLCSharp.Avalonia;
using LibVLCSharp.Shared;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Avalonia/LibVLC video surface adapter.
/// </summary>
public sealed class AvaloniaVideoSurface : IVideoSurface
{
    private readonly VideoView _videoView;

    public AvaloniaVideoSurface(VideoView videoView) => _videoView = videoView;

    public void Attach(MediaPlayer player) => _videoView.MediaPlayer = player;

    public void Detach() => _videoView.MediaPlayer = null;
}
