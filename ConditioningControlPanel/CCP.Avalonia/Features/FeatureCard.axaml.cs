using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;

namespace ConditioningControlPanel.Avalonia.Features;

/// <summary>
/// Click-to-open tile for a feature on the dashboard grid. Shows an icon + title;
/// when locked, desaturates the content and overlays a padlock + required level.
/// </summary>
public partial class FeatureCard : UserControl
{
    private string _title = "Feature";
    public static readonly DirectProperty<FeatureCard, string> TitleProperty =
        AvaloniaProperty.RegisterDirect<FeatureCard, string>(
            nameof(Title), o => o.Title, (o, v) => o.Title = v);

    private Bitmap? _icon;
    public static readonly DirectProperty<FeatureCard, Bitmap?> IconProperty =
        AvaloniaProperty.RegisterDirect<FeatureCard, Bitmap?>(
            nameof(Icon), o => o.Icon, (o, v) => o.Icon = v);

    private string? _glyph;
    public static readonly DirectProperty<FeatureCard, string?> GlyphProperty =
        AvaloniaProperty.RegisterDirect<FeatureCard, string?>(
            nameof(Glyph), o => o.Glyph, (o, v) => o.Glyph = v);

    private int _lockLevel;
    public static readonly DirectProperty<FeatureCard, int> LockLevelProperty =
        AvaloniaProperty.RegisterDirect<FeatureCard, int>(
            nameof(LockLevel), o => o.LockLevel, (o, v) => o.LockLevel = v);

    private bool _isLocked;
    public static readonly DirectProperty<FeatureCard, bool> IsLockedProperty =
        AvaloniaProperty.RegisterDirect<FeatureCard, bool>(
            nameof(IsLocked), o => o.IsLocked, (o, v) => o.IsLocked = v);

    private bool _isActive;
    public static readonly DirectProperty<FeatureCard, bool> IsActiveProperty =
        AvaloniaProperty.RegisterDirect<FeatureCard, bool>(
            nameof(IsActive), o => o.IsActive, (o, v) => o.IsActive = v);

    private string? _helpSectionId;
    public static readonly DirectProperty<FeatureCard, string?> HelpSectionIdProperty =
        AvaloniaProperty.RegisterDirect<FeatureCard, string?>(
            nameof(HelpSectionId), o => o.HelpSectionId, (o, v) => o.HelpSectionId = v);

    private string? _iconUri;
    public static readonly DirectProperty<FeatureCard, string?> IconUriProperty =
        AvaloniaProperty.RegisterDirect<FeatureCard, string?>(
            nameof(IconUri), o => o.IconUri, (o, v) => o.IconUri = v);

    public static readonly RoutedEvent<RoutedEventArgs> ClickEvent =
        RoutedEvent.Register<FeatureCard, RoutedEventArgs>(nameof(Click), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<RoutedEventArgs> ToggleRequestedEvent =
        RoutedEvent.Register<FeatureCard, RoutedEventArgs>(nameof(ToggleRequested), RoutingStrategies.Bubble);

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

    public string? Glyph
    {
        get => _glyph;
        set => SetAndRaise(GlyphProperty, ref _glyph, value);
    }

    /// <summary>Required level for this feature. 0 means always unlocked.</summary>
    public int LockLevel
    {
        get => _lockLevel;
        set => SetAndRaise(LockLevelProperty, ref _lockLevel, value);
    }

    public bool IsLocked
    {
        get => _isLocked;
        set => SetAndRaise(IsLockedProperty, ref _isLocked, value);
    }

    /// <summary>
    /// Highlights the card with a pink glow + border when the underlying
    /// feature is enabled in settings.
    /// </summary>
    public bool IsActive
    {
        get => _isActive;
        set => SetAndRaise(IsActiveProperty, ref _isActive, value);
    }

    /// <summary>
    /// ID of the help section whose tooltip should be shown when hovering the "?" icon.
    /// Null/empty IDs hide the icon entirely.
    /// </summary>
    public string? HelpSectionId
    {
        get => _helpSectionId;
        set => SetAndRaise(HelpSectionIdProperty, ref _helpSectionId, value);
    }

    /// <summary>
    /// Optional legacy pack:// or avares:// URI for the card background image.
    /// When set, this is loaded and assigned to <see cref="Icon"/>.
    /// </summary>
    public string? IconUri
    {
        get => _iconUri;
        set => SetAndRaise(IconUriProperty, ref _iconUri, value);
    }

    public event EventHandler<RoutedEventArgs> Click
    {
        add => AddHandler(ClickEvent, value);
        remove => RemoveHandler(ClickEvent, value);
    }

    /// <summary>
    /// Raised on right-click so the dashboard can quick-toggle the underlying
    /// feature on/off without opening its config popup. Left-click still opens
    /// the popup via <see cref="Click"/>.
    /// </summary>
    public event EventHandler<RoutedEventArgs> ToggleRequested
    {
        add => AddHandler(ToggleRequestedEvent, value);
        remove => RemoveHandler(ToggleRequestedEvent, value);
    }

    static FeatureCard()
    {
        TitleProperty.Changed.AddClassHandler<FeatureCard>(OnTitleChanged);
        IconProperty.Changed.AddClassHandler<FeatureCard>(OnIconChanged);
        GlyphProperty.Changed.AddClassHandler<FeatureCard>(OnGlyphChanged);
        LockLevelProperty.Changed.AddClassHandler<FeatureCard>(OnLockStateChanged);
        IsLockedProperty.Changed.AddClassHandler<FeatureCard>(OnLockStateChanged);
        IsActiveProperty.Changed.AddClassHandler<FeatureCard>(OnActiveStateChanged);
        HelpSectionIdProperty.Changed.AddClassHandler<FeatureCard>(OnHelpSectionIdChanged);
        IconUriProperty.Changed.AddClassHandler<FeatureCard>(OnIconUriChanged);
    }

    public FeatureCard()
    {
        InitializeComponent();
        RootBorder.PointerPressed += OnPointerPressed;
        ApplyLockState();
    }

    private static void OnTitleChanged(FeatureCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.TxtTitle.Text = c.Title ?? "";
    }

    private static void OnIconChanged(FeatureCard c, AvaloniaPropertyChangedEventArgs e)
    {
        var src = c.Icon;
        if (src is not null)
        {
            c.ImgIconHost.Background = new ImageBrush(src)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentY = AlignmentY.Center
            };
            c.ImgIconHost.IsVisible = true;
            c.GlyphHost.IsVisible = false;
        }
        else
        {
            c.ImgIconHost.Background = null;
            if (!string.IsNullOrEmpty(c.Glyph))
            {
                c.ImgIconHost.IsVisible = false;
                c.GlyphHost.IsVisible = true;
            }
        }
    }

