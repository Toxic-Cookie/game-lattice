using Lattice.Ai.Defs;
using Lattice.Core.Content;

namespace Lattice.Ai.Goap;

/// <summary>
/// HTN decomposition (ch04 §4.3): depth-first over a compound task's
/// methods in declared order, backtracking on failure, with effects
/// tracked through decomposition so later subtasks plan against the state
/// earlier ones will have produced. Pure function with a recursion budget;
/// emits the ch07 §7.5 indented decompose/✓/✗ trace.
/// </summary>
public static class HtnPlanner
{
    public const int DefaultMaxDepth = 16;

    /// <summary>
    /// Decompose <paramref name="rootTaskId"/> into a primitive plan, or
    /// null. Cost formulas are supplied per primitive by
    /// <paramref name="makeCandidate"/> (the brain pre-evaluates costs the
    /// same way GOAP does).
    /// </summary>
    public static GoapPlan? Decompose(
        string rootTaskId,
        IReadOnlyDictionary<string, object> start,
        DefRegistry defs,
        Func<GoapActionDef, GoapCandidate> makeCandidate,
        List<string>? trace = null,
        int maxDepth = DefaultMaxDepth)
    {
        var state = new Dictionary<string, object>(start, StringComparer.Ordinal);
        var plan = new List<GoapCandidate>();
        if (!Expand(rootTaskId, state, plan, defs, makeCandidate, trace, depth: 0, maxDepth))
        {
            return null;
        }

        return new GoapPlan
        {
            Steps = plan,
            TotalCost = plan.Sum(step => step.Cost),
            NodesExplored = plan.Count,
        };
    }

    private static bool Expand(
        string taskId,
        Dictionary<string, object> state,
        List<GoapCandidate> plan,
        DefRegistry defs,
        Func<GoapActionDef, GoapCandidate> makeCandidate,
        List<string>? trace,
        int depth,
        int maxDepth)
    {
        var indent = new string(' ', depth * 2);
        if (depth > maxDepth)
        {
            trace?.Add($"{indent}✗ {taskId} (depth budget {maxDepth} exceeded)");
            return false;
        }

        if (defs.TryGet<GoapActionDef>(taskId, out var primitive))
        {
            var candidate = makeCandidate(primitive);
            if (!PredicateState.MatchesAll(state, candidate.Preconditions))
            {
                trace?.Add($"{indent}✗ {taskId} (preconditions)");
                return false;
            }

            foreach (var effect in candidate.Effects)
            {
                state[effect.Key] = effect.Value;
            }

            plan.Add(candidate);
            trace?.Add($"{indent}✓ {taskId}");
            return true;
        }

        if (!defs.TryGet<HtnCompoundDef>(taskId, out var compound))
        {
            trace?.Add($"{indent}✗ {taskId} (unknown task)");
            return false;
        }

        trace?.Add($"{indent}decompose {taskId}");
        for (var i = 0; i < compound.Methods.Count; i++)
        {
            var method = compound.Methods[i];
            var label = method.Name ?? $"method[{i}]";
            if (!PredicateState.MatchesAll(state, PredicateState.ToPlain(method.Preconditions)))
            {
                trace?.Add($"{indent}  ✗ {label} (preconditions)");
                continue;
            }

            // checkpoint for backtracking: subtasks mutate state and plan
            var stateCheckpoint = new Dictionary<string, object>(state, StringComparer.Ordinal);
            var planCheckpoint = plan.Count;
            trace?.Add($"{indent}  try {label}");

            var succeeded = true;
            foreach (var subtask in method.Subtasks)
            {
                if (!Expand(subtask, state, plan, defs, makeCandidate, trace, depth + 1, maxDepth))
                {
                    succeeded = false;
                    break;
                }
            }

            if (succeeded)
            {
                trace?.Add($"{indent}  ✓ {label}");
                return true;
            }

            // backtrack and try the next method in order
            state.Clear();
            foreach (var pair in stateCheckpoint)
            {
                state[pair.Key] = pair.Value;
            }

            plan.RemoveRange(planCheckpoint, plan.Count - planCheckpoint);
            trace?.Add($"{indent}  ✗ {label} (backtracked)");
        }

        trace?.Add($"{indent}✗ {taskId} (no method applies)");
        return false;
    }
}
