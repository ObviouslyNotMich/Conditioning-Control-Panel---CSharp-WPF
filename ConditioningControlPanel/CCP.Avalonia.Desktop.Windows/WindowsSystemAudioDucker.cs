using System;
using System.Collections.Generic;
using ConditioningControlPanel.Core.Platform;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows;

/// <summary>
/// Windows-specific system audio ducker. Lowers the simple volume of other applications'
/// audio sessions while CCP audio is playing, then restores the original levels.
/// </summary>
/// <remarks>
/// This is a best-effort implementation using NAudio's CoreAudio session APIs. It skips
/// CCP's own sessions and the system-sounds session. Sessions created after Duck() is
/// called will not be ducked; sessions that disappear before Unduck() are ignored.
/// TODO: Duck sessions that start while already ducked (register for OnSessionCreated).
/// TODO: Handle multiple active sessions per process more precisely than a single
/// per-PID volume snapshot.
/// </remarks>
public sealed class WindowsSystemAudioDucker : ISystemAudioDucker, IDisposable
{
    private const float DuckVolume = 0.20f;

    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly Dictionary<uint, float> _originalVolumes = new();
    private readonly object _stateLock = new();
    private bool _isDucked;
    private bool _disposed;

    public void Duck()
    {
        lock (_stateLock)
        {
            if (_disposed || _isDucked)
                return;

            try
            {
                var ownProcessId = (uint)Environment.ProcessId;

                // Duck across ALL active render endpoints, not just the default device, so audio
                // routed to a secondary output still gets ducked (parity with WPF bug #415 fix).
                foreach (var device in GetActiveRenderDevices())
                {
                    using (device)
                    {
                        try
                        {
                            var sessionManager = device.AudioSessionManager;
                            sessionManager.RefreshSessions();
                            var sessions = sessionManager.Sessions;

                            for (int i = 0; i < sessions.Count; i++)
                            {
                                try
                                {
                                    using var session = sessions[i];
                                    if (ShouldSkipSession(session, ownProcessId))
                                        continue;

                                    var volume = session.SimpleAudioVolume;
                                    var pid = session.GetProcessID;

                                    // Preserve the first observed volume per process; do not overwrite
                                    // if Duck() is called repeatedly.
                                    if (!_originalVolumes.ContainsKey(pid))
                                        _originalVolumes[pid] = volume.Volume;

                                    volume.Volume = DuckVolume;
                                }
                                catch
                                {
                                    // Individual sessions can become invalid at any time; skip them.
                                }
                            }
                        }
                        catch
                        {
                            // A single endpoint's session manager may be unavailable; skip it.
                        }
                    }
                }

                _isDucked = true;
            }
            catch
            {
                // CoreAudio may be unavailable in some Windows configurations. Fail open.
            }
        }
    }

    public void Unduck()
    {
        lock (_stateLock)
        {
            if (_disposed || !_isDucked)
                return;

            try
            {
                foreach (var device in GetActiveRenderDevices())
                {
                    using (device)
                    {
                        try
                        {
                            var sessionManager = device.AudioSessionManager;
                            sessionManager.RefreshSessions();
                            var sessions = sessionManager.Sessions;
                            for (int i = 0; i < sessions.Count; i++)
                            {
                                try
                                {
                                    using var session = sessions[i];
                                    var pid = session.GetProcessID;
                                    if (_originalVolumes.TryGetValue(pid, out var originalVolume))
                                    {
                                        session.SimpleAudioVolume.Volume = originalVolume;
                                    }
                                }
                                catch
                                {
                                    // Session may have exited; ignore.
                                }
                            }
                        }
                        catch
                        {
                            // A single endpoint's session manager may be unavailable; skip it.
                        }
                    }
                }
            }
            catch
            {
                // Best-effort restore.
            }
            finally
            {
                _originalVolumes.Clear();
                _isDucked = false;
            }
        }
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            if (_disposed)
                return;

            Unduck();
            _enumerator.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    /// All active render endpoints — not just the default multimedia device. A user who routes
    /// their browser / media player to a secondary output would otherwise never get ducked, since
    /// those sessions live on a different device's session manager (parity with WPF bug #415).
    /// The caller disposes each returned device.
    /// </summary>
    private List<MMDevice> GetActiveRenderDevices()
    {
        var list = new List<MMDevice>();
        try
        {
            var endpoints = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            for (int i = 0; i < endpoints.Count; i++)
                list.Add(endpoints[i]);
        }
        catch
        {
            // Enumeration may fail in odd configs; fall back to no devices (fail open).
        }
        return list;
    }

    private static bool ShouldSkipSession(AudioSessionControl session, uint ownProcessId)
    {
        try
        {
            if (session.IsSystemSoundsSession)
                return true;

            if (session.GetProcessID == ownProcessId)
                return true;

            // Only duck sessions that are currently producing audio. Inactive sessions
            // are typically not audible, and ducking them has no user-visible effect.
            if (session.State != AudioSessionState.AudioSessionStateActive)
                return true;
        }
        catch
        {
            // If we cannot read session metadata, leave it alone.
            return true;
        }

        return false;
    }
}
