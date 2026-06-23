using System.Runtime.InteropServices;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Windows-only low-level mouse hook (WH_MOUSE_LL) for the Avalonia head.
/// Reports global left/right button down events to the bubble service and other consumers.
/// On non-Windows platforms the events never fire.
/// </summary>
public sealed class AvaloniaMouseHook : IMouseHook
{
    private readonly ILogger<AvaloniaMouseHook>? _logger;

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
    private LowLevelMouseProc? _mouseProc;
    private IntPtr _mouseHook = IntPtr.Zero;

    public AvaloniaMouseHook(ILogger<AvaloniaMouseHook>? logger = null)
    {
        _logger = logger;
    }

    public event EventHandler<HookPoint>? LeftButtonDown;
    public event EventHandler<HookPoint>? RightButtonDown;
    public event EventHandler<HookPoint>? RightButtonUp;
    public event EventHandler<HookPoint>? LeftButtonUp;

    public void Install()
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger?.LogInformation("Low-level mouse hooks are only supported on Windows in the Avalonia head.");
            return;
        }

        if (_mouseHook != IntPtr.Zero)
            return;

        try
        {
            _mouseProc = MouseHookCallback;
            var moduleHandle = GetModuleHandle(null);
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, moduleHandle, 0);
            if (_mouseHook == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                _logger?.LogWarning("SetWindowsHookEx(WH_MOUSE_LL) failed with error {Error}", error);
                _mouseProc = null;
            }
            else
            {
                _logger?.LogInformation("Low-level mouse hook installed");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to install low-level mouse hook");
            _mouseProc = null;
        }
    }

    public void Uninstall()
    {
        if (_mouseHook != IntPtr.Zero && OperatingSystem.IsWindows())
        {
            try
            {
                UnhookWindowsHookEx(_mouseHook);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to uninstall low-level mouse hook");
            }
            _mouseHook = IntPtr.Zero;
        }
        _mouseProc = null;
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && lParam != IntPtr.Zero)
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var pt = new HookPoint(info.pt.X, info.pt.Y);

            try
            {
                if (wParam == (IntPtr)WM_LBUTTONDOWN)
                {
                    LeftButtonDown?.Invoke(this, pt);
                }
                else if (wParam == (IntPtr)WM_RBUTTONDOWN)
                {
                    RightButtonDown?.Invoke(this, pt);
                }
                else if (wParam == (IntPtr)WM_RBUTTONUP)
                {
                    RightButtonUp?.Invoke(this, pt);
                }
                else if (wParam == (IntPtr)WM_LBUTTONUP)
                {
                    LeftButtonUp?.Invoke(this, pt);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Exception in low-level mouse hook handler");
            }
        }

        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}
