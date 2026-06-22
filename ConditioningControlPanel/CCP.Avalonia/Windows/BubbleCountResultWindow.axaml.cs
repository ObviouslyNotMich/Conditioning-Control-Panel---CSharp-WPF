using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Settings;
using BubbleCountService = ConditioningControlPanel.Avalonia.Services.BubbleCountService;

using IModService = ConditioningControlPanel.IModService;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Avalonia port of the bubble-count result window.
/// </summary>
public partial class BubbleCountResultWindow : Window
{
    private readonly global::ConditioningControlPanel.IAppLogger _logger;


    private readonly int _correctAnswer;
    private readonly bool _strictMode;
    private readonly Action<bool> _onComplete = _ => { };
    private readonly ScreenInfo? _screen;
    private readonly bool _isPrimary;

    private int _attemptsRemaining = 3;
    private bool _isCompleted;

    private static readonly List<BubbleCountResultWindow> _allWindows = new();
    private static string _sharedInput = "";
    private readonly IProgressionService _progression;
    private readonly IModService _mods;

    public BubbleCountResultWindow()
    {
        InitializeComponent();

        ApplyThemeShadow();

        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
_progression = App.Services.GetRequiredService<IProgressionService>();
        _mods = App.Services.GetRequiredService<IModService>();
    }

    public BubbleCountResultWindow(int correctAnswer, bool strictMode, Action<bool> onComplete,
        ScreenInfo? screen = null, bool isPrimary = true) : this()
    {
        _correctAnswer = correctAnswer;
        _strictMode = strictMode;
        _onComplete = onComplete;
        _screen = screen;
        _isPrimary = isPrimary;

        UpdateAttemptsDisplay();

        if (_strictMode)
        {
            TxtStrict.IsVisible = true;
            TxtEscHint.IsVisible = false;
        }

        if (!_isPrimary)
        {
            TxtAnswer.IsReadOnly = true;
            TxtAnswer.Focusable = false;
            BtnSubmit.IsEnabled = false;
        }

        _allWindows.Add(this);

        Loaded += (_, _) =>
        {
            PositionWindow();
            if (_isPrimary) TxtAnswer.Focus();
        };

        KeyDown += Window_KeyDown;
        TxtAnswer.KeyDown += OnInputKeyDown;
        TxtAnswer.TextChanged += OnTextChanged;
    }

    /// <summary>
    /// Show result window on all monitors.
    /// </summary>
    public static void ShowOnAllMonitors(int correctAnswer, bool strictMode, Action<bool> onComplete)
    {
        var logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
        _allWindows.Clear();
        _sharedInput = "";

        var provider = App.Services?.GetService<IScreenProvider>();
        var screens = provider?.GetAllScreens();

        if (screens == null || screens.Count == 0)
        {
            App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>()?.Warning("BubbleCountResultWindow: no screens available");
            onComplete?.Invoke(false);
            return;
        }

        var settings = App.Services?.GetService<ISettingsService>()?.Current;
        var allScreens = screens.ToArray();
        var primary = allScreens[0];

        foreach (var screen in allScreens.Where(s => s != primary))
        {
            var window = new BubbleCountResultWindow(correctAnswer, strictMode, onComplete, screen, false);
            window.Show();
        }

        var primaryWindow = new BubbleCountResultWindow(correctAnswer, strictMode, onComplete, primary, true);
        primaryWindow.Show();
        primaryWindow.Activate();
    }

    private void ApplyThemeShadow()
    {
        if (ContentCard == null) return;
        var accent = (Application.Current?.TryFindResource("PinkColor", out var res) == true && res is Color c)
            ? c
            : Color.Parse("#FF69B4");
        ContentCard.BoxShadow = new BoxShadows(new BoxShadow
        {
            OffsetX = 0, OffsetY = 0, Blur = 30, Spread = 0,
            Color = Color.FromArgb(0x80, accent.R, accent.G, accent.B)
        });
    }

