using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Dialogs;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Features;

public partial class SystemFeatureControl : UserControl
{
    private readonly ISettingsService _settings;
    private readonly IStartupRegistration _startup;
    private readonly IInputHook? _inputHook;
    private readonly IDialogService _dialogService;
    private readonly ILogger<SystemFeatureControl>? _logger;
    private bool _isLoading = true;
    private bool _capturingPanicKey;
    private DispatcherTimer? _panicKeyConfirmationTimer;

    public IPlatformCapabilities Capabilities { get; }

    public SystemFeatureControl()
    {
        _settings = App.Services.GetRequiredService<ISettingsService>();
        _startup = App.Services.GetRequiredService<IStartupRegistration>();
        _inputHook = App.Services.GetService<IInputHook>();
        Capabilities = App.Services.GetRequiredService<IPlatformCapabilities>();
        _dialogService = App.Services.GetRequiredService<IDialogService>();
        _logger = App.Services.GetRequiredService<ILogger<SystemFeatureControl>>();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        LoadFromSettings();
        if (_settings.Current is INotifyPropertyChanged inpc)
            inpc.PropertyChanged += OnSettingsPropertyChanged;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _panicKeyConfirmationTimer?.Stop();
        _panicKeyConfirmationTimer = null;
        if (_settings.Current is INotifyPropertyChanged inpc)
            inpc.PropertyChanged -= OnSettingsPropertyChanged;
    }

