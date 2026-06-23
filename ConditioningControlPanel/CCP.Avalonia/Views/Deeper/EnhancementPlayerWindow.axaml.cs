using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ShapesPath = Avalonia.Controls.Shapes.Path;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Deeper;
using ConditioningControlPanel.Avalonia.Services.Deeper;
using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Views.Deeper;

/// <summary>
/// Avalonia/LibVLC port of the Deeper enhancement player view.
///
/// Media playback, event log, mini-timeline, and engine-host binding are wired.
/// PiP and eye-tracking are parity stubs pending cross-platform surface support.
/// </summary>
public partial class EnhancementPlayerWindow : Window
{
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;
    private readonly IDialogService _dialogService;
    private readonly AudioWaveformCache? _waveformCache;
    private readonly ILogger<EnhancementPlayerWindow>? _logger;
    private Media? _currentMedia;

    private readonly DispatcherTimer _uiTimer;
    private readonly ObservableCollection<EventLogEntry> _logEntries = new();
    private readonly List<EventLogEntry> _filteredEntries = new();

    private Enhancement? _loadedEnhancement;
    private string? _loadedFilePath;
    private string? _lastMediaPath;
    private bool _isVideoMode;
    private bool _suppressVolumeSync;
    private bool _isScrubbing;
    private bool _loadInProgress;
    private bool _eventLogCollapsed;
    private double _eventLogExpandedHeight = 140;
    private string _activeFilter = "all";
    private float[]? _peaks = null;

    // Timeline / overlay state
    private Enhancement? _miniEnhancement;
    private double _miniTotalSeconds;

    // Engine-host state
    private readonly EnhancementHostService _host;
    private AvaloniaLibVlcTimeSource? _timeSource;

    public EnhancementPlayerWindow()
    {
        InitializeComponent();

        _libVlc = App.Services.GetRequiredService<LibVLC>();
        _dialogService = App.Services.GetRequiredService<IDialogService>();
        _host = App.Services.GetRequiredService<EnhancementHostService>();
        _waveformCache = App.Services.GetService<AudioWaveformCache>();
        _logger = App.Services.GetRequiredService<ILogger<EnhancementPlayerWindow>>();
        _mediaPlayer = new MediaPlayer(_libVlc);

        _host.ActionLogged += OnHostActionLogged;
        _host.Diagnostic += OnHostDiagnostic;

        WireMediaPlayerEvents();

        VideoView.MediaPlayer = _mediaPlayer;

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _uiTimer.Tick += UiTimer_Tick;
        _uiTimer.Start();

        InitializeEventLog();
        UpdateVolumeFromPlayer();

        AddHandler(DragDrop.DragOverEvent, Window_DragOver);
        AddHandler(DragDrop.DropEvent, Window_Drop);
        Closing += Window_Closing;
    }

    /// <summary>
    /// Opens the player with an in-memory enhancement pre-loaded (editor preview flow).
    /// </summary>
    public EnhancementPlayerWindow(Enhancement enhancement, string sourceTag) : this()
    {
        if (enhancement == null) return;
        Loaded += (_, _) => LoadEnhancement(enhancement, sourceTag);
    }

    // ========================================================================
    // Public load entry points
    // ========================================================================

    public void LoadEnhancementFile(string ccpenhJsonPath)
    {
        if (string.IsNullOrWhiteSpace(ccpenhJsonPath)) return;
        void Load() => _ = LoadEnhancementFromFileAsync(ccpenhJsonPath);
        if (IsLoaded) Load();
        else Loaded += (_, _) => Load();
    }

