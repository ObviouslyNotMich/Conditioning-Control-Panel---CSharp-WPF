using System;
using System.IO;
using System.Windows.Input;
using ConditioningControlPanel.Core.Localization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Features;

/// <summary>
/// Skill-tree node card. Shows a full-bleed skill image, title strip, cost/status
/// button, lock overlay, and state-based border/glow.
/// </summary>
public partial class SkillNodeCard : UserControl
{
    private string _title = "Skill";
    public static readonly DirectProperty<SkillNodeCard, string> TitleProperty =
        AvaloniaProperty.RegisterDirect<SkillNodeCard, string>(
            nameof(Title), o => o.Title, (o, v) => o.Title = v);

    private Bitmap? _icon;
    public static readonly DirectProperty<SkillNodeCard, Bitmap?> IconProperty =
        AvaloniaProperty.RegisterDirect<SkillNodeCard, Bitmap?>(
            nameof(Icon), o => o.Icon, (o, v) => o.Icon = v);

    private string? _iconUri;
    public static readonly DirectProperty<SkillNodeCard, string?> IconUriProperty =
        AvaloniaProperty.RegisterDirect<SkillNodeCard, string?>(
            nameof(IconUri), o => o.IconUri, (o, v) => o.IconUri = v);

    private bool _isUnlocked;
    public static readonly DirectProperty<SkillNodeCard, bool> IsUnlockedProperty =
        AvaloniaProperty.RegisterDirect<SkillNodeCard, bool>(
            nameof(IsUnlocked), o => o.IsUnlocked, (o, v) => o.IsUnlocked = v);

    private bool _canPurchase;
    public static readonly DirectProperty<SkillNodeCard, bool> CanPurchaseProperty =
        AvaloniaProperty.RegisterDirect<SkillNodeCard, bool>(
            nameof(CanPurchase), o => o.CanPurchase, (o, v) => o.CanPurchase = v);

    private bool _isLocked;
    public static readonly DirectProperty<SkillNodeCard, bool> IsLockedProperty =
        AvaloniaProperty.RegisterDirect<SkillNodeCard, bool>(
            nameof(IsLocked), o => o.IsLocked, (o, v) => o.IsLocked = v);

    private string _statusText = "";
    public static readonly DirectProperty<SkillNodeCard, string> StatusTextProperty =
        AvaloniaProperty.RegisterDirect<SkillNodeCard, string>(
            nameof(StatusText), o => o.StatusText, (o, v) => o.StatusText = v);

    private int _cost;
    public static readonly DirectProperty<SkillNodeCard, int> CostProperty =
        AvaloniaProperty.RegisterDirect<SkillNodeCard, int>(
            nameof(Cost), o => o.Cost, (o, v) => o.Cost = v);

    private string? _prerequisiteName;
    public static readonly DirectProperty<SkillNodeCard, string?> PrerequisiteNameProperty =
        AvaloniaProperty.RegisterDirect<SkillNodeCard, string?>(
            nameof(PrerequisiteName), o => o.PrerequisiteName, (o, v) => o.PrerequisiteName = v);

    private ICommand? _purchaseCommand;
    public static readonly DirectProperty<SkillNodeCard, ICommand?> PurchaseCommandProperty =
        AvaloniaProperty.RegisterDirect<SkillNodeCard, ICommand?>(
            nameof(PurchaseCommand), o => o.PurchaseCommand, (o, v) => o.PurchaseCommand = v);

    private ICommand? _detailsCommand;
    public static readonly DirectProperty<SkillNodeCard, ICommand?> DetailsCommandProperty =
        AvaloniaProperty.RegisterDirect<SkillNodeCard, ICommand?>(
            nameof(DetailsCommand), o => o.DetailsCommand, (o, v) => o.DetailsCommand = v);

    private object? _commandParameter;
    public static readonly DirectProperty<SkillNodeCard, object?> CommandParameterProperty =
        AvaloniaProperty.RegisterDirect<SkillNodeCard, object?>(
            nameof(CommandParameter), o => o.CommandParameter, (o, v) => o.CommandParameter = v);

    public string Title
    {
        get => _title;
        set => SetAndRaise(TitleProperty, ref _title, value);
    }

    public Bitmap? Icon
    {
        get => _icon;
        set => SetAndRaise(IconProperty, ref _icon, value);
    }

    public string? IconUri
    {
        get => _iconUri;
        set => SetAndRaise(IconUriProperty, ref _iconUri, value);
    }

    public bool IsUnlocked
    {
        get => _isUnlocked;
        set => SetAndRaise(IsUnlockedProperty, ref _isUnlocked, value);
    }

    public bool CanPurchase
    {
        get => _canPurchase;
        set => SetAndRaise(CanPurchaseProperty, ref _canPurchase, value);
    }

