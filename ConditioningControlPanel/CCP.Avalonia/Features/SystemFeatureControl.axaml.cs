using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using ConditioningControlPanel.Core.Localization;
namespace ConditioningControlPanel.Avalonia.Features;

public partial class SystemFeatureControl : UserControl
{
    private readonly ISettingsService _settings;
    private readonly IStartupRegistration _startup;
    private readonly IInputHook? _inputHook;
    private bool _isLoading = true;
    private bool _capturingPanicKey;

    public IPlatformCapabilities Capabilities { get; }

    public SystemFeatureControl()
    {
        InitializeComponent();
        _settings = App.Services.GetRequiredService<ISettingsService>();
        _startup = App.Services.GetRequiredService<IStartupRegistration>();
        _inputHook = App.Services.GetService<IInputHook>();
        Capabilities = App.Services.GetRequiredService<IPlatformCapabilities>();
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
                ? LocalizationManager.Instance.Get("label_random")
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

    private void ChkWinStart_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var enable = ChkWinStart.IsChecked ?? false;

        try { _startup.SetRegistered(enable); }
        catch (Exception ex)
        {
            App.Services?.GetService<IAppLogger>()?.Warning(ex, "Failed to update startup registration");
        }

        _settings.Current.RunOnStartup = enable;
        _settings.Save();
    }

    private void ChkNoPanic_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var s = _settings.Current;

        var disablePanic = ChkNoPanic.IsChecked ?? false;

        if (disablePanic)
        {
            // TODO: Panic-key disable confirmation dialog (WarningDialog)
        }

        s.PanicKeyEnabled = !disablePanic;
        _settings.Save();

        // TODO: MainWindow sync helper (SyncNoPanicState)
    }

    private void ChkOfflineMode_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var s = _settings.Current;

        var enable = ChkOfflineMode.IsChecked ?? false;

        if (enable && string.IsNullOrWhiteSpace(s.OfflineUsername))
        {
            // TODO: Offline-mode username dialog (OfflineUsernameDialog)
        }

        s.OfflineMode = enable;
        _settings.Save();

        // TODO: MainWindow sync helper (SyncOfflineModeState)
    }

    private void BtnPanicKey_Click(object? sender, RoutedEventArgs e)
    {
        if (_capturingPanicKey || _inputHook == null) return;
        _capturingPanicKey = true;
        BtnPanicKey.Content = LocalizationManager.Instance.Get("msg_press_any_key_to_set_as_the_new_panic_key");
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
                var t = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(1200)
                };
                t.Tick += (_, __) =>
                {
                    t.Stop();
                    BtnPanicKey.Content = $"🔑 {newKey}";
                };
                t.Start();
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

            var selected = await dialog.ShowOpenFolderDialogAsync(LocalizationManager.Instance.Get("title_select_custom_assets_folder"));
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
                // TODO: App.Logger?.Warning(ex, "Could not create custom assets subdirectories");
                Console.WriteLine($"[SystemFeatureControl] Create subdirs failed: {ex.Message}");
            }

            _settings.Save();
        }
        catch (Exception ex)
        {
            // TODO: App.Logger?.Warning(ex, "Pick assets folder failed");
            Console.WriteLine($"[SystemFeatureControl] Pick assets folder failed: {ex.Message}");
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
            // TODO: App.Logger?.Warning(ex, "Open assets folder failed");
            Console.WriteLine($"[SystemFeatureControl] Open assets folder failed: {ex.Message}");
        }
    }

    private async void BtnSelectStartupVideo_Click(object? sender, RoutedEventArgs e)
    {
        if (_settings.Current == null) return;
        try
        {
            var dialog = App.Services.GetRequiredService<IDialogService>();
            var result = await dialog.ShowOpenFileDialogAsync(
                LocalizationManager.Instance.Get("title_select_startup_video"),
                new FileFilter[]
                {
                    new("Video Files", new[] { "mp4", "webm", "mov", "avi", "mkv" }),
                    new("All Files", new[] { "*" })
                });

            if (result.Count == 0) return;

            var s = _settings.Current;
            s.StartupVideoPath = result[0];
            TxtStartupVideo.Text = Path.GetFileName(s.StartupVideoPath);
            _settings.Save();
        }
        catch (Exception ex)
        {
            // TODO: App.Logger?.Warning(ex, "Select startup video failed");
            Console.WriteLine($"[SystemFeatureControl] Select startup video failed: {ex.Message}");
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
        TxtStartupVideo.Text = LocalizationManager.Instance.Get("label_random");
        _settings.Save();
    }
}
