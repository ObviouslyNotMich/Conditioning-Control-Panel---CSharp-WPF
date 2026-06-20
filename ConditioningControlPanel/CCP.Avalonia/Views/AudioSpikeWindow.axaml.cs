using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Views;

public partial class AudioSpikeWindow : Window
{
    private readonly IAudioPlayer _audioPlayer;

    public AudioSpikeWindow()
    {
        InitializeComponent();
        _audioPlayer = App.Services.GetRequiredService<IAudioPlayer>();
        VolumeSlider.ValueChanged += (_, e) => _audioPlayer.SetVolume(e.NewValue);
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        try
        {
            var path = FindSampleAudio();
            StatusText.Text = string.IsNullOrEmpty(path)
                ? "No sample audio found."
                : $"Ready: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Init failed: {ex.Message}";
        }
    }

    private async void PlayButton_Click(object? sender, RoutedEventArgs e)
    {
        var path = FindSampleAudio();
        if (string.IsNullOrEmpty(path))
        {
            StatusText.Text = "No sample audio found.";
            return;
        }

        await _audioPlayer.PlayAsync(path);
        StatusText.Text = $"Playing: {Path.GetFileName(path)}";
    }

    private async void LoopButton_Click(object? sender, RoutedEventArgs e)
    {
        var path = FindSampleAudio();
        if (string.IsNullOrEmpty(path))
        {
            StatusText.Text = "No sample audio found.";
            return;
        }

        await _audioPlayer.PlayLoopAsync(path);
        StatusText.Text = $"Looping: {Path.GetFileName(path)}";
    }

    private void StopButton_Click(object? sender, RoutedEventArgs e)
    {
        _audioPlayer.Stop();
        StatusText.Text = "Stopped.";
    }

    private static string? FindSampleAudio()
    {
        const string fileName = "chime1.mp3";
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Resources", "sounds", fileName),
            Path.Combine(AppContext.BaseDirectory, "Resources", "sounds", fileName),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Resources", "sounds", fileName),
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
