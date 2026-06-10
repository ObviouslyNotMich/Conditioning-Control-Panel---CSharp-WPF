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

    /// <summary>True for the freeze bubble: a good pickup — catching it freezes the whole field.</summary>
    public bool IsFreeze { get; init; }

    /// <summary>True for the lucky golden bubble: benign, fast, pays real gold (Sparks) on pop.</summary>
    public bool IsGolden { get; init; }
    /// <summary>True for the Pop-up Notification heart: a gentle once-per-loop pickup worth +1 resistance.</summary>
    public bool IsHeart { get; init; }
    /// <summary>Gold Digger: a small falling gold droplet (banks a few Sparks on catch, no score).</summary>
    public bool IsDroplet { get; init; }
    /// <summary>"Look at the bright colors..." sin: a mimic prism — pays 10x and fires the copied effect.</summary>
    public bool IsPrism { get; init; }
    /// <summary>Prism only: the variant id whose soul it copied (its payload + the shadow-pop ghost).</summary>
    public string MimicVariantId { get; init; } = "";
    /// <summary>GG make more GG: an uncatchable sweeper rabbit — born spanked, mows what it crosses.</summary>
    public bool IsSweeper { get; init; }
    /// <summary>Benign-pop score multiplier carried by special treats (Heavy Drop pays 3x). 1 = normal.</summary>
    public double PayMult { get; init; } = 1.0;
    /// <summary>Treat-rot override in ms (0 = the standard 5s). Heavy Drops drift slowly — they get longer.</summary>
    public int TreatLifeMs { get; init; }
    /// <summary>Multiplier on the bubble's natural drift speed (the golden bubble flies).</summary>
    public double SpeedMult { get; init; } = 1.0;

    /// <summary>Optional spawn point (physical screen px). When set, the bubble materialises
    /// centred there (on the screen containing the point) instead of at its motion's usual
    /// origin — Rabbit Caller summons rabbits at the player's click.</summary>
    public double? SpawnAtPxX { get; init; }
    public double? SpawnAtPxY { get; init; }

    // ---- darter (bouncing-flash catch target) ----
    /// <summary>True for a darter: small, fast, telegraphed, self-expiring benign reward target.</summary>
    public bool IsDarter { get; init; }
    /// <summary>Tunnel Vision capstone: this darter spawned bigger + gold-glowing (glow is perf-gated).</summary>
    public bool Spotlight { get; init; }
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
    public const int    DARTER_LIFETIME_MS    = 8000;  // safety backstop; despawn is driven by the 3-bounce-then-exit
    public const int    DARTER_QUICK_WINDOW_MS = 500;  // catch this fast after going active = bonus
    public const int    DARTER_TELEGRAPH_MS   = 400;   // flare-at-origin before it starts moving
    public const double DARTER_SPEED          = 9.0;   // DIPs/frame — a speedy orb
    public const int    DARTER_MAX_BOUNCES    = 3;     // bounces this many times, then flies off-screen
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
    public static EffectBubbleSpec? RollDarter(double intensity, double rateMult = 1.0, bool spotlight = false)
    {
        double chance = (0.05 + Math.Clamp(intensity, 0, 1) * 0.12) * Math.Max(0, rateMult);  // ~0.05 early → ~0.17 late (halved 2026-06-10: field felt overrun)
        if (_rng.NextDouble() >= chance) return null;
        return BuildDarter(intensity, spotlight);
    }

    /// <summary>Build a darter spec (benign, no fuse; carries a brief micro-flash payload).
    /// <paramref name="atPxX"/>/<paramref name="atPxY"/> (physical px) pin the spawn point
    /// — Rabbit Caller's summon-at-click.</summary>
    public static EffectBubbleSpec BuildDarter(double intensity, bool spotlight = false,
                                               double? atPxX = null, double? atPxY = null,
                                               bool sweeper = false)
    {
        double size = DARTER_SIZE_MIN + (DARTER_SIZE_MAX - DARTER_SIZE_MIN) * _rng.NextDouble();
        if (spotlight) size *= 1.15;   // Tunnel Vision capstone: rabbits run bigger
        return new EffectBubbleSpec
        {
            SpawnAtPxX = atPxX,
            SpawnAtPxY = atPxY,
            VariantId = "darter",
            Payload = new FlashPayload { Strength = 8 },  // a brief micro-flash on catch
            SizePx = size,
            Spotlight = spotlight,
            Tint = DarterTint,
            Label = "",
            IsLive = false,
            FuseMs = 0,
            Motion = ChaosMotion.RoamBounce,              // bounce style; darter path overrides speed
            IsDarter = true,
            IsSweeper = sweeper,                          // GG make more GG: born spanked, never caught
            LifetimeMs = DARTER_LIFETIME_MS,
            TelegraphMs = sweeper ? 150 : DARTER_TELEGRAPH_MS,   // sweepers bolt almost immediately
            QuickWindowMs = DARTER_QUICK_WINDOW_MS,
            DarterSpeed = DARTER_SPEED,
        };
    }

    // ---- Lucky golden bubble tuning ----
    public const double GOLDEN_SIZE_MIN   = 110;
    public const double GOLDEN_SIZE_MAX   = 140;
    public const double GOLDEN_SPEED_MULT = 2.8;   // faster than everything else — blink and it's gone
    private static readonly Color GoldenTint = Color.FromRgb(0xFF, 0xD7, 0x00);

    /// <summary>
    /// Build a lucky golden bubble: benign, small, quick, vertical (rises from the bottom or
    /// falls from the top, 50/50) and gone fast. Popping it pays real gold (Sparks) on the
    /// spot — the payout lives in ChaosModeService.OnBenignPopped, keyed off IsGolden.
    /// </summary>
    public static EffectBubbleSpec BuildGolden()
    {
        double size = GOLDEN_SIZE_MIN + (GOLDEN_SIZE_MAX - GOLDEN_SIZE_MIN) * _rng.NextDouble();
        return new EffectBubbleSpec
        {
            VariantId = "golden",
            Payload = new FlashPayload { Strength = 0 },   // no payload — the gold IS the treat
            SizePx = size,
            Tint = GoldenTint,
            Label = "✦",
            IsLive = false,
            FuseMs = 0,
            Motion = _rng.NextDouble() < 0.5 ? ChaosMotion.FloatUp : ChaosMotion.RainDown,
            IsGolden = true,
            SpeedMult = GOLDEN_SPEED_MULT,
        };
    }

    // ---- Pop-up Notification heart tuning ----
    public const double HEART_SIZE_MIN   = 88;
    public const double HEART_SIZE_MAX   = 110;
    public const double HEART_SPEED_MULT = 0.8;    // a lazy drift — kind, but you still have to notice it
    private static readonly Color HeartTint = Color.FromRgb(0xFF, 0x4D, 0x6E);

    /// <summary>
    /// Build the Pop-up Notification heart: a small benign pickup that drifts down from the
    /// top once per loop (when the habit is trained and the roll lands). Catching it grants
    /// +1 resistance — the payout lives in ChaosModeService.OnBenignPopped, keyed off IsHeart.
    /// Missing it costs nothing; it just exits the bottom.
    /// </summary>
    public static EffectBubbleSpec BuildHeart()
    {
        double size = HEART_SIZE_MIN + (HEART_SIZE_MAX - HEART_SIZE_MIN) * _rng.NextDouble();
        return new EffectBubbleSpec
        {
            VariantId = "heart",
            Payload = new FlashPayload { Strength = 0 },   // no payload — the heart IS the treat
            SizePx = size,
            Tint = HeartTint,
            Label = "💖",
            IsLive = false,
            FuseMs = 0,
            Motion = ChaosMotion.RainDown,
            IsHeart = true,
            SpeedMult = HEART_SPEED_MULT,
        };
    }

    // ---- Gold Digger droplet tuning ----
    public const double DROPLET_SIZE_MIN   = 58;
    public const double DROPLET_SIZE_MAX   = 74;
    public const double DROPLET_SPEED_MULT = 2.2;   // they fall fast — lean in or lose them

    /// <summary>
    /// Build one Gold Digger droplet: a small gold bead that bursts out of a popped lucky
    /// bubble (pinned at the pop point in physical px) and rains straight down. Catching it
    /// banks a few Sparks — the payout lives in ChaosModeService.OnBenignPopped (IsDroplet).
    /// </summary>
    public static EffectBubbleSpec BuildGoldDroplet(double atPxX, double atPxY)
    {
        double size = DROPLET_SIZE_MIN + (DROPLET_SIZE_MAX - DROPLET_SIZE_MIN) * _rng.NextDouble();
        return new EffectBubbleSpec
        {
            SpawnAtPxX = atPxX,
            SpawnAtPxY = atPxY,
            VariantId = "gold_droplet",
            Payload = new FlashPayload { Strength = 0 },
            SizePx = size,
            Tint = GoldenTint,
            Label = "✧",
            IsLive = false,
            FuseMs = 0,
            Motion = ChaosMotion.RainDown,
            IsDroplet = true,
            SpeedMult = DROPLET_SPEED_MULT,
        };
    }

    // ---- Heavy Drop tuning ----
    public const double HEAVY_SIZE_MULT  = 1.55;   // on top of the treat's max band (a true giant)
    public const double HEAVY_SPEED_MULT = 0.45;   // slow, stately, unmissable
    public const double HEAVY_PAY_MULT   = 3.0;

    /// <summary>
    /// Build a Heavy Drop: every ~10th bubble swaps for a giant, slow treat (flash or
    /// subliminal, 50/50) that pays triple on pop (spec.PayMult, read in OnBenignPopped).
    /// </summary>
    public static EffectBubbleSpec BuildHeavy(double intensity, double effectIntensity = 1.0, double sizeScale = 1.0)
    {
        var variant = All[_rng.Next(2)];   // rows 0/1 = the flash + subliminal treats
        double classic = variant.MaxSize;  // top of the band → Strength near the band's ceiling
        int strength = (int)Math.Round(Math.Clamp((classic - SizeMinGlobal) / (SizeMaxGlobal - SizeMinGlobal), 0, 1) * 100);
        var payload = EffectPayloadFactory.Build(variant.PayloadKind);
        payload.Strength = (int)Math.Clamp(strength * effectIntensity, 0, 100);
        return new EffectBubbleSpec
        {
            VariantId = variant.Id,
            Payload = payload,
            SizePx = classic * GLOBAL_SIZE_SCALE * Math.Max(0.5, sizeScale) * HEAVY_SIZE_MULT,
            Tint = variant.Tint,
            Label = variant.Label,
            IsLive = false,
            FuseMs = 0,
            Motion = ChaosMotion.RainDown,   // it DROPS — heavy, after all
            SpeedMult = HEAVY_SPEED_MULT,
            PayMult = HEAVY_PAY_MULT,
            TreatLifeMs = 9000,              // slow faller — give it time to be reached
        };
    }

    // ---- "Look at the bright colors..." prism tuning ----
    public const double PRISM_SIZE_MIN   = 165;
    public const double PRISM_SIZE_MAX   = 215;
    public const double PRISM_SPEED_MULT = 0.7;    // a lazy, mesmerising drift
    private static readonly Color PrismTint = Color.FromRgb(0xC8, 0xA8, 0xFF);

    /// <summary>
    /// Build the mimic prism: a swirling iridescent ball wearing another bubble's soul.
    /// Popping it pays 10x — and fires the copied variant's payload (the sin). The copied
    /// bubble's sprite ghosts out underneath as it pops (the "shadow pop", BubbleService).
    /// Video is excluded from the mimic pool — a surprise fullscreen tape is too much hijack.
    /// </summary>
    public static EffectBubbleSpec BuildPrism(double intensity, double effectIntensity = 1.0)
    {
        var pool = All.Where(v => v.Id != "video" && v.PayloadKind != EffectBubblePayloadKind.BambiFreeze).ToList();
        var mimic = pool[_rng.Next(pool.Count)];
        double size = PRISM_SIZE_MIN + (PRISM_SIZE_MAX - PRISM_SIZE_MIN) * _rng.NextDouble();
        int strength = (int)Math.Round(Math.Clamp((size - SizeMinGlobal) / (SizeMaxGlobal - SizeMinGlobal), 0, 1) * 100);
        EffectPayload payload = mimic.PayloadKind == EffectBubblePayloadKind.Overlay && mimic.OverlayKind != null
            ? new OverlayPayload(mimic.OverlayKind)
            : EffectPayloadFactory.Build(mimic.PayloadKind);
        payload.Strength = (int)Math.Clamp(strength * effectIntensity, 0, 100);
        return new EffectBubbleSpec
        {
            VariantId = "prism",
            Payload = payload,
            SizePx = size * GLOBAL_SIZE_SCALE,
            Tint = PrismTint,
            Label = "❂",
            IsLive = false,
            FuseMs = 0,
            Motion = _rng.NextDouble() < 0.5 ? ChaosMotion.RainDown : ChaosMotion.RoamBounce,
            IsPrism = true,
            MimicVariantId = mimic.Id,
            SpeedMult = PRISM_SPEED_MULT,
        };
    }

    /// <summary>All variant ids, in table order.</summary>
    public static List<string> AllIds() => All.Select(v => v.Id).ToList();

    /// <summary>
    /// Short, color-coded word flashed at the bubble the instant its effect fires (the floating
    /// "combat text"). Kept to one punchy uppercase token for a fast read. Empty = no label
    /// (e.g. the darter's micro-flash, which has its own juice). Themed to the conditioning lexicon.
    /// </summary>
    public static string PopWordFor(string id) => id switch
    {
        "flash"       => "FLASH",
        "subliminal"  => "WHISPER",
        "pink"        => "PINK",
        "spiral"      => "SPIRAL",
        "braindrain"  => "DRAIN",
        "bambifreeze" => "FREEZE",
        "video"       => "WATCH",
        "htlink"      => "RAIN",
        "golden"      => "LUCKY",
        "heart"       => "RESIST",
        "gold_droplet"=> "GOLD",
        "prism"       => "10x!",
        _             => ""
    };

    /// <summary>Codex blurb for a bubble variant (plain, understated). Empty for unknown ids.</summary>
    public static string DescriptionFor(string id) => id switch
    {
        "flash"       => "A benign treat. Pop it for a quick flash burst and a little score. Ignore it and it fades in seconds — and your streak halves.",
        "subliminal"  => "A benign treat. Pop it to flash a subliminal from the active pool. Ignore it and it fades in seconds — and your streak halves.",
        "pink"        => "Live. Snap it before the trance runs out, or a pink filter settles over the screen.",
        "spiral"      => "Live. Roams and bounces. Snap it or it drops a spiral overlay.",
        "braindrain"  => "Live and large. Slow but heavy. Triggers into a creeping mind-mist.",
        "bambifreeze" => "A good pickup. Catch it to freeze the whole field. Bubbles hold in place and trances pause for a few seconds.",
        "video"       => "Live and rare. A long trance, but it opens a mandatory video if it goes off.",
        "htlink"      => "Live and rare. Snap it or it triggers a rain of gifs sliding down the screen.",
        "darter"      => "A white rabbit. Fast, bouncing, always late. Catch it for points and a micro flash. Harmless if it gets away.",
        "golden"      => "A lucky bubble. Rare, quick, gone before you know it. Pop it for real gold, banked on the spot. Let it fade and your streak halves.",
        "gold_droplet"=> "A gold bead spilled from a lucky bubble. Falls fast. Catch it for a few Sparks; missing it costs nothing.",
        "prism"       => "A swirling prism wearing another bubble's soul. Pops for 10x — and fires the copied effect. The shadow underneath tells you what it was.",
        _             => ""
    };

    /// <summary>Curated one-click bubble-pool mixes for the setup window.</summary>
    public sealed record ChaosPreset(string Name, List<string> VariantIds);

    public static List<ChaosPreset> Presets => new()
    {
        new("Balanced",   AllIds()),
        new("Tease",      new() { "flash", "subliminal", "pink", "spiral", "bambifreeze" }),
        new("Overload",   AllIds()),
        new("Flash-only", new() { "flash", "subliminal" }),
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
        // freeze — a GOOD pickup (no fuse): catch it to freeze the whole field for a few seconds.
        // FloatUp so an uncaught one drifts off-screen harmlessly (benign bubbles have no fuse/despawn).
        new("bambifreeze","Freeze",       EffectBubblePayloadKind.BambiFreeze,null,
            false, 190, 250, ChaosMotion.FloatUp,    Color.FromRgb(0x8A,0xE6,0xFF), "❄",  1.0, 0.15, 0, 0),
        new("video",      "Video",       EffectBubblePayloadKind.Video,      null,
            true,  240, 300, ChaosMotion.RainDown,   Color.FromRgb(0xE0,0x40,0x4D), "▶",  0.5, 0.50, 5000, 7000),
        // Display renamed HT Link → Gif Rain (2026-06-10): the hypnotube-link payload is long
        // gone — it rains gifs (GifCascadePayload). Id "htlink" is save/discovery-persisted, keep it.
        new("htlink",     "Gif Rain",    EffectBubblePayloadKind.GifCascade, null,
            true,  200, 280, ChaosMotion.FloatUp,    Color.FromRgb(0xFF,0xC8,0x3D), "▼", 0.45,0.60, 4500, 6500),
    };

    /// <summary>
    /// Pick a variant (weighted, filtered by MinIntensity) and build a concrete
    /// <see cref="EffectBubbleSpec"/>. <paramref name="intensity"/> (0..1) also biases
    /// size toward the top of the variant's band; <paramref name="fuseTimeMult"/> scales
    /// live fuses (boons); <paramref name="motionOverride"/> forces a motion if set.
    /// </summary>
    public static EffectBubbleSpec Pick(double intensity, double fuseTimeMult = 1.0, ChaosMotion? motionOverride = null,
                                        IReadOnlyCollection<string>? enabledIds = null, double effectIntensity = 1.0,
                                        double sizeScale = 1.0)
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

        return Build(variant, intensity, fuseTimeMult, motionOverride, effectIntensity, sizeScale);
    }

    /// <summary>Global field shrink (2026-06-10): every variant bubble renders 25% smaller than
    /// its classic band; Breast Enlargement swells them back up via <c>sizeScale</c>.</summary>
    public const double GLOBAL_SIZE_SCALE = 0.75;
    /// <summary>The two giants (video + gif rain) run a further 30% smaller still.</summary>
    public const double GIANT_SIZE_SCALE = 0.70;

    public static EffectBubbleSpec Build(ChaosBubbleVariant variant, double intensity, double fuseTimeMult = 1.0,
                                         ChaosMotion? motionOverride = null, double effectIntensity = 1.0,
                                         double sizeScale = 1.0)
    {
        // Size: random across the band, nudged upward by run intensity. Strength is keyed to
        // the CLASSIC (unscaled) size so visual sizing never weakens payloads or scoring.
        double t = Math.Clamp(_rng.NextDouble() * 0.7 + intensity * 0.45, 0, 1);
        double size = variant.MinSize + (variant.MaxSize - variant.MinSize) * t;
        int strength = (int)Math.Round(Math.Clamp((size - SizeMinGlobal) / (SizeMaxGlobal - SizeMinGlobal), 0, 1) * 100);
        double visual = GLOBAL_SIZE_SCALE * Math.Max(0.5, sizeScale);
        if (variant.Id is "video" or "htlink") visual *= GIANT_SIZE_SCALE;
        size *= visual;

        EffectPayload payload = variant.PayloadKind == EffectBubblePayloadKind.Overlay && variant.OverlayKind != null
            ? new OverlayPayload(variant.OverlayKind)
            : EffectPayloadFactory.Build(variant.PayloadKind);
        payload.Strength = (int)Math.Clamp(strength * effectIntensity, 0, 100);

        // The freeze bubble has no fuse, so it must use a motion that exits the screen (RoamBounce
        // never leaves → an uncaught one would live forever). Force FloatUp if an override picked roam.
        var motion = motionOverride ?? variant.Motion;
        bool isFreezeVariant = variant.PayloadKind == EffectBubblePayloadKind.BambiFreeze;
        if (isFreezeVariant && motion == ChaosMotion.RoamBounce) motion = ChaosMotion.FloatUp;

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
            IsFreeze = isFreezeVariant,
            FuseMs = fuse,
            Motion = motion,
        };
    }
}
