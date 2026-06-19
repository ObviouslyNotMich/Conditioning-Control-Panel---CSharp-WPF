using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel;

/// <summary>
/// The big hero button for an equipped active skill, parked at the bottom-right of the screen
/// for the whole descent — clickable at a glance (Hades-style ability slot). Shows the toy's
/// glyph, its keybind, and its live status (ready / cooldown seconds / charges left). Three
/// visual states: ready = bright with a slow flashing glow, effect running = steady hot glow,
/// cooling/spent = greyed out behind the countdown. One small topmost no-activate window per
/// equipped toy. Clicking it is the same path as the keybind.
/// </summary>
public sealed class ChaosToyButtonWindow : Window
{
    private const double DISC = 138;
    private const double PAD = 14;   // breathing room so the glow doesn't clip at the window edge

    private readonly ChaosToyState _toy;
    private readonly ChaosModeService _chaos;
    private readonly Border _disc;
    private readonly TextBlock _glyph;
    private readonly TextBlock _status;
    private readonly ScaleTransform _press = new(1, 1);
    private readonly System.Windows.Media.Effects.DropShadowEffect _glow;
    private VisualState _vstate = (VisualState)(-1);

    private enum VisualState { Cooling, Ready, Active }

    private static readonly Brush DiscBg = Frozen(Color.FromArgb(0xE6, 0x15, 0x12, 0x26));
    private static readonly Brush ReadyBorder = Frozen(Color.FromRgb(0xFF, 0x69, 0xB4));
    private static readonly Brush ActiveBorder = Frozen(Color.FromRgb(0xFF, 0xC8, 0xE8));
    private static readonly Brush CoolBorder = Frozen(Color.FromArgb(0x55, 0x9A, 0x9A, 0xA8));

    public ChaosToyButtonWindow(ChaosToyState toy, ChaosModeService chaos, int slotIndex)
    {
        _toy = toy;
        _chaos = chaos;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = ChaosWindowZ.BornTopmost;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = DISC + PAD * 2;
        Height = DISC + PAD * 2;
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - 16 - Width - slotIndex * (DISC + 20);
        Top = wa.Bottom - DISC - PAD * 2 - 14;
        Cursor = Cursors.Hand;

        _glyph = new TextBlock
        {
            Text = toy.Glyph, FontSize = 50, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 20),
        };
        _status = new TextBlock
        {
            FontSize = 15, FontWeight = FontWeights.Bold,
            Foreground = Frozen(Color.FromRgb(0xE8, 0xA0, 0xD8)),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 18),
        };
        var keyBadge = new Border
        {
            Background = Frozen(Color.FromArgb(0xDD, 0x12, 0x10, 0x26)),
            BorderBrush = ReadyBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9), Padding = new Thickness(9, 3, 9, 3),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 4, 0),
            Child = new TextBlock
            {
                Text = toy.KeyLabel, FontSize = 15, FontWeight = FontWeights.Bold,
                Foreground = Frozen(Color.FromRgb(0xFF, 0x69, 0xB4)),
            },
        };

        // The glow lives on the disc itself: pulsing while ready ("come press me"), pinned
        // bright while the effect runs, off while cooling. Small window, so the software-
        // rendered effect stays cheap.
        _glow = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Color.FromRgb(0xFF, 0x69, 0xB4),
            BlurRadius = 26, ShadowDepth = 0, Opacity = 0,
        };

        _disc = new Border
        {
            Width = DISC, Height = DISC,
            CornerRadius = new CornerRadius(DISC / 2),
            Background = DiscBg,
            BorderBrush = ReadyBorder, BorderThickness = new Thickness(3.5),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _press,
            Effect = _glow,
            Child = new Grid { Children = { _glyph, _status } },
        };

        Content = new Grid { Children = { _disc, keyBadge } };

        MouseLeftButtonDown += (_, _) => { PressPulse(); _chaos.UseToyById(_toy.Id); };
        _toy.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(new Action(UpdateVisual));
        SourceInitialized += (_, _) => ApplyExStyles();

        ChaosTips.Attach(_disc, $"{toy.Glyph} {toy.Name} · {toy.KeyLabel}", toy.Desc,
            string.IsNullOrEmpty(toy.CapstoneDesc) ? null : "max: " + toy.CapstoneDesc,
            flavor: toy.Flavor);
        UpdateVisual();
    }

    private void UpdateVisual()
    {
        try
        {
            _status.Text = _toy.StatusText;
            var vs = _toy.IsEffectActive ? VisualState.Active
                   : _toy.IsReady ? VisualState.Ready
                   : VisualState.Cooling;
            if (vs == _vstate) return;   // animations only restart on a real state change
            _vstate = vs;

            _glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, null);
            switch (vs)
            {
                case VisualState.Active:
                    // Steady hot glow for the whole effect window.
                    _glow.Color = Color.FromRgb(0xFF, 0xC8, 0xE8);
                    _glow.BlurRadius = 34;
                    _glow.Opacity = 1.0;
                    _disc.Opacity = 1.0;
                    _disc.BorderBrush = ActiveBorder;
                    _glyph.Opacity = 1.0;
                    break;

                case VisualState.Ready:
                    // Bright + a slow flashing glow: "this is pressable right now".
                    _glow.Color = Color.FromRgb(0xFF, 0x69, 0xB4);
                    _glow.BlurRadius = 26;
                    _disc.Opacity = 1.0;
                    _disc.BorderBrush = ReadyBorder;
                    _glyph.Opacity = 1.0;
                    var flash = new DoubleAnimation(0.25, 0.95, TimeSpan.FromMilliseconds(950))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                    };
                    _glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, flash);
                    break;

                default:   // Cooling / spent: greyed out behind the countdown.
                    _glow.Opacity = 0;
                    _disc.Opacity = 0.45;
                    _disc.BorderBrush = CoolBorder;
                    _glyph.Opacity = 0.55;
                    break;
            }
        }
        catch { }
    }

    private void PressPulse()
    {
        var anim = new DoubleAnimation(0.86, 1.0, TimeSpan.FromMilliseconds(180))
        { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 } };
        _press.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        _press.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    /// <summary>Keep the button clickable above a mid-run mandatory video (same kick as the HUD).</summary>
    public void RaiseToTopmost() => ChaosWindowZ.RaiseTopmost(this);   // demotes in Free Desktop mode

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        if (b.CanFreeze) b.Freeze();
        return b;
    }

    // Clickable but never focus-stealing (no WS_EX_TRANSPARENT — it must take the click).
    private void ApplyExStyles()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);
        }
        catch { }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
