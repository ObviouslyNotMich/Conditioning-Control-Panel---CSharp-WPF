using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Mantra;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Avalonia port of the full-screen mantra typing lab.
///
/// Audio uses the cross-platform <see cref="IAudioPlayer"/> seam: the drone is a short
/// generated sine loop and the typing tones are generated WAV files played through LibVLC.
/// </summary>
public partial class MantraWindow : Window
{
    private readonly IMantraService _service;
    private readonly IAudioPlayer? _audioPlayer;
    private DispatcherTimer? _floatTimer;
    private DispatcherTimer? _idleTimer;
    private DispatcherTimer? _pulseTimer;
    private DateTime _startTime;
    private bool _sessionComplete;
    private bool _updatingInput;

    private GradientStop _baseCenter = null!;
    private GradientStop _washCenter = null!;
    private SolidColorBrush _inputBorderBrush = null!;
    private ScaleTransform _mantraScale = null!;
    private TranslateTransform _mantraTranslate = null!;

    // Per-character highlight state
    private readonly List<Run> _mantraRuns = new();
    private int _prevMatchCount;
    private int _prevInputLength;
    private Color _highlightColor = Color.FromRgb(0x99, 0x88, 0xDD);
    private static readonly Color DimColor = Color.FromRgb(0x35, 0x35, 0x50);
    private static readonly Color ErrorColor = Color.FromRgb(0xFF, 0x44, 0x44);
    private static readonly Color FlashColor = (Color)global::Avalonia.Application.Current!.Resources["TextLight"]!;

    private readonly List<string> _tempAudioFiles = new();
    private string? _droneFile;

    public MantraWindow()
    {
        InitializeComponent();
        _service = App.Services?.GetService<IMantraService>() ?? new MantraService(
            App.Services?.GetRequiredService<ISettingsService>()
            ?? throw new InvalidOperationException("Settings service is required for MantraWindow."));
        _audioPlayer = App.Services?.GetService<IAudioPlayer>();
    }

