using System.Text;
using Lattice.Ai.Agents;
using Lattice.Ai.Defs;
using Lattice.Ai.Utility;
using Lattice.Core.Formulas;
using Lattice.Rpg.Conditions;

namespace Lattice.Ai.Goap;

/// <summary>
/// The high-emergence tier (ch03, F.E.A.R.): goals declare *what*, actions
/// declare *how*, and the A* planner finds the cheapest chain at runtime.
/// All three validation mechanisms from ch03 §3.6 are in force:
/// (1) a freshly built plan is simulated on a state copy before acceptance,
/// (2) the active goal's replanRequired conditions — and only those, the
///     relevance filter — invalidate the running plan, gated by
///     non-interruptible animations and the replan cooldown,
/// (3) every action re-checks its preconditions at activation.
/// </summary>
public sealed class GoapBrain : IBrain
{
    private readonly PlanRunner _runner = new();
    private GoapGoalDef? _goal;
    private double _goalPriority;
    private double _nextPlanAt;
    private uint _replanMask;

    // dump state (ch07 §7.4: every decision reconstructable)
    private Dictionary<string, object> _lastState = new(StringComparer.Ordinal);
    private List<GoapCandidate> _lastCandidates = [];
    private readonly List<(string GoalId, double Priority)> _lastPriorities = [];

    public string Kind => "goap";

    public string? CurrentGoalId => _goal?.Id;

    public GoapPlan? CurrentPlan => _runner.Plan;

    public string Describe()
        => _goal is null
            ? "goap (no relevant goal)"
            : $"goap goal={_goal.Id} " + (_runner.Plan is not { } plan
                ? "(no plan)"
                : $"plan {_runner.StepIndex + 1}/{plan.Steps.Count} [{string.Join(" -> ", plan.Steps.Select(s => s.Id))}]");

    public void Tick(AgentContext ctx, float dt)
    {
        var agent = ctx.Agent;
        var state = BuildPredicateState(ctx);
        _lastState = state;

        // non-interruptible animations block all decision changes (ch05 §5.6)
        var committed = ctx.Session.Services.Animation.IsPlayingNonInterruptible(ctx.Entity.InstanceId);

        SelectGoal(ctx, committed);
        if (_goal is null)
        {
            return;
        }

        var desired = PredicateState.ToPlain(_goal.Desired);
        if (PredicateState.MatchesAll(state, desired))
        {
            // goal satisfied — idle until priorities shift (the feedback loop is the reactivity)
            _runner.Abandon(ctx, reason: null);
            return;
        }

        // mechanism 2: relevance-filtered replan triggers, gated by
        // non-interruptible animation (ch05 §5.6) and the replan cooldown
        if (_runner.HasPlan && agent.Conditions.HasAnyOf(_replanMask) && !committed)
        {
            var triggered = agent.Conditions.SetNames(agent.Catalog)
                .Where(name => (agent.Catalog.MaskOf([name]) & _replanMask) != 0);
            _runner.Abandon(ctx, $"replan required by {string.Join("|", triggered)}");
        }

        if (!_runner.HasPlan)
        {
            if (ctx.Session.SimTimeSeconds < _nextPlanAt)
            {
                return;
            }

            BuildPlan(ctx, state, desired);
            if (!_runner.HasPlan)
            {
                return;
            }
        }

        if (_runner.Tick(ctx, state, dt) == PlanRunStatus.Completed)
        {
            ctx.Agent.AddTrace(ctx.Session.Tick, $"plan for {_goal.Id} complete");
        }
    }

    /// <summary>Conditions (as bools) layered over scalar beliefs — the agent's plannable view of the world.</summary>
    public static Dictionary<string, object> BuildPredicateState(AgentContext ctx)
    {
        var state = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var fact in ctx.Agent.Beliefs.Facts)
        {
            if (fact.Value is bool or double or string)
            {
                state[fact.Key] = fact.Value;
            }
        }

        foreach (var name in ctx.Agent.Catalog.Names)
        {
            state[name] = ctx.Agent.Conditions.IsSet(ctx.Agent.Catalog, name);
        }

