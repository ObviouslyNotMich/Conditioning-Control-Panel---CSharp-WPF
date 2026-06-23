using System;
using System.Collections.Generic;
using System.Linq;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using global::Avalonia.Interactivity;
using global::Avalonia.Threading;
using ConditioningControlPanel.Core.Services.Chaos;

namespace ConditioningControlPanel.Avalonia.Chaos;

public partial class ChaosHubWindow
{
    #region reveals

    private readonly Dictionary<string, Control> _revealMap = new();
    private bool _dollhouseBeatsFired;
    internal const string WALL_TIP = "there's a wall here that isn't quite a wall.";

    private void InitRevealMap()
    {
        _revealMap[RevealIds.TabLookingGlass] = TabImprove;
        _revealMap[RevealIds.SectionToys] = HdrToys;
        _revealMap[RevealIds.SectionAccessories] = HdrAccessories;
        _revealMap[RevealIds.HerCorner] = HerCornerCard;
        _revealMap[RevealIds.PillTeasing] = SegMedium;
        _revealMap[RevealIds.PillRelentless] = SegHard;
        _revealMap[RevealIds.PillInescapable] = SegExtreme;
        _revealMap[RevealIds.StartPicker] = CardMantras;
        _revealMap[RevealIds.Diary] = HdrDiary;
        _revealMap[RevealIds.StatsPanel] = HdrStats;

        ChaosTips.Attach(StubToys, "???", WALL_TIP,
            "opens with your first toy pocket. she sews pockets for gold (her corner, later her bench).");
        ChaosTips.Attach(StubAccessories, "???", WALL_TIP,
            "opens with your first accessory pocket. she sews pockets for gold (her corner, later her bench).");
    }

    internal void ApplyReveals()
    {
        try
        {
            bool lookingGlass = RevealService.IsUnlocked(RevealIds.TabLookingGlass);
            TabImprove.IsVisible = true;
            TabImprove.IsEnabled = lookingGlass;
            TabImprove.Opacity = lookingGlass ? 1.0 : 0.40;
            TabImprove.Content = lookingGlass ? "the Looking Glass" : "??? 🔒";
            if (!lookingGlass)
            {
                ToolTip.SetTip(TabImprove, WALL_TIP + "\n" + ChaosRanks.RankSpecifics(ChaosRank.Slipping));
            }
            else ToolTip.SetTip(TabImprove, null);
            if (!lookingGlass && TabImprove.IsChecked == true) ShowTab("loadout");

            bool toys = RevealService.IsUnlocked(RevealIds.SectionToys);
            HdrToys.IsVisible = toys;
            BoonHostSkills.IsVisible = toys;
            StubToys.IsVisible = !toys;

            bool accs = RevealService.IsUnlocked(RevealIds.SectionAccessories);
            HdrAccessories.IsVisible = accs;
            BoonHostAccessories.IsVisible = accs;
            StubAccessories.IsVisible = !accs;

            CardBagToys.IsVisible = toys;
            CardBagAccessories.IsVisible = accs;

            bool corner = RevealService.IsUnlocked(RevealIds.HerCorner);
            HerCornerCard.IsVisible = corner;
            if (corner) BuildHerCorner();

            bool teasing = RevealService.IsUnlocked(RevealIds.PillTeasing);
            bool relentless = RevealService.IsUnlocked(RevealIds.PillRelentless);
            SegMedium.IsVisible = teasing;
            SegHard.IsVisible = relentless;
            if ((!teasing && SegMedium.IsChecked == true) || (!relentless && SegHard.IsChecked == true))
            {
                SetSegment(GrpDifficulty, "Easy");
                _diffAutoClamped = true;
            }

            CardMantras.IsVisible = RevealService.IsUnlocked(RevealIds.StartPicker);
            bool diary = RevealService.IsUnlocked(RevealIds.Diary);
            TabDiary.IsVisible = diary;
            if (!diary && TabDiary.IsChecked == true) ShowTab("loadout");
            bool stats = RevealService.IsUnlocked(RevealIds.StatsPanel);
            HdrStats.IsVisible = stats;
            StatsGrid.IsVisible = stats;

            int toyPockets = ChaosMeta.SlotsFor(ChaosBoonCategory.Skill);
            CardKeybinds.IsVisible = toyPockets >= 1;
            RowToyKey2.IsVisible = toyPockets >= 2;
            TxtKeybindsSub.Text = toyPockets >= 2
                ? "your equipped toys fire on these, mid-descent."
                : "your equipped toy fires on this, mid-descent. the second pocket isn't sewn yet.";

            TxtPocketsSub.Text = ChaosMeta.State.ToyPockets + ChaosMeta.State.AccessoryPockets == 0
                ? "you fall in with empty hands, for now."
                : "what you take down with you. choose like it matters.";
        }
        catch (Exception ex) { _logger?.LogWarning("ApplyReveals failed ({E})", ex.Message); }
    }

    private void OnHubOpenedReveals()
    {
        if (_dollhouseBeatsFired) return;
        _dollhouseBeatsFired = true;
        try
        {
            if (!ChaosMeta.State.SeenIntroGuide)
            {
                var intro = new ChaosIntroWindow();
                intro.ShowDialog(this);
                ChaosMeta.State.SeenIntroGuide = true;
                ChaosMeta.Save();
            }
        }
        catch (Exception ex) { _logger?.LogWarning("Chaos intro guide failed ({E})", ex.Message); }
        try
        {
            ChaosHappyPath.OnDollhouseFirstOpen();
        }
        catch (Exception ex) { _logger?.LogInformation("Dollhouse first-open beat: {E}", ex.Message); }
        RunRevealFlashes("hub_open");
    }

    private void RunRevealFlashes(string reason)
    {
        try
        {
            RevealService.Sync(reason);
            var pending = RevealService.PendingIds();
            if (pending.Count == 0) return;

            double stagger = 0;
            string? firstFlashed = null;
            foreach (var id in pending)
            {
                if (_revealMap.TryGetValue(id, out var el) && el != null && el.IsVisible)
                {
                    string captured = id;
                    firstFlashed ??= id;
                    FlashReveal(el, stagger, () => RevealService.MarkSeen(captured));
                    stagger += 0.6;
                }
                else
                {
                    RevealService.MarkSeen(id);
                }
            }
            if (firstFlashed != null)
            {
                try { AvaloniaChaosApp.Bark?.NotifyChaosRevealFlash(firstFlashed); } catch { }
            }
        }
        catch (Exception ex) { _logger?.LogWarning("RunRevealFlashes failed ({E})", ex.Message); }
    }

