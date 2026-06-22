using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.ViewModels;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;

using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Avalonia port of the season-rollover surface. Wraps the recap card plus share
/// actions and a "continue to next season" button. Copy and save render the card
/// to a PNG via Avalonia's <see cref="RenderTargetBitmap"/> and use the cross-platform
/// <see cref="IClipboard"/> extension for the clipboard path.
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
    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        using var bitmap = RenderCardToBitmap();
        if (bitmap == null) return;

        var clipboard = this.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetBitmapAsync(bitmap);
            ShowStatus(Loc.Get("recap_toast_copied"));
        }
        else
        {
            _ = _dialogService?.ShowMessageAsync(Loc.Get("recap_window_title"), Loc.Get("msg_clipboard_unavailable"));
        }
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_dialogService == null) return;

        var filters = new[]
        {
            new FileFilter("PNG Files", new[] { "png" })
        };

        var path = await _dialogService.ShowSaveFileDialogAsync(
            Loc.Get("recap_toast_saved"),
            filters,
            "season-recap.png");

        if (string.IsNullOrEmpty(path)) return;

        using var bitmap = RenderCardToBitmap();
        if (bitmap == null) return;

        try
        {
            using var stream = File.Create(path);
            bitmap.Save(stream);
            ShowStatus(Loc.Get("recap_toast_saved"));
        }
        catch (Exception ex)
        {
            App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>()?.Warning(ex, "SeasonRecapWindow: failed to save PNG to {Path}", path);
            _ = _dialogService.ShowMessageAsync(Loc.Get("recap_window_title"), string.Format(Loc.Get("msg_save_image_failed_fmt"), ex.Message));
        }
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

    private RenderTargetBitmap? RenderCardToBitmap()
    {
        if (_card == null) return null;

        _card.PrepareForStill();
        const int width = 1040;
        const int height = 585;
        _card.Measure(new Size(width, height));
        _card.Arrange(new Rect(0, 0, width, height));

        var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        bitmap.Render(_card);
        return bitmap;
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
