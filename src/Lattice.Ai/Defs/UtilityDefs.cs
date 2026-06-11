using System.Text.Json;
using Lattice.Core.Content;

namespace Lattice.Ai.Defs;

/// <summary>
/// A reusable scoring function (ch05 §5.4): weighted factors, each a formula
/// over the agent's needs (by key), stats, beliefs, and global flags, clamped
/// to 0–1. The score is the weighted average, so it is itself 0–1. Used
/// standalone through the UtilityAtLeast condition (threshold gates — the
/// HZD attack-interest pattern) and as goal-priority input in M4c/M4d.
/// </summary>
public sealed class UtilityEvaluatorDef : Def
{
    public List<Factor> Factors { get; set; } = [];

    public sealed class Factor
    {
        /// <summary>Formula producing 0–1 (clamped after evaluation).</summary>
        public string Formula { get; set; } = "0";

        public double Weight { get; set; } = 1.0;
    }
}

/// <summary>
/// A decaying motive (the Sims pattern, ch05 §5.4): value 1 = satisfied,
/// 0 = desperate; urgency = 1 − value. Decayed every tick by the agent
/// system for agents whose profile declares the need.
/// </summary>
public sealed class NeedDef : Def
{
    /// <summary>Formula identifier (convention: PascalCase key, e.g. "Thirst"; the def ID goes in structural fields).</summary>
    public string Key { get; set; } = "";

    /// <summary>Starting value, 0–1.</summary>
    public double Initial { get; set; } = 1.0;

    /// <summary>Value lost per simulated second.</summary>
    public double DecayPerSecond { get; set; }
}

/// <summary>
/// Something an agent can do about its needs: candidacy conditions, a cost
/// formula, the needs it restores on completion, and the task list that
/// realizes it — the same task vocabulary as schedules and BT leaves.
/// Selector score = Σ (need urgency × satisfaction) / cost (ch05 §5.4).
/// </summary>
public sealed class ActivityDef : Def
{
    /// <summary>Need def ID → value restored when the activity completes.</summary>
    public Dictionary<string, double> Satisfies { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Cost formula (relative effort; needs/stats/flags in scope). Floored at 0.05 when scoring.</summary>
    public string Cost { get; set; } = "1";

    /// <summary>Condition primitives gating candidacy (subject = the agent).</summary>
    public List<JsonElement>? Conditions { get; set; }

    /// <summary>Ordered task payloads executed when this activity is chosen.</summary>
    public List<JsonElement> Tasks { get; set; } = [];

    public override IEnumerable<DefReference> GetReferences()
    {
        foreach (var need in Satisfies.Keys)
        {
            yield return new DefReference(need, $"{Id}.satisfies");
        }
    }
}
