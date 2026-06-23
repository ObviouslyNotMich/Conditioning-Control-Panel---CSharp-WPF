using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Avalonia screen enumeration shim using <see cref="Screens"/>.
/// </summary>
public sealed class AvaloniaScreenProvider : IScreenProvider
{
    private readonly Func<TopLevel?>? _getTopLevel;

    public AvaloniaScreenProvider(Func<TopLevel?>? getTopLevel = null)
    {
        _getTopLevel = getTopLevel;
    }

    private EventHandler? _screensChanged;

    public event EventHandler? ScreensChanged
    {
        add
        {
            var hadSubscribers = _screensChanged != null;
            _screensChanged += value;
            if (!hadSubscribers && _screensChanged != null)
            {
                AttachToScreens();
            }
        }
        remove
        {
            _screensChanged -= value;
            if (_screensChanged == null)
            {
                DetachFromScreens();
            }
        }
    }

    private Screens? _attachedScreens;

    private void AttachToScreens()
    {
        DetachFromScreens();
        var screens = GetScreens();
        if (screens == null) return;
        _attachedScreens = screens;
        _attachedScreens.Changed += OnScreensChanged;
    }

    private void DetachFromScreens()
    {
        if (_attachedScreens == null) return;
        _attachedScreens.Changed -= OnScreensChanged;
        _attachedScreens = null;
    }

    private void OnScreensChanged(object? sender, EventArgs e)
    {
        _screensChanged?.Invoke(this, EventArgs.Empty);
    }

    private Screens? GetScreens()
    {
        if (_getTopLevel?.Invoke() is { } top)
            return top.Screens;

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is { } window)
        {
            return window.Screens;
        }

        if (Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime single
            && single.MainView is { } view)
        {
            return TopLevel.GetTopLevel(view)?.Screens;
        }

        return null;
    }

    public IReadOnlyList<ScreenInfo> GetAllScreens()
    {
        var screens = GetScreens();
        if (screens is null) return Array.Empty<ScreenInfo>();

        return screens.All.Select(MapScreen).ToList();
    }

    public ScreenInfo? GetPrimaryScreen()
    {
        var screens = GetScreens();
        if (screens?.Primary is not { } primary) return null;
        return MapScreen(primary);
    }

    private static ScreenInfo MapScreen(global::Avalonia.Platform.Screen screen)
    {
        return new ScreenInfo(
            screen.DisplayName ?? string.Empty,
            new ConditioningControlPanel.Core.Platform.PixelRect(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height),
            new ConditioningControlPanel.Core.Platform.PixelRect(screen.WorkingArea.X, screen.WorkingArea.Y, screen.WorkingArea.Width, screen.WorkingArea.Height),
            screen.Scaling);
    }
}
