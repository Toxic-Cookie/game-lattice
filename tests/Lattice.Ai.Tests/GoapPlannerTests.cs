using Lattice.Ai.Goap;

namespace Lattice.Ai.Tests;

/// <summary>
/// The planner is a pure function (plan/04 §7), so these are table-driven:
/// no host, no world, no formulas — just states, actions, and budgets.
/// </summary>
public sealed class GoapPlannerTests
{
    private static GoapCandidate Make(string id, double cost, Dictionary<string, object>? pre = null, Dictionary<string, object>? eff = null)
        => new()
        {
            Id = id,
            Cost = cost,
            Preconditions = pre ?? new Dictionary<string, object>(StringComparer.Ordinal),
            Effects = eff ?? new Dictionary<string, object>(StringComparer.Ordinal),
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
    public void CheaperChain_BeatsExpensiveSingleAction()
    {
        // the canonical ch03 §3.3 example: cost 1 + 2 beats cost 5
        var actions = new[]
        {
            Make("direct_assault", 5, eff: State(("enemy_dead", true))),
            Make("draw_weapon", 1, eff: State(("armed", true))),
            Make("attack", 2, pre: State(("armed", true)), eff: State(("enemy_dead", true))),
        };

        var plan = GoapPlanner.Plan(actions, State(), State(("enemy_dead", true)));

        Assert.NotNull(plan);
        Assert.Equal(["draw_weapon", "attack"], plan.Steps.Select(s => s.Id));
        Assert.Equal(3, plan.TotalCost);
    }

    [Fact]
    public void SingleAction_WinsWhenChainsAreCostlier()
    {
        var actions = new[]
        {
            Make("direct", 2, eff: State(("done", true))),
            Make("step_a", 2, eff: State(("half", true))),
            Make("step_b", 2, pre: State(("half", true)), eff: State(("done", true))),
        };

        var plan = GoapPlanner.Plan(actions, State(), State(("done", true)));

        Assert.NotNull(plan);
        Assert.Equal(["direct"], plan.Steps.Select(s => s.Id));
    }

    [Fact]
    public void MultiStepChain_IsFoundThroughTheMismatchHeuristic()
    {
        var actions = new[]
        {
            Make("goto_armory", 1, eff: State(("at_armory", true))),
            Make("grab_rifle", 1, pre: State(("at_armory", true)), eff: State(("armed", true))),
            Make("goto_wall", 1, pre: State(("armed", true)), eff: State(("at_wall", true))),
            Make("stand_guard", 1, pre: State(("at_wall", true), ("armed", true)), eff: State(("guarding", true))),
        };

        var plan = GoapPlanner.Plan(actions, State(), State(("guarding", true)));

        Assert.NotNull(plan);
        Assert.Equal(["goto_armory", "grab_rifle", "goto_wall", "stand_guard"], plan.Steps.Select(s => s.Id));
    }

    [Fact]
    public void MaxPlanLength_BoundsTheSearch()
    {
        // a 5-step staircase cannot fit in the default budget of 4 (ch07 anti-pattern 5)
        var actions = Enumerable.Range(0, 5)
            .Select(i => Make($"step_{i}", 1,
                pre: i == 0 ? null : State(($"s{i - 1}", true)),
                eff: State(($"s{i}", true))))
            .ToArray();

        Assert.Null(GoapPlanner.Plan(actions, State(), State(("s4", true))));
        Assert.NotNull(GoapPlanner.Plan(actions, State(), State(("s4", true)), maxPlanLength: 5));
    }

    [Fact]
    public void OpenNodeBudget_DegradesToNoPlanInsteadOfHanging()
    {
        var actions = new[]
        {
            Make("noise_a", 1, eff: State(("a", true))),
            Make("noise_b", 1, eff: State(("b", true))),
            Make("win", 1, pre: State(("a", true), ("b", true)), eff: State(("done", true))),
        };

        Assert.Null(GoapPlanner.Plan(actions, State(), State(("done", true)), maxOpenNodes: 2));
        Assert.NotNull(GoapPlanner.Plan(actions, State(), State(("done", true))));
    }

    [Fact]
    public void ImpossibleGoal_ReturnsNull()
    {
        var actions = new[] { Make("unrelated", 1, eff: State(("x", true))) };

        Assert.Null(GoapPlanner.Plan(actions, State(), State(("y", true))));
    }

    [Fact]
    public void MissingBooleanKey_ReadsAsFalse()
    {
        // "concealed: false" must match an agent that has never heard of concealment
        var actions = new[]
        {
            Make("sneak", 1, pre: State(("spotted", false)), eff: State(("inside", true))),
        };

        var plan = GoapPlanner.Plan(actions, State(), State(("inside", true)));

        Assert.NotNull(plan);
        Assert.Equal(["sneak"], plan.Steps.Select(s => s.Id));
    }

    [Fact]
    public void NonBooleanPredicates_MatchByEquality()
    {
        var actions = new[]
        {
            Make("equip_torch", 1, pre: State(("held_item", "torch")), eff: State(("lit", true))),
            Make("draw_torch", 1, eff: State(("held_item", "torch"))),
        };

        var plan = GoapPlanner.Plan(actions, State(("held_item", "sword")), State(("lit", true)));

        Assert.NotNull(plan);
        Assert.Equal(["draw_torch", "equip_torch"], plan.Steps.Select(s => s.Id));
    }

    [Fact]
    public void EqualCostAlternatives_ResolveDeterministically()
    {
        var actions = new[]
        {
            Make("door_a", 1, eff: State(("inside", true))),
            Make("door_b", 1, eff: State(("inside", true))),
        };

        for (var i = 0; i < 5; i++)
        {
            var plan = GoapPlanner.Plan(actions, State(), State(("inside", true)));
            Assert.NotNull(plan);
            Assert.Equal("door_a", plan.Steps.Single().Id); // first candidate wins ties, every time
        }
    }

    [Fact]
    public void Simulate_AcceptsValidAndRejectsBrokenPlans()
    {
        var draw = Make("draw", 1, eff: State(("armed", true)));
        var attack = Make("attack", 1, pre: State(("armed", true)), eff: State(("won", true)));
        var goal = State(("won", true));

        var good = new GoapPlan { Steps = [draw, attack], TotalCost = 2, NodesExplored = 0 };
        var reordered = new GoapPlan { Steps = [attack, draw], TotalCost = 2, NodesExplored = 0 };
        var incomplete = new GoapPlan { Steps = [draw], TotalCost = 1, NodesExplored = 0 };

        Assert.True(GoapBrain.Simulate(good, State(), goal));
        Assert.False(GoapBrain.Simulate(reordered, State(), goal)); // mechanism 1 catches ordering breaks
        Assert.False(GoapBrain.Simulate(incomplete, State(), goal));
    }
}