    public bool IsLocked
    {
        get => _isLocked;
        set => SetAndRaise(IsLockedProperty, ref _isLocked, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetAndRaise(StatusTextProperty, ref _statusText, value);
    }

    public int Cost
    {
        get => _cost;
        set => SetAndRaise(CostProperty, ref _cost, value);
    }

    public string? PrerequisiteName
    {
        get => _prerequisiteName;
        set => SetAndRaise(PrerequisiteNameProperty, ref _prerequisiteName, value);
    }

    public ICommand? PurchaseCommand
    {
        get => _purchaseCommand;
        set => SetAndRaise(PurchaseCommandProperty, ref _purchaseCommand, value);
    }

    public ICommand? DetailsCommand
    {
        get => _detailsCommand;
        set => SetAndRaise(DetailsCommandProperty, ref _detailsCommand, value);
    }

    public object? CommandParameter
    {
        get => _commandParameter;
        set => SetAndRaise(CommandParameterProperty, ref _commandParameter, value);
    }

    static SkillNodeCard()
    {
        TitleProperty.Changed.AddClassHandler<SkillNodeCard>(OnTitleChanged);
        IconProperty.Changed.AddClassHandler<SkillNodeCard>(OnIconChanged);
        IconUriProperty.Changed.AddClassHandler<SkillNodeCard>(OnIconUriChanged);
        IsUnlockedProperty.Changed.AddClassHandler<SkillNodeCard>(OnStateChanged);
        CanPurchaseProperty.Changed.AddClassHandler<SkillNodeCard>(OnStateChanged);
        IsLockedProperty.Changed.AddClassHandler<SkillNodeCard>(OnStateChanged);
        StatusTextProperty.Changed.AddClassHandler<SkillNodeCard>(OnStatusTextChanged);
        CostProperty.Changed.AddClassHandler<SkillNodeCard>(OnStateChanged);
        PrerequisiteNameProperty.Changed.AddClassHandler<SkillNodeCard>(OnStateChanged);
        PurchaseCommandProperty.Changed.AddClassHandler<SkillNodeCard>(OnCommandsChanged);
        DetailsCommandProperty.Changed.AddClassHandler<SkillNodeCard>(OnCommandsChanged);
        CommandParameterProperty.Changed.AddClassHandler<SkillNodeCard>(OnCommandsChanged);
    }

    public SkillNodeCard()
    {
        InitializeComponent();
        ApplyState();
    }

    private static void OnTitleChanged(SkillNodeCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.TxtTitle.Text = c.Title ?? "";
    }

    private static void OnIconChanged(SkillNodeCard c, AvaloniaPropertyChangedEventArgs e)
    {
        var src = c.Icon;
        if (src is not null)
        {
            c.ImgIconHost.Background = new ImageBrush(src)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentY = AlignmentY.Center
            };
        }
        else
        {
            c.ImgIconHost.Background = null;
        }
    }

    private static void OnIconUriChanged(SkillNodeCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.Icon = LoadBitmapFromUri(c.IconUri);
    }

    private static void OnStatusTextChanged(SkillNodeCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.TxtStatus.Text = c.StatusText ?? "";
    }

    private static void OnStateChanged(SkillNodeCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.ApplyState();
    }

    private static void OnCommandsChanged(SkillNodeCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.BindCommandButtons();
    }

    private void BindCommandButtons()
    {
        BtnPurchase.Command = PurchaseCommand;
        BtnPurchase.CommandParameter = CommandParameter;
        BtnDetails.Command = DetailsCommand;
        BtnDetails.CommandParameter = CommandParameter;
    }

    private void ApplyState()
    {
        // Status text fallback
        TxtStatus.Text = StatusText;

        if (IsUnlocked)
        {
            RootBorder.BorderBrush = new SolidColorBrush(Color.Parse("#90EE90"));
            RootBorder.BoxShadow = new BoxShadows(new BoxShadow { Blur = 18, Color = Color.Parse("#8C90EE90") });
            ContentRoot.Opacity = 1.0;
            LockedOverlay.IsVisible = false;
            BtnPurchase.IsVisible = false;
            BtnDetails.IsVisible = true;
        }
        else if (CanPurchase)
        {
            RootBorder.BorderBrush = (IBrush?)Application.Current?.Resources["PinkBrush"];
            RootBorder.BoxShadow = new BoxShadows(new BoxShadow { Blur = 18, Color = Application.Current?.Resources["TransparentPink50"] is Color c ? c : Colors.Transparent });
            ContentRoot.Opacity = 1.0;
            LockedOverlay.IsVisible = false;
            BtnPurchase.IsVisible = true;
            BtnDetails.IsVisible = true;
        }
        else
        {
            RootBorder.BorderBrush = new SolidColorBrush(Color.Parse("#555566"));
            RootBorder.BoxShadow = new BoxShadows(new BoxShadow { Blur = 18, Color = Color.Parse("#00000000") });
            ContentRoot.Opacity = 0.55;
            LockedOverlay.IsVisible = true;
            BtnPurchase.IsVisible = false;
            BtnDetails.IsVisible = true;
            TxtLockLabel.Text = string.IsNullOrWhiteSpace(PrerequisiteName)
                ? Loc.Get("label_locked")
                : Loc.GetF("label_skill_requires", PrerequisiteName);
        }
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
            // Fail-soft: missing assets are expected until mod/image resources are ported.
        }
        return null;
    }
}
