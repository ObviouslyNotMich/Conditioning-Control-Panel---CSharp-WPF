using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>The three permanent-upgrade branches.</summary>
public enum ChaosBranch { Control, Greed, Depth }

/// <summary>
/// One permanent, purchasable upgrade. <see cref="Apply"/> mutates a freshly-built
/// <see cref="ChaosRunConfig"/> at run start — owning the upgrade shapes every run.
/// </summary>
public sealed class ChaosUpgrade
{
    public string Id = "";
    public ChaosBranch Branch;
    public string Name = "";
    public int Cost;                            // in Sparks
    public Action<ChaosRunConfig> Apply = _ => { };
    /// <summary>Optional icon path; null falls back to a vector placeholder. Wired in phase 5.</summary>
    public string? IconPath = null;
}

/// <summary>
/// The v1 upgrade catalogue. Costs are constants up top for easy balancing later.
/// Rows tagged "runtime deferred" only set a config field; the consuming runtime
/// (hitbox hit-test, shield regen, detonation-penalty path) lands in a later session,
/// so those fields are inert for now.
/// </summary>
public static class ChaosUpgrades
{
    // ---- cost constants (tunable) ----
    public const int COST_START_SHIELD    = 100;
    public const int COST_SLOW_FUSES      = 120;
    public const int COST_BIGGER_HITBOXES = 80;
    public const int COST_SHIELD_RECHARGE = 200;
    public const int COST_BASE_MULT       = 90;
    public const int COST_GOLDEN_TOUCH    = 130;
    public const int COST_MAGNET          = 150;
    public const int COST_SPARK_GAIN      = 180;
    public const int COST_MAX_BUBBLES     = 110;
    public const int COST_DRAFT4          = 200;
    public const int COST_EXTREME_TIER    = 350;
    public const int COST_TAKE_MORE       = 400;

    public static readonly IReadOnlyList<ChaosUpgrade> All = new List<ChaosUpgrade>
    {
        // ---- Control ----
        new() { Id = "start_shield",    Branch = ChaosBranch.Control, Name = "+1 Start Willpower", Cost = COST_START_SHIELD,
                Apply = c => c.StartingShields += 1 },
        new() { Id = "slow_fuses",      Branch = ChaosBranch.Control, Name = "Slower Trance",    Cost = COST_SLOW_FUSES,
                Apply = c => c.FuseTimeMult *= 1.15 },
        new() { Id = "bigger_hitboxes", Branch = ChaosBranch.Control, Name = "Bigger Hitboxes", Cost = COST_BIGGER_HITBOXES,
                Apply = c => c.HitboxScale = 1.25 },                       // runtime deferred
        new() { Id = "shield_recharge", Branch = ChaosBranch.Control, Name = "Willpower Recharge", Cost = COST_SHIELD_RECHARGE,
                Apply = c => c.ShieldRechargeSeconds = 45 },               // runtime deferred

        // ---- Greed ----
        new() { Id = "base_mult",       Branch = ChaosBranch.Greed,   Name = "Base Mult x1.2",  Cost = COST_BASE_MULT,
                Apply = c => c.BaseMult = 1.2 },
        new() { Id = "golden_touch",    Branch = ChaosBranch.Greed,   Name = "Golden Touch",    Cost = COST_GOLDEN_TOUCH,
                Apply = c => c.GoldenTouchBaseline = true },
        new() { Id = "magnet",          Branch = ChaosBranch.Greed,   Name = "Magnet Radius",   Cost = COST_MAGNET,
                Apply = c => c.MagnetEnabled = true },
        new() { Id = "spark_gain",      Branch = ChaosBranch.Greed,   Name = "+Sparks Gain",    Cost = COST_SPARK_GAIN,
                Apply = c => c.SparkGainMult = 1.2 },

        // ---- Depth ----
        new() { Id = "max_bubbles",     Branch = ChaosBranch.Depth,   Name = "+2 Max Bubbles",  Cost = COST_MAX_BUBBLES,
                Apply = c => c.MaxBubblesBonus += 2 },
        new() { Id = "draft4",          Branch = ChaosBranch.Depth,   Name = "4-Mantra Draft",    Cost = COST_DRAFT4,
                Apply = c => c.DraftChoices = 4 },
        new() { Id = "extreme_tier",    Branch = ChaosBranch.Depth,   Name = "Extreme Tier",    Cost = COST_EXTREME_TIER,
                Apply = _ => { } },                                       // flag stored at purchase time
        new() { Id = "take_more",       Branch = ChaosBranch.Depth,   Name = "Take More",       Cost = COST_TAKE_MORE,
                Apply = c => c.DetonationPenaltyMult = 0.5 },             // runtime deferred
    };

