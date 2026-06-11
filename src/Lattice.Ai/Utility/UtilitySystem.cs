using System.Text.Json;
using Lattice.Ai.Agents;
using Lattice.Ai.Defs;
using Lattice.Ai.Tasks;
using Lattice.Core.Content;
using Lattice.Core.Formulas;
using Lattice.Core.Simulation;
using Lattice.Rpg.Conditions;
using Lattice.Rpg.Effects;
using TaskStatus = Lattice.Ai.Tasks.TaskStatus;

namespace Lattice.Ai.Utility;

/// <summary>
/// Resolves an agent's need keys, catalog condition names (1/0), and
/// numeric/bool beliefs as formula identifiers — the scope for utility
/// factors, GOAP goal priorities, and action costs.
/// </summary>
public sealed class AgentFormulaContext(AgentComponent agent, GameSession session) : IFormulaContext
{
    public bool TryResolve(string identifier, out double value)
    {
        foreach (var pair in agent.Needs)
        {
            if (session.Defs.TryGet<NeedDef>(pair.Key, out var need) && need.Key == identifier)
            {
                value = pair.Value;
                return true;
            }
        }

        if (agent.Catalog.TryGetBit(identifier, out _))
        {
            value = agent.Conditions.IsSet(agent.Catalog, identifier) ? 1 : 0;
            return true;
        }

        switch (agent.Beliefs.Get(identifier))
        {
            case double number:
                value = number;
                return true;
            case bool flag:
                value = flag ? 1 : 0;
                return true;
        }

        value = 0;
        return false;
    }
}

/// <summary>One scored factor of a utility evaluator (debug breakdown).</summary>
public readonly record struct FactorScore(string Formula, double Weight, double Value);

/// <summary>One row of the activity scoreboard (ch07: every choice must be explainable).</summary>
public readonly record struct ActivityScore(ActivityDef Activity, bool Eligible, double Cost, double Score, string Breakdown);

/// <summary>The two data-driven utility patterns from ch05 §5.4: weighted evaluators and the need-based selector.</summary>
public static class UtilityScoring
{
    /// <summary>Weighted average of clamped factors — always 0–1 (normalization by construction).</summary>
    public static double Score(UtilityEvaluatorDef def, IFormulaEngine formulas, IFormulaContext scope, List<FactorScore>? breakdown = null)
    {
        double total = 0, weights = 0;
        foreach (var factor in def.Factors)
        {
            var value = Math.Clamp(formulas.Evaluate(factor.Formula, scope), 0, 1);
            breakdown?.Add(new FactorScore(factor.Formula, factor.Weight, value));
            total += value * factor.Weight;
            weights += factor.Weight;
        }

        return weights <= 0 ? 0 : total / weights;
    }

    /// <summary>The formula scope for utility evaluation: needs/beliefs first, then subject stats and global flags.</summary>
    public static IFormulaContext ScopeFor(AgentContext ctx, ConditionContext conditionContext)
        => new CompositeFormulaContext(new AgentFormulaContext(ctx.Agent, ctx.Session), conditionContext.Scope);

    /// <summary>Score every activity in the agent's profile — the scoreboard; the selector takes the best eligible row.</summary>
    public static List<ActivityScore> ScoreActivities(AgentContext ctx)
    {
        var results = new List<ActivityScore>();
        var conditionContext = new ConditionContext { Session = ctx.Session, Rpg = ctx.Ai.Rpg, Subject = ctx.Entity };
        var scope = ScopeFor(ctx, conditionContext);

        foreach (var activityId in ctx.Agent.Profile.Activities ?? [])
        {
            if (!ctx.Session.Defs.TryGet<ActivityDef>(activityId, out var activity))
            {
                continue;
            }

            var eligible = ctx.Ai.Rpg.Conditions.EvaluateAll(activity.Conditions, conditionContext);
            var cost = Math.Max(ctx.Session.Formulas.Evaluate(activity.Cost, scope), 0.05);
            double satisfactionSum = 0;
            var parts = new List<string>();
            foreach (var pair in activity.Satisfies)
            {
                var urgency = 1 - (ctx.Agent.Needs.TryGetValue(pair.Key, out var current) ? current : 1);
                satisfactionSum += urgency * pair.Value;
                parts.Add($"{pair.Key} {urgency:F2}×{pair.Value:F2}");
            }

            results.Add(new ActivityScore(activity, eligible, cost, satisfactionSum / cost, string.Join(" + ", parts)));
        }

        return results;
    }

    /// <summary>The need-based selector: best eligible positive score (ties broken by ID for determinism).</summary>
    public static ActivityScore? SelectActivity(AgentContext ctx)
        => ScoreActivities(ctx)
            .Where(score => score.Eligible && score.Score > 0)
            .OrderByDescending(score => score.Score)
            .ThenBy(score => score.Activity.Id, StringComparer.Ordinal)
            .Cast<ActivityScore?>()
            .FirstOrDefault();
}

/// <summary>
/// Threshold gate over a utility evaluator (the HZD attack-interest pattern):
/// {"type":"UtilityAtLeast","evaluator":"utility_x","threshold":0.6}.
/// Constructible without a runtime for validation-only use (tooling).
/// </summary>
[PrimitiveDoc("True when a utility evaluator's weighted score (0-1) meets the threshold (agent subjects only).",
    "evaluator: utility def id; threshold?: 0-1 (default 0.5)",
    """{"type":"UtilityAtLeast","evaluator":"utility_motivation","threshold":0.4}""")]
