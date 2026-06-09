using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel;

/// <summary>
/// Centered full-screen topmost overlay used by Chaos Mode for three transient
/// moments: the 3·2·1·GO countdown (click-through, desktop stays usable), the
/// between-waves boon draft, and the end-of-run results (both interactive with a
/// dim backdrop). Bubbles + HUD live in their own windows; this is only shown
/// when one of these modes is active.
/// </summary>
public partial class ChaosOverlayWindow : Window
{
    private Action<ChaosBoon?>? _onBoonPick;
    private bool _clickThrough = true;

    // ---- boon draft reveal/selection state ----
    private sealed class DraftCard
    {
        public Border Card = null!;
        public Button Pick = null!;
        public ScaleTransform Scale = null!;
        public ChaosBoon Boon = null!;
    }
    private readonly System.Collections.Generic.List<DraftCard> _draftCards = new();
    private DispatcherTimer? _revealTimer;
    private int _revealIndex;
    private ChaosBoon? _selectedBoon;
    private bool _selectionMade;

    public Action? OnRunAgain;
    public Action? OnDismissed;

    public ChaosOverlayWindow()
    {
        InitializeComponent();
        Left = 0; Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
        SourceInitialized += (_, _) => ApplyExStyles();
    }

    // ============================ countdown ============================

    public void ShowCountdown(Action onComplete)
    {
        SetClickThrough(true);
        Backdrop.Visibility = Visibility.Collapsed;
        DraftPanel.Visibility = Visibility.Collapsed;
        ResultsPanel.Visibility = Visibility.Collapsed;
        CountdownBox.Visibility = Visibility.Visible;

        string[] steps = { "3", "2", "1", "GO!" };
        int i = 0;
        ShowCountdownStep(steps[0]);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        timer.Tick += (_, _) =>
        {
            i++;
            if (i < steps.Length)
            {
                ShowCountdownStep(steps[i]);
            }
            else
            {
                timer.Stop();
                CountdownBox.Visibility = Visibility.Collapsed;
                onComplete();
            }
        };
        timer.Start();
    }

