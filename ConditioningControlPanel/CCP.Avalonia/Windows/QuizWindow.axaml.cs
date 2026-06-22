using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Documents;
using AvaloniaLayout = global::Avalonia.Layout;
using IOPath = System.IO.Path;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Settings;

using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Avalonia port of the AI-generated personality quiz window.
/// </summary>
public partial class QuizWindow : Window
{
    private readonly global::ConditioningControlPanel.IAppLogger? _logger;

    public static bool IsOpen { get; private set; }

    private QuizService? _quizService;
    private QuizQuestion? _currentQuestion;
    private bool _isProcessing;
    private bool _isFullscreen;
    private bool _isTrickQuestion;
    private readonly DispatcherTimer _loadingDotsTimer;
    private int _loadingDotCount;
    private readonly Ellipse[] _progressDots = new Ellipse[10];
    private List<QuizAnswerRecord> _answerHistory = new();
    private Session? _generatedSession;
    private bool _sessionReady;

    private static readonly Random _random = new();

    private static string[] LoadingFlavors => new[]
    {
        Loc.Get("quiz_loading_1"),
        Loc.Get("quiz_loading_2"),
        Loc.Get("quiz_loading_3"),
        Loc.Get("quiz_loading_4"),
        Loc.Get("quiz_loading_5"),
        Loc.Get("quiz_loading_6"),
        Loc.Get("quiz_loading_7"),
        Loc.Get("quiz_loading_8"),
        Loc.Get("quiz_loading_9"),
        Loc.Get("quiz_loading_10")
    };

    private static readonly string[] GiggleFiles = new[]
    {
        "giggle1.MP3", "giggle2.MP3", "giggle3.MP3", "giggle4.MP3",
        "giggle5.mp3", "giggle6.wav", "giggle7.mp3", "giggle8.mp3"
    };
    private static readonly string[] ChimeFiles = new[] { "chime1.mp3", "chime2.mp3", "chime3.mp3" };

    private static readonly (string Question, string Answer)[] TrickQuestions = new[]
    {
        ("Do you like to let go and obey?", "Yes"),
        ("Are you a good girl?", "Obviously"),
        ("Do you want to go deeper?", "Yes please"),
        ("Is it easier when you don't think?", "Mmhmm"),
        ("Do you enjoy being told what to do?", "Absolutely"),
        ("Would you like to surrender control?", "Yes"),
    };

    private readonly bool _playDrone;

    public QuizWindow()
    {
        InitializeComponent();

        ApplyTitleShadow();

        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
_loadingDotsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _loadingDotsTimer.Tick += LoadingDotsTimer_Tick;
    }

    public QuizWindow(bool fullscreen = true, bool playDrone = false) : this()
    {
        IsOpen = true;
        _isFullscreen = fullscreen;
        _playDrone = playDrone;

        if (fullscreen)
        {
            WindowState = WindowState.Maximized;
            TitleBar.IsVisible = false;
        }
        else
        {
            WindowState = WindowState.Normal;
            Topmost = false;
        }
    }

    private void Window_Loaded(object? sender, RoutedEventArgs e)
    {
        BuildProgressDots();
        BuildCategoryButtons();
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private void BuildProgressDots()
    {
        ProgressDotsPanel.Children.Clear();
        for (int i = 0; i < 10; i++)
        {
            var dot = new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                Margin = new Thickness(3, 0, 3, 0)
            };
            _progressDots[i] = dot;
            ProgressDotsPanel.Children.Add(dot);
        }
    }

