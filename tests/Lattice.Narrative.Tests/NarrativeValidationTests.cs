using Lattice.Core.Content;
using Lattice.Core.Formulas;
using Lattice.Core.Hosting.Standalone;
using Lattice.Core.Simulation;
using Lattice.Rpg;
using Lattice.Rpg.Conditions;
using Lattice.Rpg.Effects;

namespace Lattice.Narrative.Tests;

public sealed class NarrativeValidationTests : IDisposable
{
    private readonly NarrativeTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private ContentLoadReport Validate()
    {
        using var source = new DirectoryContentSource(_host.ContentRoot, watch: false);
        var registry = new DefRegistry();
        var loader = new ContentLoader(LatticeNarrative.CreateDefTypes());
        var report = loader.LoadAll(source, registry);
        var formulas = new NCalcFormulaEngine(new LatticeRandom(0));
        var effects = BuiltinEffects.CreateDefault();
        effects.Register(new StartQuestEffect());
        var conditions = ConditionRegistry.CreateDefault();
        registry.Validate(report, formulas);
        new RpgContentValidator(effects, conditions).Validate(registry, report, formulas);
        new NarrativeContentValidator(effects, conditions).Validate(registry, report, formulas);
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
    public void TreeWithDanglingNext_IsError()
    {
        _host.WriteContent("tree.json", """
            { "id": "tree_x", "type": "dialogue", "start": "a",
              "nodes": { "a": { "line": "Hi", "next": "missing" } } }
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("missing node 'missing'"));
    }

    [Fact]
    public void TreeWithBadStart_IsError_UnreachableIsWarning()
    {
        _host.WriteContent("tree.json", """
            { "id": "tree_x", "type": "dialogue", "start": "nope",
              "nodes": { "orphan": { "line": "Hi" } } }
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("start node 'nope'"));
        Assert.Contains(report.Warnings, w => w.Contains("'orphan' is unreachable"));
    }

    [Fact]
    public void QuestWithoutCompleteCondition_IsError()
    {
        _host.WriteContent("quest.json", """
            { "id": "quest_x", "type": "quest", "steps": [ { "id": "s1" } ] }
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("no 'complete' condition"));
    }

    [Fact]
    public void StartQuestEffect_ValidatesQuestReference()
    {
        _host.WriteContent("tree.json", """
            { "id": "tree_x", "type": "dialogue", "start": "a",
              "nodes": { "a": { "line": "Hi",
                "effects": [ { "type": "StartQuest", "quest": "quest_missing" } ] } } }
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("quest_missing"));
    }

    [Fact]
    public void SmartObjectWithBadEntityBinding_IsError()
    {
        _host.WriteContent("objects.json", """
            [ { "id": "stat_x", "type": "stat", "key": "X" },
              { "id": "so_x", "type": "smartobject", "entity": "stat_x" } ]
            """);

        var report = Validate();

        Assert.Contains(report.Errors, e => e.Contains("is not an entity template"));
    }

    [Fact]
    public void BrokenYarn_KeepsSessionAliveAndReportsErrors()
    {
        _host.WriteStandardContent();
        _host.WriteContent("dialogue/broken.yarn", """
            title: Broken
            ---
            <<if flag_bool("x")>>
            Greeter: Never closed the if.
            ===
            """);

        var session = GameSession.Create(_host.Services, LatticeNarrative.CreateDefTypes());
        var rpg = LatticeRpg.Attach(session);
        var narrative = LatticeNarrative.Attach(session, rpg);
        var report = session.LoadContent(); // yarn compile failure must not fail the JSON load

        Assert.True(report.Ok);
        Assert.NotEmpty(narrative.Yarn.LastErrors);
        Assert.Null(narrative.Yarn.Program); // nothing usable, but the session lives
    }
}
