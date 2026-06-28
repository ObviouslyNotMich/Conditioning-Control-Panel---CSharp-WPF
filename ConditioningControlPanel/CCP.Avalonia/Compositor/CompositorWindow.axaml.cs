using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Avalonia.Compositor;

/// <summary>
/// A single full-screen compositor window per monitor. Hosts a <see cref="CompositorControl"/>
/// that renders directly via Skia on Avalonia's GPU render thread.
/// On Windows, the native window is styled so mouse clicks pass through to underlying
/// windows while overlays remain visible on top.
/// </summary>
public partial class CompositorWindow : Window
{
    private readonly CompositorEngine _engine;
    private readonly CompositorControl _control;
    private readonly ILogger<CompositorWindow>? _logger;

    public CompositorWindow(ScreenInfo screen, CompositorEngine engine)
    {
        InitializeComponent();
        _engine = engine;
        _logger = App.Services?.GetService<ILogger<CompositorWindow>>();

        // Use the full monitor bounds (taskbar included) so overlays cover the
        // entire screen, matching the WPF head which positions on physical bounds,
        // not the working area.
        Position = new PixelPoint((int)screen.Bounds.X, (int)screen.Bounds.Y);
        Width = screen.Bounds.Width;
        Height = screen.Bounds.Height;

        _control = new CompositorControl(engine);
        Content = _control;

        Opened += OnWindowOpened;
        Activated += OnWindowActivated;
        Deactivated += OnWindowDeactivated;
        PropertyChanged += OnPropertyChanged;
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty)
            OnWindowStateChanged();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        _logger?.LogDebug("CompositorWindow deactivated");
    }

    private void OnWindowStateChanged()
    {
        _logger?.LogDebug("CompositorWindow WindowState changed to {State}", WindowState);
        ApplyNativeTransparency();
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        // Re-apply transparency when the window is activated (e.g. when the
        // main window is minimized and Windows tries to activate this window).
        // This ensures the click-through styles remain effective.
        ApplyNativeTransparency();
    }

    public CompositorControl GetControl() => _control;

    /// <summary>
    /// Applies native click-through styles so mouse clicks pass through to underlying
    /// windows. Uses WS_EX_LAYERED | WS_EX_TRANSPARENT together — this is the reliable
    /// click-through combination. WS_EX_TRANSPARENT alone fails once the window becomes
    /// the foreground/active window (e.g. when the main app is minimized and Windows
    /// activates this topmost overlay), which then captures all clicks and locks the
    /// desktop. This matches the proven click-through overlays already in this repo
    /// (AvaloniaKeywordHighlightService, ChaosWin32Helper), which render correctly with
    /// LAYERED + TransparencyLevelHint.Transparent under Avalonia 12's DWM-composited GPU
    /// pipeline. Must be called immediately after Show().
    /// </summary>
    public void ApplyNativeTransparency()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero)
            {
                _logger?.LogWarning("ApplyNativeTransparency: no native handle");
                return;
            }

            var exStyle = (uint)GetWindowLong(hwnd, GWL_EXSTYLE).ToInt64();
            var before = exStyle;
            exStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            var after = exStyle;
            _logger?.LogDebug("ApplyNativeTransparency: EXSTYLE before={Before:X} after={After:X}", before, after);
            SetWindowLong(hwnd, GWL_EXSTYLE, new IntPtr(exStyle));

            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_NOZORDER);

            bool ok = SetWindowSubclass(hwnd, _hitTestSubclassProc, 0, IntPtr.Zero);
            _logger?.LogDebug("ApplyNativeTransparency: SetWindowSubclass returned {Ok}", ok);
            if (!ok)
            {
                var err = Marshal.GetLastWin32Error();
                _logger?.LogWarning("ApplyNativeTransparency: SetWindowSubclass failed, error={Error}", err);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "ApplyNativeTransparency: exception");
        }
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        ApplyNativeTransparency();
    }

    protected override void OnClosed(EventArgs e)
    {
        Opened -= OnWindowOpened;

        if (OperatingSystem.IsWindows())
        {
            try
            {
                var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (hwnd != IntPtr.Zero)
                    RemoveWindowSubclass(hwnd, _hitTestSubclassProc, 0);
            }
            catch { }
        }

        base.OnClosed(e);
    }

    private const int GWL_EXSTYLE = -20;
    private const int GWL_STYLE = -16;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint WS_DISABLED = 0x08000000;

    private const int WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;
    private const int WM_MOUSEACTIVATE = 0x0021;
    // MA_NOACTIVATE (3): refuse activation but let the mouse message be PROCESSED (not eaten).
    // MA_NOACTIVATEANDEAT (4) would discard the click — wrong for a click-through overlay and a
    // cause of the "minimize locks the desktop" bug. Matches AvaloniaBubbleWindow.Windows.cs.
    private const int MA_NOACTIVATE = 0x0003;

    private delegate IntPtr SubclassProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, nuint uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, nuint uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, nuint uIdSubclass);

    [DllImport("user32.dll")]
    private static extern bool EnableWindow(IntPtr hWnd, bool bEnable);

    private static readonly SubclassProc _hitTestSubclassProc = OnHitTestSubclassProc;
    private static int _hitTestCount;
    private static DateTime _lastHitTestLog = DateTime.MinValue;

    private static IntPtr OnHitTestSubclassProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, nuint uIdSubclass, IntPtr dwRefData)
    {
        if (msg == WM_NCHITTEST)
        {
            _hitTestCount++;
            if (DateTime.Now - _lastHitTestLog > TimeSpan.FromSeconds(5))
            {
                _lastHitTestLog = DateTime.Now;
                Console.WriteLine($"[CompositorWindow] WM_NCHITTEST handled ({_hitTestCount} total)");
            }
            return new IntPtr(HTTRANSPARENT);
        }
        if (msg == WM_MOUSEACTIVATE)
            return new IntPtr(MA_NOACTIVATE);

        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLong64(IntPtr hWnd, int nIndex);
    private static IntPtr GetWindowLong(IntPtr hWnd, int nIndex)
        => IntPtr.Size == 4 ? GetWindowLong32(hWnd, nIndex) : GetWindowLong64(hWnd, nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLong64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    private static IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        => IntPtr.Size == 4 ? SetWindowLong32(hWnd, nIndex, dwNewLong) : SetWindowLong64(hWnd, nIndex, dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
