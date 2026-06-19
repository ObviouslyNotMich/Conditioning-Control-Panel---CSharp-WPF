using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ConditioningControlPanel;

/// <summary>
/// The Porn DVD active skill AND the Intrusive Thoughts accessory share this window: a
/// DVD-screensaver text logo that drifts across the primary screen, bouncing off the edges
/// (hue shift on every bounce, as tradition demands) and popping every chaos bubble it flies
/// through — treats pop with their payloads, live ones snap. One small window per logo,
/// geometry-stroked text via <see cref="OutlinedText"/>, vsync-driven motion.
/// Intrusive Thoughts spawns these with a custom phrase ("thoughts", capped at
/// <see cref="MAX_THOUGHTS"/> alive); its capstone lets a thought that brushes a white
/// rabbit SPLIT (self gets +2s and a diverged clone; clones can split again).
/// The Spanker's capstone makes every logo clickable: a smack turns it (window stays
/// NOACTIVATE, so no focus steal).
///
/// Windows are POOLED: a retired logo hides and gets recycled by the next Launch — with
/// Intrusive Thoughts equipped a fresh logo spawns (and dies) every ~5s for the whole run,
/// and per-logo layered-window create/close churn mid-run can wedge the shared WPF render
/// thread (Application Hang 1002). Real closes happen only at <see cref="CloseActive"/>
/// (run teardown), which also drains the pool.
/// </summary>
public sealed class ChaosDvdOverlay : Window
{
    private const double BASE_FONT = 46;
    private const double BASE_SPEED = 230;     // DIPs per second before the rank speed multiplier
    private const double PEAK_OPAC = 0.85;
    private const int TICK_MS = 33;
    private const int MAX_THOUGHTS = 8;        // Intrusive Thoughts alive cap (splits included)
    private const int POOL_MAX = 12;           // recycled-window cap (actives + pool ≤ thoughts cap + toys)

    // The classic logo palette, advanced one step per bounce.
    private static readonly Color[] Hues =
    {
        Color.FromRgb(0xFF, 0x4D, 0xC4), Color.FromRgb(0x7A, 0xE0, 0xFF), Color.FromRgb(0xFF, 0xD7, 0x00),
        Color.FromRgb(0x9C, 0xE8, 0xA0), Color.FromRgb(0xD2, 0x4D, 0xFF), Color.FromRgb(0xFF, 0x8A, 0x5C),
    };

    private static readonly List<ChaosDvdOverlay> _active = new();
    private static readonly Stack<ChaosDvdOverlay> _pool = new();
    private static readonly Random _rng = new();

    /// <summary>The Spanker capstone gate, installed by ChaosModeService at run start and
    /// cleared at teardown — sampled when each logo spawns.</summary>
    public static Func<bool>? SpankerRedirect;

    private readonly OutlinedText _label;
    private readonly Grid _host;
    private bool _isThought;                   // Intrusive Thoughts instance (counts toward the cap)
    private bool _splitOnRabbit;               // capstone: brushes a rabbit → splits
    private bool _clickable;                   // Spanker capstone smack-to-turn (per launch)
    private double _fontScale = 1.0;
    private bool _splitSpent;                  // each instance splits at most once
    private int _splitBouncesLeft;             // Casting Couch: bounce-splits left (2 → two, then four)
    private const int MAX_TOY_LOGOS = 8;       // Casting Couch alive cap (capstone double-launch included)
    private bool _closed;
    private double _vx, _vy;                   // DIPs per second
    private static DateTime _lastBounceCue;    // shared across logos: one boing per 250ms max
    private double _remainingSec;
    private int _hueIndex;
    // Composition-clock state. Motion runs off CompositionTarget.Rendering (one
    // vsync-aligned callback per rendered frame) instead of a 33ms DispatcherTimer,
    // whose coarse OS-quantized cadence beat against the display refresh and made the
    // logo judder. _lastRender feeds true delta-time movement.
    private TimeSpan _lastRender = TimeSpan.MinValue;

