using System;
using System.Collections.Generic;
using System.Windows.Media;
using SkiaSharp;

namespace ConditioningControlPanel;

/// <summary>
/// The Rabbit Hole's payload-based COLOR LANGUAGE. Every boon belongs to a "family" by what it
/// DOES (electric / pleasure / economy / mind / risk), and that one color drives the draft card,
/// the sidebar/ribbon tile, the effect banner, and the Skia particle FX — so a gold boon throws
/// gold sparks, an electric boon throws cyan arcs, etc. Unmapped ids fall back to the caller's
/// existing color (so neutral mechanics keep their green and nothing regresses).
///
/// To recolor a boon, just move its id between the lists below — this is the single source of truth.
/// </summary>
public static class ChaosBoonColors
{
    public static readonly Color Electric = Color.FromRgb(0x42, 0xDC, 0xE6);  // cyan — E-Stim / lightning / freeze
    public static readonly Color Pleasure = Color.FromRgb(0xFF, 0x4D, 0xC4);  // hot pink — buzz / rabbits / touch
    public static readonly Color Economy  = Color.FromRgb(0xFF, 0xC8, 0x3D);  // gold — drops / luck / payout
    public static readonly Color Mind     = Color.FromRgb(0xB9, 0x8C, 0xFF);  // purple — perception / trance / intrusion
    public static readonly Color Risk     = Color.FromRgb(0xFF, 0x5A, 0x5A);  // red — sins / gambles / last-second
    public static readonly Color Neutral  = Color.FromRgb(0x9C, 0xE8, 0xA0);  // green — pure mechanics

    private static readonly Dictionary<string, Color> _map = new(StringComparer.OrdinalIgnoreCase)
    {
        // ⚡ Electric
        ["e_stim"] = Electric, ["overload"] = Electric, ["tail_plug"] = Electric, ["unleashed"] = Electric,
        ["electrified_rabbits"] = Electric, ["body_buzz"] = Electric, ["aftermath"] = Electric,
        ["freeze_trigger"] = Electric, ["freeze"] = Electric, ["snap_field"] = Electric, ["size_queen"] = Electric,
        // 💗 Pleasure
        ["vibe_popping"] = Pleasure, ["afterglow"] = Pleasure, ["rabbit_caller"] = Pleasure, ["gg_rabbits"] = Pleasure,
        ["the_spanker"] = Pleasure, ["intrusive_thoughts"] = Pleasure, ["casting_couch"] = Pleasure,
        ["porn_dvd"] = Pleasure, ["the_pull"] = Pleasure, ["chain_reaction"] = Pleasure,
        // 💰 Economy
        ["rabbits_foot"] = Economy, ["gold_digger"] = Economy, ["golden_touch"] = Economy, ["drip_feed"] = Economy,
        ["welcome_shower"] = Economy, ["heavy_drop"] = Economy, ["taking_chances"] = Economy,
        // 🧠 Mind
        ["blindfold"] = Mind, ["blank_eyes"] = Mind, ["the_urge"] = Mind, ["slowburner"] = Mind,
        ["bright_colors"] = Mind, ["skipping_stone"] = Mind, ["slow_fuses"] = Mind, ["slow_recovery"] = Mind,
        ["pendulum_swing"] = Mind, ["focus_here"] = Mind, ["breast_enlargement"] = Mind,
        // 🔥 Risk
        ["hair_trigger"] = Risk, ["playing_fire"] = Risk, ["cam_girl"] = Risk, ["double_or_nothing"] = Risk,
        ["last_breath"] = Risk, ["surrender"] = Risk,
    };

    /// <summary>The family color for <paramref name="id"/>, or <paramref name="fallback"/> if unmapped.</summary>
    public static Color ForOrDefault(string? id, Color fallback)
        => (id != null && _map.TryGetValue(id, out var c)) ? c : fallback;

    private static readonly Dictionary<Color, Brush> _brushCache = new();
    /// <summary>A frozen accent brush for <paramref name="id"/>, or <paramref name="fallback"/> if unmapped.</summary>
    public static Brush BrushForOrDefault(string? id, Brush fallback)
    {
        if (id == null || !_map.TryGetValue(id, out var c)) return fallback;
        if (!_brushCache.TryGetValue(c, out var b)) { b = new SolidColorBrush(c); b.Freeze(); _brushCache[c] = b; }
        return b;
    }

    /// <summary>The family color as an SKColor (alpha forced opaque), or <paramref name="fallback"/>.</summary>
    public static SKColor SkForOrDefault(string? id, SKColor fallback)
        => (id != null && _map.TryGetValue(id, out var c)) ? new SKColor(c.R, c.G, c.B) : fallback;

    public static SKColor ToSk(Color c) => new(c.R, c.G, c.B, c.A);
}
