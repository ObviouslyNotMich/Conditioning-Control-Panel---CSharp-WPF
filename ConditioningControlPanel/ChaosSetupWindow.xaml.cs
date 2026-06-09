using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel;

/// <summary>
/// Pre-run setup/lobby for Chaos Mode. Opened by the Lab "START CHAOS" card;
/// lets the user configure the run (difficulty/length/motion/shields, the bubble
/// pool + presets, juice feedback, and the boon draft), persists it to
/// <c>AppSettings</c>, and launches the run on BEGIN CHAOS. The in-run HUD/overlay
/// are separate; this window closes when the run starts.
/// </summary>
public partial class ChaosSetupWindow : Window
{
    private static readonly Random _rng = new();
    private int _shields = 3;
    private int _waves = 5;

    public ChaosSetupWindow()
    {
        InitializeComponent();
        TitleBar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } } };
        SldLiveShare.ValueChanged += (_, _) => TxtLiveShare.Text = $"{(int)(SldLiveShare.Value * 100)}%";
        LoadFromSettings();
        Loaded += (_, _) => InitMetaDebugStrip();
    }

    // ===================== TEMP meta debug strip (Session 1) =====================
    // Lets us exercise the earn→spend→persist→apply loop before the hub window exists.
    // The Sparks/upgrades readout is always shown; the buy buttons are DEBUG-only.
    // This whole strip is removed when the real hub window lands in Session 3.

    private void RefreshMetaDebug()
    {
        var st = ChaosMeta.State;
        TxtMetaDebug.Text = $"Sparks: {st.Sparks}   Upgrades: {st.PurchasedUpgrades.Count}";
    }

    private void InitMetaDebugStrip()
    {
        RefreshMetaDebug();
#if DEBUG
        DebugStrip.Children.Add(MakeDebugBuyButton("buy slow_fuses", "slow_fuses"));
        DebugStrip.Children.Add(MakeDebugBuyButton("buy start_shield", "start_shield"));
#endif
    }

#if DEBUG
    private Button MakeDebugBuyButton(string label, string upgradeId)
    {
        var btn = new Button
        {
            Content = label,
            Tag = upgradeId,
            Style = (Style)FindResource("Stepper"),
            Width = double.NaN,
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(10, 0, 0, 0),
            FontSize = 10
        };
        btn.Click += (_, _) => { ChaosMeta.TryPurchase(upgradeId); RefreshMetaDebug(); };
        return btn;
    }