    private static void OnGlyphChanged(FeatureCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.TxtGlyph.Text = c.Glyph ?? "";
        if (c.Icon is null && !string.IsNullOrEmpty(c.Glyph))
        {
            c.GlyphHost.IsVisible = true;
            c.ImgIconHost.IsVisible = false;
        }
        else if (string.IsNullOrEmpty(c.Glyph))
        {
            c.GlyphHost.IsVisible = false;
        }
    }

    private static void OnLockStateChanged(FeatureCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.ApplyLockState();
    }

    private static void OnActiveStateChanged(FeatureCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.ApplyActiveState();
    }

    private static void OnHelpSectionIdChanged(FeatureCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.RefreshHelpState();
    }

    private static void OnIconUriChanged(FeatureCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.Icon = LoadBitmapFromUri(c.IconUri);
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
                // WPF Resources/ folder is linked into the Avalonia head as Assets/.
                if (relative.StartsWith("Resources/", StringComparison.OrdinalIgnoreCase))
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

    private void RefreshHelpState()
    {
        BtnHelp.IsVisible = !string.IsNullOrWhiteSpace(HelpSectionId);
    }

    private void ApplyLockState()
    {
        if (IsLocked)
        {
            LockedOverlay.IsVisible = true;
            TxtLockLabel.Text = LockLevel > 0
                ? Core.Localization.LocalizationManager.Instance.GetF("label_lvl_0", LockLevel)
                : Core.Localization.LocalizationManager.Instance.Get("label_locked");
            ContentRoot.Opacity = 0.35;
        }
        else
        {
            LockedOverlay.IsVisible = false;
            ContentRoot.Opacity = 1.0;
        }
        ApplyActiveState();
    }

    private void ApplyActiveState()
    {
        // Active state is suppressed while the card is locked — a locked feature
        // can't really be "on" even if the underlying setting is true.
        var showActive = IsActive && !IsLocked;
        ActiveBorder.IsVisible = showActive;
        RootBorder.BoxShadow = showActive
            ? new BoxShadows(new BoxShadow { Blur = 18, Color = Color.Parse("#8CFF69B4") })
            : new BoxShadows(new BoxShadow { Blur = 18, Color = Color.Parse("#00000000") });
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Swallow presses that originate inside the help button so the user
        // can hover/click the "?" without also opening the feature popup.
        if (e.Source is Visual src && IsDescendantOf(src, BtnHelp))
            return;

        var point = e.GetCurrentPoint(this);
        if (point.Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed && !IsLocked)
        {
            e.Handled = true;
            RaiseEvent(new RoutedEventArgs(ToggleRequestedEvent, this));
        }
        else
        {
            RaiseEvent(new RoutedEventArgs(ClickEvent, this));
        }
    }

    private static bool IsDescendantOf(Visual? node, Visual ancestor)
    {
        if (node is null) return false;
        if (ReferenceEquals(node, ancestor)) return true;
        return node.GetVisualAncestors().Any(v => ReferenceEquals(v, ancestor));
    }
}
