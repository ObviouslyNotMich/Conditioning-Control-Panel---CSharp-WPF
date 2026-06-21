using System;
using System.Collections.Generic;
using System.Linq;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Input;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using global::Avalonia.Interactivity;
using global::Avalonia.Threading;

using Microsoft.Extensions.DependencyInjection;
using ChaosNarrativeContext = ConditioningControlPanel.Core.Services.Chaos.ChaosNarrativeContext;
namespace ConditioningControlPanel.Avalonia.Chaos;

public partial class ChaosHubWindow : Window
{
    private readonly global::ConditioningControlPanel.IAppLogger _logger;
    private readonly global::ConditioningControlPanel.Core.Services.Settings.ISettingsService _settings;


    private static readonly Random _rng = new();
    private int _waves = 5;
    private bool _uiSoundsReady;
    private bool _fallingIn;
    private bool _diffAutoClamped;

    public static ChaosHubWindow? Current { get; private set; }

    public ChaosHubWindow()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
        _settings = App.Services.GetRequiredService<global::ConditioningControlPanel.Core.Services.Settings.ISettingsService>();
Current = this;
        AddHandler(Button.ClickEvent, (s, e) => { if (_uiSoundsReady) AvaloniaChaosSfx.Play("ui_click", 0.3f); }, RoutingStrategies.Bubble);
        Closed += (_, _) =>
        {
            Current = null;
            AvaloniaChaosApp.Chaos?.CloseLoadoutSidebar();
            if (!_fallingIn) AvaloniaChaosApp.Avatar?.SetChaosRunActive(false);
        };
        AvaloniaChaosApp.Chaos?.ShowLoadoutSidebar();
        AvaloniaChaosApp.Avatar?.SetChaosRunActive(true);