    public void OpenLocalMediaFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        void Load()
        {
            if (IsLocalVideoFile(path)) _ = LoadVideoAsync(path);
            else LoadAudio(path);
        }
        if (IsLoaded) Load();
        else Loaded += (_, _) => Load();
    }

    // ========================================================================
    // Media player wiring
    // ========================================================================

    private void WireMediaPlayerEvents()
    {
        _mediaPlayer.Playing += (_, _) => OnPlaybackStateChanged();
        _mediaPlayer.Paused += (_, _) => OnPlaybackStateChanged();
        _mediaPlayer.Stopped += (_, _) => OnPlaybackStopped();
        _mediaPlayer.EndReached += (_, _) => Dispatcher.UIThread.Post(OnPlaybackEnded);
        _mediaPlayer.EncounteredError += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            TxtStatus.Text = Loc.Get("deeper_player_status_audio_failed");
            IngestErrorLine(Loc.Get("deeper_player_error_libvlc"));
        });
    }

    private void OnPlaybackStateChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            BtnPlayPause.Content = _mediaPlayer.IsPlaying ? "⏸" : "▶";
            UpdateStatusPill();
        });
    }

    private void OnPlaybackStopped()
    {
        Dispatcher.UIThread.Post(() =>
        {
            BtnPlayPause.Content = "▶";
            UpdateStatusPill();
        });
    }

    private void OnPlaybackEnded()
    {
        BtnPlayPause.Content = "▶";
        TxtStatus.Text = Loc.Get("deeper_player_status_ended");
        _host.UnbindEngine();
        DisposeTimeSource();
    }

    // ========================================================================
    // Enhancement loading
    // ========================================================================

    private async Task LoadEnhancementFromFileAsync(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                IngestErrorLine(string.Format(Loc.Get("deeper_player_error_file_not_found_fmt"), path));
                return;
            }

            var enh = EnhancementSerializer.LoadFromFile(path);
            var issues = EnhancementValidator.Validate(enh);
            var firstError = issues.FirstOrDefault(i => i.Severity == ValidationSeverity.Error);
            if (firstError != null)
            {
                IngestErrorLine(string.Format(Loc.Get("deeper_player_error_validation_fmt"), firstError.Message));
                return;
            }

            LoadEnhancement(enh, path);
        }
        catch (Exception ex)
        {
            IngestErrorLine(string.Format(Loc.Get("deeper_player_error_load_fmt"), ex.Message));
        }
    }

    private void LoadEnhancement(Enhancement enh, string path)
    {
        _loadedEnhancement = enh;
        _loadedFilePath = path;

        _host.LoadFromMemory(enh, path);

        UpdateHostUi(enh, path);
        OnEnhancementLoadedForMini(enh);

        if (string.Equals(enh.MediaType, MediaTypes.Video, StringComparison.OrdinalIgnoreCase))
        {
            if (IsRemoteVideoUrl(enh.MediaSource))
            {
                _ = LoadVideoAsync(enh.MediaSource);
            }
            else if (!string.IsNullOrEmpty(enh.MediaSource) && File.Exists(enh.MediaSource))
            {
                _ = LoadVideoAsync(enh.MediaSource);
            }
        }
        else if (_mediaPlayer.IsPlaying)
        {
            BindEngineIfReady();
        }
    }

    private void UnloadEnhancement()
    {
        _loadedEnhancement = null;
        _loadedFilePath = null;
        _host.Unload();
        UpdateHostUi(null, null);
        OnEnhancementLoadedForMini(null);
    }

    // ========================================================================
    // UI update (host / file context)
    // ========================================================================

    private void UpdateHostUi(Enhancement? enh, string? path)
    {
        if (enh == null)
        {
            TxtEnhName.Text = Loc.Get("deeper_player_no_enh");
            TxtEnhMetadata.Text = "";
            TxtEnhPath.Text = "";
            TxtEnhPath.IsVisible = false;
            TxtEnhSource.Text = "";
            TxtEnhSource.IsVisible = false;
            SourcePill.IsVisible = false;
            BtnUnloadEnhancement.IsVisible = false;
            BtnCreateNewEnhancement.IsVisible = false;
            BtnOpenInEditor.IsVisible = false;
            ShowMediaPaneFor(MediaTypes.Audio);
            UpdateStatusPill();
            return;
        }

        var name = string.IsNullOrEmpty(enh.Metadata?.Name) ? Loc.Get("deeper_player_untitled") : enh.Metadata!.Name;
        var creator = string.IsNullOrEmpty(enh.Metadata?.Creator) ? "" : $" — {enh.Metadata.Creator}";
        var haptics = enh.HapticTracks.Sum(t => t?.Events?.Count ?? 0);
        var counts = string.Format(Loc.Get("deeper_player_meta_counts_fmt"), enh.Regions.Count, enh.Rules.Count, haptics);
        TxtEnhName.Text = name;
        TxtEnhMetadata.Text = $"{name}{creator}  ·  {counts}";
        TxtEnhPath.Text = path ?? "";
        TxtEnhSource.Text = Loc.Get("deeper_player_enh_source_manual");
        TxtEnhSource.IsVisible = true;
        SourcePill.IsVisible = true;
        BtnUnloadEnhancement.IsVisible = true;
        BtnCreateNewEnhancement.IsVisible = false;
        BtnOpenInEditor.IsVisible = true;

        RefreshFileContextStrip(enh, path);
        ShowMediaPaneFor(enh.MediaType);
        UpdateStatusPill();
    }

    private void RefreshFileContextStrip(Enhancement? enh, string? path)
    {
        if (TxtEnhName == null) return;
        if (enh == null)
        {
            TxtEnhName.Text = Loc.Get("deeper_player_no_enh");
            TxtEnhMetadata.Text = "";
            TxtEnhPath.IsVisible = false;
            MediaTypeIcon.Text = "🎵";
            MediaTypeIconBg.Background = this.FindResource("DeeperHubAudioBadgeBgBrush") as IBrush ?? Brushes.Gray;
            SourcePill.IsVisible = false;
            BtnOpenInEditor.IsVisible = false;
            return;
        }

        var name = string.IsNullOrEmpty(enh.Metadata?.Name) ? Loc.Get("deeper_player_untitled") : enh.Metadata!.Name;
        TxtEnhName.Text = name;

        var isVideo = string.Equals(enh.MediaType, MediaTypes.Video, StringComparison.OrdinalIgnoreCase);
        MediaTypeIcon.Text = isVideo ? "🎬" : "🎵";
        MediaTypeIconBg.Background = this.FindResource(isVideo ? "DeeperHubVideoBadgeBgBrush" : "DeeperHubAudioBadgeBgBrush") as IBrush ?? Brushes.Gray;

        var creator = enh.Metadata?.Creator;
        var (srcGlyph, srcText) = DescribeMediaSource(enh);
        int regions = enh.Regions?.Count ?? 0;
        int rules = enh.Rules?.Count ?? 0;
        int haptics = 0;
        if (enh.HapticTracks != null)
            foreach (var t in enh.HapticTracks) haptics += t?.Events?.Count ?? 0;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(creator)) parts.Add(creator!);
        if (!string.IsNullOrWhiteSpace(srcText)) parts.Add($"{srcGlyph} {srcText}");
        parts.Add(string.Format(Loc.Get("deeper_player_meta_counts_fmt"), regions, rules, haptics));
        TxtEnhMetadata.Text = string.Join("  ·  ", parts);

        TxtEnhPath.Text = path ?? "";
        TxtEnhPath.IsVisible = false;
        ToolTip.SetTip(TxtEnhName, path ?? "");

        SourcePill.IsVisible = true;
        BtnOpenInEditor.IsVisible = true;
    }

    private static (string Glyph, string Text) DescribeMediaSource(Enhancement enh)
    {
        var src = enh.MediaSource;
        if (string.IsNullOrWhiteSpace(src)) return ("⚠", Loc.Get("deeper_player_source_missing"));
        if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try { return ("🌐", new Uri(src).Host); } catch { return ("🌐", src); }
        }
        if (File.Exists(src)) return ("✓", System.IO.Path.GetFileName(src));
        return ("⚠", System.IO.Path.GetFileName(src));
    }

    private void ShowMediaPaneFor(string? mediaType)
    {
        var isVideo = string.Equals(mediaType, MediaTypes.Video, StringComparison.OrdinalIgnoreCase);
        _isVideoMode = isVideo;
        AudioFileRow.IsVisible = !isVideo;
        AudioPane.IsVisible = !isVideo;
        VideoPane.IsVisible = isVideo;
        VolumePanel.IsVisible = !isVideo;
        BtnPictureInPicture.IsVisible = isVideo;
        if (isVideo) TxtVideoStatus.IsVisible = true;
    }

    // ========================================================================
    // Audio / video loading
    // ========================================================================

    private async void LoadAudio(string path)
    {
        if (_loadInProgress) return;
        _loadInProgress = true;
        try
        {
            StopInternal();
            _lastMediaPath = path;
            TxtAudioPath.Text = path;
            TxtStatus.Text = Loc.Get("deeper_player_status_loading_audio");

            await PlayMediaAsync(path);
            TxtTotal.Text = FormatTime(_mediaPlayer.Length / 1000.0);
            BtnPlayPause.Content = "⏸";
            TxtStatus.Text = Loc.Get("deeper_player_status_playing");
            _ = LoadWaveformAsync(path);
            BindEngineIfReady();
        }
        catch (Exception ex)
        {
            TxtStatus.Text = Loc.Get("deeper_player_status_audio_failed");
            IngestErrorLine(string.Format(Loc.Get("deeper_player_error_audio_load_fmt"), ex.Message));
        }
        finally
        {
            _loadInProgress = false;
        }
    }

    private async Task LoadVideoAsync(string path)
    {
        try
        {
            StopInternal();
            _lastMediaPath = path;
            ShowMediaPaneFor(MediaTypes.Video);
            TxtVideoStatus.Text = Loc.Get("deeper_player_video_loading");
            TxtVideoStatus.IsVisible = true;

            await PlayMediaAsync(path);
            TxtVideoStatus.IsVisible = false;
            BtnPlayPause.Content = "⏸";
            TxtStatus.Text = Loc.Get("deeper_player_status_playing");
            BindEngineIfReady();
        }
        catch (Exception ex)
        {
            TxtVideoStatus.Text = Loc.Get("deeper_player_video_no_video");
            IngestErrorLine(string.Format(Loc.Get("deeper_player_error_video_load_fmt"), ex.Message));
        }
    }

    private async Task PlayMediaAsync(string path)
    {
        await Task.Yield();
        StopInternal();
        _currentMedia = new Media(_libVlc, path);
        _mediaPlayer.Play(_currentMedia);
    }

    private void StopInternal()
    {
        _mediaPlayer.Stop();
        _currentMedia?.Dispose();
        _currentMedia = null;
    }

    // ========================================================================
    // Transport
    // ========================================================================

    private void BtnPlayPause_Click(object? sender, RoutedEventArgs e)
    {
        if (_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Pause();
            BtnPlayPause.Content = "▶";
        }
        else if (_mediaPlayer.CanPause && _mediaPlayer.Time > 0)
        {
            _mediaPlayer.Play();
            BtnPlayPause.Content = "⏸";
        }
        else if (!string.IsNullOrEmpty(_lastMediaPath))
        {
            if (_isVideoMode) _ = LoadVideoAsync(_lastMediaPath);
            else LoadAudio(_lastMediaPath);
        }
        else
        {
            TxtStatus.Text = Loc.Get("deeper_player_status_pick_first");
        }
    }

    private void BtnStop_Click(object? sender, RoutedEventArgs e)
    {
        StopInternal();
        BtnPlayPause.Content = "▶";
        TxtCurrent.Text = "0:00";
        UpdatePlayhead(0);
        TxtStatus.Text = Loc.Get("deeper_player_status_stopped");
        UnbindEngineIfRunning();
    }

    private void SliderVolume_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressVolumeSync) return;
        _mediaPlayer.Volume = Math.Clamp((int)e.NewValue, 0, 100);
    }

    private void UpdateVolumeFromPlayer()
    {
        try
        {
            _suppressVolumeSync = true;
            SliderVolume.Value = Math.Clamp(_mediaPlayer.Volume, 0, 100);
        }
        finally { _suppressVolumeSync = false; }
    }

    // ========================================================================
    // UI tick
    // ========================================================================

    private void UiTimer_Tick(object? sender, EventArgs e)
    {
        if (_isScrubbing) return;

        var ms = _mediaPlayer.Time;
        var len = _mediaPlayer.Length;
        TxtCurrent.Text = FormatTime(ms / 1000.0);
        TxtTotal.Text = FormatTime(len / 1000.0);
        BtnPlayPause.Content = _mediaPlayer.IsPlaying ? "⏸" : "▶";

        UpdatePlayhead(len > 0 ? (double)ms / len : 0);
        UpdateMiniPlayheadX();
        UpdateMiniTimelineReadout();
        RefreshNowRegionOverlay();
        UpdateStatusPill();
    }

    private void UpdateMiniTimelineReadout()
    {
        try
        {
            if (TxtMiniTimelineReadout == null) return;
            double curSec = _mediaPlayer.Time / 1000.0;
            double totalSec = _mediaPlayer.Length / 1000.0;
            if (totalSec <= 0) totalSec = _miniTotalSeconds;
            TxtMiniTimelineReadout.Text = $"{FormatTime(curSec)} / {FormatTime(totalSec)}";
        }
        catch { }
    }

    private void UpdateStatusPill()
    {
        try
        {
            if (StatusPill == null || StatusPillText == null) return;
            if (_loadedEnhancement == null)
            {
                StatusPillText.Text = Loc.Get("deeper_player_pill_empty");
                StatusPillText.Foreground = this.FindResource("TextMutedBrush") as IBrush ?? Brushes.Gray;
                StatusPill.Background = this.FindResource("DeeperAccentTransparent20Brush") as IBrush ?? Brushes.Transparent;
                StatusPill.BorderBrush = this.FindResource("DeeperAccentTransparent40Brush") as IBrush ?? Brushes.Transparent;
                return;
            }

            bool isPlaying = _mediaPlayer.IsPlaying;
            if (isPlaying)
            {
                StatusPillText.Text = Loc.Get("deeper_player_pill_live");
                var accent = this.FindResource("DeeperAccentBrush") as IBrush ?? Brushes.Purple;
                StatusPillText.Foreground = Brushes.White;
                StatusPill.Background = accent;
                StatusPill.BorderBrush = accent;
            }
            else
            {
                StatusPillText.Text = Loc.Get("deeper_player_pill_loaded");
                var soft = this.FindResource("DeeperAccentSoftBrush") as IBrush ?? Brushes.LightGray;
                StatusPillText.Foreground = soft;
                StatusPill.Background = this.FindResource("DeeperAccentTransparent20Brush") as IBrush ?? Brushes.Transparent;
                StatusPill.BorderBrush = soft;
            }
        }
        catch { }
    }

    // ========================================================================
    // Waveform render
    // ========================================================================

    private void RenderWaveform()
    {
        if (_peaks == null || _peaks.Length == 0)
        {
            WaveformPath.Data = null;
            return;
        }

        var w = WaveformCanvas.Bounds.Width;
        var h = WaveformCanvas.Bounds.Height;
        if (w <= 0 || h <= 0) return;

        var midY = h / 2.0;
        var amp = (h - 4) / 2.0;
        var geometry = new PathGeometry();
        var figure = new PathFigure { IsClosed = false };
        int samples = Math.Min(_peaks.Length, Math.Max(64, (int)w));
        for (int i = 0; i < samples; i++)
        {
            var x = (double)i / (samples - 1) * w;
            var idx = (int)Math.Round((double)i / (samples - 1) * (_peaks.Length - 1));
            var v = Math.Clamp(_peaks[idx], 0f, 1f);
            figure.Segments?.Add(new LineSegment { Point = new global::Avalonia.Point(x, midY - v * amp) });
            figure.Segments?.Add(new LineSegment { Point = new global::Avalonia.Point(x, midY + v * amp) });
        }
        geometry.Figures?.Add(figure);
        WaveformPath.Data = geometry;
    }

    private void UpdatePlayhead(double frac)
    {
        var w = WaveformCanvas.Bounds.Width;
        var h = WaveformCanvas.Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var x = Math.Clamp(frac, 0, 1) * w;
        PlayheadLine.StartPoint = new global::Avalonia.Point(x, 0);
        PlayheadLine.EndPoint = new global::Avalonia.Point(x, h);
    }

    private void WaveformCanvas_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        RenderWaveform();
    }

    private async Task LoadWaveformAsync(string audioPath)
    {
        if (_waveformCache == null) return;
        try
        {
            var result = await _waveformCache.LoadAsync(audioPath);
            if (result?.Peaks == null || result.Peaks.Length == 0) return;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _peaks = result.Peaks;
                RenderWaveform();
            });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load waveform for {AudioPath}", audioPath);
        }
    }

    private void WaveformCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var w = WaveformCanvas.Bounds.Width;
        var len = _mediaPlayer.Length;
        if (w <= 0 || len <= 0) return;
        var frac = Math.Clamp(e.GetPosition(WaveformCanvas).X / w, 0, 1);
        _mediaPlayer.SeekTo(TimeSpan.FromMilliseconds(frac * len));
        UpdatePlayhead(frac);
    }

    // ========================================================================
    // Mini-timeline
    // ========================================================================

    private void OnEnhancementLoadedForMini(Enhancement? enh)
    {
        _miniEnhancement = enh;
        if (MiniTimelinePanel == null) return;
        MiniTimelinePanel.IsVisible = enh != null;
        RebuildMiniTimeline();
    }

    private void MiniTimelineCanvas_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        RebuildMiniTimeline();
    }

    private void RebuildMiniTimeline()
    {
        try
        {
            if (MiniTimelineCanvas == null) return;
            MiniTimelineCanvas.Children.Clear();
            var enh = _miniEnhancement;
            if (enh == null) return;
            var w = MiniTimelineCanvas.Bounds.Width;
            var h = MiniTimelineCanvas.Bounds.Height;
            if (w <= 0 || h <= 0) return;

            var total = GetEffectiveTimelineTotal(enh);
            if (total <= 0) return;
            _miniTotalSeconds = total;

            if (enh.Regions != null)
            {
                foreach (var r in enh.Regions)
                {
                    if (r == null) continue;
                    double rs = Math.Max(0, r.Start);
                    double re = Math.Max(rs, r.End);
                    if (re <= 0) continue;
                    var x1 = (rs / total) * w;
                    var x2 = (re / total) * w;
                    var bw = Math.Max(2, x2 - x1);
                    var brush = ParseHexBrush(r.Color, "DeeperAccentBrush");
                    var fill = brush is SolidColorBrush sb
                        ? new SolidColorBrush(Color.FromArgb(110, sb.Color.R, sb.Color.G, sb.Color.B))
                        : (this.FindResource("DeeperAccentTransparent40Brush") as IBrush ?? Brushes.Transparent);
                    var rect = new Rectangle
                    {
                        Width = bw,
                        Height = h - 2,
                        Fill = fill,
                        Stroke = brush,
                        StrokeThickness = 1,
                        RadiusX = 2,
                        RadiusY = 2,
                    };
                    ToolTip.SetTip(rect, string.IsNullOrEmpty(r.Label) ? r.Id : r.Label);
                    Canvas.SetLeft(rect, x1);
                    Canvas.SetTop(rect, 1);
                    MiniTimelineCanvas.Children.Add(rect);

                    if (bw >= 40 && !string.IsNullOrEmpty(r.Label))
                    {
                        var tb = new TextBlock
                        {
                            Text = r.Label,
                            Foreground = Brushes.White,
                            FontSize = 9,
                            FontWeight = FontWeight.SemiBold,
                            IsHitTestVisible = false,
                        };
                        Canvas.SetLeft(tb, x1 + 4);
                        Canvas.SetTop(tb, (h - 12) / 2.0);
                        MiniTimelineCanvas.Children.Add(tb);
                    }
                }
            }

            if (enh.Rules != null)
            {
                foreach (var rule in enh.Rules)
                {
                    var trig = rule?.Trigger;
                    if (trig is not TimeReachedTrigger tr) continue;
                    var t = Math.Max(0, tr.Time);
                    if (t > total) continue;
                    var x = (t / total) * w;
                    var line = new Line
                    {
                        StartPoint = new global::Avalonia.Point(x, 1),
                        EndPoint = new global::Avalonia.Point(x, h - 1),
                        Stroke = Brushes.Orange,
                        StrokeThickness = 1.5,
                        StrokeDashArray = new AvaloniaList<double> { 2, 2 },
                        IsHitTestVisible = false,
                    };
                    MiniTimelineCanvas.Children.Add(line);
                    var flag = new Polygon
                    {
                        Points = new Points
                        {
                            new global::Avalonia.Point(x - 3, 1),
                            new global::Avalonia.Point(x + 3, 1),
                            new global::Avalonia.Point(x, 5),
                        },
                        Fill = Brushes.Orange,
                        IsHitTestVisible = false,
                    };
                    MiniTimelineCanvas.Children.Add(flag);
                }
            }

            var ph = new Line
            {
                StartPoint = new global::Avalonia.Point(0, 0),
                EndPoint = new global::Avalonia.Point(0, h),
                Stroke = this.FindResource("DeeperAccentBrush") as IBrush ?? Brushes.Purple,
                StrokeThickness = 2,
                IsHitTestVisible = false,
                Tag = "playhead",
            };
            MiniTimelineCanvas.Children.Add(ph);
            UpdateMiniPlayheadX();
        }
        catch (Exception ex)
        {
            IngestDiagnosticLine(string.Format(Loc.Get("deeper_player_error_mini_timeline_fmt"), ex.Message));
        }
    }

    private void UpdateMiniPlayheadX()
    {
        if (MiniTimelineCanvas == null || _miniEnhancement == null) return;
        var w = MiniTimelineCanvas.Bounds.Width;
        if (w <= 0) return;

        var effective = GetEffectiveTimelineTotal(_miniEnhancement);
        if (effective <= 0) return;
        if (_miniTotalSeconds <= 0
            || Math.Abs(effective - _miniTotalSeconds) / Math.Max(effective, _miniTotalSeconds) > 0.01)
        {
            _miniTotalSeconds = effective;
            RebuildMiniTimeline();
            return;
        }

        double currentSec = _mediaPlayer.Time / 1000.0;
        var x = Math.Clamp(currentSec / _miniTotalSeconds, 0, 1) * w;

        foreach (var child in MiniTimelineCanvas.Children)
        {
            if (child is Line l && (l.Tag as string) == "playhead")
            {
                l.StartPoint = new global::Avalonia.Point(x, l.StartPoint.Y);
                l.EndPoint = new global::Avalonia.Point(x, l.EndPoint.Y);
                break;
            }
        }
    }

    private double GetEffectiveTimelineTotal(Enhancement enh)
    {
        double media = _mediaPlayer.Length / 1000.0;
        var content = ComputeMiniTotalSeconds(enh);
        return Math.Max(media, content);
    }

    private static double ComputeMiniTotalSeconds(Enhancement enh)
    {
        double max = 0;
        if (enh.Regions != null)
            foreach (var r in enh.Regions)
                if (r != null) max = Math.Max(max, r.End);
        if (enh.Rules != null)
            foreach (var rule in enh.Rules)
                if (rule?.Trigger is TimeReachedTrigger tr) max = Math.Max(max, tr.Time);
        if (enh.HapticTracks != null)
            foreach (var t in enh.HapticTracks)
                if (t?.Events != null)
                    foreach (var ev in t.Events)
                        if (ev != null) max = Math.Max(max, ev.Start + ev.Duration);
        return max > 0 ? max : 60.0;
    }

    private void MiniTimelineCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(MiniTimelineCanvas).Properties.IsLeftButtonPressed) return;
        if (_miniEnhancement == null) return;
        var w = MiniTimelineCanvas.Bounds.Width;
        if (w <= 0 || _miniTotalSeconds <= 0) return;

        _isScrubbing = true;
        e.Pointer.Capture(MiniTimelineCanvas);
        SeekToMiniPosition(e.GetPosition(MiniTimelineCanvas).X);
        e.Handled = true;
    }

    private void MiniTimelineCanvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isScrubbing) return;
        SeekToMiniPosition(e.GetPosition(MiniTimelineCanvas).X);
    }

    private void MiniTimelineCanvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isScrubbing) return;
        _isScrubbing = false;
        e.Pointer.Capture(null);
        SeekToMiniPosition(e.GetPosition(MiniTimelineCanvas).X);
    }

    private void MiniTimelineCanvas_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isScrubbing = false;
    }

    private void SeekToMiniPosition(double xInCanvas)
    {
        var w = MiniTimelineCanvas.Bounds.Width;
        if (w <= 0 || _miniTotalSeconds <= 0) return;
        var frac = Math.Clamp(xInCanvas / w, 0, 1);
        var targetSec = frac * _miniTotalSeconds;
        var len = _mediaPlayer.Length;
        if (len > 0)
        {
            var mediaFrac = Math.Clamp(targetSec / (len / 1000.0), 0, 1);
            _mediaPlayer.SeekTo(TimeSpan.FromMilliseconds(mediaFrac * len));
        }
        UpdateMiniPlayheadX();
    }

    // ========================================================================
    // Now region overlay
    // ========================================================================

    private void RefreshNowRegionOverlay()
    {
        try
        {
            if (NowRegionPanel == null) return;
            var enh = _miniEnhancement;
            if (enh?.Regions == null || enh.Regions.Count == 0)
            {
                NowRegionPanel.IsVisible = false;
                return;
            }
            double currentSec = _mediaPlayer.Time / 1000.0;
            Region? hit = null;
            foreach (var r in enh.Regions)
            {
                if (r == null) continue;
                if (currentSec >= r.Start && currentSec <= r.End) { hit = r; break; }
            }
            if (hit == null)
            {
                NowRegionPanel.IsVisible = false;
                return;
            }
            TxtNowRegion.Text = string.IsNullOrEmpty(hit.Label) ? hit.Id : hit.Label;
            NowRegionSwatch.Fill = ParseHexBrush(hit.Color, "DeeperAccentBrush");
            NowRegionPanel.IsVisible = true;
        }
        catch { }
    }

    // ========================================================================
    // Event log
    // ========================================================================

    private void InitializeEventLog()
    {
        LstEvents.ItemsSource = _filteredEntries;
        UpdateFilterCounts();
    }

    private void IngestActionLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        AppendLogEntry(new EventLogEntry
        {
            Category = EventLogCategory.Action,
            Description = line,
        });
    }

    private void IngestDiagnosticLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        var lower = line.ToLowerInvariant();
        var cat = (lower.Contains("error") || lower.Contains("fail") || lower.Contains("rejected"))
            ? EventLogCategory.Error
            : EventLogCategory.Engine;
        AppendLogEntry(new EventLogEntry { Category = cat, Description = line });
    }

    private void IngestErrorLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        AppendLogEntry(new EventLogEntry { Category = EventLogCategory.Error, Description = line });
    }

    private void AppendLogEntry(EventLogEntry entry)
    {
        _logEntries.Insert(0, entry);
        while (_logEntries.Count > 30)
            _logEntries.RemoveAt(_logEntries.Count - 1);
        ApplyFilter();
        UpdateFilterCounts();
    }

    private void ApplyFilter()
    {
        _filteredEntries.Clear();
        foreach (var e in _logEntries)
        {
            bool include = _activeFilter switch
            {
                "action" => e.Category == EventLogCategory.Action,
                "engine" => e.Category == EventLogCategory.Engine,
                "error" => e.Category == EventLogCategory.Error,
                _ => true,
            };
            if (include) _filteredEntries.Add(e);
        }
        LstEvents.ItemsSource = null;
        LstEvents.ItemsSource = _filteredEntries;
    }

    private void UpdateFilterCounts()
    {
        try
        {
            int all = _logEntries.Count;
            int action = 0, engine = 0, error = 0;
            foreach (var e in _logEntries)
            {
                switch (e.Category)
                {
                    case EventLogCategory.Action: action++; break;
                    case EventLogCategory.Engine: engine++; break;
                    case EventLogCategory.Error: error++; break;
                }
            }
            if (TxtFilterCountAll != null) TxtFilterCountAll.Text = all.ToString();
            if (TxtFilterCountActions != null) TxtFilterCountActions.Text = action.ToString();
            if (TxtFilterCountEngine != null) TxtFilterCountEngine.Text = engine.ToString();
            if (TxtFilterCountErrors != null) TxtFilterCountErrors.Text = error.ToString();
        }
        catch { }
    }

    private void EventFilterPill_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton clicked) return;
        if (clicked.IsChecked != true) { clicked.IsChecked = true; return; }
        _activeFilter = (clicked.Tag as string ?? "all").ToLowerInvariant();
        foreach (var pill in new[] { PillFilterAll, PillFilterActions, PillFilterEngine, PillFilterErrors })
        {
            if (pill == null || ReferenceEquals(pill, clicked)) continue;
            pill.IsChecked = false;
        }
        ApplyFilter();
    }

    private void BtnClearEvents_Click(object? sender, RoutedEventArgs e)
    {
        _logEntries.Clear();
        ApplyFilter();
        UpdateFilterCounts();
    }

    private void BtnCollapseEventLog_Click(object? sender, RoutedEventArgs e)
    {
        if (EventScroll == null) return;
        _eventLogCollapsed = !_eventLogCollapsed;
        if (_eventLogCollapsed)
        {
            _eventLogExpandedHeight = EventScroll.MaxHeight > 0 ? EventScroll.MaxHeight : 140;
            EventScroll.MaxHeight = 0;
            EventScroll.IsVisible = false;
            BtnCollapseEventLog.Content = "▴";
        }
        else
        {
            EventScroll.MaxHeight = _eventLogExpandedHeight;
            EventScroll.IsVisible = true;
            BtnCollapseEventLog.Content = "▾";
        }
    }

    private void EventOpenInEditor_Click(object? sender, RoutedEventArgs e)
    {
        OpenLoadedEnhancementInEditor();
    }

    // ========================================================================
    // File picker / popover handlers
    // ========================================================================

    private void BtnChange_Click(object? sender, RoutedEventArgs e)
    {
        if (ChangePopup == null) return;
        ChangePopup.IsOpen = !ChangePopup.IsOpen;
    }

    private async void BtnPickAudio_Click(object? sender, RoutedEventArgs e)
    {
        ChangePopup.IsOpen = false;
        var filters = new[]
        {
            new FileFilter(Loc.Get("deeper_filter_media"), new[] { "mp3", "wav", "m4a", "aac", "flac", "ogg", "mp4", "webm", "mkv", "mov", "avi", "m4v" }),
            new FileFilter(Loc.Get("deeper_editor_media_type_audio"), new[] { "mp3", "wav", "m4a", "aac", "flac", "ogg" }),
            new FileFilter(Loc.Get("deeper_editor_media_type_video"), new[] { "mp4", "webm", "mkv", "mov", "avi", "m4v" }),
        };
        var files = await _dialogService.ShowOpenFileDialogAsync(Loc.Get("deeper_player_pick_media"), filters);
        var path = files.FirstOrDefault();
        if (string.IsNullOrEmpty(path)) return;

        if (IsLocalVideoFile(path)) _ = LoadVideoAsync(path);
        else LoadAudio(path);
    }

    private async void BtnPickEnhancement_Click(object? sender, RoutedEventArgs e)
    {
        ChangePopup.IsOpen = false;
        var filters = new[] { new FileFilter(Loc.Get("deeper_filter_deeper_enhancement"), new[] { "ccpenh.json" }) };
        var files = await _dialogService.ShowOpenFileDialogAsync(Loc.Get("deeper_player_pick_enh"), filters);
        var path = files.FirstOrDefault();
        if (!string.IsNullOrEmpty(path)) _ = LoadEnhancementFromFileAsync(path);
    }

    private void BtnUnloadEnhancement_Click(object? sender, RoutedEventArgs e)
    {
        ChangePopup.IsOpen = false;
        UnloadEnhancement();
    }

    private async void BtnLoadUrl_Click(object? sender, RoutedEventArgs e)
    {
        ChangePopup.IsOpen = false;
        var dialog = new UrlPromptDialog();
        var result = await dialog.ShowDialog<bool?>(this);
        if (result != true || string.IsNullOrWhiteSpace(dialog.Result)) return;

        await FetchEnhancementFromUrlAsync(dialog.Result);
    }

    private static readonly HttpClient _httpClient = new();

    private async Task FetchEnhancementFromUrlAsync(string url)
    {
        try
        {
            TxtStatus.Text = Loc.Get("deeper_player_status_loading_url");
            using var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var enh = EnhancementSerializer.Load(json);
            var issues = EnhancementValidator.Validate(enh);
            var firstError = issues.FirstOrDefault(i => i.Severity == ValidationSeverity.Error);
            if (firstError != null)
            {
                IngestErrorLine(string.Format(Loc.Get("deeper_player_error_validation_fmt"), firstError.Message));
                return;
            }
            LoadEnhancement(enh, url);
        }
        catch (Exception ex)
        {
            IngestErrorLine(string.Format(Loc.Get("deeper_player_error_fetch_url_fmt"), ex.Message));
        }
    }

    private void BtnCreateNewEnhancement_Click(object? sender, RoutedEventArgs e)
    {
        ChangePopup.IsOpen = false;
        try
        {
            var editor = new DeeperEditorWindow(new Enhancement(), null);
            editor.Show();
        }
        catch (Exception ex)
        {
            IngestErrorLine(string.Format(Loc.Get("deeper_editor_preview_open_failed_fmt"), ex.Message));
        }
    }

    private void BtnOpenInEditor_Click(object? sender, RoutedEventArgs e)
    {
        OpenLoadedEnhancementInEditor();
    }

    private void OpenLoadedEnhancementInEditor()
    {
        if (_loadedEnhancement == null) return;
        try
        {
            var editor = new DeeperEditorWindow(_loadedEnhancement, _loadedFilePath);
            editor.Show();
        }
        catch (Exception ex)
        {
            IngestErrorLine(string.Format(Loc.Get("deeper_editor_preview_open_failed_fmt"), ex.Message));
        }
    }

    private void BtnZoomIn_Click(object? sender, RoutedEventArgs e)
    {
        // LibVLC video view does not expose page zoom; no-op in Avalonia/LibVLC.
    }

    private void BtnZoomOut_Click(object? sender, RoutedEventArgs e)
    {
        // LibVLC video view does not expose page zoom; no-op in Avalonia/LibVLC.
    }

    private void BtnPictureInPicture_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: PiP support for LibVLC if/when a cross-platform surface is available.
        IngestDiagnosticLine(Loc.Get("deeper_player_diag_pip_stub"));
    }

    private void BtnEyeTracking_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: wire to Avalonia webcam/eye-tracking service once ported.
        IngestDiagnosticLine(Loc.Get("deeper_player_diag_eye_tracking_stub"));
    }

    // ========================================================================
    // Engine-host binding
    // ========================================================================

    private void BindEngineIfReady()
    {
        if (_loadedEnhancement == null) return;
        if (_host.IsRunning) return;
        if (_timeSource != null) return;

        _timeSource = new AvaloniaLibVlcTimeSource(_mediaPlayer, VideoView);
        _host.Bind(_timeSource, attach: () => _timeSource.StartTicking(), detach: () => _timeSource.StopTicking());
    }

    private void UnbindEngineIfRunning()
    {
        _host.UnbindEngine();
        DisposeTimeSource();
    }

    private void DisposeTimeSource()
    {
        if (_timeSource == null) return;
        _timeSource.Dispose();
        _timeSource = null;
    }

    private void OnHostActionLogged(string line) => IngestActionLine(line);

    private void OnHostDiagnostic(string line) => IngestDiagnosticLine(line);

    // ========================================================================
    // Drag & drop
    // ========================================================================

    private void Window_DragOver(object? sender, DragEventArgs e)
    {
        try
        {
            e.DragEffects = DragDropEffects.None;
            if (e.DataTransfer.Formats.Contains(DataFormat.File)
                && e.DataTransfer.TryGetFiles() is IEnumerable<IStorageItem> files
                && files.Any(f => IsDroppablePlayerPath(f.Path.LocalPath)))
            {
                e.DragEffects = DragDropEffects.Copy;
            }
        }
        catch { }
        e.Handled = true;
    }

    private void Window_Drop(object? sender, DragEventArgs e)
    {
        try
        {
            if (!e.DataTransfer.Formats.Contains(DataFormat.File)) return;
            var files = e.DataTransfer.TryGetFiles()?.Select(f => f.Path.LocalPath).ToArray();
            if (files == null || files.Length == 0) return;
            e.Handled = true;

            var enhPath = files.FirstOrDefault(IsEnhancementJsonPath);
            if (!string.IsNullOrEmpty(enhPath))
            {
                _ = LoadEnhancementFromFileAsync(enhPath);
                return;
            }
            var mediaPath = files.FirstOrDefault(IsLocalMediaFile);
            if (!string.IsNullOrEmpty(mediaPath))
            {
                OpenLocalMediaFile(mediaPath);
            }
        }
        catch (Exception ex)
        {
            IngestErrorLine(string.Format(Loc.Get("deeper_player_error_drop_fmt"), ex.Message));
        }
    }

    private static bool IsDroppablePlayerPath(string path)
        => IsLocalMediaFile(path) || IsEnhancementJsonPath(path);

    private static bool IsLocalMediaFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext is ".mp3" or ".wav" or ".m4a" or ".aac" or ".flac" or ".ogg"
                   or ".mp4" or ".webm" or ".mkv" or ".mov" or ".avi" or ".m4v";
    }

    private static bool IsLocalVideoFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext is ".mp4" or ".webm" or ".mkv" or ".mov" or ".avi" or ".m4v";
    }

    private static bool IsEnhancementJsonPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        return path.EndsWith(".ccpenh.json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRemoteVideoUrl(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return false;
        return source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    // ========================================================================
    // Cleanup
    // ========================================================================

    private void Window_Closing(object? sender, WindowClosingEventArgs e)
    {
        try { _uiTimer.Stop(); } catch { }
        try { _uiTimer.Tick -= UiTimer_Tick; } catch { }
        try { UnbindEngineIfRunning(); } catch { }
        try { _host.ActionLogged -= OnHostActionLogged; } catch { }
        try { _host.Diagnostic -= OnHostDiagnostic; } catch { }
        try { StopInternal(); } catch { }
        try { _mediaPlayer.Dispose(); } catch { }
    }

    // ========================================================================
    // Helpers
    // ========================================================================

    private static string FormatTime(double seconds)
    {
        if (seconds < 0 || double.IsNaN(seconds)) seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}"
            : $"{ts.Minutes}:{ts.Seconds:00}";
    }

    private static IBrush ParseHexBrush(string? hex, string fallbackResourceKey)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex))
            {
                var c = Color.Parse(hex);
                return new SolidColorBrush(c);
            }
        }
        catch { }
        try
        {
            if (Application.Current?.Resources[fallbackResourceKey] is IBrush b) return b;
        }
        catch { }
        return Brushes.Gray;
    }

    // ========================================================================
    // Event log model
    // ========================================================================

    public enum EventLogCategory { Action, Engine, Error }

    public sealed class EventLogEntry
    {
        public DateTime Timestamp { get; init; } = DateTime.Now;
        public EventLogCategory Category { get; init; }
        public string Description { get; init; } = "";
        public string? RuleId { get; init; }
        public string? RuleLabel { get; init; }

        public string TimestampDisplay => Timestamp.ToString("HH:mm:ss");

        public string IconGlyph => Category switch
        {
            EventLogCategory.Action => "⚡",
            EventLogCategory.Engine => "⚙",
            EventLogCategory.Error => "⚠",
            _ => "•",
        };

        public IBrush IconBrush => Category switch
        {
            EventLogCategory.Action => Application.Current?.Resources["DeeperAccentBrush"] as IBrush ?? Brushes.Purple,
            EventLogCategory.Engine => Application.Current?.Resources["TextMutedBrush"] as IBrush ?? Brushes.Gray,
            EventLogCategory.Error => Application.Current?.Resources["DangerBrush"] as IBrush ?? Brushes.Red,
            _ => Brushes.Gray,
        };

        public IBrush RowBgBrush => Category switch
        {
            EventLogCategory.Engine => Application.Current?.Resources["DeeperLaneHeaderBrush"] as IBrush ?? Brushes.Transparent,
            EventLogCategory.Error => Application.Current?.Resources["DeeperLaneHeaderBrush"] as IBrush ?? Brushes.Transparent,
            _ => Brushes.Transparent,
        };

        public IBrush RuleLabelBrush => Application.Current?.Resources["DeeperAccentSoftBrush"] as IBrush ?? Brushes.LightGray;

        public bool OpenInEditorVisible => !string.IsNullOrEmpty(RuleId);
    }
}
