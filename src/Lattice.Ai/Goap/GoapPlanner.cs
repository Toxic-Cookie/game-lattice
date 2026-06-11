using System.Text.Json;
using Lattice.Core.Content;

namespace Lattice.Ai.Goap;

/// <summary>
/// A candidate the planner can chain: either a <see cref="Defs.GoapActionDef"/>
/// from the agent's action subset or a smart object in sensor range surfaced
/// as an action (its reservation is the exclusion coordinator). Costs are
/// evaluated once, when the candidate set is built — the planner itself never
/// touches formulas.
/// </summary>
public sealed class GoapCandidate
{
    public required string Id { get; init; }

    public required double Cost { get; init; }

    public required Dictionary<string, object> Preconditions { get; init; }

    public required Dictionary<string, object> Effects { get; init; }

    /// <summary>The action def realizing this candidate (null for smart-object candidates).</summary>
    public Defs.GoapActionDef? Action { get; init; }

    /// <summary>Smart-object entity instance ID (null for def-backed candidates).</summary>
    public string? SmartObjectId { get; init; }
}

/// <summary>An ordered, costed plan.</summary>
public sealed class GoapPlan
{
    public required IReadOnlyList<GoapCandidate> Steps { get; init; }

    public required double TotalCost { get; init; }

    /// <summary>Open-set nodes expanded while planning (the dump's perf line).</summary>
    public required int NodesExplored { get; init; }
}

/// <summary>
/// Predicate-state helpers shared by the planner and the brain. Values are
/// plain scalars (bool/double/string); a missing key matches an expected
/// <c>false</c> — absent facts are false facts, which keeps content terse.
/// </summary>
public static class PredicateState
{
    public static bool Matches(IReadOnlyDictionary<string, object> state, string key, object expected)
        => state.TryGetValue(key, out var actual)
            ? Equals(actual, expected)
            : expected is false;

    public static bool MatchesAll(IReadOnlyDictionary<string, object> state, IReadOnlyDictionary<string, object> predicates)
        => predicates.All(pair => Matches(state, pair.Key, pair.Value));

    public static int MismatchCount(IReadOnlyDictionary<string, object> state, IReadOnlyDictionary<string, object> predicates)
        => predicates.Count(pair => !Matches(state, pair.Key, pair.Value));

    /// <summary>Json predicate maps (def fields) → plain predicate maps. Non-scalar values are skipped.</summary>
    public static Dictionary<string, object> ToPlain(IReadOnlyDictionary<string, JsonElement>? json)
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var pair in json ?? new Dictionary<string, JsonElement>())
        {
            if (JsonValueHelper.TryToPlain(pair.Value, out var value))
            {
                result[pair.Key] = value;
            }
        }

        return result;
    }

    public static Dictionary<string, object> ToPlain(IReadOnlyDictionary<string, bool>? map)
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var pair in map ?? new Dictionary<string, bool>())
        {
            result[pair.Key] = pair.Value;
        }

        return result;
    }
}

/// <summary>
/// The GOAP planner (ch03 §3.3): forward A* over predicate states. Pure
/// function — no world access, no formulas, deterministic for a given
/// candidate list order. Budgets are the point: MAX_PLAN_LENGTH defaults to
/// 4 (ch07 anti-pattern 5 — long plans are stale plans) and the open set is
/// capped so a pathological action set degrades to "no plan", never to a
/// frame hitch.
/// </summary>
public static class GoapPlanner
{
    public const int DefaultMaxPlanLength = 4;
    public const int DefaultMaxOpenNodes = 256;

    private sealed class Node
    {
        public required Dictionary<string, object> State { get; init; }

        public required double G { get; init; }

        public required int H { get; init; }

        public required int Depth { get; init; }

        public Node? Parent { get; init; }

        public GoapCandidate? Via { get; init; }

        public double F => G + H;
    }

    /// <summary>Find the cheapest action chain from <paramref name="start"/> to a state matching <paramref name="goal"/>, or null.</summary>
    public static GoapPlan? Plan(
        IReadOnlyList<GoapCandidate> candidates,
        IReadOnlyDictionary<string, object> start,
        IReadOnlyDictionary<string, object> goal,
        int maxPlanLength = DefaultMaxPlanLength,
        int maxOpenNodes = DefaultMaxOpenNodes)
    {
        var open = new List<Node>
        {
            new()
            {
                State = new Dictionary<string, object>(start, StringComparer.Ordinal),
                G = 0,
                H = PredicateState.MismatchCount(start, goal),
                Depth = 0,
            },
        };
        var explored = 0;

        while (open.Count > 0 && explored < maxOpenNodes)
        {
            // lowest f wins; ties go to the earliest-inserted node (determinism)
            var bestIndex = 0;
            for (var i = 1; i < open.Count; i++)
            {
                if (open[i].F < open[bestIndex].F)
                {
                    bestIndex = i;
                }
            }

            var current = open[bestIndex];
            open.RemoveAt(bestIndex);
            explored++;

            if (current.H == 0)
            {
                return Reconstruct(current, explored);
            }

            if (current.Depth >= maxPlanLength)
            {
                continue;
            }

            foreach (var candidate in candidates)
            {
                if (!PredicateState.MatchesAll(current.State, candidate.Preconditions))
                {
                    continue;
                }

                var nextState = new Dictionary<string, object>(current.State, StringComparer.Ordinal);
                foreach (var effect in candidate.Effects)
                {
                    nextState[effect.Key] = effect.Value;
                }

                open.Add(new Node
                {
                    State = nextState,
                    G = current.G + candidate.Cost,
                    H = PredicateState.MismatchCount(nextState, goal),
                    Depth = current.Depth + 1,
                    Parent = current,
                    Via = candidate,
                });
            }
        }

        return null;
    }

    private static GoapPlan Reconstruct(Node node, int explored)
    {
        var steps = new List<GoapCandidate>();
        var cost = node.G;
        for (var n = node; n?.Via is not null; n = n.Parent)
        {
            steps.Add(n.Via);
        }

        steps.Reverse();
        return new GoapPlan { Steps = steps, TotalCost = cost, NodesExplored = explored };
    }
}
