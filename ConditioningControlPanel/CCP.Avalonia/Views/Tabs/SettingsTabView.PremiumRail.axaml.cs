using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Services.Auth;
using ConditioningControlPanel.Avalonia.ViewModels;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Autonomy;
using ConditioningControlPanel.Core.Services.Awareness;
using ConditioningControlPanel.Core.Services.BlinkTrainer;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

/// <summary>
/// Dashboard premium quick-toggle rail (port of the WPF <c>SettingsTabView</c> col-0 rail).
/// Surfaces the gated features (Takeover, Awareness, Haptics, Voice, Lockdown, Blink, Remote)
/// as one-tap chips on the left of the dashboard. Takeover and Awareness toggle their service
/// directly (clean Start/Stop seams); the others jump to their dedicated tab where the full,
/// consent-gated flow already lives — so nothing risky is duplicated here. Each chip carries a
/// live status dot reflecting the underlying service state, and the whole rail is locked behind
/// a translucent overlay when the user is not a logged-in patron.
/// </summary>
public partial class SettingsTabView
{
    private static readonly ISolidColorBrush PremiumDotOn = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly ISolidColorBrush PremiumDotOff = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));

    private DispatcherTimer? _railRefreshTimer;
    private AvaloniaPatreonProvider? _railPatreon;
    private bool _railPatreonSubscribed;

    /// <summary>Wires the rail to patron-status changes and starts the live dot refresh.</summary>
    internal void InitPremiumRail()
    {
        _railPatreon = App.Services?.GetService<AvaloniaPatreonProvider>();
        if (_railPatreon != null && !_railPatreonSubscribed)
        {
            _railPatreon.PropertyChanged += OnRailPatreonChanged;
            _railPatreonSubscribed = true;
        }

        RefreshPremiumRail();

        // The services raise no generic "state changed" event, so poll once a second to keep the
        // status dots honest when a feature is started/stopped from elsewhere (a tab, a session,
        // the AI companion, etc.). Cheap and avoids missed updates.
        _railRefreshTimer?.Stop();
        _railRefreshTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => RefreshPremiumRail());
        _railRefreshTimer.Start();
    }

    /// <summary>Tears down rail subscriptions (called on unload).</summary>
    internal void ShutdownPremiumRail()
    {
        _railRefreshTimer?.Stop();
        _railRefreshTimer = null;
        if (_railPatreon != null && _railPatreonSubscribed)
        {
            _railPatreon.PropertyChanged -= OnRailPatreonChanged;
            _railPatreonSubscribed = false;
        }
        _railPatreon = null;
    }

    private void OnRailPatreonChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AvaloniaPatreonProvider.HasPremiumAccess))
            Dispatcher.UIThread.Post(RefreshPremiumRail);
    }

    private void NavigateToTab(string key)
    {
        if (TopLevel.GetTopLevel(this) is Window window && window.DataContext is MainWindowViewModel mainVm)
            mainVm.SelectTabCommand.Execute(key);
    }

    // --- Toggle chips (safe Start/Stop seams) ---

    private void ChipTakeover_Click(object? sender, RoutedEventArgs e)
    {
        if (App.Services?.GetService<IAutonomyService>() is { } autonomy)
        {
            if (autonomy.IsEnabled) autonomy.Stop(); else autonomy.Start();
            RefreshPremiumRail();
        }
    }

    private void ChipAwareness_Click(object? sender, RoutedEventArgs e)
    {
        if (App.Services?.GetService<IAwarenessService>() is { } awareness)
        {
            if (awareness.IsRunning) awareness.Stop(); else awareness.Start();
            RefreshPremiumRail();
        }
    }

    // --- Launcher chips (jump to the feature's full tab — consent/login flows live there) ---

    private void ChipHaptics_Click(object? sender, RoutedEventArgs e) => NavigateToTab("haptics");
    private void ChipVoice_Click(object? sender, RoutedEventArgs e) => NavigateToTab("shelistening");
    private void ChipLockdown_Click(object? sender, RoutedEventArgs e) => NavigateToTab("lockdown");
    private void ChipBlink_Click(object? sender, RoutedEventArgs e) => NavigateToTab("blinktrainer");
    private void ChipRemote_Click(object? sender, RoutedEventArgs e) => NavigateToTab("remotecontrol");

    /// <summary>Repaints the rail: patron lock overlay + per-chip status dots.</summary>
    internal void RefreshPremiumRail()
    {
        var premium = _railPatreon?.HasPremiumAccess == true;
        if (PremiumRailLock != null) PremiumRailLock.IsVisible = !premium;
        if (PremiumRailContent != null) PremiumRailContent.IsEnabled = premium;

        SetDot(DotTakeover, App.Services?.GetService<IAutonomyService>()?.IsEnabled == true);
        SetDot(DotAwareness, App.Services?.GetService<IAwarenessService>()?.IsRunning == true);
        SetDot(DotHaptics, App.Services?.GetService<IHapticsService>()?.IsConnected == true);
        SetDot(DotVoice, App.Services?.GetService<IAutonomyService>()?.UserDrivenVoiceArmed == true);
        SetDot(DotLockdown, App.Services?.GetService<ILockdownService>()?.IsActive == true);
        SetDot(DotBlink, App.Services?.GetService<IBlinkTrainerService>()?.IsRunning == true);
        SetDot(DotRemote, App.Services?.GetService<IRemoteControlService>()?.IsActive == true);
    }

    private static void SetDot(Ellipse? dot, bool on)
    {
        if (dot != null) dot.Fill = on ? PremiumDotOn : PremiumDotOff;
    }
}
