using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Media;
using global::Avalonia.Platform;
using global::Avalonia.Rendering;
using global::Avalonia.Rendering.SceneGraph;
using global::Avalonia.Skia;
using global::Avalonia.Threading;
using global::ConditioningControlPanel.Avalonia;
using global::ConditioningControlPanel.Avalonia.Chaos;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Avalonia Skia control that renders the Rabbit Hole main-menu art: a crossfading flipbook of
/// menu frames, authored glint FX (glow / twinkle / sheen) from painted masks, drifting pink fog,
/// and a one-shot intro reveal. Mirrors the WPF SKElement menu scene from ChaosHubWindow.
/// </summary>
public sealed class MenuArtControl : Control
{
    private const float Dt = 0.033f;
    private const float SweepDur = 1.4f;
    private const float SweepPeriodBase = 7.5f;
    private const float IntroDur = 1.1f;

    private readonly SKImage?[] _frames = new SKImage?[6];
    private readonly SKImage?[] _fxMasks = new SKImage?[6];
    private readonly SKImage?[] _blooms = new SKImage?[6];
    private readonly (float nx, float ny, float w)[][] _twSpots = new (float, float, float)[6][];
    private SKImage? _skStill, _fxStill, _bloomStill;
    private (float nx, float ny, float w)[] _twStill = Array.Empty<(float, float, float)>();
    private SKColorFilter? _rToA, _gToA, _bToA;

    private readonly List<Tw> _tw = new();
    private readonly List<FogPuff> _fog = new();
    private readonly Random _rng = new();

    private int _baseIdx, _topIdx = -1;
    private float _fadeT = 1f, _fadeDurSec = 0.55f;
    private bool _fading;
    private float _breathClock;
    private float _glowI = 1f, _glowF = 1f, _twkI = 1f, _twkF = 1f, _shI = 1f, _shF = 1f;
    private float _twAccum, _sweepClock;
    private int _fogW, _fogH;
    private float _introClock;
    private bool _introActive;

    private DispatcherTimer? _timer;
    private DispatcherTimer? _flipTimer;
    private int _flipPos;
    private (int f, int holdMs)[] _flipSeq = Array.Empty<(int, int)>();
    private bool _running;

    private static ConditioningControlPanel.Models.AppSettings? Settings =>
        App.Services?.GetService<global::ConditioningControlPanel.Core.Services.Settings.ISettingsService>()?.Current;

    private struct Tw { public float Nx, Ny, Age, Life, Size; public SKColor Col; }
    private struct FogPuff { public float X, Y, R, VX, VY, Phase, PhaseSpd, BaseA; }

