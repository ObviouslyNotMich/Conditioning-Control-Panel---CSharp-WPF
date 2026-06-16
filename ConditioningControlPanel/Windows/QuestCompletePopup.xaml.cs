using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ConditioningControlPanel;

/// <summary>
/// Popup window shown when a quest is completed
/// </summary>
public partial class QuestCompletePopup : Window
{
    private readonly DispatcherTimer _autoCloseTimer;

    public QuestCompletePopup(string questName, int xpAwarded)
    {
        InitializeComponent();

        TxtQuestName.Text = questName;
        TxtXPAwarded.Text = $"+{xpAwarded} XP";

        PositionWindow();

        // Auto-close after 5 seconds
        _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _autoCloseTimer.Tick += (s, e) =>
        {
            _autoCloseTimer.Stop();
            FadeOutAndClose();
        };
        _autoCloseTimer.Start();

        // Fade in
        Opacity = 0;
        Loaded += (s, e) =>
        {
            // Re-assert to the very top of the topmost z-band WITHOUT activating, so the popup
            // is visible over an in-app fullscreen video (which is also Topmost but was activated
            // more recently and would otherwise sit above us). NOACTIVATE keeps video playback
            // focused/uninterrupted. Beats the app's own fullscreen surfaces (#332). (A browser
            // HTML5-fullscreen Chromium surface may still win — the inline QuestCompleteBanner is
            // the fallback there.)
            ForceToTopMost();

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
            BeginAnimation(OpacityProperty, fadeIn);
        };
    }

    private void ForceToTopMost()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "QuestCompletePopup: ForceToTopMost failed");
        }
    }

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    private void PositionWindow()
    {
        try
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 20;
            Top = workArea.Bottom - Height - 20;
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Failed to position quest complete popup");
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void FadeOutAndClose()
    {
        try
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            fadeOut.Completed += (s, e) =>
            {
                try { Close(); }
                catch { }
            };
            BeginAnimation(OpacityProperty, fadeOut);
        }
        catch
        {
            try { Close(); } catch { }
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        _autoCloseTimer.Stop();
        FadeOutAndClose();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        FadeOutAndClose();
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoCloseTimer.Stop();
        base.OnClosed(e);
    }
}
