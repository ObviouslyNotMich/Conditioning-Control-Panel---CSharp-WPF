using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ConditioningControlPanel.Services;

public sealed class NativeOverlayTargetProcessTracker : IDisposable
{
    private readonly TimeSpan _pollInterval;
    private Timer? _pollTimer;
    private NativeOverlayTargetSnapshot? _lastSnapshot;
    private bool _disposed;

    public NativeOverlayTargetProcessTracker(TimeSpan? pollInterval = null)
    {
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(750);
    }

    public event Action<NativeOverlayTargetSnapshot?>? TargetChanged;

    public void Start()
    {
        if (_disposed) return;
        _pollTimer ??= new Timer(Poll, null, TimeSpan.Zero, _pollInterval);
    }

    public void Stop()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
        if (_lastSnapshot.HasValue)
        {
            _lastSnapshot = null;
            TargetChanged?.Invoke(null);
        }
    }

    private void Poll(object? _)
    {
        if (_disposed) return;
        var snapshot = GetForegroundTargetSnapshot();

        if (!SnapshotsEqual(_lastSnapshot, snapshot))
        {
            _lastSnapshot = snapshot;
            TargetChanged?.Invoke(snapshot);
        }
    }

    private static bool SnapshotsEqual(NativeOverlayTargetSnapshot? a, NativeOverlayTargetSnapshot? b)
    {
        if (!a.HasValue || !b.HasValue)
            return !a.HasValue && !b.HasValue;

        var av = a.Value;
        var bv = b.Value;
        return av.ProcessId == bv.ProcessId &&
               string.Equals(av.ProcessName, bv.ProcessName, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(av.ExecutablePath, bv.ExecutablePath, StringComparison.OrdinalIgnoreCase) &&
             av.WindowHandle == bv.WindowHandle &&
             string.Equals(av.ScreenDeviceName, bv.ScreenDeviceName, StringComparison.OrdinalIgnoreCase) &&
               av.IsAttachReady == bv.IsAttachReady;
    }

    private static NativeOverlayTargetSnapshot? GetForegroundTargetSnapshot()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;

        _ = GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return null;

        string? path = null;
        string? processName = null;
        string? screenDeviceName = null;
        bool attachReady = false;

        try
        {
            var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
            screenDeviceName = screen?.DeviceName;
        }
        catch
        {
            // best effort
        }

        if (TryOpenProcessForQuery(pid, out var handle))
        {
            attachReady = true;
            try
            {
                path = TryGetProcessImagePath(handle);
            }
            finally
            {
                _ = CloseHandle(handle);
            }
        }

        try
        {
            using var process = Process.GetProcessById((int)pid);
            processName = process.ProcessName;
        }
        catch
        {
            // best effort
        }

        return new NativeOverlayTargetSnapshot(
            ProcessId: pid,
            ProcessName: processName,
            ExecutablePath: path,
            WindowHandle: hwnd,
            ScreenDeviceName: screenDeviceName,
            IsAttachReady: attachReady,
            SeenAtUtc: DateTimeOffset.UtcNow);
    }

    private static bool TryOpenProcessForQuery(uint pid, out IntPtr handle)
    {
        handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        return handle != IntPtr.Zero;
    }

    private static string? TryGetProcessImagePath(IntPtr processHandle)
    {
        var sb = new StringBuilder(1024);
        var size = sb.Capacity;
        return QueryFullProcessImageName(processHandle, 0, sb, ref size) ? sb.ToString() : null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}
