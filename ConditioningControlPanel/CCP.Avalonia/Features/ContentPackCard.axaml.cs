using System;
using System.IO;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ConditioningControlPanel.Core.Localization;

namespace ConditioningControlPanel.Avalonia.Features;

/// <summary>
/// 192×240 card for a community content pack. Shows a preview image, badges,
/// title, description, media counts, install/activate buttons, and a progress bar.
/// </summary>
public partial class ContentPackCard : UserControl
{
    private string _title = "";
    private string _description = "";
    private string _sizeDisplay = "";
    private int _imageCount;
    private int _videoCount;
    private string? _previewImageUri;
    private Bitmap? _previewImage;
    private bool _isExternal;
    private bool _isDownloaded;
    private bool _isDownloading;
    private double _downloadProgress;
    private string _downloadButtonText = LocalizationManager.Instance["btn_install"];
    private string _activateButtonText = LocalizationManager.Instance["btn_activate"];
    private ICommand? _installCommand;
    private object? _installCommandParameter;
    private ICommand? _activateCommand;
    private object? _activateCommandParameter;

    public static readonly DirectProperty<ContentPackCard, string> TitleProperty =
        AvaloniaProperty.RegisterDirect<ContentPackCard, string>(
            nameof(Title), o => o.Title, (o, v) => o.Title = v);

    public static readonly DirectProperty<ContentPackCard, string> DescriptionProperty =
        AvaloniaProperty.RegisterDirect<ContentPackCard, string>(
            nameof(Description), o => o.Description, (o, v) => o.Description = v);

    public static readonly DirectProperty<ContentPackCard, string> SizeDisplayProperty =
        AvaloniaProperty.RegisterDirect<ContentPackCard, string>(
            nameof(SizeDisplay), o => o.SizeDisplay, (o, v) => o.SizeDisplay = v);

    public static readonly DirectProperty<ContentPackCard, int> ImageCountProperty =
        AvaloniaProperty.RegisterDirect<ContentPackCard, int>(
            nameof(ImageCount), o => o.ImageCount, (o, v) => o.ImageCount = v);

    public static readonly DirectProperty<ContentPackCard, int> VideoCountProperty =
        AvaloniaProperty.RegisterDirect<ContentPackCard, int>(
            nameof(VideoCount), o => o.VideoCount, (o, v) => o.VideoCount = v);

    public static readonly DirectProperty<ContentPackCard, string?> PreviewImageUriProperty =
        AvaloniaProperty.RegisterDirect<ContentPackCard, string?>(
            nameof(PreviewImageUri), o => o.PreviewImageUri, (o, v) => o.PreviewImageUri = v);

    public static readonly DirectProperty<ContentPackCard, Bitmap?> PreviewImageProperty =
        AvaloniaProperty.RegisterDirect<ContentPackCard, Bitmap?>(
            nameof(PreviewImage), o => o.PreviewImage, (o, v) => o.PreviewImage = v);

    public static readonly DirectProperty<ContentPackCard, bool> IsExternalProperty =
        AvaloniaProperty.RegisterDirect<ContentPackCard, bool>(
            nameof(IsExternal), o => o.IsExternal, (o, v) => o.IsExternal = v);

    public static readonly DirectProperty<ContentPackCard, bool> IsDownloadedProperty =
        AvaloniaProperty.RegisterDirect<ContentPackCard, bool>(
            nameof(IsDownloaded), o => o.IsDownloaded, (o, v) => o.IsDownloaded = v);

    public static readonly DirectProperty<ContentPackCard, bool> IsDownloadingProperty =
        AvaloniaProperty.RegisterDirect<ContentPackCard, bool>(
            nameof(IsDownloading), o => o.IsDownloading, (o, v) => o.IsDownloading = v);

    public static readonly DirectProperty<ContentPackCard, double> DownloadProgressProperty =
        AvaloniaProperty.RegisterDirect<ContentPackCard, double>(
            nameof(DownloadProgress), o => o.DownloadProgress, (o, v) => o.DownloadProgress = v);

    public static readonly DirectProperty<ContentPackCard, string> DownloadButtonTextProperty =
        AvaloniaProperty.RegisterDirect<ContentPackCard, string>(
            nameof(DownloadButtonText), o => o.DownloadButtonText, (o, v) => o.DownloadButtonText = v);

    public static readonly DirectProperty<ContentPackCard, string> ActivateButtonTextProperty =
        AvaloniaProperty.RegisterDirect<ContentPackCard, string>(
            nameof(ActivateButtonText), o => o.ActivateButtonText, (o, v) => o.ActivateButtonText = v);

    public static readonly DirectProperty<ContentPackCard, ICommand?> InstallCommandProperty =
        AvaloniaProperty.RegisterDirect<ContentPackCard, ICommand?>(
            nameof(InstallCommand), o => o.InstallCommand, (o, v) => o.InstallCommand = v);