    public static ChaosUpgrade? ById(string id) => All.FirstOrDefault(u => u.Id == id);
}

/// <summary>
/// Static facade over the persistent meta state — the API the future hub UI and the
/// run hooks call. <see cref="State"/> is loaded once at startup via <see cref="Init"/>.
/// </summary>
public static class ChaosMeta
{
    public static ChaosMetaState State { get; private set; } = new();

    public static void Init() => State = ChaosMetaStore.Load();

    /// <summary>Persist the current state (after a mutation made directly on State).</summary>
    public static void Save() => ChaosMetaStore.Save(State);

    // ---- progressive hub unlocks (named thresholds, by lifetime RunsCompleted) ----
    public const int UNLOCK_UPGRADES_RUNS = 1;
    public const int UNLOCK_STATS_RUNS    = 1;
    public const int UNLOCK_CODEX_RUNS    = 1;
    public const int UNLOCK_LOADOUT_RUNS  = 3;

    /// <summary>Rank derived from lifetime <see cref="ChaosMetaState.RunsCompleted"/> (monotonic, simple).</summary>
    public static string Rank => State.RunsCompleted switch
    {
        >= 100 => "Paragon",
        >= 50  => "Adept",
        >= 25  => "Initiate",
        >= 10  => "Novice",
        >= 3   => "Dabbler",
        _      => "Newcomer"
    };

    /// <summary>Equip (or clear, with null) the pre-drafted start boon and persist.</summary>
    public static void EquipStartBoon(string? boonId)
    {
        State.EquippedStartBoon = boonId;
        Save();
    }

    /// <summary>Mark a Codex entry encountered. Persists only on the first sighting (bounded writes).</summary>
    public static void MarkDiscovered(string codexId)
    {
        if (string.IsNullOrEmpty(codexId)) return;
        State.DiscoveredCodexIds ??= new();
        if (State.DiscoveredCodexIds.Add(codexId)) Save();
    }

    public static bool IsDiscovered(string codexId) =>
        State.DiscoveredCodexIds != null && State.DiscoveredCodexIds.Contains(codexId);

    public static bool IsOwned(string id) => State.PurchasedUpgrades.Contains(id);

    public static bool CanAfford(string id)
    {
        var u = ChaosUpgrades.ById(id);
        return u != null && !IsOwned(id) && State.Sparks >= u.Cost;
    }

    /// <summary>
    /// Validate + buy: checks the id exists, isn't already owned, and is affordable;
    /// deducts the cost, records the purchase, sets <see cref="ChaosMetaState.ExtremeUnlocked"/>
    /// for <c>extreme_tier</c>, persists, and returns whether it succeeded.
    /// </summary>
    public static bool TryPurchase(string id)
    {
        var u = ChaosUpgrades.ById(id);
        if (u == null) return false;
        if (State.PurchasedUpgrades.Contains(id)) return false;
        if (State.Sparks < u.Cost) return false;

        State.Sparks -= u.Cost;
        State.PurchasedUpgrades.Add(id);
        if (id == "extreme_tier") State.ExtremeUnlocked = true;
        ChaosMetaStore.Save(State);
        return true;
    }

    /// <summary>Apply every owned upgrade's effect to a freshly-built run config.</summary>
    public static void ApplyTo(ChaosRunConfig config)
    {
        if (config == null) return;
        foreach (var id in State.PurchasedUpgrades)
            ChaosUpgrades.ById(id)?.Apply(config);
    }

    // ---- lifetime boons (Skills / Accessories / Utility): unlock + level + toggle ----
    // Permanent, leveled, toggleable boons bought with Sparks; applied to a run at start when
    // active. They carry no run-mult (utility, not score) and have no active-slot cap yet —
    // both are future balance levers as the shelves fill out.

    /// <summary>Current level of a lifetime boon (0 = locked, >=1 = unlocked at that level).</summary>
    public static int BoonLevel(string id) =>
        State.LifetimeBoonLevels != null && State.LifetimeBoonLevels.TryGetValue(id, out var l) ? l : 0;

    public static bool IsBoonUnlocked(string id) => BoonLevel(id) >= 1;
    public static bool IsBoonActive(string id) =>
        State.ActiveLifetimeBoons != null && State.ActiveLifetimeBoons.Contains(id) && IsBoonUnlocked(id);

    /// <summary>Spark cost to unlock (level 1), or null if the id is unknown/already unlocked.</summary>
    public static int? UnlockCostOf(string id)
    {
        var b = ChaosLifetimeBoons.ById(id);
        return (b == null || IsBoonUnlocked(id)) ? (int?)null : b.UnlockCost;
    }

