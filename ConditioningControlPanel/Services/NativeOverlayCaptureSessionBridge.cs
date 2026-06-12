using System;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using D3DDevice = SharpDX.Direct3D11.Device;

namespace ConditioningControlPanel.Services;

public sealed class NativeOverlayCaptureSessionBridge : IDisposable
{
    private uint? _targetProcessId;
    private bool _isRunning;
    private string? _lastError;
    private D3DDevice? _device;
    private Factory1? _factory;
    private OutputDuplication? _duplication;
    private string? _boundDisplay;

    public bool IsRunning => _isRunning;
    public string? LastError => _lastError;
    public uint? TargetProcessId => _targetProcessId;

    public bool TryStart(NativeOverlayTargetSnapshot target, out string reason)
    {
        reason = string.Empty;
        _lastError = null;

        if (!target.IsAttachReady)
        {
            reason = "capture-denied-target-not-ready";
            _lastError = reason;
            return false;
        }

        if (target.ProcessId == (uint)Environment.ProcessId)
        {
            reason = "capture-self-target-skip";
            _lastError = reason;
            return false;
        }

        if (string.IsNullOrWhiteSpace(target.ScreenDeviceName))
        {
            reason = "capture-target-screen-unknown";
            _lastError = reason;
            return false;
        }

        if (!TryCreateDuplication(target.ScreenDeviceName!, out reason))
        {
            _lastError = reason;
            return false;
        }

        _targetProcessId = target.ProcessId;
        _boundDisplay = target.ScreenDeviceName;
        _isRunning = true;
        reason = "capture-started";
        return true;
    }

    public void Stop()
    {
        _isRunning = false;
        _targetProcessId = null;
        _boundDisplay = null;
        try { _duplication?.Dispose(); } catch { }
        _duplication = null;
    }

    public bool IsHealthy(out string reason)
    {
        reason = string.Empty;
        if (!_isRunning)
        {
            reason = _lastError ?? "capture-not-running";
            return false;
        }

        if (_duplication == null)
        {
            reason = _lastError ?? "capture-duplication-missing";
            return false;
        }

        return true;
    }

    public bool TryAcquireFrame(out string reason)
    {
        reason = string.Empty;

        if (!_isRunning || _duplication == null)
        {
            reason = "capture-not-running";
            return false;
        }

        try
        {
            var result = _duplication.TryAcquireNextFrame(0, out var frameInfo, out var desktopResource);
            if (!result.Success)
            {
                if (result == SharpDX.DXGI.ResultCode.WaitTimeout)
                {
                    // No new frame this tick is not a failure.
                    return true;
                }

                reason = "acquire-frame-failed: " + result.Code;
                _lastError = reason;
                return false;
            }

            using (desktopResource)
            {
                if (desktopResource != null)
                {
                    using var _ = desktopResource.QueryInterface<Texture2D>();
                }
            }

            _duplication.ReleaseFrame();
            return true;
        }
        catch (SharpDXException ex)
        {
            reason = "acquire-frame-exception: " + ex.ResultCode.Code;
            _lastError = reason;
            return false;
        }
        catch (Exception ex)
        {
            reason = "acquire-frame-exception: " + ex.Message;
            _lastError = reason;
            return false;
        }
    }

    private bool TryCreateDuplication(string screenDeviceName, out string reason)
    {
        reason = string.Empty;

        try
        {
            _factory ??= new Factory1();
            _device ??= new D3DDevice(DriverType.Hardware, DeviceCreationFlags.BgraSupport, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1);

            foreach (var adapter in _factory.Adapters1)
            {
                using (adapter)
                {
                    foreach (var output in adapter.Outputs)
                    {
                        using (output)
                        {
                            var desc = output.Description;
                            if (!string.Equals(desc.DeviceName, screenDeviceName, StringComparison.OrdinalIgnoreCase))
                                continue;

                            using var output1 = output.QueryInterfaceOrNull<Output1>();
                            if (output1 == null)
                                continue;

                            try { _duplication?.Dispose(); } catch { }
                            _duplication = output1.DuplicateOutput(_device);
                            reason = "duplication-created";
                            return true;
                        }
                    }
                }
            }

            reason = "capture-output-not-found: " + screenDeviceName;
            return false;
        }
        catch (Exception ex)
        {
            reason = "capture-init-failed: " + ex.Message;
            return false;
        }
    }

    public void Dispose()
    {
        Stop();
        try { _duplication?.Dispose(); } catch { }
        try { _factory?.Dispose(); } catch { }
        try { _device?.Dispose(); } catch { }
        _duplication = null;
        _factory = null;
        _device = null;
    }
}