    /// <summary>Launch <paramref name="count"/> logos for <paramref name="durationSec"/>.
    /// Rank ramp: <paramref name="speedMult"/>/<paramref name="scale"/> grow with the skill's level.
    /// <paramref name="text"/> marks an Intrusive Thoughts instance (custom phrase, alive cap);
    /// <paramref name="splitOnRabbit"/> arms its capstone split. <paramref name="splitBounces"/>
    /// (Casting Couch mantra) makes the logo split at its next wall hits: 2 → one, then four.</summary>
    public static void Launch(double durationSec, double speedMult, double scale, int count = 1,
                              string? text = null, bool splitOnRabbit = false, int splitBounces = 0)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted) return;
        disp.BeginInvoke(new Action(() =>
        {
            try
            {
                for (int i = 0; i < Math.Clamp(count, 1, 2); i++)
                {
                    if (text != null && ThoughtCount() >= MAX_THOUGHTS) break;
                    Acquire().Begin(durationSec, speedMult, scale, text, splitOnRabbit, null, null, null, null, splitBounces);
                }
            }
            catch (Exception ex) { App.Logger?.Debug("ChaosDvdOverlay.Launch: {E}", ex.Message); }
        }));
    }

    /// <summary>Pop a recycled logo window or create a fresh one (UI thread only).</summary>
    private static ChaosDvdOverlay Acquire()
    {
        while (_pool.Count > 0)
        {
            var pooled = _pool.Pop();
            if (pooled.IsLoaded && !pooled._closed) return pooled;
        }
        return new ChaosDvdOverlay();
    }

    private static int ThoughtCount() => _active.Count(w => w._isThought);

    /// <summary>True while at least one logo is flying (used to gate re-use, if ever needed).</summary>
    public static bool AnyActive => _active.Count > 0;

    /// <summary>True while a TOY-launched logo flies. Intrusive Thoughts phrases don't count —
    /// they share this overlay, and counting them lit the Porn DVD button mid-cooldown.</summary>
    public static bool AnyToyActive => _active.Any(w => !w._isThought);

    /// <summary>Re-stack every flying logo above a mandatory video (see ChaosWindowZ). UI thread only.</summary>
    public static void RaiseActive()
    {
        foreach (var w in _active) ChaosWindowZ.RaiseTopmost(w);
    }

    /// <summary>Run teardown: close every flying logo immediately and drain the pool —
    /// the only point where these hwnds actually die.</summary>
    public static void CloseActive()
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null) return;
        disp.BeginInvoke(new Action(() =>
        {
            foreach (var w in _active.ToArray()) w.CloseNow();
            while (_pool.Count > 0)
            {
                try { _pool.Pop().CloseNow(); } catch { }
            }
        }));
    }

    private ChaosDvdOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = ChaosWindowZ.BornTopmost;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Opacity = 0;

        _label = new OutlinedText
        {
            Stroke = FrozenBrush(Color.FromRgb(0x0B, 0x08, 0x12)),
            StrokeThickness = 2.6,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _host = new Grid { Children = { _label } };
        // One-time handler, gated on the per-launch _clickable flag (no handler stacking on reuse).
        _host.MouseLeftButtonDown += (_, e) =>
        {
            if (!_clickable) return;
            Redirect();
            e.Handled = true;
        };
        Content = _host;
    }

    /// <summary>Start (or restart, when recycled) one logo flight. All per-launch state lives here.</summary>
    private void Begin(double durationSec, double speedMult, double scale,
                       string? text, bool splitOnRabbit,
                       double? startX, double? startY, double? vxOverride, double? vyOverride,
                       int splitBounces = 0)
    {
        _remainingSec = Math.Max(1, durationSec);
        _isThought = text != null;
        _splitOnRabbit = splitOnRabbit;
        _fontScale = Math.Clamp(scale, 0.5, 1.5);
        _splitSpent = false;
        _splitBouncesLeft = Math.Max(0, splitBounces);

        _clickable = SpankerRedirect?.Invoke() == true;   // The Spanker capstone: smack to turn
        IsHitTestVisible = _clickable;
        // A near-invisible background makes the whole logo rect clickable for the smack.
        _host.Background = _clickable ? new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) : null;
        _host.Cursor = _clickable ? Cursors.Hand : null;

        _hueIndex = _rng.Next(Hues.Length);
        _label.Text = text ?? "PORN";
        _label.FontSize = BASE_FONT * _fontScale;
        _label.Fill = FrozenBrush(Hues[_hueIndex]);

        // Random start inside the work area, random diagonal. Speed scales with rank.
        var wa = SystemParameters.WorkArea;
        double speed = BASE_SPEED * Math.Clamp(speedMult, 0.3, 2.0);
        double angle = _rng.NextDouble() * Math.PI / 3 + Math.PI / 9;   // 20°..80°, never axis-flat
        _vx = vxOverride ?? speed * Math.Cos(angle) * (_rng.Next(2) == 0 ? 1 : -1);
        _vy = vyOverride ?? speed * Math.Sin(angle) * (_rng.Next(2) == 0 ? 1 : -1);

        _active.Add(this);
        Show();                                  // first call creates the hwnd; re-shows unhide
        ChaosWindowZ.RaiseAboveVideo(this);      // un-hiding doesn't re-stack — kick over a playing video
        ApplyExStyles(_clickable);               // per launch — clickability can differ per run

        _label.Build();
        Width = _label.Width;
        Height = _label.Height;
        Left = startX ?? wa.Left + _rng.NextDouble() * Math.Max(1, wa.Width - Width);
        Top = startY ?? wa.Top + _rng.NextDouble() * Math.Max(1, wa.Height - Height);

        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, PEAK_OPAC, TimeSpan.FromMilliseconds(180)));
        _lastRender = TimeSpan.MinValue;
        CompositionTarget.Rendering -= Step;     // guard against double-subscribe on reuse
        CompositionTarget.Rendering += Step;
    }

    /// <summary>The Spanker capstone: a smacked logo takes a fresh random heading + a hue hop.</summary>
    private void Redirect()
    {
        double spd = Math.Sqrt(_vx * _vx + _vy * _vy);
        if (spd < 1) spd = BASE_SPEED;
        // Each smack also hurries it along (+18%), capped at ~2.5x the base pace.
        spd = Math.Min(spd * 1.18, BASE_SPEED * 2.5);
        double angle = _rng.NextDouble() * Math.PI * 2;
        _vx = Math.Cos(angle) * spd;
        _vy = Math.Sin(angle) * spd;
        _hueIndex = (_hueIndex + 1) % Hues.Length;
        _label.Fill = FrozenBrush(Hues[_hueIndex]);
        _label.InvalidateVisual();
        Services.Chaos.ChaosSfx.Play("dvd_bounce", 0.4f);
    }

    /// <summary>Intrusive Thoughts capstone: brushing a rabbit splits the thought — self gets
    /// +2s, plus one diverged clone (which can split again). Cap enforced.</summary>
    private void SplitInTwo()
    {
        _remainingSec += 2.0;
        if (ThoughtCount() >= MAX_THOUGHTS) return;
        double spd = Math.Sqrt(_vx * _vx + _vy * _vy);
        double baseAng = Math.Atan2(_vy, _vx);
        double ang = baseAng + (_rng.Next(2) == 0 ? 0.61 : -0.61);   // ~35° divergence
        Acquire().Begin(_remainingSec, 1.0, _fontScale, _label.Text, true,
                        Left, Top, Math.Cos(ang) * spd, Math.Sin(ang) * spd);
        Services.Chaos.ChaosSfx.Play("dvd_bounce", 0.4f);
    }

    private void Step(object? sender, EventArgs e)
    {
        try
        {
            // Delta time from the composition clock: baseline on the first frame,
            // skip duplicate callbacks, clamp after a stall so the logo can't jump.
            double dt = TICK_MS / 1000.0;
            if (e is RenderingEventArgs r)
            {
                if (_lastRender == TimeSpan.MinValue) { _lastRender = r.RenderingTime; return; }
                dt = (r.RenderingTime - _lastRender).TotalSeconds;
                _lastRender = r.RenderingTime;
                if (dt <= 0) return;
                if (dt > 0.1) dt = 0.1;
            }

            _remainingSec -= dt;
            if (_remainingSec <= 0) { FadeOutAndRetire(); return; }

            var wa = SystemParameters.WorkArea;
            double x = Left + _vx * dt;
            double y = Top + _vy * dt;
            bool bounced = false;
            if (x <= wa.Left) { x = wa.Left; _vx = Math.Abs(_vx); bounced = true; }
            else if (x + Width >= wa.Right) { x = wa.Right - Width; _vx = -Math.Abs(_vx); bounced = true; }
            if (y <= wa.Top) { y = wa.Top; _vy = Math.Abs(_vy); bounced = true; }
            else if (y + Height >= wa.Bottom) { y = wa.Bottom - Height; _vy = -Math.Abs(_vy); bounced = true; }
            Left = x;
            Top = y;

            if (bounced)
            {
                _hueIndex = (_hueIndex + 1) % Hues.Length;
                _label.Fill = FrozenBrush(Hues[_hueIndex]);
                _label.InvalidateVisual();
                // Soft retro boing, throttled so two logos hugging a corner can't machine-gun it.
                var now = DateTime.UtcNow;
                if ((now - _lastBounceCue).TotalMilliseconds >= 250)
                {
                    _lastBounceCue = now;
                    Services.Chaos.ChaosSfx.Play("dvd_bounce", 0.35f);
                }

                // Casting Couch: the bounce splits the logo — a diverged twin peels off, both
                // keep one fewer split (2 → two logos → four), capped so a corner can't flood.
                if (_splitBouncesLeft > 0 && !_isThought)
                {
                    _splitBouncesLeft--;
                    if (_active.Count(w => !w._isThought) < MAX_TOY_LOGOS)
                    {
                        double spd = Math.Sqrt(_vx * _vx + _vy * _vy);
                        double baseAng = Math.Atan2(_vy, _vx);
                        double ang = baseAng + (_rng.Next(2) == 0 ? 0.61 : -0.61);   // ~35° divergence
                        Acquire().Begin(_remainingSec, 1.0, _fontScale, null, false,
                                        Left, Top, Math.Cos(ang) * spd, Math.Sin(ang) * spd,
                                        _splitBouncesLeft);
                        Services.Chaos.ChaosSfx.Play("dvd_launch", 0.35f);
                    }
                }
            }

            // The collider: everything the logo overlaps pops/snaps (darters/freeze exempt;
            // Pop()'s pause-lock guard silences this while the run is manually paused).
            App.Bubbles?.PopBubblesInRect(new Rect(x, y, Width, Height));

            // Intrusive Thoughts capstone: a thought that brushes a rabbit splits (once per instance).
            if (_splitOnRabbit && !_splitSpent
                && App.Bubbles?.AnyDarterIntersects(new Rect(x, y, Width, Height)) == true)
            {
                _splitSpent = true;
                SplitInTwo();
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ChaosDvdOverlay step: {E}", ex.Message);
            CloseNow();
        }
    }

    private void FadeOutAndRetire()
    {
        CompositionTarget.Rendering -= Step;
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(240));
        fade.Completed += (_, _) => Retire();
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Hide and return to the pool — the window outlives the flight.</summary>
    private void Retire()
    {
        CompositionTarget.Rendering -= Step;
        _active.Remove(this);
        if (_closed) return;
        try
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 0;
            Hide();
        }
        catch { }
        if (_pool.Count < POOL_MAX) _pool.Push(this);
        else { try { Close(); } catch { } }
    }

    private void CloseNow()
    {
        CompositionTarget.Rendering -= Step;
        _active.Remove(this);
        try { Close(); } catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        CompositionTarget.Rendering -= Step;
        base.OnClosed(e);
    }

    private static Brush FrozenBrush(Color c)
    {
        var b = new SolidColorBrush(c);
        if (b.CanFreeze) b.Freeze();
        return b;
    }

    private void ApplyExStyles(bool clickable)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            // Clickable (Spanker capstone) logos keep hit-testing; everything else is pass-through.
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            if (clickable) ex &= ~WS_EX_TRANSPARENT;
            else ex |= WS_EX_TRANSPARENT;
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);
        }
        catch { }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
}
