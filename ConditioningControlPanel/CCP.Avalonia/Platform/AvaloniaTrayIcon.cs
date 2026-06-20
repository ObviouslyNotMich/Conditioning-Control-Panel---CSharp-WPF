using Avalonia.Controls;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Avalonia tray icon shim. Desktop only; no-op on mobile lifetimes.
/// </summary>
public sealed class AvaloniaTrayIcon : ITrayIcon
{
    private readonly TrayIcon _trayIcon;
    private readonly TrayMenu _menu;
    private bool _available = true;

    public AvaloniaTrayIcon(WindowIcon? icon = null)
    {
        _menu = new TrayMenu();
        _trayIcon = new TrayIcon
        {
            Icon = icon ?? LoadAppIcon(),
            ToolTipText = "Conditioning Control Panel",
            Menu = _menu.NativeMenu
        };
        _trayIcon.Clicked += (_, _) => Clicked?.Invoke();
    }

    /// <summary>
    /// Raised when the user clicks the tray icon (left click on Windows/Linux;
    /// macOS does not raise this event). This mirrors the legacy double-click
    /// "show dashboard" behavior.
    /// </summary>
    public event Action? Clicked;

    public ITrayMenu Menu => _menu;

    public void Show()
    {
        if (!_available) return;
        try
        {
            _trayIcon.IsVisible = true;
        }
        catch (Exception ex)
        {
            // Linux hosts without DBus/AppIndicator can warn or throw here.
            // Degrade gracefully rather than crashing the app.
            _available = false;
            Console.WriteLine($"[AvaloniaTrayIcon] Tray icon unavailable: {ex.Message}");
        }
    }

    public void Hide()
    {
        if (!_available) return;
        try
        {
            _trayIcon.IsVisible = false;
        }
        catch (Exception ex)
        {
            _available = false;
            Console.WriteLine($"[AvaloniaTrayIcon] Tray icon unavailable: {ex.Message}");
        }
    }

    public void SetTooltip(string text)
    {
        try
        {
            _trayIcon.ToolTipText = text;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AvaloniaTrayIcon] SetTooltip failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        try
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
        }
        catch
        {
            // ignored
        }
    }

    private static WindowIcon? LoadAppIcon()
    {
        try
        {
            var uri = new Uri("avares://CCP.Avalonia/Assets/app.ico", UriKind.Absolute);
            if (!global::Avalonia.Platform.AssetLoader.Exists(uri))
                return null;

            using var stream = global::Avalonia.Platform.AssetLoader.Open(uri);
            return new WindowIcon(stream);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AvaloniaTrayIcon] Failed to load app icon: {ex.Message}");
            return null;
        }
    }

    private sealed class TrayMenu : ITrayMenu
    {
        public NativeMenu NativeMenu { get; } = new NativeMenu();

        public void AddItem(string label, Action callback, bool isSeparator = false)
        {
            if (isSeparator)
            {
                NativeMenu.Add(new NativeMenuItemSeparator());
                return;
            }

            var item = new NativeMenuItem { Header = label };
            item.Click += (_, _) => callback();
            NativeMenu.Add(item);
        }

        public void Clear() => NativeMenu.Items.Clear();
    }
}