    private static void FlashReveal(Control el, double beginSec, Action done)
    {
        try
        {
            if (beginSec <= 0) AvaloniaChaosSfx.Play("reveal_chime", 0.5f);
            else
            {
                var chime = new DispatcherTimer { Interval = TimeSpan.FromSeconds(beginSec) };
                chime.Tick += (_, _) => { chime.Stop(); AvaloniaChaosSfx.Play("reveal_chime", 0.5f); };
                chime.Start();
            }

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            double startMs = Environment.TickCount64 + beginSec * 1000;
            timer.Tick += (_, _) =>
            {
                double elapsed = Environment.TickCount64 - startMs;
                if (elapsed < 0) return;
                double cycle = 500 * 6;
                double t = (elapsed % cycle) / 500.0;
                el.Opacity = 1 - 0.75 * ((Math.Sin(t * Math.PI) + 1) / 2);
                if (elapsed >= cycle) { timer.Stop(); el.Opacity = 1.0; done(); }
            };
            timer.Start();
        }
        catch
        {
            try { done(); } catch { }
        }
    }

    #endregion

    #region lessons

    private static readonly Color LessonTrack = Color.FromRgb(0x2A, 0x26, 0x4C);
    private static readonly Color LessonFillA = AppColor("PinkColor", Colors.HotPink);
    private static readonly Color LessonFillB = Color.FromRgb(0x8B, 0x5C, 0xF6);
    private const double LESSON_BAR_WIDTH = 120;
    private const double LESSON_BAR_HEIGHT = 7;

    private Control BuildLessonLockPanel(string id, Color accent)
    {
        var def = ChaosLessons.ById(id);
        if (def == null) return new StackPanel();
        long target = Math.Max(1, def.Target);
        long progress = Math.Clamp(ChaosLessons.Progress(id), 0, target);

        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(new TextBlock
        {
            Text = "🔒 " + def.Text,
            Foreground = new SolidColorBrush(Color.FromArgb(0xB0, accent.R, accent.G, accent.B)),
            FontSize = 10.5,
            FontStyle = FontStyle.Italic,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Right,
        });

        double frac = (double)progress / target;
        var fill = new Border
        {
            CornerRadius = new CornerRadius(LESSON_BAR_HEIGHT / 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = progress <= 0 ? 0 : Math.Max(LESSON_BAR_HEIGHT, LESSON_BAR_WIDTH * frac),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = { new GradientStop(LessonFillA, 0), new GradientStop(LessonFillB, 1) }
            },
        };
        panel.Children.Add(new Border
        {
            Width = LESSON_BAR_WIDTH,
            Height = LESSON_BAR_HEIGHT,
            CornerRadius = new CornerRadius(LESSON_BAR_HEIGHT / 2),
            Background = new SolidColorBrush(LessonTrack),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 7, 0, 4),
            Child = fill,
        });

        panel.Children.Add(new TextBlock
        {
            Text = $"lesson: {progress} of {target}",
            Foreground = new SolidColorBrush(Color.FromArgb(0xA0, 0xB8, 0xB8, 0xD0)),
            FontSize = 10.5,
            HorizontalAlignment = HorizontalAlignment.Right,
        });

