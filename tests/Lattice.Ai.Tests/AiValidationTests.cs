using Lattice.Ai.Tasks;
using Lattice.Ai.Utility;
using Lattice.Core.Content;
using Lattice.Core.Formulas;
using Lattice.Core.Hosting.Standalone;
using Lattice.Core.Simulation;
using Lattice.Rpg.Conditions;

namespace Lattice.Ai.Tests;

public sealed class AiValidationTests : IDisposable
{
    private readonly AiTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private ContentLoadReport Validate()
    {
        using var source = new DirectoryContentSource(_host.ContentRoot, watch: false);
        var registry = new DefRegistry();
        var loader = new ContentLoader(LatticeAi.CreateDefTypes());
        var report = loader.LoadAll(source, registry);
        var formulas = new NCalcFormulaEngine(new LatticeRandom(0));
        var conditions = ConditionRegistry.CreateDefault();
        conditions.Register(new AgentConditionEvaluator());
        conditions.Register(new AgentMetaCondition());
        conditions.Register(new BeliefEqualsCondition());
        conditions.Register(new UtilityAtLeastCondition());
        conditions.Register(new NeedBelowCondition());
        registry.Validate(report, formulas);
        new AiContentValidator(conditions, TaskRegistry.CreateDefault(), Lattice.Rpg.Effects.BuiltinEffects.CreateDefault())
            .Validate(registry, report, formulas);
        return report;
    }

    [Fact]
    public void StandardContent_Validates()
    {
        _host.WriteStandardContent();

        var report = Validate();

        Assert.True(report.Ok, string.Join("; ", report.Errors));
    }

    [Fact]
    public void CatalogOver32Conditions_IsError()
    {
        var names = string.Join(",", Enumerable.Range(0, 33).Select(i => $"\"C{i}\""));
        _host.WriteContent("conditions.json", $$"""
            { "id": "conditions_big", "type": "conditions", "names": [{{names}}] }
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("the budget is 32"));
    }

    [Fact]
    public void ScheduleConditionMissingFromCatalog_IsError()
    {
        _host.WriteContent("ai.json", """
            [ { "id": "conditions_default", "type": "conditions", "names": ["CAN_SEE_ENEMY"] },
              { "id": "entity_x", "type": "entity" },
              { "id": "schedule_x", "type": "schedule", "require": ["NOT_A_CONDITION"],
                "tasks": [ { "task": "Wait", "seconds": 1 } ] },
              { "id": "profile_x", "type": "agent", "entities": ["entity_x"],
                "brain": "schedules", "schedules": ["schedule_x"] } ]
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("NOT_A_CONDITION") && e.Contains("missing from catalog"));
    }

    [Fact]
    public void UnknownTaskType_IsError()
    {
        _host.WriteContent("schedule.json", """
            { "id": "schedule_x", "type": "schedule", "tasks": [ { "task": "Backflip" } ] }
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("unknown task type 'Backflip'"));
    }

    [Fact]
    public void FsmTransitionToMissingState_IsError()
    {
        _host.WriteContent("brain.json", """
            { "id": "fsmbrain_x", "type": "fsmbrain", "initial": "a",
              "states": { "a": { "transitions": [ { "to": "nowhere" } ] } } }
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("missing state 'nowhere'"));
    }

    [Fact]
    public void FsmBrainProfileWithoutBrainDef_IsError()
    {
        _host.WriteContent("ai.json", """
            [ { "id": "conditions_default", "type": "conditions", "names": [] },
              { "id": "entity_x", "type": "entity" },
              { "id": "profile_x", "type": "agent", "entities": ["entity_x"], "brain": "fsm" } ]
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("declares no 'fsmBrain'"));
    }

    [Fact]
    public void UnknownBrainTier_IsError()
    {
        _host.WriteContent("ai.json", """
            [ { "id": "conditions_default", "type": "conditions", "names": [] },
              { "id": "profile_x", "type": "agent", "brain": "psychic" } ]
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("unknown brain tier 'psychic'"));
    }

    [Fact]
    public void BtBrainProfileWithoutTreeDef_IsError()
    {
        _host.WriteContent("ai.json", """
            [ { "id": "conditions_default", "type": "conditions", "names": [] },
              { "id": "profile_x", "type": "agent", "brain": "bt" } ]
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("declares no 'behaviorTree'"));
    }

    [Fact]
    public void UnknownBtNodeKind_IsError()
    {
        _host.WriteContent("bt.json", """
            { "id": "btree_x", "type": "btree", "root": { "node": "Parallel", "children": [] } }
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("unknown behavior tree node 'Parallel'"));
    }