    public static readonly DirectProperty<ContentPackCard, object?> InstallCommandParameterProperty =
        AvaloniaProperty.RegisterDirect<ContentPackCard, object?>(
            nameof(InstallCommandParameter), o => o.InstallCommandParameter, (o, v) => o.InstallCommandParameter = v);

    public static readonly DirectProperty<ContentPackCard, ICommand?> ActivateCommandProperty =
        AvaloniaProperty.RegisterDirect<ContentPackCard, ICommand?>(
            nameof(ActivateCommand), o => o.ActivateCommand, (o, v) => o.ActivateCommand = v);

    public static readonly DirectProperty<ContentPackCard, object?> ActivateCommandParameterProperty =
        AvaloniaProperty.RegisterDirect<ContentPackCard, object?>(
            nameof(ActivateCommandParameter), o => o.ActivateCommandParameter, (o, v) => o.ActivateCommandParameter = v);

    public string Title
    {
        get => _title;
        set => SetAndRaise(TitleProperty, ref _title, value);
    }

    public string Description
    {
        get => _description;
        set => SetAndRaise(DescriptionProperty, ref _description, value);
    }

    public string SizeDisplay
    {
        get => _sizeDisplay;
        set => SetAndRaise(SizeDisplayProperty, ref _sizeDisplay, value);
    }

    public int ImageCount
    {
        get => _imageCount;
        set => SetAndRaise(ImageCountProperty, ref _imageCount, value);
    }

    public int VideoCount
    {
        get => _videoCount;
        set => SetAndRaise(VideoCountProperty, ref _videoCount, value);
    }

    public string? PreviewImageUri
    {
        get => _previewImageUri;
        set => SetAndRaise(PreviewImageUriProperty, ref _previewImageUri, value);
    }

    public Bitmap? PreviewImage
    {
        get => _previewImage;
        set => SetAndRaise(PreviewImageProperty, ref _previewImage, value);
    }

    public bool IsExternal
    {
        get => _isExternal;
        set => SetAndRaise(IsExternalProperty, ref _isExternal, value);
    }

    public bool IsDownloaded
    {
        get => _isDownloaded;
        set => SetAndRaise(IsDownloadedProperty, ref _isDownloaded, value);
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        set => SetAndRaise(IsDownloadingProperty, ref _isDownloading, value);
    }

    public double DownloadProgress
    {
        get => _downloadProgress;
        set => SetAndRaise(DownloadProgressProperty, ref _downloadProgress, value);
    }

    public string DownloadButtonText
    {
        get => _downloadButtonText;
        set => SetAndRaise(DownloadButtonTextProperty, ref _downloadButtonText, value);
    }

    public string ActivateButtonText
    {
        get => _activateButtonText;
        set => SetAndRaise(ActivateButtonTextProperty, ref _activateButtonText, value);
    }

    public bool ShowActivateButton => IsDownloaded;

    public bool IsNotDownloading => !IsDownloading;

    public ICommand? InstallCommand
    {
        get => _installCommand;
        set => SetAndRaise(InstallCommandProperty, ref _installCommand, value);
    }

    public object? InstallCommandParameter
    {
        get => _installCommandParameter;
        set => SetAndRaise(InstallCommandParameterProperty, ref _installCommandParameter, value);
    }

    public ICommand? ActivateCommand
    {
        get => _activateCommand;
        set => SetAndRaise(ActivateCommandProperty, ref _activateCommand, value);
    }

    public object? ActivateCommandParameter
    {
        get => _activateCommandParameter;
        set => SetAndRaise(ActivateCommandParameterProperty, ref _activateCommandParameter, value);
    }

    static ContentPackCard()
    {
        TitleProperty.Changed.AddClassHandler<ContentPackCard>(OnTitleChanged);
        DescriptionProperty.Changed.AddClassHandler<ContentPackCard>(OnDescriptionChanged);
        SizeDisplayProperty.Changed.AddClassHandler<ContentPackCard>(OnSizeDisplayChanged);
        ImageCountProperty.Changed.AddClassHandler<ContentPackCard>(OnCountsChanged);
        VideoCountProperty.Changed.AddClassHandler<ContentPackCard>(OnCountsChanged);
        PreviewImageUriProperty.Changed.AddClassHandler<ContentPackCard>(OnPreviewImageUriChanged);
        PreviewImageProperty.Changed.AddClassHandler<ContentPackCard>(OnPreviewImageChanged);
        IsExternalProperty.Changed.AddClassHandler<ContentPackCard>(OnBadgesChanged);
        IsDownloadedProperty.Changed.AddClassHandler<ContentPackCard>(OnBadgesChanged);
        IsDownloadingProperty.Changed.AddClassHandler<ContentPackCard>(OnDownloadingChanged);
        DownloadProgressProperty.Changed.AddClassHandler<ContentPackCard>(OnDownloadProgressChanged);
        DownloadButtonTextProperty.Changed.AddClassHandler<ContentPackCard>(OnDownloadButtonTextChanged);
        ActivateButtonTextProperty.Changed.AddClassHandler<ContentPackCard>(OnActivateButtonTextChanged);
        InstallCommandProperty.Changed.AddClassHandler<ContentPackCard>(OnInstallCommandChanged);
        InstallCommandParameterProperty.Changed.AddClassHandler<ContentPackCard>(OnInstallCommandParameterChanged);
        ActivateCommandProperty.Changed.AddClassHandler<ContentPackCard>(OnActivateCommandChanged);
        ActivateCommandParameterProperty.Changed.AddClassHandler<ContentPackCard>(OnActivateCommandParameterChanged);
    }

