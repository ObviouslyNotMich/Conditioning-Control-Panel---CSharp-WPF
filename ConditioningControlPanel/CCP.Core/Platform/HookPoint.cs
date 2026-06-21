namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Physical-screen point reported by a low-level global mouse hook.
/// </summary>
public readonly record struct HookPoint(double X, double Y);
