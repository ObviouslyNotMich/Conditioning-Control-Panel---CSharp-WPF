using System;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Core.Services.MindWipe;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Settings;
using LibVLCSharp.Shared;

namespace ConditioningControlPanel.Avalonia.Services.MindWipe;

/// <summary>
/// Avalonia implementation of the mind-wipe audio effect engine.
/// Plays scheduled or looping mind-wipe phrases from the user's assets folder
/// or the built-in Resources/sounds/mindwipe directory using LibVLCSharp.
/// </summary>
public sealed class AvaloniaMindWipeService : IMindWipeService, IDisposable
{
    private static readonly string[] AudioExtensions = { ".mp3", ".wav", ".ogg", ".flac", ".m4a" };

    private readonly ISettingsService _settings;
    private readonly IAppEnvironment _environment;
    private readonly LibVLC _libVlc;
    private readonly IAchievementService _achievements;
    private readonly IDialogService _dialogService;
    private readonly ILogger<AvaloniaMindWipeService>? _logger;
    private readonly Random _random = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(10) };

    private string[] _audioFiles = Array.Empty<string>();
    private MediaPlayer? _mediaPlayer;
    private Media? _currentMedia;
    private bool _isRunning;
    private bool _loopMode;
    private double _frequencyPerHour = 6;
    private double _volume = 0.5;
    private DateTime _loopStartTime;
    private bool _cleanSlateAchieved;
    private bool _disposed;

    public AvaloniaMindWipeService(
        ISettingsService settings,
        IAppEnvironment environment,
        LibVLC libVlc,
        IAchievementService achievements,
        IDialogService dialogService,
        ILogger<AvaloniaMindWipeService>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _libVlc = libVlc ?? throw new ArgumentNullException(nameof(libVlc));
        _achievements = achievements ?? throw new ArgumentNullException(nameof(achievements));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _logger = logger;

        _mediaPlayer = new MediaPlayer(_libVlc);
        _mediaPlayer.EndReached += OnMediaPlayerEndReached;
        _timer.Tick += Timer_Tick;

        LoadAudioFiles();
    }

    public bool IsRunning => _isRunning;
    public bool IsLooping => _loopMode && _mediaPlayer?.IsPlaying == true;
    public int AudioFileCount => _audioFiles.Length;

    public event EventHandler? MindWipeTriggered;

    public void Start(double frequencyPerHour, double volume)
    {
        if (_disposed) return;
        if (_isRunning)
        {
            UpdateSettings(frequencyPerHour, volume);
            return;
        }

        LoadAudioFiles();
        _frequencyPerHour = frequencyPerHour;
        _volume = Math.Clamp(volume, 0, 1);
        _loopMode = false;
        _isRunning = true;

        _timer.Start();
        _logger?.LogInformation("AvaloniaMindWipeService started (frequency: {Freq}/hour, volume: {Vol}%, files: {Count})",
            frequencyPerHour, _volume * 100, _audioFiles.Length);
    }

    public void Stop()
    {
        if (!_isRunning && !_loopMode) return;
        _isRunning = false;
        _loopMode = false;
        _timer.Stop();
        StopCurrentAudio();
        _logger?.LogInformation("AvaloniaMindWipeService stopped");
    }

    public void StartLoop(double volume)
    {
        if (_disposed) return;
        LoadAudioFiles();
        if (_audioFiles.Length == 0)
        {
            _logger?.LogWarning("AvaloniaMindWipeService: no audio files available for loop");
            return;
        }

        StopCurrentAudio();
        _loopMode = true;
        _volume = Math.Clamp(volume, 0, 1);
        _loopStartTime = DateTime.Now;
        _cleanSlateAchieved = false;

        var file = _audioFiles[_random.Next(_audioFiles.Length)];
        PlayFile(file, _volume, loop: true);
        _logger?.LogInformation("AvaloniaMindWipeService loop started with {File}", Path.GetFileName(file));
    }

    public void StopLoop()
    {
        if (!_loopMode) return;

        if (!_cleanSlateAchieved)
        {
            var elapsed = (DateTime.Now - _loopStartTime).TotalSeconds;
            if (elapsed >= 60)
            {
                _cleanSlateAchieved = true;
                _achievements.TrackMindWipeDuration(elapsed);
            }
        }

        _loopMode = false;
        StopCurrentAudio();
        _logger?.LogInformation("AvaloniaMindWipeService loop stopped");
    }

    public void TriggerOnce()
    {
        LoadAudioFiles();
        if (_audioFiles.Length == 0)
        {
            _logger?.LogWarning("AvaloniaMindWipeService: no audio files available");
            _ = _dialogService.ShowMessageAsync(
                "Mind Wipe",
                "No audio files found in assets/mindwipe/.");
            return;
        }

        _volume = (_settings.Current?.MindWipeVolume ?? 50) / 100.0;
        PlayAudioNow();
    }

    public void ReloadAudioFiles()
    {
        LoadAudioFiles();
    }

    public void UpdateSettings(double frequencyPerHour, double volume)
    {
        _frequencyPerHour = frequencyPerHour;
        _volume = Math.Clamp(volume, 0, 1);
        try
        {
            if (_mediaPlayer != null)
                _mediaPlayer.Volume = (int)(_volume * 100);
        }
        catch { }
    }

    private void LoadAudioFiles()
    {
        try
        {
            var customPath = _settings.Current?.MindWipeAudioPath;
            if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
            {
                _audioFiles = new[] { customPath };
                _logger?.LogInformation("AvaloniaMindWipeService: using custom audio file {Path}", customPath);
                return;
            }

            var audioFolderPath = Path.Combine(_environment.BaseDirectory, "Resources", "sounds", "mindwipe");
            if (!Directory.Exists(audioFolderPath))
            {
                try { Directory.CreateDirectory(audioFolderPath); } catch { }
                _audioFiles = Array.Empty<string>();
                _logger?.LogWarning("AvaloniaMindWipeService: created empty folder at {Path}", audioFolderPath);
                return;
            }

            _audioFiles = Directory.GetFiles(audioFolderPath, "*.*")
                .Where(f => AudioExtensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "AvaloniaMindWipeService: failed to load audio files");
            _audioFiles = Array.Empty<string>();
        }
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_loopMode || !_isRunning) return;
        if (_audioFiles.Length == 0)
        {
            _logger?.LogWarning("AvaloniaMindWipeService: timer ticked but no audio files loaded");
            return;
        }

        double probability = _frequencyPerHour / 360.0;
        var roll = _random.NextDouble();
        if (roll < probability)
        {
            _logger?.LogInformation("AvaloniaMindWipeService: triggering audio (roll {Roll:F2} < prob {Prob:F2})", roll, probability);
            PlayAudioNow();
        }
    }

    private void PlayAudioNow()
    {
        if (_audioFiles.Length == 0) return;
        var file = _audioFiles[_random.Next(_audioFiles.Length)];
        PlayFile(file, _volume, loop: false);
        MindWipeTriggered?.Invoke(this, EventArgs.Empty);
    }

    private void PlayFile(string filePath, double volume, bool loop)
    {
        if (_mediaPlayer == null) return;
        StopCurrentAudio();

        try
        {
            _currentMedia = new Media(_libVlc, filePath);
            if (loop)
                _currentMedia.AddOption(":input-repeat=-1");

            _mediaPlayer.Volume = (int)(volume * 100);
            _mediaPlayer.Play(_currentMedia);

            _logger?.LogDebug("AvaloniaMindWipeService: playing {File} (loop={Loop})", Path.GetFileName(filePath), loop);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "AvaloniaMindWipeService: failed to play {File}", filePath);
        }
    }

    private void StopCurrentAudio()
    {
        try
        {
            _mediaPlayer?.Stop();
            _currentMedia?.Dispose();
            _currentMedia = null;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("AvaloniaMindWipeService: error stopping audio: {Error}", ex.Message);
        }
    }

    private void OnMediaPlayerEndReached(object? sender, EventArgs e)
    {
        // LibVLC fires this on its own thread; dispatch cleanup to the UI thread.
        Dispatcher.UIThread.Post(() =>
        {
            if (_loopMode) return; // looping uses input-repeat, so EndReached is not expected
            try
            {
                _currentMedia?.Dispose();
                _currentMedia = null;
            }
            catch { }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _timer.Tick -= Timer_Tick;
        if (_mediaPlayer != null)
        {
            _mediaPlayer.EndReached -= OnMediaPlayerEndReached;
            _mediaPlayer.Dispose();
            _mediaPlayer = null;
        }
        _currentMedia?.Dispose();
        _currentMedia = null;
    }
}
