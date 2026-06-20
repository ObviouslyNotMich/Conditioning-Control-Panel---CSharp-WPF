using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.ViewModels;
using ConditioningControlPanel.Core.Services;
using ShapesPath = Avalonia.Controls.Shapes.Path;

using Animation = global::Avalonia.Animation.Animation;
using IterationCount = global::Avalonia.Animation.IterationCount;
using KeyFrame = global::Avalonia.Animation.KeyFrame;
using PlaybackDirection = global::Avalonia.Animation.PlaybackDirection;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Controls;

/// <summary>
/// Animated, fully data-bound Season Recap card (Avalonia port).
///
/// Ambient loops: foil-edge shimmer, slow-rotating hypno spiral, holographic sweep.
/// On reveal the two time figures and the rank count up. All of it can be frozen
/// to a clean representative frame via <see cref="PrepareForStill"/> before PNG export.
/// </summary>
public partial class SeasonRecapCard : UserControl
{
    private readonly global::ConditioningControlPanel.IAppLogger _logger;


    private SeasonRecapViewModel? _vm;
    private DispatcherTimer? _countTimer;

    private CancellationTokenSource? _spiralCts;
    private CancellationTokenSource? _foilCts;
    private CancellationTokenSource? _holoCts;

    private readonly RotateTransform _spiralRotate = new() { Angle = 0 };
    private readonly TranslateTransform _holoTranslate = new() { X = -260, Y = -200 };
    private GradientStop _foil1 = null!;
    private GradientStop _foil2 = null!;

    private const double StillSpiralAngle = 24;

    public bool AnimateReveal { get; set; } = true;

    public SeasonRecapCard()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        SpiralCanvas.RenderTransform = _spiralRotate;
        Holo.RenderTransform = _holoTranslate;

