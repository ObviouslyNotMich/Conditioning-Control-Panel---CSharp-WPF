using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>How a chaos bubble travels across the screen.</summary>
public enum ChaosMotion
{
    /// <summary>Rises from the bottom and exits the top (the ambient bubble behaviour).</summary>
    FloatUp,
    /// <summary>Falls from the top and exits the bottom.</summary>
    RainDown,
    /// <summary>Drifts and bounces off the screen edges; never exits on its own.</summary>
    RoamBounce
}

/// <summary>
/// Everything BubbleService needs to render + run one chaos effect-bubble. Built
/// by <see cref="ChaosBubbleVariants"/> from a variant row, then handed to
/// <c>BubbleService.SpawnChaosBubble</c>. Kept in the Chaos namespace so the
/// effect/payload concepts don't leak into the ambient bubble game.
/// </summary>
public sealed class EffectBubbleSpec
{
    public string VariantId { get; init; } = "";
    public EffectPayload Payload { get; init; } = new FlashPayload();
    public double SizePx { get; init; } = 200;
    public Color Tint { get; init; } = Colors.HotPink;
    public string Label { get; init; } = "";
    public bool IsLive { get; init; }
    public int FuseMs { get; init; }
    public ChaosMotion Motion { get; init; } = ChaosMotion.FloatUp;

    // ---- darter (bouncing-flash catch target) ----
    /// <summary>True for a darter: small, fast, telegraphed, self-expiring benign reward target.</summary>
    public bool IsDarter { get; init; }
    /// <summary>Active lifetime in ms after the telegraph; on expiry the darter vanishes harmlessly.</summary>
    public int LifetimeMs { get; init; }
    /// <summary>Telegraph (flare) duration in ms before the darter starts moving.</summary>
    public int TelegraphMs { get; init; }
    /// <summary>Catch within this many ms of going active to earn the quick-catch bonus.</summary>
    public int QuickWindowMs { get; init; }
    /// <summary>Velocity magnitude in DIPs/frame once active (fast).</summary>
    public double DarterSpeed { get; init; }

    public int Strength => Payload.Strength;
}

/// <summary>One row in the chaos bubble pool — visual + behaviour + payload binding.</summary>
public sealed record ChaosBubbleVariant(
    string Id,
    string Name,
    EffectBubblePayloadKind PayloadKind,
    string? OverlayKind,        // when PayloadKind == Overlay: pink_filter | spiral | braindrain
    bool IsLive,
    double MinSize,
    double MaxSize,
    ChaosMotion Motion,
    Color Tint,
    string Label,
    double Weight,
    double MinIntensity,
    int FuseMinMs,
    int FuseMaxMs);

/// <summary>
/// The data-driven chaos bubble pool (8 variants for v1) + a weighted picker.
/// Size → Strength (0..100) scales each payload (bigger bubble = stronger effect).
/// Add a row to grow the pool; nothing else needs to change.
/// </summary>
public static class ChaosBubbleVariants
{
    // Global size envelope used to normalise any bubble's size into a 0..100 Strength.
    public const double SizeMinGlobal = 150;
    public const double SizeMaxGlobal = 320;

    // ---- Darter tuning (bouncing-flash catch target) ----
    public const int    DARTER_LIFETIME_MS    = 1800;  // active flight time before it vanishes
    public const int    DARTER_QUICK_WINDOW_MS = 500;  // catch this fast after going active = bonus
    public const int    DARTER_TELEGRAPH_MS   = 400;   // flare-at-origin before it starts moving
    public const double DARTER_SPEED          = 7.0;   // DIPs/frame (~2.5x a RoamBounce bubble)
    public const double DARTER_SIZE_MIN       = 72;
    public const double DARTER_SIZE_MAX       = 96;
    public const int    DARTER_BASE_POINTS    = 120;
    public const int    DARTER_QUICK_BONUS    = 90;
    private static readonly Color DarterTint  = Color.FromRgb(0xFF, 0x4D, 0xC4); // bright flash-pink