    private void LoadFromSettings()
    {
        if (_settings.Current is not { } s) return;
        _isLoading = true;
        try
        {
            ChkMultiMon.IsChecked = s.DualMonitorEnabled;
            ChkFillAllMon.IsChecked = s.FillAllMonitorsWithVideo;
            ChkWinStart.IsChecked = s.RunOnStartup;
            ChkVidLaunch.IsChecked = s.ForceVideoOnLaunch;
            ChkAutoRun.IsChecked = s.AutoStartEngine;
            ChkStartHidden.IsChecked = s.StartMinimized;
            ChkNoPanic.IsChecked = !s.PanicKeyEnabled;
            ChkOfflineMode.IsChecked = s.OfflineMode;

            TxtStartupVideo.Text = string.IsNullOrEmpty(s.StartupVideoPath)
                ? Loc.Get("label_random")
                : Path.GetFileName(s.StartupVideoPath);

            // Skip overwriting the button while we're showing the "Press any key..."
            // prompt — LoadFromSettings runs on Loaded and on unrelated property changes,
            // and we don't want it to clobber the in-progress capture state.
            if (!_capturingPanicKey)
                BtnPanicKey.Content = $"🔑 {s.PanicKey}";
        }
        finally { _isLoading = false; }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.DualMonitorEnabled)
            or nameof(AppSettings.FillAllMonitorsWithVideo)
            or nameof(AppSettings.ForceVideoOnLaunch)
            or nameof(AppSettings.AutoStartEngine)
            or nameof(AppSettings.StartMinimized)
            or nameof(AppSettings.PanicKeyEnabled)
            or nameof(AppSettings.PanicKey)
            or nameof(AppSettings.OfflineMode)
            or nameof(AppSettings.StartupVideoPath)
            or nameof(AppSettings.RunOnStartup))
        {
            Dispatcher.UIThread.Post(LoadFromSettings);
        }
    }

    // ---- Simple local toggles (write directly to settings) ----

    private void ChkMultiMon_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.DualMonitorEnabled = ChkMultiMon.IsChecked ?? false;
        _settings.Save();
    }

    private void ChkFillAllMon_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.FillAllMonitorsWithVideo = ChkFillAllMon.IsChecked ?? false;
        _settings.Save();
    }

    private void ChkVidLaunch_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.ForceVideoOnLaunch = ChkVidLaunch.IsChecked ?? false;
        _settings.Save();
    }

    private void ChkAutoRun_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.AutoStartEngine = ChkAutoRun.IsChecked ?? false;
        _settings.Save();
    }

    private void ChkStartHidden_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.StartMinimized = ChkStartHidden.IsChecked ?? false;
        _settings.Save();
    }

    // ---- Complex toggles delegated to platform-specific helpers ----

    private async void ChkWinStart_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var enable = ChkWinStart.IsChecked ?? false;

        if (enable && (_settings.Current.StartMinimized || ChkStartHidden.IsChecked == true))
        {
            var confirmed = await _dialogService.ShowConfirmationAsync(
                Loc.Get("title_startup_warning"),
                Loc.Get("msg_startup_hidden_warning"));
            if (!confirmed)
            {
                _isLoading = true;
                ChkWinStart.IsChecked = false;
                _isLoading = false;
                return;
            }
        }

        try
        {
            _startup.SetRegistered(enable);
            var actual = _startup.IsRegistered;
            if (actual != enable)
            {
                await _dialogService.ShowMessageAsync(
                    Loc.Get("title_startup_error"),
                    Loc.Get("msg_failed_to_update_startup"),
                    DialogSeverity.Warning);
            }
            enable = actual;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to update startup registration");
            await _dialogService.ShowMessageAsync(
                Loc.Get("title_startup_error"),
                Loc.Get("msg_failed_to_update_startup"),
                DialogSeverity.Warning);
            enable = _startup.IsRegistered;
        }

        _isLoading = true;
        ChkWinStart.IsChecked = enable;
        _isLoading = false;

        _settings.Current.RunOnStartup = enable;
        _settings.Save();
    }

    private async void ChkNoPanic_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var s = _settings.Current;

        var disablePanic = ChkNoPanic.IsChecked ?? false;

        if (disablePanic)
        {
            var owner = TopLevel.GetTopLevel(this) as Window;
            var confirmed = owner != null && await WarningDialog.ShowDoubleWarning(
                owner,
                Loc.Get("setting_no_panic"),
                Loc.Get("msg_disable_panic_key_consequences"));

            if (!confirmed)
            {
                ChkNoPanic.IsChecked = false;
                return;
            }
        }

        s.PanicKeyEnabled = !disablePanic;
        _settings.Save();
    }

    private async void ChkOfflineMode_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var s = _settings.Current;

        var enable = ChkOfflineMode.IsChecked ?? false;

        if (enable && string.IsNullOrWhiteSpace(s.OfflineUsername))
        {
            var owner = TopLevel.GetTopLevel(this) as Window;
            if (owner != null)
            {
                var dialog = new OfflineUsernameDialog
                {
                    Title = Loc.Get("dialog_offline_username_choose_offline_username_title")
                };
                var result = await dialog.ShowDialog<bool?>(owner);
                if (result == true && !string.IsNullOrWhiteSpace(dialog.Username))
                {
                    s.OfflineUsername = dialog.Username;
                }
                else
                {
                    ChkOfflineMode.IsChecked = false;
                    return;
                }
            }
        }

        s.OfflineMode = enable;
        _settings.Save();
    }

    private void BtnPanicKey_Click(object? sender, RoutedEventArgs e)
    {
        if (_capturingPanicKey || _inputHook == null) return;
        _capturingPanicKey = true;
        BtnPanicKey.Content = Loc.Get("msg_press_any_key_to_set_as_the_new_panic_key");
        BtnPanicKey.IsEnabled = false;

        EventHandler<KeyboardHookEventArgs>? handler = null;
        handler = (s, ev) =>
        {
            if (_inputHook == null) return;
            _inputHook.KeyPressed -= handler;

            var keyName = VirtualKeyToName(ev.VirtualKeyCode);
            if (_settings.Current is { } settings && !string.IsNullOrEmpty(keyName))
            {
                settings.PanicKey = keyName;
                _settings.Save();
            }

            Dispatcher.UIThread.Post(() =>
            {
                var newKey = _settings.Current?.PanicKey ?? "?";
                _capturingPanicKey = false;
                BtnPanicKey.IsEnabled = true;

                // Brief confirmation, then settle into normal label.
                BtnPanicKey.Content = $"✓ {newKey}";
                _panicKeyConfirmationTimer?.Stop();
                var confirmationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                EventHandler? confirmationHandler = null;
                confirmationHandler = (_, _) =>
                {
                    confirmationTimer.Stop();
                    confirmationTimer.Tick -= confirmationHandler;
                    Dispatcher.UIThread.Post(() => BtnPanicKey.Content = $"🔑 {newKey}");
                };
                confirmationTimer.Tick += confirmationHandler;
                confirmationTimer.Start();
                _panicKeyConfirmationTimer = confirmationTimer;
            });
        };

        _inputHook.KeyPressed += handler;
    }

    private static string VirtualKeyToName(int virtualKeyCode)
    {
        // Virtual-key codes are defined by Windows; keep a small portable map.
        return virtualKeyCode switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x13 => "Pause",
            0x1B => "Escape",
            0x20 => "Space",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2C => "PrintScreen",
            0x2D => "Insert",
            0x2E => "Delete",
            >= 0x30 and <= 0x39 => ((char)('0' + (virtualKeyCode - 0x30))).ToString(),
            >= 0x41 and <= 0x5A => ((char)('A' + (virtualKeyCode - 0x41))).ToString(),
            0x60 => "NumPad0",
            0x61 => "NumPad1",
            0x62 => "NumPad2",
            0x63 => "NumPad3",
            0x64 => "NumPad4",
            0x65 => "NumPad5",
            0x66 => "NumPad6",
            0x67 => "NumPad7",
            0x68 => "NumPad8",
            0x69 => "NumPad9",
            0x70 => "F1",
            0x71 => "F2",
            0x72 => "F3",
            0x73 => "F4",
            0x74 => "F5",
            0x75 => "F6",
            0x76 => "F7",
            0x77 => "F8",
            0x78 => "F9",
            0x79 => "F10",
            0x7A => "F11",
            0x7B => "F12",
            0x90 => "NumLock",
            0x91 => "ScrollLock",
            _ => $"VK{virtualKeyCode:X}"
        };
    }

    // ---- Asset folder / startup video ----

    private async void BtnPickAssets_Click(object? sender, RoutedEventArgs e)
    {
        if (_settings.Current == null) return;
        try
        {
            var dialog = App.Services.GetRequiredService<IDialogService>();
            var current = _settings.Current.CustomAssetsPath;
            var initial = !string.IsNullOrWhiteSpace(current) && Directory.Exists(current)
                ? current
                : DefaultAssetsPath;

            var selected = await dialog.ShowOpenFolderDialogAsync(Loc.Get("title_select_custom_assets_folder"));
            if (string.IsNullOrWhiteSpace(selected)) return;

            _settings.Current.CustomAssetsPath = selected;

            // Ensure the subdirectories the app expects exist.
            try
            {
                Directory.CreateDirectory(Path.Combine(selected, "images"));
                Directory.CreateDirectory(Path.Combine(selected, "videos"));
                Directory.CreateDirectory(Path.Combine(selected, "wallpapers"));
                Directory.CreateDirectory(Path.Combine(selected, ".packs"));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not create custom assets subdirectories");
            }

            _settings.Save();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Pick assets folder failed");
        }
    }

    private void BtnOpenAssets_Click(object? sender, RoutedEventArgs e)
    {
        if (_settings.Current == null) return;
        try
        {
            var folder = !string.IsNullOrWhiteSpace(_settings.Current.CustomAssetsPath)
                ? _settings.Current.CustomAssetsPath
                : DefaultAssetsPath;

            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Open assets folder failed");
        }
    }

    private async void BtnSelectStartupVideo_Click(object? sender, RoutedEventArgs e)
    {
        if (_settings.Current == null) return;
        try
        {
            var dialog = App.Services.GetRequiredService<IDialogService>();
            var result = await dialog.ShowOpenFileDialogAsync(
                Loc.Get("title_select_startup_video"),
                new FileFilter[]
                {
                    new(Loc.Get("label_video_files"), new[] { "mp4", "webm", "mov", "avi", "mkv" }),
                    new(Loc.Get("label_all_files"), new[] { "*" })
                });

            if (result.Count == 0) return;

            var s = _settings.Current;
            s.StartupVideoPath = result[0];
            TxtStartupVideo.Text = Path.GetFileName(s.StartupVideoPath);
            _settings.Save();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Select startup video failed");
        }
    }

    private static string DefaultAssetsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ConditioningControlPanel",
        "assets");

    private void BtnClearStartupVideo_Click(object? sender, RoutedEventArgs e)
    {
        if (_settings.Current is not { } s) return;
        s.StartupVideoPath = null;
        TxtStartupVideo.Text = Loc.Get("label_random");
        _settings.Save();
    }
}
