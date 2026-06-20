using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Global low-level mouse hook for the chaos Ripple verb (right-click anywhere on the live
/// desktop). The decision callback runs SYNCHRONOUSLY on the hook thread and returns whether
/// to swallow the click — so it must be cheap and touch only thread-safe snapshots (never
/// WPF dependency properties). Swallowed clicks never reach the app under the cursor;
/// unswallowed ones pass through untouched, keeping normal desktop right-clicks alive mid-run.
/// </summary>
public sealed class GlobalMouseHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_LBUTTONDOWN = 0x0201;

    private IntPtr _hookId = IntPtr.Zero;
    private readonly LowLevelMouseProc _proc;
    private bool _isDisposed;

    /// <summary>Right button pressed at this PHYSICAL-px screen point. Return true to swallow.</summary>
    public Func<Point, bool>? RightDown;

    /// <summary>Left button pressed at this PHYSICAL-px screen point. Return true to swallow.
    /// Used by the shared-host bubble field to pop a bubble (and swallow the click so it doesn't
    /// also land on whatever sits behind the click-through host); a miss returns false and passes
    /// the click through untouched. Same hook-thread contract as <see cref="RightDown"/>.</summary>
    public Func<Point, bool>? LeftDown;

    public GlobalMouseHook()
    {
        _proc = HookCallback;
    }

    public void Start()
    {
        if (_hookId != IntPtr.Zero) return;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
        App.Logger?.Debug("Global mouse hook started");
    }

    public void Stop()
    {
        if (_hookId == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
        App.Logger?.Debug("Global mouse hook stopped");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_RBUTTONDOWN || wParam == (IntPtr)WM_LBUTTONDOWN))
        {
            try
            {
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                var pt = new Point(info.pt.X, info.pt.Y);
                var cb = wParam == (IntPtr)WM_RBUTTONDOWN ? RightDown : LeftDown;
                if (cb?.Invoke(pt) == true)
                    return (IntPtr)1;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Mouse hook callback: {E}", ex.Message);
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Stop();
    }

    #region Win32 API

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn,
        IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    #endregion
}
