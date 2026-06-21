namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Simple double-precision point in physical screen pixels.
/// Mirrors the WPF/System.Drawing.Point contract but lives in Core so it is portable.
/// </summary>
public readonly record struct Point(double X, double Y);