#endif

    // ============================ load / save ============================

    private void LoadFromSettings()
    {
        var s = App.Settings?.Current;
        if (s == null) { LoadDefaults(); return; }

        SetSegment(GrpDifficulty, s.ChaosDifficulty);
        SetSegment(GrpLength, s.ChaosRunDurationSec.ToString());
        SetSegment(GrpMotion, s.ChaosMotionMode);
        SldLiveShare.Value = s.ChaosLiveBubbleShare;
        TxtLiveShare.Text = $"{(int)(s.ChaosLiveBubbleShare * 100)}%";
        _shields = s.ChaosStartingShields; TxtShields.Text = _shields.ToString();
        _waves = s.ChaosWaveCount; TxtWaves.Text = _waves.ToString();

        var enabled = s.ChaosEnabledVariants; // null = all
        foreach (var t in GrpPool.Children.OfType<ToggleButton>())
            t.IsChecked = enabled == null || enabled.Contains(t.Tag?.ToString() ?? "");

        ChkShake.IsChecked = s.ChaosScreenShakeEnabled;
        SldShake.Value = s.ChaosShakeIntensity;
        ChkFlashes.IsChecked = s.ChaosColorFlashesEnabled;
        SldEffect.Value = s.ChaosEffectIntensity;
        ChkBoonDraft.IsChecked = s.ChaosBoonDraftEnabled;
        ChkCurses.IsChecked = s.ChaosAllowCurses;
        ChkDarters.IsChecked = s.ChaosDartersEnabled;
    }

    private void LoadDefaults()
    {
        SetSegment(GrpDifficulty, "Easy");
        SetSegment(GrpLength, "180");
        SetSegment(GrpMotion, "Mixed");
        SldLiveShare.Value = 0.35; TxtLiveShare.Text = "35%";
        _shields = 3; TxtShields.Text = "3";
        _waves = 5; TxtWaves.Text = "5";
        foreach (var t in GrpPool.Children.OfType<ToggleButton>()) t.IsChecked = true;
        ChkShake.IsChecked = true; SldShake.Value = 0.8;
        ChkFlashes.IsChecked = true; SldEffect.Value = 0.85;
        ChkBoonDraft.IsChecked = true; ChkCurses.IsChecked = true;
        ChkDarters.IsChecked = true;
    }

    private void SaveToSettings()
    {
        var s = App.Settings?.Current;
        if (s == null) return;

        s.ChaosDifficulty = GetSegment(GrpDifficulty) ?? "Easy";
        if (int.TryParse(GetSegment(GrpLength), out var len)) s.ChaosRunDurationSec = len;
        s.ChaosMotionMode = GetSegment(GrpMotion) ?? "Mixed";
        s.ChaosLiveBubbleShare = SldLiveShare.Value;
        s.ChaosStartingShields = _shields;
        s.ChaosWaveCount = _waves;

        var checkd = GrpPool.Children.OfType<ToggleButton>()
            .Where(t => t.IsChecked == true).Select(t => t.Tag?.ToString() ?? "").Where(x => x.Length > 0).ToList();
        // All (or none) selected → null = "all variants".
        s.ChaosEnabledVariants = (checkd.Count == 0 || checkd.Count == GrpPool.Children.OfType<ToggleButton>().Count())
            ? null : checkd;

        s.ChaosScreenShakeEnabled = ChkShake.IsChecked == true;
        s.ChaosShakeIntensity = SldShake.Value;
        s.ChaosColorFlashesEnabled = ChkFlashes.IsChecked == true;
        s.ChaosEffectIntensity = SldEffect.Value;
        s.ChaosBoonDraftEnabled = ChkBoonDraft.IsChecked == true;
        s.ChaosAllowCurses = ChkCurses.IsChecked == true;
        s.ChaosDartersEnabled = ChkDarters.IsChecked == true;
    }

    // ============================ controls ============================

    private void Segment_Click(object sender, RoutedEventArgs e)
    {
        var btn = (ToggleButton)sender;
        var grp = (Panel)btn.Parent;
        foreach (var t in grp.Children.OfType<ToggleButton>()) t.IsChecked = ReferenceEquals(t, btn);
    }

    private void Stepper_Click(object sender, RoutedEventArgs e)
    {
        switch ((sender as Button)?.Tag?.ToString())
        {
            case "shields-": _shields = Math.Max(0, _shields - 1); TxtShields.Text = _shields.ToString(); break;
            case "shields+": _shields = Math.Min(5, _shields + 1); TxtShields.Text = _shields.ToString(); break;
            case "waves-": _waves = Math.Max(1, _waves - 1); TxtWaves.Text = _waves.ToString(); break;
            case "waves+": _waves = Math.Min(12, _waves + 1); TxtWaves.Text = _waves.ToString(); break;
        }
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        var name = (sender as Button)?.Tag?.ToString();
        var preset = ChaosBubbleVariants.Presets.FirstOrDefault(p => p.Name == name);
        if (preset == null) return;
        foreach (var t in GrpPool.Children.OfType<ToggleButton>())
            t.IsChecked = preset.VariantIds.Contains(t.Tag?.ToString() ?? "");
        SldLiveShare.Value = preset.LiveShare;
    }

    private void BtnRandomize_Click(object sender, RoutedEventArgs e)
    {
        SetSegment(GrpDifficulty, new[] { "Easy", "Medium", "Hard", "Extreme" }[_rng.Next(4)]);
        SetSegment(GrpLength, new[] { "120", "180", "300" }[_rng.Next(3)]);
        SetSegment(GrpMotion, new[] { "Mixed", "FloatUp", "RainDown", "RoamBounce" }[_rng.Next(4)]);
        SldLiveShare.Value = 0.2 + _rng.NextDouble() * 0.6;
        var pool = GrpPool.Children.OfType<ToggleButton>().ToList();
        foreach (var t in pool) t.IsChecked = _rng.NextDouble() < 0.6;
        if (!pool.Any(t => t.IsChecked == true)) pool[0].IsChecked = true;
    }

    private void BtnDefaults_Click(object sender, RoutedEventArgs e) => LoadDefaults();

    private void BtnBegin_Click(object sender, RoutedEventArgs e)
    {
        SaveToSettings();
        Close();
        Application.Current?.Dispatcher.BeginInvoke(new Action(() => App.Chaos?.StartRun()));
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    // ============================ helpers ============================

    private static string? GetSegment(Panel grp) =>
        grp.Children.OfType<ToggleButton>().FirstOrDefault(t => t.IsChecked == true)?.Tag?.ToString();

    private static void SetSegment(Panel grp, string? tag)
    {
        foreach (var t in grp.Children.OfType<ToggleButton>())
            t.IsChecked = (t.Tag?.ToString() == tag);
    }
}
