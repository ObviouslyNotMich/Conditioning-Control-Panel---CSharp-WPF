using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel;

/// <summary>
/// The Chaos Mode hub: opened from the Lab "START CHAOS" entry point. Tabs for
/// Upgrades (spend Sparks), Run setup (configure + BEGIN CHAOS), Loadout (equip a
/// start boon), Codex (stub), and Stats (lifetime readout). Tabs unlock as the
/// player completes runs. Replaces the old setup-only window.
/// </summary>
public partial class ChaosHubWindow : Window
{
    private static readonly Random _rng = new();
    private int _shields = 3;
    private int _waves = 5;

    public ChaosHubWindow()
    {
        InitializeComponent();
        TitleBar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } } };

        LoadFromSettings();
        BuildUpgrades();
        BuildLoadout();
        BuildCodex();
        RefreshTopBar();
        RefreshStats();
        ApplyUnlocks();
        LoadCrests();
        LoadBanner();
        ShowTab(DefaultTab());
    }

    private void LoadBanner()
    {
        var src = ChaosArt.ResolveBanner();
        if (src != null) { BannerImage.Source = src; BannerImage.Visibility = Visibility.Visible; }
    }

    // ============================ tabs / gating ============================

    private void ApplyUnlocks()
    {
        int runs = ChaosMeta.State.RunsCompleted;
        SetTabEnabled(TabUpgrades, runs >= ChaosMeta.UNLOCK_UPGRADES_RUNS);
        SetTabEnabled(TabStats, runs >= ChaosMeta.UNLOCK_STATS_RUNS);
        SetTabEnabled(TabCodex, runs >= ChaosMeta.UNLOCK_CODEX_RUNS);
        SetTabEnabled(TabLoadout, runs >= ChaosMeta.UNLOCK_LOADOUT_RUNS);
        // Run setup is always available.
        TabRun.IsEnabled = true;
    }

    private static void SetTabEnabled(ToggleButton tab, bool enabled)
    {
        tab.IsEnabled = enabled;
        tab.ToolTip = enabled ? null : "play a run to unlock";
    }

    private string DefaultTab() => TabUpgrades.IsEnabled ? "upgrades" : "run";

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton tb && tb.IsEnabled) ShowTab(tb.Tag?.ToString() ?? "run");
        else if (sender is ToggleButton tb2) tb2.IsChecked = false;
    }

    private void ShowTab(string tag)
    {
        PanelUpgrades.Visibility = tag == "upgrades" ? Visibility.Visible : Visibility.Collapsed;
        PanelRun.Visibility      = tag == "run"      ? Visibility.Visible : Visibility.Collapsed;
        PanelLoadout.Visibility  = tag == "loadout"  ? Visibility.Visible : Visibility.Collapsed;
        PanelCodex.Visibility    = tag == "codex"    ? Visibility.Visible : Visibility.Collapsed;
        PanelStats.Visibility    = tag == "stats"    ? Visibility.Visible : Visibility.Collapsed;

        TabUpgrades.IsChecked = tag == "upgrades";
        TabRun.IsChecked      = tag == "run";
        TabLoadout.IsChecked  = tag == "loadout";
        TabCodex.IsChecked    = tag == "codex";
        TabStats.IsChecked    = tag == "stats";

        // BEGIN CHAOS is the action for the run setup; keep it always available
        // (run setup is always unlocked), but nudge the hint on other tabs.
        TxtHint.Text = tag == "run" ? "" : "switch to Run setup to start";
    }

    // ============================ top bar / stats ============================

    private void RefreshTopBar()
    {
        TxtSparks.Text = ChaosMeta.State.Sparks.ToString("N0");
        TxtRank.Text = ChaosMeta.Rank;
    }

    private void RefreshStats()
    {
        var s = ChaosMeta.State;
        StSparks.Text = s.Sparks.ToString("N0");
        StRuns.Text = s.RunsCompleted.ToString("N0");
        StBestScore.Text = s.BestScore.ToString("N0");
        StBestCombo.Text = s.BestCombo.ToString("N0");
        StDefused.Text = s.TotalDefused.ToString("N0");
    }

    // ============================ upgrades tab ============================

    private static Color BranchColor(ChaosBranch b) => b switch
    {
        ChaosBranch.Control => Color.FromRgb(0x49, 0xB6, 0xE8),
        ChaosBranch.Greed   => Color.FromRgb(0xE8, 0xB4, 0x43),
        ChaosBranch.Depth   => Color.FromRgb(0x8B, 0x5C, 0xF6),
        _                   => Color.FromRgb(0xE8, 0x43, 0x93)
    };

    private void LoadCrests()
    {
        CrestControl.Source = ChaosArt.Resolve("crests", "control");
        CrestGreed.Source = ChaosArt.Resolve("crests", "greed");
        CrestDepth.Source = ChaosArt.Resolve("crests", "depth");
        foreach (var img in new[] { CrestControl, CrestGreed, CrestDepth })
            img.Visibility = img.Source != null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildUpgrades()
    {
        UpgColControl.Children.Clear();
        UpgColGreed.Children.Clear();
        UpgColDepth.Children.Clear();

        foreach (var u in ChaosUpgrades.All)
        {
            var host = u.Branch switch
            {
                ChaosBranch.Control => UpgColControl,
                ChaosBranch.Greed => UpgColGreed,
                _ => UpgColDepth
            };
            host.Children.Add(BuildUpgradeRow(u));
        }
    }

    private Border BuildUpgradeRow(ChaosUpgrade u)
    {
        bool owned = ChaosMeta.IsOwned(u.Id);
        bool afford = ChaosMeta.CanAfford(u.Id);

        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // icon slot — real art if present (phase 5), else a branch-tinted placeholder.
        var iconSrc = u.IconPath != null ? ChaosArt.TryLoad(u.IconPath) : ChaosArt.Resolve("upgrades", u.Id);
        FrameworkElement icon;
        if (iconSrc != null)
            icon = new Image { Source = iconSrc, Width = 30, Height = 30 };
        else
        {
            var c = BranchColor(u.Branch);
            icon = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(Color.FromArgb(80, c.R, c.G, c.B)),
                BorderBrush = new SolidColorBrush(c), BorderThickness = new Thickness(1)
            };
        }
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.Margin = new Thickness(0, 0, 10, 0);
        Grid.SetColumn(icon, 0);
        row.Children.Add(icon);

        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        mid.Children.Add(new TextBlock { Text = u.Name, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        mid.Children.Add(new TextBlock { Text = $"✦ {u.Cost}", Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x5C, 0xF6)), FontSize = 11 });
        Grid.SetColumn(mid, 1);
        row.Children.Add(mid);

        FrameworkElement right;
        if (owned)
        {
            right = new TextBlock { Text = "✓", Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0xE0, 0x96)), FontSize = 16, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        }
        else
        {
            var buy = new Button
            {
                Content = "Buy",
                Tag = u.Id,
                Padding = new Thickness(10, 4, 10, 4),
                Background = afford ? new SolidColorBrush(Color.FromRgb(0xE8, 0x43, 0x93)) : new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                Foreground = afford ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x88, 0xA0, 0xA0)),
                BorderThickness = new Thickness(0),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Cursor = afford ? Cursors.Hand : Cursors.Arrow,
                IsEnabled = afford
            };
            buy.Click += Buy_Click;
            right = buy;
        }
        right.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(right, 2);
        row.Children.Add(right);

        var c2 = BranchColor(u.Branch);
        return new Border
        {
            Child = row,
            Background = new SolidColorBrush(Color.FromRgb(0x22, 0x1F, 0x40)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(owned ? (byte)90 : (byte)40, c2.R, c2.G, c2.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 8)
        };
    }

    private void Buy_Click(object sender, RoutedEventArgs e)
    {
        var id = (sender as Button)?.Tag?.ToString();
        if (string.IsNullOrEmpty(id)) return;
        if (ChaosMeta.TryPurchase(id!))
        {
            if (id == "extreme_tier") ApplyExtremeGate();
            BuildUpgrades();
            RefreshTopBar();
            RefreshStats();
        }
    }

    // ============================ loadout tab ============================

    private void BuildLoadout()
    {
        LoadoutPanel.Children.Clear();
        LoadoutPanel.Children.Add(BuildBoonCard(null));   // "None"
        foreach (var b in ChaosBoonPool.All.Where(b => !b.IsCurse))
            LoadoutPanel.Children.Add(BuildBoonCard(b));
    }

    private Border BuildBoonCard(ChaosBoon? boon)
    {
        bool selected = (boon?.Id ?? null) == ChaosMeta.State.EquippedStartBoon
                        || (boon == null && string.IsNullOrEmpty(ChaosMeta.State.EquippedStartBoon));

        var sp = new StackPanel { Width = 150 };
        var iconSrc = boon != null ? ChaosArt.Resolve("boons", boon.Id) : null;
        if (iconSrc != null)
            sp.Children.Add(new Image { Source = iconSrc, Width = 34, Height = 34, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6) });
        else
            sp.Children.Add(new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Color.FromArgb(70, 0xE8, 0x43, 0x93)), BorderBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0x43, 0x93)), BorderThickness = new Thickness(1) });

        sp.Children.Add(new TextBlock { Text = boon?.Name ?? "None", Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold });
        sp.Children.Add(new TextBlock { Text = boon?.Desc ?? "No start boon equipped.", Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xB8, 0xB8)), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });

        var card = new Border
        {
            Child = sp,
            Tag = boon?.Id,
            Background = new SolidColorBrush(Color.FromRgb(0x22, 0x1F, 0x40)),
            BorderBrush = selected ? new SolidColorBrush(Color.FromRgb(0xE8, 0x43, 0x93)) : new SolidColorBrush(Color.FromArgb(40, 0xE8, 0x43, 0x93)),
            BorderThickness = new Thickness(selected ? 2 : 1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 10, 10),
            Cursor = Cursors.Hand
        };
        card.MouseLeftButtonDown += (_, _) =>
        {
            ChaosMeta.EquipStartBoon(boon?.Id);
            BuildLoadout();
        };
        return card;
    }

    // ============================ codex tab ============================

    private void BuildCodex()
    {
        CodexPanel.Children.Clear();
        CodexPanel.Children.Add(new TextBlock { Text = "CODEX", Style = (Style)FindResource("SectionHdr") });
        CodexPanel.Children.Add(new TextBlock
        {
            Text = "Entries fill in as you encounter bubbles and boons during a run.",
            Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xB8, 0xB8)), FontSize = 12,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
        });

        CodexPanel.Children.Add(SubHeader("BUBBLES"));
        foreach (var v in ChaosBubbleVariants.All)
            CodexPanel.Children.Add(CodexRow("bubble:" + v.Id, v.Name, ChaosBubbleVariants.DescriptionFor(v.Id),
                ChaosArt.Resolve("bubbles", v.Id), "●", Color.FromRgb(v.Tint.R, v.Tint.G, v.Tint.B)));
        // Darter is not part of the weighted pool table; list it explicitly.
        CodexPanel.Children.Add(CodexRow("bubble:darter", "Darter", ChaosBubbleVariants.DescriptionFor("darter"),
            ChaosArt.Resolve("bubbles", "darter"), "✦", Color.FromRgb(0xFF, 0x4D, 0xC4)));

        CodexPanel.Children.Add(SubHeader("BOONS & CURSES"));
        foreach (var b in ChaosBoonPool.All)
            CodexRow_Add(b);
    }

    private void CodexRow_Add(ChaosBoon b)
    {
        var accent = b.IsCurse ? Color.FromRgb(0xFF, 0x8A, 0x8A) : Color.FromRgb(0x9C, 0xE8, 0xA0);
        CodexPanel.Children.Add(CodexRow("boon:" + b.Id, b.Name, b.Desc, ChaosArt.Resolve("boons", b.Id),
            b.IsCurse ? "☠" : "◈", accent));
    }

    private TextBlock SubHeader(string text) => new()
    {
        Text = text, Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0x43, 0x93)),
        FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold, FontSize = 11,
        Margin = new Thickness(0, 12, 0, 6)
    };

    private Border CodexRow(string codexId, string name, string desc, ImageSource? iconSrc, string glyph, Color accent)
    {
        bool seen = ChaosMeta.IsDiscovered(codexId);

        var row = new StackPanel { Orientation = Orientation.Horizontal };

        // icon / silhouette
        FrameworkElement icon;
        if (seen && iconSrc != null)
            icon = new Image { Source = iconSrc, Width = 26, Height = 26 };
        else
            icon = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(seen ? Color.FromArgb(60, accent.R, accent.G, accent.B) : Color.FromArgb(40, 120, 120, 140)),
                BorderBrush = new SolidColorBrush(seen ? accent : Color.FromRgb(90, 90, 110)), BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = seen ? glyph : "?", Foreground = new SolidColorBrush(seen ? accent : Color.FromRgb(0x88, 0x88, 0xA0)), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.Margin = new Thickness(0, 0, 12, 0);
        row.Children.Add(icon);

        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Width = 700 };
        mid.Children.Add(new TextBlock { Text = seen ? name : "???", Foreground = seen ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x90)), FontSize = 12, FontWeight = FontWeights.SemiBold });
        mid.Children.Add(new TextBlock { Text = seen ? desc : "Locked — encounter it in a run to reveal.", Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xB8, 0xB8)), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        row.Children.Add(mid);

        return new Border
        {
            Child = row,
            Background = new SolidColorBrush(Color.FromRgb(0x22, 0x1F, 0x40)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(seen ? (byte)70 : (byte)25, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 6)
        };
    }

    // ============================ run-setup load / save ============================

    private void LoadFromSettings()
    {
        var s = App.Settings?.Current;
        if (s == null) { LoadDefaults(); ApplyExtremeGate(); return; }

        SetSegment(GrpDifficulty, s.ChaosDifficulty);
        SetSegment(GrpLength, s.ChaosRunDurationSec.ToString());
        SetSegment(GrpMotion, s.ChaosMotionMode);
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

        ApplyExtremeGate();
    }

    /// <summary>Lock/unlock the Extreme difficulty pill from meta state; fall back off it when locked.</summary>
    private void ApplyExtremeGate()
    {
        bool unlocked = ChaosMeta.State.ExtremeUnlocked;
        SegExtreme.IsEnabled = unlocked;
        SegExtreme.Content = unlocked ? "Extreme" : "Extreme 🔒";
        if (!unlocked && SegExtreme.IsChecked == true)
            SetSegment(GrpDifficulty, "Hard");
    }

    private void LoadDefaults()
    {
        SetSegment(GrpDifficulty, "Easy");
        SetSegment(GrpLength, "180");
        SetSegment(GrpMotion, "Mixed");
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
        s.ChaosStartingShields = _shields;
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
    }

    // ============================ run-setup controls ============================

    private void Segment_Click(object sender, RoutedEventArgs e)
    {
        var btn = (ToggleButton)sender;
        if (!btn.IsEnabled) { btn.IsChecked = false; return; }   // locked (Extreme)
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
    }

    private void BtnRandomize_Click(object sender, RoutedEventArgs e)
    {
        var diffs = ChaosMeta.State.ExtremeUnlocked
            ? new[] { "Easy", "Medium", "Hard", "Extreme" }
            : new[] { "Easy", "Medium", "Hard" };
        SetSegment(GrpDifficulty, diffs[_rng.Next(diffs.Length)]);
        SetSegment(GrpLength, new[] { "120", "180", "300" }[_rng.Next(3)]);
        SetSegment(GrpMotion, new[] { "Mixed", "FloatUp", "RainDown", "RoamBounce" }[_rng.Next(4)]);
        var pool = GrpPool.Children.OfType<ToggleButton>().ToList();
        foreach (var t in pool) t.IsChecked = _rng.NextDouble() < 0.6;
        if (!pool.Any(t => t.IsChecked == true)) pool[0].IsChecked = true;
    }

    private void BtnDefaults_Click(object sender, RoutedEventArgs e) { LoadDefaults(); ApplyExtremeGate(); }

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