    private static readonly Random _rng = new();

    /// <summary>
    /// Per-spawn-tick roll for a darter. Density climbs with run intensity (present from
    /// early waves, denser late). Returns a built darter spec, or null on a no-spawn roll.
    /// </summary>
    public static EffectBubbleSpec? RollDarter(double intensity)
    {
        double chance = 0.10 + Math.Clamp(intensity, 0, 1) * 0.22;  // ~0.10 early → ~0.32 late
        if (_rng.NextDouble() >= chance) return null;
        return BuildDarter(intensity);
    }

    /// <summary>Build a darter spec (benign, no fuse; carries a brief micro-flash payload).</summary>
    public static EffectBubbleSpec BuildDarter(double intensity)
    {
        double size = DARTER_SIZE_MIN + (DARTER_SIZE_MAX - DARTER_SIZE_MIN) * _rng.NextDouble();
        return new EffectBubbleSpec
        {
            VariantId = "darter",
            Payload = new FlashPayload { Strength = 8 },  // a brief micro-flash on catch
            SizePx = size,
            Tint = DarterTint,
            Label = "",
            IsLive = false,
            FuseMs = 0,
            Motion = ChaosMotion.RoamBounce,              // bounce style; darter path overrides speed
            IsDarter = true,
            LifetimeMs = DARTER_LIFETIME_MS,
            TelegraphMs = DARTER_TELEGRAPH_MS,
            QuickWindowMs = DARTER_QUICK_WINDOW_MS,
            DarterSpeed = DARTER_SPEED,
        };
    }

    /// <summary>All variant ids, in table order.</summary>
    public static List<string> AllIds() => All.Select(v => v.Id).ToList();

    /// <summary>Curated one-click bubble-pool mixes for the setup window.</summary>
    public sealed record ChaosPreset(string Name, List<string> VariantIds, double LiveShare);

    public static List<ChaosPreset> Presets => new()
    {
        new("Balanced",   AllIds(),                                                  0.40),
        new("Tease",      new() { "flash", "subliminal", "pink", "spiral", "bambifreeze" }, 0.30),
        new("Overload",   AllIds(),                                                  0.70),
        new("Flash-only", new() { "flash", "subliminal" },                           0.20),
    };

    public static readonly List<ChaosBubbleVariant> All = new()
    {
        // benign "treats" — pop fires a small payload, no fuse
        new("flash",      "Flash",       EffectBubblePayloadKind.Flash,      null,
            false, 150, 210, ChaosMotion.FloatUp,    Color.FromRgb(0xFF,0xD0,0xE8), "",   3.0, 0.00, 0, 0),
        new("subliminal", "Subliminal",  EffectBubblePayloadKind.Subliminal, null,
            false, 170, 220, ChaosMotion.FloatUp,    Color.FromRgb(0xB0,0x80,0xFF), "♥",  3.0, 0.00, 0, 0),

        // live "threats" — defuse for reward or they detonate the effect
        new("pink",       "Pink Filter", EffectBubblePayloadKind.Overlay,    "pink_filter",
            true,  180, 240, ChaosMotion.RainDown,   Color.FromRgb(0xFF,0x3D,0xA5), "◑",  2.0, 0.10, 3500, 5000),
        new("spiral",     "Spiral",      EffectBubblePayloadKind.Overlay,    "spiral",
            true,  180, 240, ChaosMotion.RoamBounce, Color.FromRgb(0x40,0xD0,0xC0), "◎",  2.0, 0.15, 3500, 5000),
        new("braindrain", "BrainDrain",  EffectBubblePayloadKind.Overlay,    "braindrain",
            true,  240, 320, ChaosMotion.RoamBounce, Color.FromRgb(0x40,0x60,0xC0), "☁",  1.4, 0.25, 4500, 6500),
        new("bambifreeze","Bambi Freeze",EffectBubblePayloadKind.BambiFreeze,null,
            true,  180, 240, ChaosMotion.RoamBounce, Color.FromRgb(0x8A,0xE6,0xFF), "❄",  1.0, 0.35, 4000, 5500),
        new("video",      "Video",       EffectBubblePayloadKind.Video,      null,
            true,  240, 300, ChaosMotion.RainDown,   Color.FromRgb(0xE0,0x40,0x4D), "▶",  0.5, 0.50, 5000, 7000),
        new("htlink",     "HT Link",     EffectBubblePayloadKind.HtLink,     null,
            true,  200, 280, ChaosMotion.FloatUp,    Color.FromRgb(0xFF,0xC8,0x3D), "HT", 0.45,0.60, 4500, 6500),
    };

