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
    private DispatcherTimer? _draftCountdown;
    private int _draftSecsLeft;
    private bool _clickThrough = true;

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
        SetClickThrough(false);
        CountdownBox.Visibility = Visibility.Collapsed;
        ResultsPanel.Visibility = Visibility.Collapsed;
        Backdrop.Visibility = Visibility.Visible;
        DraftPanel.Visibility = Visibility.Visible;
        BringToFront();

        DraftTitle.Text = $"WAVE {waveJustCleared} CLEARED · CHOOSE A BOON";
        BoonCardHost.Children.Clear();
        foreach (var boon in options)
            BoonCardHost.Children.Add(BuildBoonCard(boon));

        _draftSecsLeft = 12;
        DraftCountdown.Text = $"auto-skip 0:{_draftSecsLeft:00}";
        _draftCountdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _draftCountdown.Tick += (_, _) =>
        {
            _draftSecsLeft--;
            DraftCountdown.Text = $"auto-skip 0:{Math.Max(0, _draftSecsLeft):00}";
            if (_draftSecsLeft <= 0) ChooseBoon(null);
        };
        _draftCountdown.Start();
    }

    private void HideDraft()
    {
        _draftCountdown?.Stop();
        _draftCountdown = null;
        DraftPanel.Visibility = Visibility.Collapsed;
        Backdrop.Visibility = Visibility.Collapsed;
        SetClickThrough(true);
    }

    private Border BuildBoonCard(ChaosBoon boon)
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
        var pick = new Button
        {
            Content = boon.IsCurse ? "RISK" : "PICK",
            Padding = new Thickness(0, 8, 0, 8), Background = accentBrush, Foreground = Brushes.Black,
            FontWeight = FontWeights.Bold, BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        pick.Click += (_, _) => ChooseBoon(boon);
        panel.Children.Add(pick);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            BorderBrush = accentBrush, BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(16),
            Margin = new Thickness(8), Child = panel
        };
    }

    private static string RarityDots(ChaosRarity r) => r switch
    {
        ChaosRarity.Common => "◆",
        ChaosRarity.Uncommon => "◆◆",
        ChaosRarity.Rare => "◆◆◆",
        _ => "◆"
    };

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
