using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Models;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Settings;
using IInteractionQueueService = ConditioningControlPanel.IInteractionQueueService;
using Animation = global::Avalonia.Animation.Animation;
using KeyFrame = global::Avalonia.Animation.KeyFrame;
using FillMode = global::Avalonia.Animation.FillMode;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Avalonia port of the pop-quiz reinforcement window.
/// </summary>
public partial class PopQuizWindow : Window
{
    private readonly global::ConditioningControlPanel.IAppLogger _logger;


    public static bool IsOpen { get; private set; }

    private readonly PopQuizQuestion _question;
    private readonly bool _isTest;
    private readonly bool _wasAvatarMuted;
    private bool _answered;
    private static readonly Random _random = new();
    private readonly DispatcherTimer _keepOnTopTimer;
    private readonly IProgressionService _progression;
    private readonly IInteractionQueueService _interactionQueue;
    private readonly IAvatarWindowService? _avatarWindowService;

    public PopQuizWindow(PopQuizQuestion question, bool isTest = false)
    {
        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();

IsOpen = true;
        _progression = App.Services.GetRequiredService<IProgressionService>();
        _interactionQueue = App.Services.GetRequiredService<IInteractionQueueService>();
        _avatarWindowService = App.Services.GetService<IAvatarWindowService>();

        // Mute avatar while the quiz is open so it does not steal focus.
        _wasAvatarMuted = _avatarWindowService?.IsMuted ?? true;
        if (!_wasAvatarMuted && _avatarWindowService != null)
        {
            try { _avatarWindowService.SetMuteAvatar(true); }
            catch { }
        }

        InitializeComponent();

        _question = question;
        _isTest = isTest;

        Title = Loc.Get("label_pop_quiz");
        TxtEscHint.Text = Loc.Get("label_esc_to_skip");
        TxtXpAwarded.Text = Loc.Get("label_25_xp");

        // Shuffle answer order.
        var indices = new[] { 0, 1, 2, 3 };
        for (int i = 3; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (indices[i], indices[j]) = (indices[j], indices[i]);
        }

        TxtQuestion.Text = question.QuestionText;
        TxtAnswerA.Text = question.Answers[indices[0]];
        TxtAnswerB.Text = question.Answers[indices[1]];
        TxtAnswerC.Text = question.Answers[indices[2]];
        TxtAnswerD.Text = question.Answers[indices[3]];

        AnswerA.Tag = indices[0];
        AnswerB.Tag = indices[1];
        AnswerC.Tag = indices[2];
        AnswerD.Tag = indices[3];

        _keepOnTopTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _keepOnTopTimer.Tick += KeepOnTopTimer_Tick;

        Loaded += (s, e) =>
        {
            Topmost = true;
            Activate();
            _keepOnTopTimer.Start();
        };

        Deactivated += (s, e) =>
        {
            if (_answered) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (_answered || !IsVisible) return;
                Topmost = true;
                try { Activate(); } catch { }
            });
        };
    }

    /// <summary>
    /// Required parameterless constructor for Avalonia designer/build.
    /// </summary>
    public PopQuizWindow()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
_progression = App.Services.GetRequiredService<IProgressionService>();
        _interactionQueue = App.Services.GetRequiredService<IInteractionQueueService>();
        _avatarWindowService = App.Services.GetService<IAvatarWindowService>();
        _question = new PopQuizQuestion("", Array.Empty<string>(), Array.Empty<string>());
        _keepOnTopTimer = new DispatcherTimer();
    }

    private void KeepOnTopTimer_Tick(object? sender, EventArgs e)
    {
        if (_answered || !IsVisible)
        {
            _keepOnTopTimer.Stop();
            return;
        }

        var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (mainWindow == null || !mainWindow.IsVisible)
        {
            CleanupAndClose();
            return;
        }

        Topmost = true;
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !_answered)
        {
            CleanupAndClose();
        }
    }

    private async void Answer_Click(object? sender, PointerPressedEventArgs e)
    {
        if (_answered) return;
        _answered = true;

        if (sender is not Border border || border.Tag == null) return;

        var answerIndex = (int)border.Tag;

        border.Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0x69, 0xB4));
        border.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0x69, 0xB4));

        PlayChime();

        if (!_isTest)
        {
            try
            {
                _progression.AddXP(25, XPSource.Other);
            }
            catch (Exception ex)
            {
                _logger?.Warning(ex, "PopQuiz XP award failed");
            }
        }

        await Task.Delay(300);
        TxtAffirmation.Text = _question.Affirmations[answerIndex];
        QuestionPanel.IsVisible = false;
        AffirmationPanel.IsVisible = true;

        await Task.Delay(1500);
        CleanupAndClose();
    }

    private void CleanupAndClose()
    {
        _keepOnTopTimer.Stop();

        try
        {
            _interactionQueue.Complete("PopQuiz");
        }
        catch { }

        Close();
    }

    private static void PlayChime()
    {
        var logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
        try
        {
            var env = App.Services?.GetService<IAppEnvironment>();
            var player = App.Services?.GetService<IAudioPlayer>();
            if (player == null) return;

            var soundsPath = env != null
                ? Path.Combine(env.BaseDirectory, "Resources", "sounds")
                : Path.Combine(AppContext.BaseDirectory, "Resources", "sounds");

            var files = new[] { "chime1.mp3", "chime2.mp3", "chime3.mp3" };
            var file = files[_random.Next(files.Length)];
            var path = Path.Combine(soundsPath, file);
            if (!File.Exists(path)) return;

            var settings = App.Services?.GetService<ISettingsService>()?.Current;
            var master = (settings?.MasterVolume ?? 100) / 100.0;
            var volume = Math.Pow(master * 0.5, 1.5);

            player.SetVolume(Math.Clamp(volume, 0.01, 1.0));
            _ = player.PlayAsync(path);
        }
        catch (Exception ex)
        {
            App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>()?.Warning(ex, "PopQuiz chime failed");
        }
    }

    private void Answer_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (_answered || sender is not Border border) return;

        border.Background = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF));
        border.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0x69, 0xB4));
    }

    private void Answer_PointerExited(object? sender, PointerEventArgs e)
    {
        if (_answered || sender is not Border border) return;

        border.Background = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
        border.BorderBrush = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
    }

    /// <summary>
    /// Force close all pop quiz windows (used by panic button).
    /// </summary>
    public static void ForceCloseAll()
    {
        try
        {
            var windows = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?
                .Windows.OfType<PopQuizWindow>().ToList();

            foreach (var window in windows ?? new System.Collections.Generic.List<PopQuizWindow>())
            {
                try { window.Close(); } catch { }
            }
        }
        catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        IsOpen = false;
        _keepOnTopTimer.Stop();

        if (!_wasAvatarMuted && _avatarWindowService != null)
        {
            try { _avatarWindowService.SetMuteAvatar(false); }
            catch { }
        }

        if (!_answered)
        {
            try { _interactionQueue.Complete("PopQuiz"); }
            catch { }
        }

        base.OnClosed(e);
    }
}
