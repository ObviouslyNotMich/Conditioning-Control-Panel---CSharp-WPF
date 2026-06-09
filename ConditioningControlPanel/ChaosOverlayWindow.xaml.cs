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
        public Border Art = null!;                 // artwork square (placeholder until per-boon art ships)
        public SolidColorBrush ArtBorder = null!;  // its thick border brush — flashed on pick
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

    private DispatcherTimer? _countdownTimer;
    private Action? _countdownComplete;
    private bool _countdownFinished;
    private Services.GlobalKeyboardHook? _countdownKeyHook;
    private IntPtr _countdownMouseHook = IntPtr.Zero;
    private CountdownMouseProc? _countdownMouseProc;

    /// <summary>Show the GO countdown. <paramref name="shortFlash"/> uses a single 1s "GO!" flash
    /// (RunAgain); otherwise the full 3·2·1·GO. Skippable on click/keypress in both cases.</summary>
    public void ShowCountdown(Action onComplete, bool shortFlash = false)
    {
        string[] steps = shortFlash ? new[] { "GO!" } : new[] { "3", "2", "1", "GO!" };
        int interval = shortFlash ? ChaosModeService.ChaosRestartCountdownMs : 750;
        ShowCountdownSteps(steps, interval, onComplete);
    }

    /// <summary>A short "Ready? :3" → "GO!" beat after a mantra pick, using the same flashing
    /// countdown display as the run start, so play resumes with a moment to settle. Skippable.</summary>
    public void ShowReadyGo(Action onComplete)
        => ShowCountdownSteps(new[] { "Ready? :3", "GO!" }, 800, onComplete);

    private void ShowCountdownSteps(string[] steps, int interval, Action onComplete)
    {
        SetClickThrough(true);
        Backdrop.Visibility = Visibility.Collapsed;
        DraftPanel.Visibility = Visibility.Collapsed;
        ResultsPanel.Visibility = Visibility.Collapsed;
        CountdownBox.Visibility = Visibility.Visible;

        _countdownComplete = onComplete;
        _countdownFinished = false;

        int i = 0;
        ShowCountdownStep(steps[0]);

        _countdownTimer?.Stop();
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(interval) };
        _countdownTimer.Tick += (_, _) =>
        {
            i++;
            if (i < steps.Length) ShowCountdownStep(steps[i]);
            else FinishCountdown();
        };
        _countdownTimer.Start();
        InstallCountdownSkipHooks();
    }

    /// <summary>Complete the countdown immediately (timer end, or a skip click/keypress).</summary>
    private void FinishCountdown()
    {
        if (_countdownFinished) return;
        _countdownFinished = true;
        _countdownTimer?.Stop();
        _countdownTimer = null;
        RemoveCountdownSkipHooks();
        CountdownBox.Visibility = Visibility.Collapsed;
        var cb = _countdownComplete;
        _countdownComplete = null;
        cb?.Invoke();
    }

    // The countdown overlay is click-through (so the desktop stays usable), so it can't
    // receive WPF input itself. Use brief low-level keyboard + mouse hooks (torn down the
    // instant the countdown ends) so any click/keypress skips straight to GO.
    private void InstallCountdownSkipHooks()
    {
        try
        {
            _countdownKeyHook = new Services.GlobalKeyboardHook();
            _countdownKeyHook.KeyPressed += OnCountdownKey;
            _countdownKeyHook.Start();
        }
        catch { }
        try
        {
            _countdownMouseProc = CountdownMouseHook;
            using var proc = System.Diagnostics.Process.GetCurrentProcess();
            using var mod = proc.MainModule!;
            _countdownMouseHook = SetWindowsHookEx(WH_MOUSE_LL, _countdownMouseProc, GetModuleHandle(mod.ModuleName), 0);
        }
        catch { }
    }

    private void RemoveCountdownSkipHooks()
    {
        try { if (_countdownKeyHook != null) { _countdownKeyHook.KeyPressed -= OnCountdownKey; _countdownKeyHook.Dispose(); _countdownKeyHook = null; } } catch { }
        try { if (_countdownMouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_countdownMouseHook); _countdownMouseHook = IntPtr.Zero; } } catch { }
        _countdownMouseProc = null;
    }

    private void OnCountdownKey(System.Windows.Input.Key _)
    {
        // Marshal to the UI thread; the hook callback runs on the message-pump thread.
        try { Dispatcher.BeginInvoke(new Action(FinishCountdown)); } catch { }
    }

    private IntPtr CountdownMouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_LBUTTONDOWN || wParam == (IntPtr)WM_RBUTTONDOWN))
        {
            try { Dispatcher.BeginInvoke(new Action(FinishCountdown)); } catch { }
        }
        return CallNextHookEx(_countdownMouseHook, nCode, wParam, lParam);
    }

    private delegate IntPtr CountdownMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, CountdownMouseProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

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

    private DispatcherTimer? _autoResumeTimer;
    private int _autoResumeRemainingSec;

    public void ShowBoonDraft(int waveJustCleared, List<ChaosBoon> options, Action<ChaosBoon?> onPick, int autoResumeSec = 0)
    {
        _onBoonPick = onPick;
        _selectedBoon = null;
        _selectionMade = false;
        _autoResumeTimer?.Stop();
        _autoResumeTimer = null;
        _autoResumeRemainingSec = autoResumeSec;
        SetClickThrough(false);
        CountdownBox.Visibility = Visibility.Collapsed;
        ResultsPanel.Visibility = Visibility.Collapsed;
        Backdrop.Visibility = Visibility.Visible;
        DraftPanel.Visibility = Visibility.Visible;
        BringToFront();

        DraftTitle.Text = $"LOOP {waveJustCleared} CLEARED · CHOOSE A MANTRA";
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
                if (!_selectionMade)
                {
                    if (_autoResumeRemainingSec > 0) StartAutoResume();
                    else DraftCountdown.Text = "field frozen — take your time";
                }
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
        _autoResumeTimer?.Stop();
        _autoResumeTimer = null;
        _draftCards.Clear();
        _selectedBoon = null;
        _selectionMade = false;
        DraftPanel.Visibility = Visibility.Collapsed;
        Backdrop.Visibility = Visibility.Collapsed;
        SetClickThrough(true);
    }

    /// <summary>Auto-resume: an untouched draft ticks down, then auto-takes the SKIP (+1 shield) and
    /// resumes the run so an unattended run never freezes forever. Any pick cancels it (see SelectBoon).</summary>
    private void StartAutoResume()
    {
        DraftCountdown.Text = $"auto-skip in {_autoResumeRemainingSec}s — pick to keep playing";
        _autoResumeTimer?.Stop();
        _autoResumeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _autoResumeTimer.Tick += (_, _) =>
        {
            _autoResumeRemainingSec--;
            if (_selectionMade) { _autoResumeTimer?.Stop(); return; }
            if (_autoResumeRemainingSec <= 0)
            {
                _autoResumeTimer?.Stop();
                ChooseBoon(null);   // auto-skip → +1 shield + ChaosBoonSkipped fired by the service
                return;
            }
            DraftCountdown.Text = $"auto-skip in {_autoResumeRemainingSec}s — pick to keep playing";
        };
        _autoResumeTimer.Start();
    }

    private DraftCard BuildBoonCard(ChaosBoon boon)
    {
        var accent = boon.IsCurse ? Color.FromRgb(255, 120, 120) : Color.FromRgb(156, 232, 160);
        var accentBrush = new SolidColorBrush(accent);

        var panel = new StackPanel { Width = 190 };

        // Artwork square on top of the card — a thick accent border (flashed on pick) around the
        // boon's art. Real art at assets/Chaos/boons/{id}.png is used when present; until then a
        // placeholder (dark fill + the boon's glyph) stands in.
        var artBorderBrush = new SolidColorBrush(accent);
        var artFill = Services.Chaos.ChaosArt.Resolve("boons", boon.Id);
        var art = new Border
        {
            Width = 190, Height = 190,
            BorderBrush = artBorderBrush, BorderThickness = new Thickness(4),
            CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 10),
            RenderTransformOrigin = new Point(0.5, 0.5),
            Background = artFill != null
                ? new ImageBrush(artFill) { Stretch = Stretch.UniformToFill }
                : new SolidColorBrush(Color.FromArgb(48, 0, 0, 0)),
        };
        if (artFill == null)
            art.Child = new TextBlock
            {
                Text = boon.IsCurse ? "☠" : "◈", FontSize = 70,
                Foreground = new SolidColorBrush(Color.FromArgb(90, accent.R, accent.G, accent.B)),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
        panel.Children.Add(art);

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

        var dc = new DraftCard { Card = card, Pick = pick, Scale = scale, Boon = boon, Art = art, ArtBorder = artBorderBrush };
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
        _autoResumeTimer?.Stop();
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

        // Flash the chosen art-square's thick border: pulse its colour bright↔accent on a loop, and
        // give the square a matching glow. The loop tears down with the draft a moment later.
        chosen.Art.BorderThickness = new Thickness(5);
        chosen.Art.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = hi, BlurRadius = 24, ShadowDepth = 0, Opacity = 0.95 };
        var flash = new ColorAnimation
        {
            From = chosen.ArtBorder.Color, To = hi,
            Duration = TimeSpan.FromMilliseconds(240),
            AutoReverse = true,
            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
        };
        chosen.ArtBorder.BeginAnimation(SolidColorBrush.ColorProperty, flash);

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

    public void ShowResults(ChaosRunState s, double baseXp, double skillMult, double finalXp, long previousBest)
    {
        SetClickThrough(false);
        CountdownBox.Visibility = Visibility.Collapsed;
        DraftPanel.Visibility = Visibility.Collapsed;
        Backdrop.Visibility = Visibility.Visible;
        ResultsPanel.Visibility = Visibility.Visible;
        BringToFront();

        // PB / delta-vs-best (best already updated by AwardRunRewards; compare run score to the prior best).
        double score = s.Score;
        double pbDelta = score - previousBest;
        bool isPb = score > previousBest;

        ResultsBody.Children.Clear();
        AddResultLine($"Reached DEPTH {Roman(s.ActIndex)} · L{s.WaveIndex}    Best streak x{s.BestCombo}    Survived {(int)s.ElapsedSec / 60:00}:{(int)s.ElapsedSec % 60:00}", 14, Brushes.White, FontWeights.SemiBold);
        AddResultLine($"snapped {s.Defused} · triggered {s.Detonated} · effects fired {s.EffectsFired}", 13, new SolidColorBrush(Color.FromRgb(180, 180, 208)), FontWeights.Normal);
        AddResultLine($"score {score:N0}", 14, Brushes.White, FontWeights.SemiBold);
        // Compulsion hook: a personal-best / delta line.
        if (isPb)
            AddResultLine($"★ NEW BEST  (+{pbDelta:N0} over {previousBest:N0})", 14, new SolidColorBrush(Color.FromRgb(255, 215, 90)), FontWeights.Bold);
        else
            AddResultLine($"best {previousBest:N0}   ({pbDelta:N0} vs best)", 13, new SolidColorBrush(Color.FromRgb(180, 180, 208)), FontWeights.Normal);
        ResultsBody.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(85, 255, 105, 180)), Margin = new Thickness(0, 12, 0, 12) });
        AddResultLine($"base {baseXp:N0}  ×  skill x{skillMult:0.0}", 14, Brushes.White, FontWeights.Normal);
        AddResultLine($"TOTAL  {finalXp:N0} XP ✦", 22, new SolidColorBrush(Color.FromRgb(255, 105, 180)), FontWeights.Bold);

        // Bark over the results (+ PB fields for the compulsion line).
        App.Bark?.NotifyChaosResultsShown(score, ChaosMeta.State.BestScore, pbDelta, isPb,
            s.Defused, s.Detonated, s.BestCombo, s.Config.Difficulty.ToString());
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
        try { RemoveCountdownSkipHooks(); } catch { }
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
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
}
