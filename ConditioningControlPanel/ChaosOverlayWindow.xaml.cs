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
        string[] steps = shortFlash ? new[] { "SINK" } : new[] { "3", "2", "1", "SINK" };
        int interval = shortFlash ? ChaosModeService.ChaosRestartCountdownMs : 750;
        ShowCountdownSteps(steps, interval, onComplete);
    }

    /// <summary>A short "Ready? :3" → "GO!" beat after a mantra pick, using the same flashing
    /// countdown display as the run start, so play resumes with a moment to settle. Skippable.</summary>
    public void ShowReadyGo(Action onComplete)
        => ShowCountdownSteps(new[] { "ready? :3", "SINK" }, 800, onComplete);

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
        ChaosSfx.Play(text == "SINK" ? "sink" : "countdown_tick", text == "SINK" ? 0.6f : 0.45f);
        CountdownText.Text = text;
        CountdownText.Foreground = text == "SINK" ? new SolidColorBrush(Color.FromRgb(120, 255, 160)) : Brushes.White;
        var pop = new DoubleAnimation(1.5, 1.0, TimeSpan.FromMilliseconds(350)) { EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut } };
        var fade = new DoubleAnimation(0.2, 1.0, TimeSpan.FromMilliseconds(200));
        CountdownScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        CountdownScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        CountdownBox.BeginAnimation(OpacityProperty, fade);
    }

    // ============================ boon draft ============================

    private DispatcherTimer? _autoResumeTimer;
    private DispatcherTimer? _confirmTimer;     // post-pick beat before the draft commits itself
    private int _autoResumeRemainingSec;
    private int _draftWave;
    private Func<(List<ChaosBoon> options, int rerollsLeft)?>? _rerollFunc;   // Taking Chances
    /// <summary>Skip reveal state captured per deal: true = timeout skips (+1 resistance),
    /// false = the table has no skip and a timeout autopicks a card.</summary>
    private bool _skipAllowed = true;

    public void ShowBoonDraft(int waveJustCleared, List<ChaosBoon> options, Action<ChaosBoon?> onPick, int autoResumeSec = 0,
                              int rerollsLeft = 0, Func<(List<ChaosBoon> options, int rerollsLeft)?>? onReroll = null)
    {
        _onBoonPick = onPick;
        _selectedBoon = null;
        _selectionMade = false;
        _draftWave = waveJustCleared;
        _rerollFunc = onReroll;
        BtnReroll.Visibility = rerollsLeft > 0 && onReroll != null ? Visibility.Visible : Visibility.Collapsed;
        BtnReroll.Content = rerollsLeft > 1 ? $"🎲 tempt fate again ({rerollsLeft} left)" : "🎲 tempt fate again";
        _autoResumeTimer?.Stop();
        _autoResumeTimer = null;
        _confirmTimer?.Stop();
        _confirmTimer = null;
        _autoResumeRemainingSec = autoResumeSec;
        SetClickThrough(false);
        CountdownBox.Visibility = Visibility.Collapsed;
        ResultsPanel.Visibility = Visibility.Collapsed;
        Backdrop.Visibility = Visibility.Visible;
        DraftPanel.Visibility = Visibility.Visible;
        BringToFront();

        ChaosSfx.Play("cards_in", 0.5f);   // the fan whoosh under the per-card reveals

        bool hasSin = options.Exists(o => o.IsCurse);
        DraftTitle.Text = hasSin
            ? $"LOOP {waveJustCleared} CLEARED · CHOOSE A MANTRA... OR DON'T"
            : $"LOOP {waveJustCleared} CLEARED · CHOOSE A MANTRA";
        DraftCountdown.Text = "";
        // Happy path: the skip affordance stays hidden until its reveal flips (run 3).
        // Before that an untouched draft AUTOPICKS instead of skipping (see StartAutoResume).
        _skipAllowed = RevealService.IsUnlocked(RevealIds.DraftSkip);
        BtnSkipBoon.Visibility = _skipAllowed ? Visibility.Visible : Visibility.Collapsed;
        if (_skipAllowed && !ChaosMeta.State.SeenSkipDebut)
        {
            ChaosMeta.State.SeenSkipDebut = true;
            ChaosMeta.Save();
            ChaosAnnouncerOverlay.Announce("you're allowed to refuse now.", ChaosAnnounceKind.Willpower);
        }

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
                    else DraftCountdown.Text = "the field holds. take your time.";
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
        if (dc.Boon.IsCurse) ChaosSfx.Play("sin_reveal", 0.55f);   // a sin lands with its own drone
        else ChaosSfx.PlayBoonReveal(dc.Boon.Rarity == ChaosRarity.Rare);
    }

    private void HideDraft()
    {
        _revealTimer?.Stop();
        _revealTimer = null;
        _autoResumeTimer?.Stop();
        _autoResumeTimer = null;
        _confirmTimer?.Stop();
        _confirmTimer = null;
        _draftCards.Clear();
        _selectedBoon = null;
        _selectionMade = false;
        _rerollFunc = null;
        BtnReroll.Visibility = Visibility.Collapsed;
        DraftPanel.Visibility = Visibility.Collapsed;
        Backdrop.Visibility = Visibility.Collapsed;
        SetClickThrough(true);
    }

    /// <summary>Taking Chances: spend a reroll and re-deal the table (no-op once a pick is made).</summary>
    private void BtnReroll_Click(object sender, RoutedEventArgs e)
    {
        if (_selectionMade) return;
        var pick = _onBoonPick;
        var result = _rerollFunc?.Invoke();
        if (pick == null || result == null) { BtnReroll.Visibility = Visibility.Collapsed; return; }
        ChaosSfx.PlayBoonReveal(true);
        ShowBoonDraft(_draftWave, result.Value.options, pick, _autoResumeRemainingSec,
                      result.Value.rerollsLeft, _rerollFunc);
    }

    /// <summary>Auto-resume: an untouched draft ticks down, then resolves itself so an
    /// unattended run never freezes forever. With the skip revealed it auto-takes the SKIP
    /// (+1 shield); before that (runs 1 and 2) it AUTOPICKS a random card — the hole chooses.
    /// Any pick cancels it (see SelectBoon).</summary>
    private void StartAutoResume()
    {
        string CountText() => _skipAllowed
            ? $"auto-resist in {_autoResumeRemainingSec}s. pick to keep playing"
            : $"it chooses in {_autoResumeRemainingSec}s. pick first if you'd rather";
        DraftCountdown.Text = CountText();
        _autoResumeTimer?.Stop();
        _autoResumeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _autoResumeTimer.Tick += (_, _) =>
        {
            _autoResumeRemainingSec--;
            if (_selectionMade) { _autoResumeTimer?.Stop(); return; }
            if (_autoResumeRemainingSec <= 0)
            {
                _autoResumeTimer?.Stop();
                if (_skipAllowed) ChooseBoon(null);   // auto-skip → +1 shield + ChaosBoonSkipped fired by the service
                else AutopickRandom();                 // no skip yet: it chose for you
                return;
            }
            DraftCountdown.Text = CountText();
        };
        _autoResumeTimer.Start();
    }

    /// <summary>The timed-out, skipless table picks for the player: a random revealed card
    /// runs the normal pick beat (glow + commit), with its own bark.</summary>
    private void AutopickRandom()
    {
        if (_selectionMade || _draftCards.Count == 0) return;
        var revealed = _draftCards.FindAll(dc => dc.Pick.IsEnabled);
        var pool = revealed.Count > 0 ? revealed : _draftCards;
        var chosen = pool[Random.Shared.Next(pool.Count)];
        try { App.Bark?.NotifyChaosDraftAutopick(); } catch { }
        SelectBoon(chosen);
    }

    private DraftCard BuildBoonCard(ChaosBoon boon)
    {
        // Sins red, synergy duos gold (their partner gear is equipped), plain mantras green.
        var accent = boon.IsCurse ? Color.FromRgb(255, 120, 120)
                   : boon.RequiresAny != null || boon.RequiresAll != null ? Color.FromRgb(255, 215, 0)
                   : Color.FromRgb(156, 232, 160);
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
        Services.Chaos.ChaosTips.Attach(art, boon.Name, boon.Desc, accent: accent);
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
            Content = boon.IsCurse ? "GIVE IN" : "ACCEPT",
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
        // The art square picks too — same gating as the button (disabled until revealed).
        art.Cursor = System.Windows.Input.Cursors.Hand;
        art.MouseLeftButtonUp += (_, _) => { if (dc.Pick.IsEnabled) SelectBoon(dc); };
        return dc;
    }

    private static string RarityDots(ChaosRarity r) => r switch
    {
        ChaosRarity.Common => "◆",
        ChaosRarity.Uncommon => "◆◆",
        ChaosRarity.Rare => "◆◆◆",
        _ => "◆"
    };

    /// <summary>A card was picked: dissolve the others, highlight + bounce this one, then auto-commit after a beat.</summary>
    private void SelectBoon(DraftCard chosen)
    {
        if (_selectionMade) return;
        _selectionMade = true;
        _selectedBoon = chosen.Boon;
        _revealTimer?.Stop();
        _autoResumeTimer?.Stop();
        // Mantras get the warm confirm; sins and skips have their own cues (service-side).
        if (chosen != null && !chosen.Boon.IsCurse) ChaosSfx.PlayBoonPicked();

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

        // No Continue button: hold the glowing pick for a short beat, then commit on its own.
        BtnSkipBoon.Visibility = Visibility.Collapsed;
        BtnReroll.Visibility = Visibility.Collapsed;   // the die is cast — no rerolling a made choice
        DraftCountdown.Text = "";
        _confirmTimer?.Stop();
        _confirmTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _confirmTimer.Tick += (_, _) =>
        {
            _confirmTimer?.Stop();
            _confirmTimer = null;
            ChooseBoon(_selectedBoon);
        };
        _confirmTimer.Start();
    }

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

    public void ShowResults(ChaosRunState s, double baseXp, double skillMult, double finalXp, long previousBest, int sparksEarned,
                            ChaosRank? rankUp = null)
    {
        SetClickThrough(false);
        CountdownBox.Visibility = Visibility.Collapsed;
        DraftPanel.Visibility = Visibility.Collapsed;
        Backdrop.Visibility = Visibility.Visible;
        ResultsPanel.Visibility = Visibility.Visible;
        BringToFront();

        ResultsHero.Source = ChaosArt.ResolveRecap();   // null = the gradient wash shows instead

        // PB / delta-vs-best (best already updated by AwardRunRewards; compare run score to the prior best).
        double score = s.Score;
        double pbDelta = score - previousBest;
        bool isPb = score > previousBest;
        BtnClose.Content = isPb ? "wake up (you'll be back)" : "wake up";

        // Breaking the surface; a PB earns its fanfare once the whoosh has landed.
        ChaosSfx.Play("surface", 0.55f);
        if (isPb)
        {
            var pbTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            pbTimer.Tick += (_, _) => { pbTimer.Stop(); ChaosSfx.Play("pb_fanfare", 0.6f); };
            pbTimer.Start();
        }

        var dim = new SolidColorBrush(Color.FromRgb(170, 170, 200));
        var gold = new SolidColorBrush(Color.FromRgb(255, 215, 90));
        var pink = new SolidColorBrush(Color.FromRgb(255, 105, 180));

        ResultsBody.Children.Clear();

        // Row of three stat chips: how deep, how clean, how long.
        ResultsBody.Children.Add(ChipRow(
            StatChip("DEPTH", $"{Roman(s.ActIndex)} · L{s.WaveIndex}"),
            StatChip("BEST STREAK", $"x{s.BestCombo}"),
            StatChip("SURVIVED", $"{(int)s.ElapsedSec / 60:00}:{(int)s.ElapsedSec % 60:00}")));

        AddResultLine($"snapped {s.Defused} · triggered {s.Detonated} · effects fired {s.EffectsFired}",
            12, dim, FontWeights.Normal);

        ResultsBody.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(70, 255, 105, 180)), Margin = new Thickness(0, 10, 0, 10) });

        // Score + the compulsion hook (PB / delta-vs-best).
        AddResultLine($"score {score:N0}", 24, Brushes.White, FontWeights.Bold);
        if (isPb)
            AddResultLine($"★ NEW BEST  (+{pbDelta:N0} over {previousBest:N0})", 14, gold, FontWeights.Bold);
        else
            AddResultLine($"best {previousBest:N0}   ({pbDelta:N0} vs best)", 12, dim, FontWeights.Normal);

        ResultsBody.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(70, 255, 105, 180)), Margin = new Thickness(0, 10, 0, 10) });

        // The take-home: XP and drops, side by side. Glyph canon: 🕰 xp, ✦ drops, 🐇 gold —
        // the run award banks as DROPS (gold only ever lands instantly, mid-run).
        ResultsBody.Children.Add(ChipRow(
            StatChip("XP", $"{ChaosGlyphs.Xp} {finalXp:N0}", pink, $"base {baseXp:N0} x{skillMult:0.0}"),
            StatChip("DROPS", $"{ChaosGlyphs.Drops} {sparksEarned:N0}", gold, "banked in the dollhouse")));

        // First completion ("first fall"): name the one-time bonus already inside the haul.
        if (ChaosMeta.State.RunsCompleted == 1)
            AddResultLine($"{ChaosGlyphs.Drops} +{ChaosMeta.FIRST_FALL_BONUS} first fall, counted in",
                11, gold, FontWeights.Normal);

        // Run 2 done: one quiet nudge toward her corner (the gold has somewhere to go now).
        if (ChaosMeta.State.RunsCompleted == 2)
            AddResultLine("she set up a small corner in the toybox.", 11, dim, FontWeights.Normal);

        // The door: from the first completed fall onward, the recap always offers the dollhouse.
        BtnDollhouse.Visibility = ChaosMeta.State.RunsCompleted >= 1 ? Visibility.Visible : Visibility.Collapsed;

        // Bark over the results (+ PB fields for the compulsion line).
        App.Bark?.NotifyChaosResultsShown(score, ChaosMeta.State.BestScore, pbDelta, isPb,
            s.Defused, s.Detonated, s.BestCombo, s.Config.Difficulty.ToString());

        // Rank spine: once the tally has settled, the quiet rank-up beat.
        if (rankUp.HasValue) ScheduleRankCard(rankUp.Value);
    }

    // ============================ rank card ============================

    private DispatcherTimer? _rankBeatTimer;
    private DispatcherTimer? _rankCardTimer;

    /// <summary>
    /// The rank-up beat, after the score tally has landed: the announcer murmurs the
    /// [LOCKED] "it noticed." line, then the bare rank card fades into the recap —
    /// no header, no fanfare. Bark, LastRankSeen and the reveal sync land with the card.
    /// </summary>
    private void ScheduleRankCard(ChaosRank rank)
    {
        _rankBeatTimer?.Stop();
        _rankBeatTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
        _rankBeatTimer.Tick += (_, _) =>
        {
            _rankBeatTimer?.Stop();
            _rankBeatTimer = null;
            if (ResultsPanel.Visibility != Visibility.Visible) return;
            ChaosAnnouncerOverlay.Announce("it noticed.", ChaosAnnounceKind.Temptation);
            _rankCardTimer?.Stop();
            _rankCardTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1100) };
            _rankCardTimer.Tick += (_, _) =>
            {
                _rankCardTimer?.Stop();
                _rankCardTimer = null;
                ShowRankCard(rank);
            };
            _rankCardTimer.Start();
        };
        _rankBeatTimer.Start();
    }

    private void ShowRankCard(ChaosRank rank)
    {
        if (ResultsPanel.Visibility != Visibility.Visible) return;

        // Bare and quiet: the rank word, huge and lowercase, one dim line under it.
        var card = new StackPanel { Margin = new Thickness(0, 16, 0, 0), Opacity = 0 };
        card.Children.Add(new TextBlock
        {
            Text = ChaosRanks.NameLower(rank),
            FontSize = 46, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
        });
        card.Children.Add(new TextBlock
        {
            Text = ChaosRanks.Line(rank),
            FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 200)),
            HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
        });
        ResultsBody.Children.Add(card);
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(700)));

        try { App.Bark?.NotifyChaosRankUp(ChaosRanks.NameLower(rank)); } catch { }
        ChaosMeta.State.LastRankSeen = (int)rank;
        ChaosMeta.Save();
        RevealService.Sync("rank_up");
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

    /// <summary>Equal-width row of stat chips for the recap card.</summary>
    private static Grid ChipRow(params Border[] chips)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        for (int i = 0; i < chips.Length; i++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            chips[i].Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0);
            Grid.SetColumn(chips[i], i);
            row.Children.Add(chips[i]);
        }
        return row;
    }

    /// <summary>One recap stat chip: small dim label, bold value, optional sub-line.</summary>
    private static Border StatChip(string label, string value, Brush? valueBrush = null, string? sub = null)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = label, FontSize = 10, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(140, 138, 178)),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        stack.Children.Add(new TextBlock
        {
            Text = value, FontSize = 19, FontWeight = FontWeights.Bold,
            Foreground = valueBrush ?? Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 0),
        });
        if (sub != null)
            stack.Children.Add(new TextBlock
            {
                Text = sub, FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 148, 186)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0),
            });
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 0x22, 0x1E, 0x3E)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 8, 10, 8),
            Child = stack,
        };
    }

    private static string Roman(int n) => n switch
    {
        <= 1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", _ => n.ToString()
    };

    private void BtnRunAgain_Click(object sender, RoutedEventArgs e) { OnRunAgain?.Invoke(); }
    private void BtnClose_Click(object sender, RoutedEventArgs e) { Close(); }

    /// <summary>The recap's door: dismiss the recap, then open the Dollhouse (same single-
    /// instance discipline as the Lab card's entry).</summary>
    private void BtnDollhouse_Click(object sender, RoutedEventArgs e)
    {
        Close();   // OnDismissed → the service tears the run windows down first
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                if (App.Chaos == null || App.Chaos.IsRunning) return;
                if (ChaosHubWindow.Current != null) { ChaosHubWindow.Current.Activate(); return; }
                var hub = new ChaosHubWindow();
                if (App.MainWindowRef != null) hub.Owner = App.MainWindowRef;
                hub.Show();
            }
            catch (Exception ex) { App.Logger?.Warning("Recap dollhouse door failed ({E})", ex.Message); }
        }));
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        try { RemoveCountdownSkipHooks(); } catch { }
        _rankBeatTimer?.Stop(); _rankBeatTimer = null;
        _rankCardTimer?.Stop(); _rankCardTimer = null;
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
