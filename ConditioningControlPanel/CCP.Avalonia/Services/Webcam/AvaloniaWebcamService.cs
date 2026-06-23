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
    private readonly ILogger<AvaloniaWebcamService>? _logger;
    private bool _isRunning;

    public AvaloniaWebcamService(ILogger<AvaloniaWebcamService>? logger = null)
    {
        _logger = logger;
    }

    public bool IsRunning => _isRunning;

    public event Action? OnBlink;
    public event Action? OnMouthOpen;
    public event Action<Point>? OnGazeMove;
    public event Action? OnFaceLost;
    public event Action? OnFaceFound;

    public void StartTracking()
    {
        _isRunning = true;
        _logger?.LogInformation("AvaloniaWebcamService: tracking started (stub)");
    }

    public void StopTracking()
    {
        _isRunning = false;
        _logger?.LogInformation("AvaloniaWebcamService: tracking stopped (stub)");
    }

    public void Calibrate()
    {
        _logger?.LogInformation("AvaloniaWebcamService: calibration requested (stub)");
    }

    public void TestTracker()
    {
        _logger?.LogInformation("AvaloniaWebcamService: tracker test requested (stub)");
    }

    public void RefreshDevices()
    {
        _logger?.LogInformation("AvaloniaWebcamService: device list refreshed (stub)");
    }

    public void RevokeConsent()
    {
        _isRunning = false;
        _logger?.LogInformation("AvaloniaWebcamService: consent revoked (stub)");
    }
}
