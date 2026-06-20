using System.Windows;
using System.Windows.Controls;
using LibVLCSharp.Shared;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.WindowsOnly;

/// <summary>
/// WPF video surface shim for <see cref="IVideoSurface"/>.
/// This minimal implementation hosts a WPF element and tracks the attached media player.
/// A real renderer can replace the placeholder with a LibVLCSharp.WPF VideoView later.
/// </summary>
public sealed class WpfVideoSurface : IVideoSurface
{
    private readonly Border _host;
    private MediaPlayer? _player;

    public WpfVideoSurface()
    {
        _host = new Border
        {
            Background = System.Windows.Media.Brushes.Black
        };
    }

    /// <summary>
    /// The underlying WPF element. Host this in the UI tree.
    /// </summary>
    public FrameworkElement Element => _host;

    public void Attach(MediaPlayer player)
    {
        Detach();
        _player = player;
    }

    public void Detach()
    {
        _player = null;
    }
}