    private void BuildCategoryButtons()
    {
        CategoryButtonsPanel.Children.Clear();
        var categories = QuizService.GetAllCategories();
        foreach (var cat in categories)
        {
            var color = (Color)global::Avalonia.Application.Current!.Resources["TextLight"]!;
            try { color = Color.Parse(cat.Color); } catch { }

            var border = new Border
            {
                Cursor = new Cursor(StandardCursorType.Hand),
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(20, 16),
                Background = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, color.R, color.G, color.B)),
                BorderThickness = new Thickness(1.5),
                Tag = cat
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = cat.Name,
                Foreground = new SolidColorBrush(color),
                FontWeight = FontWeight.Bold,
                FontSize = 26
            });
            stack.Children.Add(new TextBlock
            {
                Text = cat.Description,
                Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x90)),
                FontSize = 17,
                Margin = new Thickness(0, 4, 0, 0)
            });
            Grid.SetColumn(stack, 0);
            grid.Children.Add(stack);

            // Edit button for custom categories
            if (!cat.IsBuiltIn)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var editBtn = new TextBlock
                {
                    Text = "Edit",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x80)),
                    FontSize = 13,
                    VerticalAlignment = AvaloniaLayout.VerticalAlignment.Center,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Margin = new Thickness(10, 0, 0, 0),
                    Tag = cat
                };
                editBtn.PointerPressed += EditCategoryButton_Click;
                editBtn.PointerEntered += (s, _) => { if (s is TextBlock t) t.Foreground = (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!; };
                editBtn.PointerExited += (s, _) => { if (s is TextBlock t) t.Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x80)); };
                Grid.SetColumn(editBtn, 1);
                grid.Children.Add(editBtn);
            }

            border.Child = grid;
            border.PointerPressed += DynamicCategoryButton_Click;
            border.PointerEntered += CategoryButton_PointerEntered;
            border.PointerExited += CategoryButton_PointerExited;

            CategoryButtonsPanel.Children.Add(border);
        }

        // "+ Create Custom" button
        var createBorder = new Border
        {
            Cursor = new Cursor(StandardCursorType.Hand),
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(0, 4, 0, 12),
            Padding = new Thickness(20, 14),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1.5)
        };

        var createStack = new StackPanel { HorizontalAlignment = AvaloniaLayout.HorizontalAlignment.Center };
        createStack.Children.Add(new TextBlock
        {
            Text = "+ Create Custom Category",
            Foreground = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x88)),
            FontWeight = FontWeight.SemiBold,
            FontSize = 20,
            HorizontalAlignment = AvaloniaLayout.HorizontalAlignment.Center
        });
        createBorder.Child = createStack;
        createBorder.PointerPressed += CreateCategoryButton_Click;
        createBorder.PointerEntered += (s, _) =>
        {
            if (s is Border b) b.Background = new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF));
        };
        createBorder.PointerExited += (s, _) =>
        {
            if (s is Border b) b.Background = new SolidColorBrush(Colors.Transparent);
        };

        CategoryButtonsPanel.Children.Add(createBorder);
    }

    private void CreateCategoryButton_Click(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        var editor = new QuizCategoryEditorWindow();
        _ = editor.ShowDialog<bool?>(this);
        if (editor.DialogResult == true && editor.Result != null)
        {
            QuizService.SaveCustomCategory(editor.Result);
            BuildCategoryButtons();
        }
    }

    private void EditCategoryButton_Click(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        if (sender is not TextBlock el || el.Tag is not QuizCategoryDefinition catDef) return;

        var editor = new QuizCategoryEditorWindow(catDef);
        _ = editor.ShowDialog<bool?>(this);
        if (editor.DialogResult == true)
        {
            if (editor.Result != null)
                QuizService.SaveCustomCategory(editor.Result);
            BuildCategoryButtons();
        }
    }

    private void UpdateProgressDots(int currentQuestion)
    {
        var accent = GetAccentColor();
        for (int i = 0; i < 10; i++)
        {
            if (i < currentQuestion - 1)
            {
                _progressDots[i].Fill = new SolidColorBrush(accent);
            }
            else if (i == currentQuestion - 1)
            {
                _progressDots[i].Fill = (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!;
            }
            else
            {
                _progressDots[i].Fill = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
            }
        }
    }

    private void UpdateScore(int score)
    {
        ScoreText.Text = Loc.GetF("quiz_score", score);
    }

    private void ShowPanel(Control panel)
    {
        CategorySelectPanel.IsVisible = false;
        LoadingPanel.IsVisible = false;
        QuestionPanel.IsVisible = false;
        ResultPanel.IsVisible = false;
        ErrorPanel.IsVisible = false;

        panel.IsVisible = true;
    }

    private void ShowLoading(string? flavorText = null)
    {
        _loadingDotCount = 0;
        TxtLoadingDots.Text = Loc.Get("label_generating_3");
        TxtLoadingFlavor.Text = flavorText ?? LoadingFlavors[_random.Next(LoadingFlavors.Length)];
        _loadingDotsTimer.Start();
        ShowPanel(LoadingPanel);
        PlayRandomGiggle();
    }

    private static void ShuffleAnswers(QuizQuestion question)
    {
        var n = question.Answers.Length;
        for (int i = n - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (question.Answers[i], question.Answers[j]) = (question.Answers[j], question.Answers[i]);
            (question.Points[i], question.Points[j]) = (question.Points[j], question.Points[i]);
        }
    }

    private void ShowQuestion(QuizQuestion question)
    {
        _currentQuestion = question;
        _loadingDotsTimer.Stop();

        UpdateProgressDots(question.Number);
        UpdateScore(_quizService?.QuestionNumber ?? 0);

        ShuffleAnswers(question);
        TxtQuestion.Text = question.QuestionText;
        TxtAnswerA.Text = question.Answers[0];
        TxtAnswerB.Text = question.Answers[1];
        TxtAnswerC.Text = question.Answers[2];
        TxtAnswerD.Text = question.Answers[3];

        SetAnswersEnabled(true);
        ShowPanel(QuestionPanel);
    }

    private void ShowResult(QuizResult result)
    {
        _loadingDotsTimer.Stop();

        var catDef = _quizService?.CurrentCategoryDefinition;

        QuizHistoryEntry? savedEntry = null;
        try
        {
            savedEntry = new QuizHistoryEntry
            {
                TakenAt = DateTime.Now,
                Category = result.Category,
                CategoryId = catDef?.Id ?? result.Category.ToString(),
                CategoryName = catDef?.Name ?? result.Category.ToString(),
                TotalScore = result.TotalScore,
                MaxScore = result.MaxScore,
                ProfileText = result.ProfileText,
                Answers = new List<QuizAnswerRecord>(_answerHistory)
            };
            QuizService.SaveEntry(savedEntry);
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "QuizWindow: Failed to save quiz history");
        }

        try
        {
            var settings = App.Services?.GetService<ISettingsService>()?.Current;
            if (settings != null)
            {
                dynamic? companion = settings;
                companion.LatestQuizCategoryId = catDef?.Id ?? result.Category.ToString();
                companion.LatestQuizScorePercentage = result.MaxScore > 0
                    ? (int)Math.Round((double)result.TotalScore / result.MaxScore * 100) : 0;
                companion.LatestQuizProfileText = result.ProfileText;

                var archetypeMatch = System.Text.RegularExpressions.Regex.Match(
                    result.ProfileText, @"You are a (.+?)\.");
                companion.LatestQuizArchetype = archetypeMatch.Success
                    ? archetypeMatch.Groups[1].Value : "";
            }
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "QuizWindow: Failed to save quiz result to settings");
        }

        TxtFinalScore.Text = Loc.GetF("quiz_final_score", result.TotalScore, result.MaxScore);

        var percentage = result.MaxScore > 0 ? (double)result.TotalScore / result.MaxScore * 100 : 0;
        TxtScoreLabel.Text = percentage switch
        {
            >= 90 => Loc.Get("quiz_result_90"),
            >= 75 => Loc.Get("quiz_result_75"),
            >= 60 => Loc.Get("quiz_result_60"),
            >= 40 => Loc.Get("quiz_result_40"),
            >= 20 => Loc.Get("quiz_result_20"),
            _ => Loc.Get("quiz_result_0")
        };

        TxtProfileText.Text = result.ProfileText;

        try
        {
            var perfect = result.MaxScore > 0 && result.TotalScore == result.MaxScore;
            var passed = percentage >= 60;
            var categoryId = catDef?.Id ?? result.Category.ToString();
            QuizService.RaiseQuizCompleted(result.TotalScore, passed, perfect, categoryId);
        }
        catch (Exception ex)
        {
            _logger?.Information("QuizWindow: RaiseQuizCompleted failed: {Error}", ex.Message);
        }

        if (savedEntry != null)
        {
            BuildTrendDisplay(savedEntry.Category);
            _ = GenerateSessionInBackgroundAsync(result, savedEntry);
        }

        ShowPanel(ResultPanel);
        PlayResultSound();
    }

    private async Task GenerateSessionInBackgroundAsync(QuizResult result, QuizHistoryEntry entry)
    {
        try
        {
            var catDef = _quizService?.CurrentCategoryDefinition;
            var categoryId = catDef?.Id ?? result.Category.ToString();
            var categoryName = catDef?.Name ?? result.Category.ToString();
            var scorePercent = result.MaxScore > 0 ? (double)result.TotalScore / result.MaxScore * 100 : 0;

            SessionTextContent? textContent = null;
            try
            {
                textContent = _quizService != null ? await _quizService.GenerateSessionContentAsync() : null;
            }
            catch (Exception ex)
            {
                _logger?.Warning(ex, "QuizWindow: AI session content generation failed, using fallback");
            }

            textContent ??= QuizSessionGenerator.GetFallbackContent(categoryId, scorePercent);

            var session = QuizSessionGenerator.GenerateSession(
                result.TotalScore, result.MaxScore, categoryId, categoryName, textContent);

            _generatedSession = session;
            _sessionReady = true;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var displayName = session.Name.Length > 30 ? session.Name[..30] + "..." : session.Name;
                TxtTrySessionIcon.Text = "\u2728";
                TxtTrySessionLabel.Text = Loc.GetF("quiz_save_session", displayName);
                BtnTrySession.IsHitTestVisible = true;
                BtnTrySession.Opacity = 1.0;
            });
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "QuizWindow: Failed to generate session in background");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                BtnTrySession.IsVisible = false;
            });
        }
    }

    private async void BtnTrySession_Click(object? sender, PointerPressedEventArgs e)
    {
        if (_generatedSession == null || !_sessionReady) return;

        var dialogService = App.Services?.GetService<IDialogService>();
        var env = App.Services?.GetService<IAppEnvironment>();
        if (dialogService == null || env == null) return;

        var fileName = SessionFileService.GetExportFileName(_generatedSession);
        var path = await dialogService.ShowSaveFileDialogAsync(
            "Save Quiz Session",
            new[]
            {
                new FileFilter("Session files", new[] { "session.json" })
            },
            fileName);

        if (string.IsNullOrEmpty(path)) return;

        try
        {
            var fileService = new SessionFileService(env);
            fileService.ExportSession(_generatedSession, path);
            TxtTrySessionLabel.Text = Loc.Get("label_session_saved");
            BtnTrySession.IsHitTestVisible = false;
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "QuizWindow: Failed to export session");
        }
    }

    private void BtnTrySession_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Border b)
        {
            var accent = GetAccentColor();
            b.Background = new SolidColorBrush(Color.FromArgb(0x40, accent.R, accent.G, accent.B));
            b.BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, accent.R, accent.G, accent.B));
        }
    }

    private void BtnTrySession_PointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Border b)
        {
            var accent = GetAccentColor();
            b.Background = new SolidColorBrush(Color.FromArgb(0x20, accent.R, accent.G, accent.B));
            b.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, accent.R, accent.G, accent.B));
        }
    }

    private void BuildTrendDisplay(QuizCategory category)
    {
        try
        {
            TrendPanel.Children.Clear();
            var history = QuizService.LoadHistory();
            var trend = QuizService.GetScoreTrend(history, category);
            if (trend == null) return;

            TxtTrendHeader.IsVisible = true;

            var arrow = trend.Direction switch
            {
                TrendDirection.Up => "\u2191",
                TrendDirection.Down => "\u2193",
                TrendDirection.Flat => "\u2192",
                _ => ""
            };
            var arrowColor = trend.Direction switch
            {
                TrendDirection.Up => Color.FromRgb(0x2E, 0xCC, 0x71),
                TrendDirection.Down => Color.FromRgb(0xE7, 0x4C, 0x3C),
                _ => Color.FromRgb(0x80, 0x80, 0x90)
            };

            var trendBlock = new TextBlock
            {
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xC8)),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = AvaloniaLayout.HorizontalAlignment.Center
            };

            if (trend.Direction != TrendDirection.FirstQuiz)
            {
                trendBlock.Inlines?.Add(new Run($"Score: {trend.LatestPercent}% (") { Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xC8)) });
                trendBlock.Inlines?.Add(new Run($"{arrow}{Math.Abs(trend.DeltaPercent)}%") { Foreground = new SolidColorBrush(arrowColor), FontWeight = FontWeight.SemiBold });
                trendBlock.Inlines?.Add(new Run($" from last time) \u00B7 Average: {trend.AveragePercent}% across {trend.QuizCount} quizzes") { Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xC8)) });
            }
            else
            {
                trendBlock.Text = $"Score: {trend.LatestPercent}% — Your first {category} quiz!";
            }

            TrendPanel.Children.Add(trendBlock);
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "QuizWindow: Failed to build trend display");
        }
    }

    private void ShowError(string message)
    {
        _loadingDotsTimer.Stop();
        TxtError.Text = message;
        ShowPanel(ErrorPanel);
    }

    private static string QuizGenerationFailedMessage()
    {
        try
        {
            var settings = App.Services?.GetService<ISettingsService>()?.Current;
            dynamic? companion = settings?.CompanionPrompt;
            bool useLocal = companion?.UseLocalAi == true;
            return useLocal
                ? "Couldn't generate the quiz. Make sure Ollama is running and your model is pulled (Companion → AI), then try again."
                : "Couldn't generate the quiz. The AI might be busy or you've hit your daily limit. Try again in a moment.";
        }
        catch
        {
            return "Couldn't generate the quiz. The AI might be busy. Try again in a moment.";
        }
    }

    private async void DynamicCategoryButton_Click(object? sender, PointerPressedEventArgs e)
    {
        if (_isProcessing) return;

        if (sender is not Border border || border.Tag is not QuizCategoryDefinition catDef) return;

        _isProcessing = true;
        _answerHistory.Clear();
        ShowLoading("Preparing your quiz...");

        _quizService?.Dispose();
        _quizService = new QuizService();

        try
        {
            var question = await _quizService.StartQuizAsync(catDef);
            if (question != null)
            {
                ShowQuestion(question);
            }
            else
            {
                ShowError(QuizGenerationFailedMessage());
            }
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "QuizWindow: Failed to start quiz");
            ShowError("Something went wrong starting the quiz. Please try again.");
        }
        finally
        {
            _isProcessing = false;
        }
    }

    private async void Answer_Click(object? sender, PointerPressedEventArgs e)
    {
        if (_isProcessing || _currentQuestion == null) return;

        if (sender is not Border border || border.Tag == null) return;

        var answerIndex = int.Parse(border.Tag.ToString()!);
        var points = _currentQuestion.Points[answerIndex];

        _answerHistory.Add(new QuizAnswerRecord
        {
            QuestionNumber = _currentQuestion.Number,
            QuestionText = _currentQuestion.QuestionText,
            AllAnswers = (string[])_currentQuestion.Answers.Clone(),
            AllPoints = (int[])_currentQuestion.Points.Clone(),
            ChosenIndex = answerIndex,
            PointsEarned = points
        });

        _isProcessing = true;
        SetAnswersEnabled(false);

        if (_isTrickQuestion)
        {
            _isTrickQuestion = false;
        }

        TriggerRandomEffect();

        var questionNum = _quizService?.QuestionNumber ?? 0;

        try
        {
            if (questionNum >= 10)
            {
                ShowLoading("Analyzing your personality...");
                var result = _quizService != null
                    ? await _quizService.SubmitFinalAnswerAndGetResultAsync(answerIndex, points)
                    : null;
                if (result != null)
                {
                    ShowResult(result);
                }
                else
                {
                    ShowError("Couldn't generate your result. Please try again.");
                }
            }
            else
            {
                ShowLoading();
                var nextQuestion = _quizService != null
                    ? await _quizService.SubmitAnswerAndGetNextAsync(answerIndex, points)
                    : null;
                if (nextQuestion != null)
                {
                    if (_random.Next(20) == 0)
                    {
                        nextQuestion = CreateTrickQuestion(nextQuestion.Number);
                        _isTrickQuestion = true;
                    }
                    ShowQuestion(nextQuestion);
                }
                else
                {
                    ShowError("Couldn't generate the next question. The AI might be unavailable.");
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "QuizWindow: Failed to process answer");
            ShowError("Something went wrong. Please try again.");
        }
        finally
        {
            _isProcessing = false;
        }
    }

    private static QuizQuestion CreateTrickQuestion(int number)
    {
        var (question, answer) = TrickQuestions[_random.Next(TrickQuestions.Length)];
        return new QuizQuestion
        {
            Number = number,
            QuestionText = question,
            Answers = new[] { answer, answer, answer, answer },
            Points = new[] { 4, 4, 4, 4 }
        };
    }

    private void SetAnswersEnabled(bool enabled)
    {
        var opacity = enabled ? 1.0 : 0.5;
        AnswerA.IsHitTestVisible = enabled;
        AnswerB.IsHitTestVisible = enabled;
        AnswerC.IsHitTestVisible = enabled;
        AnswerD.IsHitTestVisible = enabled;
        AnswerA.Opacity = opacity;
        AnswerB.Opacity = opacity;
        AnswerC.Opacity = opacity;
        AnswerD.Opacity = opacity;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void BtnMaximizeTitleBar_Click(object? sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void ToggleMaximize()
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            BtnMaximizeTitleBar.Content = "☐";
        }
        else
        {
            WindowState = WindowState.Maximized;
            BtnMaximizeTitleBar.Content = "❐";
        }
    }

    private void BtnCloseTitleBar_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BtnPlayAgain_Click(object? sender, RoutedEventArgs e)
    {
        _quizService?.Reset();
        _currentQuestion = null;
        ShowPanel(CategorySelectPanel);
    }

    private void BtnCloseResult_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void CategoryButton_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF));
        }
    }

    private void CategoryButton_PointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
        }
    }

    private void Answer_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Border border && border.IsHitTestVisible)
        {
            var accent = GetAccentColor();
            border.Background = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF));
            border.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, accent.R, accent.G, accent.B));
        }
    }

    private void Answer_PointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
            border.BorderBrush = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
        }
    }

    private void LoadingDotsTimer_Tick(object? sender, EventArgs e)
    {
        _loadingDotCount = (_loadingDotCount + 1) % 4;
        TxtLoadingDots.Text = Loc.Get("label_generating_3") + new string('.', _loadingDotCount);
    }

    private static string GetSoundsPath()
    {
        var env = App.Services?.GetService<IAppEnvironment>();
        return env != null
            ? IOPath.Combine(env.BaseDirectory, "Resources", "sounds")
            : IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds");
    }

    private static double GetAudioVolume(double multiplier = 1.0)
    {
        var settings = App.Services?.GetService<ISettingsService>()?.Current;
        var master = (settings?.MasterVolume ?? 100) / 100.0;
        return Math.Pow(master * multiplier, 1.5);
    }

    private static async void PlaySoundAsync(string path, double volume)
    {
        var logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
        try
        {
            var player = App.Services?.GetService<IAudioPlayer>();
            if (player == null || !File.Exists(path)) return;
            player.SetVolume(Math.Clamp(volume, 0.01, 1.0));
            await player.PlayAsync(path);
        }
        catch (Exception ex)
        {
            App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>()?.Information("Quiz audio playback failed: {Error}", ex.Message);
        }
    }

    private static void PlayRandomGiggle()
    {
        var soundsPath = GetSoundsPath();
        var file = GiggleFiles[_random.Next(GiggleFiles.Length)];
        var path = IOPath.Combine(soundsPath, file);
        if (File.Exists(path))
            PlaySoundAsync(path, GetAudioVolume(0.5));
    }

    private static void PlayRandomChime()
    {
        var soundsPath = GetSoundsPath();
        var file = ChimeFiles[_random.Next(ChimeFiles.Length)];
        var path = IOPath.Combine(soundsPath, file);
        if (File.Exists(path))
            PlaySoundAsync(path, GetAudioVolume(0.5));
    }

    private static void PlayResultSound()
    {
        var soundsPath = GetSoundsPath();
        var path =
IOPath.Combine(soundsPath, "result.mp3");
        if (File.Exists(path))
            PlaySoundAsync(path, GetAudioVolume());
    }

    private static void TriggerRandomEffect()
    {
        // TODO: wire up Flash/Bubbles/Subliminal/MindWipe services once ported.
    }

    private static Color GetAccentColor()
    {
        if (Application.Current?.TryFindResource("PinkColor", out var res) == true && res is Color c)
            return c;
        return Color.Parse("#FF69B4");
    }

    private void ApplyTitleShadow()
    {
        if (TitleBorder == null) return;
        var accent = GetAccentColor();
        TitleBorder.BoxShadow = new BoxShadows(new BoxShadow
        {
            OffsetX = 0, OffsetY = 0, Blur = 30, Spread = 0,
            Color = Color.FromArgb(0x60, accent.R, accent.G, accent.B)
        });
    }

    public static void ForceCloseAll()
    {
        try
        {
            foreach (var window in ((Application.Current as App)?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows.OfType<QuizWindow>().ToList() ?? new List<QuizWindow>())
            {
                try { window.Close(); } catch { }
            }
        }
        catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        IsOpen = false;
        _loadingDotsTimer.Stop();
        _quizService?.Dispose();
        _quizService = null;
        base.OnClosed(e);
    }
}
