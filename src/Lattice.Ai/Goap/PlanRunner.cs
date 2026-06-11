using Lattice.Ai.Agents;

namespace Lattice.Ai.Goap;

public enum PlanRunStatus
{
    Idle,
    Running,
    Completed,
    Failed,
}

/// <summary>
/// Executes a primitive plan step by step through the 3-state
/// <see cref="ExecutionLayer"/>, re-checking each step's preconditions at
/// activation (ch03 §3.6 mechanism 3) and applying its symbolic effects to
/// beliefs on completion. Shared by the GOAP and HTN brains — how a plan
/// was *found* doesn't change how it *runs*.
/// </summary>
public sealed class PlanRunner
{
    private readonly ExecutionLayer _exec = new();
    private GoapPlan? _plan;
    private int _stepIndex;
    private bool _stepActive;

    public GoapPlan? Plan => _plan;

    public int StepIndex => _stepIndex;

    public bool HasPlan => _plan is not null;

    public void Set(GoapPlan plan)
    {
        _plan = plan;
        _stepIndex = 0;
        _stepActive = false;
    }

    public PlanRunStatus Tick(AgentContext ctx, IReadOnlyDictionary<string, object> state, float dt)
    {
        if (_plan is null)
        {
            return PlanRunStatus.Idle;
        }

        var step = _plan.Steps[_stepIndex];
        if (!_stepActive)
        {
            if (!PredicateState.MatchesAll(state, step.Preconditions))
            {
                Abandon(ctx, $"{step.Id} preconditions no longer hold");
                return PlanRunStatus.Failed;
            }

            if (!_exec.Begin(ctx, step))
            {
                Abandon(ctx, $"{step.Id} failed to activate");
                return PlanRunStatus.Failed;
            }

            _stepActive = true;
        }

        switch (_exec.Tick(ctx, dt))
        {
            case ExecutionStatus.Complete:
                CompleteStep(ctx, step);
                return _plan is null ? PlanRunStatus.Completed : PlanRunStatus.Running;

            case ExecutionStatus.Failed:
                Abandon(ctx, $"{step.Id} failed");
                return PlanRunStatus.Failed;

            default:
                return PlanRunStatus.Running;
        }
    }

    public void Abandon(AgentContext ctx, string? reason)
    {
        if (reason is not null)
        {
            ctx.Agent.AddTrace(ctx.Session.Tick, reason);
        }

        if (_stepActive)
        {
            _exec.Abort(ctx);
        }

        _plan = null;
        _stepIndex = 0;
        _stepActive = false;
    }

    /// <summary>Release any held smart-object reservation (occupation legitimately outlives a plan).</summary>
    public void ReleaseReservation(AgentContext ctx) => _exec.ReleaseReservation(ctx);

    private void CompleteStep(AgentContext ctx, GoapCandidate step)
    {
        // symbolic effects become beliefs; sensor-owned keys are overwritten
        // by the next perception update anyway
        foreach (var effect in step.Effects)
        {
            ctx.Agent.Beliefs.Set(effect.Key, effect.Value);
        }

        if (step.Action?.RunEffects is { Count: > 0 } effects)
        {
            var target = ctx.Agent.Beliefs.GetString("enemy_id") is { } enemyId
                         && ctx.Session.World.TryGet(enemyId, out var entity)
                ? entity
                : ctx.Entity;
            ctx.Ai.Rpg.RunEffects(effects, source: ctx.Entity, target: target);
        }

        ctx.Agent.AddTrace(ctx.Session.Tick, $"action {step.Id} done");
        _stepIndex++;
        _stepActive = false;
        if (_stepIndex >= _plan!.Steps.Count)
        {
            _plan = null;
        }
    }
}