    private void Window_Loaded(object? sender, RoutedEventArgs e)
    {
        var baseBrush = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.7, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.7, RelativeUnit.Relative)
        };
        baseBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x1A, 0x0A, 0x2E), 0));
        baseBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x0A, 0x05, 0x14), 1));
        _baseCenter = baseBrush.GradientStops[0];
        BaseLayer.Background = baseBrush;

        var washBrush = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.6, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.6, RelativeUnit.Relative)
        };
        washBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x66, 0x33, 0xAA), 0));
        washBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        _washCenter = washBrush.GradientStops[0];
        WashLayer.Background = washBrush;

        _inputBorderBrush = (InputBorderHost.BorderBrush as SolidColorBrush) ?? new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0x69, 0xB4));
        _mantraScale = new ScaleTransform(1, 1);
        _mantraTranslate = new TranslateTransform(0, 0);
        TxtMantra.RenderTransform = new TransformGroup { Children = { _mantraScale, _mantraTranslate } };

        _startTime = DateTime.UtcNow;

        _service.StreakChanged += OnStreakChanged;
        _service.StreakBroken += OnStreakBroken;
        _service.MantraCompleted += OnMantraCompleted;
        _service.SessionComplete += OnSessionComplete;

        _service.StartSession(10);

        BuildMantraRuns(_service.CurrentMantra ?? "");
        TxtTarget.Text = $"/{_service.TargetCount}";
        TxtCompletions.Text = _service.Completions.ToString();
        TxtStreak.Text = _service.Streak.ToString();
        TxtBestStreak.Text = _service.BestStreak.ToString();

        _floatTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _floatTimer.Tick += FloatTimer_Tick;
        _floatTimer.Start();

        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _idleTimer.Tick += IdleTimer_Tick;
        _idleTimer.Start();

        _pulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) };
        _pulseTimer.Tick += (_, _) => PulseGlow();
        _pulseTimer.Start();

        StartDrone();
        TxtInput.Focus();
    }

    #region Per-character highlight system

    private void BuildMantraRuns(string mantra)
    {
        TxtMantra.Inlines?.Clear();
        _mantraRuns.Clear();
        _prevMatchCount = 0;
        _prevInputLength = 0;

        foreach (char c in mantra)
        {
            var run = new Run(c.ToString())
            {
                Foreground = new SolidColorBrush(DimColor)
            };
            _mantraRuns.Add(run);
            TxtMantra.Inlines?.Add(run);
        }
    }

    private int UpdateHighlights(string input)
    {
        var mantra = _service.CurrentMantra;
        if (mantra == null || _mantraRuns.Count == 0) return 0;

        int matchCount = 0;
        bool hasError = false;

        for (int i = 0; i < mantra.Length && i < input.Length; i++)
        {
            if (char.ToLowerInvariant(input[i]) == char.ToLowerInvariant(mantra[i]))
                matchCount = i + 1;
            else
            {
                hasError = true;
                break;
            }
        }

        for (int i = 0; i < _mantraRuns.Count; i++)
        {
            Color color;
            if (i < matchCount)
                color = _highlightColor;
            else if (hasError && i == matchCount)
                color = ErrorColor;
            else
                color = DimColor;

            _mantraRuns[i].Foreground = new SolidColorBrush(color);
        }

        bool newCharTyped = input.Length > _prevInputLength;
        if (newCharTyped && matchCount > _prevMatchCount && matchCount > 0)
        {
            int flashIdx = matchCount - 1;
            _mantraRuns[flashIdx].Foreground = new SolidColorBrush(FlashColor);

            var idx = flashIdx;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (idx < _mantraRuns.Count)
                    _mantraRuns[idx].Foreground = new SolidColorBrush(_highlightColor);
            };
            timer.Start();

            PulseLetter();
        }

        if (newCharTyped && hasError && matchCount == _prevMatchCount)
        {
            ShakeText();
        }

        _prevMatchCount = matchCount;
        _prevInputLength = input.Length;

        return matchCount;
    }

    #endregion

    private void FloatTimer_Tick(object? sender, EventArgs e)
    {
        var elapsed = (DateTime.UtcNow - _startTime).TotalSeconds;
        _mantraTranslate.Y = Math.Sin(elapsed * 0.5) * 6;
    }

    private void IdleTimer_Tick(object? sender, EventArgs e)
    {
        if (_service.IsActive && _service.Streak > 0)
        {
            _service.BreakStreak();
        }
    }

    private void TxtInput_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_updatingInput || _sessionComplete || !_service.IsActive) return;

        _idleTimer?.Stop();
        _idleTimer?.Start();

        var input = TxtInput.Text ?? "";
        var target = _service.CurrentMantra;
        if (target == null) return;

        int matchCount = UpdateHighlights(input);

        if (matchCount == target.Length && input.Length == target.Length)
        {
            if (_service.TryCompleteMantra())
            {
                _updatingInput = true;
                TxtInput.Text = "";
                _updatingInput = false;
            }
        }
    }

    private void TxtInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control &&
            (e.Key == Key.C || e.Key == Key.V || e.Key == Key.A))
        {
            e.Handled = true;
        }
    }

    private void OnMantraCompleted()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(OnMantraCompleted); return; }

        BuildMantraRuns(_service.CurrentMantra ?? "");
        TxtCompletions.Text = _service.Completions.ToString();

        PulseText();
        PlayTone(400 + _service.Streak * 20, 150);
    }

    private void OnStreakChanged(int streak)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => OnStreakChanged(streak)); return; }

        TxtStreak.Text = streak.ToString();
        TxtBestStreak.Text = _service.BestStreak.ToString();

        UpdateVisualIntensity(streak);
    }

    private void OnStreakBroken()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(OnStreakBroken); return; }

        ShakeText();
        PlayTone(200, 300);
        UpdateVisualIntensity(0);
    }

    private void OnSessionComplete(int totalReps, int bestStreak)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => OnSessionComplete(totalReps, bestStreak)); return; }

        _sessionComplete = true;
        _idleTimer?.Stop();

        TxtCompletionStats.Text = $"{totalReps} repetitions  |  Best streak: {bestStreak}";
        CompletionOverlay.IsVisible = true;
        TxtInput.IsEnabled = false;

        PlayTone(523, 400);

        var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        closeTimer.Tick += (_, _) =>
        {
            closeTimer.Stop();
            CleanupAndClose();
        };
        closeTimer.Start();
    }

    private void UpdateVisualIntensity(int streak)
    {
        double t = Math.Min(streak / 15.0, 1.0);

        _highlightColor = LerpColor(Color.FromRgb(0x99, 0x88, 0xDD), Color.FromRgb(0xFF, 0x69, 0xB4), t);

        var input = TxtInput.Text ?? "";
        var mantra = _service.CurrentMantra;
        if (mantra != null)
        {
            int matchLen = 0;
            for (int i = 0; i < mantra.Length && i < input.Length; i++)
            {
                if (char.ToLowerInvariant(input[i]) == char.ToLowerInvariant(mantra[i]))
                    matchLen = i + 1;
                else break;
            }
            for (int i = 0; i < matchLen && i < _mantraRuns.Count; i++)
                _mantraRuns[i].Foreground = new SolidColorBrush(_highlightColor);
        }

        WashLayer.Opacity = t * 0.8;
        _washCenter.Color = LerpColor(Color.FromRgb(0x66, 0x33, 0xAA), Color.FromRgb(0xFF, 0x69, 0xB4), t);

        var glowColor = LerpColor(Color.FromRgb(0x99, 0x66, 0xCC), Color.FromRgb(0xFF, 0x69, 0xB4), t);
        var inputBorderColor = LerpColor(Color.FromArgb(0x40, 0xFF, 0x69, 0xB4), Color.FromArgb(0xFF, 0xFF, 0x69, 0xB4), t);
        var baseCenter = LerpColor(Color.FromRgb(0x1A, 0x0A, 0x2E), Color.FromRgb(0x2E, 0x0A, 0x2E), t);

        MantraBorder.BoxShadow = new BoxShadows(new BoxShadow
        {
            Color = glowColor,
            Blur = (double)(20 + t * 30),
            OffsetX = 0,
            OffsetY = 0,
            Spread = 0
        });

        _inputBorderBrush.Color = inputBorderColor;
        _baseCenter.Color = baseCenter;
    }

    private static Color LerpColor(Color a, Color b, double t)
    {
        return Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t),
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    private void PulseText()
    {
        AnimateScale(1.0, 1.06, 150);
    }

    private void PulseLetter()
    {
        AnimateScale(1.0, 1.02, 80);
    }

    private void ShakeText()
    {
        AnimateShake(new[] { 8.0, -8.0, 6.0, -6.0, 3.0, 0.0 }, 50);
    }

    private DispatcherTimer? _animTimer;

    private void AnimateScale(double from, double to, int durationMs)
    {
        var start = DateTime.UtcNow;
        _animTimer?.Stop();
        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _animTimer.Tick += (_, _) =>
        {
            var elapsed = (DateTime.UtcNow - start).TotalMilliseconds;
            var p = Math.Min(1, elapsed / durationMs);
            var scale = from + (to - from) * Math.Sin(p * Math.PI);
            _mantraScale.ScaleX = scale;
            _mantraScale.ScaleY = scale;
            if (p >= 1)
            {
                _animTimer?.Stop();
                _mantraScale.ScaleX = 1;
                _mantraScale.ScaleY = 1;
            }
        };
        _animTimer.Start();
    }

    private void AnimateShake(double[] steps, int intervalMs)
    {
        var idx = 0;
        _animTimer?.Stop();
        _mantraTranslate.X = steps[0];
        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(intervalMs) };
        _animTimer.Tick += (_, _) =>
        {
            idx++;
            if (idx >= steps.Length)
            {
                _animTimer?.Stop();
                _mantraTranslate.X = 0;
                return;
            }
            _mantraTranslate.X = steps[idx];
        };
        _animTimer.Start();
    }

    private double _glowPhase;
    private void PulseGlow()
    {
        _glowPhase += Math.PI;
        GlowOverlay.Opacity = (Math.Sin(_glowPhase) + 1) * 0.15;
    }

    #region Audio

    private void StartDrone()
    {
        if (_audioPlayer == null) return;
        try
        {
            _droneFile = Path.Combine(Path.GetTempPath(), $"ccp_mantra_drone_{Guid.NewGuid():N}.wav");
            MantraToneGenerator.WriteSineWave(_droneFile, 44100, 2000,
                new[] { (90.0, 0.05), (180.0, 0.02) });
            _tempAudioFiles.Add(_droneFile);
            _audioPlayer.SetVolume(0.5);
            _audioPlayer.PlayLoopAsync(_droneFile);
        }
        catch (Exception ex)
        {
            App.Services?.GetRequiredService<ILogger<MantraWindow>>().LogWarning(ex, "Failed to start mantra drone audio");
        }
    }

    private void StopDrone()
    {
        try { _audioPlayer?.Stop(); }
        catch { /* ignore */ }
    }

    private void PlayTone(double frequency, int durationMs)
    {
        if (_audioPlayer == null) return;
        try
        {
            var path = Path.Combine(Path.GetTempPath(), $"ccp_mantra_tone_{frequency:0}_{durationMs}_{Guid.NewGuid():N}.wav");
            MantraToneGenerator.WriteSineWave(path, 44100, durationMs, new[] { (frequency, 0.15) });
            _tempAudioFiles.Add(path);
            _audioPlayer.SetVolume(0.6);
            _audioPlayer.PlayAsync(path);
            _ = CleanupToneFileAsync(path, durationMs + 500);
        }
        catch (Exception ex)
        {
            App.Services?.GetRequiredService<ILogger<MantraWindow>>().LogWarning(ex, "Failed to play mantra tone");
        }
    }

    private async Task CleanupToneFileAsync(string path, int delayMs)
    {
        try
        {
            await Task.Delay(delayMs);
            if (File.Exists(path))
            {
                File.Delete(path);
                _tempAudioFiles.Remove(path);
            }
        }
        catch
        {
            // If LibVLC still has the file locked it will be cleaned up on window close.
        }
    }

    private void DeleteTempAudioFiles()
    {
        foreach (var path in _tempAudioFiles.ToArray())
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch { /* ignore locked files */ }
        }
        _tempAudioFiles.Clear();
    }

    /// <summary>
    /// Generates simple 16-bit mono PCM WAV files from one or more sine waves.
    /// </summary>
    private static class MantraToneGenerator
    {
        public static void WriteSineWave(string path, int sampleRate, int durationMs, IEnumerable<(double Frequency, double Gain)> components)
        {
            var comps = components.ToList();
            int totalSamples = sampleRate * durationMs / 1000;
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var writer = new BinaryWriter(stream);

            // WAV header placeholders; actual sizes written after samples.
            writer.Write("RIFF"u8);
            writer.Write(0);
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write((short)1); // mono
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2); // byte rate
            writer.Write((short)2); // block align
            writer.Write((short)16); // bits per sample
            writer.Write("data"u8);
            long dataSizePosition = stream.Position;
            writer.Write(0);

            int samplesWritten = 0;
            for (int i = 0; i < totalSamples; i++)
            {
                double sample = 0.0;
                foreach (var (freq, gain) in comps)
                    sample += Math.Sin(2 * Math.PI * freq * i / sampleRate) * gain;
                // Soft clamp to avoid clipping when mixing multiple components.
                sample = Math.Tanh(sample);
                short value = (short)(sample * 32767);
                writer.Write(value);
                samplesWritten++;
            }

            long fileLength = stream.Length;
            stream.Position = 4;
            writer.Write((int)(fileLength - 8));
            stream.Position = dataSizePosition;
            writer.Write(samplesWritten * 2);
        }
    }

    #endregion

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CleanupAndClose();
            e.Handled = true;
            return;
        }

        if (_sessionComplete)
        {
            CleanupAndClose();
            e.Handled = true;
        }
    }

    private void CleanupAndClose()
    {
        CleanupResources();
        Close();
    }

    private void CleanupResources()
    {
        _floatTimer?.Stop();
        _idleTimer?.Stop();
        _pulseTimer?.Stop();
        _animTimer?.Stop();
        StopDrone();
        DeleteTempAudioFiles();

        _service.StreakChanged -= OnStreakChanged;
        _service.StreakBroken -= OnStreakBroken;
        _service.MantraCompleted -= OnMantraCompleted;
        _service.SessionComplete -= OnSessionComplete;

        if (_service.IsActive)
            _service.EndSession();
    }

    protected override void OnClosed(EventArgs e)
    {
        CleanupResources();
        base.OnClosed(e);
    }

}
