using System;
using System.Collections.Generic;
using global::Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Seeds the Avalonia Chaos stub catalogues (lifetime boons, upgrades, draftable mantras,
/// bubble variants) so the Hub shelves and the run-time draft UI are populated. This is a
/// stand-in until the WPF catalogue classes are moved into CCP.Core and shared.
/// </summary>
public static class AvaloniaChaosCatalogs
{
    private static bool _initialized;

    public static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        SeedLifetimeBoons();
        SeedUpgrades();
        SeedBoonPool();
        SeedBubbleVariants();
    }

    private static void SeedLifetimeBoons()
    {
        void Add(ChaosLifetimeBoon b)
        {
            if (ChaosLifetimeBoons.All.Exists(x => x.Id == b.Id)) return;
            ChaosLifetimeBoons.All.Add(b);
        }

        // ---- Toys (active-use skills) ----
        Add(new ChaosLifetimeBoon
        {
            Id = "vibe_popping", Category = ChaosBoonCategory.Skill, RankFloor = ChaosRank.Slipping,
            Name = "VibePopping", Glyph = "🔸",
            Desc = "press for a 3/4/5/5s buzz by level. while it buzzes, hold left or right mouse and sweep: everything you brush over pops instantly, and live ones snap clean for full pay. 20s cooldown.",
            Flavor = "you don't have to aim. just let the hand wander.",
            UnlockCost = 400, MaxLevel = 4, ValueLabel = "{0:0}s buzz",
            IsActiveUse = true, UseCooldownSec = 20,
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "freeze_trigger", Category = ChaosBoonCategory.Skill, RankFloor = ChaosRank.Slipping,
            Name = "Freeze Trigger", Glyph = "❄",
            Desc = "press to freeze the whole field for 3.5s, exactly like a caught freeze bubble. 1/2/3/3 uses per descent, and holds channeled while frozen spend no focus.",
            Flavor = "stillness on demand. she lends it, never gives it.",
            UnlockCost = 500, MaxLevel = 4, ValueLabel = "{0:0} uses",
            IsActiveUse = true, UseCooldownSec = 0,
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "porn_dvd", Category = ChaosBoonCategory.Skill, RankFloor = ChaosRank.Devoted,
            Name = "Porn DVD", Glyph = "📀",
            Desc = "press play: a logo bounces across the screen for 10/15/20/20s by level, popping every treat it touches and snapping every live one. bigger and faster at higher levels. 60s cooldown.",
            Flavor = "it always finds the corner eventually. so will you.",
            UnlockCost = 600, MaxLevel = 4, ValueLabel = "{0:0}s playback",
            IsActiveUse = true, UseCooldownSec = 60,
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "snap_field", Category = ChaosBoonCategory.Skill, RankFloor = ChaosRank.Devoted,
            Name = "Snap Field", Glyph = "✋",
            Desc = "the panic button. every live bubble on screen snaps at once, each paying in full. cooldown 60/45/30s by level.",
            Flavor = "one clean breath and the whole room lets go.",
            UnlockCost = 600, MaxLevel = 3, ValueLabel = "{0:0}s cooldown",
            IsActiveUse = true, UseCooldownSec = 60,
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "rabbit_caller", Category = ChaosBoonCategory.Skill, RankFloor = ChaosRank.Slipping,
            Name = "Rabbit Caller", Glyph = "🐇",
            Desc = "press to arm the whistle, then click anywhere: 1/2/3 white rabbits by level arrive right where you pointed. 45s cooldown.",
            Flavor = "they were always waiting to be called.",
            UnlockCost = 500, MaxLevel = 3, ValueLabel = "{0:0} rabbits",
            IsActiveUse = true, UseCooldownSec = 45,
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "e_stim", Category = ChaosBoonCategory.Skill,
            Name = "E-Stim", Glyph = "⚡",
            Desc = "press to charge your next 3/4/5 clicks by level. a charged pop arcs lightning into up to 3 bubbles within 600px, snapping any live ones. nothing in reach? the charge keeps. 30s cooldown.",
            Flavor = "the current knows exactly where you're tender.",
            UnlockCost = 600, MaxLevel = 3, ValueLabel = "{0:0} charged pops",
            IsActiveUse = true, UseCooldownSec = 30,
        });

        // ---- Accessories (passives) ----
        Add(new ChaosLifetimeBoon
        {
            Id = "surrender", Category = ChaosBoonCategory.Accessory, RankFloor = ChaosRank.Slipping,
            Name = "Surrender", Glyph = "🕯",
            Desc = "every sin you accept adds +0.05/+0.10/+0.15x run multiplier by level, on top of whatever the sin pays.",
            Flavor = "you stopped pretending you'd say no.",
            UnlockCost = 150, MaxLevel = 3, ValueLabel = "+{0:0.00}x per sin",
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "chain_reaction", Category = ChaosBoonCategory.Accessory, RankFloor = ChaosRank.Slipping,
            Name = "Poppers", Glyph = "💨",
            Desc = "a popped bubble bursts outward and pops whatever it overlaps, rippling on through the cluster. burst reach x1.20/1.35/1.60/1.80/2.00 by level.",
            Flavor = "they dilate. everything opens a little wider.",
            UnlockCost = 150, MaxLevel = 5, ValueLabel = "{0:0.00}x reach",
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "blindfold", Category = ChaosBoonCategory.Accessory, RankFloor = ChaosRank.Devoted,
            Name = "Blindfold", Glyph = "🙈",
            Desc = "bubbles dim to 40/32/25% visibility by level. in exchange every pop and snap pays x1.50/x1.75/x2.00.",
            Flavor = "you don't need to see them. you feel where they are.",
            UnlockCost = 300, MaxLevel = 3, ValueLabel = "x{0:0.00} payout",
            CapstoneDesc = "a heartbeat tells you when one is about to go. listen.",
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "last_breath", Category = ChaosBoonCategory.Accessory, RankFloor = ChaosRank.Devoted,
            Name = "Last Breath", Glyph = "⏱",
            Desc = "snap a live bubble with 0.4/0.6/0.8s of trance left by level and it pays x5/x10/x20.",
            Flavor = "the closer the edge, the sweeter she sings.",
            UnlockCost = 250, MaxLevel = 3, ValueLabel = "x{0:0} at the brink",
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "taking_chances", Category = ChaosBoonCategory.Accessory, RankFloor = ChaosRank.Devoted,
            Name = "Taking Chances", Glyph = "🎲",
            Desc = "every pop flips a coin: x2 or x0.5 pay, with 50/55/60% odds on the double by level. also grants 1/2/3 mantra draft rerolls per descent.",
            Flavor = "heads she wins, tails you do. you keep forgetting which is which.",
            UnlockCost = 250, MaxLevel = 3, ValueLabel = "{0:0} rerolls",
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "the_pull", Category = ChaosBoonCategory.Accessory, RankFloor = ChaosRank.Slipping,
            Name = "The Pull", Glyph = "🧭",
            Desc = "bubbles drift toward your cursor, pull strength 0.12/0.22/0.32/0.44/0.58 by level, and white rabbits fly straight at you instead of past you.",
            Flavor = "you're not chasing them. be honest.",
            UnlockCost = 200, MaxLevel = 5, ValueLabel = "{0:0.00} pull",
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "the_spanker", Category = ChaosBoonCategory.Accessory, RankFloor = ChaosRank.Slipping,
            Name = "The Spanker", Glyph = "🏓",
            Desc = "rabbits can't be caught anymore. smack one and it turns, swells x1.20/1.45/1.70 by level, gains 18% speed per smack, and pops everything in its path.",
            Flavor = "good rabbits get a pat. yours get the paddle.",
            UnlockCost = 300, MaxLevel = 3, ValueLabel = "x{0:0.00} swell",
            CapstoneDesc = "the bouncing texts answer to you too — smack them to turn them.",
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "intrusive_thoughts", Category = ChaosBoonCategory.Accessory, RankFloor = ChaosRank.Slipping,
            Name = "Intrusive Thoughts", Glyph = "💭",
            Desc = "every 5 seconds a stray thought races across the screen for 3/4/5s by level, popping whatever it touches.",
            Flavor = "they aren't yours. they pop things anyway.",
            UnlockCost = 250, MaxLevel = 3, ValueLabel = "{0:0}s thoughts",
            CapstoneDesc = "a thought that brushes a rabbit splits in two. and those split too. (max 8, +2s)",
        });

        // ---- Utility (charms) ----
        Add(new ChaosLifetimeBoon
        {
            Id = "rabbits_foot", Category = ChaosBoonCategory.Utility, RankFloor = ChaosRank.Slipping,
            Name = "Rabbit's Foot", Glyph = "🍀",
            Desc = "lucky golden bubbles surface on 1.0/1.5/2.0/2.0% of spawns by level and pay 12-24/14-28/16-32/20-40 gold on the spot.",
            Flavor = "it wasn't lucky for the rabbit.",
            UnlockCost = 200, MaxLevel = 4, ValueLabel = "{0:0.0%} lucky",
            CapstoneDesc = "the gold doubles — twenty to forty a bubble.",
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "drip_feed", Category = ChaosBoonCategory.Utility, RankFloor = ChaosRank.Devoted,
            Name = "Drip Feed", Glyph = "💧",
            Desc = "every treat popped and every trance snapped banks +1/+2/+3/+4 drops by level — up to 60/90/120/150 a descent — collected when you surface.",
            Flavor = "drop by drop. that's how anything fills.",
            UnlockCost = 250, MaxLevel = 4, ValueLabel = "+{0:0} a pop",
            CapstoneDesc = "the hole tips you 10% extra on everything gathered when you surface.",
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "blank_eyes", Category = ChaosBoonCategory.Utility,
            Name = "Blank Eyes", Glyph = "👁",
            Desc = "every pop floats its true payout on screen, multipliers and coin flips included.",
            Flavor = "glaze over. let the numbers do the looking.",
            UnlockCost = 120, MaxLevel = 1, ValueLabel = "on",
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "breast_enlargement", Category = ChaosBoonCategory.Utility,
            Name = "Breast Enlargement", Glyph = "🎈",
            Desc = "every effect bubble runs +5/+10/+15/+25% bigger by level. pay is unchanged, they're simply easier to touch.",
            Flavor = "fuller. rounder. harder to ignore.",
            UnlockCost = 120, MaxLevel = 4, ValueLabel = "+{0:0}% size",
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "slow_recovery", Category = ChaosBoonCategory.Utility, RankFloor = ChaosRank.Slipping,
            Name = "Slow Recovery", Glyph = "♻",
            Desc = "every 60/50/40/30 pops by level knits back one point of resistance, up to where you started.",
            Flavor = "it grows back slow. everything down here does.",
            UnlockCost = 200, MaxLevel = 4, ValueLabel = "{0:0} pops a point",
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "start_resistance", Category = ChaosBoonCategory.Utility,
            Name = "It would never work on me...", Glyph = "♥",
            Desc = "you descend wearing +1/+2/+3 resistance by level. without it you start bare, at zero.",
            Flavor = "famous last words.",
            UnlockCost = 100, MaxLevel = 3, ValueLabel = "+{0:0} resistance",
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "collar", Category = ChaosBoonCategory.Utility, RankFloor = ChaosRank.Slipping,
            Name = "Collar", Glyph = "📿",
            Desc = "when a trigger slips past your resistance, the collar holds your streak: 1/2/3 saves per descent.",
            Flavor = "the streak was never yours to drop.",
            UnlockCost = 200, MaxLevel = 3, ValueLabel = "{0:0} saves",
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "golden_touch", Category = ChaosBoonCategory.Utility, RankFloor = ChaosRank.Slipping,
            Name = "Golden Touch", Glyph = "✨",
            Desc = "your run multiplier starts at x1.10/x1.20/x1.30/x1.45 by level, and calm pops pay from a 45/50/55/60% baseline instead of 40%.",
            Flavor = "everything you touch comes back heavier.",
            UnlockCost = 150, MaxLevel = 4, ValueLabel = "x{0:0.00} baseline",
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "slowburner", Category = ChaosBoonCategory.Utility, RankFloor = ChaosRank.Slipping,
            Name = "Slowburner", Glyph = "🐌",
            Desc = "live bubbles hold their trance 10/20/30/40% longer by level before they trigger.",
            Flavor = "no rush. she likes you slow.",
            UnlockCost = 150, MaxLevel = 4, ValueLabel = "{0:0}% slower",
            CapstoneDesc = "snapping one in its final 1.5 seconds pays triple.",
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "pocket_watch", Category = ChaosBoonCategory.Utility, RankFloor = ChaosRank.Slipping,
            Name = "Pocket Watch", Glyph = "🕰",
            Desc = "the loop countdown hangs top right and the sidebar shows the descent clock. without it, time down here stays a mystery.",
            Flavor = "borrowed from the white rabbit. he knows where you live.",
            UnlockCost = 150, MaxLevel = 1, ValueLabel = "on",
        });
        Add(new ChaosLifetimeBoon
        {
            Id = "skipping_stone", Category = ChaosBoonCategory.Utility, RankFloor = ChaosRank.Devoted,
            Name = "Skipping Stone", Glyph = "🪨",
            Desc = "your ripple gathers in 13/11/9/8 seconds by level (15 bare-handed), and each level sends a wider, slower wave.",
            Flavor = "flat stone, still water. she taught you the wrist for it.",
            UnlockCost = 220, MaxLevel = 4, ValueLabel = "{0:0}s gather",
            CapstoneDesc = "the stone skips — every cast sends three waves, a second apart.",
        });
    }

    private static void SeedUpgrades()
    {
        void Add(ChaosUpgrade u)
        {
            if (ChaosUpgrades.All.Exists(x => x.Id == u.Id)) return;
            ChaosUpgrades.All.Add(u);
        }

        Add(new ChaosUpgrade { Id = "slow_fuses", Branch = ChaosBranch.Control, Name = "Slower Trance", Cost = 120, Glyph = "⏳",
            Desc = "live bubbles hold their trance 15% longer before they trigger.",
            Flavor = "a little more time to change your mind. you won't." });
        Add(new ChaosUpgrade { Id = "silk_touch", Branch = ChaosBranch.Control, Name = "Silk Touch", Cost = 180, Glyph = "🪶",
            Desc = "bubble hitboxes grow 25%, and a near-miss on a live one still counts as a touch.",
            Flavor = "silk doesn't try. it just lands." });
        Add(new ChaosUpgrade { Id = "popup_notification", Branch = ChaosBranch.Control, Name = "Pop-up Notification", Cost = 160, Glyph = "💖",
            Desc = "once per loop, 60% of the time, a heart drifts down mid-loop. catch it for +1 resistance and +10 focus.",
            Flavor = "you opted in. you always opt in." });
        Add(new ChaosUpgrade { Id = "pendulum_swing", Branch = ChaosBranch.Control, Name = "Pendulum", Cost = 220, Glyph = "🕰",
            Desc = "once per loop, at a random beat, the pendulum swings: 2.5 seconds of slow motion.",
            Flavor = "tick. tock. you looked." });
        Add(new ChaosUpgrade { Id = "draft4", Branch = ChaosBranch.Depth, Name = "4-Mantra Draft", Cost = 200, Glyph = "🃏",
            Desc = "mantra drafts offer four choices instead of three.",
            Flavor = "more ways to say yes." });
        Add(new ChaosUpgrade { Id = "extreme_tier", Branch = ChaosBranch.Depth, Name = "Inescapable Tier", Cost = 350, Glyph = "🌀",
            Desc = "opens the inescapable difficulty in the descent setup.",
            Flavor = "the last door was never locked." });
    }

    private static void SeedBoonPool()
    {
        void Add(ChaosBoon b)
        {
            if (ChaosBoonPool.All.Exists(x => x.Id == b.Id)) return;
            ChaosBoonPool.All.Add(b);
        }

        Add(new ChaosBoon { Id = "golden_touch", Name = "Golden Touch", Desc = "run multiplier starts x1.10 and calm pops pay more.", Rarity = ChaosRarity.Uncommon });
        Add(new ChaosBoon { Id = "slowburner", Name = "Slowburner", Desc = "live bubbles hold their trance 10% longer.", Rarity = ChaosRarity.Common });
        Add(new ChaosBoon { Id = "blank_eyes", Name = "Blank Eyes", Desc = "every pop shows its true payout on screen.", Rarity = ChaosRarity.Common });
        Add(new ChaosBoon { Id = "the_pull", Name = "The Pull", Desc = "bubbles drift toward your cursor.", Rarity = ChaosRarity.Common });
        Add(new ChaosBoon { Id = "chain_reaction", Name = "Poppers", Desc = "popped bubbles burst outward and pop anything they overlap.", Rarity = ChaosRarity.Uncommon });
        Add(new ChaosBoon { Id = "blindfold", Name = "Blindfold", Desc = "bubbles dim, but every pop pays x1.50.", Rarity = ChaosRarity.Rare });
        Add(new ChaosBoon { Id = "taking_chances", Name = "Taking Chances", Desc = "every pop flips a coin: x2 or x0.5 pay.", Rarity = ChaosRarity.Uncommon });
        Add(new ChaosBoon { Id = "intrusive_thoughts", Name = "Intrusive Thoughts", Desc = "a stray thought races across the screen, popping what it touches.", Rarity = ChaosRarity.Common });
        Add(new ChaosBoon { Id = "last_breath", Name = "Last Breath", Desc = "snapping a live bubble in its final moments pays x5.", Rarity = ChaosRarity.Rare });
        Add(new ChaosBoon { Id = "surrender", Name = "Surrender", Desc = "every sin you accept adds +0.05x run multiplier.", IsCurse = true, Rarity = ChaosRarity.Common });
        Add(new ChaosBoon { Id = "drip_feed", Name = "Drip Feed", Desc = "every pop banks a few drops for the surface.", Rarity = ChaosRarity.Common });
    }

    private static void SeedBubbleVariants()
    {
        void Add(ChaosBubbleVariants.Variant v)
        {
            if (ChaosBubbleVariants.All.Exists(x => x.Id == v.Id)) return;
            ChaosBubbleVariants.All.Add(v);
        }

        Add(new ChaosBubbleVariants.Variant { Id = "flash", Name = "Flash", Tint = Color.FromRgb(0xFF, 0xD7, 0x00) });
        Add(new ChaosBubbleVariants.Variant { Id = "subliminal", Name = "Subliminal", Tint = Color.FromRgb(0x9C, 0x5C, 0xFF) });
        Add(new ChaosBubbleVariants.Variant { Id = "pink", Name = "Pink Filter", Tint = Color.FromRgb(0xFF, 0x4D, 0xC4) });
        Add(new ChaosBubbleVariants.Variant { Id = "spiral", Name = "Spiral", Tint = Color.FromRgb(0x7A, 0xE0, 0xFF) });
        Add(new ChaosBubbleVariants.Variant { Id = "braindrain", Name = "BrainDrain", Tint = Color.FromRgb(0xFF, 0x69, 0xB4) });
        Add(new ChaosBubbleVariants.Variant { Id = "bambifreeze", Name = "Bambi Freeze", Tint = Color.FromRgb(0xAA, 0xE8, 0xFF) });
        Add(new ChaosBubbleVariants.Variant { Id = "video", Name = "Video", Tint = Color.FromRgb(0xFF, 0x8A, 0x14) });
        Add(new ChaosBubbleVariants.Variant { Id = "htlink", Name = "Gif Rain", Tint = Color.FromRgb(0xFF, 0xA0, 0x70) });

        ChaosBubbleVariants.Presets.Add(new BubblePreset { Name = "Balanced", VariantIds = new() { "flash", "pink", "spiral", "bambifreeze" } });
        ChaosBubbleVariants.Presets.Add(new BubblePreset { Name = "Tease", VariantIds = new() { "subliminal", "braindrain", "pink", "video" } });
        ChaosBubbleVariants.Presets.Add(new BubblePreset { Name = "Flash-only", VariantIds = new() { "flash", "htlink" } });
    }
}
