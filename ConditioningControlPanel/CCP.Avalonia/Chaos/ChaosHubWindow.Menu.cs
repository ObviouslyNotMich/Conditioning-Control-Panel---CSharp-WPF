using System;
using System.Collections.Generic;
using System.Linq;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Controls.Documents;
using global::Avalonia.Controls.Shapes;
using global::Avalonia.Input;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using global::Avalonia.Interactivity;
using global::Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Chaos;

public partial class ChaosHubWindow
{
    // ============================ main menu / view swap ============================

    private bool _runContext;

    private void EnterRunContext()
    {
        if (_runContext) return;
        _runContext = true;
        AvaloniaChaosApp.Chaos?.ShowLoadoutSidebar();
        AvaloniaChaosApp.Avatar?.SetChaosRunActive(true);
    }

    private void LeaveRunContext()
    {
        if (!_runContext) return;
        _runContext = false;
        AvaloniaChaosApp.Chaos?.CloseLoadoutSidebar();
        if (!_fallingIn) AvaloniaChaosApp.Avatar?.SetChaosRunActive(false);
    }

    private void ShowMenuView()
    {
        MenuView.IsVisible = true;
        DollhouseView.IsVisible = false;
        MenuLeftCol.IsVisible = true;
        MenuArtPanel.IsVisible = true;
        MenuOptions.IsVisible = false;

        var logo = ChaosArt.TryLoad(ChaosArt.FilePath("menu_logo.png"));
        if (logo != null && MenuLogo != null) MenuLogo.Source = logo;

        if (BtnMenuStory != null) BtnMenuStory.IsEnabled = AvaloniaChaosMode.StoryModeEnabled;

        RefreshTopBar();
        MenuArtBox?.Start();
        StartMenuMusic();
    }

    private void ShowDollhouseView()
    {
        MenuView.IsVisible = false;
        DollhouseView.IsVisible = true;
        MenuArtBox?.Stop();
        StopMenuMusic();
    }

    private void Menu_FallIn_Click(object? sender, RoutedEventArgs e)
    {
        StopMenuMusic();
        AvaloniaChaosApp.Avatar?.SetChaosRunActive(true);
        FallIn();
    }

    private void Menu_Dollhouse_Click(object? sender, RoutedEventArgs e)
    {
        EnterRunContext();
        MenuArtBox?.Stop();
        ShowDollhouseView();
        ShowTab("loadout");
    }

    private void Menu_Story_Click(object? sender, RoutedEventArgs e) { /* story locked until content ships */ }

    private void Menu_Options_Click(object? sender, RoutedEventArgs e)
    {
        OptFullscreen.IsChecked = WindowState == WindowState.Maximized;
        OptKeepTop.IsChecked = ChkPinTop.IsChecked;
        OptSkiaFx.IsChecked = ChkSkiaFx.IsChecked;
        OptAnnounce.IsChecked = ChkAnnouncer.IsChecked;
        OptSldEffect.Value = SldEffect.Value;

        MenuLeftCol.IsVisible = false;
        MenuArtPanel.IsVisible = false;
        MenuOptions.IsVisible = true;
        MenuArtBox?.Stop();
    }

    private void Options_Back_Click(object? sender, RoutedEventArgs e)
    {
        SyncOptionsToControls();
        SaveToSettings();
        MenuOptions.IsVisible = false;
        MenuLeftCol.IsVisible = true;
        MenuArtPanel.IsVisible = true;
        MenuArtBox?.Start();
    }

    private void Menu_Exit_Click(object? sender, RoutedEventArgs e) => Close();

    private void Back_To_Menu_Click(object? sender, RoutedEventArgs e)
    {
        SaveToSettings();
        LeaveRunContext();
        ShowMenuView();
    }

    // ============================ window chrome ============================

    private void DragBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void MenuTitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void BtnMin_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void BtnFull_Click(object? sender, RoutedEventArgs e) => SetFullscreen(WindowState != WindowState.Maximized);