        return state;
    }

    private void SelectGoal(AgentContext ctx, bool committed = false)
    {
        var scope = Scope(ctx);
        _lastPriorities.Clear();

        GoapGoalDef? best = null;
        double bestPriority = 0;
        foreach (var goalId in ctx.Agent.Profile.Goals ?? [])
        {
            if (!ctx.Session.Defs.TryGet<GoapGoalDef>(goalId, out var goal))
            {
                continue;
            }

            double priority;
            try
            {
                priority = ctx.Session.Formulas.Evaluate(goal.Priority, scope);
            }
            catch (FormulaException)
            {
                priority = 0; // unevaluable = irrelevant (fails closed)
            }

            _lastPriorities.Add((goal.Id, priority));
            if (priority > 0 && (best is null || priority > bestPriority))
            {
                best = goal;
                bestPriority = priority;
            }
        }

        if (_goal is not null)
        {
            // refresh the active goal's priority so decayed goals release
            _goalPriority = _lastPriorities.FirstOrDefault(p => p.GoalId == _goal.Id).Priority;
        }

        var switchGoal = !committed && best is not null && best != _goal
            && (_goal is null || _goalPriority <= 0 || bestPriority > _goalPriority + ctx.Agent.Profile.GoalHysteresis);
        var dropGoal = !committed && best is null && _goal is not null && _goalPriority <= 0;

        if (switchGoal)
        {
            ctx.Agent.AddTrace(ctx.Session.Tick, $"goal {_goal?.Id ?? "(none)"} -> {best!.Id} (priority {bestPriority:F1})");
            _runner.Abandon(ctx, reason: null);
            _runner.ReleaseReservation(ctx);
            _goal = best;
            _goalPriority = bestPriority;
            _replanMask = ctx.Agent.Catalog.MaskOf(best.ReplanRequired);
            _nextPlanAt = 0; // a new goal may plan immediately
        }
        else if (dropGoal)
        {
            ctx.Agent.AddTrace(ctx.Session.Tick, $"goal {_goal!.Id} no longer relevant");
            _runner.Abandon(ctx, reason: null);
            _runner.ReleaseReservation(ctx);
            _goal = null;
        }
    }

    private void BuildPlan(AgentContext ctx, Dictionary<string, object> state, Dictionary<string, object> desired)
    {
        var candidates = BuildCandidates(ctx);
        _lastCandidates = candidates;
        _nextPlanAt = ctx.Session.SimTimeSeconds + ctx.Agent.Profile.ReplanCooldown;

        var plan = GoapPlanner.Plan(candidates, state, desired);
        if (plan is null)
        {
            ctx.Agent.AddTrace(ctx.Session.Tick, $"no plan for {_goal!.Id} ({candidates.Count} candidates)");
            return;
        }

        // mechanism 1: simulate the plan on a state copy before accepting it
        if (!Simulate(plan, state, desired))
        {
            ctx.Agent.AddTrace(ctx.Session.Tick, $"plan for {_goal!.Id} failed simulation");
            return;
        }

        _runner.Set(plan);
        ctx.Agent.AddTrace(ctx.Session.Tick,
            $"plan {_goal!.Id}: {string.Join(" -> ", plan.Steps.Select(s => s.Id))} (cost {plan.TotalCost:F1}, {plan.NodesExplored} nodes)");
    }

    /// <summary>Replay the plan over a copy of the state: every step applicable in order, goal reached at the end.</summary>
    public static bool Simulate(GoapPlan plan, IReadOnlyDictionary<string, object> state, IReadOnlyDictionary<string, object> desired)
    {
        var working = new Dictionary<string, object>(state, StringComparer.Ordinal);
        foreach (var step in plan.Steps)
        {
            if (!PredicateState.MatchesAll(working, step.Preconditions))
            {
                return false;
            }

            foreach (var effect in step.Effects)
            {
                working[effect.Key] = effect.Value;
            }
        }

        return PredicateState.MatchesAll(working, desired);
    }

    /// <summary>The agent's action subset (cost-profiled) plus reservable smart objects in sensor range.</summary>
    public List<GoapCandidate> BuildCandidates(AgentContext ctx)
    {
        var scope = Scope(ctx);
        var candidates = new List<GoapCandidate>();

        var overrides = ctx.Agent.Profile.CostProfile is { } profileId
                        && ctx.Session.Defs.TryGet<CostProfileDef>(profileId, out var costProfile)
            ? costProfile.Overrides
            : null;

        foreach (var actionId in ctx.Agent.Profile.Actions ?? [])
        {
            if (ctx.Session.Defs.TryGet<GoapActionDef>(actionId, out var action))
            {
                candidates.Add(MakeCandidate(ctx, action, scope, overrides));
            }
        }

        // smart objects in sensor range surface as actions (plan/04 §9);
        // already-reserved-by-others objects never enter the search space
        if (ctx.Ai.Narrative?.Interactions is { } interactions)
        {
            var range = ctx.Agent.Profile.Sensors?.Max(s => s.Range) ?? 0;
            foreach (var entity in ctx.Ai.QueryEntitiesNear(ctx.Entity, range).OrderBy(e => e.InstanceId, StringComparer.Ordinal))
            {
                var binding = interactions.GetBinding(entity);
                if (binding?.AiEffects is not { Count: > 0 } || !interactions.CanReserve(entity, ctx.Entity.InstanceId))
                {
                    continue;
                }

                var distance = (entity.Position - ctx.Entity.Position).Length();
                candidates.Add(new GoapCandidate
                {
                    Id = $"use:{binding.Id}@{entity.InstanceId}",
                    Cost = 1 + distance * 0.1, // nearer objects are cheaper — distance is the tiebreaker
                    Preconditions = PredicateState.ToPlain(binding.Preconditions),
                    Effects = PredicateState.ToPlain(binding.AiEffects),
                    SmartObjectId = entity.InstanceId,
                });
            }
        }

        return candidates;
    }

    /// <summary>A costed candidate from an action def, honoring a cost-profile override (shared with the HTN brain).</summary>
    public static GoapCandidate MakeCandidate(
        AgentContext ctx, GoapActionDef action, IFormulaContext scope, IReadOnlyDictionary<string, string>? overrides)
    {
        var formula = overrides is not null && overrides.TryGetValue(action.Id, out var costOverride) ? costOverride : action.Cost;
        return new GoapCandidate
        {
            Id = action.Id,
            Cost = EvaluateCost(ctx, formula, scope),
            Preconditions = PredicateState.ToPlain(action.Preconditions),
            Effects = PredicateState.ToPlain(action.Effects),
            Action = action,
        };
    }

    internal static IFormulaContext Scope(AgentContext ctx)
    {
        var conditionContext = new ConditionContext { Session = ctx.Session, Rpg = ctx.Ai.Rpg, Subject = ctx.Entity };
        return UtilityScoring.ScopeFor(ctx, conditionContext);
    }

    private static double EvaluateCost(AgentContext ctx, string formula, IFormulaContext scope)
    {
        try
        {
            return Math.Max(ctx.Session.Formulas.Evaluate(formula, scope), 0.01);
        }
        catch (FormulaException)
        {
            return double.MaxValue / 1024; // unevaluable cost = effectively unusable, but never poisons A* math
        }
    }

    /// <summary>The ch07 §7.4 decision dump: world state, goal priorities, the plan, and per-candidate applicability.</summary>
    public string Dump(AgentContext ctx)
    {
        var sb = new StringBuilder();
        var state = BuildPredicateState(ctx);

        sb.AppendLine("world state:");
        foreach (var pair in state.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            sb.AppendLine($"  {pair.Key} = {Format(pair.Value)}");
        }

        sb.AppendLine("goals:");
        foreach (var (goalId, priority) in _lastPriorities)
        {
            var marker = goalId == _goal?.Id ? ">" : " ";
            sb.AppendLine($" {marker} {goalId,-24} priority {priority:F1}");
        }

        sb.AppendLine(_runner.Plan is not { } plan
            ? "plan: (none)"
            : $"plan: {string.Join(" -> ", plan.Steps.Select(s => s.Id))} (cost {plan.TotalCost:F1}, step {_runner.StepIndex + 1}/{plan.Steps.Count}, {plan.NodesExplored} nodes)");

        var candidates = _lastCandidates.Count > 0 ? _lastCandidates : BuildCandidates(ctx);
        sb.AppendLine("candidates:");
        foreach (var candidate in candidates)
        {
            var missing = candidate.Preconditions
                .Where(p => !PredicateState.Matches(state, p.Key, p.Value))
                .Select(p => $"{p.Key}={Format(p.Value)}")
                .ToList();
            sb.AppendLine($"  {candidate.Id,-28} cost {candidate.Cost,6:F1}  "
                          + (missing.Count == 0 ? "possible now" : $"missing: {string.Join(", ", missing)}"));
        }

        return sb.ToString().TrimEnd();

        static string Format(object value) => value switch
        {
            bool b => b ? "true" : "false",
            double d => d.ToString("0.###"),
            _ => value.ToString() ?? "?",
        };
    }
}
