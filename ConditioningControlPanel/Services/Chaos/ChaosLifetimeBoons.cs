using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>The three lifetime-boon shelves shown as Hub tabs (we lean on the tropes).</summary>
public enum ChaosBoonCategory { Skill, Accessory, Utility }

/// <summary>
/// A permanent, leveled, toggleable meta-progression boon — distinct from the in-run
/// drafted boons (<see cref="ChaosBoonPool"/>, ephemeral), the single
/// <c>EquippedStartBoon</c>, and the always-on <see cref="ChaosUpgrade"/>s. Unlocked +
/// upgraded with Sparks, switched on/off in the Hub, and applied to the live
/// <see cref="ChaosRunState"/> at run start when active. Data-driven: the shelf grows by
/// adding records, not code paths. Art (if present) resolves via
/// <c>ChaosArt.Resolve("boons", Id)</c>; until then the <see cref="Glyph"/> is the placeholder.
/// </summary>
public sealed class ChaosLifetimeBoon
{
    public string Id = "";
    public ChaosBoonCategory Category;
    public string Name = "";
    public string Desc = "";
    public string Glyph = "◈";                       // placeholder icon char until real art ships
    public int UnlockCost;                            // Sparks for level 1
    public int[] UpgradeCosts = Array.Empty<int>();   // Sparks for levels 2..MaxLevel (len = MaxLevel - 1)
    public double[] LevelValues = Array.Empty<double>(); // the boon's value at each level ([level - 1])
    public string ValueLabel = "{0}";                 // how to render the level value, e.g. "{0:0.00}x reach"
    /// <summary>Max-rank bonus effect blurb shown on the Hub card (dim until maxed). Empty = no capstone.</summary>
    public string CapstoneDesc = "";
    /// <summary>Active-use skill: fires on its keybind / HUD button mid-descent instead of acting passively.</summary>
    public bool IsActiveUse;
    /// <summary>Seconds between uses for active-use skills. 0 = charge-based (LevelValues are uses per descent).</summary>
    public double UseCooldownSec;
    public Action<ChaosRunState, double> Apply = (_, __) => { };

    /// <summary>Highest level this boon can reach (length of <see cref="LevelValues"/>).</summary>
    public int MaxLevel => LevelValues.Length;

    /// <summary>The value at a 1-indexed <paramref name="level"/>, clamped into range.</summary>
    public double ValueAt(int level) =>
        LevelValues.Length == 0 ? 0 : LevelValues[Math.Clamp(level, 1, LevelValues.Length) - 1];
}

