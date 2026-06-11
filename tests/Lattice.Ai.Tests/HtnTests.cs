using System.Numerics;
using Lattice.Ai.Defs;
using Lattice.Ai.Goap;
using Lattice.Core.Content;
using Lattice.Core.Hosting.Standalone;

namespace Lattice.Ai.Tests;

/// <summary>
/// HTN decomposition (plan/04 §12): method order as priority, effects
/// tracked through decomposition, backtracking, the depth budget, and the
/// brain executing/replanning over the shared execution layer.
/// </summary>
public sealed class HtnTests : IDisposable
{
    private readonly AiTestHost _host = new();

    public HtnTests() => _host.WriteStandardContent();

    public void Dispose() => _host.Dispose();

    private DefRegistry LoadRegistry()
    {
        using var source = new DirectoryContentSource(_host.ContentRoot, watch: false);
        var registry = new DefRegistry();
        new ContentLoader(LatticeAi.CreateDefTypes()).LoadAll(source, registry);
        return registry;
    }

    private static GoapCandidate Make(GoapActionDef action) => new()
    {
        Id = action.Id,
        Cost = 1,
        Preconditions = PredicateState.ToPlain(action.Preconditions),
        Effects = PredicateState.ToPlain(action.Effects),
        Action = action,
    };

    private static Dictionary<string, object> State(params (string Key, object Value)[] pairs)
    {
        var state = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (key, value) in pairs)
        {
            state[key] = value;
        }

        return state;
    }

    [Fact]
    public void MethodOrder_IsThePriority()
    {
        var defs = LoadRegistry();

        var calm = HtnPlanner.Decompose("htn_forage", State(), defs, Make);
        Assert.NotNull(calm);
        Assert.Equal(
            ["action_goto_berries", "action_gather", "action_carry_home"],
            calm.Steps.Select(s => s.Id));

        var threatened = HtnPlanner.Decompose("htn_forage", State(("THREAT_KNOWN", true)), defs, Make);
        Assert.NotNull(threatened);
        Assert.Equal(["action_run_home"], threatened.Steps.Select(s => s.Id));
    }

    [Fact]
    public void EffectsTrackThroughDecomposition()
    {
        // action_gather requires at_berries, which only action_goto_berries
        // establishes — the calm plan above only works if effects propagate
        var defs = LoadRegistry();

        var plan = HtnPlanner.Decompose("htn_forage", State(), defs, Make);

        Assert.NotNull(plan);
        Assert.Equal(3, plan.Steps.Count); // and not a precondition failure on gather
    }

    [Fact]
    public void Backtracking_FallsToTheNextMethod()
    {
        _host.WriteContent("htn-extra.json", """
            [ { "id": "action_blocked", "type": "goapaction",
                "preconditions": { "never_true": true }, "effects": { "x": true }, "cost": "1" },
              { "id": "action_fallback", "type": "goapaction", "effects": { "x": true }, "cost": "1" },
              { "id": "htn_tricky", "type": "htncompound", "methods": [
                  { "name": "fancy", "subtasks": ["action_goto_berries", "action_blocked"] },
                  { "name": "plain", "subtasks": ["action_fallback"] } ] } ]
            """);
        var defs = LoadRegistry();
        var trace = new List<string>();

        var plan = HtnPlanner.Decompose("htn_tricky", State(), defs, Make, trace);

        Assert.NotNull(plan);
        Assert.Equal(["action_fallback"], plan.Steps.Select(s => s.Id)); // fancy's partial progress was rolled back
        Assert.Contains(trace, t => t.Contains("✗ fancy (backtracked)"));
        Assert.Contains(trace, t => t.Contains("✓ plain"));
    }

    [Fact]
    public void RecursionDepthBudget_StopsRunawayDecomposition()
    {
        _host.WriteContent("htn-extra.json", """
            { "id": "htn_loop", "type": "htncompound", "methods": [
                { "name": "again", "subtasks": ["htn_loop"] } ] }
            """);
        var defs = LoadRegistry();
        var trace = new List<string>();

        Assert.Null(HtnPlanner.Decompose("htn_loop", State(), defs, Make, trace));
        Assert.Contains(trace, t => t.Contains("depth budget"));
    }

    [Fact]
    public void EmptyMethod_SucceedsVacuously()
    {
        _host.WriteContent("htn-extra.json", """
            { "id": "htn_idle", "type": "htncompound", "methods": [ { "name": "nothing", "subtasks": [] } ] }
            """);
        var defs = LoadRegistry();

        var plan = HtnPlanner.Decompose("htn_idle", State(), defs, Make);

        Assert.NotNull(plan);
        Assert.Empty(plan.Steps);
    }

    [Fact]
    public void HtnBrain_RunsTheForageLoopAndRepeats()
    {
        var (session, _, ai) = _host.CreateLoadedSession();
        var forager = session.World.Spawn("entity_forager", Vector3.Zero);

        _host.TickSeconds(session, 8.0);

        var agent = ai.GetAgent(forager)!;
        Assert.IsType<HtnBrain>(agent.Brain);
        Assert.Contains(agent.Trace, t => t.Contains("htn htn_forage complete"));
        Assert.Equal(true, agent.Beliefs.Get("deliveries_done"));
        // and it re-decomposed for another run
        Assert.True(agent.Trace.Count(t => t.Contains("htn htn_forage: action_goto_berries")) >= 2
                    || agent.Trace.Count(t => t.Contains("htn htn_forage complete")) >= 2,
            string.Join("\n", agent.Trace));
    }

    [Fact]
    public void InterruptCondition_ForcesRedecompositionToTheHideMethod()
    {
        var (session, _, ai) = _host.CreateLoadedSession();
        var forager = session.World.Spawn("entity_forager", Vector3.Zero);
        _host.TickSeconds(session, 0.5); // walking to the berries

        session.World.Spawn("entity_player", new Vector3(4, 0, 0)); // hostile in view (sensitivity 0.6 -> THREAT_KNOWN)
        _host.TickSeconds(session, 1.0);

        var agent = ai.GetAgent(forager)!;
        Assert.Contains(agent.Trace, t => t.Contains("decomposition invalidated by"));
        Assert.Contains(agent.Trace, t => t.Contains("htn htn_forage: action_run_home"));
        var trace = string.Join("\n", ((HtnBrain)agent.Brain).DecompositionTrace);
        Assert.Contains("✓ hide", trace);
    }
}
