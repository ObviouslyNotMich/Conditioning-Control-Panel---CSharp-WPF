using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Services.Compositor;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using PixelRect = ConditioningControlPanel.Core.Platform.PixelRect;

namespace ConditioningControlPanel.Avalonia.Compositor;

/// <summary>
/// The unified compositor engine. Renders all registered <see cref="ILayer"/> instances
/// directly into Avalonia's render thread via <see cref="CompositorDrawOp"/> at 60Hz.
/// No WriteableBitmap, no Image control, no manual invalidation — Avalonia handles presentation.
/// </summary>
public sealed class CompositorEngine : IDisposable
{
    private readonly ILogger<CompositorEngine>? _logger;
    private readonly IScreenProvider? _screenProvider;
    private readonly List<CompositorWindow> _windows = new();
    private readonly SortedList<int, ILayer> _layers = new();
    private readonly DispatcherTimer _timer;
    private readonly object _layerLock = new();
    private DateTime? _emptySince;

    private DateTime _lastFrame = DateTime.MinValue;
    private bool _disposed;

    public CompositorEngine(ILogger<CompositorEngine>? logger = null, IScreenProvider? screenProvider = null)
    {
        _logger = logger;
        _screenProvider = screenProvider;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16) // ~60 Hz
        };
        _timer.Tick += OnFrameTick;
    }

    /// <summary>
    /// Starts the compositor: creates one <see cref="CompositorWindow"/> per monitor
    /// and begins the 60Hz update loop. Safe to call multiple times.
    /// </summary>
    public void Start()
    {
        if (_disposed) return;
        if (_windows.Count > 0)
        {
            // Already started; just ensure the timer is running.
            if (!_timer.IsEnabled)
            {
                _lastFrame = DateTime.UtcNow;
                _emptySince = null;
                _timer.Start();
            }
            return;
        }

        var screens = _screenProvider?.GetAllScreens() ?? Array.Empty<ScreenInfo>();
        if (screens.Count == 0)
        {
            screens = new[] { new ScreenInfo("fallback", new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, 1920, 1080), new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, 1920, 1080), 1.0) };
        }

        foreach (var screen in screens)
        {
            try
            {
                var window = new CompositorWindow(screen, this);
                window.Show();
                window.ApplyNativeTransparency();
                _windows.Add(window);
                _logger?.LogInformation("CompositorWindow created on {Screen}", screen.Name);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to create CompositorWindow on {Screen}", screen.Name);
            }
        }

        if (_windows.Count > 0)
        {
            _lastFrame = DateTime.UtcNow;
            _timer.Start();
            _logger?.LogInformation("CompositorEngine started: {Count} window(s)", _windows.Count);
        }
    }

    /// <summary>Stops the update loop and closes all compositor windows.</summary>
    public void Stop()
    {
        _emptySince = null;
        _timer.Stop();
        foreach (var window in _windows.ToList())
        {
            try { window.Close(); } catch { /* ignore */ }
        }
        _windows.Clear();
        _logger?.LogInformation("CompositorEngine stopped");
    }

    /// <summary>Register a layer with the compositor. Layer is ordered by <see cref="ILayer.ZIndex"/>.</summary>
    public void RegisterLayer(ILayer layer)
    {
        lock (_layerLock)
        {
            if (_layers.ContainsKey(layer.ZIndex))
            {
                _logger?.LogWarning("Layer with ZIndex {ZIndex} already registered; replacing", layer.ZIndex);
                _layers[layer.ZIndex].OnDeactivated();
                _layers.Remove(layer.ZIndex);
            }
            _layers.Add(layer.ZIndex, layer);
            layer.OnActivated();
            _logger?.LogDebug("Layer registered: {Layer} at Z={ZIndex}", layer.GetType().Name, layer.ZIndex);
        }
    }

    /// <summary>Unregister a layer from the compositor.</summary>
    public void UnregisterLayer(ILayer layer)
    {
        lock (_layerLock)
        {
            if (_layers.TryGetValue(layer.ZIndex, out var existing) && ReferenceEquals(existing, layer))
            {
                layer.OnDeactivated();
                _layers.Remove(layer.ZIndex);
                _logger?.LogDebug("Layer unregistered: {Layer}", layer.GetType().Name);
            }
        }
    }

    /// <summary>Get the layer at the specified z-index, or null.</summary>
    public ILayer? GetLayer(int zIndex)
    {
        lock (_layerLock)
        {
            _layers.TryGetValue(zIndex, out var layer);
            return layer;
        }
    }

    /// <summary>All currently registered layers, ordered by z-index.</summary>
    public IReadOnlyList<IAvaloniaLayer> Layers
    {
        get
        {
            lock (_layerLock)
            {
                return _layers.Values.OfType<IAvaloniaLayer>().ToList();
            }
        }
    }

    /// <summary>Number of compositor windows (one per monitor).</summary>
    public int WindowCount => _windows.Count;

    /// <summary>True when the update loop is running.</summary>
    public bool IsRunning => _timer.IsEnabled;

    private int _dialogModeRefCount;

    /// <summary>
    /// Temporarily lower compositor windows so dialogs and popups can be clicked.
    /// DEPRECATED: compositor now uses WS_EX_LAYERED | WS_EX_TRANSPARENT for native
    /// click-through, so it stays on top of dialogs while still passing clicks through.
    /// This method is kept for API compatibility but is a no-op.
    /// </summary>
    public void PushDialogMode()
    {
        Interlocked.Increment(ref _dialogModeRefCount);
        // No longer lowering Topmost — compositor stays on top with click-through styles
    }

    /// <summary>Restore compositor windows after a dialog closes. No-op for compatibility.</summary>
    public void PopDialogMode()
    {
        if (Interlocked.Decrement(ref _dialogModeRefCount) <= 0)
        {
            _dialogModeRefCount = 0;
        }
    }

    /// <summary>
    /// Render all active layers into the given Skia canvas. Called from the render thread
    /// via <see cref="CompositorDrawOp"/>.
    /// </summary>
    public void RenderToCanvas(SKCanvas canvas, PixelRect bounds)
    {
        // Clear to transparent so inactive pixels pass through to the desktop.
        // Avalonia's render thread may clear to a solid color before our ICustomDrawOperation
        // runs, so we explicitly clear to transparent here.
        canvas.Clear(SKColors.Transparent);

        IAvaloniaLayer[] activeLayers;
        lock (_layerLock)
        {
            activeLayers = _layers.Values.OfType<IAvaloniaLayer>().Where(l => l.IsActive).ToArray();
        }

        foreach (var layer in activeLayers)
        {
            try
            {
                canvas.Save();
                layer.Render(canvas, bounds, TimeSpan.Zero);
                canvas.Restore();
            }
            catch (Exception ex) { _logger?.LogDebug(ex, "Layer {Layer} render failed", layer.GetType().Name); }
        }
    }

    private void OnFrameTick(object? sender, EventArgs e)
    {
        if (_disposed || _windows.Count == 0) return;

        var now = DateTime.UtcNow;
        var delta = now - _lastFrame;
        _lastFrame = now;

        // Cap delta to avoid large time jumps after pauses (e.g. debugger break, window drag)
        var cappedDelta = delta.TotalMilliseconds > 100
            ? TimeSpan.FromMilliseconds(100)
            : delta;

        IAvaloniaLayer[] activeLayers;
        lock (_layerLock)
        {
            activeLayers = _layers.Values.OfType<IAvaloniaLayer>().Where(l => l.IsActive).ToArray();
        }

        // Keep compositor topmost whenever active layers are present.
        // WS_EX_LAYERED | WS_EX_TRANSPARENT handles click-through natively,
        // so we no longer need to lower the compositor for dialogs.
        var shouldBeTopmost = activeLayers.Length > 0;
        foreach (var window in _windows)
        {
            if (window.Topmost != shouldBeTopmost)
            {
                try { window.Topmost = shouldBeTopmost; }
                catch { }
            }
        }

        if (activeLayers.Length == 0)
        {
            // No active layers — start the auto-shutdown timer.
            _emptySince ??= now;
            if ((now - _emptySince.Value).TotalMilliseconds > 500)
            {
                Stop();
            }
            return;
        }

        _emptySince = null;

        // Update all layer animations / state
        foreach (var layer in activeLayers)
        {
            try { layer.Update(cappedDelta); }
            catch (Exception ex) { _logger?.LogDebug(ex, "Layer {Layer} update failed", layer.GetType().Name); }
        }

        // Tell Avalonia to re-render each window's CompositorControl.
        // Invalidating the control directly (not the window) is required for
        // ICustomDrawOperation to re-run on the render thread.
        foreach (var window in _windows)
        {
            try
            {
                window.GetControl().InvalidateVisual();
            }
            catch (Exception ex) { _logger?.LogDebug(ex, "Compositor control invalidation failed"); }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _timer.Tick -= OnFrameTick;

        lock (_layerLock)
        {
            foreach (var layer in _layers.Values) layer.OnDeactivated();
            _layers.Clear();
        }
    }
}
