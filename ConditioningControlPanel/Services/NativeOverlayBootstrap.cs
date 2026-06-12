using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ConditioningControlPanel.Services;

public sealed class NativeOverlayProbeResult
{
    public bool IsReady { get; init; }
    public bool ProcessAttachReady { get; init; }
    public bool DwmCompositionEnabled { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public static class NativeOverlayBootstrap
{
    public static NativeOverlayProbeResult Probe()
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (!OperatingSystem.IsWindows())
        {
            errors.Add("Native backend requires Windows");
            return Build(false, false, false, errors, warnings);
        }

        if (!Environment.Is64BitProcess)
        {
            errors.Add("Native backend requires x64 process");
        }

        if (!CanLoadNativeLibrary("d3d11.dll"))
            errors.Add("Cannot load d3d11.dll");

        if (!CanLoadNativeLibrary("dxgi.dll"))
            errors.Add("Cannot load dxgi.dll");

        bool dwmEnabled = false;
        if (DwmIsCompositionEnabled(out var enabled) == 0)
        {
            dwmEnabled = enabled;
            if (!dwmEnabled)
                warnings.Add("DWM composition is disabled; exclusive fullscreen handling may be limited");
        }
        else
        {
            warnings.Add("Could not query DWM composition state");
        }

        bool processAttachReady = ProbeForegroundProcessAttach(warnings, errors);

        bool isReady = errors.Count == 0;
        return Build(isReady, processAttachReady, dwmEnabled, errors, warnings);
    }

    private static NativeOverlayProbeResult Build(
        bool isReady,
        bool processAttachReady,
        bool dwmCompositionEnabled,
        List<string> errors,
        List<string> warnings)
    {
        return new NativeOverlayProbeResult
        {
            IsReady = isReady,
            ProcessAttachReady = processAttachReady,
            DwmCompositionEnabled = dwmCompositionEnabled,
            Errors = errors,
            Warnings = warnings
        };
    }

    private static bool CanLoadNativeLibrary(string libraryName)
    {
        if (NativeLibrary.TryLoad(libraryName, out var handle))
        {
            NativeLibrary.Free(handle);
            return true;
        }

        return false;
    }

    private static bool ProbeForegroundProcessAttach(List<string> warnings, List<string> errors)
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            warnings.Add("No foreground window found during attach probe");
            return false;
        }

        _ = GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
        {
            warnings.Add("Foreground window process id unavailable");
            return false;
        }

        var currentPid = (uint)Process.GetCurrentProcess().Id;
        if (pid == currentPid)
        {
            // Foreground is our own process. Treat as attach-ready baseline.
            return true;
        }

        var processHandle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (processHandle == IntPtr.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            errors.Add($"OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION) failed for foreground pid {pid} (Win32 {err})");
            return false;
        }

        _ = CloseHandle(processHandle);
        return true;
    }

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("dwmapi.dll")]
    private static extern int DwmIsCompositionEnabled(out bool pfEnabled);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}
