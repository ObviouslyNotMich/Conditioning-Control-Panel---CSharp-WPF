using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Services.Haptics;

/// <summary>
/// Stub implementation of <see cref="IHapticsService"/> for the Avalonia head.
/// Simulates connect/disconnect/test flows so the Haptics tab can be exercised
/// without requiring a real device provider.
/// </summary>
public sealed class AvaloniaHapticsService : IHapticsService
{
    private readonly IAppLogger? _logger;
    private readonly object _sync = new();
    private readonly List<string> _devices = new();

    private bool _isConnected;
    private bool _isConnecting;

    public AvaloniaHapticsService(IAppLogger? logger = null)
    {
        _logger = logger;
    }

    public bool IsConnected
    {
        get { lock (_sync) return _isConnected; }
        private set { lock (_sync) _isConnected = value; }
    }

    public bool IsConnecting
    {
        get { lock (_sync) return _isConnecting; }
        private set { lock (_sync) _isConnecting = value; }
    }

    public IReadOnlyList<string> ConnectedDevices
    {
        get { lock (_sync) return _devices.ToArray(); }
    }

    public event EventHandler<bool>? ConnectionStateChanged;
    public event EventHandler<string>? DeviceAdded;
    public event EventHandler<string>? DeviceRemoved;

    public async Task<bool> ConnectAsync(string providerUrl)
    {
        if (IsConnected)
        {
            _logger?.Information("Haptics connect requested but already connected");
            return true;
        }

        IsConnecting = true;

        try
        {
            _logger?.Information("Haptics connecting to {ProviderUrl} (stub)", providerUrl);
            await Task.Delay(300).ConfigureAwait(false);

            lock (_sync)
            {
                _isConnected = true;
                _isConnecting = false;
                _devices.Clear();
                _devices.Add("Mock Device");
            }

            _logger?.Information("Haptics connected (stub), device added: {Device}", "Mock Device");
            ConnectionStateChanged?.Invoke(this, true);
            DeviceAdded?.Invoke(this, "Mock Device");

            return true;
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Haptics connect failed (stub)");
            IsConnecting = false;
            return false;
        }
    }

    public void Disconnect()
    {
        if (!IsConnected)
        {
            _logger?.Information("Haptics disconnect requested but not connected");
            return;
        }

        string[] removedDevices;
        lock (_sync)
        {
            _isConnected = false;
            _isConnecting = false;
            removedDevices = _devices.ToArray();
            _devices.Clear();
        }

        foreach (var device in removedDevices)
        {
            _logger?.Information("Haptics device removed: {Device}", device);
            DeviceRemoved?.Invoke(this, device);
        }

        _logger?.Information("Haptics disconnected (stub)");
        ConnectionStateChanged?.Invoke(this, false);
    }

    public async Task<bool> TestAsync(int intensityPercent, int durationMs)
    {
        if (!IsConnected)
        {
            _logger?.Information("Haptics test requested but not connected");
            return false;
        }

        _logger?.Information(
            "Haptics test triggered (intensity {Intensity}%, duration {Duration}ms) (stub)",
            intensityPercent,
            durationMs);

        await Task.Delay(150).ConfigureAwait(false);

        _logger?.Information("Haptics test completed (stub)");
        return true;
    }
}
