using System.Runtime.InteropServices;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Low-level input hook implementation.
/// On Windows it installs WH_KEYBOARD_LL and WH_MOUSE_LL hooks so panic keys, hotkeys
/// and idle-detection mouse movement work in the Avalonia head.
/// On Linux/macOS/mobile it degrades gracefully to a no-op because global hooks require
/// platform-native interop that is not available in the shared Avalonia project.
/// </summary>
public sealed class AvaloniaInputHook : IInputHook
{
    private readonly ILogger<AvaloniaInputHook>? _logger;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    private HookProc? _keyboardProc;
    private HookProc? _mouseProc;
    private IntPtr _keyboardHook = IntPtr.Zero;
    private IntPtr _mouseHook = IntPtr.Zero;

    public AvaloniaInputHook(ILogger<AvaloniaInputHook>? logger = null)
    {
        _logger = logger;
        Start();
    }

    public event EventHandler<KeyboardHookEventArgs>? KeyPressed;
    public event EventHandler<MouseHookEventArgs>? MouseMoved;

    public bool CanSuppressKeys => false;

    public bool SuppressKey(int virtualKeyCode) => false;

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
        {
            Unhook(ref _keyboardHook, "keyboard");
            Unhook(ref _mouseHook, "mouse");
        }

        _keyboardProc = null;
        _mouseProc = null;
    }

    public AvaloniaInputHook Start()
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger?.LogInformation("Low-level input hooks are only supported on Windows in the Avalonia head.");
            return this;
        }

        InstallKeyboardHook();
        InstallMouseHook();
        return this;
    }

    private void InstallKeyboardHook()
    {
        if (_keyboardHook != IntPtr.Zero)
            return;

        try
        {
            _keyboardProc = KeyboardHookCallback;
            var moduleHandle = GetModuleHandle(null);
            _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);
            if (_keyboardHook == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                _logger?.LogWarning("SetWindowsHookEx(WH_KEYBOARD_LL) failed with error {Error}", error);
                _keyboardProc = null;
            }
            else
            {
                _logger?.LogDebug("Low-level keyboard hook installed");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to install low-level keyboard hook");
            _keyboardProc = null;
        }
    }

    private void InstallMouseHook()
    {
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
                _logger?.LogDebug("Low-level mouse hook installed");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to install low-level mouse hook");
            _mouseProc = null;
        }
    }

    private void Unhook(ref IntPtr hook, string name)
    {
        if (hook == IntPtr.Zero)
            return;

        try
        {
            UnhookWindowsHookEx(hook);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to uninstall low-level {Name} hook", name);
        }

        hook = IntPtr.Zero;
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && lParam != IntPtr.Zero)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var keyArgs = new KeyboardHookEventArgs((int)info.vkCode, false, false, false, false);
            try
            {
                KeyPressed?.Invoke(this, keyArgs);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Exception in low-level keyboard hook handler");
            }
        }

        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && lParam != IntPtr.Zero && wParam.ToInt32() == WM_MOUSEMOVE)
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            try
            {
                MouseMoved?.Invoke(this, new MouseHookEventArgs(info.pt.x, info.pt.y));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Exception in low-level mouse hook handler");
            }
        }

        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEMOVE = 0x0200;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