    private void ShowCountdownStep(string text)
    {
        CountdownText.Text = text;
        CountdownText.Foreground = text == "GO!" ? new SolidColorBrush(Color.FromRgb(120, 255, 160)) : Brushes.White;
        var pop = new DoubleAnimation(1.5, 1.0, TimeSpan.FromMilliseconds(350)) { EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut } };
        var fade = new DoubleAnimation(0.2, 1.0, TimeSpan.FromMilliseconds(200));
        CountdownScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        CountdownScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        CountdownBox.BeginAnimation(OpacityProperty, fade);
    }

    // ============================ boon draft ============================

    public void ShowBoonDraft(int waveJustCleared, List<ChaosBoon> options, Action<ChaosBoon?> onPick)
    {
        _onBoonPick = onPick;
        _selectedBoon = null;
        _selectionMade = false;
        SetClickThrough(false);
        CountdownBox.Visibility = Visibility.Collapsed;
        ResultsPanel.Visibility = Visibility.Collapsed;
        Backdrop.Visibility = Visibility.Visible;
        DraftPanel.Visibility = Visibility.Visible;
        BringToFront();

        DraftTitle.Text = $"WAVE {waveJustCleared} CLEARED · CHOOSE A BOON";
        DraftCountdown.Text = "";
        BtnSkipBoon.Visibility = Visibility.Visible;
        BtnContinue.Visibility = Visibility.Collapsed;
        BtnContinue.Opacity = 0;

        // Build every card hidden, then reveal them one at a time (each with a per-rarity cue:
        // a bright "dling" for rare, a dull "thud" otherwise). Picks are disabled until revealed.
        BoonCardHost.Children.Clear();
        _draftCards.Clear();
        foreach (var boon in options)
        {
            var dc = BuildBoonCard(boon);
            dc.Card.Opacity = 0;
            dc.Scale.ScaleX = dc.Scale.ScaleY = 0.7;
            dc.Pick.IsEnabled = false;
            _draftCards.Add(dc);
            BoonCardHost.Children.Add(dc.Card);
        }

        _revealTimer?.Stop();
        _revealTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _revealTimer.Tick += (_, _) =>
        {
            if (_revealIndex >= _draftCards.Count)
            {
                _revealTimer?.Stop();
                if (!_selectionMade) DraftCountdown.Text = "field frozen — take your time";
                return;
            }
            RevealCard(_draftCards[_revealIndex]);
            _revealIndex++;
        };
        // First card lands immediately; the rest follow on the timer.
        _revealIndex = 0;
        if (_draftCards.Count > 0) { RevealCard(_draftCards[0]); _revealIndex = 1; }
        _revealTimer.Start();
    }

    private void RevealCard(DraftCard dc)
    {
        dc.Pick.IsEnabled = true;
        dc.Card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        var pop = new DoubleAnimation(0.7, 1.0, TimeSpan.FromMilliseconds(300))
        { EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut } };
        dc.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        dc.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        ChaosSfx.PlayBoonReveal(dc.Boon.Rarity == ChaosRarity.Rare);
    }

    private void HideDraft()
    {
        _revealTimer?.Stop();
        _revealTimer = null;
        _draftCards.Clear();
        _selectedBoon = null;
        _selectionMade = false;
        DraftPanel.Visibility = Visibility.Collapsed;
        Backdrop.Visibility = Visibility.Collapsed;
        SetClickThrough(true);
    }

    private DraftCard BuildBoonCard(ChaosBoon boon)
    {
        var accent = boon.IsCurse ? Color.FromRgb(255, 120, 120) : Color.FromRgb(156, 232, 160);
        var accentBrush = new SolidColorBrush(accent);

        var panel = new StackPanel { Width = 190 };
        panel.Children.Add(new TextBlock
        {
            Text = (boon.IsCurse ? "☠ " : "◈ ") + boon.Name.ToUpperInvariant(),
            Foreground = accentBrush, FontWeight = FontWeights.Bold, FontSize = 14,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        });
        panel.Children.Add(new TextBlock
        {
            Text = boon.Desc, Foreground = Brushes.White, FontSize = 12,
            TextWrapping = TextWrapping.Wrap, Height = 64, Margin = new Thickness(0, 0, 0, 8)
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"{RarityDots(boon.Rarity)} {boon.Rarity}",
            Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 208)), FontSize = 11,
            Margin = new Thickness(0, 0, 0, 10)
        });
        var pickScale = new ScaleTransform(1, 1);
        var pick = new Button
        {
            Content = boon.IsCurse ? "RISK" : "PICK",
            Padding = new Thickness(0, 8, 0, 8), Background = accentBrush, Foreground = Brushes.Black,
            FontWeight = FontWeights.Bold, BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = pickScale
        };
        panel.Children.Add(pick);

        var scale = new ScaleTransform(1, 1);
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            BorderBrush = accentBrush, BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(16),
            Margin = new Thickness(8), Child = panel,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale
        };

        var dc = new DraftCard { Card = card, Pick = pick, Scale = scale, Boon = boon };
        pick.Click += (_, _) => SelectBoon(dc);
        return dc;
    }

    private static string RarityDots(ChaosRarity r) => r switch
    {
        ChaosRarity.Common => "◆",
        ChaosRarity.Uncommon => "◆◆",
        ChaosRarity.Rare => "◆◆◆",
        _ => "◆"
    };

    /// <summary>A card was picked: dissolve the others, highlight + bounce this one, reveal Continue.</summary>
    private void SelectBoon(DraftCard chosen)
    {
        if (_selectionMade) return;
        _selectionMade = true;
        _selectedBoon = chosen.Boon;
        _revealTimer?.Stop();
        ChaosSfx.PlayBoonPicked();

        foreach (var dc in _draftCards)
        {
            dc.Pick.IsEnabled = false;
            if (dc == chosen) continue;
            // Dissolve the unchosen cards (already-invisible unrevealed ones just stay gone).
            dc.Card.BeginAnimation(OpacityProperty, new DoubleAnimation(dc.Card.Opacity, 0.0, TimeSpan.FromMilliseconds(260)));
            var shrink = new DoubleAnimation(0.8, TimeSpan.FromMilliseconds(260)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            dc.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
            dc.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
        }

        // Snap the chosen card to fully shown, then brighten its border + add a glow.
        chosen.Card.BeginAnimation(OpacityProperty, null);
        chosen.Card.Opacity = 1;
        chosen.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        chosen.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        chosen.Scale.ScaleX = chosen.Scale.ScaleY = 1;
        var hi = chosen.Boon.IsCurse ? Color.FromRgb(255, 150, 150) : Color.FromRgb(120, 255, 170);
        chosen.Card.BorderBrush = new SolidColorBrush(hi);
        chosen.Card.BorderThickness = new Thickness(3);
        chosen.Card.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = hi, BlurRadius = 28, ShadowDepth = 0, Opacity = 0.9 };

        // Slight bounce on the chosen button.
        if (chosen.Pick.RenderTransform is ScaleTransform ps)
        {
            var bounce = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(360)) { From = 1.15, EasingFunction = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut } };
            ps.BeginAnimation(ScaleTransform.ScaleXProperty, bounce);
            ps.BeginAnimation(ScaleTransform.ScaleYProperty, bounce);
        }
        chosen.Pick.Content = "✓ CHOSEN";

        // Swap skip → Continue.
        BtnSkipBoon.Visibility = Visibility.Collapsed;
        DraftCountdown.Text = "";
        BtnContinue.Visibility = Visibility.Visible;
        BtnContinue.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
    }

    private void BtnContinue_Click(object sender, RoutedEventArgs e) => ChooseBoon(_selectedBoon);

    private void ChooseBoon(ChaosBoon? boon)
    {
        var cb = _onBoonPick;
        _onBoonPick = null;
        if (cb == null) return;
        HideDraft();
        cb(boon);
    }

    private void BtnSkipBoon_Click(object sender, RoutedEventArgs e) => ChooseBoon(null);

    // ============================ results ============================

    public void ShowResults(ChaosRunState s, double baseXp, double skillMult, double finalXp)
    {
        SetClickThrough(false);
        CountdownBox.Visibility = Visibility.Collapsed;
        DraftPanel.Visibility = Visibility.Collapsed;
        Backdrop.Visibility = Visibility.Visible;
        ResultsPanel.Visibility = Visibility.Visible;
        BringToFront();

        ResultsBody.Children.Clear();
        AddResultLine($"Reached ACT {Roman(s.ActIndex)} · W{s.WaveIndex}    Best combo x{s.BestCombo}    Survived {(int)s.ElapsedSec / 60:00}:{(int)s.ElapsedSec % 60:00}", 14, Brushes.White, FontWeights.SemiBold);
        AddResultLine($"defused {s.Defused} · detonated {s.Detonated} · effects fired {s.EffectsFired}", 13, new SolidColorBrush(Color.FromRgb(180, 180, 208)), FontWeights.Normal);
        ResultsBody.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(85, 255, 105, 180)), Margin = new Thickness(0, 12, 0, 12) });
        AddResultLine($"base {baseXp:N0}  ×  skill x{skillMult:0.0}", 14, Brushes.White, FontWeights.Normal);
        AddResultLine($"TOTAL  {finalXp:N0} XP ✦", 22, new SolidColorBrush(Color.FromRgb(255, 105, 180)), FontWeights.Bold);
    }

    private void AddResultLine(string text, double size, Brush color, FontWeight weight)
    {
        ResultsBody.Children.Add(new TextBlock
        {
            Text = text, FontSize = size, Foreground = color, FontWeight = weight,
            HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 3, 0, 3), TextWrapping = TextWrapping.Wrap
        });
    }

    private static string Roman(int n) => n switch
    {
        <= 1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", _ => n.ToString()
    };

    private void BtnRunAgain_Click(object sender, RoutedEventArgs e) { OnRunAgain?.Invoke(); }
    private void BtnClose_Click(object sender, RoutedEventArgs e) { Close(); }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        OnDismissed?.Invoke();
    }

    // ============================ click-through ============================

    private void SetClickThrough(bool on)
    {
        _clickThrough = on;
        ApplyExStyles();
    }

    private void ApplyExStyles()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW;
            if (_clickThrough)
                ex |= WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;   // play/countdown: pass clicks to desktop
            else
                ex &= ~(WS_EX_TRANSPARENT | WS_EX_NOACTIVATE); // draft/results: interactive + focusable
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);
        }
        catch { }
    }

    /// <summary>Re-assert top of the topmost band so the draft/results sit above any
    /// payload window (flash/overlay/video) that fired just before a wave boundary.</summary>
    private void BringToFront()
    {
        try { Topmost = false; Topmost = true; Activate(); Focus(); } catch { }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
}
