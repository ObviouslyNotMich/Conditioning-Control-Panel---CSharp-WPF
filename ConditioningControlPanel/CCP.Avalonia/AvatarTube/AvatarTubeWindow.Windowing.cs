using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

#pragma warning disable CS0169 // Avalonia port: unused stub fields kept for future companion/avatar work
#pragma warning disable CS0414
#pragma warning disable CS0649

namespace ConditioningControlPanel.Avalonia.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        private IntPtr _tubeHandle;
        private IntPtr _parentHandle;
        private bool _reassertingAboveParent;

        // Win32 constants (kept for reference; P/Invoke calls are Windows-only and stubbed here).
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint GW_HWNDPREV = 3;
        private const int GWL_EXSTYLE = -20;
        private const int GWL_STYLE = -16;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TOPMOST = 0x00000008;
        private const uint WS_POPUP = 0x80000000;
        private const uint WS_CAPTION = 0x00C00000;

#if WINDOWS
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr hWnd);
#endif

        private void StartFullscreenDetection()
        {
            _fullscreenCheckTimer?.Stop();
            _fullscreenCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _fullscreenCheckTimer.Tick += FullscreenCheckTimer_Tick;
            _fullscreenCheckTimer.Start();
        }

        private void FullscreenCheckTimer_Tick(object? sender, EventArgs e)
        {
            // TODO: cross-platform fullscreen detection and attached-mode hide/show.
        }

        private bool IsOtherAppFullscreen() => false;

        private void CalculateScaleFactor()
        {
            _scaleFactor = 0.7;
            if (ContentViewbox != null)
            {
                ContentViewbox.Width = DesignWidth * _scaleFactor;
                ContentViewbox.Height = DesignHeight * _scaleFactor;
            }
        }

        private void ApplyScale()
        {
            if (ContentViewbox != null)
            {
                ContentViewbox.Width = DesignWidth * _scaleFactor * _currentScale;
                ContentViewbox.Height = DesignHeight * _scaleFactor * _currentScale;
            }
            ClampAvatarPosition();
        }

        private void ClampAvatarPosition()
        {
            // TODO: keep detached avatar within screen bounds using Avalonia Screens API.
        }

        public void UpdatePosition()
        {
            if (!_isAttached || _parentWindow == null) return;
            if (_parentWindow.ClientSize.Height <= 0 || _parentWindow.ClientSize.Width <= 0) return;
            if (_parentWindow.Position.X == 0 && _parentWindow.Position.Y == 0 && _parentWindow.ClientSize.Height < 100) return;

            double actualWidth = ClientSize.Width > 0 ? ClientSize.Width : DesignWidth * _scaleFactor;
            double actualHeight = ClientSize.Height > 0 ? ClientSize.Height : DesignHeight * _scaleFactor;
            double scaledOffset = BaseOffsetFromParent * _scaleFactor;

            double newLeft = _parentWindow.Position.X - actualWidth - scaledOffset;
            double newTop = _parentWindow.Position.Y + (_parentWindow.ClientSize.Height - actualHeight) / 2 + (VerticalOffset * _scaleFactor);
            if (newTop < -500 || newTop > 5000 || newLeft < -2000 || newLeft > 5000) return;

            Position = new PixelPoint((int)newLeft, (int)newTop);
        }

        private void StartFloatingAnimation()
        {
            StopFloatingAnimation();
            _floatPhase = 0;
            _floatTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _floatTimer.Tick += (_, _) =>
            {
                _floatPhase += 0.05;
                // TODO: animate the TranslateTransform inside ImgAvatar.RenderTransform.
            };
            _floatTimer.Start();
        }

        private void StopFloatingAnimation()
        {
            _floatTimer?.Stop();
            _floatTimer = null;
        }

        private void Attach()
        {
            _isAttached = true;
            Topmost = false;
            Show();
            UpdatePosition();
            BringAttachedPairToFront(true);
        }

        private void Detach()
        {
            _isAttached = false;
            Topmost = true;
            Show();
            ReassertTopmost();
        }

        private void ReassertTopmost()
        {
            // TODO: pulse Topmost true->false->true on non-Windows; on Windows use SetWindowPos HWND_TOPMOST.
        }

        private void BringToFrontTemporarily()
        {
            if (!_isAttached) return;
            // TODO: Windows SetWindowPos raise.
        }

        public void RaiseAttachedTubeAboveOwner() => BringAttachedPairToFront(true);

        private void BringAttachedPairToFront(bool force = false)
        {
            if (!_isAttached || _parentWindow == null || !_parentWindow.IsVisible || _parentWindow.WindowState == WindowState.Minimized)
                return;
            // TODO: raise parent and tube above owner on Windows; on Linux/macOS this is a no-op.
        }

        private void SetToolWindowStyle(bool isToolWindow)
        {
            // TODO: Windows-only WS_EX_TOOLWINDOW toggle.
        }

        private bool IsOurAppForeground() => true;

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            return IntPtr.Zero;
        }

        private IntPtr ParentWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            return IntPtr.Zero;
        }

        public void SetChaosRunActive(bool active)
        {
            _chaosRunActive = active;
            // TODO: hide/restore tube around chaos runs.
        }
    }
}

#pragma warning restore CS0169
#pragma warning restore CS0414
#pragma warning restore CS0649