    public MenuArtControl()
    {
        LoadFx();
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _introClock = 0f;
        _introActive = true;
        if (_timer == null)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _timer.Tick += (_, _) => Step();
        }
        _timer.Start();
        StartFlipbook();
        InvalidateVisual();
    }

    public void Stop()
    {
        _running = false;
        _timer?.Stop();
        _flipTimer?.Stop();
    }

    private void StartFlipbook()
    {
        if (_flipSeq.Length == 0) return;
        if (_flipTimer == null)
        {
            _flipTimer = new DispatcherTimer();
            _flipTimer.Tick += (_, _) => AdvanceFlip();
        }
        _flipPos = 0;
        _flipTimer.Interval = TimeSpan.FromMilliseconds(_flipSeq[0].holdMs);
        _flipTimer.Start();
    }

    private void AdvanceFlip()
    {
        if (_flipSeq.Length == 0) return;
        _flipPos = (_flipPos + 1) % _flipSeq.Length;
        var step = _flipSeq[_flipPos];
        CrossfadeTo(step.f, 550);
        if (_flipTimer != null) _flipTimer.Interval = TimeSpan.FromMilliseconds(step.holdMs);
    }

    /// <summary>Advance the flipbook to the next expression now (click response).</summary>
    public void Advance()
    {
        if (_frames.Length == 0) return;
        int idx = _rng.Next(_frames.Length);
        CrossfadeTo(idx, 550);
    }

    private void Step()
    {
        bool fx = Settings?.ChaosSkiaFxEnabled == true;
        if (_fading)
        {
            _fadeT += Dt / Math.Max(0.05f, _fadeDurSec);
            if (_fadeT >= 1f) { _fadeT = 1f; _fading = false; _baseIdx = _topIdx; }
        }
        _breathClock += Dt;
        if (_introActive) { _introClock += Dt; if (_introClock >= IntroDur) _introActive = false; }
        if (fx) { StepFogPuffs(); StepTwinkles(); }
        InvalidateVisual();
    }

    private void LoadFx()
    {
        bool hasCore = true;
        for (int i = 0; i < 6; i++)
        {
            _frames[i] = LoadSk(ChaosArt.MenuFramePath(i + 1));
            var fxp = ChaosArt.FilePath($"menu_{i + 1}_fx.png");
            _fxMasks[i] = LoadSk(fxp);
            _twSpots[i] = ExtractSpots(fxp, 1);
            if (i < 3 && _frames[i] == null) hasCore = false;
        }
        _skStill = LoadSk(ChaosArt.FilePath("menu.png")) ?? LoadSk(ChaosArt.FilePath("banner.png"));
        var stillFx = ChaosArt.FilePath("menu_fx.png");
        _fxStill = LoadSk(stillFx);
        _twStill = ExtractSpots(stillFx, 1);

        if (hasCore) _flipSeq = BuildFlipSeq();

        _rToA ??= ChanToAlpha(0); _gToA ??= ChanToAlpha(1); _bToA ??= ChanToAlpha(2);
        LoadFxTuning();
        BuildAllBlooms();
    }

    private (int f, int holdMs)[] BuildFlipSeq()
    {
        const int IDLE = 6000;
        var seq = new List<(int, int)>();
        void Add(int idx, int hold) { if (idx < _frames.Length && _frames[idx] != null) seq.Add((idx, hold)); }
        Add(0, IDLE); Add(1, 1900);
        Add(0, IDLE); Add(4, 2300);
        Add(0, IDLE); Add(2, 3000);
        Add(0, IDLE); Add(3, 2700);
        Add(0, IDLE); Add(5, 2500);
        if (seq.Count == 0) seq.Add((0, IDLE));
        return seq.ToArray();
    }

    private static SKImage? LoadSk(string? path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            using var s = File.OpenRead(path);
            return SKImage.FromEncodedData(s);
        }
        catch { return null; }
    }

    private static SKColorFilter ChanToAlpha(int ch)
    {
        var m = new float[20];
        m[4] = 1; m[9] = 1; m[14] = 1;
        m[15 + ch] = 1;
        return SKColorFilter.CreateColorMatrix(m);
    }

    private void LoadFxTuning()
    {
        try
        {
            var p = ChaosArt.FilePath("menu_fx.json");
            if (p == null || !File.Exists(p)) return;
            var o = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(p));
            float Get(string fx, string k, float def) => (float?)o[fx]?[k] ?? def;
            _glowI = Get("glow", "intensity", 1f); _glowF = Get("glow", "frequency", 1f);
            _twkI = Get("twinkle", "intensity", 1f); _twkF = Get("twinkle", "frequency", 1f);
            _shI = Get("sheen", "intensity", 1f); _shF = Get("sheen", "frequency", 1f);
        }
        catch { }
    }

    private static (float nx, float ny, float w)[] ExtractSpots(string? path, int channel)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return Array.Empty<(float, float, float)>();
            using var raw = SKBitmap.Decode(path);
            if (raw == null) return Array.Empty<(float, float, float)>();
            int tw = 96, th = Math.Max(1, raw.Height * 96 / Math.Max(1, raw.Width));
            using var small = raw.Resize(new SKImageInfo(tw, th), SKFilterQuality.Medium) ?? raw;
            int w = small.Width, h = small.Height;
            const int cell = 8;
            int cols = Math.Max(1, w / cell), rows = Math.Max(1, h / cell);
            var best = new (float v, int x, int y)[cols * rows];
            float gmax = 0.001f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = small.GetPixel(x, y);
                    float v = (channel == 0 ? c.Red : channel == 1 ? c.Green : c.Blue) / 255f;
                    int ci = Math.Min(rows - 1, y * rows / h) * cols + Math.Min(cols - 1, x * cols / w);
                    if (v > best[ci].v) best[ci] = (v, x, y);
                    if (v > gmax) gmax = v;
                }
            var list = new List<(float, float, float)>();
            foreach (var b in best)
                if (b.v > gmax * 0.5f)
                    list.Add(((b.x + 0.5f) / w, (b.y + 0.5f) / h, b.v / gmax));
            return list.OrderByDescending(z => z.Item3).Take(10).ToArray();
        }
        catch { return Array.Empty<(float, float, float)>(); }
    }

    private void BuildAllBlooms()
    {
        for (int i = 0; i < 6; i++) { _blooms[i]?.Dispose(); _blooms[i] = MakeBloom(i); }
        _bloomStill?.Dispose(); _bloomStill = MakeBloom(-1);
    }

    private SKImage? MakeBloom(int idx)
    {
        var src = FrameImage(idx); var mask = FxMask(idx);
        if (src == null || mask == null || _rToA == null) return null;
        try
        {
            int bw = Math.Min(src.Width, 540);
            int bh = Math.Max(1, src.Height * bw / src.Width);
            var bi = new SKImageInfo(bw, bh, SKColorType.Rgba8888, SKAlphaType.Premul);
            var rect = new SKRect(0, 0, bw, bh);
            using var s1 = SKSurface.Create(bi);
            s1.Canvas.Clear(SKColors.Transparent);
            using (var ap = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.Medium })
                s1.Canvas.DrawImage(src, rect, ap);
            using (var mp = new SKPaint { BlendMode = SKBlendMode.DstIn, ColorFilter = _rToA, FilterQuality = SKFilterQuality.Medium })
                s1.Canvas.DrawImage(mask, rect, mp);
            using var masked = s1.Snapshot();
            using var s2 = SKSurface.Create(bi);
            s2.Canvas.Clear(SKColors.Transparent);
            using (var bp = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(bw * 0.013f, bw * 0.013f) })
                s2.Canvas.DrawImage(masked, rect, bp);
            return s2.Snapshot();
        }
        catch { return null; }
    }

    private SKImage? FrameImage(int idx) =>
        idx >= 0 && idx < _frames.Length && _frames[idx] != null ? _frames[idx] : _skStill;

    private SKImage? FxMask(int idx) =>
        idx >= 0 && idx < _fxMasks.Length && _fxMasks[idx] != null ? _fxMasks[idx] : _fxStill;

    private (float nx, float ny, float w)[] TwSpots(int idx) =>
        idx >= 0 && idx < _twSpots.Length && _twSpots[idx] != null ? _twSpots[idx] : _twStill;

    private SKImage? BloomFor(int idx) =>
        idx >= 0 && idx < _blooms.Length && _blooms[idx] != null ? _blooms[idx] : _bloomStill;

    private void CrossfadeTo(int idx, double fadeMs)
    {
        var src = FrameImage(idx);
        if (src == null) return;
        _topIdx = idx; _fadeT = 0f; _fading = true; _fadeDurSec = (float)(fadeMs / 1000.0);
    }

    private static SKRect CoverRect(SKImage img, SKImageInfo info)
    {
        float ew = info.Width, eh = info.Height, iw = img.Width, ih = img.Height;
        float s = Math.Max(ew / iw, eh / ih);
        float dw = iw * s, dh = ih * s;
        return new SKRect((ew - dw) / 2f, (eh - dh) / 2f, (ew + dw) / 2f, (eh + dh) / 2f);
    }

    private void StepTwinkles()
    {
        _sweepClock += Dt;
        var spots = TwSpots(_baseIdx);
        if (spots.Length > 0)
        {
            _twAccum -= Dt;
            int maxn = Math.Max(1, (int)Math.Round(3 * _twkI));
            if (_twAccum <= 0f)
            {
                _twAccum = (0.35f + (float)_rng.NextDouble() * 0.45f) / Math.Max(0.05f, _twkF);
                if (_tw.Count < maxn)
                {
                    float total = 0; foreach (var s in spots) total += s.w + 0.05f;
                    float pick = (float)_rng.NextDouble() * total; var hs = spots[0];
                    foreach (var s in spots) { pick -= s.w + 0.05f; if (pick <= 0) { hs = s; break; } }
                    double r = _rng.NextDouble();
                    var col = r < 0.55 ? new SKColor(255, 255, 255) : r < 0.8 ? new SKColor(255, 230, 176) : new SKColor(255, 199, 230);
                    _tw.Add(new Tw
                    {
                        Nx = hs.nx, Ny = hs.ny, Age = 0,
                        Life = 0.7f + (float)_rng.NextDouble() * 0.45f,
                        Size = 6f + (float)_rng.NextDouble() * 8f,
                        Col = col,
                    });
                }
            }
        }
        for (int i = _tw.Count - 1; i >= 0; i--)
        {
            var t = _tw[i]; t.Age += Dt;
            if (t.Age >= t.Life) _tw.RemoveAt(i); else _tw[i] = t;
        }
    }

    private static float Frac(float v) { v -= (float)Math.Floor(v); return v; }

    private void InitFog(int w, int h)
    {
        _fog.Clear();
        _fogW = w; _fogH = h;
        int n = 14;
        for (int i = 0; i < n; i++)
        {
            float t = (i + 0.5f) / n;
            _fog.Add(new FogPuff
            {
                X = w * (0.10f + 0.85f * Frac(t * 1.7f)),
                Y = h * (0.40f + 0.65f * Frac(t * 2.3f)),
                R = w * (0.34f + 0.26f * Frac(t * 3.1f)),
                VX = w * (0.004f + 0.006f * Frac(t * 5f)) * (i % 2 == 0 ? 1 : -1),
                VY = -h * (0.003f + 0.004f * Frac(t * 4f)),
                Phase = t * 6.283f,
                PhaseSpd = 0.012f + 0.01f * Frac(t * 6f),
                BaseA = 0.34f + 0.22f * Frac(t * 7f),
            });
        }
    }

    private void StepFogPuffs()
    {
        for (int i = 0; i < _fog.Count; i++)
        {
            var p = _fog[i];
            p.X += p.VX; p.Y += p.VY; p.Phase += p.PhaseSpd;
            if (p.Y + p.R < 0) { p.Y = _fogH + p.R; p.X = _fogW * (0.15f + 0.7f * Frac(p.Phase)); }
            if (p.X - p.R > _fogW) p.X = -p.R;
            if (p.X + p.R < 0) p.X = _fogW + p.R;
            _fog[i] = p;
        }
    }

    private void DrawFog(SKCanvas canvas, SKImageInfo info)
    {
        if (_fog.Count == 0 || _fogW != info.Width || _fogH != info.Height) InitFog(info.Width, info.Height);
        using var paint = new SKPaint { IsAntialias = true };
        foreach (var p in _fog)
        {
            float a = p.BaseA * (0.7f + 0.3f * (float)Math.Sin(p.Phase));
            if (a <= 0.01f) continue;
            var c = new SKPoint(p.X, p.Y);
            using var shader = SKShader.CreateRadialGradient(
                c, p.R,
                new[] { new SKColor(0xE8, 0x43, 0x93, (byte)(a * 255)), new SKColor(0xE8, 0x43, 0x93, 0) },
                null, SKShaderTileMode.Clamp);
            paint.Shader = shader;
            canvas.DrawCircle(c, p.R, paint);
        }
    }

    private static float EaseOutCubic(float p) { p = Math.Clamp(p, 0f, 1f); float u = 1f - p; return 1f - u * u * u; }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.Custom(new MenuArtDrawOp(new Rect(0, 0, Bounds.Width, Bounds.Height), this));
    }

    private void OnPaint(SKCanvas canvas, SKImageInfo info)
    {
        canvas.Clear(SKColors.Transparent);
        if (info.Width <= 0 || info.Height <= 0) return;

        bool fx = Settings?.ChaosSkiaFxEnabled == true;
        float ia = _introActive ? EaseOutCubic(_introClock / IntroDur) : 1f;
        bool introLayer = ia < 0.999f;
        if (introLayer) canvas.SaveLayer(new SKPaint { Color = SKColors.White.WithAlpha((byte)(ia * 255)) });

        canvas.Save();
        ApplyBreath(canvas, info);
        DrawMenuArt(canvas, info);
        if (fx) { try { DrawAuthoredFx(canvas, info); } catch { } }
        canvas.Restore();

        if (fx) { try { DrawFog(canvas, info); } catch { } }

        if (introLayer) canvas.Restore();
    }

    private void ApplyBreath(SKCanvas canvas, SKImageInfo info)
    {
        float t = _breathClock;
        float s = 1.035f + 0.012f * (float)Math.Sin(t * 0.9);
        float dx = 0.0025f * info.Width * (float)Math.Sin(t * 0.50);
        float dy = 0.0040f * info.Height * (float)Math.Sin(t * 0.78);
        float ang = 0.18f * (float)Math.Sin(t * 0.62);
        if (_introActive)
        {
            float e = EaseOutCubic(_introClock / IntroDur);
            s *= 1.10f - 0.10f * e;
            dy += (1f - e) * 0.03f * info.Height;
        }
        canvas.Translate(info.Width / 2f, info.Height / 2f);
        canvas.Scale(s, s);
        canvas.RotateDegrees(ang);
        canvas.Translate(-info.Width / 2f + dx, -info.Height / 2f + dy);
    }

    private void DrawMenuArt(SKCanvas canvas, SKImageInfo info)
    {
        var baseImg = FrameImage(_baseIdx);
        if (baseImg == null) return;
        using var p = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
        canvas.DrawImage(baseImg, CoverRect(baseImg, info), p);
        if (_fading)
        {
            var topImg = FrameImage(_topIdx);
            if (topImg != null)
            {
                p.Color = SKColors.White.WithAlpha((byte)(Math.Clamp(_fadeT, 0f, 1f) * 255));
                canvas.DrawImage(topImg, CoverRect(topImg, info), p);
            }
        }
    }

    private void DrawAuthoredFx(SKCanvas canvas, SKImageInfo info)
    {
        var baseImg = FrameImage(_baseIdx);
        if (baseImg == null) return;
        var rect = CoverRect(baseImg, info);
        float t = _breathClock;
        bool fading = _fading;
        float tt = Math.Clamp(_fadeT, 0f, 1f);

        if (_glowI > 0.001f)
        {
            float pulse = (0.42f + 0.22f * (float)Math.Sin(t * 1.6 * _glowF)) * _glowI;
            DrawBloom(canvas, rect, BloomFor(_baseIdx), pulse * (fading ? 1f - tt : 1f));
            if (fading) DrawBloom(canvas, rect, BloomFor(_topIdx), pulse * tt);
        }

        if (_bToA != null && _shI > 0.001f)
        {
            float period = SweepPeriodBase / Math.Max(0.05f, _shF);
            float ph = _sweepClock % period;
            if (ph < SweepDur)
            {
                float pp = ph / SweepDur;
                float env = (float)Math.Sin(Math.PI * pp);
                float center = -0.15f + 1.3f * pp;
                float baseA = 0.5f * env * _shI;
                DrawSheen(canvas, rect, FxMask(_baseIdx), center, baseA * (fading ? 1f - tt : 1f));
                if (fading) DrawSheen(canvas, rect, FxMask(_topIdx), center, baseA * tt);
            }
        }

        if (_tw.Count > 0 && _twkI > 0.001f)
        {
            float surf = Math.Min(rect.Width, rect.Height) / 760f;
            using var paint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus };
            foreach (var tw in _tw)
            {
                float env = (float)Math.Sin(Math.PI * (tw.Age / tw.Life)) * _twkI;
                if (env <= 0.01f) continue;
                float cx = rect.Left + tw.Nx * rect.Width;
                float cy = rect.Top + tw.Ny * rect.Height;
                float r = tw.Size * surf * (0.7f + 0.3f * env);
                using (var glow = SKShader.CreateRadialGradient(new SKPoint(cx, cy), r * 2.4f,
                    new[] { tw.Col.WithAlpha((byte)(Math.Clamp(env, 0, 1) * 150)), tw.Col.WithAlpha(0) }, null, SKShaderTileMode.Clamp))
                {
                    paint.Shader = glow; canvas.DrawCircle(cx, cy, r * 2.4f, paint);
                }
                paint.Shader = null;
                paint.Color = SKColors.White.WithAlpha((byte)(Math.Clamp(env, 0, 1) * 220));
                canvas.DrawCircle(cx, cy, r * 0.45f, paint);
            }
        }
    }

    private static void DrawBloom(SKCanvas canvas, SKRect rect, SKImage? bloom, float alpha)
    {
        if (bloom == null || alpha <= 0.002f) return;
        byte a = (byte)Math.Clamp(alpha * 255f, 0, 255);
        using var p = new SKPaint { BlendMode = SKBlendMode.Plus, IsAntialias = true, FilterQuality = SKFilterQuality.High, Color = SKColors.White.WithAlpha(a) };
        canvas.DrawImage(bloom, rect, p);
    }

    private void DrawSheen(SKCanvas canvas, SKRect rect, SKImage? mask, float center, float alpha)
    {
        if (mask == null || _bToA == null || alpha <= 0.002f) return;
        byte a = (byte)Math.Clamp(alpha * 255f, 0, 255);
        const float hw = 0.16f;
        float c0 = Math.Max(0f, center - hw), c2 = Math.Min(1f, center + hw);
        if (a <= 1 || c2 <= c0) return;
        float c1 = Math.Min(Math.Max(center, c0), c2);
        using var layer = new SKPaint { BlendMode = SKBlendMode.Plus };
        canvas.SaveLayer(layer);
        using (var band = SKShader.CreateLinearGradient(
            new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Right, rect.Bottom),
            new[] { SKColors.Transparent, new SKColor(0xFF, 0xFF, 0xFF, a), SKColors.Transparent },
            new[] { c0, c1, c2 }, SKShaderTileMode.Clamp))
        using (var bp = new SKPaint { Shader = band })
            canvas.DrawRect(rect, bp);
        using (var mp = new SKPaint { BlendMode = SKBlendMode.DstIn, ColorFilter = _bToA, FilterQuality = SKFilterQuality.Medium })
            canvas.DrawImage(mask, rect, mp);
        canvas.Restore();
    }


    private sealed class MenuArtDrawOp : ICustomDrawOperation
    {
        private readonly MenuArtControl _owner;
        public MenuArtDrawOp(Rect bounds, MenuArtControl owner) { Bounds = bounds; _owner = owner; }
        public Rect Bounds { get; }
        public bool HitTest(global::Avalonia.Point p) => false;
        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature == null) return;
            using var lease = leaseFeature.Lease();
            var info = new SKImageInfo((int)Math.Ceiling(Bounds.Width), (int)Math.Ceiling(Bounds.Height), SKColorType.Bgra8888, SKAlphaType.Premul);
            _owner.OnPaint(lease.SkCanvas, info);
        }
        public void Dispose() { }
        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);
        public override bool Equals(object? obj) => obj is ICustomDrawOperation other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(typeof(MenuArtDrawOp), Bounds);
    }
}
