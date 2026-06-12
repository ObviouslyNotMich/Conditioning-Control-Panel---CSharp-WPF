using System;

namespace ConditioningControlPanel.Services;

public enum NativeHookSessionState
{
    Uninitialized = 0,
    Stopped = 1,
    Idle = 2,
    TrackingTarget = 3,
    AttachPending = 4,
    Attached = 5,
    Fallback = 6,
    Faulted = 7
}

public readonly record struct NativeOverlayTargetSnapshot(
    uint ProcessId,
    string? ProcessName,
    string? ExecutablePath,
    nint WindowHandle,
    string? ScreenDeviceName,
    bool IsAttachReady,
    DateTimeOffset SeenAtUtc);

public readonly record struct NativeOverlayDesiredState(
    bool PinkEnabled,
    int PinkOpacity,
    bool SpiralEnabled,
    int SpiralOpacity,
    bool BrainDrainEnabled,
    int BrainDrainIntensity,
    bool DualMonitorEnabled);
