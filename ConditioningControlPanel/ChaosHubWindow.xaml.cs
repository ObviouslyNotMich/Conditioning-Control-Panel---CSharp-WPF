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
/// The Rabbit Hole hub ("The Warren"): opened from the Lab card. Four tabs:
/// LOADOUT (pocket slots + the whole collection as clickable tiles), ENHANCEMENTS
/// (unlock/deepen skills &amp; accessories), THE DESCENT (run setup + Habit training —
/// the always-on passives), IMPROVEMENTS (future meta unlocks + start-mantra picker +
/// stats and diary). Improvements unlocks after the first completed descent.
/// </summary>
public partial class ChaosHubWindow : Window
{
    private static readonly Random _rng = new();
    private int _waves = 5;

    /// <summary>The open Warren, if any — lets the loadout sidebar push unequips back into it.</summary>
    public static ChaosHubWindow? Current { get; private set; }

    private bool _uiSoundsReady;   // suppress cues while the ctor builds the initial view

    public ChaosHubWindow()
    {
        InitializeComponent();
        Current = this;
        // A quiet wet click under every button press; specific cues (equip/unlock/…) layer on top.
        AddHandler(ButtonBase.ClickEvent,
            new RoutedEventHandler((_, _) => { if (_uiSoundsReady) ChaosSfx.Play("ui_click", 0.3f); }), true);
        // The loadout sidebar opens beside the Warren so the pockets read at a glance while
        // equipping; it follows this window's lifetime (a started run swaps in the real HUD).
        Closed += (_, _) => { Current = null; App.Chaos?.CloseLoadoutSidebar(); };
        App.Chaos?.ShowLoadoutSidebar();
        TitleBar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } } };

        LoadFromSettings();
        BuildHabits();
        BuildLifetimeBoons();
        BuildLoadoutTiles();
        BuildImprovements();
        BuildMantras();
        BuildDiary();
        RefreshTopBar();
        RefreshStats();
        ApplyUnlocks();
        LoadBanner();
        ShowTab("loadout");
        _uiSoundsReady = true;
    }

    private void LoadBanner()
    {
        var src = ChaosArt.ResolveBanner();
        if (src != null) { BannerImage.Source = src; BannerImage.Visibility = Visibility.Visible; }
    }

    // ============================ tabs / gating ============================

    private void ApplyUnlocks()
    {
        // Loadout/Enhancements/Descent are open from the first visit (the Sparks cost is the
        // real gate, and run setup must be reachable before run #1). The bench waits one descent.
        int runs = ChaosMeta.State.RunsCompleted;
        TabLoadout.IsEnabled = true;
        TabEnhance.IsEnabled = true;
        TabRun.IsEnabled = true;
        SetTabEnabled(TabImprove, runs >= ChaosMeta.UNLOCK_STATS_RUNS);
    }

    private static void SetTabEnabled(ToggleButton tab, bool enabled)
    {
        tab.IsEnabled = enabled;
        tab.ToolTip = enabled ? null : "finish a descent to unlock";
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton tb && tb.IsEnabled) ShowTab(tb.Tag?.ToString() ?? "loadout");
        else if (sender is ToggleButton tb2) tb2.IsChecked = false;
    }

    private void ShowTab(string tag)
    {
        PanelLoadout.Visibility = tag == "loadout" ? Visibility.Visible : Visibility.Collapsed;
        PanelEnhance.Visibility = tag == "enhance" ? Visibility.Visible : Visibility.Collapsed;
        PanelHabits.Visibility  = tag == "habits"  ? Visibility.Visible : Visibility.Collapsed;
        PanelRun.Visibility     = tag == "run"     ? Visibility.Visible : Visibility.Collapsed;
        PanelImprove.Visibility = tag == "improve" ? Visibility.Visible : Visibility.Collapsed;

        TabLoadout.IsChecked = tag == "loadout";
        TabEnhance.IsChecked = tag == "enhance";
        TabHabits.IsChecked  = tag == "habits";
        TabRun.IsChecked     = tag == "run";
        TabImprove.IsChecked = tag == "improve";

        TxtHint.Text = tag switch
        {
            "loadout" => "click a tile to slip it into a pocket. + takes you where it's sold.",
            "enhance" => "spend your drops. deepen what you like.",
            "habits"  => "train once, keep forever. switch them on or off between descents.",
            "run"     => "dress up the fall, then FALL IN.",
            "improve" => "the bench, the mantras, the diary.",
            _ => "",
        };
    }

    /// <summary>A + tile wants to take the player shopping.</summary>
    private void JumpToTab(string tag) => ShowTab(tag);

    /// <summary>External navigation (the loadout sidebar's empty "+" tiles): switch tab and
    /// bring the Warren forward.</summary>
    public void NavigateTo(string tag)
    {
        ShowTab(tag);
        try { Activate(); } catch { }
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
        StTimeUnder.Text = FormatPlaytime(s.TotalRunSeconds);
        StBestScore.Text = s.BestScore.ToString("N0");
        StBestCombo.Text = s.BestCombo.ToString("N0");
        StDefused.Text = s.TotalDefused.ToString("N0");
    }

    private static string FormatPlaytime(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{t.Minutes}m {t.Seconds}s";
    }

    // ============================ habits (The Descent tab) ============================

    private static Color BranchColor(ChaosBranch b) => b switch
    {
        ChaosBranch.Control => Color.FromRgb(0x49, 0xB6, 0xE8),
        ChaosBranch.Greed   => Color.FromRgb(0xE8, 0xB4, 0x43),
        ChaosBranch.Depth   => Color.FromRgb(0x8B, 0x5C, 0xF6),
        _                   => Color.FromRgb(0xE8, 0x43, 0x93)
    };

    private static string BranchLabel(ChaosBranch b) => b switch
    {
        ChaosBranch.Control => "RESTRAINT",
        ChaosBranch.Greed   => "CRAVING",
        _                   => "DEPTH",
    };

    /// <summary>One grouped list (RESTRAINT / CRAVING / DEPTH) of the trainable passives —
    /// untrained rows sell, trained rows toggle on/off.</summary>
    private void BuildHabits()
    {
        // One flat list, one card style: the single-rank habits first (branch order),
        // then the leveled ones (Utility lifetime boons — Rabbit's Foot etc.), unlabeled.
        HabitsHost.Children.Clear();
        foreach (var u in ChaosUpgrades.All)
            HabitsHost.Children.Add(BuildUpgradeRow(u));
        foreach (var b in ChaosLifetimeBoons.InCategory(ChaosBoonCategory.Utility))
            HabitsHost.Children.Add(BuildLifetimeBoonRow(b));
    }

    /// <summary>One habit card, in the same dress as the boon rows (72px art, big card,
    /// gold edge while switched on) — the Habits tab is one cohesive list.</summary>
    private Border BuildUpgradeRow(ChaosUpgrade u)
    {
        bool owned = ChaosMeta.IsOwned(u.Id);
        bool afford = ChaosMeta.CanAfford(u.Id);
        bool on = owned && ChaosMeta.IsUpgradeActive(u.Id);
        var accent = BranchColor(u.Branch);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // ---- icon (real art if present, else a branch-tinted square with the glyph) ----
        var iconSrc = u.IconPath != null ? ChaosArt.TryLoad(u.IconPath) : ChaosArt.Resolve("upgrades", u.Id);
        FrameworkElement icon;
        if (iconSrc != null)
            icon = ArtIcon(iconSrc, 72, 14, accent, ring: 3);
        else
            icon = new Border
            {
                Width = 72, Height = 72, CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(70, accent.R, accent.G, accent.B)),
                BorderBrush = new SolidColorBrush(accent), BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = u.Glyph, FontSize = 34, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 0, 12, 0);
        icon.Opacity = owned ? 1.0 : 0.5;
        ChaosTips.Attach(icon, u.Name, u.Desc, accent: accent);
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        // ---- middle: name + desc + branch tag ----
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        mid.Children.Add(new TextBlock { Text = u.Name, Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold });
        mid.Children.Add(new TextBlock { Text = u.Desc, Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xB8, 0xB8)), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        mid.Children.Add(new TextBlock
        {
            Text = BranchLabel(u.Branch).ToLowerInvariant(),
            Foreground = new SolidColorBrush(Color.FromArgb(0xAA, accent.R, accent.G, accent.B)),
            FontSize = 10.5, FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 6, 0, 0)
        });
        Grid.SetColumn(mid, 1);
        grid.Children.Add(mid);

        // ---- right: ON badge + on/off toggle, or the Train buy button ----
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Width = 132 };
        if (owned)
        {
            if (on)
                right.Children.Add(new TextBlock
                {
                    Text = "ON ✓",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)),
                    FontSize = 11, FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 0, 0, 4)
                });
            var toggle = new Button
            {
                Style = (Style)FindResource("Stepper"),
                Content = on ? "switch off" : "switch on",
                Tag = u.Id,
                Width = double.NaN, Height = double.NaN, MinWidth = 112,
                FontSize = 12.5,
                Padding = new Thickness(14, 8, 14, 8),
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            Pillify(toggle);
            ChaosTips.Attach(toggle, u.Name, on ? "switched on — shapes your next descent." : "switched off — sits out the descent.", accent: accent);
            toggle.Click += HabitToggle_Click;
            right.Children.Add(toggle);
        }
        else
        {
            right.Children.Add(BuyButton($"Train  ✦{u.Cost}", u.Id, afford, Buy_Click));
        }
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        return new Border
        {
            Child = grid,
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1A, 0x36)),
            BorderBrush = on
                ? new SolidColorBrush(Color.FromArgb(180, 0xFF, 0xD7, 0x00))
                : new SolidColorBrush(Color.FromArgb(owned ? (byte)90 : (byte)40, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(on ? 3 : 2),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 12)
        };
    }

    private void Buy_Click(object sender, RoutedEventArgs e)
    {
        var id = (sender as Button)?.Tag?.ToString();
        if (string.IsNullOrEmpty(id)) return;
        if (ChaosMeta.TryPurchase(id!))
        {
            ChaosSfx.Play("ui_unlock", 0.55f);
            if (id == "extreme_tier") ApplyExtremeGate();
            BuildHabits();
            BuildLoadoutTiles();   // the habit grid lights up
            RefreshTopBar();
            RefreshStats();
        }
        else ChaosSfx.Play("ui_denied", 0.45f);
    }

    private void HabitToggle_Click(object sender, RoutedEventArgs e)
    {
        var id = (sender as Button)?.Tag?.ToString();
        if (string.IsNullOrEmpty(id)) return;
        ChaosMeta.SetUpgradeActive(id!, !ChaosMeta.IsUpgradeActive(id!));
        ChaosSfx.Play(ChaosMeta.IsUpgradeActive(id!) ? "ui_equip" : "ui_unequip", 0.45f);
        BuildHabits();
        BuildLoadoutTiles();
        App.Chaos?.NotifyLoadoutChanged();   // sidebar CONDITIONING list mirrors the switch
    }

    // ===================== lifetime boons (skills/accessories/utility) =====================

    private static readonly Color BoonAccent = Color.FromRgb(0xE8, 0x43, 0x93);

    private void BuildLifetimeBoons()
    {
        BuildBoonShelf(BoonHostSkills, ChaosBoonCategory.Skill);
        BuildBoonShelf(BoonHostAccessories, ChaosBoonCategory.Accessory);
        // Utility charms train on the HABITS tab (BuildHabits) — they're habits in spirit.
    }

    private void BuildBoonShelf(Panel host, ChaosBoonCategory cat)
    {
        host.Children.Clear();
        var boons = ChaosLifetimeBoons.InCategory(cat).ToList();
        if (boons.Count == 0)
        {
            host.Children.Add(new Border
            {
                Style = (Style)FindResource("CardStyle"),
                Child = new TextBlock
                {
                    Text = "something is being prepared for you.",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0xA0, 0xC0)),
                    FontSize = 12, TextWrapping = TextWrapping.Wrap
                }
            });
            return;
        }
        foreach (var b in boons) host.Children.Add(BuildLifetimeBoonRow(b));
    }

    private Border BuildLifetimeBoonRow(ChaosLifetimeBoon b)
    {
        int level = ChaosMeta.BoonLevel(b.Id);
        bool unlocked = level >= 1;
        bool active = ChaosMeta.IsBoonActive(b.Id);
        bool maxed = unlocked && level >= b.MaxLevel;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // ---- icon (real art if present, else a tinted square with the glyph) ----
        var iconSrc = ChaosArt.Resolve("boons", b.Id);
        FrameworkElement icon;
        if (iconSrc != null)
            icon = ArtIcon(iconSrc, 72, 14, BoonAccent, ring: 3);
        else
            icon = new Border
            {
                Width = 72, Height = 72, CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(70, BoonAccent.R, BoonAccent.G, BoonAccent.B)),
                BorderBrush = new SolidColorBrush(BoonAccent), BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = b.Glyph, FontSize = 34, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 0, 12, 0);
        icon.Opacity = unlocked ? 1.0 : 0.5;
        ChaosTips.Attach(icon, unlocked ? $"{b.Name} · L{level}" : b.Name, b.Desc,
            string.IsNullOrEmpty(b.CapstoneDesc) ? null : "max: " + b.CapstoneDesc);
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        // ---- middle: name + desc + level pips + value ----
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        mid.Children.Add(new TextBlock { Text = b.Name, Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold });
        mid.Children.Add(new TextBlock { Text = b.Desc, Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xB8, 0xB8)), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });

        // Active-use skills carry their trigger: the keybind (when equipped) or a generic hint.
        if (b.IsActiveUse)
        {
            string key = App.Settings?.Current?.ChaosAccessoryKey1 ?? "Q";
            string useHint = b.UseCooldownSec > 0 ? $"{b.UseCooldownSec:0}s cooldown" : "limited uses";
            mid.Children.Add(new TextBlock
            {
                Text = active ? $"ACTIVE · fires on {key} mid-descent · {useHint}"
                              : $"ACTIVE · fires on your skill key mid-descent · {useHint}",
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)),
                FontSize = 10.5, FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 3, 0, 0)
            });
        }

        // Capstone teaser: dim until the final rank is bought, gold once it's live.
        if (!string.IsNullOrEmpty(b.CapstoneDesc))
            mid.Children.Add(new TextBlock
            {
                Text = "max: " + b.CapstoneDesc,
                Foreground = new SolidColorBrush(maxed ? Color.FromRgb(0xFF, 0xD7, 0x00) : Color.FromArgb(0x90, 0x8A, 0x86, 0xB8)),
                FontSize = 11,
                FontStyle = maxed ? FontStyles.Normal : FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0)
            });

        var pips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        for (int i = 1; i <= b.MaxLevel; i++)
            pips.Children.Add(new TextBlock
            {
                Text = i <= level ? "●" : "○",
                Foreground = new SolidColorBrush(i <= level ? BoonAccent : Color.FromArgb(0x66, 0xB8, 0xB8, 0xD0)),
                FontSize = 13, Margin = new Thickness(0, 0, 3, 0)
            });
        string valueText = unlocked ? string.Format(b.ValueLabel, b.ValueAt(level)) : "locked";
        pips.Children.Add(new TextBlock
        {
            Text = $"   {valueText}",
            Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x5C, 0xF6)),
            FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center
        });
        mid.Children.Add(pips);
        Grid.SetColumn(mid, 1);
        grid.Children.Add(mid);

        // ---- right: on/off toggle + unlock/upgrade ----
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Width = 132 };

        // Utility rows live on the Habits tab and speak habit language (switch on/off);
        // Skills/Accessories keep the pocketed Equip semantics.
        bool habitVoice = b.Category == ChaosBoonCategory.Utility;
        if (unlocked)
        {
            // Equip semantics instead of an ambiguous ON/OFF: the badge shows the STATE,
            // the button is always the ACTION. Pockets cap Skills/Accessories at 2 each.
            if (active)
                right.Children.Add(new TextBlock
                {
                    Text = habitVoice ? "ON ✓" : "EQUIPPED ✓",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)),
                    FontSize = 11, FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 0, 0, 4)
                });
            bool pocketFree = active || ChaosMeta.HasFreePocket(b.Category);
            var equip = new Button
            {
                Style = (Style)FindResource("Stepper"),
                Content = active ? (habitVoice ? "switch off" : "Unequip")
                                 : pocketFree ? (habitVoice ? "switch on" : "Equip") : "pockets full",
                Tag = b.Id,
                IsEnabled = pocketFree,
                Width = double.NaN,
                Height = double.NaN,
                MinWidth = 112,
                FontSize = 12.5,
                Padding = new Thickness(14, 8, 14, 8),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 0, 8)
            };
            Pillify(equip);
            equip.Click += BoonEquip_Click;
            right.Children.Add(equip);
        }

        if (!unlocked)
            right.Children.Add(BuyButton($"Unlock  ✦{b.UnlockCost}", b.Id, ChaosMeta.CanAffordUnlock(b.Id), BoonUnlock_Click));
        else if (maxed)
            right.Children.Add(new TextBlock { Text = "MAX  ✓", Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0xE0, 0x96)), FontSize = 13, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right });
        else
        {
            int cost = ChaosMeta.NextUpgradeCostOf(b.Id) ?? 0;
            right.Children.Add(BuyButton($"Upgrade  ✦{cost}", b.Id, ChaosMeta.CanAffordUpgrade(b.Id), BoonUpgrade_Click));
        }
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        return new Border
        {
            Child = grid,
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1A, 0x36)),
            // Equipped cards read at a glance: gold edge; otherwise the usual pink, dimmed when locked.
            BorderBrush = active
                ? new SolidColorBrush(Color.FromArgb(180, 0xFF, 0xD7, 0x00))
                : new SolidColorBrush(Color.FromArgb(unlocked ? (byte)90 : (byte)40, BoonAccent.R, BoonAccent.G, BoonAccent.B)),
            BorderThickness = new Thickness(active ? 3 : 2),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 12)
        };
    }

    private Button BuyButton(string text, string id, bool afford, RoutedEventHandler onClick)
    {
        var btn = new Button
        {
            Content = text,
            Tag = id,
            Padding = new Thickness(20, 10, 20, 10),
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = afford ? new SolidColorBrush(BoonAccent) : new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            Foreground = afford ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x88, 0xA0, 0xA0)),
            BorderThickness = new Thickness(0),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Cursor = afford ? Cursors.Hand : Cursors.Arrow,
            IsEnabled = afford
        };
        Pillify(btn);
        btn.Click += onClick;
        return btn;
    }

    /// <summary>Round a code-built button's corners (same implicit-Border-style trick the
    /// Lab's FALL IN button uses — the default button chrome is a Border).</summary>
    private static void Pillify(Button b, double radius = 11)
    {
        var s = new Style(typeof(Border));
        s.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(radius)));
        b.Resources.Add(typeof(Border), s);
    }

    private void AccKey_Changed(object sender, SelectionChangedEventArgs e)
    {
        var s = App.Settings?.Current;
        if (s == null) return;
        if (CmbAccKey1?.SelectedItem is string k1) s.ChaosAccessoryKey1 = k1;
        if (CmbAccKey2?.SelectedItem is string k2) s.ChaosAccessoryKey2 = k2;
    }

    private void BoonEquip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            ChaosMeta.SetBoonActive(id, !ChaosMeta.IsBoonActive(id));
            ChaosSfx.Play(ChaosMeta.IsBoonActive(id) ? "ui_equip" : "ui_unequip", 0.45f);
            AfterBoonChange();   // rebuild shelves (badges + full-pocket states) and the Pockets tab
        }
    }

    private void BoonUnlock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            if (ChaosMeta.TryUnlockBoon(id)) { ChaosSfx.Play("ui_unlock", 0.55f); AfterBoonChange(); }
            else ChaosSfx.Play("ui_denied", 0.45f);
        }
    }

    private void BoonUpgrade_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            if (ChaosMeta.TryUpgradeBoon(id)) { ChaosSfx.Play("ui_deepen", 0.5f); AfterBoonChange(); }
            else ChaosSfx.Play("ui_denied", 0.45f);
        }
    }

    private void AfterBoonChange()
    {
        BuildLifetimeBoons();
        BuildHabits();          // charms render on the Habits tab
        BuildLoadoutTiles();
        RefreshTopBar();
        RefreshStats();
        App.Chaos?.NotifyLoadoutChanged();   // mirror into the loadout sidebar, if up
    }

    /// <summary>The loadout sidebar unequipped something — re-render shelves/tiles here
    /// WITHOUT notifying the sidebar back (it already refreshed itself).</summary>
    public void RefreshAfterExternalLoadoutChange()
    {
        BuildLifetimeBoons();
        BuildLoadoutTiles();
        RefreshTopBar();
    }

    // ============================ loadout tab (tile grids) ============================

    private const double TILE = 96;
    private static readonly Color Gold = Color.FromRgb(0xFF, 0xD7, 0x00);

    private enum TileState { Equipped, Owned, Locked, Empty }

    /// <summary>The whole glance page: pocket slots + accessory/skill/habit tile grids.</summary>
    private void BuildLoadoutTiles()
    {
        // ---- pocket slots (big tiles, one group per category) ----
        PocketSlotsHost.Children.Clear();
        PocketSlotsHost.Children.Add(PocketGroup("SKILL", ChaosBoonCategory.Skill));
        PocketSlotsHost.Children.Add(PocketGroup("ACCESSORY", ChaosBoonCategory.Accessory));

        // ---- collections ----
        FillCategoryTiles(TilesAccessories, ChaosBoonCategory.Accessory, padTo: 8);
        FillCategoryTiles(TilesSkills, ChaosBoonCategory.Skill, padTo: 8);
        TxtAccCount.Text = $"{ChaosMeta.EquippedCountIn(ChaosBoonCategory.Accessory)}/{ChaosMeta.SlotsFor(ChaosBoonCategory.Accessory)} equipped";
        TxtSkillCount.Text = $"{ChaosMeta.EquippedCountIn(ChaosBoonCategory.Skill)}/{ChaosMeta.SlotsFor(ChaosBoonCategory.Skill)} equipped";

        // ---- habits 4x4 (trained = on/off toggle; click an untrained one to go train it) ----
        TilesHabits.Children.Clear();
        var habits = ChaosUpgrades.All.ToList();
        int trained = 0, switchedOn = 0;
        foreach (var u in habits)
        {
            string id = u.Id;
            bool owned = ChaosMeta.IsOwned(id);
            bool on = owned && ChaosMeta.IsUpgradeActive(id);
            if (owned) trained++;
            if (on) switchedOn++;
            Action onClick = owned
                ? () => { ChaosMeta.SetUpgradeActive(id, !ChaosMeta.IsUpgradeActive(id)); BuildHabits(); BuildLoadoutTiles(); App.Chaos?.NotifyLoadoutChanged(); }
                : () => JumpToTab("habits");
            TilesHabits.Children.Add(LoadoutTile(u.Glyph, u.Name, u.Desc,
                on ? "click to switch off" : owned ? "click to switch on" : $"train for ✦{u.Cost} in HABITS",
                BranchColor(u.Branch),
                on ? TileState.Equipped : owned ? TileState.Owned : TileState.Locked,
                onClick,
                cornerBadge: on ? "✓" : null,
                art: u.IconPath != null ? ChaosArt.TryLoad(u.IconPath) : ChaosArt.Resolve("upgrades", id)));
        }
        // Charms (Utility lifetime boons — Rabbit's Foot etc.) live with the habits:
        // leveled, always-on once worn, toggled exactly like a trained habit.
        var charms = ChaosLifetimeBoons.InCategory(ChaosBoonCategory.Utility).ToList();
        foreach (var b in charms)
        {
            string bid = b.Id;
            bool unlocked = ChaosMeta.IsBoonUnlocked(bid);
            bool active = ChaosMeta.IsBoonActive(bid);
            if (unlocked) trained++;
            if (active) switchedOn++;
            Action onClick = unlocked
                ? () =>
                  {
                      ChaosMeta.SetBoonActive(bid, !ChaosMeta.IsBoonActive(bid));
                      ChaosSfx.Play(ChaosMeta.IsBoonActive(bid) ? "ui_equip" : "ui_unequip", 0.45f);
                      BuildHabits(); BuildLoadoutTiles(); App.Chaos?.NotifyLoadoutChanged();
                  }
                : () => JumpToTab("habits");
            TilesHabits.Children.Add(LoadoutTile(b.Glyph, unlocked ? $"{b.Name} · L{ChaosMeta.BoonLevel(bid)}" : b.Name, b.Desc,
                active ? "click to switch off" : unlocked ? "click to switch on" : $"unlock for ✦{b.UnlockCost} in HABITS",
                BoonAccent,
                active ? TileState.Equipped : unlocked ? TileState.Owned : TileState.Locked,
                onClick,
                cornerBadge: active ? "✓" : null,
                art: ChaosArt.Resolve("boons", bid)));
        }
        int shown = habits.Count + charms.Count;
        int target = Math.Max(16, ((shown + 3) / 4) * 4);
        for (int i = shown; i < target; i++)
            TilesHabits.Children.Add(LoadoutTile("+", "a habit not yet formed",
                "more training arrives in a later fitting.", null,
                Color.FromRgb(0xB8, 0xB8, 0xD0), TileState.Empty, null));
        TxtHabitCount.Text = $"{switchedOn} on · {trained}/{shown} trained";
    }

    /// <summary>One labelled pocket column: equipped boon as a big gold tile, plus + tiles for free slots.</summary>
    private FrameworkElement PocketGroup(string label, ChaosBoonCategory cat)
    {
        var col = new StackPanel { Margin = new Thickness(0, 0, 30, 0) };
        col.Children.Add(new TextBlock
        {
            Text = label, Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x86, 0xB8)),
            FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold, FontSize = 11,
            Margin = new Thickness(0, 0, 0, 6)
        });
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var equipped = ChaosLifetimeBoons.InCategory(cat).Where(b => ChaosMeta.IsBoonActive(b.Id)).ToList();
        foreach (var b in equipped)
        {
            string id = b.Id;
            var cell = LoadoutTile(b.Glyph, $"{b.Name} · L{ChaosMeta.BoonLevel(b.Id)}", b.Desc,
                "click to unequip", BoonAccent, TileState.Equipped,
                () => { ChaosMeta.SetBoonActive(id, false); ChaosSfx.Play("ui_unequip", 0.45f); AfterBoonChange(); },
                art: ChaosArt.Resolve("boons", b.Id), size: 114);
            cell.Margin = new Thickness(0, 0, 24, 0);
            row.Children.Add(cell);
        }
        for (int i = equipped.Count; i < ChaosMeta.SlotsFor(cat); i++)
        {
            var cell = LoadoutTile("+", $"empty {label.ToLowerInvariant()} pocket",
                "pick one from the shelf below, or go shopping in ENHANCEMENTS.", null,
                BoonAccent, TileState.Empty, () => JumpToTab("enhance"), size: 114, caption: "empty");
            cell.Margin = new Thickness(0, 0, 24, 0);
            row.Children.Add(cell);
        }
        col.Children.Add(row);
        return col;
    }

    /// <summary>A category's collection as tiles: equipped gold, owned pink (click = equip,
    /// swapping out the current occupant), locked dim (click = go shopping), padded with
    /// placeholder + tiles to full rows.</summary>
    private void FillCategoryTiles(Panel host, ChaosBoonCategory cat, int padTo)
    {
        host.Children.Clear();
        var boons = ChaosLifetimeBoons.InCategory(cat).ToList();
        foreach (var b in boons)
        {
            string id = b.Id;
            int level = ChaosMeta.BoonLevel(id);
            bool unlocked = level >= 1;
            bool active = ChaosMeta.IsBoonActive(id);
            var state = active ? TileState.Equipped : unlocked ? TileState.Owned : TileState.Locked;
            Action onClick = active ? () => { ChaosMeta.SetBoonActive(id, false); ChaosSfx.Play("ui_unequip", 0.45f); AfterBoonChange(); }
                : unlocked ? () => EquipSwapping(id, cat)
                : () => JumpToTab("enhance");
            string extra = active ? "click to unequip"
                : unlocked ? "click to equip"
                : $"unlock for ✦{b.UnlockCost} in ENHANCEMENTS";
            host.Children.Add(LoadoutTile(b.Glyph, unlocked ? $"{b.Name} · L{level}" : b.Name,
                b.Desc, extra, BoonAccent, state, onClick,
                cornerBadge: active ? "★" : null, art: ChaosArt.Resolve("boons", id)));
        }
        int target = Math.Max(padTo, ((boons.Count + 3) / 4) * 4);
        for (int i = boons.Count; i < target; i++)
            host.Children.Add(LoadoutTile("+",
                cat == ChaosBoonCategory.Skill ? "another skill is being stitched" : "another accessory is being stitched",
                "it'll hang here when it's ready.", null,
                Color.FromRgb(0xB8, 0xB8, 0xD0), TileState.Empty, null));
    }

    /// <summary>Equip into a full 1-slot pocket by quietly swapping the current occupant out.</summary>
    private void EquipSwapping(string id, ChaosBoonCategory cat)
    {
        if (!ChaosMeta.HasFreePocket(cat))
        {
            var current = ChaosLifetimeBoons.InCategory(cat).FirstOrDefault(b => ChaosMeta.IsBoonActive(b.Id));
            if (current != null) ChaosMeta.SetBoonActive(current.Id, false);
        }
        ChaosMeta.SetBoonActive(id, true);
        ChaosSfx.Play("ui_equip", 0.45f);
        AfterBoonChange();
    }

    private FrameworkElement LoadoutTile(string glyph, string title, string? desc, string? extra, Color accent,
                               TileState state, Action? onClick, string? cornerBadge = null,
                               ImageSource? art = null, double size = TILE, string? caption = null)
    {
        // Rounded clip so the square art can't poke past the ring corners (Border doesn't
        // clip children to its CornerRadius; the tile is fixed-size so a geometry works).
        var content = new Grid
        {
            Clip = new RectangleGeometry(new Rect(0, 0, size, size), 12, 12)
        };
        if (art != null && state != TileState.Empty)
            content.Children.Add(new Image { Source = art, Stretch = Stretch.UniformToFill, Opacity = state == TileState.Locked ? 0.35 : 1.0 });
        else
            content.Children.Add(new TextBlock
            {
                Text = glyph,
                FontSize = size >= 114 ? 46 : 36,
                Foreground = Brushes.White,
                Opacity = state switch { TileState.Locked => 0.35, TileState.Empty => 0.4, _ => 1.0 },
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
        var ringBrush = state switch
        {
            TileState.Equipped => new SolidColorBrush(Color.FromArgb(200, Gold.R, Gold.G, Gold.B)),
            TileState.Owned    => new SolidColorBrush(accent),
            TileState.Locked   => new SolidColorBrush(Color.FromArgb(60, accent.R, accent.G, accent.B)),
            _                  => new SolidColorBrush(Color.FromArgb(0x50, 0xB8, 0xB8, 0xD0)),
        };
        // The ring rides ABOVE the art (full-bleed sprites were burying a thin behind-border)
        // and ABOVE the badge — always the topmost layer of the tile.
        content.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderBrush = ringBrush,
            BorderThickness = new Thickness(state == TileState.Equipped ? 4 : 3.5),
            IsHitTestVisible = false,
        });
        if (cornerBadge != null)
            content.Children.Add(new TextBlock
            {
                Text = cornerBadge, FontSize = 15, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Gold),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 6, 3)
            });

        var tile = new Border
        {
            Width = size, Height = size,
            CornerRadius = new CornerRadius(12),
            Child = content,
            ClipToBounds = true,
            Background = state switch
            {
                TileState.Equipped => new SolidColorBrush(Color.FromArgb(80, accent.R, accent.G, accent.B)),
                TileState.Owned    => new SolidColorBrush(Color.FromArgb(45, accent.R, accent.G, accent.B)),
                TileState.Locked   => new SolidColorBrush(Color.FromRgb(0x22, 0x1F, 0x40)),
                _                  => Brushes.Transparent,
            },
            BorderBrush = ringBrush,
            BorderThickness = new Thickness(0),
        };

        // Name under the tile — locked/placeholder tiles keep their mystery.
        caption ??= state is TileState.Locked or TileState.Empty ? "???" : title.Split(" · ")[0];
        var label = new TextBlock
        {
            Text = caption,
            FontSize = 12,
            FontWeight = state == TileState.Equipped ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = state switch
            {
                TileState.Equipped => new SolidColorBrush(Gold),
                TileState.Owned    => Brushes.White,
                _                  => new SolidColorBrush(Color.FromArgb(0x80, 0xB8, 0xB8, 0xD0)),
            },
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = size + 36,
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var cell = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 22),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Cursor = onClick != null ? Cursors.Hand : Cursors.Arrow,
            Background = Brushes.Transparent,
        };
        cell.Children.Add(tile);
        cell.Children.Add(label);
        if (onClick != null) cell.MouseLeftButtonDown += (_, _) => onClick();
        ChaosTips.Attach(cell, title, desc, extra, accent);
        return cell;
    }

    // ============================ improvements tab ============================

    /// <summary>The seamstress's bench: permanent meta unlocks. Placeholder rows until the
    /// second-pocket pass lands — the shelf exists so they have somewhere to live.</summary>
    private void BuildImprovements()
    {
        ImprovementsHost.Children.Clear();
        ImprovementsHost.Children.Add(ImprovementRow("👝", "a second skill pocket", "not yet sewn. the seamstress takes her time."));
        ImprovementsHost.Children.Add(ImprovementRow("👝", "a second accessory pocket", "not yet sewn. she only has two hands."));
    }

    private Border ImprovementRow(string glyph, string name, string desc)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = glyph, FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Opacity = 0.5 });
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        mid.Children.Add(new TextBlock { Text = name, Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xB8)), FontSize = 12, FontWeight = FontWeights.SemiBold });
        mid.Children.Add(new TextBlock { Text = desc, Foreground = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x90)), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        row.Children.Add(mid);
        return new Border
        {
            Child = row,
            Background = new SolidColorBrush(Color.FromRgb(0x22, 0x1F, 0x40)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(30, 0xE8, 0x43, 0x93)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 6),
            Opacity = 0.8
        };
    }

    // ============================ mantras box ============================

    private void BuildMantras()
    {
        MantrasHost.Children.Clear();
        foreach (var b in ChaosBoonPool.All)
            MantrasHost.Children.Add(MantraRow(b));
    }

    /// <summary>One mantra/sin row: ??? until met in a draft; discovered mantras are clickable
    /// to set/clear the start mantra (gold ring + ★ on the whispered one). Sins are listed but
    /// can only be taken mid-fall, never chosen.</summary>
    private Border MantraRow(ChaosBoon b)
    {
        bool seen = ChaosMeta.IsDiscovered("boon:" + b.Id);
        bool isStart = ChaosMeta.State.EquippedStartBoon == b.Id;
        bool pickable = seen && !b.IsCurse;
        var accent = b.IsCurse ? Color.FromRgb(0xFF, 0x8A, 0x8A) : Color.FromRgb(0x9C, 0xE8, 0xA0);

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new Border
        {
            Width = 39, Height = 39, CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(seen ? Color.FromArgb(60, accent.R, accent.G, accent.B) : Color.FromArgb(40, 120, 120, 140)),
            BorderBrush = new SolidColorBrush(seen ? accent : Color.FromRgb(90, 90, 110)), BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = seen ? (b.IsCurse ? "☠" : "◈") : "?", Foreground = new SolidColorBrush(seen ? accent : Color.FromRgb(0x88, 0x88, 0xA0)), FontSize = 19, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(icon, 0);
        row.Children.Add(icon);

        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        mid.Children.Add(new TextBlock { Text = seen ? b.Name : "???", Foreground = seen ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x90)), FontSize = 12, FontWeight = FontWeights.SemiBold });
        mid.Children.Add(new TextBlock { Text = seen ? b.Desc : "hazy. go back down and look closer.", Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xB8, 0xB8)), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(mid, 1);
        row.Children.Add(mid);

        if (seen)
        {
            var badge = new TextBlock
            {
                Text = isStart ? "start ★" : b.IsCurse ? "taken, never chosen" : "set start",
                Foreground = isStart ? new SolidColorBrush(Gold) : new SolidColorBrush(Color.FromArgb(0x90, 0xB8, 0xB8, 0xD0)),
                FontSize = 10.5, FontWeight = isStart ? FontWeights.Bold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(badge, 2);
            row.Children.Add(badge);
        }

        var card = new Border
        {
            Child = row,
            Background = new SolidColorBrush(Color.FromRgb(0x22, 0x1F, 0x40)),
            BorderBrush = isStart
                ? new SolidColorBrush(Color.FromArgb(190, Gold.R, Gold.G, Gold.B))
                : new SolidColorBrush(Color.FromArgb(seen ? (byte)70 : (byte)25, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(isStart ? 3 : 2),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 6),
            Cursor = pickable ? Cursors.Hand : Cursors.Arrow
        };
        if (seen)
            ChaosTips.Attach(card, b.Name, b.Desc,
                b.IsCurse ? "a sin. it can only be taken mid-fall." : isStart ? "whispered on the way down. click to fall in bare." : "click to whisper it on the way down.",
                accent);
        if (pickable)
            card.MouseLeftButtonDown += (_, _) =>
            {
                ChaosMeta.EquipStartBoon(isStart ? null : b.Id);
                BuildMantras();
            };
        return card;
    }

    // ============================ diary (bubbles met) ============================

    private void BuildDiary()
    {
        DiaryHost.Children.Clear();
        FillDiaryRows(DiaryHost, 270);
    }

    /// <summary>Every diary entry into <paramref name="host"/> — shared by the half-width
    /// Improvements box (narrow wrap) and the pop-out reader (roomier).</summary>
    private void FillDiaryRows(Panel host, double maxWidth)
    {
        foreach (var v in ChaosBubbleVariants.All)
            host.Children.Add(CodexRow("bubble:" + v.Id, v.Name, ChaosBubbleVariants.DescriptionFor(v.Id),
                ChaosArt.Resolve("bubbles", v.Id), "●", Color.FromRgb(v.Tint.R, v.Tint.G, v.Tint.B), maxWidth));
        // Not part of the weighted pool table; listed explicitly.
        host.Children.Add(CodexRow("bubble:darter", "White Rabbit", ChaosBubbleVariants.DescriptionFor("darter"),
            ChaosArt.Resolve("bubbles", "darter"), "✦", Color.FromRgb(0xFF, 0x4D, 0xC4), maxWidth));
        host.Children.Add(CodexRow("bubble:golden", "Lucky Bubble", ChaosBubbleVariants.DescriptionFor("golden"),
            ChaosArt.Resolve("bubbles", "golden"), "✦", Color.FromRgb(0xFF, 0xD7, 0x00), maxWidth));
        host.Children.Add(CodexRow("bubble:echo", "The Echo", ChaosBubbleVariants.DescriptionFor("echo"),
            ChaosArt.Resolve("bubbles", "echo"), "◌", Color.FromRgb(0xC9, 0xC4, 0xE8), maxWidth));
        host.Children.Add(CodexRow("bubble:chaperone", "The Chaperone", ChaosBubbleVariants.DescriptionFor("chaperone"),
            ChaosArt.Resolve("bubbles", "chaperone"), "💞", Color.FromRgb(0x9C, 0xE8, 0xFF), maxWidth));
        host.Children.Add(CodexRow("bubble:tease", "The Tease", ChaosBubbleVariants.DescriptionFor("tease"),
            ChaosArt.Resolve("bubbles", "tease"), "✖", Color.FromRgb(0xB3, 0x0E, 0x2E), maxWidth));
    }

    private Window? _diaryPopout;

    /// <summary>The diary box is a teaser — clicking it opens the full, scrollable reader.</summary>
    private void Diary_PopOut(object sender, MouseButtonEventArgs e)
    {
        if (_diaryPopout != null)
        {
            try { _diaryPopout.Activate(); } catch { }
            return;
        }
        var host = new StackPanel { Margin = new Thickness(18, 14, 18, 18) };
        host.Children.Add(new TextBlock
        {
            Text = "DIARY — what you've met down there",
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0x43, 0x93)),
            FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold, FontSize = 13,
            Margin = new Thickness(0, 0, 0, 12)
        });
        FillDiaryRows(host, 440);
        _diaryPopout = new Window
        {
            Title = "Diary",
            Owner = this,
            Width = 580, Height = 680,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.ToolWindow,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x11, 0x26)),
            Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = host },
        };
        _diaryPopout.Closed += (_, _) => _diaryPopout = null;
        _diaryPopout.Show();
    }

    /// <summary>A square art icon cut to rounded corners with its accent ring ON TOP —
    /// raw full-bleed sprites otherwise show sharp corners and bury thin borders.</summary>
    private static FrameworkElement ArtIcon(ImageSource src, double size, double radius, Color accent, double ring = 2.5)
    {
        return new Border
        {
            Width = size, Height = size,
            Child = new Grid
            {
                Clip = new RectangleGeometry(new Rect(0, 0, size, size), radius, radius),
                Children =
                {
                    new Image { Source = src, Stretch = Stretch.UniformToFill },
                    new Border
                    {
                        CornerRadius = new CornerRadius(radius),
                        BorderBrush = new SolidColorBrush(accent),
                        BorderThickness = new Thickness(ring),
                        IsHitTestVisible = false,
                    },
                }
            },
        };
    }

    private TextBlock SubHeader(string text) => new()
    {
        Text = text, Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0x43, 0x93)),
        FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold, FontSize = 11,
        Margin = new Thickness(0, 12, 0, 6)
    };

    private Border CodexRow(string codexId, string name, string desc, ImageSource? iconSrc, string glyph, Color accent, double maxWidth = 270)
    {
        bool seen = ChaosMeta.IsDiscovered(codexId);

        var row = new StackPanel { Orientation = Orientation.Horizontal };

        // icon / silhouette
        FrameworkElement icon;
        if (seen && iconSrc != null)
            icon = ArtIcon(iconSrc, 39, 9, accent);
        else
            icon = new Border
            {
                Width = 39, Height = 39, CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(seen ? Color.FromArgb(60, accent.R, accent.G, accent.B) : Color.FromArgb(40, 120, 120, 140)),
                BorderBrush = new SolidColorBrush(seen ? accent : Color.FromRgb(90, 90, 110)), BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = seen ? glyph : "?", Foreground = new SolidColorBrush(seen ? accent : Color.FromRgb(0x88, 0x88, 0xA0)), FontSize = 19, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.Margin = new Thickness(0, 0, 12, 0);
        if (seen) ChaosTips.Attach(icon, name, desc, accent: accent);
        row.Children.Add(icon);

        // MaxWidth keeps descs wrapping inside their box (StackPanel rows otherwise
        // measure at infinite width and clip); the pop-out reader passes a roomier one.
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MaxWidth = maxWidth };
        mid.Children.Add(new TextBlock { Text = seen ? name : "???", Foreground = seen ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x90)), FontSize = 12, FontWeight = FontWeights.SemiBold });
        mid.Children.Add(new TextBlock { Text = seen ? desc : "hazy. go back down and look closer.", Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xB8, 0xB8)), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        row.Children.Add(mid);

        return new Border
        {
            Child = row,
            Background = new SolidColorBrush(Color.FromRgb(0x22, 0x1F, 0x40)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(seen ? (byte)70 : (byte)25, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(2),
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
        ChkAnnouncer.IsChecked = s.ChaosAnnouncerEnabled;

        // Accessory keybinds (future active-use; the binds persist now so loadouts feel real).
        var keyOpts = new[] { "Q", "E", "R", "F", "Z", "X", "C", "V", "1", "2", "3", "4" };
        CmbAccKey1.ItemsSource = keyOpts;
        CmbAccKey2.ItemsSource = keyOpts;
        CmbAccKey1.SelectedItem = keyOpts.Contains(s.ChaosAccessoryKey1) ? s.ChaosAccessoryKey1 : "Q";
        CmbAccKey2.SelectedItem = keyOpts.Contains(s.ChaosAccessoryKey2) ? s.ChaosAccessoryKey2 : "E";

        ApplyExtremeGate();
    }

    /// <summary>Lock/unlock the Extreme difficulty pill from meta state; fall back off it when locked.</summary>
    private void ApplyExtremeGate()
    {
        bool unlocked = ChaosMeta.State.ExtremeUnlocked;
        SegExtreme.IsEnabled = unlocked;
        SegExtreme.Content = unlocked ? "Inescapable" : "Inescapable 🔒";
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
    }

    private void SaveToSettings()
    {
        var s = App.Settings?.Current;
        if (s == null) return;

        s.ChaosDifficulty = GetSegment(GrpDifficulty) ?? "Easy";
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

    /// <summary>The loadout sidebar's FALL IN hero button lands here — same path as the footer button.</summary>
    public void FallIn() => BtnBegin_Click(this, new RoutedEventArgs());

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
