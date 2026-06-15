using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel;

/// <summary>
/// The Down the Rabbit Hole hub ("the Dollhouse"): opened from the Lab card. Four tabs:
/// BAG (pocket slots + the whole collection as clickable tiles), THE TOYBOX
/// (unlock/deepen toys, accessories and habits), SETTINGS (run setup), and
/// THE LOOKING GLASS (stats + diary + start-mantra picker + the seamstress's bench).
/// </summary>
public partial class ChaosHubWindow : Window
{
    private static readonly Random _rng = new();
    private int _waves = 5;

    /// <summary>The open Dollhouse, if any. Lets the loadout sidebar push unequips back into it.</summary>
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
        Closed += (_, _) =>
        {
            Current = null;
            App.Chaos?.CloseLoadoutSidebar();
            // Entering the Dollhouse detached the avatar; if we're leaving WITHOUT a descent
            // starting (FALL IN sets _fallingIn), put it back where it was.
            if (!_fallingIn) App.AvatarWindow?.SetChaosRunActive(false);
        };
        App.Chaos?.ShowLoadoutSidebar();
        // Detach the companion the moment we enter the hole's antechamber — not at run start —
        // so it's already a floating widget out of the dollhouse's way. The run-start call is
        // then a no-op; a hub closed without falling in re-attaches it (above).
        App.AvatarWindow?.SetChaosRunActive(true);
        TitleBar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } } };

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
        BuildDebugStrip();   // CCP_CHAOS_DEBUG=1 only — a normal launch builds nothing
        ShowTab("loadout");
        Loaded += (_, _) => { OnHubOpenedReveals(); FireHubGreeting(); };
        _uiSoundsReady = true;
    }

    /// <summary>The Madam's hub-return greeting (gated on NarrativeModeEnabled inside the director).
    /// A short beat lets the hub paint before the card slides in over it.</summary>
    private void FireHubGreeting()
    {
        try
        {
            if (App.Settings?.Current?.NarrativeModeEnabled != true) return;
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                if (Current != this) return;   // hub was closed in the meantime
                var ctx = new Services.Chaos.ChaosNarrativeContext { RankIndex = (int)Services.Chaos.ChaosMeta.RankIndex };
                Services.Chaos.ChaosNarrativeHooks.OnHubMoment("hub_return", ctx);
            };
            t.Start();
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosHub.FireHubGreeting: {E}", ex.Message); }
    }

    private void LoadBanner()
    {
        var src = ChaosArt.ResolveBanner();
        if (src != null) { BannerImage.Source = src; BannerImage.Visibility = Visibility.Visible; }
        var bd = ChaosArt.Resolve("hub", "backdrop");
        if (bd != null) { HubBackdrop.Source = bd; HubBackdrop.Visibility = Visibility.Visible; }
    }

    // ============================ tabs / gating ============================

    private void ApplyUnlocks()
    {
        // All four tabs render from the first visit (the drops cost is the real gate, and
        // run setup must be reachable before run #1). Gating returns with the reveal framework.
        TabLoadout.IsEnabled = true;
        TabEnhance.IsEnabled = true;
        TabRun.IsEnabled = true;
        TabImprove.IsEnabled = true;
        // The Diary tab only appears once there's something in it (met a bubble down there).
        TabDiary.Visibility = DiaryUnlocked ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton tb && tb.IsEnabled) ShowTab(tb.Tag?.ToString() ?? "loadout");
        else if (sender is ToggleButton tb2) tb2.IsChecked = false;
    }

    private void ShowTab(string tag)
    {
        // The old Habits tab folded into the Toybox; external callers may still ask for it.
        if (tag == "habits") tag = "enhance";
        // The Looking Glass stays out of reach until its reveal flips (the tab renders
        // greyed + locked, so gate on IsEnabled — it's always Visible now).
        if (tag == "improve" && !TabImprove.IsEnabled) tag = "loadout";
        // The diary lives behind its own reveal — nothing met yet, nothing to read.
        if (tag == "diary" && !DiaryUnlocked) tag = "loadout";

        PanelLoadout.Visibility = tag == "loadout" ? Visibility.Visible : Visibility.Collapsed;
        PanelEnhance.Visibility = tag == "enhance" ? Visibility.Visible : Visibility.Collapsed;
        PanelRun.Visibility     = tag == "run"     ? Visibility.Visible : Visibility.Collapsed;
        PanelImprove.Visibility = tag == "improve" ? Visibility.Visible : Visibility.Collapsed;
        PanelDiary.Visibility   = tag == "diary"   ? Visibility.Visible : Visibility.Collapsed;

        TabLoadout.IsChecked = tag == "loadout";
        TabEnhance.IsChecked = tag == "enhance";
        TabRun.IsChecked     = tag == "run";
        TabImprove.IsChecked = tag == "improve";
        TabDiary.IsChecked   = tag == "diary";

        TxtHint.Text = tag switch
        {
            "loadout" => "click a tile to slip it into a pocket. + takes you where it's sold.",
            "enhance" => "spend your drops. deepen what you like.",
            "run"     => "dress up the fall, then FALL IN.",
            "improve" => "the bench, the mantras, how far you've fallen.",
            "diary"   => "everything you've met down there. click an entry to pop it out.",
            _ => "",
        };
    }

    /// <summary>The Diary tab is offered once its reveal flips (the same "you've met something
    /// down there" unlock the old in-card diary used) — before that it's an empty page.</summary>
    private static bool DiaryUnlocked => RevealService.IsUnlocked(RevealIds.Diary);

    /// <summary>Settings tab: fold the dev/test knobs (bubble pool, mantra toggles, loops)
    /// in and out. Collapsed every open — the casual read stays short.</summary>
    private void TestingToggle_Click(object sender, RoutedEventArgs e)
    {
        bool open = TglTesting.IsChecked == true;
        TestingBody.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        TglTesting.Content = open ? "🧪 testing options ▾" : "🧪 testing options ▸";
    }

    /// <summary>A + tile wants to take the player shopping.</summary>
    private void JumpToTab(string tag) => ShowTab(tag);

    /// <summary>External navigation (the loadout sidebar's empty "+" tiles): switch tab and
    /// bring the Dollhouse forward.</summary>
    public void NavigateTo(string tag)
    {
        ShowTab(tag);
        try { Activate(); } catch { }
    }

    // ============================ top bar / stats ============================

    private int _shownSparks = -1, _shownGold = -1;
    private readonly Dictionary<TextBlock, DispatcherTimer> _balanceAnims = new();
    private DispatcherTimer? _balanceTickOwner;   // one tick stream even when both balances move

    private void RefreshTopBar()
    {
        AnimateBalance(TxtSparks, _shownSparks, ChaosMeta.State.Sparks);
        AnimateBalance(TxtGold, _shownGold, ChaosMeta.State.Gold);
        _shownSparks = ChaosMeta.State.Sparks;
        _shownGold = ChaosMeta.State.Gold;
        TxtRank.Text = ChaosMeta.Rank;
        RefreshTabBadges();   // every balance change re-counts what the shelves can sell
    }

    /// <summary>Signposting on the shop tabs: a small count of what's buyable RIGHT NOW
    /// (drops purchases on the Toybox, gold purchases on the Looking Glass) — the answer
    /// to "is there anything for me in there?" without scanning four shelves.</summary>
    private void RefreshTabBadges()
    {
        SetTabBadge(TabEnhance, "the Toybox", CountAffordableToybox());
        SetTabBadge(TabImprove, "the Looking Glass", TabImprove.IsEnabled ? CountAffordableBench() : 0);
    }

    private static void SetTabBadge(ToggleButton tab, string label, int count)
    {
        if (count <= 0)
        {
            tab.Content = label;
            tab.ClearValue(ToolTipProperty);
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
            Child = new TextBlock { Text = count.ToString(), Foreground = Brushes.White, FontSize = 10.5, FontWeight = FontWeights.Bold },
        });
        tab.Content = row;
        tab.ToolTip = count == 1 ? "1 thing you can afford right now" : $"{count} things you can afford right now";
    }

    /// <summary>Drops purchases buyable this instant: untrained habits + boon unlocks +
    /// boon level-ups — same gates the shelf buttons enforce (lesson, rank, her script).</summary>
    private static int CountAffordableToybox()
    {
        int n = 0;
        foreach (var u in ChaosUpgrades.All)
            if (!ChaosMeta.IsOwned(u.Id) && !ChaosMeta.IsPurchaseRankLocked(u.Id)
                && !ChaosLessons.IsLessonBlocked(u.Id) && ChaosMeta.CanAfford(u.Id)) n++;
        foreach (var b in ChaosLifetimeBoons.All)
        {
            int level = ChaosMeta.BoonLevel(b.Id);
            if (level <= 0)
            {
                if (!ChaosMeta.IsBoonRankLocked(b.Id) && !ChaosMeta.IsAccessoryScriptLocked(b.Id)
                    && !ChaosLessons.IsLessonBlocked(b.Id) && ChaosMeta.CanAffordUnlock(b.Id)) n++;
            }
            else if (level < b.MaxLevel && !ChaosMeta.IsCapstonePurchaseRankLocked(b.Id)
                     && ChaosMeta.CanAffordUpgrade(b.Id)) n++;
        }
        return n;
    }

    /// <summary>Gold purchases buyable this instant at her bench (rank + reveal gated).</summary>
    private int CountAffordableBench()
    {
        int n = 0;
        foreach (var item in BenchItems)
        {
            if (ChaosMeta.State.BenchPurchases.Contains(item.Id)) continue;
            if (item.RankNeed.HasValue && !ChaosMeta.AtLeast(item.RankNeed.Value)) continue;
            if (item.RevealGate != null && !RevealService.IsUnlocked(item.RevealGate)) continue;
            if (ChaosMeta.State.Gold >= item.Cost) n++;
        }
        return n;
    }

    /// <summary>Roll a top-bar balance from its last shown value to the new one (~500ms,
    /// soft tick underneath) so spending visibly *costs* — first paint just snaps.</summary>
    private void AnimateBalance(TextBlock tb, int from, int to)
    {
        if (_balanceAnims.TryGetValue(tb, out var old))
        {
            old.Stop();
            _balanceAnims.Remove(tb);
            if (ReferenceEquals(_balanceTickOwner, old)) _balanceTickOwner = null;
        }
        if (from < 0 || from == to || !IsLoaded)
        {
            tb.Text = to.ToString("N0");
            return;
        }

        const int DURATION_MS = 500, FRAME_MS = 33, TICK_EVERY_MS = 90;
        int elapsed = 0, lastTick = -TICK_EVERY_MS;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FRAME_MS) };
        _balanceAnims[tb] = timer;
        _balanceTickOwner ??= timer;             // only one stream of ticks at a time
        timer.Tick += (_, _) =>
        {
            elapsed += FRAME_MS;
            if (elapsed >= DURATION_MS)
            {
                timer.Stop();
                _balanceAnims.Remove(tb);
                if (ReferenceEquals(_balanceTickOwner, timer)) _balanceTickOwner = null;
                tb.Text = to.ToString("N0");
                return;
            }
            double eased = 1 - Math.Pow(1 - elapsed / (double)DURATION_MS, 3);
            tb.Text = ((int)Math.Round(from + (to - from) * eased)).ToString("N0");
            if (ReferenceEquals(_balanceTickOwner, timer) && elapsed - lastTick >= TICK_EVERY_MS)
            {
                lastTick = elapsed;
                ChaosSfx.Play("count_tick", 0.4f);
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

    // ---- happy path: the Toybox starter view (after run 1, before run 2) ----
    // Exactly three rows: two cheap lessonless charms + the one habit whose lesson
    // already shows what run 1 banked. Full shelves render from RunsCompleted >= 2.
    private static readonly string[] StarterShelfIds = { "start_resistance", "blank_eyes", "slow_fuses" };

    private static bool OnShelfNow(string id) =>
        ChaosMeta.State.RunsCompleted >= 2 || Array.IndexOf(StarterShelfIds, id) >= 0;

    /// <summary>One grouped list (RESTRAINT / CRAVING / DEPTH) of the trainable passives —
    /// untrained rows sell, trained rows toggle on/off.</summary>
    private void BuildHabits()
    {
        // One flat list, one card style: the single-rank habits first (branch order),
        // then the leveled ones (Utility lifetime boons — Rabbit's Foot etc.), unlabeled.
        HabitsHost.Children.Clear();
        foreach (var u in ChaosUpgrades.All)
            if (OnShelfNow(u.Id)) HabitsHost.Children.Add(BuildUpgradeRow(u));
        foreach (var b in ChaosLifetimeBoons.InCategory(ChaosBoonCategory.Utility))
            if (OnShelfNow(b.Id)) HabitsHost.Children.Add(BuildLifetimeBoonRow(b));
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
        ChaosTips.Attach(icon, u.Name, u.Desc, accent: accent, flavor: u.Flavor);
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        // ---- middle: name + desc + flavor + branch tag ----
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        mid.Children.Add(new TextBlock { Text = u.Name, Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold });
        mid.Children.Add(new TextBlock { Text = u.Desc, Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xB8, 0xB8)), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        if (!string.IsNullOrEmpty(u.Flavor))
            mid.Children.Add(new TextBlock
            {
                Text = u.Flavor, FontStyle = FontStyles.Italic, FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xB0, 0xB0, 0xC8)),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0)
            });
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
            if (ChaosLessons.IsLessonBlocked(u.Id))
            {
                // the lesson gates the training — no buy button
                right.Children.Add(BuildLessonLockPanel(u.Id, accent));
            }
            else
            {
                // Rank-floored purchases (extreme_tier needs Devoted) stay visible but locked.
                bool rankLocked = ChaosMeta.IsPurchaseRankLocked(u.Id);
                var train = BuyButton($"Train  ✦{u.Cost}", u.Id, afford && !rankLocked, Buy_Click);
                if (rankLocked)
                {
                    train.ToolTip = ChaosRanks.RankLockedTip + "\n" + ChaosRanks.RankSpecifics(ChaosRank.Devoted);
                    ToolTipService.SetShowOnDisabled(train, true);
                }
                right.Children.Add(train);
            }
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
            // No cue here — the unlock card below carries the sting.
            if (id == "extreme_tier") ApplyExtremeGate();
            BuildHabits();
            BuildLoadoutTiles();   // the habit grid lights up
            RefreshTopBar();
            RefreshStats();
            RevealService.Sync("purchase");   // extreme_tier flips the Inescapable pill reveal
            ApplyReveals();
            RunRevealFlashes("purchase");
            ShowUnlockCard(ChaosUnlockCards.ForHabit(id!));
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
        // Rank-locked = below your depth tier: a MYSTERY. Hide the art, name and what it does —
        // only the rank gate shows, so the deeper toys stay a reveal instead of a spoiled preview.
        bool rankLocked = ChaosMeta.IsBoonRankLocked(b.Id);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // ---- icon (real art if present, else a tinted square with the glyph) ----
        // Rank-locked → the keyhole mystery art (or a "?" box), never the boon's own sprite.
        var iconSrc = rankLocked ? null : ChaosArt.Resolve("boons", b.Id);
        FrameworkElement icon;
        if (rankLocked && TileUnknownArt != null)
            icon = ArtIcon(TileUnknownArt, 72, 14, BoonAccent, ring: 3);
        else if (iconSrc != null)
            icon = ArtIcon(iconSrc, 72, 14, BoonAccent, ring: 3);
        else
            icon = new Border
            {
                Width = 72, Height = 72, CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(70, BoonAccent.R, BoonAccent.G, BoonAccent.B)),
                BorderBrush = new SolidColorBrush(BoonAccent), BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = rankLocked ? "?" : b.Glyph, FontSize = 34, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 0, 12, 0);
        icon.Opacity = unlocked ? 1.0 : (rankLocked ? 0.6 : 0.5);
        if (rankLocked)
            ChaosTips.Attach(icon, "? ? ?", ChaosRanks.RankLockedTip, ChaosRanks.RankSpecifics(b.RankFloor));
        else
            ChaosTips.Attach(icon, unlocked ? $"{b.Name} · L{level}" : b.Name, b.Desc,
                string.IsNullOrEmpty(b.CapstoneDesc) ? null : "max: " + b.CapstoneDesc,
                flavor: b.Flavor);
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        // ---- middle: name + desc + level pips + value ----
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        mid.Children.Add(new TextBlock { Text = rankLocked ? "? ? ?" : b.Name, Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold });
        if (rankLocked)
        {
            // No desc, no flavor — only the gate. The reveal is the reward for sinking deeper.
            mid.Children.Add(new TextBlock
            {
                Text = ChaosRanks.RankLockedTip + " " + ChaosRanks.RankSpecifics(b.RankFloor),
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x80, 0xA8)), FontStyle = FontStyles.Italic,
                FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0)
            });
        }
        else
        {
            mid.Children.Add(new TextBlock { Text = b.Desc, Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xB8, 0xB8)), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            if (!string.IsNullOrEmpty(b.Flavor))
                mid.Children.Add(new TextBlock
                {
                    Text = b.Flavor, FontStyle = FontStyles.Italic, FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xB0, 0xB0, 0xC8)),
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0)
                });
        }

        // Active-use toys carry their trigger: the keybind (when equipped) or a generic hint.
        if (b.IsActiveUse && !rankLocked)
        {
            string key = App.Settings?.Current?.ChaosAccessoryKey1 ?? "Q";
            string useHint = b.UseCooldownSec > 0 ? $"{b.UseCooldownSec:0}s cooldown" : "limited uses";
            mid.Children.Add(new TextBlock
            {
                Text = active ? $"ACTIVE · fires on {key} mid-descent · {useHint}"
                              : $"ACTIVE · fires on your toy key mid-descent · {useHint}",
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)),
                FontSize = 10.5, FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 3, 0, 0)
            });
        }

        // Capstone teaser: dim until the final rank is bought, gold once it's live.
        if (!string.IsNullOrEmpty(b.CapstoneDesc) && !rankLocked)
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
        {
            // Rank floor is the outermost gate: too shallow and the row stays priced but locked,
            // with the exact descent count on hover. Lessons + her script still stack beneath it.
            if (ChaosMeta.IsBoonRankLocked(b.Id))
            {
                // Mystery: hide the cost too — show only the depth gate. The button names the
                // rank you must reach, not the price of a toy you're not meant to know yet.
                var held = BuyButton($"🔒 {ChaosRanks.Name(b.RankFloor)}", b.Id, false, BoonUnlock_Click);
                held.ToolTip = ChaosRanks.RankLockedTip + "\n" + ChaosRanks.RankSpecifics(b.RankFloor);
                ToolTipService.SetShowOnDisabled(held, true);
                right.Children.Add(held);
            }
            // Happy path: until the_spanker is owned, every OTHER accessory hangs locked —
            // even with its lesson complete. ChaosMeta.TryUnlockBoon enforces the same gate.
            else if (ChaosMeta.IsAccessoryScriptLocked(b.Id))
            {
                var held = BuyButton($"Unlock  ✦{b.UnlockCost}", b.Id, false, BoonUnlock_Click);
                held.ToolTip = "she sells these in an order of her own.";
                ToolTipService.SetShowOnDisabled(held, true);
                right.Children.Add(held);
            }
            else
            {
                right.Children.Add(ChaosLessons.IsLessonBlocked(b.Id)
                    ? BuildLessonLockPanel(b.Id, BoonAccent)   // the lesson gates the unlock — no buy button
                    : BuyButton($"Unlock  ✦{b.UnlockCost}", b.Id, ChaosMeta.CanAffordUnlock(b.Id), BoonUnlock_Click));
            }
        }
        else if (maxed)
            right.Children.Add(new TextBlock { Text = "MAX  ✓", Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0xE0, 0x96)), FontSize = 13, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right });
        else
        {
            int cost = ChaosMeta.NextUpgradeCostOf(b.Id) ?? 0;
            // The capstone (final) level waits for the Devoted rank: the row stays visible,
            // priced and locked, with her line on hover.
            bool capLocked = ChaosMeta.IsCapstonePurchaseRankLocked(b.Id);
            var deepen = BuyButton($"deepen  ✦{cost}", b.Id,
                !capLocked && ChaosMeta.CanAffordUpgrade(b.Id), BoonUpgrade_Click);
            if (capLocked)
            {
                deepen.ToolTip = ChaosRanks.CapstoneLockedTip + "\n" + ChaosRanks.RankSpecifics(ChaosRank.Devoted);
                ToolTipService.SetShowOnDisabled(deepen, true);
            }
            right.Children.Add(deepen);
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
            if (ChaosMeta.TryUnlockBoon(id))
            {
                // No cue here — the unlock card below carries the sting.
                AfterBoonChange();
                ShowUnlockCard(ChaosUnlockCards.ForBoonUnlock(id));   // after the unlock: reads auto-equip state
            }
            else ChaosSfx.Play("ui_denied", 0.45f);
        }
    }

    private void BoonUpgrade_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            if (ChaosMeta.TryUpgradeBoon(id))
            {
                AfterBoonChange();
                // Only the FINAL level gets a card — the capstone changes behavior; mid levels
                // just grow a number (and keep the quiet deepen cue; the card stings itself).
                var b = ChaosLifetimeBoons.ById(id);
                var capstoneCard = b != null && ChaosMeta.BoonLevel(id) >= b.MaxLevel
                    ? ChaosUnlockCards.ForCapstone(id) : null;
                if (capstoneCard != null) ShowUnlockCard(capstoneCard);
                else ChaosSfx.Play("ui_deepen", 0.5f);
            }
            else ChaosSfx.Play("ui_denied", 0.45f);
        }
    }

    // ===================== unlock announcement cards =====================

    private readonly Queue<ChaosUnlockCardData> _unlockCards = new();
    private bool _unlockCardShowing;
    private const int UNLOCK_CARD_HOLD_MS = 4500;

    /// <summary>Float an unlock card top-center over the hub: slide-in, dwell, fade. Cards
    /// queue so back-to-back purchases play in sequence; the layer never takes clicks, so
    /// shopping continues underneath.</summary>
    private void ShowUnlockCard(ChaosUnlockCardData? data)
    {
        if (data == null) return;
        _unlockCards.Enqueue(data);
        if (!_unlockCardShowing) ShowNextUnlockCard();
    }

    private void ShowNextUnlockCard()
    {
        if (_unlockCards.Count == 0) { _unlockCardShowing = false; return; }
        _unlockCardShowing = true;

        var card = ChaosUnlockCards.BuildCardVisual(_unlockCards.Dequeue());
        card.HorizontalAlignment = HorizontalAlignment.Center;
        card.VerticalAlignment = VerticalAlignment.Top;
        card.Margin = new Thickness(0, 96, 0, 0);   // below the title bar + tab strip
        var slide = new TranslateTransform(0, -14);
        card.RenderTransform = slide;
        card.Opacity = 0;
        UnlockCardLayer.Children.Add(card);

        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(-14, 0, TimeSpan.FromMilliseconds(220))
            { EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut } });

        var hold = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(UNLOCK_CARD_HOLD_MS) };
        hold.Tick += (_, _) =>
        {
            hold.Stop();
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(260));
            fade.Completed += (_, _)
                => { try { UnlockCardLayer.Children.Remove(card); } catch { } ShowNextUnlockCard(); };
            card.BeginAnimation(OpacityProperty, fade);
        };
        hold.Start();
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

    // Stitched-keyhole sprite for the not-yet-formed ??? pads. Resolved once per window —
    // tile rebuilds run on every loadout change and must not re-hit the disk.
    private bool _tileUnknownTried;
    private ImageSource? _tileUnknownArt;
    private ImageSource? TileUnknownArt
    {
        get
        {
            if (!_tileUnknownTried) { _tileUnknownTried = true; _tileUnknownArt = ChaosArt.Resolve("hub", "tile_unknown"); }
            return _tileUnknownArt;
        }
    }

    private enum TileState { Equipped, Owned, Locked, Empty }

    /// <summary>The whole glance page: pocket slots + accessory/skill/habit tile grids.</summary>
    private void BuildLoadoutTiles()
    {
        // ---- pocket slots (big tiles, one group per category; unsewn categories don't render) ----
        PocketSlotsHost.Children.Clear();
        var toyGroup = PocketGroup("TOY", ChaosBoonCategory.Skill);
        if (toyGroup != null) PocketSlotsHost.Children.Add(toyGroup);
        var accGroup = PocketGroup("ACCESSORY", ChaosBoonCategory.Accessory);
        if (accGroup != null) PocketSlotsHost.Children.Add(accGroup);
        if (PocketSlotsHost.Children.Count == 0)
        {
            // Empty-state vignette (turned-out pocket) above the line, art-optional as ever.
            var vignette = ChaosArt.Resolve("hub", "pockets_empty");
            var emptyCol = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
            if (vignette != null)
                emptyCol.Children.Add(new Image
                {
                    Source = vignette,
                    Height = 96,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Opacity = 0.8,
                    Margin = new Thickness(0, 0, 0, 6),
                });
            emptyCol.Children.Add(new TextBlock
            {
                Text = "no pockets sewn yet.",
                Foreground = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x90)),
                FontSize = 12,
            });
            PocketSlotsHost.Children.Add(emptyCol);
        }

        // ---- collections ----
        FillCategoryTiles(TilesAccessories, ChaosBoonCategory.Accessory, padTo: 8);
        FillCategoryTiles(TilesSkills, ChaosBoonCategory.Skill, padTo: 8);
        TxtAccCount.Text = $"{ChaosMeta.EquippedCountIn(ChaosBoonCategory.Accessory)}/{ChaosMeta.SlotsFor(ChaosBoonCategory.Accessory)} equipped";
        TxtSkillCount.Text = $"{ChaosMeta.EquippedCountIn(ChaosBoonCategory.Skill)}/{ChaosMeta.SlotsFor(ChaosBoonCategory.Skill)} equipped";

        // ---- habits 4x4 (trained = on/off toggle; click an untrained one to go train it) ----
        TilesHabits.Children.Clear();
        var habits = ChaosUpgrades.All.Where(u => OnShelfNow(u.Id)).ToList();
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
                : () => JumpToTab("enhance");
            TilesHabits.Children.Add(LoadoutTile(u.Glyph, u.Name, u.Desc,
                on ? "click to switch off" : owned ? "click to switch on" : $"train for ✦{u.Cost} in the Toybox",
                BranchColor(u.Branch),
                on ? TileState.Equipped : owned ? TileState.Owned : TileState.Locked,
                onClick,
                cornerBadge: on ? "✓" : null,
                art: u.IconPath != null ? ChaosArt.TryLoad(u.IconPath) : ChaosArt.Resolve("upgrades", id),
                flavor: u.Flavor));
        }
        // Charms (Utility lifetime boons — Rabbit's Foot etc.) live with the habits:
        // leveled, always-on once worn, toggled exactly like a trained habit.
        var charms = ChaosLifetimeBoons.InCategory(ChaosBoonCategory.Utility).Where(b => OnShelfNow(b.Id)).ToList();
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
                : () => JumpToTab("enhance");
            bool charmRankLocked = ChaosMeta.IsBoonRankLocked(bid);
            TilesHabits.Children.Add(LoadoutTile(charmRankLocked ? "?" : b.Glyph,
                charmRankLocked ? "? ? ?" : unlocked ? $"{b.Name} · L{ChaosMeta.BoonLevel(bid)}" : b.Name,
                charmRankLocked ? ChaosRanks.RankLockedTip : b.Desc,
                active ? "click to switch off" : unlocked ? "click to switch on"
                    : charmRankLocked ? ChaosRanks.RankSpecifics(b.RankFloor) : $"unlock for ✦{b.UnlockCost} in the Toybox",
                BoonAccent,
                active ? TileState.Equipped : unlocked ? TileState.Owned : TileState.Locked,
                onClick,
                cornerBadge: active ? "✓" : null,
                art: charmRankLocked ? TileUnknownArt : ChaosArt.Resolve("boons", bid),
                flavor: charmRankLocked ? null : b.Flavor));
        }
        int shown = habits.Count + charms.Count;
        int target = Math.Max(16, ((shown + 3) / 4) * 4);
        for (int i = shown; i < target; i++)
            TilesHabits.Children.Add(LoadoutTile("+", "a habit not yet formed",
                "more training arrives in a later fitting.", null,
                Color.FromRgb(0xB8, 0xB8, 0xD0), TileState.Empty, null));
        TxtHabitCount.Text = $"{switchedOn} on · {trained}/{shown} trained";
    }

    /// <summary>One labelled pocket column: equipped boon as a big gold tile, plus + tiles for
    /// free slots. Null when the category has no pockets sewn (and nothing stale equipped).</summary>
    private FrameworkElement? PocketGroup(string label, ChaosBoonCategory cat)
    {
        if (ChaosMeta.SlotsFor(cat) <= 0 && ChaosMeta.EquippedCountIn(cat) == 0) return null;
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
                art: ChaosArt.Resolve("boons", b.Id), size: 114, flavor: b.Flavor);
            cell.Margin = new Thickness(0, 0, 24, 0);
            row.Children.Add(cell);
        }
        for (int i = equipped.Count; i < ChaosMeta.SlotsFor(cat); i++)
        {
            var cell = LoadoutTile("+", $"empty {label.ToLowerInvariant()} pocket",
                "pick one from the shelf below, or go shopping in the Toybox.", null,
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
            bool rankLocked = ChaosMeta.IsBoonRankLocked(id);
            var state = active ? TileState.Equipped : unlocked ? TileState.Owned : TileState.Locked;
            Action onClick = active ? () => { ChaosMeta.SetBoonActive(id, false); ChaosSfx.Play("ui_unequip", 0.45f); AfterBoonChange(); }
                : unlocked ? () => EquipSwapping(id, cat)
                : () => JumpToTab("enhance");
            // Rank-locked → mystery: keyhole art, "???" everywhere, the depth gate instead of a price.
            string extra = active ? "click to unequip"
                : unlocked ? "click to equip"
                : rankLocked ? ChaosRanks.RankSpecifics(b.RankFloor)
                : $"unlock for ✦{b.UnlockCost} in the Toybox";
            host.Children.Add(LoadoutTile(rankLocked ? "?" : b.Glyph,
                rankLocked ? "? ? ?" : unlocked ? $"{b.Name} · L{level}" : b.Name,
                rankLocked ? ChaosRanks.RankLockedTip : b.Desc, extra, BoonAccent, state, onClick,
                cornerBadge: active ? "★" : null,
                art: rankLocked ? TileUnknownArt : ChaosArt.Resolve("boons", id),
                flavor: rankLocked ? null : b.Flavor));
        }
        int target = Math.Max(padTo, ((boons.Count + 3) / 4) * 4);
        for (int i = boons.Count; i < target; i++)
            host.Children.Add(LoadoutTile("+",
                cat == ChaosBoonCategory.Skill ? "another toy is being stitched" : "another accessory is being stitched",
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
                               ImageSource? art = null, double size = TILE, string? caption = null,
                               string? flavor = null)
    {
        // Rounded clip so the square art can't poke past the ring corners (Border doesn't
        // clip children to its CornerRadius; the tile is fixed-size so a geometry works).
        var content = new Grid
        {
            Clip = new RectangleGeometry(new Rect(0, 0, size, size), 12, 12)
        };
        // Mystery pads (Empty + default ??? caption) wear the stitched keyhole instead of a
        // bare "+"; clickable "empty pocket" tiles keep the + — there it means "add one".
        var keyhole = state == TileState.Empty && caption == null ? TileUnknownArt : null;
        if (art != null && state != TileState.Empty)
            content.Children.Add(new Image { Source = art, Stretch = Stretch.UniformToFill, Opacity = state == TileState.Locked ? 0.35 : 1.0 });
        else if (keyhole != null)
            content.Children.Add(new Image
            {
                Source = keyhole,
                Width = size * 0.5,
                Height = size * 0.5,
                Stretch = Stretch.Uniform,
                Opacity = 0.45,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
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
        ChaosTips.Attach(cell, title, desc, extra, accent, flavor: flavor);
        return cell;
    }

    // ============================ improvements tab ============================
    // Her bench (the gold shop) lives in ChaosHubWindow.Bench.cs — see BuildBench().

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
        if (seen && !string.IsNullOrEmpty(b.Flavor))
            mid.Children.Add(new TextBlock
            {
                Text = b.Flavor, FontStyle = FontStyles.Italic, FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xB0, 0xB0, 0xC8)),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0)
            });
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
                accent, flavor: b.Flavor);
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

    /// <summary>The interaction sheet at the top of the diary: every core verb, always
    /// readable — the recall surface for all the one-time in-run teaches (miss a beat
    /// mid-chaos and this is where it can be re-read).</summary>
    private static readonly (string Glyph, string Name, string Desc)[] DiaryVerbs =
    {
        ("✋", "hold to snap", "press and HOLD a live (ringed) bubble about a second to defuse it — costs 30 focus. a quick click, or letting go early, TRIGGERS it instead."),
        ("○", "click the treats", "a tap pops a treat: its payload plays, the streak climbs, and +10 focus flows back (+15 from heavies and rabbits)."),
        ("🌊", "right-click · the ripple", "casts a wave from your cursor (near the bubbles): treats pop fully paid, trances snap clean, rabbits get flung. one charge, gathered back over time — READY on the sidebar means it's in your hand."),
        ("◌", "focus", "the defuse fuel: max 100, you fall in with 50, no regen on its own. when the bar runs red you can't afford a hold — farm treats before touching a live one (pressing one anyway triggers it in your grip)."),
        ("🔥", "lust", "the orange bar. climbs while you perform and pays up to x2 at full burn; an unblocked trigger cools it to zero."),
        ("💨", "never let treats rot", "a treat that fades unpopped HALVES your streak. chase the rewards too, not just the threats."),
        ("🐇", "catch the white rabbit", "everything slows to a crawl for six seconds. with the Spanker worn, you smack it into the field instead."),
        ("❄", "the pickups", "freeze ❄ holds the whole field 3.5 seconds (still poppable, and snaps cost no focus) · the lucky bubble 🍀 pays gold on the spot."),
        ("⏸", "your panic key", "one press holds the field mid-fall; pressing it again wakes you up to the recap."),
    };

    /// <summary>Every diary entry into <paramref name="host"/> — shared by the half-width
    /// Improvements box (narrow wrap) and the pop-out reader (roomier).</summary>
    private void FillDiaryRows(Panel host, double maxWidth)
    {
        // How to play before what you've met — never discovery-gated.
        host.Children.Add(SubHeader("VERBS · how to play down there"));
        foreach (var v in DiaryVerbs)
            host.Children.Add(VerbRow(v.Glyph, v.Name, v.Desc, maxWidth));
        host.Children.Add(SubHeader("WHAT YOU'VE MET"));
        foreach (var v in ChaosBubbleVariants.All)
            host.Children.Add(CodexRow("bubble:" + v.Id, v.Name, ChaosBubbleVariants.DescriptionFor(v.Id),
                ChaosArt.Resolve("bubbles", v.Id), "●", Color.FromRgb(v.Tint.R, v.Tint.G, v.Tint.B), maxWidth));
        // Not part of the weighted pool table; listed explicitly.
        host.Children.Add(CodexRow("bubble:darter", "White Rabbit", ChaosBubbleVariants.DescriptionFor("darter"),
            ChaosArt.Resolve("bubbles", "darter"), "✧", Color.FromRgb(0xFF, 0x4D, 0xC4), maxWidth));
        host.Children.Add(CodexRow("bubble:golden", "Lucky Bubble", ChaosBubbleVariants.DescriptionFor("golden"),
            ChaosArt.Resolve("bubbles", "golden"), "🍀", Color.FromRgb(0xFF, 0xD7, 0x00), maxWidth));
        host.Children.Add(CodexRow("bubble:echo", "The Echo", ChaosBubbleVariants.DescriptionFor("echo"),
            ChaosArt.Resolve("bubbles", "echo"), "◌", Color.FromRgb(0xC9, 0xC4, 0xE8), maxWidth));
        host.Children.Add(CodexRow("bubble:chaperone", "The Chaperone", ChaosBubbleVariants.DescriptionFor("chaperone"),
            ChaosArt.Resolve("bubbles", "chaperone"), "💞", Color.FromRgb(0x9C, 0xE8, 0xFF), maxWidth));
        host.Children.Add(CodexRow("bubble:tease", "The Tease", ChaosBubbleVariants.DescriptionFor("tease"),
            ChaosArt.Resolve("bubbles", "tease"), "✖", Color.FromRgb(0xB3, 0x0E, 0x2E), maxWidth));
        host.Children.Add(CodexRow("bubble:bound", "The Bound", ChaosBubbleVariants.DescriptionFor("bound"),
            ChaosArt.Resolve("bubbles", "bound"), "⛓", Color.FromRgb(0xFF, 0x69, 0xB4), maxWidth));
        host.Children.Add(CodexRow("bubble:brittle", "The Brittle", ChaosBubbleVariants.DescriptionFor("brittle"),
            ChaosArt.Resolve("bubbles", "brittle"), "◇", Color.FromRgb(0xD9, 0xEF, 0xFF), maxWidth));
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
            Text = "DIARY · what you've met down there",
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

    /// <summary>A diary verb row: the CodexRow look without the discovery gate — the
    /// how-to-play sheet is always legible.</summary>
    private Border VerbRow(string glyph, string name, string desc, double maxWidth)
    {
        var accent = Color.FromRgb(0x7A, 0xE0, 0xFF);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Border
        {
            Width = 39, Height = 39, CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(Color.FromArgb(45, accent.R, accent.G, accent.B)),
            BorderBrush = new SolidColorBrush(accent), BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0),
            Child = new TextBlock { Text = glyph, Foreground = new SolidColorBrush(accent), FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MaxWidth = maxWidth };
        mid.Children.Add(new TextBlock { Text = name, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold });
        mid.Children.Add(new TextBlock { Text = desc, Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xB8, 0xB8)), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        row.Children.Add(mid);
        return new Border
        {
            Child = row,
            Background = new SolidColorBrush(Color.FromRgb(0x22, 0x1F, 0x40)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(55, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 6),
        };
    }

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
        ChkNarrative.IsChecked = s.NarrativeModeEnabled;
        ChkBackdrop.IsChecked = s.BackdropEnabled;
        SldBackdropOpacity.Value = s.BackdropOpacity;

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
        if (!unlocked)
        {
            // The lock keeps its mystery on the face; the hover gives the exact path.
            int cost = ChaosUpgrades.ById("extreme_tier")?.Cost ?? 0;
            SegExtreme.ToolTip = "a deeper door. she sells the key in the Toybox: "
                + $"finish {ChaosLessons.T_EXTREME_TIER} relentless descents, reach Devoted "
                + $"({ChaosRanks.Thresholds[(int)ChaosRank.Devoted]} descents), then train it for ✦{cost}.";
            ToolTipService.SetShowOnDisabled(SegExtreme, true);
        }
        ApplyDifficultyPillTips(extremeUnlocked: unlocked);
        if (!unlocked && SegExtreme.IsChecked == true)
            SetSegment(GrpDifficulty, "Hard");
    }

    /// <summary>What each pill actually changes, on hover — pay multiplier, spawn pace, field
    /// size — so picking a difficulty is a choice instead of a mystery. The locked Inescapable
    /// pill keeps its unlock-path tooltip (set in <see cref="ApplyExtremeGate"/>).</summary>
    private void ApplyDifficultyPillTips(bool extremeUnlocked)
    {
        foreach (var pill in GrpDifficulty.Children.OfType<ToggleButton>())
        {
            string? tip = pill.Tag?.ToString() switch
            {
                "Easy" => "x1.0 pay. the calmest fall: baseline spawn pace, the longest trances, and the strange bubbles roll half as often.",
                "Medium" => "x1.3 on every payout. bubbles surface ~30% faster and the field holds ~14% more of them at once.",
                "Hard" => "x1.7 on every payout. ~70% faster spawns, ~30% more on screen, shorter trances — and the Bound hunts here on any rank.",
                "Extreme" => extremeUnlocked
                    ? "x2.2 on every payout. spawns at more than double pace, ~48% more on screen. the deepest the hole goes."
                    : null,   // the unlock-path tooltip owns the locked pill
                _ => null,
            };
            if (tip != null) pill.ToolTip = tip;
        }
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
        var s = App.Settings?.Current;
        if (s == null) return;

        // Reveal-gate fallbacks are visual only — never persist them over the saved choice.
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

    // ============================ run-setup controls ============================

    private void Segment_Click(object sender, RoutedEventArgs e)
    {
        var btn = (ToggleButton)sender;
        if (!btn.IsEnabled) { btn.IsChecked = false; return; }   // locked (Extreme)
        var grp = (Panel)btn.Parent;
        foreach (var t in grp.Children.OfType<ToggleButton>()) t.IsChecked = ReferenceEquals(t, btn);
        if (ReferenceEquals(grp, GrpDifficulty)) _diffAutoClamped = false;   // a real choice again
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
        // Only difficulties whose pills are revealed can roll (the saved setting is untouched).
        var diffs = new List<string> { "Easy" };
        if (SegMedium.Visibility == Visibility.Visible) diffs.Add("Medium");
        if (SegHard.Visibility == Visibility.Visible) diffs.Add("Hard");
        if (ChaosMeta.State.ExtremeUnlocked) diffs.Add("Extreme");
        SetSegment(GrpDifficulty, diffs[_rng.Next(diffs.Count)]);
        SetSegment(GrpLength, new[] { "120", "180", "300" }[_rng.Next(3)]);
        SetSegment(GrpMotion, new[] { "Mixed", "FloatUp", "RainDown", "RoamBounce" }[_rng.Next(4)]);
        var pool = GrpPool.Children.OfType<ToggleButton>().ToList();
        foreach (var t in pool) t.IsChecked = _rng.NextDouble() < 0.6;
        if (!pool.Any(t => t.IsChecked == true)) pool[0].IsChecked = true;
    }

    private void BtnDefaults_Click(object sender, RoutedEventArgs e) { LoadDefaults(); ApplyExtremeGate(); }

    private bool _fallingIn;   // FALL IN clicked: the Closed handler must NOT re-attach the avatar

    private void BtnBegin_Click(object sender, RoutedEventArgs e)
    {
        _fallingIn = true;   // keep the avatar detached through the hub→run handoff (no flicker)
        SaveToSettings();
        Close();
        Application.Current?.Dispatcher.BeginInvoke(new Action(() => App.Chaos?.StartRun()));
    }

    /// <summary>The loadout sidebar's FALL IN hero button lands here — same path as the footer button.</summary>
    public void FallIn() => BtnBegin_Click(this, new RoutedEventArgs());

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Re-open the spoiler-free rules card on demand (the same card shown the first
    /// time the Dollhouse opened) — a "how do I play this" refresher anytime.</summary>
    private void BtnGuide_Click(object sender, RoutedEventArgs e)
    {
        try { new ChaosIntroWindow { Owner = this }.ShowDialog(); }
        catch (Exception ex) { App.Logger?.Debug("Chaos guide reshow: {E}", ex.Message); }
    }

    // ============================ helpers ============================

    private static string? GetSegment(Panel grp) =>
        grp.Children.OfType<ToggleButton>().FirstOrDefault(t => t.IsChecked == true)?.Tag?.ToString();

    private static void SetSegment(Panel grp, string? tag)
    {
        foreach (var t in grp.Children.OfType<ToggleButton>())
            t.IsChecked = (t.Tag?.ToString() == tag);
    }
}
