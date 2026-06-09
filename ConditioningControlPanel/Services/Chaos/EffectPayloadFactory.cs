using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// Per-payload tuning the user can configure (which kinds the chaos pool draws
/// from and how often). Mirrors a row in the AppSettings payload map.
/// </summary>
public sealed class PayloadConfig
{
    public EffectBubblePayloadKind Kind { get; set; }
    public bool Enabled { get; set; } = true;
    /// <summary>Relative draw weight (higher = more common).</summary>
    public double Weight { get; set; } = 1.0;
    /// <summary>Run-intensity (0..1) below which this kind won't appear.</summary>
    public double MinIntensity { get; set; } = 0.0;
}

/// <summary>
/// Builds <see cref="EffectPayload"/> instances by weighted random pick, honoring
/// per-payload enable/weight/min-intensity config. <see cref="EffectPayload.Strength"/>
/// is set by the caller from bubble size — the factory only chooses the kind.
/// </summary>
public static class EffectPayloadFactory
{
    private static readonly Random _rng = new();

    /// <summary>Sensible built-in defaults if the user hasn't configured the pool.</summary>
    public static List<PayloadConfig> DefaultConfig() => new()
    {
        new() { Kind = EffectBubblePayloadKind.Flash,       Weight = 3.0, MinIntensity = 0.00 },
        new() { Kind = EffectBubblePayloadKind.Subliminal,  Weight = 3.0, MinIntensity = 0.00 },
        new() { Kind = EffectBubblePayloadKind.Overlay,     Weight = 2.0, MinIntensity = 0.15 },
        new() { Kind = EffectBubblePayloadKind.Audio,       Weight = 1.5, MinIntensity = 0.00 },
        new() { Kind = EffectBubblePayloadKind.BambiFreeze, Weight = 1.0, MinIntensity = 0.35 },
        new() { Kind = EffectBubblePayloadKind.Video,       Weight = 0.6, MinIntensity = 0.55 },
        new() { Kind = EffectBubblePayloadKind.HtLink,      Weight = 0.5, MinIntensity = 0.65 },
    };

    /// <summary>
    /// Pick a payload kind by weight, filtered to enabled kinds whose
    /// MinIntensity ≤ <paramref name="runIntensity"/> (0..1), and build it.
    /// Falls back to a Flash payload if nothing qualifies.
    /// </summary>
    public static EffectPayload PickWeighted(IReadOnlyList<PayloadConfig> config, double runIntensity)
    {
        var pool = (config ?? DefaultConfig())
            .Where(c => c.Enabled && c.Weight > 0 && runIntensity >= c.MinIntensity)
            .ToList();

        EffectBubblePayloadKind kind;
        if (pool.Count == 0)
        {
            kind = EffectBubblePayloadKind.Flash;
        }
        else
        {
            double total = pool.Sum(c => c.Weight);
            double roll = _rng.NextDouble() * total;
            kind = pool[pool.Count - 1].Kind;
            foreach (var c in pool)
            {
                roll -= c.Weight;
                if (roll <= 0) { kind = c.Kind; break; }
            }
        }

        return Build(kind);
    }

    /// <summary>Construct a payload for a specific kind (Strength set later by caller).</summary>
    public static EffectPayload Build(EffectBubblePayloadKind kind) => kind switch
    {
        EffectBubblePayloadKind.Flash       => new FlashPayload(),
        EffectBubblePayloadKind.Subliminal  => new SubliminalPayload(),
        EffectBubblePayloadKind.Overlay     => new OverlayPayload(PickOverlayKind()),
        EffectBubblePayloadKind.Video       => new VideoPayload(),
        EffectBubblePayloadKind.HtLink      => new HtLinkPayload(),
        EffectBubblePayloadKind.Audio       => new AudioPayload(),
        EffectBubblePayloadKind.BambiFreeze => new BambiFreezePayload(),
        _                                   => new FlashPayload(),
    };

    private static string PickOverlayKind()
    {
        string[] kinds = { "pink_filter", "spiral", "braindrain" };
        return kinds[_rng.Next(kinds.Length)];
    }
}
