using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Overlays;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Features;

public partial class SpiralFeatureControl : UserControl
{
    private readonly ISettingsService _settings;
    private readonly IOverlayService? _overlay;
    private readonly IAppLogger? _logger;
    private bool _isLoading = true;

    private static readonly string[] SpiralImageExts =
        { ".gif", ".png", ".jpg", ".jpeg", ".webp", ".bmp" };
    private static readonly string[] SpiralVideoExts =
        { ".mp4", ".webm", ".mov", ".avi", ".mkv" };

    private static readonly Color SelectedAccent = Color.FromRgb(0xFF, 0x69, 0xB4);
    private static readonly Color IdleAccent = Color.FromRgb(0x33, 0x33, 0x3A);

    /// <summary>User spiral folder: %LOCALAPPDATA%/ConditioningControlPanel/Spirals.</summary>
    private static string SpiralsFolderPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ConditioningControlPanel",
        "Spirals");

    public SpiralFeatureControl()
    {
        InitializeComponent();
        _settings = App.Services.GetRequiredService<ISettingsService>();
        _overlay = App.Services.GetService<IOverlayService>();
        _logger = App.Services.GetService<IAppLogger>();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        LoadFromSettings();
        RefreshLibrary();

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
            ChkEnable.IsChecked = s.SpiralEnabled;
            SliderOpacity.Value = s.SpiralOpacity;
            TxtOpacity.Text = $"{s.SpiralOpacity}%";
        }
        finally { _isLoading = false; }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.SpiralEnabled)
            or nameof(AppSettings.SpiralOpacity))
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(LoadFromSettings);
        }
        else if (e.PropertyName == nameof(AppSettings.SpiralPath))
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(UpdateSelectionHighlight);
        }
    }

    private void ChkEnable_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var s = _settings.Current;
        s.SpiralEnabled = ChkEnable.IsChecked ?? false;
        _settings.Save();

        try { _overlay?.RefreshOverlays(); }
        catch (Exception ex) { _logger?.Warning(ex, "Spiral toggle: RefreshOverlays failed"); }
    }

    private void SliderOpacity_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var s = _settings.Current;
        var value = (int)e.NewValue;
        TxtOpacity.Text = $"{value}%";
        s.SpiralOpacity = value;
        _settings.Save();

        try { _overlay?.RefreshOverlays(); }
        catch (Exception ex) { _logger?.Warning(ex, "Spiral opacity: RefreshOverlays failed"); }
    }

    // ── Spiral library ───────────────────────────────────────────────────

    private static string NormPath(string? p)
    {
        if (string.IsNullOrWhiteSpace(p)) return "";
        try { return Path.GetFullPath(p).TrimEnd('\\', '/'); }
        catch { return p.Trim(); }
    }

    /// <summary>
    /// Rebuilds the spiral preview gallery: a "Default" card for the built-in
    /// spiral plus one card per file dropped into the Spirals folder.
    /// </summary>
    private void RefreshLibrary()
    {
        if (SpiralLibraryPanel == null) return;
        SpiralLibraryPanel.Children.Clear();

        // Built-in spiral (active when SpiralPath is empty / missing).
        string? defaultThumb = null;
        var builtInPath = Path.Combine(AppContext.BaseDirectory, "Resources", "spiral.gif");
        if (File.Exists(builtInPath))
            defaultThumb = builtInPath;
        // TODO: ModResourceResolver.ResolveUri fallback for built-in spiral.

        SpiralLibraryPanel.Children.Add(BuildSpiralCard("", LocalizationManager.Instance["label_default"], defaultThumb));

        int fileCount = 0;
        try
        {
            var folder = SpiralsFolderPath;
            if (Directory.Exists(folder))
            {
                var files = Directory.EnumerateFiles(folder)
                    .Where(f => SpiralImageExts.Contains(Path.GetExtension(f).ToLowerInvariant()) ||
                                SpiralVideoExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);

                foreach (var file in files)
                {
                    fileCount++;
                    bool isVideo = SpiralVideoExts.Contains(Path.GetExtension(file).ToLowerInvariant());
                    SpiralLibraryPanel.Children.Add(
                        BuildSpiralCard(file, Path.GetFileNameWithoutExtension(file),
                                        isVideo ? null : file));
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Spiral library: enumeration failed");
        }

        if (SpiralEmptyState != null)
            SpiralEmptyState.IsVisible = fileCount == 0;
    }

    /// <summary>
    /// Builds a clickable preview card. <paramref name="path"/> is the spiral file
    /// path ("" for the built-in default). <paramref name="thumbUri"/> is a file
    /// path to render as a thumbnail, or null to show a glyph (video / unloadable).
    /// </summary>
    private Border BuildSpiralCard(string path, string display, string? thumbUri)
    {
        var card = new Border
        {
            Width = 120,
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E)),
            BorderBrush = new SolidColorBrush(IdleAccent),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 8, 8),
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = path,
        };
        ToolTip.SetTip(card, string.IsNullOrEmpty(path) ? LocalizationManager.Instance["tooltip_built_in_spiral"] : path);

        var stack = new StackPanel();

        var thumbHost = new Border
        {
            Height = 80,
            Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x14)),
            CornerRadius = new CornerRadius(6, 6, 0, 0),
            ClipToBounds = true,
        };

        if (thumbUri != null)
        {
            try
            {
                var bmp = new Bitmap(thumbUri);
                thumbHost.Child = new Image
                {
                    Source = bmp,
                    Stretch = Stretch.UniformToFill
                };
            }
            catch
            {
                thumbHost.Child = SpiralGlyph("🌀");
            }
        }
        else
        {
            thumbHost.Child = SpiralGlyph("🎬");
        }
        stack.Children.Add(thumbHost);

        stack.Children.Add(new TextBlock
        {
            Text = display,
            Foreground = Brushes.White,
            FontWeight = FontWeight.SemiBold,
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(6, 6, 6, 8),
        });

        card.Child = stack;
        card.PointerPressed += (_, _) => SelectSpiral(path);
        ApplyHighlight(card);
        return card;
    }

    private static TextBlock SpiralGlyph(string glyph) => new()
    {
        Text = glyph,
        FontSize = 32,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
    };

    /// <summary>Sets the chosen spiral as the single active spiral.</summary>
    private void SelectSpiral(string path)
    {
        if (_settings.Current is not { } s) return;

        // Clicking a missing file is a no-op (keeps the previous selection).
        if (!string.IsNullOrEmpty(path) && !File.Exists(path)) return;

        if (NormPath(s.SpiralPath) == NormPath(path)) return; // already active

        s.SpiralPath = path; // "" => built-in default
        _settings.Save();

        UpdateSelectionHighlight();

        try { _overlay?.RefreshOverlays(); }
        catch (Exception ex) { _logger?.Warning(ex, "Spiral select: RefreshOverlays failed"); }
    }

    private void UpdateSelectionHighlight()
    {
        if (SpiralLibraryPanel == null) return;
        foreach (var child in SpiralLibraryPanel.Children)
            if (child is Border b)
                ApplyHighlight(b);
    }

    private void ApplyHighlight(Border card)
    {
        var current = NormPath(_settings.Current?.SpiralPath);
        var tag = NormPath(card.Tag as string);
        bool defaultActive = string.IsNullOrEmpty(current) ||
                             !File.Exists(_settings.Current?.SpiralPath ?? "");
        bool selected = string.IsNullOrEmpty(tag) ? defaultActive : tag == current;
        card.BorderBrush = new SolidColorBrush(selected ? SelectedAccent : IdleAccent);
    }

    private void BtnOpenSpiralFolder_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folder = SpiralsFolderPath;
            Directory.CreateDirectory(folder);

            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Spiral library: open folder failed");
        }
    }

    private void BtnRefreshSpirals_Click(object? sender, RoutedEventArgs e) => RefreshLibrary();

    private async void BtnSelectGif_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = App.Services.GetRequiredService<IDialogService>();
            var result = await dialog.ShowOpenFileDialogAsync(
                LocalizationManager.Instance["title_select_spiral_file"],
                new FileFilter[]
                {
                    new(LocalizationManager.Instance["label_image_files"], new[] { "gif", "png", "jpg", "jpeg", "webp", "bmp" }),
                    new(LocalizationManager.Instance["label_video_files"], new[] { "mp4", "webm", "mov", "avi", "mkv" }),
                    new(LocalizationManager.Instance["label_all_files"], new[] { "*" })
                });

            if (result.Count == 0) return;

            var path = result[0];
            if (_settings.Current is not { } s) return;
            s.SpiralPath = path;
            _settings.Save();
            RefreshLibrary();

            try { _overlay?.RefreshOverlays(); }
            catch (Exception ex2) { _logger?.Warning(ex2, "Spiral select: RefreshOverlays failed"); }
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Spiral select failed");
        }
    }
}
