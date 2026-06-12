using System;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// First-contact verb hints: a small text pill floats under every chaos bubble whose
/// interaction the player hasn't performed correctly yet ("hold to snap", "click to pop",
/// "do not touch"...). The FIRST correct play of that verb marks it learned in chaos_meta —
/// the hint vanishes from every bubble on screen and never shows again. Pure onboarding:
/// no scoring, no gameplay state, only display + a persisted learned-set.
/// </summary>
public static class ChaosBubbleHints
{
    /// <summary>The learned-set key for a spec's interaction archetype. Per-VARIANT for the
    /// regular pool (every bubble kind gets its own first text); special flags first. Null =
    /// no hint (ambient bubbles, sweepers — nothing for the player to learn).</summary>
    public static string? KeyFor(EffectBubbleSpec? spec)
    {
        if (spec == null) return null;
        if (spec.IsSweeper) return null;                          // uncatchable — nothing to teach
        if (spec.IsDarter) return "rabbit";
        if (spec.IsFreeze) return "freeze";
        if (spec.IsTease) return "tease";
        if (spec.IsBrittle) return "brittle";
        if (spec.IsEscort || spec.IsChaperoneLive) return "chaperone";
        if (spec.IsEcho) return "echo";
        if (spec.IsBoundHalf) return "bound";
        if (spec.IsGolden) return "golden";
        if (spec.IsHeart) return "heart";
        if (spec.IsDroplet) return "droplet";
        if (spec.IsPrism) return "prism";
        if (spec.PayMult >= 2.0) return "heavy";                  // Heavy Drop (pays x3)
        return (spec.IsLive ? "live:" : "treat:") + spec.VariantId;
    }

    /// <summary>Hint text for a spec (lowercase, lexicon voice, SHORT). The chaperone pair
    /// shares one learned-key but each half teaches its own side of the lesson.</summary>
    public static string TextFor(EffectBubbleSpec? spec)
    {
        if (spec == null) return "";
        if (spec.IsChaperoneLive) return "pop my escort first";
        if (spec.IsEscort) return "pop me first";
        string? key = KeyFor(spec);
        if (key == null) return "";
        if (key.StartsWith("live:")) return "hold to snap";
        if (key.StartsWith("treat:")) return "click to pop";
        return key switch
        {
            "rabbit" => "click to catch",
            "freeze" => "click to freeze",
            "tease" => "don't touch. let it leave",
            "brittle" => "glass. dodge it",
            "echo" => "hold fully or it splits",
            "bound" => "hold both. fast",
            "golden" => "pop for gold",
            "heart" => "click. +1 resistance",
            "droplet" => "catch the gold",
            "prism" => "pop. pays 10x",
            "heavy" => "click. pays x3",
            _ => ""
        };
    }

    public static bool IsLearned(string key)
    {
        try { return ChaosMeta.State.BubbleHintsLearned.Contains(key); }
        catch { return true; }   // any meta hiccup → fail toward NO hint clutter
    }

    /// <summary>The verb was just performed correctly: persist it and strip the hint from
    /// every bubble currently wearing it. Safe to call repeatedly / with null.</summary>
    public static void MarkLearned(string? key)
    {
        if (string.IsNullOrEmpty(key)) return;
        try
        {
            if (!ChaosMeta.State.BubbleHintsLearned.Add(key)) return;
            ChaosMeta.Save();
            App.Bubbles?.HideChaosHints(key);
            App.Logger?.Information("Chaos: bubble verb hint learned: {Key}", key);
        }
        catch { }
    }
}
