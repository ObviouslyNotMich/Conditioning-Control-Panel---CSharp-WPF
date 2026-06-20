using System.Windows.Input;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using ConditioningControlPanel.Core.Models;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia-compatible wrapper around a <see cref="ContentPack"/> for the
/// <see cref="ContentPackCard"/> control. Bridges WPF-specific preview images
/// to Avalonia <see cref="Bitmap"/> and exposes install/activate commands.
/// </summary>
public partial class PackCardViewModel : ObservableObject
{
    private ContentPack _pack;

    public PackCardViewModel(ContentPack pack)
    {
        _pack = pack;
        _pack.PropertyChanged += OnPackPropertyChanged;
        RefreshDerivedProperties();
    }

    public ContentPack Pack => _pack;

    public string Id => _pack.Id;

    public string Name => _pack.Name;

    public string Description => _pack.Description;

    public string SizeDisplay => _pack.SizeDisplay;

    public int ImageCount => _pack.ImageCount;

    public int VideoCount => _pack.VideoCount;

    public bool IsExternal => _pack.IsExternal;

    public bool IsDownloaded => _pack.IsDownloaded;

    public bool IsActive => _pack.IsActive;

    public bool IsDownloading => _pack.IsDownloading;

    public bool IsNotDownloading => _pack.IsNotDownloading;

    public double DownloadProgress => _pack.DownloadProgress;

    public string DownloadButtonText => _pack.DownloadButtonText;

    public string ActivateButtonText => _pack.ActivateButtonText;

    public string? PreviewImageUrl => string.IsNullOrWhiteSpace(_pack.PreviewImageUrl) ? null : _pack.PreviewImageUrl;

    [ObservableProperty]
    private Bitmap? _previewImage;

    public bool HasPreviewImage => PreviewImage is not null;

    public bool HasAnyPreview => HasPreviewImage || !string.IsNullOrWhiteSpace(PreviewImageUrl);

    [ObservableProperty]
    private ICommand? _installCommand;

    [ObservableProperty]
    private object? _installCommandParameter;

    [ObservableProperty]
    private ICommand? _activateCommand;

    [ObservableProperty]
    private object? _activateCommandParameter;

    public void SetPack(ContentPack pack)
    {
        if (_pack != pack)
        {
            _pack.PropertyChanged -= OnPackPropertyChanged;
            _pack = pack;
            _pack.PropertyChanged += OnPackPropertyChanged;
            RefreshDerivedProperties();
        }
    }

    public void SetPreviewImage(Bitmap? image)
    {
        PreviewImage = image;
        OnPropertyChanged(nameof(HasPreviewImage));
        OnPropertyChanged(nameof(HasAnyPreview));
    }

    private void OnPackPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null)
        {
            RefreshDerivedProperties();
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(ContentPack.Name):
                OnPropertyChanged(nameof(Name));
                break;
            case nameof(ContentPack.Description):
                OnPropertyChanged(nameof(Description));
                break;
            case nameof(ContentPack.SizeDisplay):
                OnPropertyChanged(nameof(SizeDisplay));
                break;
            case nameof(ContentPack.ImageCount):
                OnPropertyChanged(nameof(ImageCount));
                break;
            case nameof(ContentPack.VideoCount):
                OnPropertyChanged(nameof(VideoCount));
                break;
            case nameof(ContentPack.IsExternal):
                OnPropertyChanged(nameof(IsExternal));
                break;
            case nameof(ContentPack.IsDownloaded):
                OnPropertyChanged(nameof(IsDownloaded));
                OnPropertyChanged(nameof(ShowActivateButton));
                break;
            case nameof(ContentPack.IsActive):
                OnPropertyChanged(nameof(IsActive));
                break;
            case nameof(ContentPack.IsDownloading):
                OnPropertyChanged(nameof(IsDownloading));
                OnPropertyChanged(nameof(IsNotDownloading));
                break;
            case nameof(ContentPack.DownloadProgress):
                OnPropertyChanged(nameof(DownloadProgress));
                break;
            case nameof(ContentPack.DownloadButtonText):
                OnPropertyChanged(nameof(DownloadButtonText));
                break;
            case nameof(ContentPack.ActivateButtonText):
                OnPropertyChanged(nameof(ActivateButtonText));
                break;
            case nameof(ContentPack.PreviewImageUrl):
                OnPropertyChanged(nameof(PreviewImageUrl));
                OnPropertyChanged(nameof(HasAnyPreview));
                break;
            case nameof(ContentPack.CurrentPreviewImage):
                OnPropertyChanged(nameof(PreviewImage));
                break;
        }
    }

    public bool ShowActivateButton => IsDownloaded;

    private void RefreshDerivedProperties()
    {
        OnPropertyChanged(nameof(Id));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(SizeDisplay));
        OnPropertyChanged(nameof(ImageCount));
        OnPropertyChanged(nameof(VideoCount));
        OnPropertyChanged(nameof(IsExternal));
        OnPropertyChanged(nameof(IsDownloaded));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsDownloading));
        OnPropertyChanged(nameof(IsNotDownloading));
        OnPropertyChanged(nameof(DownloadProgress));
        OnPropertyChanged(nameof(DownloadButtonText));
        OnPropertyChanged(nameof(ActivateButtonText));
        OnPropertyChanged(nameof(PreviewImageUrl));
        OnPropertyChanged(nameof(HasPreviewImage));
        OnPropertyChanged(nameof(HasAnyPreview));
        OnPropertyChanged(nameof(ShowActivateButton));
    }
}