public sealed class UtilityAtLeastCondition(AiRuntime? ai = null) : IConditionEvaluator
{
    public string Type => "UtilityAtLeast";

    public bool Evaluate(ConditionContext ctx, JsonElement args)
    {
        if (ai is null
            || ctx.Subject is null
            || ai.GetAgent(ctx.Subject) is not { } agent
            || !ctx.Session.Defs.TryGet<UtilityEvaluatorDef>(JsonArgs.GetString(args, "evaluator"), out var evaluator))
        {
            return false;
        }

        var scope = new CompositeFormulaContext(new AgentFormulaContext(agent, ctx.Session), ctx.Scope);
        return UtilityScoring.Score(evaluator, ctx.Session.Formulas, scope) >= JsonArgs.GetDouble(args, "threshold", 0.5);
    }

    public void Validate(JsonElement args, EffectValidationContext v)
        => v.RequireDef<UtilityEvaluatorDef>(args, "evaluator");
}

/// <summary>Need urgency gate: {"type":"NeedBelow","need":"need_thirst","threshold":0.4}. False when the agent lacks the need.</summary>
[PrimitiveDoc("True when the agent's need value (1 = satisfied) is below the threshold.",
    "need: need def id; threshold?: 0-1 (default 0.5)",
    """{"type":"NeedBelow","need":"need_thirst","threshold":0.3}""")]
public sealed class NeedBelowCondition(AiRuntime? ai = null) : IConditionEvaluator
{
    public string Type => "NeedBelow";

    public bool Evaluate(ConditionContext ctx, JsonElement args)
        => ai is not null
           && ctx.Subject is not null
           && ai.GetAgent(ctx.Subject) is { } agent
           && agent.Needs.TryGetValue(JsonArgs.GetString(args, "need"), out var value)
           && value < JsonArgs.GetDouble(args, "threshold", 0.5);

    public void Validate(JsonElement args, EffectValidationContext v)
        => v.RequireDef<NeedDef>(args, "need");
}

/// <summary>
/// The bridge between needs and any brain tier: run the selector, commit to
/// the best activity, execute its task list (one inner task at a time, like a
/// schedule), and apply the need restoration on completion. Fails when
/// nothing is worth doing — a BT selector can then fall through to idling.
/// </summary>
[PrimitiveDoc("Run the need-based utility selector, commit to the best activity from the profile, and execute its task list.",
    "(no args; candidates come from the profile's activities)",
    """{"task":"PerformActivity"}""")]
public sealed class PerformActivityTask : ITaskExecutor
{
    public string Type => "PerformActivity";

    public object? Start(AgentContext ctx, JsonElement args)
    {
        if (UtilityScoring.SelectActivity(ctx) is not { } choice)
        {
            return null;
        }

        ctx.Agent.AddTrace(ctx.Session.Tick, $"activity {choice.Activity.Id} selected (score {choice.Score:F2})");
        ctx.Agent.Beliefs.Set("current_activity", choice.Activity.Id);
        return new Run { Activity = choice.Activity };
    }

    public TaskStatus Tick(AgentContext ctx, JsonElement args, ref object? state, float dt)
    {
        if (state is not Run run)
        {
            return TaskStatus.Failed; // nothing worth doing
        }

        if (run.Index >= run.Activity.Tasks.Count)
        {
            Complete(ctx, run);
            return TaskStatus.Complete;
        }

        var element = run.Activity.Tasks[run.Index];
        if (!ctx.Ai.Tasks.TryGet(element, out var executor, out var taskType))
        {
            ctx.Agent.AddTrace(ctx.Session.Tick, $"activity {run.Activity.Id} task '{taskType}' unknown");
            ctx.Agent.Beliefs.Remove("current_activity");
            return TaskStatus.Failed;
        }

        if (!run.Started)
        {
            run.State = executor.Start(ctx, element);
            run.Started = true;
        }

        switch (executor.Tick(ctx, element, ref run.State, dt))
        {
            case TaskStatus.Complete:
                run.Index++;
                run.Started = false;
                run.State = null;
                if (run.Index >= run.Activity.Tasks.Count)
                {
                    Complete(ctx, run);
                    return TaskStatus.Complete;
                }

                return TaskStatus.Running;

            case TaskStatus.Failed:
                ctx.Agent.AddTrace(ctx.Session.Tick, $"activity {run.Activity.Id} task {taskType} failed");
                ctx.Agent.Beliefs.Remove("current_activity");
                return TaskStatus.Failed;

            default:
                return TaskStatus.Running;
        }
    }

    public void Validate(JsonElement args, EffectValidationContext v)
    {
    }

    private static void Complete(AgentContext ctx, Run run)
    {
        foreach (var pair in run.Activity.Satisfies)
        {
            if (ctx.Agent.Needs.TryGetValue(pair.Key, out var current))
            {
                ctx.Agent.Needs[pair.Key] = Math.Clamp(current + pair.Value, 0, 1);
            }
        }

        ctx.Agent.AddTrace(ctx.Session.Tick, $"activity {run.Activity.Id} complete");
        ctx.Agent.Beliefs.Remove("current_activity");
    }

    private sealed class Run
    {
        public required ActivityDef Activity { get; init; }

        public int Index { get; set; }

        public bool Started { get; set; }

        /// <summary>Field, not property: the inner executor takes it by ref.</summary>
        public object? State;
    }
}
