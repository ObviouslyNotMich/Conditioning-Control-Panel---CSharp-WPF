using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.ViewModels;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;

using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Avalonia port of the season-rollover surface. Wraps the recap card plus share
/// actions and a "continue to next season" button.
///
/// Rendering to PNG/clipboard is WPF-only today; the share actions are stubbed
/// with clear TODOs and surface a status message until a cross-platform export
/// service is available.
/// </summary>
public partial class SeasonRecapWindow : Window
{
    private SeasonRecapViewModel? _vm;
    private SeasonRecapCard? _card;
    private DispatcherTimer? _statusTimer;
    private readonly IDialogService? _dialogService;

    public SeasonRecapWindow()
    {
        InitializeComponent();

_dialogService = App.Services.GetService<IDialogService>();
    }

    public SeasonRecapWindow(SeasonRecapViewModel vm) : this()
    {
        SetViewModel(vm);
    }

    public void SetViewModel(SeasonRecapViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));

        _card = new SeasonRecapCard { AnimateReveal = true };
        _card.SetViewModel(vm);
        PART_CardHost.Child = _card;

        BtnCopy.Content = Loc.Get("recap_btn_copy");
        BtnSave.Content = Loc.Get("recap_btn_save");
        BtnShareX.Content = Loc.Get("recap_btn_share_x");
        BtnShareReddit.Content = Loc.Get("recap_btn_share_reddit");
        BtnContinue.Content = Loc.GetF("recap_btn_continue", _vm.NextSeasonNumber.ToString("00"));
        PART_Note.Text = Loc.Get("recap_share_note");
    }

    // ---------- share actions ----------
    private void OnCopy(object? sender, RoutedEventArgs e)
    {
        // TODO: port CardExporter to Avalonia and copy the rendered PNG to the clipboard.
        ShowStatus(Loc.Get("recap_toast_copied"));
        _ = _dialogService?.ShowMessageAsync("Share", "Copy to clipboard is not yet implemented on Avalonia.");
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        // TODO: port CardExporter to Avalonia and save the rendered PNG to disk.
        ShowStatus(Loc.Get("recap_toast_saved"));
        _ = _dialogService?.ShowMessageAsync("Share", "Save to pictures is not yet implemented on Avalonia.");
    }

    private void OnShareX(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        var url = "https://x.com/intent/post?text=" + Uri.EscapeDataString(_vm.SharePrefillText);
        if (OpenUrl(url))
            ShowStatus(Loc.Get("recap_toast_x"));
    }

    private void OnShareReddit(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        var url = "https://www.reddit.com/submit?title=" + Uri.EscapeDataString(_vm.SharePrefillText);
        if (OpenUrl(url))
            ShowStatus(Loc.Get("recap_toast_reddit"));
    }

    private void OnContinue(object? sender, RoutedEventArgs e) => Close();

    private static bool OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>()?.Warning(ex, "SeasonRecapWindow: failed to open URL {Url}", url);
            return false;
}
    }

    private void ShowStatus(string message)
    {
        PART_Status.Text = message;
        PART_Status.IsVisible = true;

        _statusTimer?.Stop();
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _statusTimer.Tick += (s, e) =>
        {
            _statusTimer?.Stop();
            PART_Status.IsVisible = false;
        };
        _statusTimer.Start();
    }
}
