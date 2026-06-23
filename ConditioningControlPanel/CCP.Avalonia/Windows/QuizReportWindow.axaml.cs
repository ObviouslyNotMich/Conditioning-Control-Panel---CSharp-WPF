using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models.Quiz;
using AvaloniaLayout = global::Avalonia.Layout;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Avalonia port of the quiz-report/history-detail window.
/// </summary>
public partial class QuizReportWindow : Window
{
    public QuizReportWindow()
    {
        InitializeComponent();
    }

    public QuizReportWindow(QuizHistoryEntry entry) : this()
    {
        var categoryDisplay = !string.IsNullOrEmpty(entry.CategoryName) ? entry.CategoryName : entry.Category.ToString();
        TxtSubtitle.Text = $"{categoryDisplay}  ·  {entry.TakenAt:MMM d, yyyy  h:mm tt}";
        var pct = entry.MaxScore > 0 ? (int)Math.Round((double)entry.TotalScore / entry.MaxScore * 100) : 0;
        TxtScore.Text = $"{entry.TotalScore} / {entry.MaxScore}  ({pct}%)";

        BuildQuestions(entry);
        BuildProfileCard(entry.ProfileText);
    }

    private void BuildQuestions(QuizHistoryEntry entry)
    {
        var letters = new[] { "A", "B", "C", "D" };

        foreach (var answer in entry.Answers)
        {
            var qHeader = new TextBlock
            {
                Text = $"Q{answer.QuestionNumber}. {answer.QuestionText}",
                Foreground = (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!,
                FontWeight = FontWeight.Bold,
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 14, 0, 6)
            };
            ContentPanel.Children.Add(qHeader);

            for (int i = 0; i < 4; i++)
            {
                if (i >= answer.AllAnswers.Length) break;

                bool isChosen = i == answer.ChosenIndex;

                var row = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12, 7),
                    Margin = new Thickness(0, 2, 0, 2),
                    Background = isChosen
                        ? new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0x69, 0xB4))
                        : new SolidColorBrush(Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF)),
                    BorderBrush = isChosen
                        ? new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0x69, 0xB4))
                        : new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(1)
                };

                var grid = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = new GridLength(28) },
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                        new ColumnDefinition { Width = GridLength.Auto }
                    }
                };

                var letterTxt = new TextBlock
                {
                    Text = letters[i],
                    FontWeight = FontWeight.Bold,
                    FontSize = 13,
                    Foreground = isChosen
                        ? new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4))
                        : new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)),
                    VerticalAlignment = AvaloniaLayout.VerticalAlignment.Center
                };
                Grid.SetColumn(letterTxt, 0);
                grid.Children.Add(letterTxt);

                var answerTxt = new TextBlock
                {
                    Text = answer.AllAnswers[i],
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = isChosen
                        ? new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xF0))
                        : new SolidColorBrush(Color.FromArgb(0x70, 0xC0, 0xC0, 0xD0)),
                    VerticalAlignment = AvaloniaLayout.VerticalAlignment.Center
                };
                Grid.SetColumn(answerTxt, 1);
                grid.Children.Add(answerTxt);

                var pointsTxt = new TextBlock
                {
                    Text = isChosen ? $"+{answer.PointsEarned}" : $"{answer.AllPoints[i]}",
                    FontSize = 12,
                    Foreground = isChosen
                        ? new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4))
                        : new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x90)),
                    VerticalAlignment = AvaloniaLayout.VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                };
                Grid.SetColumn(pointsTxt, 2);
                grid.Children.Add(pointsTxt);

                row.Child = grid;
                ContentPanel.Children.Add(row);
            }
        }
    }

    private void BuildProfileCard(string profileText)
    {
        if (string.IsNullOrWhiteSpace(profileText)) return;

        var headerTxt = new TextBlock
        {
            Text = "YOUR PROFILE",
            Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x90)),
            FontWeight = FontWeight.Bold,
            FontSize = 12,
            HorizontalAlignment = AvaloniaLayout.HorizontalAlignment.Center,
            Margin = new Thickness(0, 20, 0, 8)
        };
        ContentPanel.Children.Add(headerTxt);

        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20, 16),
            Margin = new Thickness(0, 0, 0, 8),
            Background = new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1)
        };
        card.BorderBrush = new LinearGradientBrush
        {
            GradientStops = new GradientStops
            {
                new GradientStop(Color.FromArgb(0x40, 0xFF, 0x69, 0xB4), 0),
                new GradientStop(Color.FromArgb(0x40, 0x9B, 0x59, 0xB6), 1)
            }
        };

        var profileTxt = new TextBlock
        {
            Text = profileText,
            FontSize = 15,
            Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xD8)),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 24,
            TextAlignment = TextAlignment.Center
        };

        card.Child = profileTxt;
        ContentPanel.Children.Add(card);
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
