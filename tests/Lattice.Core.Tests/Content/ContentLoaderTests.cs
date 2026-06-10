using Lattice.Core.Content;
using Lattice.Core.Formulas;
using Lattice.Core.Simulation;

namespace Lattice.Core.Tests.Content;

public sealed class ContentLoaderTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    private (DefRegistry Registry, ContentLoadReport Report) Load()
    {
        var registry = new DefRegistry();
        var loader = new ContentLoader(DefTypeRegistry.CreateDefault());
        var report = loader.LoadAll(_host.Content, registry);
        return (registry, report);
    }

    [Fact]
    public void Load_SingleDefAndArrayFiles()
    {
        _host.WriteContent("one.json", """{ "id": "entity_a", "type": "entity", "name": "A" }""");
        _host.WriteContent("many.json", """
            [
              { "id": "entity_b", "type": "entity" },
              { "id": "entity_c", "type": "entity" }
            ]
            """);

        var (registry, report) = Load();

        Assert.True(report.Ok, string.Join("; ", report.Errors));
        Assert.Equal(3, report.DefsLoaded);
        Assert.Equal("A", registry.Get<EntityTemplateDef>("entity_a").Name);
        Assert.Equal("many.json", registry.Get<EntityTemplateDef>("entity_c").SourceFile);
    }

    [Fact]
    public void Load_ReportsUnknownType_MissingId_BadJson()
    {
        _host.WriteContent("bad-type.json", """{ "id": "x", "type": "nonsense" }""");
        _host.WriteContent("no-id.json", """{ "type": "entity" }""");
        _host.WriteContent("broken.json", "{ not json");

        var (_, report) = Load();

        Assert.Equal(3, report.Errors.Count);
        Assert.Contains(report.Errors, e => e.Contains("unknown def type 'nonsense'"));
        Assert.Contains(report.Errors, e => e.Contains("empty 'id'"));
        Assert.Contains(report.Errors, e => e.Contains("invalid JSON"));
    }

    [Fact]
    public void Load_DuplicateIds_AreErrors()
    {
        _host.WriteContent("a.json", """{ "id": "entity_dup", "type": "entity" }""");
        _host.WriteContent("b.json", """{ "id": "entity_dup", "type": "entity" }""");

        var (_, report) = Load();

        Assert.Single(report.Errors);
        Assert.Contains("Duplicate def ID 'entity_dup'", report.Errors[0]);
    }

    [Fact]
    public void Validate_ReportsDanglingReferences()
    {
        _host.WriteContent("lifecycle.json", """
            { "id": "lifecycle_x", "type": "lifecycle",
              "spawns": [ { "entity": "entity_missing" } ] }
            """);

        var (registry, report) = Load();
        registry.Validate(report, new NCalcFormulaEngine(new LatticeRandom(0)));

        Assert.Single(report.Errors);
        Assert.Contains("entity_missing", report.Errors[0]);
        Assert.Contains("lifecycle_x.spawns", report.Errors[0]);
    }

    [Fact]
    public void BrokenFile_DoesNotApplyPartially()
    {
        var registry = new DefRegistry();
        var loader = new ContentLoader(DefTypeRegistry.CreateDefault());
        var file = new Lattice.Core.Hosting.ContentFile("mixed.json", "mixed.json");

        var report = loader.LoadFile(file, """
            [
              { "id": "entity_good", "type": "entity" },
              { "id": "entity_bad", "type": "nonsense" }
            ]
            """, registry, replace: false);

        Assert.False(report.Ok);
        Assert.False(registry.Contains("entity_good")); // staged batch discarded whole
    }

    [Fact]
    public void RemoveBySourceFile_RemovesOnlyThatFilesDefs()
    {
        _host.WriteContent("a.json", """{ "id": "entity_a", "type": "entity" }""");
        _host.WriteContent("b.json", """{ "id": "entity_b", "type": "entity" }""");
        var (registry, _) = Load();

        var removed = registry.RemoveBySourceFile("a.json");

        Assert.Equal(["entity_a"], removed);
        Assert.False(registry.Contains("entity_a"));
        Assert.True(registry.Contains("entity_b"));
    }
}