/// <summary>The v1 lifetime-boon catalogue. Costs/levels live here for easy balancing.</summary>
public static class ChaosLifetimeBoons
{
    public static readonly IReadOnlyList<ChaosLifetimeBoon> All = new List<ChaosLifetimeBoon>
    {
        // ---- Skills (active-use: you press them mid-descent) ----
        new()
        {
            Id = "vibe_popping", Category = ChaosBoonCategory.Skill, Name = "VibePopping", Glyph = "🔸",
            Desc = "press it, then hold the button and sweep. everything you pass over pops itself, even the live ones.",
            UnlockCost = 200,
            UpgradeCosts = new[] { 250, 400, 600 },               // levels 2..4
            LevelValues  = new[] { 3.0, 4, 5, 5 },                // buzz duration (seconds)
            ValueLabel = "{0:0}s buzz",
            CapstoneDesc = "no need to hold. while it buzzes, hovering alone pops.",
            IsActiveUse = true, UseCooldownSec = 20,
            Apply = (s, v) => s.ToyPower["vibe_popping"] = v,
        },
        new()
        {
            Id = "freeze_trigger", Category = ChaosBoonCategory.Skill, Name = "Freeze Trigger", Glyph = "❄",
            Desc = "press it and everything holds still, exactly like a caught freeze bubble. limited uses per descent.",
            UnlockCost = 250,
            UpgradeCosts = new[] { 400, 650, 900 },               // levels 2..4
            LevelValues  = new[] { 1.0, 2, 3, 3 },                // uses per descent
            ValueLabel = "{0:0} uses",
            CapstoneDesc = "each freeze also snaps every live bubble on screen.",
            IsActiveUse = true, UseCooldownSec = 0,               // charge-based
            Apply = (s, v) => s.ToyPower["freeze_trigger"] = v,
        },
        new()
        {
            Id = "porn_dvd", Category = ChaosBoonCategory.Skill, Name = "Porn DVD", Glyph = "📀",
            Desc = "press play. the logo drifts across the screen popping every treat it touches and snapping the rest.",
            UnlockCost = 300,
            UpgradeCosts = new[] { 450, 700, 1000 },              // levels 2..4
            LevelValues  = new[] { 10.0, 15, 20, 20 },            // flight time (seconds); speed+size also scale with rank
            ValueLabel = "{0:0}s playback",
            CapstoneDesc = "two screens.",
            IsActiveUse = true, UseCooldownSec = 60,
            Apply = (s, v) => s.ToyPower["porn_dvd"] = v,
        },
        new()
        {
            Id = "snap_field", Category = ChaosBoonCategory.Skill, Name = "Snap Field", Glyph = "✋",
            Desc = "one clean snap and every live bubble on screen lets go at once. the panic button.",
            UnlockCost = 300,
            UpgradeCosts = new[] { 400, 600 },                    // levels 2..3
            LevelValues  = new[] { 60.0, 45, 30 },                // cooldown seconds by level
            ValueLabel = "{0:0}s cooldown",
            CapstoneDesc = "the snap clears EVERYTHING — every bubble on screen goes.",
            IsActiveUse = true, UseCooldownSec = 60,              // real cooldown comes from the level value
            Apply = (s, v) => s.ToyPower["snap_field"] = v,
        },
        new()
        {
            Id = "rabbit_caller", Category = ChaosBoonCategory.Skill, Name = "Rabbit Caller", Glyph = "🐇",
            Desc = "whistle, then point — your cursor glows, and the rabbits arrive wherever you click next. more of them the deeper your bond.",
            UnlockCost = 250,
            UpgradeCosts = new[] { 350, 550 },                    // levels 2..3
            LevelValues  = new[] { 1.0, 2, 3 },                   // rabbits per whistle
            ValueLabel = "{0:0} rabbits",
            CapstoneDesc = "each whistle also calls a storm — eight more rabbits over the next ten seconds.",
            IsActiveUse = true, UseCooldownSec = 45,
            Apply = (s, v) => s.ToyPower["rabbit_caller"] = v,
        },
        new()
        {
            Id = "e_stim", Category = ChaosBoonCategory.Skill, Name = "E-Stim", Glyph = "⚡",
            Desc = "press it to charge the current. your next clicks on good bubbles discharge — lightning arcs into everything close enough.",
            UnlockCost = 300,
            UpgradeCosts = new[] { 450, 650 },                    // levels 2..3
            LevelValues  = new[] { 3.0, 4, 5 },                   // charged clicks per press
            ValueLabel = "{0:0} charged pops",
            CapstoneDesc = "charged pops chain-react — the current leaps on through every bubble close enough, and onward.",
            IsActiveUse = true, UseCooldownSec = 30,
            Apply = (s, v) => s.ToyPower["e_stim"] = v,
        },

        // ---- Accessories (passives that shape the run) ----
        // breast_enlargement moved to Utility 2026-06-10 (it reads as a trained habit, not a
        // pocketed accessory) — same id, levels carry over; Utility pockets are uncapped.
        new()
        {
            Id = "surrender", Category = ChaosBoonCategory.Accessory, Name = "Surrender", Glyph = "🕯",
            Desc = "you stopped pretending you'd say no. each accepted sin adds extra run multiplier.",
            UnlockCost = 150,
            UpgradeCosts = new[] { 250, 450 },                    // levels 2..3
            LevelValues  = new[] { 0.05, 0.10, 0.15 },            // extra BoonMult per accepted sin
            ValueLabel = "+{0:0.00}x per sin",
            CapstoneDesc = "every draft offers a sin, saying yes restores +1 resistance, and the first sin you embrace loses its sting entirely.",
            Apply = (s, v) => s.SinExtraMult = v,
        },
        // muscle_memory + magic_wand retired 2026-06-10 (pure stat passives, both duplicated by
        // habits — Slow Recovery / Soft Focus). ChaosMeta.Init refunds owners; never reuse the ids.
        // tunnel_vision + collar + pendulum moved to HABITS 2026-06-10 (they read as passives, not
        // accessories). Boon levels are scrubbed at load (no refund — never released); the same id
        // strings live on as ChaosUpgrade habits, which is fine: different stores.

        new()
        {
            // Display renamed Inflatable Plug → Poppers (2026-06-10); id is save-persisted, never change it.
            Id = "chain_reaction", Category = ChaosBoonCategory.Accessory, Name = "Poppers", Glyph = "💨",
            Desc = "they dilate. bubbles swell wider open and pop whatever they touch.",
            UnlockCost = 150,
            UpgradeCosts = new[] { 120, 160, 220, 300 },          // levels 2..5
            LevelValues  = new[] { 1.2, 1.35, 1.6, 1.8, 2.0 },    // burst reach multiplier per level
            ValueLabel = "{0:0.00}x reach",
            Apply = (s, v) => s.ChainReactionReach = v,
        },
        new()
        {
            Id = "blindfold", Category = ChaosBoonCategory.Accessory, Name = "Blindfold", Glyph = "🙈",
            Desc = "you don't need to see them. bubbles fade to a whisper, but every pop pays more.",
            UnlockCost = 300,
            UpgradeCosts = new[] { 450, 700 },                    // levels 2..3
            LevelValues  = new[] { 1.5, 1.75, 2.0 },              // payout multiplier
            ValueLabel = "x{0:0.00} payout",
            CapstoneDesc = "a heartbeat tells you when one is about to go. listen.",
            Apply = (s, v) =>
            {
                s.BlindfoldPayMult = v;
                s.BlindfoldActive = true;
                // The whisper deepens with the level: x1.5 → 40%, x1.75 → 32%, x2.0 → 25%.
                s.BlindfoldOpacity = v >= 2.0 ? 0.25 : v >= 1.75 ? 0.32 : 0.40;
            },
        },
        new()
        {
            Id = "last_breath", Category = ChaosBoonCategory.Accessory, Name = "Last Breath", Glyph = "⏱",
            Desc = "snatch a live one at the very brink and the hole pays you fortunes for the thrill.",
            UnlockCost = 250,
            UpgradeCosts = new[] { 350, 550 },                    // levels 2..3
            LevelValues  = new[] { 5.0, 10, 20 },                 // payout mult; the brink window widens with it (0.4/0.6/0.8s)
            ValueLabel = "x{0:0} at the brink",
            Apply = (s, v) =>
            {
                s.LastBreathPayMult = v;
                s.LastBreathWindowSec = v >= 20 ? 0.8 : v >= 10 ? 0.6 : 0.4;
            },
        },
        new()
        {
            Id = "taking_chances", Category = ChaosBoonCategory.Accessory, Name = "Taking Chances", Glyph = "🎲",
            Desc = "every pop is a coin flip — half pay or double pay. and you can tempt fate again at the draft table.",
            UnlockCost = 250,
            UpgradeCosts = new[] { 300, 500 },                    // levels 2..3
            LevelValues  = new[] { 1.0, 2, 3 },                   // rerolls per descent; the coin also tilts: 50/50 → 45/55 → 40/60
            ValueLabel = "{0:0} rerolls",
            Apply = (s, v) =>
            {
                s.RerollsLeft = (int)v;
                s.ChanceDoubleOdds = 0.50 + 0.05 * (Math.Clamp(v, 1, 3) - 1);   // P(double pay)
            },
        },
        new()
        {
            Id = "the_pull", Category = ChaosBoonCategory.Accessory, Name = "The Pull", Glyph = "🧭",
            Desc = "the hole leans toward you. bubbles drift to your cursor, and the rabbits fly straight at you.",
            UnlockCost = 200,
            UpgradeCosts = new[] { 200, 300, 450, 650 },          // levels 2..5
            LevelValues  = new[] { 0.12, 0.22, 0.32, 0.44, 0.58 },// drift bias (DIPs/frame); rabbits home at all levels
            ValueLabel = "{0:0.00} pull",
            Apply = (s, v) => s.CursorPullStrength = v,
        },
        new()
        {
            Id = "the_spanker", Category = ChaosBoonCategory.Accessory, Name = "The Spanker", Glyph = "🏓",
            Desc = "rabbits aren't for catching anymore. smack one and it turns, swells, and pops everything in its path. you give up the slow.",
            UnlockCost = 300,
            UpgradeCosts = new[] { 450, 700 },                    // levels 2..3
            LevelValues  = new[] { 1.20, 1.45, 1.70 },            // one-time swell on the first smack
            ValueLabel = "x{0:0.00} swell",
            CapstoneDesc = "the bouncing texts answer to you too — smack them to turn them.",
            Apply = (s, v) => { s.SpankerActive = true; s.SpankGrowFactor = v; },
        },
        new()
        {
            Id = "intrusive_thoughts", Category = ChaosBoonCategory.Accessory, Name = "Intrusive Thoughts", Glyph = "💭",
            Desc = "every few seconds a thought you didn't ask for races across the screen, popping whatever it touches.",
            UnlockCost = 250,
            UpgradeCosts = new[] { 350, 550 },                    // levels 2..3
            LevelValues  = new[] { 3.0, 4, 5 },                   // seconds each thought lives (one spawns every 5s)
            ValueLabel = "{0:0}s thoughts",
            CapstoneDesc = "a thought that brushes a rabbit splits in two. and those split too. (max 8, +2s)",
            Apply = (s, v) => s.IntrusiveThoughtsSec = v,
        },

        // ---- Utility (charms — quiet, always-on trinkets; pockets are uncapped) ----
        new()
        {
            Id = "rabbits_foot", Category = ChaosBoonCategory.Utility, Name = "Rabbit's Foot", Glyph = "🍀",
            Desc = "a charm against bad odds. lucky golden bubbles surface more often and pay richer — real gold, on the spot.",
            UnlockCost = 200,
            UpgradeCosts = new[] { 350, 600, 900 },               // levels 2..4
            LevelValues  = new[] { 0.010, 0.015, 0.020, 0.020 },  // golden-bubble chance per spawn (base 0.5% unworn);
                                                                  // the gold scales by level too (see GoldenPayRange)
            ValueLabel = "{0:0.0%} lucky",
            CapstoneDesc = "the gold doubles — twenty to forty a bubble.",
            Apply = (s, v) => s.GoldenChance = v,
        },
        new()
        {
            Id = "drip_feed", Category = ChaosBoonCategory.Utility, Name = "Drip Feed", Glyph = "💧",
            Desc = "every treat popped and trance snapped drips drops straight into your pocket.",
            UnlockCost = 250,
            UpgradeCosts = new[] { 400, 650, 1000 },              // levels 2..4
            LevelValues  = new[] { 5.0, 10, 15, 20 },             // drops banked per pop (defuses + treats)
            ValueLabel = "+{0:0} a pop",
            CapstoneDesc = "the hole tips you 10% extra on everything gathered when you surface.",
            Apply = (s, v) => s.DropPerPop = (int)v,
        },
        new()
        {
            Id = "blank_eyes", Category = ChaosBoonCategory.Utility, Name = "Blank Eyes", Glyph = "👁",
            Desc = "your eyes glaze and the numbers float up — every pop whispers what it paid.",
            UnlockCost = 120,
            LevelValues  = new[] { 1.0 },                          // single-rank QoL toggle
            ValueLabel = "on",
            Apply = (s, _) => s.ShowPopScores = true,
        },
        new()
        {
            Id = "breast_enlargement", Category = ChaosBoonCategory.Utility, Name = "Breast Enlargement", Glyph = "🎈",
            Desc = "they swell. every bubble on the field runs fuller, rounder, easier to catch.",
            UnlockCost = 120,
            UpgradeCosts = new[] { 180, 260, 380 },               // levels 2..4
            LevelValues  = new[] { 5.0, 10, 15, 25 },             // % size on every variant bubble
            ValueLabel = "+{0:0}% size",
            Apply = (s, v) => s.BubbleScale = 1.0 + v / 100.0,
        },
        new()
        {
            // Replaces the single-rank shield_recharge habit (retired 2026-06-10): regen is now
            // EARNED — pops, not seconds. The threshold tightens per level.
            Id = "slow_recovery", Category = ChaosBoonCategory.Utility, Name = "Slow Recovery", Glyph = "♻",
            Desc = "your resistance knits itself back together — pop enough bubbles and a point regrows.",
            UnlockCost = 200,
            UpgradeCosts = new[] { 300, 450, 650 },               // levels 2..4
            LevelValues  = new[] { 60.0, 50, 40, 30 },            // pops per regrown resistance point
            ValueLabel = "{0:0} pops a point",
            Apply = (s, v) => s.ShieldRegenPops = (int)v,
        },
        new()
        {
            // Replaces the +1 Start Resistance habit (retired 2026-06-10). Base resistance is
            // now ZERO — this charm is the only way to descend wearing any.
            Id = "start_resistance", Category = ChaosBoonCategory.Utility, Name = "It would never work on me...", Glyph = "♥",
            Desc = "famous last words. you descend wearing resistance you swore you'd never need.",
            UnlockCost = 100,
            UpgradeCosts = new[] { 200, 350 },                    // levels 2..3
            LevelValues  = new[] { 1.0, 2, 3 },                   // resistance points at run start
            ValueLabel = "+{0:0} resistance",
            // Shields land before BeginRun captures StartShields (the regen cap), and the config
            // bump keeps the HUD's hollow-heart row sized to what you descended with.
            Apply = (s, v) => { s.Shields += (int)v; s.Config.StartingShields += (int)v; },
        },
        new()
        {
            // Reborn from the single-rank Collar habit (retired 2026-06-10): saves now level.
            Id = "collar", Category = ChaosBoonCategory.Utility, Name = "Collar", Glyph = "📿",
            Desc = "the streak was never yours to drop. when a trigger fires past your resistance, the collar holds it for you.",
            UnlockCost = 200,
            UpgradeCosts = new[] { 300, 450 },                    // levels 2..3
            LevelValues  = new[] { 1.0, 2, 3 },                   // streak saves per descent
            ValueLabel = "{0:0} saves",
            Apply = (s, v) => s.CollarSaves = (int)v,
        },
        new()
        {
            // Deeper Pull (base_mult) + Golden Touch merged 2026-06-10: one charm, both halves —
            // the score baseline deepens AND calm pops carry their bonus, scaling together.
            Id = "golden_touch", Category = ChaosBoonCategory.Utility, Name = "Golden Touch", Glyph = "✨",
            Desc = "everything you touch pays deeper — every pop from a richer baseline, and calm pops carry a bonus of their own.",
            UnlockCost = 150,
            UpgradeCosts = new[] { 250, 400, 600 },               // levels 2..4
            LevelValues  = new[] { 1.1, 1.2, 1.3, 1.45 },         // BaseMult; calm-pop baseline scales with it
            ValueLabel = "x{0:0.00} baseline",
            Apply = (s, v) =>
            {
                s.Config.BaseMult = v;   // state.BaseMult reads through to Config — safe post-ctor
                // The calm-pop (benign) baseline climbs with the level: 0.45 → 0.50 → 0.55 → 0.60 (unworn 0.40).
                s.BenignBaseline = v >= 1.45 ? 0.60 : v >= 1.3 ? 0.55 : v >= 1.2 ? 0.50 : 0.45;
            },
        },
        new()
        {
            // Reborn from Take More (retired 2026-06-10) — instead of softening the sting,
            // it stretches the trance so you can beat it.
            Id = "slowburner", Category = ChaosBoonCategory.Utility, Name = "Slowburner", Glyph = "🐌",
            Desc = "the trance burns lazy. live bubbles take their time letting go.",
            UnlockCost = 150,
            UpgradeCosts = new[] { 250, 400, 600 },               // levels 2..4
            LevelValues  = new[] { 10.0, 20, 30, 40 },            // % slower fuse burn
            ValueLabel = "{0:0}% slower",
            CapstoneDesc = "snapping one in its final 1.5 seconds pays triple.",
            Apply = (s, v) => s.FuseTimeMult *= 1.0 + v / 100.0,
        },
        new()
        {
            Id = "pocket_watch", Category = ChaosBoonCategory.Utility, Name = "Pocket Watch", Glyph = "🕰",
            Desc = "the white rabbit lends you his watch. the wave countdown hangs top-right, and the sidebar shows the run clock — without it, time down here stays a mystery.",
            UnlockCost = 150,
            LevelValues  = new[] { 1.0 },                          // single-rank QoL toggle
            ValueLabel = "on",
            Apply = (s, _) => s.ShowWaveTimer = true,
        },
    };

    /// <summary>Gold paid by a lucky golden bubble at a Rabbit's Foot level (0 = unworn).
    /// Scales per level; the capstone is the doubled base range.</summary>
    public static (int Min, int Max) GoldenPayRange(int level) => level switch
    {
        <= 0 => (10, 20),
        1    => (12, 24),
        2    => (14, 28),
        3    => (16, 32),
        _    => (20, 40),   // level 4: the gold doubles
    };

    public static ChaosLifetimeBoon? ById(string id) => All.FirstOrDefault(b => b.Id == id);

    public static IEnumerable<ChaosLifetimeBoon> InCategory(ChaosBoonCategory cat) =>
        All.Where(b => b.Category == cat);
}
