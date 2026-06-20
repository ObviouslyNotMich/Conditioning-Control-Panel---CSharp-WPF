using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Media;
using global::Avalonia.Threading;

using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Avalonia port of ChaosBackdropService: non-topmost fullscreen backdrop plate under chaos bubbles.
/// Gated on NarrativeModeEnabled and BackdropEnabled in Story mode.
/// </summary>
internal static class ChaosBackdropService
{
    private static Window? _active;
    private static Image? _img;
    private static int _currentDepth = -1;
    private static OpacityFade? _fade;

    private static bool Enabled =>
        AvaloniaChaosMode.ActiveMode == ChaosPlayMode.Story &&
        App.Services?.GetService<global::ConditioningControlPanel.Core.Services.Settings.ISettingsService>()?.Current?.NarrativeModeEnabled == true &&
        App.Services?.GetService<global::ConditioningControlPanel.Core.Services.Settings.ISettingsService>()?.Current?.BackdropEnabled == true;

    public static IImage? CurrentSource => _img?.Source;

    public static void Show(int depth)
    {
        if (!Enabled) { CloseActive(); return; }
        try
        {
            if (_active == null) Build();
            SetDepth(depth);
        }
        catch (Exception ex) { App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>()?.Warning("ChaosBackdropService.Show failed: {E}", ex.Message); }
    }

    public static void SwapTo(int depth)
    {
        if (!Enabled) return;
        if (_active == null) { Show(depth); return; }
        if (depth == _currentDepth) return;
        try { SetDepth(depth); } catch (Exception ex) { App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>()?.Information("ChaosBackdropService.SwapTo: {E}", ex.Message); }
    }

    public static void CloseActive()
    {
        try { _active?.Close(); } catch { }
        _active = null; _img = null; _currentDepth = -1;
    }

    private static void SetDepth(int depth)
    {
        if (_img == null) return;
        _currentDepth = depth;
        var src = AvaloniaChaosArt.Resolve("backdrops", "depth" + depth);
        _img.Source = src;
        if (_active != null) _active.Opacity = App.Services?.GetService<global::ConditioningControlPanel.Core.Services.Settings.ISettingsService>()?.Current?.BackdropOpacity ?? 0.55;
        _fade?.Dispose();
        _img.Opacity = 0;
        _fade = new OpacityFade(_img, 0, 1, 350);
    }

    private static void Build()
    {
        _img = new Image { Stretch = Stretch.UniformToFill };
        var grid = new Grid();
        grid.Children.Add(_img);

        var screens = AvaloniaChaosWindowZ.GetScreens();
        var primary = screens?.Primary;
        var bounds =
primary?.Bounds ?? new PixelRect(0, 0, 1920, 1080);

        _active = new Window
        {
            WindowDecorations = WindowDecorations.None,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            Background = Brushes.Black,
            Topmost = false,
            ShowInTaskbar = false,
            ShowActivated = false,
            Focusable = false,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Position = new PixelPoint(bounds.X, bounds.Y),
            Width = bounds.Width,
            Height = bounds.Height,
            Opacity = App.Services?.GetService<global::ConditioningControlPanel.Core.Services.Settings.ISettingsService>()?.Current?.BackdropOpacity ?? 0.55,
            Content = grid,
        };
        _active.Opened += (_, _) => ApplyExStyles(_active);
        _active.Show();
        App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>()?.Information("ChaosBackdropService window up (non-topmost, click-absorbing)");
    }

    private static void ApplyExStyles(Window w)
    {
        // TODO: apply WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW on Windows (no WS_EX_TRANSPARENT — absorbs clicks).
    }

}
