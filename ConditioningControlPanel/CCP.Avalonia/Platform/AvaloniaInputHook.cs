using System.ComponentModel;
using System.Runtime.InteropServices;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Low-level input hook implementation.
/// On Windows it installs a WH_KEYBOARD_LL hook so the panic key works in the Avalonia head.
/// On Linux/macOS/mobile it degrades gracefully to a no-op because global hooks require
/// platform-native interop that is not available in the shared Avalonia project.
/// </summary>
public sealed class AvaloniaInputHook : IInputHook
{
    private readonly IAppLogger? _logger;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private LowLevelKeyboardProc? _keyboardProc;
    private IntPtr _keyboardHook = IntPtr.Zero;

    public AvaloniaInputHook(IAppLogger? logger = null)
    {
        _logger = logger;
        Start();
    }

    public event EventHandler<KeyboardHookEventArgs>? KeyPressed;
    public event EventHandler<MouseHookEventArgs>? MouseMoved { add { } remove { } }

    public bool CanSuppressKeys => false;

    public bool SuppressKey(int virtualKeyCode) => false;

    public void Dispose()
    {
        if (_keyboardHook != IntPtr.Zero && OperatingSystem.IsWindows())
        {
            try
            {
                UnhookWindowsHookEx(_keyboardHook);
            }
            catch (Exception ex)
            {
                _logger?.Warning(ex, "Failed to uninstall low-level keyboard hook");
            }
            _keyboardHook = IntPtr.Zero;
        }
        _keyboardProc = null;
    }

    public AvaloniaInputHook Start()
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger?.Information("Low-level input hooks are only supported on Windows in the Avalonia head.");
            return this;
        }

        if (_keyboardHook != IntPtr.Zero)
            return this;

        try
        {
            _keyboardProc = KeyboardHookCallback;
            var moduleHandle = GetModuleHandle(null);
            _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);
            if (_keyboardHook == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                _logger?.Warning("SetWindowsHookEx(WH_KEYBOARD_LL) failed with error {Error}", error);
                _keyboardProc = null;
            }
            else
            {
                _logger?.Information("Low-level keyboard hook installed");
            }
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Failed to install low-level keyboard hook");
            _keyboardProc = null;
        }

        return this;
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
                _logger?.Warning(ex, "Exception in low-level keyboard hook handler");
            }
        }

        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private const int WH_KEYBOARD_LL = 13;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

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
}