    private void OptFullscreen_Click(object? sender, RoutedEventArgs e) => SetFullscreen(OptFullscreen.IsChecked == true);

    private void SetFullscreen(bool on) => WindowState = on ? WindowState.Maximized : WindowState.Normal;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
        {
            bool max = WindowState == WindowState.Maximized;
            if (OptFullscreen != null) OptFullscreen.IsChecked = max;
            if (max) AvaloniaChaosApp.Avatar?.SetChaosRunActive(true);
            else if (!_runContext && !_fallingIn) AvaloniaChaosApp.Avatar?.SetChaosRunActive(false);
        }
    }

    // ============================ menu art click ============================

    private void MenuArt_Click(object? sender, PointerPressedEventArgs e)
    {
        MenuArtBox?.Advance();
    }

    // ======================= HOW TO PLAY (card tutorial overlay) =======================

    private sealed record HowToLine(string Emoji, string EmojiColor, string Lead, string LeadColor, string Body);
    private sealed record HowToCard(string Title, string Image, HowToLine[] Lines);

    private static readonly HowToCard[] _howToCards =
    {
        new("What the Rabbit Hole is", "howto_1", new[]
        {
            new HowToLine("", "", "", "",
                "Bubbles drift up the screen carrying flashes, videos and overlays. Pop the good ones, snap the dangerous ones before they go off, and ride it deeper. One descent is about **five minutes** — survive the waves, take what she offers, climb out a little more hers."),
        }),
        new("What you do", "howto_2", new[]
        {
            new HowToLine("🫧", "#FFFF9FD0", "Left-click", "#FFFF9FD0", "pop the treats — the soft pink bubbles. One click builds your streak and refills your focus."),
            new HowToLine("◉", "#FFFFD228", "Press & hold", "#FFFFD228", "the glowing bubbles are live. Keep pressing until they snap — let one finish and it goes off (a flash or video fires)."),
            new HowToLine("🌊", "#FF7AE0FF", "Right-click", "#FF7AE0FF", "the ripple. A wave near the bubbles pops treats, snaps live ones and scatters rabbits. Strong, but slow to gather again."),
            new HowToLine("🐇", "#FFFF69B4", "The rabbits", "#FFFF69B4", "chase them for little bonuses. Everything else down there is yours to find out."),
        }),
        new("The two bars", "howto_3", new[]
        {
            new HowToLine("", "", "FOCUS", "#FFFFFFFF", "your nerve. Snapping live bubbles spends it; popping treats refills it. Run dry and you can't snap — so keep feeding."),
            new HowToLine("", "", "HEAT", "#FFFFFFFF", "the burn. It climbs every time something triggers. Let it run high and the descent gets harder to resist."),
        }),
        new("A descent", "howto_4", new[]
        {
            new HowToLine("", "", "", "",
                "Four waves, then it ends. Between waves she offers you a **mantra** — pick one and it bends the rules for that run only. Finish the whole descent for the full reward; slip out early and you forfeit it."),
        }),
        new("What you keep", "howto_5", new[]
        {
            new HowToLine("", "", "", "",
                "Every descent earns **XP** toward your normal level, plus **Sparks** (gold) you carry back out."),
            new HowToLine("", "", "", "",
                "Spend Sparks in **the dollhouse** — accessories at the table by the door, charms, active toys you trigger mid-descent, and the seamstress's bench for permanent upgrades."),
            new HowToLine("", "", "", "",
                "The more descents you finish, the higher your **RANK** — curious, tempted, slipping, entranced, devoted… — and the more of the Rabbit Hole opens up to you."),
        }),
    };

    private int _howToIdx;

    private void Menu_HowTo_Click(object? sender, RoutedEventArgs e)
    {
        _howToIdx = 0;
        HowToShow();
        MenuHowTo.IsVisible = true;
    }

    private void HowTo_Close_Click(object? sender, RoutedEventArgs e) => MenuHowTo.IsVisible = false;

    private void HowTo_Backdrop_Click(object? sender, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(e.Source, MenuHowTo)) MenuHowTo.IsVisible = false;
    }

    private void HowTo_Back_Click(object? sender, RoutedEventArgs e)
    {
        if (_howToIdx > 0) { _howToIdx--; HowToShow(); }
    }

    private void HowTo_Next_Click(object? sender, RoutedEventArgs e)
    {
        if (_howToIdx < _howToCards.Length - 1) { _howToIdx++; HowToShow(); }
        else MenuHowTo.IsVisible = false;
    }

    private void HowToShow()
    {
        var card = _howToCards[_howToIdx];

        HowToStep.Text = $"STEP {_howToIdx + 1} / {_howToCards.Length}";
        HowToTitle.Text = card.Title;

        var img = ChaosArt.Resolve("howto", card.Image);
        if (HowToImage != null) HowToImage.Source = img;
        HowToImageBox.IsVisible = img != null;

        HowToBody.Children.Clear();
        foreach (var line in card.Lines)
            HowToBody.Children.Add(BuildHowToLine(line));

        HowToDots.Children.Clear();
        for (int i = 0; i < _howToCards.Length; i++)
        {
            HowToDots.Children.Add(new Ellipse
            {
                Width = 8, Height = 8, Margin = new Thickness(4, 0, 4, 0),
                Fill = i == _howToIdx
                    ? AppBrush("PinkBrush", Brushes.HotPink)
                    : new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            });
        }

        HowToBack.IsVisible = _howToIdx > 0;
        HowToNext.Content = _howToIdx < _howToCards.Length - 1 ? "NEXT  ›" : "DONE";
    }

    private Control BuildHowToLine(HowToLine line)
    {
        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13.5, LineHeight = 21, Margin = new Thickness(0, 0, 0, 9) };

        if (!string.IsNullOrEmpty(line.Lead))
        {
            tb.Inlines.Add(new Run(line.Lead + "  ")
            {
                FontWeight = FontWeight.Bold,
                Foreground = BrushFromHex(line.LeadColor),
            });
        }
        bool bold = false;
        foreach (var part in line.Body.Split("**"))
        {
            if (part.Length > 0)
                tb.Inlines.Add(new Run(part)
                {
                    Foreground = bold ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xDE)),
                    FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
                });
            bold = !bold;
        }

        if (string.IsNullOrEmpty(line.Emoji)) return tb;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var glyph = new TextBlock { Text = line.Emoji, FontSize = 17, VerticalAlignment = VerticalAlignment.Top, Foreground = BrushFromHex(line.EmojiColor) };
        Grid.SetColumn(glyph, 0);
        Grid.SetColumn(tb, 1);
        grid.Children.Add(glyph);
        grid.Children.Add(tb);
        return grid;
    }

    private static IBrush BrushFromHex(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return Brushes.White;
        try
        {
            var c = Color.Parse(hex);
            return new SolidColorBrush(c);
        }
        catch { return Brushes.White; }
    }

    // ============================ menu state sync ============================

    partial void OnRefreshTopBarPartial()
    {
        try
        {
            if (MenuRank != null) MenuRank.Text = ChaosMeta.Rank;
            if (MenuSparks != null) MenuSparks.Text = ChaosMeta.State.Sparks.ToString();
            if (MenuGold != null) MenuGold.Text = ChaosMeta.State.Gold.ToString();
        }
        catch { }
    }

    private void SyncOptionsToControls()
    {
        ChkPinTop.IsChecked = OptKeepTop.IsChecked;
        ChkSkiaFx.IsChecked = OptSkiaFx.IsChecked;
        ChkAnnouncer.IsChecked = OptAnnounce.IsChecked;
        SldEffect.Value = OptSldEffect.Value;
    }
}
