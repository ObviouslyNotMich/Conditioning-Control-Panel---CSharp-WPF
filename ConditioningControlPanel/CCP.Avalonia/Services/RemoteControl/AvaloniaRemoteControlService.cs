using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Services.RemoteControl;

/// <summary>
/// Stub/proxy implementation of <see cref="IRemoteControlService"/> for the Avalonia head.
/// Generates a local session code and PIN and simulates a controller connection after a
/// short delay. The full WPF network polling logic is intentionally not ported yet.
/// </summary>
public sealed class AvaloniaRemoteControlService : IRemoteControlService, IDisposable
{
    private readonly IScheduler _scheduler;
    private readonly IAppLogger? _logger;
    private readonly Random _rng = new();
    private readonly object _sync = new();

    private bool _isActive;
    private bool _controllerConnected;
    private string? _sessionCode;
    private string? _connectPin;
    private IDisposable? _connectionTimer;
    private bool _isDisposed;

    public AvaloniaRemoteControlService(IScheduler scheduler, IAppLogger? logger = null)
    {
        _scheduler = scheduler;
        _logger = logger;
    }

    public bool IsActive
    {
        get { lock (_sync) return _isActive; }
        private set { lock (_sync) _isActive = value; }
    }

    public bool ControllerConnected
    {
        get { lock (_sync) return _controllerConnected; }
        private set
        {
            bool changed;
            lock (_sync)
            {
                changed = _controllerConnected != value;
                _controllerConnected = value;
            }

            if (changed)
            {
                _logger?.Information("Remote controller connected changed: {Connected}", value);
                ControllerConnectedChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string? SessionCode
    {
        get { lock (_sync) return _sessionCode; }
        private set { lock (_sync) _sessionCode = value; }
    }

    public string? ConnectPin
    {
        get { lock (_sync) return _connectPin; }
        private set { lock (_sync) _connectPin = value; }
    }

    public event EventHandler? ControllerConnectedChanged;
    public event EventHandler? SessionStarted;
    public event EventHandler? SessionEnded;

    public Task<string?> StartSessionAsync(string tier)
    {
        lock (_sync)
        {
            if (_isDisposed) return Task.FromResult<string?>(null);
            if (_isActive) return Task.FromResult(SessionCode);
        }

        var code = GenerateSessionCode();
        var pin = GeneratePin();

        IsActive = true;
        SessionCode = code;
        ConnectPin = pin;
        ControllerConnected = false;

        _logger?.Information("Remote session started (tier: {Tier}, code: {Code})", tier, code);
        SessionStarted?.Invoke(this, EventArgs.Empty);

        // Simulate a controller connecting after a few seconds.
        var delay = TimeSpan.FromSeconds(_rng.Next(3, 8));
        _connectionTimer?.Dispose();
        _connectionTimer = _scheduler.StartOneShotTimer(delay, () =>
        {
            if (IsActive)
            {
                ControllerConnected = true;
            }
        });

        return Task.FromResult<string?>(code);
    }

    public Task StopSessionAsync()
    {
        CleanupSession();
        return Task.CompletedTask;
    }

    public Task OptInToDirectoryAsync(List<string> tags, string statusText)
    {
        _logger?.Information("Directory opt-in requested ({TagCount} tags, status length {StatusLength})",
            tags?.Count ?? 0, statusText?.Length ?? 0);
        return Task.CompletedTask;
    }

    public Task DisconnectControllerAsync()
    {
        _logger?.Information("Remote controller disconnect requested.");
        ControllerConnected = false;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_isDisposed) return;
            _isDisposed = true;
        }

        CleanupSession();
    }

    private void CleanupSession()
    {
        _connectionTimer?.Dispose();
        _connectionTimer = null;

        var wasActive = IsActive;
        IsActive = false;
        SessionCode = null;
        ConnectPin = null;
        ControllerConnected = false;

        if (wasActive)
        {
            _logger?.Information("Remote session stopped.");
            SessionEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    private string GenerateSessionCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var buffer = new char[6];
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = chars[_rng.Next(chars.Length)];
        }

        // Format as two groups of three, e.g. "ABC-123".
        return $"{buffer[0]}{buffer[1]}{buffer[2]}-{buffer[3]}{buffer[4]}{buffer[5]}";
    }

    private string GeneratePin()
    {
        return _rng.Next(0, 10000).ToString("D4");
    }
}
