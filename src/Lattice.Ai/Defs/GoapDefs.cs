using System.Text.Json;
using Lattice.Core.Content;

namespace Lattice.Ai.Defs;

/// <summary>
/// One plannable GOAP action (ch03, F.E.A.R. case study): symbolic
/// preconditions and effects over the agent's predicate state (catalog
/// condition names and belief keys; scalar values, missing booleans read as
/// false), a cost formula (personality lives in cost — see
/// <see cref="CostProfileDef"/>), and the execution binding the 3-state
/// layer realizes (GoTo → Animate → effects).
/// </summary>
public sealed class GoapActionDef : Def
{
    /// <summary>Predicate map that must match the agent's state for this action to be applicable.</summary>
    public Dictionary<string, JsonElement>? Preconditions { get; set; }

    /// <summary>Predicate map this action establishes (applied to beliefs on completion).</summary>
    public Dictionary<string, JsonElement> Effects { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Cost formula (agent needs/stats/conditions/flags in scope). Evaluated once per plan request.</summary>
    public string Cost { get; set; } = "1";

    /// <summary>Where to go first, if anywhere: a MoveTo target spec ("enemy", "spawn", [x,y,z], ...).</summary>
    public JsonElement? MoveTo { get; set; }

    /// <summary>Movement speed: "walk" (default) or "run".</summary>
    public string? Speed { get; set; }

    /// <summary>Animation played after arrival, if any.</summary>
    public string? Animation { get; set; }

    /// <summary>Non-interruptible animations block replanning while they play (ch05 §5.6).</summary>
    public bool AnimationBlocking { get; set; }

    /// <summary>RPG effect primitives run on completion (source = agent; target = perceived enemy when known).</summary>
    public List<JsonElement>? RunEffects { get; set; }
}

/// <summary>
/// A GOAP goal: the desired predicate state, a priority formula that
/// returns 0 when irrelevant (the ch06 checklist rule), and the
/// relevance-filtered replan triggers — only the condition names listed
/// here force a replan while this goal is active (the F.E.A.R. Part 8
/// lesson: replanning on every world change is how you get the rat bug).
/// </summary>
public sealed class GoapGoalDef : Def
{
    /// <summary>Predicate map describing success.</summary>
    public Dictionary<string, JsonElement> Desired { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Priority formula; 0 (or less) = irrelevant right now.</summary>
    public string Priority { get; set; } = "0";

    /// <summary>Catalog condition names whose *appearance* invalidates the current plan.</summary>
    public List<string>? ReplanRequired { get; set; }
}

/// <summary>
/// Personality as data (ch03 §3.7, F.E.A.R.'s per-archetype database): a
/// profile-level map of action def ID → replacement cost formula. A
/// cowardly and an aggressive soldier share the same action set and differ
/// only in this file.
/// </summary>
public sealed class CostProfileDef : Def
{
    /// <summary>Action def ID → cost formula override.</summary>
    public Dictionary<string, string> Overrides { get; set; } = new(StringComparer.Ordinal);

    public override IEnumerable<DefReference> GetReferences()
    {
        foreach (var actionId in Overrides.Keys)
        {
            yield return new DefReference(actionId, $"{Id}.overrides");
        }
    }
}