        if (OuterBorder.Background is LinearGradientBrush foilBrush && foilBrush.GradientStops.Count >= 4)
        {
            _foil1 = foilBrush.GradientStops[1];
            _foil2 = foilBrush.GradientStops[2];
        }
        else
        {
            // Fallbacks so the animation targets are never null.
            _foil1 = new GradientStop { Offset = 0.35 };
            _foil2 = new GradientStop { Offset = 0.65 };
        }
    }

    public void SetViewModel(SeasonRecapViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        SetFinalFigures();
        LoadBackgroundImage(vm.BackgroundImagePath);
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        BuildSpiral();
        StartAmbientLoops();

        if (AnimateReveal) RunCountUps();
        else SetFinalFigures();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        StopAmbientLoops();
        _countTimer?.Stop();
    }

    private void LoadBackgroundImage(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            var bitmap = LoadBitmapFromUri(path);
            if (bitmap != null)
                BackgroundImage.Source = bitmap;
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "SeasonRecapCard: failed to load background {Path}", path);
        }
    }

    private static Bitmap? LoadBitmapFromUri(string uriOrPath)
    {
        if (uriOrPath.StartsWith("pack://application:,,,/", StringComparison.Ordinal))
        {
            var relative = uriOrPath.Substring("pack://application:,,,/".Length);
            var avares = $"avares://CCP.Avalonia/Assets/{relative}";
            try
            {
                using var stream = AssetLoader.Open(new Uri(avares));
return new Bitmap(stream);
            }
            catch { /* fall through */ }
        }

        if (uriOrPath.StartsWith("file://", StringComparison.Ordinal))
        {
            var path = uriOrPath.Substring(7);
            if (File.Exists(path))
                return new Bitmap(path);
        }

        if (File.Exists(uriOrPath))
            return new Bitmap(uriOrPath);

        return null;
    }

    // ---------- spiral geometry ----------
    private void BuildSpiral()
    {
        if (SpiralCanvas.Children.Count > 0) return;
        double cx = 260, cy = 260, b = 7.4, step = 0.18, turns = 15.5 * Math.PI;

        SpiralCanvas.Children.Add(MakeArm(cx, cy, b, step, turns, 0,
            Color.FromArgb(0x66, 0xB1, 0x8C, 0xFF), 3.2));
        SpiralCanvas.Children.Add(MakeArm(cx, cy, b, step, turns, Math.PI,
            Color.FromArgb(0x38, 0xE8, 0x4C, 0xF2), 2.2));
    }

    private static ShapesPath MakeArm(double cx, double cy, double b, double step, double turns,
        double offset, Color color, double thickness)
    {
        var fig = new PathFigure { IsClosed = false };
        bool first = true;
        for (double t = 0; t <= turns; t += step)
        {
            double r = 3 + b * t;
            double x = cx + r * Math.Cos(t + offset);
            double y = cy + r * Math.Sin(t + offset);
            var pt = new Point(x, y);
            if (first) { fig.StartPoint = pt; first = false; }
            else fig.Segments?.Add(new LineSegment { Point = pt });
        }
        var geo = new PathGeometry();
        geo.Figures?.Add(fig);

        return new ShapesPath
        {
            Data = geo,
            Stroke = new SolidColorBrush(color),
            StrokeThickness = thickness,
        };
    }

    // ---------- ambient loops ----------
    private void StartAmbientLoops()
    {
        StopAmbientLoops();

        _spiralCts = new CancellationTokenSource();
        var spin = new Animation
        {
            Duration = TimeSpan.FromSeconds(26),
            IterationCount = IterationCount.Infinite,
            Children =
            {
                new KeyFrame { Setters = { new Setter(RotateTransform.AngleProperty, 0.0) }, KeyTime = TimeSpan.Zero },
                new KeyFrame { Setters = { new Setter(RotateTransform.AngleProperty, 360.0) }, KeyTime = TimeSpan.FromSeconds(26) }
            }
        };
        _ = spin.RunAsync(_spiralRotate, _spiralCts.Token);

        // Avalonia GradientStop is not Animatable, so foil shimmer is handled
        // by preparing a static representative frame in PrepareForStill().

        _holoCts = new CancellationTokenSource();
        var holo = new Animation
        {
            Duration = TimeSpan.FromSeconds(6),
            IterationCount = IterationCount.Infinite,
            PlaybackDirection = PlaybackDirection.Alternate,
            Easing = new SineEaseInOut(),
            Children =
            {
                new KeyFrame
                {
                    Setters =
                    {
                        new Setter(TranslateTransform.XProperty, -200.0),
                        new Setter(TranslateTransform.YProperty, -200.0)
                    },
                    KeyTime = TimeSpan.Zero
                },
                new KeyFrame
                {
                    Setters =
                    {
                        new Setter(TranslateTransform.XProperty, 200.0),
                        new Setter(TranslateTransform.YProperty, 200.0)
                    },
                    KeyTime = TimeSpan.FromSeconds(6)
                }
            }
        };
        _ = holo.RunAsync(_holoTranslate, _holoCts.Token);
    }

    private void StopAmbientLoops()
    {
        _spiralCts?.Cancel();
        _spiralCts?.Dispose();
        _spiralCts = null;
        _foilCts?.Cancel();
        _foilCts?.Dispose();
        _foilCts = null;
        _holoCts?.Cancel();
        _holoCts?.Dispose();
        _holoCts = null;
    }

    // ---------- count-ups ----------
    private void RunCountUps()
    {
        if (_vm == null) { SetFinalFigures(); return; }

        var start = DateTime.UtcNow;
        var dur = TimeSpan.FromMilliseconds(950);

        _countTimer?.Stop();
        _countTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _countTimer.Tick += (s, e) =>
        {
            var elapsed = DateTime.UtcNow - start;
            double p = Math.Min(1.0, elapsed.TotalMilliseconds / dur.TotalMilliseconds);
            double eased =
1 - Math.Pow(1 - p, 3);

            HeroSeasonTime.Text = SeasonRecapViewModel.FormatHm(_vm.SeasonMinutes * eased);
            HeroAllTime.Text = SeasonRecapViewModel.FormatHm(_vm.AllTimeMinutes * eased);
            StatRank.Text = _vm.PeakRankTarget > 0
                ? "#" + Math.Max(1, (int)Math.Round(_vm.PeakRankTarget * eased))
                : _vm.PeakRankText;

            if (p >= 1.0)
            {
                _countTimer?.Stop();
                SetFinalFigures();
            }
        };
        HeroSeasonTime.Text = SeasonRecapViewModel.FormatHm(0);
        HeroAllTime.Text = SeasonRecapViewModel.FormatHm(0);
        StatRank.Text = _vm.PeakRankTarget > 0 ? "#0" : _vm.PeakRankText;
        _countTimer.Start();
    }

    private void SetFinalFigures()
    {
        if (_vm == null) return;
        HeroSeasonTime.Text = _vm.SeasonTimeText;
        HeroAllTime.Text = _vm.AllTimeText;
        StatRank.Text = _vm.PeakRankText;
    }

    /// <summary>
    /// Freeze every animation to a clean representative frame and set the figures to their
    /// final values. Call this immediately before rendering the card to PNG so the still is
    /// never captured mid-sweep or mid-count-up.
    /// </summary>
    public void PrepareForStill()
    {
        _countTimer?.Stop();
        SetFinalFigures();

        StopAmbientLoops();

        _spiralRotate.Angle = StillSpiralAngle;
        _holoTranslate.X = 0;
        _holoTranslate.Y = 0;
        _foil1.Offset = 0.35;
        _foil2.Offset = 0.65;

        // Force a layout pass so the frozen values are applied.
        InvalidateMeasure();
        InvalidateArrange();
    }
}