        LoadFromSettings();
        InitRevealMap();
        BuildHabits();
        BuildLifetimeBoons();
        BuildLoadoutTiles();
        BuildBench();
        BuildMantras();
        BuildDiary();
        RefreshTopBar();
        RefreshStats();
        ApplyUnlocks();
        ApplyReveals();
        LoadBanner();
        BuildDebugStrip();
        ShowTab("loadout");
        Opened += (_, _) => { OnHubOpenedReveals(); FireHubGreeting(); };
        _uiSoundsReady = true;
    }

    private void FireHubGreeting()
    {
        try
        {
            if (!AvaloniaChaosMode.NarrativeActive) return;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                if (Current != this) return;
                ChaosNarrativeHooks.OnHubMoment("hub_return", new ChaosNarrativeContext { RankIndex = ChaosMeta.RankIndex });
            };
            t.Start();
        }
        catch (Exception ex) { _logger?.Information("ChaosHub.FireHubGreeting: {E}", ex.Message); }
    }

    private void LoadBanner()
    {
        var src = ChaosArt.ResolveBanner();
        if (src != null) { BannerImage.Source = src; BannerImage.IsVisible = true; }
        var bd = ChaosArt.Resolve("hub", "backdrop");
        if (bd != null) { HubBackdrop.Source = bd; HubBackdrop.IsVisible = true; }
    }

    private void ApplyUnlocks()
    {
        TabLoadout.IsEnabled = true;
        TabEnhance.IsEnabled = true;
        TabRun.IsEnabled = true;
        TabImprove.IsEnabled = true;
        TabDiary.IsVisible = DiaryUnlocked;
    }

    private static bool DiaryUnlocked => RevealService.IsUnlocked(RevealIds.Diary);

    private void Tab_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton tb && tb.IsEnabled == true) ShowTab(tb.Tag?.ToString() ?? "loadout");
        else if (sender is ToggleButton tb2) tb2.IsChecked = false;
    }

    public void ShowTab(string tag)
    {
        if (tag == "habits") tag = "enhance";
        if (tag == "improve" && TabImprove.IsEnabled != true) tag = "loadout";
        if (tag == "diary" && !DiaryUnlocked) tag = "loadout";

        PanelLoadout.IsVisible = tag == "loadout";
        PanelEnhance.IsVisible = tag == "enhance";
        PanelRun.IsVisible = tag == "run";
        PanelImprove.IsVisible = tag == "improve";
        PanelDiary.IsVisible = tag == "diary";

        TabLoadout.IsChecked = tag == "loadout";
        TabEnhance.IsChecked = tag == "enhance";
        TabRun.IsChecked = tag == "run";
        TabImprove.IsChecked = tag == "improve";
        TabDiary.IsChecked = tag == "diary";

        TxtHint.Text = tag switch
        {
            "loadout" => "click a tile to slip it into a pocket. + takes you where it's sold.",
            "enhance" => "spend your drops. deepen what you like.",
            "run" => "dress up the fall, then FALL IN.",
            "improve" => "the bench, the mantras, how far you've fallen.",
            "diary" => "everything you've met down there. click an entry to pop it out.",
            _ => "",
        };
    }

    public void NavigateTo(string tag)
    {
        ShowTab(tag);
        try { Activate(); } catch { }
    }

    private void TestingToggle_Click(object? sender, RoutedEventArgs e)
    {
        bool open = TglTesting.IsChecked == true;
        TestingBody.IsVisible = open;
        TglTesting.Content = open ? "🧪 testing options ▾" : "🧪 testing options ▸";
    }

    #region top bar / stats

    private int _shownSparks = -1, _shownGold = -1;

    private void RefreshTopBar()
    {
        AnimateBalance(TxtSparks, _shownSparks, ChaosMeta.State.Sparks);
        AnimateBalance(TxtGold, _shownGold, ChaosMeta.State.Gold);
        _shownSparks = ChaosMeta.State.Sparks;
        _shownGold = ChaosMeta.State.Gold;
        TxtRank.Text = ChaosMeta.Rank;
        RefreshTabBadges();
    }

    private void RefreshTabBadges()
    {
        SetTabBadge(TabEnhance, "the Toybox", CountAffordableToybox());
        SetTabBadge(TabImprove, "the Looking Glass", TabImprove.IsEnabled == true ? CountAffordableBench() : 0);
    }

    private static void SetTabBadge(ToggleButton tab, string label, int count)
    {
        if (count <= 0)
        {
            tab.Content = label;
            ToolTip.SetTip(tab, null);
            return;
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xE8, 0x43, 0x93)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(7, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = count.ToString(), Foreground = Brushes.White, FontSize = 10.5, FontWeight = FontWeight.Bold },
        });
        tab.Content = row;
        ToolTip.SetTip(tab, count == 1 ? "1 thing you can afford right now" : $"{count} things you can afford right now");
    }

    private static int CountAffordableToybox() => 0;
    private int CountAffordableBench() => 0;

    private static void AnimateBalance(TextBlock tb, int from, int to)
    {
        if (from < 0 || from == to)
        {
            tb.Text = to.ToString("N0");
            return;
        }
        const int DURATION_MS = 500, FRAME_MS = 33, TICK_EVERY_MS = 90;
        int elapsed = 0, lastTick = -TICK_EVERY_MS;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FRAME_MS) };
        timer.Tick += (_, _) =>
        {
            elapsed += FRAME_MS;
            if (elapsed >= DURATION_MS)
            {
                timer.Stop();
                tb.Text = to.ToString("N0");
                return;
            }
            double eased = 1 - Math.Pow(1 - elapsed / (double)DURATION_MS, 3);
            tb.Text = ((int)Math.Round(from + (to - from) * eased)).ToString("N0");
            if (elapsed - lastTick >= TICK_EVERY_MS)
            {
                lastTick = elapsed;
                AvaloniaChaosSfx.Play("count_tick", 0.4f);
            }
        };
        timer.Start();
    }

    private void RefreshStats()
    {
        var s = ChaosMeta.State;
        StSparks.Text = s.Sparks.ToString("N0");
        StRuns.Text = s.RunsCompleted.ToString("N0");
        StTimeUnder.Text = FormatPlaytime(s.TotalRunSeconds);
        StBestScore.Text = s.BestScore.ToString("N0");
        StBestCombo.Text = s.BestCombo.ToString("N0");
        StDefused.Text = s.TotalDefused.ToString("N0");
        StTimeHeld.Text = FormatPlaytime(s.TotalChannelSeconds);
    }

    private static string FormatPlaytime(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{t.Minutes}m {t.Seconds}s";
    }

    #endregion

    #region run-setup load / save

    private void LoadFromSettings()
    {
        var s = _settings?.Current;
        if (s == null) { LoadDefaults(); ApplyExtremeGate(); return; }

        SetSegment(GrpDifficulty, s.ChaosDifficulty);
        SetSegment(GrpLength, s.ChaosRunDurationSec.ToString());
        SetSegment(GrpMotion, s.ChaosMotionMode);
        _waves = s.ChaosWaveCount; TxtWaves.Text = _waves.ToString();

        var enabled = s.ChaosEnabledVariants;
        foreach (var t in GrpPool.Children.OfType<ToggleButton>())
            t.IsChecked = enabled == null || enabled.Contains(t.Tag?.ToString() ?? "");

        ChkShake.IsChecked = s.ChaosScreenShakeEnabled;
        SldShake.Value = s.ChaosShakeIntensity;
        ChkFlashes.IsChecked = s.ChaosColorFlashesEnabled;
        SldEffect.Value = s.ChaosEffectIntensity;
        ChkBoonDraft.IsChecked = s.ChaosBoonDraftEnabled;
        ChkCurses.IsChecked = s.ChaosAllowCurses;
        ChkDarters.IsChecked = s.ChaosDartersEnabled;
        ChkAnnouncer.IsChecked = s.ChaosAnnouncerEnabled;
        ChkNarrative.IsChecked = s.NarrativeModeEnabled;
        ChkBackdrop.IsChecked = s.BackdropEnabled;
        SldBackdropOpacity.Value = s.BackdropOpacity;

        var keyOpts = new[] { "Q", "E", "R", "F", "Z", "X", "C", "V", "1", "2", "3", "4" };
        CmbAccKey1.ItemsSource = keyOpts;
        CmbAccKey2.ItemsSource = keyOpts;
        CmbAccKey1.SelectedItem = keyOpts.Contains(s.ChaosAccessoryKey1) ? s.ChaosAccessoryKey1 : "Q";
        CmbAccKey2.SelectedItem = keyOpts.Contains(s.ChaosAccessoryKey2) ? s.ChaosAccessoryKey2 : "E";

        ApplyExtremeGate();
    }

    private void ApplyExtremeGate()
    {
        bool unlocked = ChaosMeta.State.ExtremeUnlocked;
        SegExtreme.IsEnabled = unlocked;
        SegExtreme.Content = unlocked ? "Inescapable" : "Inescapable 🔒";
        if (!unlocked)
        {
            int cost = ChaosUpgrades.ById("extreme_tier")?.Cost ?? 0;
            ToolTip.SetTip(SegExtreme, "a deeper door. she sells the key in the Toybox: "
                + $"finish {ChaosLessons.T_EXTREME_TIER} relentless descents, reach Devoted "
                + $"({ChaosRanks.Thresholds[(int)ChaosRank.Devoted]} descents), then train it for ✦{cost}.");
        }
        if (!unlocked && SegExtreme.IsChecked == true)
            SetSegment(GrpDifficulty, "Hard");
    }

    private void LoadDefaults()
    {
        SetSegment(GrpDifficulty, "Easy");
        SetSegment(GrpLength, "180");
        SetSegment(GrpMotion, "Mixed");
        _waves = 5; TxtWaves.Text = "5";
        foreach (var t in GrpPool.Children.OfType<ToggleButton>()) t.IsChecked = true;
        ChkShake.IsChecked = true; SldShake.Value = 0.8;
        ChkFlashes.IsChecked = true; SldEffect.Value = 0.85;
        ChkBoonDraft.IsChecked = true; ChkCurses.IsChecked = true;
        ChkDarters.IsChecked = true;
        ChkAnnouncer.IsChecked = true;
        ChkNarrative.IsChecked = true;
        ChkBackdrop.IsChecked = true;
        SldBackdropOpacity.Value = 0.55;
    }

    private void SaveToSettings()
    {
        var s = _settings?.Current;
        if (s == null) return;

        if (!_diffAutoClamped) s.ChaosDifficulty = GetSegment(GrpDifficulty) ?? "Easy";
        if (int.TryParse(GetSegment(GrpLength), out var len)) s.ChaosRunDurationSec = len;
        s.ChaosMotionMode = GetSegment(GrpMotion) ?? "Mixed";
        s.ChaosWaveCount = _waves;

        var checkd = GrpPool.Children.OfType<ToggleButton>()
            .Where(t => t.IsChecked == true).Select(t => t.Tag?.ToString() ?? "").Where(x => x.Length > 0).ToList();
        s.ChaosEnabledVariants = (checkd.Count == 0 || checkd.Count == GrpPool.Children.OfType<ToggleButton>().Count())
            ? null : checkd;

        s.ChaosScreenShakeEnabled = ChkShake.IsChecked == true;
        s.ChaosShakeIntensity = SldShake.Value;
        s.ChaosColorFlashesEnabled = ChkFlashes.IsChecked == true;
        s.ChaosEffectIntensity = SldEffect.Value;
        s.ChaosBoonDraftEnabled = ChkBoonDraft.IsChecked == true;
        s.ChaosAllowCurses = ChkCurses.IsChecked == true;
        s.ChaosDartersEnabled = ChkDarters.IsChecked == true;
        s.ChaosAnnouncerEnabled = ChkAnnouncer.IsChecked == true;
        s.NarrativeModeEnabled = ChkNarrative.IsChecked == true;
        s.BackdropEnabled = ChkBackdrop.IsChecked == true;
        s.BackdropOpacity = SldBackdropOpacity.Value;
    }

    private void Segment_Click(object? sender, RoutedEventArgs e)
    {
        var btn = (ToggleButton)sender!;
        if (btn.IsEnabled != true) { btn.IsChecked = false; return; }
        var grp = (Panel)btn.Parent!;
        foreach (var t in grp.Children.OfType<ToggleButton>()) t.IsChecked = ReferenceEquals(t, btn);
        if (ReferenceEquals(grp, GrpDifficulty)) _diffAutoClamped = false;
    }

    private void Stepper_Click(object? sender, RoutedEventArgs e)
    {
        switch ((sender as Button)?.Tag?.ToString())
        {
            case "waves-": _waves = Math.Max(1, _waves - 1); TxtWaves.Text = _waves.ToString(); break;
            case "waves+": _waves = Math.Min(12, _waves + 1); TxtWaves.Text = _waves.ToString(); break;
        }
    }

    private void Preset_Click(object? sender, RoutedEventArgs e)
    {
        var name = (sender as Button)?.Tag?.ToString();
        var preset = ChaosBubbleVariants.Presets.FirstOrDefault(p => p.Name == name);
        if (preset == null) return;
        foreach (var t in GrpPool.Children.OfType<ToggleButton>())
            t.IsChecked = preset.VariantIds.Contains(t.Tag?.ToString() ?? "");
    }

    private void BtnRandomize_Click(object? sender, RoutedEventArgs e)
    {
        var diffs = new List<string> { "Easy" };
        if (SegMedium.IsVisible) diffs.Add("Medium");
        if (SegHard.IsVisible) diffs.Add("Hard");
        if (ChaosMeta.State.ExtremeUnlocked) diffs.Add("Extreme");
        SetSegment(GrpDifficulty, diffs[_rng.Next(diffs.Count)]);
        SetSegment(GrpLength, new[] { "120", "180", "300" }[_rng.Next(3)]);
        SetSegment(GrpMotion, new[] { "Mixed", "FloatUp", "RainDown", "RoamBounce" }[_rng.Next(4)]);
        var pool = GrpPool.Children.OfType<ToggleButton>().ToList();
        foreach (var t in pool) t.IsChecked = _rng.NextDouble() < 0.6;
        if (!pool.Any(t => t.IsChecked == true)) pool[0].IsChecked = true;
    }

    private void BtnDefaults_Click(object? sender, RoutedEventArgs e) { LoadDefaults(); ApplyExtremeGate(); }

    private void AccKey_Changed(object? sender, SelectionChangedEventArgs e)
    {
        var s = _settings?.Current;
        if (s == null) return;
        if (CmbAccKey1?.SelectedItem is string k1) s.ChaosAccessoryKey1 = k1;
        if (CmbAccKey2?.SelectedItem is string k2) s.ChaosAccessoryKey2 = k2;
    }

    private void BtnBegin_Click(object? sender, RoutedEventArgs e)
    {
        var mode = (sender as Control)?.Tag?.ToString() == "FreeDesktop"
            ? ChaosPlayMode.FreeDesktop
            : ChaosPlayMode.Story;
        _fallingIn = true;
        SaveToSettings();
        Close();
        var cfg = ChaosRunConfig.FromSettings();
        cfg.PlayMode = mode;
        Dispatcher.UIThread.Post(() => AvaloniaChaosApp.Chaos?.StartRun(cfg));
    }

    public void FallIn() => BtnBegin_Click(this, new RoutedEventArgs());

    private void BtnClose_Click(object? sender, RoutedEventArgs e) => Close();

    private void BtnGuide_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var intro =
new ChaosIntroWindow();
            intro.ShowDialog(this);
        }
        catch (Exception ex) { _logger?.Information("Chaos guide reshow: {E}", ex.Message); }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private static string? GetSegment(Panel grp) =>
        grp.Children.OfType<ToggleButton>().FirstOrDefault(t => t.IsChecked == true)?.Tag?.ToString();

    private static void SetSegment(Panel grp, string? tag)
    {
        foreach (var t in grp.Children.OfType<ToggleButton>())
            t.IsChecked = (t.Tag?.ToString() == tag);
    }

    #endregion
}