        ChaosTips.Attach(panel,
            progress <= 0 ? "the lesson" : "the lesson, half-learned",
            string.IsNullOrEmpty(def.Detail) ? def.Text : def.Detail,
            $"progress: {progress} of {target}", accent);
        return panel;
    }

    #endregion

    #region debug

    private static readonly bool DebugStripEnabled =
        string.Equals(Environment.GetEnvironmentVariable("CCP_CHAOS_DEBUG"), "1", StringComparison.Ordinal);

    private TextBox? _dbgRuns;
    private TextBox? _dbgLesson;
    private TextBox? _dbgReveal;

    private void BuildDebugStrip()
    {
        if (!DebugStripEnabled) return;
        try
        {
            if (Content is not Border root || root.Child is not Grid grid) return;
            Height += 44;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var panel = new WrapPanel { Margin = new Thickness(0, 10, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(DbgLabel("DEBUG"));

            _dbgRuns = DbgBox(ChaosMeta.State.RunsCompleted.ToString(), 42);
            panel.Children.Add(_dbgRuns);
            panel.Children.Add(DbgButton("set runs", Dbg_SetRuns));
            panel.Children.Add(DbgButton("+500 ✦", (_, _) => { ChaosMeta.State.Sparks += 500; ChaosMeta.Save(); DebugRefresh(); }));
            panel.Children.Add(DbgButton("+500 gold", (_, _) => { ChaosMeta.AddGold(500); DebugRefresh(); }));

            _dbgLesson = DbgBox("slow_fuses", 110);
            panel.Children.Add(_dbgLesson);
            panel.Children.Add(DbgButton("complete lesson", Dbg_CompleteLesson));

            _dbgReveal = DbgBox(RevealIds.TabLookingGlass, 110);
            panel.Children.Add(_dbgReveal);
            panel.Children.Add(DbgButton("force reveal", Dbg_ForceReveal));

            panel.Children.Add(DbgButton("reset meta", Dbg_ResetMeta));

            Grid.SetRow(panel, grid.RowDefinitions.Count - 1);
            grid.Children.Add(panel);
        }
        catch (Exception ex) { _logger?.LogWarning("Chaos debug strip build failed ({E})", ex.Message); }
    }

    private void Dbg_SetRuns(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse(_dbgRuns?.Text?.Trim(), out int n) || n < 0) return;
        ChaosMeta.State.RunsCompleted = n;
        ChaosMeta.Save();
        DebugRefresh();
    }

    private void Dbg_CompleteLesson(object? sender, RoutedEventArgs e)
    {
        var id = _dbgLesson?.Text?.Trim();
        if (string.IsNullOrEmpty(id)) return;
        var def = ChaosLessons.ById(id);
        if (def == null) return;
        if (def.HighWater) ChaosLessons.RaiseTo(id, def.Target);
        else ChaosLessons.Tick(id, def.Target);
        DebugRefresh();
    }

    private void Dbg_ForceReveal(object? sender, RoutedEventArgs e)
    {
        var id = _dbgReveal?.Text?.Trim();
        if (string.IsNullOrEmpty(id)) return;
        ChaosMeta.State.SeenReveals.Remove(id);
        ChaosMeta.State.PendingReveals.Add(id);
        ChaosMeta.Save();
        DebugRefresh();
    }

    private void Dbg_ResetMeta(object? sender, RoutedEventArgs e)
    {
        ChaosMeta.State = new ChaosMetaState();
        ChaosMeta.Save();
        DebugRefresh();
    }

    private void DebugRefresh()
    {
        try
        {
            RevealService.Sync("debug");
            ApplyExtremeGate();
            BuildHabits();
            BuildLifetimeBoons();
            BuildLoadoutTiles();
            BuildBench();
            BuildMantras();
            BuildDiary();
            RefreshTopBar();
            RefreshStats();
            ApplyReveals();
            RunRevealFlashes("debug");
            if (_dbgRuns != null) _dbgRuns.Text = ChaosMeta.State.RunsCompleted.ToString();
        }
        catch (Exception ex) { _logger?.LogWarning("Chaos debug refresh failed ({E})", ex.Message); }
    }

    private static TextBlock DbgLabel(string text) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xB4, 0x43)),
        FontFamily = new FontFamily("Consolas"), FontWeight = FontWeight.Bold, FontSize = 10,
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
    };

    private static TextBox DbgBox(string text, double width) => new()
    {
        Text = text, Width = width, FontSize = 11,
        Background = new SolidColorBrush(AppColor("ElevatedSurface", Color.FromRgb(0x22, 0x1F, 0x40))),
        Foreground = AppBrush("TextLightBrush", AppBrush("TextLightBrush", _whiteFallback)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xE8, 0xB4, 0x43)),
        VerticalContentAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(4, 2, 4, 2),
    };

    private Button DbgButton(string text, EventHandler<RoutedEventArgs> onClick)
    {
        var b = new Button
        {
            Content = text, FontSize = 11,
            Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Foreground = AppBrush("TextLightBrush", AppBrush("TextLightBrush", _whiteFallback)), BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        b.Click += onClick;
        return b;
    }

    #endregion

    #region habits / lifetime boons / loadout / mantras / diary

    private static Color BranchColor(ChaosBranch b) => b switch
    {
        ChaosBranch.Control => Color.FromRgb(0x49, 0xB6, 0xE8),
        ChaosBranch.Greed => Color.FromRgb(0xE8, 0xB4, 0x43),
        ChaosBranch.Depth => Color.FromRgb(0x8B, 0x5C, 0xF6),
        _ => AppColor("PinkColor", Colors.HotPink)
    };

    private static string BranchLabel(ChaosBranch b) => b switch
    {
        ChaosBranch.Control => "RESTRAINT",
        ChaosBranch.Greed => "CRAVING",
        _ => "DEPTH",
    };

    private static readonly string[] StarterShelfIds = { "start_resistance", "blank_eyes", "slow_fuses" };
    private static bool OnShelfNow(string id) =>
        ChaosMeta.State.RunsCompleted >= 2 || Array.IndexOf(StarterShelfIds, id) >= 0;

    private void BuildHabits()
    {
        HabitsHost.Children.Clear();
        foreach (var u in ChaosUpgrades.All)
            if (OnShelfNow(u.Id)) HabitsHost.Children.Add(BuildUpgradeRow(u));
        foreach (var b in ChaosLifetimeBoons.InCategory(ChaosBoonCategory.Utility))
            if (OnShelfNow(b.Id)) HabitsHost.Children.Add(BuildLifetimeBoonRow(b));
    }

    private Control BuildUpgradeRow(ChaosUpgrade u)
    {
        bool owned = ChaosMeta.IsOwned(u.Id);
        bool on = owned && ChaosMeta.IsUpgradeActive(u.Id);
        bool canBuy = ChaosMeta.CanAfford(u.Id);
        bool rankLocked = ChaosMeta.IsPurchaseRankLocked(u.Id);
        var accent = BranchColor(u.Branch);

        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var glyph = new TextBlock
        {
            Text = u.Glyph, FontSize = 18, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0), Foreground = new SolidColorBrush(accent),
        };
        Grid.SetColumn(glyph, 0);
        grid.Children.Add(glyph);

        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        mid.Children.Add(new TextBlock
        {
            Text = u.Name, Foreground = AppBrush("TextLightBrush", AppBrush("TextLightBrush", _whiteFallback)), FontSize = 12, FontWeight = FontWeight.SemiBold,
        });
        mid.Children.Add(new TextBlock
        {
            Text = u.Desc, Foreground = AppBrush("TextDimBrush", new SolidColorBrush(Color.FromRgb(0xAA, 0xB8, 0xB8))),
            FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0),
        });
        if (!string.IsNullOrEmpty(u.Flavor))
            mid.Children.Add(new TextBlock
            {
                Text = u.Flavor, Foreground = AppBrush("TextMutedBrush", new SolidColorBrush(Color.FromArgb(0xAA, 0xB0, 0xB0, 0xC8))),
                FontSize = 10.5, FontStyle = FontStyle.Italic, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
            });
        Grid.SetColumn(mid, 1);
        grid.Children.Add(mid);

        Control right;
        if (owned)
        {
            var toggle = new Button
            {
                Content = on ? "on ✓" : "off",
                Tag = u.Id,
                Padding = new Thickness(12, 6, 12, 6),
                Background = on ? new SolidColorBrush(accent) : AppBrush("TransparentWhiteBrush", new SolidColorBrush(Color.FromArgb(40, 255, 255, 255))),
                Foreground = on ? Brushes.Black : AppBrush("TextLightBrush", _whiteFallback),
                BorderThickness = new Thickness(0), FontSize = 11, FontWeight = FontWeight.Bold,
                Cursor = new Cursor(StandardCursorType.Hand),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Pillify(toggle);
            toggle.Click += UpgradeToggle_Click;
            right = toggle;
        }
        else if (rankLocked)
        {
            right = new TextBlock
            {
                Text = "🔒", FontSize = 13, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center,
            };
        }
        else
        {
            var buy = new Button
            {
                Content = $"train ✦{u.Cost:N0}",
                Tag = u.Id,
                Padding = new Thickness(12, 6, 12, 6),
                Background = canBuy ? new SolidColorBrush(accent) : AppBrush("TransparentWhiteBrush", new SolidColorBrush(Color.FromArgb(40, 255, 255, 255))),
                Foreground = canBuy ? Brushes.Black : AppBrush("TextMutedBrush", new SolidColorBrush(Color.FromRgb(0x88, 0xA0, 0xA0))),
                BorderThickness = new Thickness(0), FontSize = 11, FontWeight = FontWeight.Bold,
                Cursor = canBuy ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Arrow),
                IsEnabled = canBuy, VerticalAlignment = VerticalAlignment.Center,
            };
            Pillify(buy);
            buy.Click += UpgradeTrain_Click;
            right = buy;
        }
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        var card = new Border
        {
            Child = grid,
            Background = new SolidColorBrush(AppColor("ElevatedSurface", Color.FromRgb(0x22, 0x1F, 0x40))),
            BorderBrush = new SolidColorBrush(Color.FromArgb((byte)(owned ? 70 : 45), accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 6),
        };
        ChaosTips.Attach(card, u.Name, u.Desc, u.Flavor, accent);
        return card;
    }

    private void UpgradeToggle_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            ChaosMeta.SetUpgradeActive(id, !ChaosMeta.IsUpgradeActive(id));
            AvaloniaChaosSfx.Play(ChaosMeta.IsUpgradeActive(id) ? "ui_equip" : "ui_unequip", 0.45f);
            AfterBoonChange();
        }
    }

    private void UpgradeTrain_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            if (ChaosMeta.TryPurchase(id))
            {
                AfterBoonChange();
                AvaloniaChaosSfx.Play("ui_unlock", 0.55f);
            }
            else AvaloniaChaosSfx.Play("ui_denied", 0.45f);
        }
    }

    private void BuildLifetimeBoons()
    {
        BuildBoonShelf(BoonHostSkills, ChaosBoonCategory.Skill);
        BuildBoonShelf(BoonHostAccessories, ChaosBoonCategory.Accessory);
    }

    private void BuildBoonShelf(Panel host, ChaosBoonCategory cat)
    {
        host.Children.Clear();
        var boons = ChaosLifetimeBoons.InCategory(cat).ToList();
        if (boons.Count == 0)
        {
            host.Children.Add(new Border
            {
                Classes = { "CardStyle" },
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

    private Control BuildLifetimeBoonRow(ChaosLifetimeBoon b)
    {
        int level = ChaosMeta.BoonLevel(b.Id);
        bool unlocked = level >= 1;
        bool active = ChaosMeta.IsBoonActive(b.Id);
        bool rankLocked = ChaosMeta.IsBoonRankLocked(b.Id);
        bool canUnlock = ChaosMeta.CanAffordUnlock(b.Id);
        bool canUpgrade = level >= 1 && level < b.MaxLevel && ChaosMeta.CanAffordUpgrade(b.Id);
        var accent = b.Category switch
        {
            ChaosBoonCategory.Skill => Color.FromRgb(0x7A, 0xFF, 0xD2),
            ChaosBoonCategory.Accessory => Color.FromRgb(0xFF, 0xD2, 0x7A),
            _ => Color.FromRgb(0x7A, 0xE0, 0xFF),
        };

        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var glyph = new TextBlock
        {
            Text = b.Glyph, FontSize = 22, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0), Foreground = new SolidColorBrush(accent),
        };
        Grid.SetColumn(glyph, 0);
        grid.Children.Add(glyph);

        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new TextBlock
        {
            Text = b.Name, Foreground = AppBrush("TextLightBrush", AppBrush("TextLightBrush", _whiteFallback)), FontSize = 13, FontWeight = FontWeight.SemiBold,
        });
        if (unlocked && b.MaxLevel > 1)
            titleRow.Children.Add(new TextBlock
            {
                Text = " · L" + level, Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xB4, 0x43)),
                FontSize = 11, Margin = new Thickness(6, 0, 0, 0),
            });
        mid.Children.Add(titleRow);
        mid.Children.Add(new TextBlock
        {
            Text = b.Desc, Foreground = AppBrush("TextDimBrush", new SolidColorBrush(Color.FromRgb(0xAA, 0xB8, 0xB8))),
            FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0),
        });
        if (!string.IsNullOrEmpty(b.Flavor))
            mid.Children.Add(new TextBlock
            {
                Text = b.Flavor, Foreground = AppBrush("TextMutedBrush", new SolidColorBrush(Color.FromArgb(0xAA, 0xB0, 0xB0, 0xC8))),
                FontSize = 10.5, FontStyle = FontStyle.Italic, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
            });
        if (unlocked && level >= b.MaxLevel && !string.IsNullOrEmpty(b.CapstoneDesc))
            mid.Children.Add(new TextBlock
            {
                Text = "max: " + b.CapstoneDesc, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x3C)),
                FontSize = 10.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0),
            });
        Grid.SetColumn(mid, 1);
        grid.Children.Add(mid);

        Control right;
        if (rankLocked)
        {
            right = new TextBlock
            {
                Text = "🔒", FontSize = 13, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center,
            };
        }
        else if (!unlocked)
        {
            var buy = new Button
            {
                Content = $"unlock ✦{b.UnlockCost:N0}",
                Tag = b.Id,
                Padding = new Thickness(12, 6, 12, 6),
                Background = canUnlock ? new SolidColorBrush(accent) : AppBrush("TransparentWhiteBrush", new SolidColorBrush(Color.FromArgb(40, 255, 255, 255))),
                Foreground = canUnlock ? Brushes.Black : AppBrush("TextMutedBrush", new SolidColorBrush(Color.FromRgb(0x88, 0xA0, 0xA0))),
                BorderThickness = new Thickness(0), FontSize = 11, FontWeight = FontWeight.Bold,
                Cursor = canUnlock ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Arrow),
                IsEnabled = canUnlock, VerticalAlignment = VerticalAlignment.Center,
            };
            Pillify(buy);
            buy.Click += BoonUnlock_Click;
            right = buy;
        }
        else if (level < b.MaxLevel)
        {
            var deepen = new Button
            {
                Content = "deepen",
                Tag = b.Id,
                Padding = new Thickness(12, 6, 12, 6),
                Background = canUpgrade ? new SolidColorBrush(accent) : AppBrush("TransparentWhiteBrush", new SolidColorBrush(Color.FromArgb(40, 255, 255, 255))),
                Foreground = canUpgrade ? Brushes.Black : AppBrush("TextMutedBrush", new SolidColorBrush(Color.FromRgb(0x88, 0xA0, 0xA0))),
                BorderThickness = new Thickness(0), FontSize = 11, FontWeight = FontWeight.Bold,
                Cursor = canUpgrade ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Arrow),
                IsEnabled = canUpgrade, VerticalAlignment = VerticalAlignment.Center,
            };
            Pillify(deepen);
            deepen.Click += BoonUpgrade_Click;
            right = deepen;
        }
        else
        {
            bool canEquip = active || ChaosMeta.HasFreePocket(b.Category);
            var equip = new Button
            {
                Content = active ? "unequip" : "equip",
                Tag = b.Id,
                Padding = new Thickness(12, 6, 12, 6),
                Background = active ? new SolidColorBrush(accent) : AppBrush("TransparentWhiteBrush", new SolidColorBrush(Color.FromArgb(40, 255, 255, 255))),
                Foreground = active ? Brushes.Black : AppBrush("TextLightBrush", _whiteFallback),
                BorderThickness = new Thickness(0), FontSize = 11, FontWeight = FontWeight.Bold,
                Cursor = canEquip ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Arrow),
                IsEnabled = canEquip, VerticalAlignment = VerticalAlignment.Center,
            };
            Pillify(equip);
            equip.Click += BoonEquip_Click;
            right = equip;
        }
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        var card = new Border
        {
            Child = grid,
            Background = new SolidColorBrush(AppColor("ElevatedSurface", Color.FromRgb(0x22, 0x1F, 0x40))),
            BorderBrush = new SolidColorBrush(Color.FromArgb((byte)(unlocked ? 70 : 45), accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 6),
        };
        ChaosTips.Attach(card, b.Name, b.Desc,
            unlocked && b.MaxLevel > 1 ? $"level {level}/{b.MaxLevel}" : null, accent, b.Flavor);
        return card;
    }

    private static Button BuyButton(string text, string id, bool afford, EventHandler<RoutedEventArgs> onClick)
    {
        var btn = new Button
        {
            Content = text,
            Tag = id,
            Padding = new Thickness(20, 10, 20, 10),
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = afford ? new SolidColorBrush(AppColor("PinkColor", Colors.HotPink)) : AppBrush("TransparentWhiteBrush", new SolidColorBrush(Color.FromArgb(40, 255, 255, 255))),
            Foreground = afford ? AppBrush("TextLightBrush", _whiteFallback) : AppBrush("TextMutedBrush", new SolidColorBrush(Color.FromRgb(0x88, 0xA0, 0xA0))),
            BorderThickness = new Thickness(0),
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            Cursor = afford ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Arrow),
            IsEnabled = afford
        };
        btn.Click += onClick;
        return btn;
    }

    private static void Pillify(Button b, double radius = 11)
    {
        b.CornerRadius = new CornerRadius(radius);
    }

    private void BoonEquip_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            ChaosMeta.SetBoonActive(id, !ChaosMeta.IsBoonActive(id));
            AvaloniaChaosSfx.Play(ChaosMeta.IsBoonActive(id) ? "ui_equip" : "ui_unequip", 0.45f);
            AfterBoonChange();
        }
    }

    private void BoonUnlock_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            if (ChaosMeta.TryUnlockBoon(id))
            {
                AfterBoonChange();
                ShowUnlockCard(ChaosUnlockCards.ForBoonUnlock(id));
            }
            else AvaloniaChaosSfx.Play("ui_denied", 0.45f);
        }
    }

    private void BoonUpgrade_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            if (ChaosMeta.TryUpgradeBoon(id))
            {
                AfterBoonChange();
                var b = ChaosLifetimeBoons.ById(id);
                var capstoneCard = b != null && ChaosMeta.BoonLevel(id) >= b.MaxLevel
                    ? ChaosUnlockCards.ForCapstone(id) : null;
                if (capstoneCard != null) ShowUnlockCard(capstoneCard);
                else AvaloniaChaosSfx.Play("ui_deepen", 0.5f);
            }
            else AvaloniaChaosSfx.Play("ui_denied", 0.45f);
        }
    }

    private readonly Queue<ChaosUnlockCardData> _unlockCards = new();
    private bool _unlockCardShowing;
    private const int UNLOCK_CARD_HOLD_MS = 4500;

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
        card.Margin = new Thickness(0, 96, 0, 0);
        var slide = new TranslateTransform(0, -14);
        card.RenderTransform = slide;
        card.Opacity = 0;
        UnlockCardLayer.Children.Add(card);

        new OpacityFade(card, 0, 1, 180);
        AnimateDouble(slide, TranslateTransform.YProperty, -14, 0, 220, EaseOutBack);

        var hold = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(UNLOCK_CARD_HOLD_MS) };
        hold.Tick += (_, _) =>
        {
            hold.Stop();
            new OpacityFade(card, 1, 0, 260, () =>
            {
                try { UnlockCardLayer.Children.Remove(card); } catch { }
                ShowNextUnlockCard();
            });
        };
        hold.Start();
    }

    private void AfterBoonChange()
    {
        BuildLifetimeBoons();
        BuildHabits();
        BuildLoadoutTiles();
        RefreshTopBar();
        RefreshStats();
        AvaloniaChaosApp.Chaos?.NotifyLoadoutChanged();
    }

    public void RefreshAfterExternalLoadoutChange()
    {
        BuildLifetimeBoons();
        BuildLoadoutTiles();
        RefreshTopBar();
    }

    private void BuildLoadoutTiles()
    {
        PocketSlotsHost.Children.Clear();
        TxtAccCount.Text = $"{ChaosMeta.EquippedCountIn(ChaosBoonCategory.Accessory)}/{ChaosMeta.SlotsFor(ChaosBoonCategory.Accessory)} equipped";
        TxtSkillCount.Text = $"{ChaosMeta.EquippedCountIn(ChaosBoonCategory.Skill)}/{ChaosMeta.SlotsFor(ChaosBoonCategory.Skill)} equipped";
        FillCategoryTiles(TilesAccessories, ChaosBoonCategory.Accessory, padTo: 8);
        FillCategoryTiles(TilesSkills, ChaosBoonCategory.Skill, padTo: 8);

        TilesHabits.Children.Clear();
        int trained = 0, switchedOn = 0;
        foreach (var u in ChaosUpgrades.All.Where(u => OnShelfNow(u.Id)))
        {
            bool owned = ChaosMeta.IsOwned(u.Id);
            bool on = owned && ChaosMeta.IsUpgradeActive(u.Id);
            if (owned) trained++;
            if (on) switchedOn++;
            TilesHabits.Children.Add(LoadoutTile(u.Glyph, u.Name, u.Desc,
                on ? "click to switch off" : owned ? "click to switch on" : $"train for ✦{u.Cost} in the Toybox",
                BranchColor(u.Branch),
                on ? TileState.Equipped : owned ? TileState.Owned : TileState.Locked,
                () => { },
                cornerBadge: on ? "✓" : null));
        }
        TxtHabitCount.Text = $"{switchedOn} on · {trained}/{ChaosUpgrades.All.Count} trained";
    }

    private Control? PocketGroup(string label, ChaosBoonCategory cat) => null;
    private void FillCategoryTiles(Panel host, ChaosBoonCategory cat, int padTo)
    {
        host.Children.Clear();
        foreach (var b in ChaosLifetimeBoons.InCategory(cat))
        {
            bool unlocked = ChaosMeta.IsBoonUnlocked(b.Id);
            bool active = ChaosMeta.IsBoonActive(b.Id);
            var state = active ? TileState.Equipped : unlocked ? TileState.Owned : TileState.Locked;
            var accent = b.Category switch
            {
                ChaosBoonCategory.Skill => Color.FromRgb(0x7A, 0xFF, 0xD2),
                ChaosBoonCategory.Accessory => Color.FromRgb(0xFF, 0xD2, 0x7A),
                _ => AppColor("PinkColor", Colors.HotPink),
            };
            Action? onClick = unlocked ? () => ToggleEquip(b.Id) : null;
            host.Children.Add(LoadoutTile(b.Glyph, b.Name, b.Desc, "", accent, state, onClick,
                caption: b.Name.Split(" · ")[0], flavor: b.Flavor));
        }
        int emptySlots = Math.Max(0, padTo - ChaosLifetimeBoons.InCategory(cat).Count());
        for (int i = 0; i < emptySlots; i++)
            host.Children.Add(LoadoutTile("+", "empty pocket", "buy a pocket on the bench to carry more.", "",
                Color.FromRgb(0x88, 0xA0, 0xC0), TileState.Empty, () => NavigateTo("improve")));
    }

    private void ToggleEquip(string id)
    {
        ChaosMeta.SetBoonActive(id, !ChaosMeta.IsBoonActive(id));
        AvaloniaChaosSfx.Play(ChaosMeta.IsBoonActive(id) ? "ui_equip" : "ui_unequip", 0.45f);
        AfterBoonChange();
    }
    private void EquipSwapping(string id, ChaosBoonCategory cat) { }

    private enum TileState { Equipped, Owned, Locked, Empty }

    private Control LoadoutTile(string glyph, string title, string? desc, string? extra, Color accent,
                                TileState state, Action? onClick, string? cornerBadge = null,
                                IImage? art = null, double size = 96, string? caption = null,
                                string? flavor = null)
    {
        var tile = new Border
        {
            Width = size, Height = size,
            CornerRadius = new CornerRadius(12),
            ClipToBounds = true,
            Background = state switch
            {
                TileState.Equipped => new SolidColorBrush(Color.FromArgb(80, accent.R, accent.G, accent.B)),
                TileState.Owned => new SolidColorBrush(Color.FromArgb(45, accent.R, accent.G, accent.B)),
                TileState.Locked => new SolidColorBrush(AppColor("ElevatedSurface", Color.FromRgb(0x22, 0x1F, 0x40))),
                _ => Brushes.Transparent,
            },
            Child = new TextBlock
            {
                Text = glyph,
                FontSize = size >= 114 ? 46 : 36,
                Foreground = AppBrush("TextLightBrush", AppBrush("TextLightBrush", _whiteFallback)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }
        };
        var cell = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 22),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Cursor = onClick != null ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Arrow),
        };
        cell.Children.Add(tile);
        cell.Children.Add(new TextBlock
        {
            Text = caption ?? (state is TileState.Locked or TileState.Empty ? "???" : title.Split(" · ")[0]),
            FontSize = 12,
            Foreground = state == TileState.Equipped ? new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)) : AppBrush("TextLightBrush", _whiteFallback),
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = size + 36,
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        if (onClick != null) cell.PointerPressed += (_, _) => onClick();
        ChaosTips.Attach(cell, title, desc, extra, accent, flavor: flavor);
        return cell;
    }

    private void BuildMantras()
    {
        MantrasHost.Children.Clear();
        foreach (var b in ChaosBoonPool.All)
            MantrasHost.Children.Add(MantraRow(b));
    }

    private Control MantraRow(ChaosBoon b)
    {
        var accent = b.IsCurse ? Color.FromRgb(0xFF, 0x78, 0x78)
                   : b.Rarity == ChaosRarity.Rare ? Color.FromRgb(0xFF, 0xC8, 0x3C)
                   : b.Rarity == ChaosRarity.Uncommon ? Color.FromRgb(0x8B, 0x5C, 0xF6)
                   : Color.FromRgb(0x7A, 0xE0, 0xFF);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Border
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(45, accent.R, accent.G, accent.B)),
            BorderBrush = new SolidColorBrush(accent), BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
            Child = new TextBlock
            {
                Text = b.IsCurse ? "☠" : "◈", Foreground = new SolidColorBrush(accent),
                FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        });
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 280 };
        mid.Children.Add(new TextBlock
        {
            Text = b.Name, Foreground = AppBrush("TextLightBrush", AppBrush("TextLightBrush", _whiteFallback)), FontSize = 12, FontWeight = FontWeight.SemiBold,
        });
        mid.Children.Add(new TextBlock
        {
            Text = b.Desc, Foreground = AppBrush("TextDimBrush", new SolidColorBrush(Color.FromRgb(0xAA, 0xB8, 0xB8))),
            FontSize = 11, TextWrapping = TextWrapping.Wrap,
        });
        row.Children.Add(mid);

        var card = new Border
        {
            Child = row,
            Background = new SolidColorBrush(AppColor("ElevatedSurface", Color.FromRgb(0x22, 0x1F, 0x40))),
            BorderBrush = new SolidColorBrush(Color.FromArgb(45, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 6),
        };
        ChaosTips.Attach(card, b.Name, b.Desc, accent: accent, flavor: b.Flavor);
        return card;
    }

    private void BuildDiary()
    {
        DiaryHost.Children.Clear();
        FillDiaryRows(DiaryHost, 270);
    }

    private static readonly (string Glyph, string Name, string Desc)[] DiaryVerbs =
    {
        ("✋", "hold to snap", "press and HOLD a live (ringed) bubble about a second to defuse it — costs 30 focus."),
        ("○", "click the treats", "a tap pops a treat: its payload plays, the streak climbs, and +10 focus flows back."),
        ("🌊", "right-click · the ripple", "casts a wave from your cursor. treats pop, trances snap, rabbits get flung."),
        ("◌", "focus", "the defuse fuel: max 100, you fall in with 50."),
        ("🔥", "lust", "the orange bar. climbs while you perform and pays up to x2 at full burn."),
        ("💨", "never let treats rot", "a treat that fades unpopped HALVES your streak."),
        ("🐇", "catch the white rabbit", "everything slows to a crawl for six seconds."),
        ("❄", "the pickups", "freeze holds the whole field 3.5 seconds."),
        ("⏸", "your panic key", "one press holds the field mid-fall."),
    };

    private void FillDiaryRows(Panel host, double maxWidth)
    {
        host.Children.Add(SubHeader("VERBS · how to play down there"));
        foreach (var v in DiaryVerbs)
            host.Children.Add(VerbRow(v.Glyph, v.Name, v.Desc, maxWidth));
        host.Children.Add(SubHeader("WHAT YOU'VE MET"));
        foreach (var v in ChaosBubbleVariants.All)
            host.Children.Add(CodexRow("bubble:" + v.Id, v.Name, ChaosBubbleVariants.DescriptionFor(v.Id),
                ChaosArt.Resolve("bubbles", v.Id), "●", v.Tint, maxWidth));
    }

    private Window? _diaryPopout;
    private void Diary_PopOut(object? sender, PointerPressedEventArgs e)
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
            Foreground = new SolidColorBrush(AppColor("PinkColor", Colors.HotPink)),
            FontFamily = new FontFamily("Consolas"), FontWeight = FontWeight.Bold, FontSize = 13,
            Margin = new Thickness(0, 0, 0, 12)
        });
        FillDiaryRows(host, 440);
        _diaryPopout = new Window
        {
            Title = "Diary",
            Width = 580, Height = 680,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x11, 0x26)),
            Content = new ScrollViewer { VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = host },
        };
        _diaryPopout.Closed += (_, _) => _diaryPopout = null;
        _diaryPopout.Show();
    }

    private static Control ArtIcon(IImage src, double size, double radius, Color accent, double ring = 2.5)
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
        Text = text, Foreground = new SolidColorBrush(AppColor("PinkColor", Colors.HotPink)),
        FontFamily = new FontFamily("Consolas"), FontWeight = FontWeight.Bold, FontSize = 11,
        Margin = new Thickness(0, 12, 0, 6)
    };

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
        mid.Children.Add(new TextBlock { Text = name, Foreground = AppBrush("TextLightBrush", AppBrush("TextLightBrush", _whiteFallback)), FontSize = 12, FontWeight = FontWeight.SemiBold });
        mid.Children.Add(new TextBlock { Text = desc, Foreground = AppBrush("TextDimBrush", new SolidColorBrush(Color.FromRgb(0xAA, 0xB8, 0xB8))), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        row.Children.Add(mid);
        return new Border
        {
            Child = row,
            Background = new SolidColorBrush(AppColor("ElevatedSurface", Color.FromRgb(0x22, 0x1F, 0x40))),
            BorderBrush = new SolidColorBrush(Color.FromArgb(55, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 6),
        };
    }

    private Border CodexRow(string codexId, string name, string desc, IImage? iconSrc, string glyph, Color accent, double maxWidth = 270)
    {
        bool seen = ChaosMeta.IsDiscovered(codexId);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        Control icon;
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
        if (seen) ChaosTips.Attach((Control)icon, name, desc, accent: accent);
        row.Children.Add(icon);

        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MaxWidth = maxWidth };
        mid.Children.Add(new TextBlock { Text = seen ? name : "???", Foreground = seen ? AppBrush("TextLightBrush", _whiteFallback) : AppBrush("TextMutedBrush", new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x90))), FontSize = 12, FontWeight = FontWeight.SemiBold });
        mid.Children.Add(new TextBlock { Text = seen ? desc : "hazy. go back down and look closer.", Foreground = AppBrush("TextDimBrush", new SolidColorBrush(Color.FromRgb(0xAA, 0xB8, 0xB8))), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        row.Children.Add(mid);

        return new Border
        {
            Child = row,
            Background = new SolidColorBrush(AppColor("ElevatedSurface", Color.FromRgb(0x22, 0x1F, 0x40))),
            BorderBrush = new SolidColorBrush(Color.FromArgb(seen ? (byte)70 : (byte)25, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 6)
        };
    }

    #endregion

    #region bench

    private sealed class BenchItem
    {
        public string Id = "";
        public string Glyph = "👝";
        public string Label = "";
        public string Line = "";
        public int Cost;
        public ChaosRank? RankNeed;
        public string? RevealGate;
        public Action? ApplyEffect;
    }

    private List<BenchItem>? _benchItems;
    private List<BenchItem> BenchItems => _benchItems ??= new List<BenchItem>
    {
        new() { Id = BenchIds.ToyPocket1, Glyph = "👝", Label = "first toy pocket", Line = "she sews you a pocket.", Cost = 50, ApplyEffect = () => ChaosMeta.State.ToyPockets++ },
        new() { Id = BenchIds.AccPocket1, Glyph = "👝", Label = "first accessory pocket", Line = "she only has two hands. she found a third.", Cost = 150, ApplyEffect = () => ChaosMeta.State.AccessoryPockets++ },
        new() { Id = BenchIds.StartMantra, Glyph = "◈", Label = "the starting mantra", Line = "fall in holding something.", Cost = 200 },
        new() { Id = BenchIds.Diary, Glyph = "📓", Label = "the diary", Line = "she keeps notes on what you meet down there.", Cost = 150 },
        new() { Id = BenchIds.StatsPanel, Glyph = "🕰", Label = "the stats panel", Line = "the numbers, if you want them.", Cost = 100 },
        new() { Id = BenchIds.ToyPocket2, Glyph = "👝", Label = "second toy pocket", Line = "she found room for one more.", Cost = 2000, RankNeed = ChaosRank.Devoted, RevealGate = RevealIds.BenchToyPocket2, ApplyEffect = () => ChaosMeta.State.ToyPockets++ },
        new() { Id = BenchIds.AccPocket2, Glyph = "👝", Label = "second accessory pocket", Line = "a fourth hand. don't ask.", Cost = 2500, RankNeed = ChaosRank.Devoted, RevealGate = RevealIds.BenchAccPocket2, ApplyEffect = () => ChaosMeta.State.AccessoryPockets++ },
    };

    private static readonly string[] ReservedRows =
    {
        "the clocks", "descent ledger", "payout eyes", "the fine print",
        "fall right in", "held breath", "soft landing", "no countdown",
        "dollhouse wallpapers", "recap frames", "a chattier companion", "the pact",
    };

    private static readonly string[] ClaimedReservedRows = { "daily descent", "leaderboard", "prestige" };

    private void BuildBench()
    {
        ImprovementsHost.Children.Clear();
        ImprovementsHost.Children.Add(GoldBalanceLine());
        foreach (var item in BenchItems)
        {
            var row = BenchRow(item);
            ImprovementsHost.Children.Add(row);
            if (item.RevealGate == RevealIds.BenchToyPocket2) _revealMap[RevealIds.BenchToyPocket2] = row;
            if (item.RevealGate == RevealIds.BenchAccPocket2) _revealMap[RevealIds.BenchAccPocket2] = row;
        }
        foreach (var name in ReservedRows)
            ImprovementsHost.Children.Add(HazyRow(name, WALL_TIP));
        if (ChaosMeta.AtLeast(ChaosRank.Devoted))
            foreach (var name in ClaimedReservedRows)
                ImprovementsHost.Children.Add(HazyRow(name, BOTTOM_TIP));
    }

    private void BuildHerCorner()
    {
        HerCornerHost.Children.Clear();
        HerCornerHost.Children.Add(GoldBalanceLine());
        foreach (var id in new[] { BenchIds.ToyPocket1, BenchIds.AccPocket1 })
        {
            var item = BenchItems.First(i => i.Id == id);
            HerCornerHost.Children.Add(BenchRow(item));
        }
    }

    private TextBlock GoldBalanceLine() => new()
    {
        Text = $"you're carrying {ChaosGlyphs.Gold} {ChaosMeta.State.Gold:N0}",
        Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xB4, 0x43)),
        FontSize = 11, FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 0, 0, 8),
    };

    private const string DEEPER_TIP = "she'll sell this to someone deeper.";
    private const string BOTTOM_TIP = "the bottom is not where you think it is.";

    private Border BenchRow(BenchItem item)
    {
        bool revealed = item.RevealGate == null || RevealService.IsUnlocked(item.RevealGate);
        if (!revealed) return HazyRow("???", WALL_TIP);

        bool owned = ChaosMeta.State.BenchPurchases.Contains(item.Id);
        bool rankShort = item.RankNeed.HasValue && !ChaosMeta.AtLeast(item.RankNeed.Value);
        var goldColor = Color.FromRgb(0xE8, 0xB4, 0x43);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var glyph = new TextBlock
        {
            Text = item.Glyph, FontSize = 16, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0), Opacity = owned ? 1.0 : 0.7,
        };
        Grid.SetColumn(glyph, 0);
        grid.Children.Add(glyph);

        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        mid.Children.Add(new TextBlock
        {
            Text = item.Label,
            Foreground = owned ? AppBrush("TextLightBrush", _whiteFallback) : AppBrush("TextDimBrush", new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xE0))),
            FontSize = 12, FontWeight = FontWeight.SemiBold,
        });
        mid.Children.Add(new TextBlock
        {
            Text = item.Line,
            Foreground = AppBrush("TextMutedBrush", new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xB8))),
            FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0),
        });
        Grid.SetColumn(mid, 1);
        grid.Children.Add(mid);

        Control right;
        if (owned)
        {
            right = new TextBlock
            {
                Text = "sewn ✓",
                Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0xE0, 0x96)),
                FontSize = 11, FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        else if (rankShort)
        {
            right = new TextBlock
            {
                Text = "🔒",
                FontSize = 13, Opacity = 0.7,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        else
        {
            bool afford = ChaosMeta.State.Gold >= item.Cost;
            var buy = new Button
            {
                Content = $"buy  {ChaosGlyphs.Gold} {item.Cost:N0}",
                Tag = item.Id,
                Padding = new Thickness(14, 6, 14, 6),
                Background = afford ? new SolidColorBrush(goldColor) : AppBrush("TransparentWhiteBrush", new SolidColorBrush(Color.FromArgb(40, 255, 255, 255))),
                Foreground = afford ? Brushes.Black : AppBrush("TextMutedBrush", new SolidColorBrush(Color.FromRgb(0x88, 0xA0, 0xA0))),
                BorderThickness = new Thickness(0),
                FontSize = 12, FontWeight = FontWeight.Bold,
                Cursor = new Cursor(StandardCursorType.Hand),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Pillify(buy);
            buy.Click += BenchBuy_Click;
            right = buy;
        }
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        var card = new Border
        {
            Child = grid,
            Background = new SolidColorBrush(AppColor("ElevatedSurface", Color.FromRgb(0x22, 0x1F, 0x40))),
            BorderBrush = new SolidColorBrush(Color.FromArgb(owned ? (byte)70 : (byte)45, goldColor.R, goldColor.G, goldColor.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 6),
            Opacity = rankShort ? 0.6 : 1.0,
        };
        if (rankShort) ChaosTips.Attach(card, item.Label, DEEPER_TIP,
            item.RankNeed.HasValue ? ChaosRanks.RankSpecifics(item.RankNeed.Value) : null);
        else ChaosTips.Attach(card, item.Label, item.Line, accent: goldColor);
        return card;
    }

    private Border HazyRow(string name, string tip)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = "▢", FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 11, 0), Opacity = 0.4 });
        row.Children.Add(new TextBlock
        {
            Text = name,
            Foreground = AppBrush("TextMutedBrush", new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x90))),
            FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
        });
        var card = new Border
        {
            Child = row,
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1B, 0x38)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(20, 0xE8, 0x43, 0x93)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 6),
            Opacity = 0.55,
        };
        ChaosTips.Attach(card, name, tip);
        return card;
    }

    private void BenchBuy_Click(object? sender, RoutedEventArgs e)
    {
        var id = (sender as Button)?.Tag?.ToString();
        if (string.IsNullOrEmpty(id)) return;
        var item = BenchItems.FirstOrDefault(i => i.Id == id);
        if (item == null || ChaosMeta.State.BenchPurchases.Contains(item.Id)) return;
        if (item.RankNeed.HasValue && !ChaosMeta.AtLeast(item.RankNeed.Value)) return;
        if (item.RevealGate != null && !RevealService.IsUnlocked(item.RevealGate)) return;

        bool paid = ChaosMeta.TrySpendGold(item.Cost);
        if (!paid)
        {
            if (item.Id == BenchIds.ToyPocket1 && !ChaosMeta.State.GiftGiven)
            {
                ChaosMeta.State.GiftGiven = true;
                ChaosMeta.State.Gold = 0;
                paid = true;
                try { AvaloniaChaosApp.Bark?.NotifyChaosGiftGiven(); } catch { }
            }
            else
            {
                AvaloniaChaosSfx.Play("ui_denied", 0.45f);
                return;
            }
        }

        ChaosMeta.State.BenchPurchases.Add(item.Id);
        try { item.ApplyEffect?.Invoke(); }
        catch (Exception ex) { _logger?.LogWarning("Bench effect {Id} failed ({E})", item.Id, ex.Message); }
        ChaosMeta.Save();
        bool cardFollows = item.Id is BenchIds.ToyPocket1 or BenchIds.ToyPocket2
                                   or BenchIds.AccPocket1 or BenchIds.AccPocket2;
        if (!cardFollows) AvaloniaChaosSfx.Play("ui_unlock", 0.55f);

        RevealService.Sync("purchase");
        ApplyReveals();
        BuildBench();
        if (HerCornerCard.IsVisible) BuildHerCorner();
        BuildLifetimeBoons();
        BuildLoadoutTiles();
        RefreshTopBar();
        RefreshStats();
        AvaloniaChaosApp.Chaos?.NotifyLoadoutChanged();
        RunRevealFlashes("purchase");

        if (item.Id is BenchIds.ToyPocket1 or BenchIds.ToyPocket2)
            ShowUnlockCard(ChaosUnlockCards.ForPocket(isToy: true, item.Label, item.Line));
        else if (item.Id is BenchIds.AccPocket1 or BenchIds.AccPocket2)
            ShowUnlockCard(ChaosUnlockCards.ForPocket(isToy: false, item.Label, item.Line));
    }

    #endregion

    private static void AnimateDouble(TranslateTransform target, AvaloniaProperty property, double from, double to, int ms, Func<double, double> ease)
    {
        target.X = from;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        double startMs = Environment.TickCount64;
        timer.Tick += (_, _) =>
        {
            double t = Math.Min(1, (Environment.TickCount64 - startMs) / ms);
            target.X = from + (to - from) * ease(t);
            if (t >= 1) timer.Stop();
        };
        timer.Start();
    }

    private static double EaseOutBack(double t)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1;
        return t >= 1 ? 1 : 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
    }
}
