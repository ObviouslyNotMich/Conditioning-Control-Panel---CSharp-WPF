using System;
using Avalonia;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.AvatarTube;

namespace ConditioningControlPanel.Avalonia.Views;

public partial class MainWindow
{
    private AvatarTubeWindow? _avatarTubeWindow;

    private void InitializeAvatarTube()
    {
        if (_avatarTubeWindow != null)
            return;

        if (_settingsService?.Current?.AvatarEnabled != true)
            return;

        try
        {
            var win = new AvatarTubeWindow(this);
            _avatarTubeWindow = win;

            if (_settingsService.Current.AvatarMuted)
                win.SetMuted(true);

            if (IsVisible && WindowState != WindowState.Minimized)
            {
                win.ShowTube();
                win.StartPoseAnimation();
            }

            _logger?.Information("Avatar Tube Window initialized (visible={Visible}, pos={Pos}, size={Size})",
                win.IsVisible, win.Position, win.ClientSize);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to initialize Avatar Tube Window");
        }
    }

    public void ShowAvatarTube()
    {
        if (_settingsService?.Current?.AvatarEnabled != true)
            return;

        if (_avatarTubeWindow == null)
        {
            InitializeAvatarTube();
        }
        else
        {
            _avatarTubeWindow.ShowTube();
            _avatarTubeWindow.StartPoseAnimation();
        }
    }

    public void HideAvatarTube()
    {
        if (_avatarTubeWindow != null)
        {
            _avatarTubeWindow.StopPoseAnimation();
            _avatarTubeWindow.HideTube();
        }
    }

    public void WakeBambiUp()
    {
        if (_avatarTubeWindow == null)
            InitializeAvatarTube();

        if (_avatarTubeWindow != null)
        {
            _avatarTubeWindow.Show();
            _avatarTubeWindow.StartPoseAnimation();
        }
    }

    /// <summary>
    /// Shift the main window right enough that the attached avatar tube is not clipped
    /// by the left edge of the working area. Keeps the dashboard + companion pair fully
    /// visible on smaller screens.
    /// </summary>
    public void EnsureAvatarTubeFitsOnScreen()
    {
        if (_avatarTubeWindow == null) return;

        try
        {
            var screen = Screens.ScreenFromWindow(this);
            if (screen == null) return;

            const int tubeWidthEstimate = 550;
            const int padding = 10;
            int minLeft = (int)(screen.WorkingArea.X + padding + tubeWidthEstimate);
            int maxLeft = (int)(screen.WorkingArea.Right - ClientSize.Width - padding);

            int desiredLeft = Math.Max(Position.X, minLeft);
            if (maxLeft > minLeft)
                desiredLeft = Math.Min(desiredLeft, maxLeft);

            if (desiredLeft != Position.X)
            {
                Position = new PixelPoint(desiredLeft, Position.Y);
            }
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Failed to ensure avatar tube fits on screen");
        }
    }
}
