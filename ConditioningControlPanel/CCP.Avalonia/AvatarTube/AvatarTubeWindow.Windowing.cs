using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.DependencyInjection;

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
        private IScreenProvider? _screenProvider;

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
            _screenProvider = App.Services.GetService<IScreenProvider>();
            _fullscreenCheckTimer?.Stop();
            _fullscreenCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _fullscreenCheckTimer.Tick += FullscreenCheckTimer_Tick;
            _fullscreenCheckTimer.Start();
        }

        private void FullscreenCheckTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isAttached) return;
            bool fullscreen = IsOtherAppFullscreen();
            if (fullscreen && !_hiddenForFullscreen)
            {
                _hiddenForFullscreen = true;
                _wasAttachedBeforeFullscreen = _isAttached;
                Hide();
            }
            else if (!fullscreen && _hiddenForFullscreen)
            {
                _hiddenForFullscreen = false;
                if (_wasAttachedBeforeFullscreen && _settings?.Current?.AvatarEnabled == true)
                {
                    Show();
                    UpdatePosition();
                    BringAttachedPairToFront(true);
                }
            }
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
            if (_screenProvider == null) return;
            var screens = _screenProvider.GetAllScreens();
            var screen = screens.FirstOrDefault(s => s.Bounds.X <= Position.X && Position.X < s.Bounds.Right
                                                  && s.Bounds.Y <= Position.Y && Position.Y < s.Bounds.Bottom)
                        ?? _screenProvider.GetPrimaryScreen();
            if (screen == null) return;

            int w = (int)Math.Max(1, Width);
            int h = (int)Math.Max(1, Height);
            int x = Math.Clamp(Position.X, (int)screen.WorkingArea.X, (int)(screen.WorkingArea.Right - w));
            int y = Math.Clamp(Position.Y, (int)screen.WorkingArea.Y, (int)(screen.WorkingArea.Bottom - h));
            Position = new PixelPoint(x, y);
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
                double y = Math.Sin(_floatPhase) * FloatDistance;
                ApplyFloatOffset(ImgAvatar, y);
                ApplyFloatOffset(ImgAvatarAnimated, y);
            };
            _floatTimer.Start();
        }

        private static void ApplyFloatOffset(Image? img, double y)
        {
            if (img == null) return;
            if (img.RenderTransform is TranslateTransform tt)
                tt.Y = y;
            else
                img.RenderTransform = new TranslateTransform(0, y);
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
            ReassertCirceEmoteVisuals();
        }

        private void ReassertTopmost()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
#if WINDOWS
                // Windows-specific topmost reassertion would go here via platform helpers.
#endif
            }
            else
            {
                var was = Topmost;
                Topmost = false;
                Dispatcher.UIThread.Post(() => Topmost = was);
            }
        }

        private void BringToFrontTemporarily()
        {
            if (!_isAttached) return;
            BringAttachedPairToFront(true);
        }

        public void RaiseAttachedTubeAboveOwner() => BringAttachedPairToFront(true);

        private void BringAttachedPairToFront(bool force = false)
        {
            if (!_isAttached || _parentWindow == null || !_parentWindow.IsVisible || _parentWindow.WindowState == WindowState.Minimized)
                return;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
#if WINDOWS
                try
                {
                    var helper = new WindowInteropHelper(this);
                    var parentHelper = new WindowInteropHelper(_parentWindow);
                    SetWindowPos(helper.Handle, new IntPtr(-1), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                    SetWindowPos(parentHelper.Handle, new IntPtr(-1), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                }
                catch { }
#endif
            }
        }

        private void SetToolWindowStyle(bool isToolWindow)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
#if WINDOWS
            try
            {
                var helper = new WindowInteropHelper(this);
                int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
                int newExStyle = isToolWindow
                    ? exStyle | WS_EX_TOOLWINDOW
                    : exStyle & ~WS_EX_TOOLWINDOW;
                SetWindowLong(helper.Handle, GWL_EXSTYLE, newExStyle);
                SetWindowPos(helper.Handle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            }
            catch { }
#endif
        }

        private bool IsOurAppForeground()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return true;
#if WINDOWS
            try
            {
                var fg = GetForegroundWindow();
                if (fg == IntPtr.Zero) return false;
                uint pid;
                GetWindowThreadProcessId(fg, out pid);
                return pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            }
            catch { }
#endif
            return true;
        }

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
            if (active)
            {
                _reattachAfterChaos = _isAttached;
                if (_isAttached) Hide();
            }
            else if (_reattachAfterChaos)
            {
                if (_settings?.Current?.AvatarEnabled == true)
                {
                    Show();
                    UpdatePosition();
                    BringAttachedPairToFront(true);
                }
            }
        }
    }
}

#pragma warning restore CS0169
#pragma warning restore CS0414
#pragma warning restore CS0649