    [Fact]
    public void SequenceWithoutChildren_IsError()
    {
        _host.WriteContent("bt.json", """
            { "id": "btree_x", "type": "btree", "root": { "node": "Sequence" } }
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("requires a non-empty 'children' array"));
    }

    [Fact]
    public void ConditionGateWithoutWhen_IsError()
    {
        _host.WriteContent("bt.json", """
            { "id": "btree_x", "type": "btree", "root": {
                "node": "ConditionGate", "child": { "task": "Wait", "seconds": 1 } } }
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("requires a 'when' condition array"));
    }

    [Fact]
    public void SubtreeCycle_IsError()
    {
        _host.WriteContent("bt.json", """
            [ { "id": "btree_a", "type": "btree", "root": { "subtree": "btree_b" } },
              { "id": "btree_b", "type": "btree", "root": { "subtree": "btree_a" } } ]
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("subtree reference cycle"));
    }

    [Fact]
    public void BtTaskLeafPayload_IsValidated()
    {
        _host.WriteContent("bt.json", """
            { "id": "btree_x", "type": "btree", "root": { "task": "Backflip" } }
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("unknown task type 'Backflip'"));
    }

    [Fact]
    public void UtilityEvaluatorWithBadFormula_IsError()
    {
        _host.WriteContent("utility.json", """
            { "id": "utility_x", "type": "utility", "factors": [ { "formula": "1 +" } ] }
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("utility_x") && e.Contains("factor formula"));
    }

    [Fact]
    public void UtilityEvaluatorWithoutFactors_IsError()
    {
        _host.WriteContent("utility.json", """
            { "id": "utility_x", "type": "utility" }
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("declares no factors"));
    }

    [Fact]
    public void ActivitySatisfyingANonNeed_IsError()
    {
        _host.WriteContent("ai.json", """
            [ { "id": "entity_x", "type": "entity" },
              { "id": "activity_x", "type": "activity", "satisfies": { "entity_x": 0.5 },
                "tasks": [ { "task": "Wait", "seconds": 1 } ] } ]
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("'entity_x', which is not a need def"));
    }

    [Fact]
    public void AgentMetaWithUnknownState_IsError()
    {
        _host.WriteContent("bt.json", """
            { "id": "btree_x", "type": "btree", "root": {
                "node": "ConditionGate", "when": [ { "type": "AgentMeta", "is": "Panicking" } ],
                "child": { "task": "Wait", "seconds": 1 } } }
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("AgentMeta requires 'is'"));
    }

    [Fact]
    public void GoapProfileWithoutGoals_IsError()
    {
        _host.WriteContent("ai.json", """
            [ { "id": "conditions_default", "type": "conditions", "names": [] },
              { "id": "profile_x", "type": "agent", "brain": "goap" } ]
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("declares no 'goals'"));
    }

    [Fact]
    public void GoapActionWithoutEffects_IsError()
    {
        _host.WriteContent("goap.json", """
            { "id": "action_x", "type": "goapaction", "cost": "1" }
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("has no effects"));
    }

    [Fact]
    public void GoapGoalWithBadPriorityFormula_IsError()
    {
        _host.WriteContent("goap.json", """
            { "id": "goal_x", "type": "goapgoal", "desired": { "ok": true }, "priority": "1 +" }
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("goal_x") && e.Contains("priority formula"));
    }

    [Fact]
    public void GoapReplanConditionMissingFromCatalog_IsError()
    {
        _host.WriteContent("ai.json", """
            [ { "id": "conditions_default", "type": "conditions", "names": ["CAN_SEE_ENEMY"] },
              { "id": "entity_x", "type": "entity" },
              { "id": "goal_x", "type": "goapgoal", "desired": { "ok": true }, "priority": "1",
                "replanRequired": ["NOT_A_CONDITION"] },
              { "id": "profile_x", "type": "agent", "entities": ["entity_x"],
                "brain": "goap", "goals": ["goal_x"] } ]
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("NOT_A_CONDITION") && e.Contains("missing from catalog"));
    }

    [Fact]
    public void GoapActionRunEffects_AreValidated()
    {
        _host.WriteContent("goap.json", """
            { "id": "action_x", "type": "goapaction", "effects": { "done": true },
              "runEffects": [ { "type": "Explode" } ] }
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("unknown effect type 'Explode'"));
    }

    [Fact]
    public void NeedWithOutOfRangeInitial_IsError()
    {
        _host.WriteContent("need.json", """
            { "id": "need_x", "type": "need", "key": "X", "initial": 1.5 }
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("initial value must be 0–1"));
    }
}