    /// <summary>Spark cost of the next level-up, or null if locked / already at max.</summary>
    public static int? NextUpgradeCostOf(string id)
    {
        var b = ChaosLifetimeBoons.ById(id);
        if (b == null) return null;
        int lvl = BoonLevel(id);
        if (lvl < 1 || lvl >= b.MaxLevel) return null;
        return (lvl - 1) < b.UpgradeCosts.Length ? b.UpgradeCosts[lvl - 1] : (int?)null;
    }

    public static bool CanAffordUnlock(string id)
    {
        var c = UnlockCostOf(id);
        return c.HasValue && State.Sparks >= c.Value;
    }

    public static bool CanAffordUpgrade(string id)
    {
        var c = NextUpgradeCostOf(id);
        return c.HasValue && State.Sparks >= c.Value;
    }

    /// <summary>Validate + buy level 1: deduct Sparks, record level, auto-activate, persist.</summary>
    public static bool TryUnlockBoon(string id)
    {
        var c = UnlockCostOf(id);
        if (!c.HasValue || State.Sparks < c.Value) return false;
        State.Sparks -= c.Value;
        State.LifetimeBoonLevels ??= new();
        State.LifetimeBoonLevels[id] = 1;
        State.ActiveLifetimeBoons ??= new();
        State.ActiveLifetimeBoons.Add(id);   // a fresh unlock comes on by default
        ChaosMetaStore.Save(State);
        return true;
    }

    /// <summary>Validate + buy the next level: deduct Sparks, bump level (capped at max), persist.</summary>
    public static bool TryUpgradeBoon(string id)
    {
        var b = ChaosLifetimeBoons.ById(id);
        var c = NextUpgradeCostOf(id);
        if (b == null || !c.HasValue || State.Sparks < c.Value) return false;
        State.Sparks -= c.Value;
        State.LifetimeBoonLevels[id] = Math.Min(BoonLevel(id) + 1, b.MaxLevel);
        ChaosMetaStore.Save(State);
        return true;
    }

    /// <summary>Toggle a lifetime boon on/off (no-op unless unlocked). Persists.</summary>
    public static void SetBoonActive(string id, bool active)
    {
        if (active && !IsBoonUnlocked(id)) return;
        State.ActiveLifetimeBoons ??= new();
        bool changed = active ? State.ActiveLifetimeBoons.Add(id) : State.ActiveLifetimeBoons.Remove(id);
        if (changed) ChaosMetaStore.Save(State);
    }

    /// <summary>Apply every active+unlocked lifetime boon (at its current level) to the run state.</summary>
    public static void ApplyLifetimeBoons(ChaosRunState run)
    {
        if (run == null || State.ActiveLifetimeBoons == null) return;
        foreach (var id in State.ActiveLifetimeBoons)
        {
            int lvl = BoonLevel(id);
            var b = ChaosLifetimeBoons.ById(id);
            if (b != null && lvl >= 1) b.Apply(run, b.ValueAt(lvl));
        }
    }

    /// <summary>
    /// Bank Sparks + update lifetime stats at the end of a completed run, then persist.
    /// Formula: <c>round((score/divisor + completionBonus) * SparkGainMult) * difficulty</c>
    /// where the difficulty scalar is folded into both the score part and the bonus.
    /// </summary>
    public static void AwardRunRewards(ChaosRunState run)
    {
        if (run == null) return;

        const double COMPLETION_BONUS_BASE = 35.0;   // bumped 25→35: raises the predictable Spark floor
        const double SPARK_SCORE_DIVISOR = 100.0;
        const int FIRST_SPARK_BONUS = 25;            // one-time "First Spark" on the very first completed run

        double difficultyMult = run.Config.DifficultyMult;
        double completionBonus = COMPLETION_BONUS_BASE * difficultyMult;
        double scorePart = run.Score / SPARK_SCORE_DIVISOR * difficultyMult;
        int sparks = (int)Math.Round((scorePart + completionBonus) * run.Config.SparkGainMult);

        // One-time cold-start kindness: +25 the first time RunsCompleted goes 0→1 (guarded so it only ever applies once).
        if (State.RunsCompleted == 0) sparks += FIRST_SPARK_BONUS;

        State.Sparks += Math.Max(0, sparks);
        State.RunsCompleted += 1;
        State.BestScore = Math.Max(State.BestScore, (long)run.Score);
        State.BestCombo = Math.Max(State.BestCombo, run.BestCombo);
        State.TotalDefused += run.Defused;
        ChaosMetaStore.Save(State);
    }
}