    public ContentPackCard()
    {
        InitializeComponent();
    }

    private static void OnTitleChanged(ContentPackCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.TxtName.Text = c.Title;
    }

    private static void OnDescriptionChanged(ContentPackCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.TxtDescription.Text = c.Description;
    }

    private static void OnSizeDisplayChanged(ContentPackCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.TxtSize.Text = c.SizeDisplay;
    }

    private static void OnCountsChanged(ContentPackCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.TxtCounts.Text = string.Format(LocalizationManager.Instance["content_pack_counts_fmt"], c.ImageCount, c.VideoCount);
    }

    private static void OnPreviewImageUriChanged(ContentPackCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.RefreshPreviewImage();
    }

    private static void OnPreviewImageChanged(ContentPackCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.RefreshPreviewImage();
    }

    private static void OnBadgesChanged(ContentPackCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.RefreshBadges();
    }

    private static void OnDownloadingChanged(ContentPackCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.ProgressBar.IsVisible = c.IsDownloading;
        c.BtnInstall.IsEnabled = c.IsNotDownloading;
    }

    private static void OnDownloadProgressChanged(ContentPackCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.ProgressBar.Value = c.DownloadProgress;
    }

    private static void OnDownloadButtonTextChanged(ContentPackCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.BtnInstall.Content = c.DownloadButtonText;
    }

    private static void OnActivateButtonTextChanged(ContentPackCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.BtnActivate.Content = c.ActivateButtonText;
    }

    private static void OnInstallCommandChanged(ContentPackCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.BtnInstall.Command = c.InstallCommand;
    }

    private static void OnInstallCommandParameterChanged(ContentPackCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.BtnInstall.CommandParameter = c.InstallCommandParameter;
    }

    private static void OnActivateCommandChanged(ContentPackCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.BtnActivate.Command = c.ActivateCommand;
    }

    private static void OnActivateCommandParameterChanged(ContentPackCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.BtnActivate.CommandParameter = c.ActivateCommandParameter;
    }

    private void RefreshPreviewImage()
    {
        if (PreviewImage is not null)
        {
            ImgPreview.Source = PreviewImage;
            ImgPreview.IsVisible = true;
            TxtNoPreview.IsVisible = false;
            return;
        }

        var bitmap = LoadBitmapFromUri(PreviewImageUri);
        if (bitmap is not null)
        {
            ImgPreview.Source = bitmap;
            ImgPreview.IsVisible = true;
            TxtNoPreview.IsVisible = false;
        }
        else
        {
            ImgPreview.Source = null;
            ImgPreview.IsVisible = false;
            TxtNoPreview.IsVisible = !HasAnyPreviewSource;
        }
    }

    private bool HasAnyPreviewSource => PreviewImage is not null || !string.IsNullOrWhiteSpace(PreviewImageUri);

    private void RefreshBadges()
    {
        RootBorder.BorderBrush = IsExternal ? SolidColorBrush.Parse("#FFA500") : (IBrush?)Application.Current?.Resources["PanelAccentBrush"] ?? SolidColorBrush.Parse("#3D3D60");
        InstalledBadge.IsVisible = IsDownloaded;
        ManualDownloadBadge.IsVisible = IsExternal && !IsDownloaded;
        BtnActivate.IsVisible = IsDownloaded;
    }

    private static Bitmap? LoadBitmapFromUri(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;

        try
        {
            if (File.Exists(uri))
                return new Bitmap(uri);

            if (uri.StartsWith("file://", StringComparison.Ordinal))
            {
                var path = uri.Substring(7);
                if (File.Exists(path))
                    return new Bitmap(path);
                return null;
            }

            if (uri.StartsWith("pack://application:,,,", StringComparison.Ordinal))
            {
                var relative = uri.Substring("pack://application:,,,".Length).TrimStart('/');
                if (relative.StartsWith("Resources/", StringComparison.Ordinal))
                    relative = relative.Substring("Resources/".Length);
                var avares = $"avares://CCP.Avalonia/Assets/{relative}";
                using var stream = AssetLoader.Open(new Uri(avares));
                return new Bitmap(stream);
            }

            if (uri.StartsWith("avares://", StringComparison.Ordinal))
            {
                using var stream = AssetLoader.Open(new Uri(uri));
                return new Bitmap(stream);
            }
        }
        catch
        {
            // Fail-soft for missing or unsupported preview URIs.
        }

        return null;
    }
}
