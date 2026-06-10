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
        // ---- Skills ----
        new()
        {
            Id = "chain_reaction", Category = ChaosBoonCategory.Skill, Name = "Chain Reaction", Glyph = "⛓",
            Desc = "Popping a bubble pops the ones its burst touches, rippling through clusters. Each level widens the burst.",
            UnlockCost = 150,
            UpgradeCosts = new[] { 120, 160, 220, 300 },          // levels 2..5
            LevelValues  = new[] { 1.2, 1.35, 1.6, 1.8, 2.0 },    // burst reach multiplier per level
            ValueLabel = "{0:0.00}x reach",
            Apply = (s, v) => s.ChainReactionReach = v,
        },

        // ---- Accessories ----  (none yet — the Hub tab shows an empty-state placeholder)

        // ---- Utility ----      (none yet — the Hub tab shows an empty-state placeholder)
    };

    public static ChaosLifetimeBoon? ById(string id) => All.FirstOrDefault(b => b.Id == id);

    public static IEnumerable<ChaosLifetimeBoon> InCategory(ChaosBoonCategory cat) =>
        All.Where(b => b.Category == cat);
}
