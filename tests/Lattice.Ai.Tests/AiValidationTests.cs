using Lattice.Ai.Tasks;
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
        registry.Validate(report, formulas);
        new AiContentValidator(conditions, TaskRegistry.CreateDefault()).Validate(registry, report, formulas);
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
              { "id": "profile_x", "type": "agent", "brain": "goap" } ]
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("unknown brain tier 'goap'"));
    }
}