    private void PositionWindow()
    {
        try
        {
            var screen = _screen ?? App.Services?.GetService<IScreenProvider>()?.GetPrimaryScreen();
            if (screen == null) return;

            Position = new PixelPoint((int)screen.Bounds.X, (int)screen.Bounds.Y);
            Width = screen.Bounds.Width / screen.Scaling;
            Height = screen.Bounds.Height / screen.Scaling;
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "BubbleCountResultWindow: failed to position window");
            WindowState = WindowState.Maximized;
        }
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_isPrimary) return;

        // Keep only digits.
        var text = new string((TxtAnswer.Text ?? string.Empty).Where(char.IsDigit).ToArray());
        if (text != TxtAnswer.Text)
        {
            var caret = TxtAnswer.CaretIndex;
            TxtAnswer.Text = text;
            TxtAnswer.CaretIndex = Math.Min(caret, text.Length);
        }

        _sharedInput = TxtAnswer.Text;

        foreach (var window in _allWindows.Where(w => w != this))
        {
            window.TxtAnswer.Text = _sharedInput;
        }
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _isPrimary)
        {
            CheckAnswer();
            e.Handled = true;
        }
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !_strictMode && !_isCompleted)
        {
            CompleteAll(false);
        }
    }

    private void BtnSubmit_Click(object? sender, RoutedEventArgs e)
    {
        if (_isPrimary) CheckAnswer();
    }

    private void CheckAnswer()
    {
        if (_isCompleted) return;

        if (!int.TryParse((TxtAnswer.Text ?? string.Empty).Trim(), out int answer))
        {
            ShowFeedbackOnAll("Please enter a number!", Color.FromRgb(255, 165, 0));
            TxtAnswer.Clear();
            TxtAnswer.Focus();
            return;
        }

        if (answer == _correctAnswer)
        {
            var xp = BubbleCountService.ScaleXpByDuration(250);
            _progression.AddXP(xp, XPSource.BubbleCount);
            ShowFeedbackOnAll($"🎉 CORRECT! +{xp} XP 🎉", Color.FromRgb(50, 205, 50));
            DisableInputOnAll();

            try
            {
                App.Services?.GetService<IAchievementService>()?.TrackBubbleCountResult(true);
            }
            catch { /* achievement service may not be present */ }

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                CompleteAll(true);
            };
            timer.Start();
        }
        else
        {
            _attemptsRemaining--;
            UpdateAttemptsOnAll();

            try
            {
                App.Services?.GetService<IAchievementService>()?.TrackBubbleCountResult(false);
            }
            catch { /* achievement service may not be present */ }

            if (_attemptsRemaining <= 0)
            {
                if (_strictMode)
                {
                    CompleteAll(false);
                }
                else
                {
                    ShowMercyCard();
                }
            }
            else
            {
                var hint = answer < _correctAnswer ? "Too low! Try higher." : "Too high! Try lower.";
                ShowFeedbackOnAll($"❌ {hint}", Color.FromRgb(255, 107, 107));
                TxtAnswer.Clear();
                TxtAnswer.Focus();
            }
        }
    }

    private void ShowFeedbackOnAll(string message, Color color)
    {
        foreach (var window in _allWindows)
        {
            window.TxtFeedback.Text = message;
            window.TxtFeedback.Foreground = new SolidColorBrush(color);
            window.TxtFeedback.IsVisible = true;
        }
    }

    private void UpdateAttemptsOnAll()
    {
        foreach (var window in _allWindows)
        {
            window._attemptsRemaining = _attemptsRemaining;
            window.UpdateAttemptsDisplay();
        }
    }

    private void DisableInputOnAll()
    {
        foreach (var window in _allWindows)
        {
            window.BtnSubmit.IsEnabled = false;
            window.TxtAnswer.IsEnabled = false;
        }
    }

    private void UpdateAttemptsDisplay()
    {
        TxtAttempts.Text = $"Attempts remaining: {_attemptsRemaining}";

        if (_attemptsRemaining == 1)
        {
            TxtAttempts.Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 107));
        }
        else if (_attemptsRemaining == 2)
        {
            TxtAttempts.Foreground = new SolidColorBrush(Color.FromRgb(255, 165, 0));
        }
        else
        {
            TxtAttempts.Foreground = new SolidColorBrush(Color.Parse("#888888"));
        }
    }

    private void ShowMercyCard()
    {
        _isCompleted = true;

        foreach (var window in _allWindows)
        {
            window._isCompleted = true;
            window.Hide();
        }

        string[] fallback = { "GOOD GIRLS PAY ATTENTION" };
        string[] phrases;
        try
        {
            var pool = _mods.GetPhrases("BubbleCountMercy");
            phrases = pool.Length > 0 ? pool : fallback;
        }
        catch
        {
            phrases = fallback;
        }

        var phrase = phrases[Random.Shared.Next(phrases.Length)];

        LockCardWindow.ShowOnAllMonitors(phrase, 2, _strictMode);

        var timer =
new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            if (LockCardWindow.IsAnyOpen())
            {
                timer.Start();
            }
            else
            {
                timer.Stop();
                CompleteAll(false);
            }
        };
        timer.Start();
    }

    /// <summary>
    /// Force close all result windows.
    /// </summary>
    public static void ForceCloseAll()
    {
        foreach (var window in _allWindows.ToArray())
        {
            window._isCompleted = true;
            try { window.Close(); } catch { }
        }
        _allWindows.Clear();
    }

    private void CompleteAll(bool success)
    {
        foreach (var window in _allWindows.ToArray())
        {
            window._isCompleted = true;
            try { window.Close(); } catch { }
        }
        _allWindows.Clear();

        _onComplete?.Invoke(success);
    }

    protected override void OnClosed(EventArgs e)
    {
        _allWindows.Remove(this);

        if (!_isCompleted && _isPrimary)
        {
            _onComplete?.Invoke(false);
        }
        base.OnClosed(e);
    }
}
