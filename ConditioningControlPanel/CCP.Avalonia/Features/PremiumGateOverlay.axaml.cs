using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Localization;

namespace ConditioningControlPanel.Avalonia.Features;

/// <summary>
/// Reusable premium-gate overlay. Covers its parent with a translucent dark layer
/// and a centered card explaining that the feature is locked, with a CTA to unlock.
/// </summary>
public partial class PremiumGateOverlay : UserControl
{
    public static readonly DirectProperty<PremiumGateOverlay, bool> IsLockedProperty =
        AvaloniaProperty.RegisterDirect<PremiumGateOverlay, bool>(
            nameof(IsLocked), o => o.IsLocked, (o, v) => o.IsLocked = v);

    public static readonly DirectProperty<PremiumGateOverlay, string?> HeaderProperty =
        AvaloniaProperty.RegisterDirect<PremiumGateOverlay, string?>(
            nameof(Header), o => o.Header, (o, v) => o.Header = v);

    public static readonly DirectProperty<PremiumGateOverlay, string?> BodyProperty =
        AvaloniaProperty.RegisterDirect<PremiumGateOverlay, string?>(
            nameof(Body), o => o.Body, (o, v) => o.Body = v);

    public static readonly DirectProperty<PremiumGateOverlay, string?> ButtonTextProperty =
        AvaloniaProperty.RegisterDirect<PremiumGateOverlay, string?>(
            nameof(ButtonText), o => o.ButtonText, (o, v) => o.ButtonText = v);

    public static readonly DirectProperty<PremiumGateOverlay, ICommand?> UnlockCommandProperty =
        AvaloniaProperty.RegisterDirect<PremiumGateOverlay, ICommand?>(
            nameof(UnlockCommand), o => o.UnlockCommand, (o, v) => o.UnlockCommand = v);

    private bool _isLocked;
    private string? _header;
    private string? _body;
    private string? _buttonText;
    private ICommand? _unlockCommand;

    public PremiumGateOverlay()
    {
        InitializeComponent();
        Header = Loc.Get("gate_premium_locked");
        Body = Loc.Get("gate_premium_subtitle");
        ButtonText = Loc.Get("gate_unlock_with_patreon");
    }

    /// <summary>When true, the overlay is visible.</summary>
    public bool IsLocked
    {
        get => _isLocked;
        set => SetAndRaise(IsLockedProperty, ref _isLocked, value);
    }

    /// <summary>Headline text in the centered card.</summary>
    public string? Header
    {
        get => _header;
        set => SetAndRaise(HeaderProperty, ref _header, value);
    }

    /// <summary>Body/subtitle text in the centered card.</summary>
    public string? Body
    {
        get => _body;
        set => SetAndRaise(BodyProperty, ref _body, value);
    }

    /// <summary>Text shown on the CTA button.</summary>
    public string? ButtonText
    {
        get => _buttonText;
        set => SetAndRaise(ButtonTextProperty, ref _buttonText, value);
    }

    /// <summary>Command invoked when the CTA button is clicked.</summary>
    public ICommand? UnlockCommand
    {
        get => _unlockCommand;
        set => SetAndRaise(UnlockCommandProperty, ref _unlockCommand, value);
    }

    /// <summary>Event raised when the user clicks the unlock CTA.</summary>
    public event EventHandler? UnlockRequested;

    private void BtnUnlock_Click(object? sender, RoutedEventArgs e)
    {
        UnlockRequested?.Invoke(this, EventArgs.Empty);
        if (UnlockCommand?.CanExecute(null) == true)
            UnlockCommand.Execute(null);
    }
}