    /// <summary>
    /// Pick a variant (weighted, filtered by MinIntensity) and build a concrete
    /// <see cref="EffectBubbleSpec"/>. <paramref name="intensity"/> (0..1) also biases
    /// size toward the top of the variant's band; <paramref name="fuseTimeMult"/> scales
    /// live fuses (boons); <paramref name="motionOverride"/> forces a motion if set.
    /// </summary>
    public static EffectBubbleSpec Pick(double intensity, double fuseTimeMult = 1.0, ChaosMotion? motionOverride = null,
                                        IReadOnlyCollection<string>? enabledIds = null, double effectIntensity = 1.0)
    {
        var pool = All.Where(v => intensity >= v.MinIntensity && v.Weight > 0
                                  && (enabledIds == null || enabledIds.Contains(v.Id))).ToList();
        // Fall back to enabled-but-gated variants if intensity filtered everything out.
        if (pool.Count == 0)
            pool = All.Where(v => v.Weight > 0 && (enabledIds == null || enabledIds.Contains(v.Id))).ToList();
        if (pool.Count == 0) pool = new List<ChaosBubbleVariant> { All[0] };

        double total = pool.Sum(v => v.Weight);
        double roll = _rng.NextDouble() * total;
        var variant = pool[^1];
        foreach (var v in pool)
        {
            roll -= v.Weight;
            if (roll <= 0) { variant = v; break; }
        }

        return Build(variant, intensity, fuseTimeMult, motionOverride, effectIntensity);
    }

    public static EffectBubbleSpec Build(ChaosBubbleVariant variant, double intensity, double fuseTimeMult = 1.0,
                                         ChaosMotion? motionOverride = null, double effectIntensity = 1.0)
    {
        // Size: random across the band, nudged upward by run intensity.
        double t = Math.Clamp(_rng.NextDouble() * 0.7 + intensity * 0.45, 0, 1);
        double size = variant.MinSize + (variant.MaxSize - variant.MinSize) * t;
        int strength = (int)Math.Round(Math.Clamp((size - SizeMinGlobal) / (SizeMaxGlobal - SizeMinGlobal), 0, 1) * 100);

        EffectPayload payload = variant.PayloadKind == EffectBubblePayloadKind.Overlay && variant.OverlayKind != null
            ? new OverlayPayload(variant.OverlayKind)
            : EffectPayloadFactory.Build(variant.PayloadKind);
        payload.Strength = (int)Math.Clamp(strength * effectIntensity, 0, 100);

        int fuse = 0;
        if (variant.IsLive)
        {
            int baseFuse = variant.FuseMinMs + _rng.Next(Math.Max(1, variant.FuseMaxMs - variant.FuseMinMs));
            // Harder/later in the run = a bit shorter; boons (fuseTimeMult>1) lengthen.
            fuse = (int)Math.Max(1200, baseFuse * (1.0 - intensity * 0.25) * fuseTimeMult);
        }

        return new EffectBubbleSpec
        {
            VariantId = variant.Id,
            Payload = payload,
            SizePx = size,
            Tint = variant.Tint,
            Label = variant.Label,
            IsLive = variant.IsLive,
            FuseMs = fuse,
            Motion = motionOverride ?? variant.Motion,
        };
    }
}
