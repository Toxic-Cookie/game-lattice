using Lattice.Ai.Agents;
using Lattice.Ai.Defs;

namespace Lattice.Ai.Goap;

/// <summary>
/// The HTN brain (ch04, HZD): decomposes the profile's root compound task
/// into a primitive plan and runs it through the shared
/// <see cref="PlanRunner"/>. Re-decomposes when the plan completes or
/// fails (rate-limited by the replan cooldown) and when any
/// <see cref="AgentProfileDef.HtnInterrupt"/> condition appears — unless a
/// non-interruptible animation has the agent committed. The last
/// decomposition trace is kept for the demo `trace` command (ch07 §7.5).
/// </summary>
public sealed class HtnBrain : IBrain
{
    private readonly PlanRunner _runner = new();
    private string? _root;
    private double _nextPlanAt;
    private uint _interruptMask;
    private bool _maskBuilt;

    public string Kind => "htn";

    public IReadOnlyList<string> DecompositionTrace { get; private set; } = [];

    public GoapPlan? CurrentPlan => _runner.Plan;

    public string Describe()
        => $"htn root={_root ?? "(none)"} " + (_runner.Plan is not { } plan
            ? "(no plan)"
            : $"step {_runner.StepIndex + 1}/{plan.Steps.Count} [{string.Join(" -> ", plan.Steps.Select(s => s.Id))}]");

    public void Tick(AgentContext ctx, float dt)
    {
        var agent = ctx.Agent;
        _root = agent.Profile.RootTask;
        if (_root is null)
        {
            return;
        }

        if (!_maskBuilt)
        {
            _interruptMask = agent.Catalog.MaskOf(agent.Profile.HtnInterrupt);
            _maskBuilt = true;
        }

        var state = GoapBrain.BuildPredicateState(ctx);
        var committed = ctx.Session.Services.Animation.IsPlayingNonInterruptible(ctx.Entity.InstanceId);

        if (_runner.HasPlan && agent.Conditions.HasAnyOf(_interruptMask) && !committed)
        {
            var triggered = agent.Conditions.SetNames(agent.Catalog)
                .Where(name => (agent.Catalog.MaskOf([name]) & _interruptMask) != 0);
            _runner.Abandon(ctx, $"decomposition invalidated by {string.Join("|", triggered)}");
        }

        if (!_runner.HasPlan)
        {
            if (ctx.Session.SimTimeSeconds < _nextPlanAt)
            {
                return;
            }

            _nextPlanAt = ctx.Session.SimTimeSeconds + agent.Profile.ReplanCooldown;
            Decompose(ctx, state);
            if (!_runner.HasPlan)
            {
                return;
            }
        }

        if (_runner.Tick(ctx, state, dt) == PlanRunStatus.Completed)
        {
            agent.AddTrace(ctx.Session.Tick, $"htn {_root} complete");
        }
    }

    private void Decompose(AgentContext ctx, Dictionary<string, object> state)
    {
        var scope = GoapBrain.Scope(ctx);
        var overrides = ctx.Agent.Profile.CostProfile is { } profileId
                        && ctx.Session.Defs.TryGet<CostProfileDef>(profileId, out var costProfile)
            ? costProfile.Overrides
            : null;

        ctx.Ai.CountPlannerInvocation(ctx.Entity.InstanceId);
        var trace = new List<string>();
        var plan = HtnPlanner.Decompose(
            _root!, state, ctx.Session.Defs,
            action => GoapBrain.MakeCandidate(ctx, action, scope, overrides),
            trace);
        DecompositionTrace = trace;

        if (plan is null || plan.Steps.Count == 0)
        {
            ctx.Agent.AddTrace(ctx.Session.Tick, plan is null
                ? $"htn {_root}: no decomposition"
                : $"htn {_root}: empty decomposition");
            return;
        }

        _runner.Set(plan);
        ctx.Agent.AddTrace(ctx.Session.Tick,
            $"htn {_root}: {string.Join(" -> ", plan.Steps.Select(s => s.Id))} (cost {plan.TotalCost:F1})");
    }
}
