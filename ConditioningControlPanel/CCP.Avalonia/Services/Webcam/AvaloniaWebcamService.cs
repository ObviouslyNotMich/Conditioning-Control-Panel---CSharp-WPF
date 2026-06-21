using System;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Webcam;

namespace ConditioningControlPanel.Avalonia.Services.Webcam;

/// <summary>
/// Avalonia stub for <see cref="IWebcamService"/>.
/// Tracks run state and logs calls so the Lab/Webcam UI can exercise the seam
/// while the real OpenCV/blazeface tracker engine is being ported from WPF.
/// </summary>
public sealed class AvaloniaWebcamService : IWebcamService
{
    private readonly IAppLogger? _logger;
    private bool _isRunning;

    public AvaloniaWebcamService(IAppLogger? logger = null)
    {
        _logger = logger;
    }

    public bool IsRunning => _isRunning;

    public void StartTracking()
    {
        _isRunning = true;
        _logger?.Information("AvaloniaWebcamService: tracking started (stub)");
    }

    public void StopTracking()
    {
        _isRunning = false;
        _logger?.Information("AvaloniaWebcamService: tracking stopped (stub)");
    }

    public void Calibrate()
    {
        _logger?.Information("AvaloniaWebcamService: calibration requested (stub)");
    }

    public void TestTracker()
    {
        _logger?.Information("AvaloniaWebcamService: tracker test requested (stub)");
    }

    public void RefreshDevices()
    {
        _logger?.Information("AvaloniaWebcamService: device list refreshed (stub)");
    }

    public void RevokeConsent()
    {
        _isRunning = false;
        _logger?.Information("AvaloniaWebcamService: consent revoked (stub)");
    }
}
