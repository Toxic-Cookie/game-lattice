using Lattice.Core.Content;

namespace Lattice.Core.Tests;

/// <summary>
/// Content-pack layering (plan/06 §5): directories with a pack.json load
/// after the base content in priority/dependency order, overriding same-ID
/// defs — the registry-overlay mechanism mods share with seasons.
/// </summary>
public sealed class PackTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public void LaterPack_OverridesBaseDefs()
    {
        _host.WriteContent("entities.json", """
            { "id": "npc_x", "type": "entity", "name": "Base", "tags": ["npc"] }
            """);
        _host.WriteContent("mods/coolmod/pack.json", """
            { "id": "coolmod", "version": "1.0" }
            """);
        _host.WriteContent("mods/coolmod/overrides.json", """
            { "id": "npc_x", "type": "entity", "name": "Modded" }
            """);
        var session = _host.CreateSession();
        var report = session.LoadContent();

        Assert.True(report.Ok, string.Join("; ", report.Errors));
        Assert.Equal("Modded", session.Defs.Get<EntityTemplateDef>("npc_x").Name);
    }

    [Fact]
    public void PackOrder_FollowsPriorityThenId()
    {
        _host.WriteContent("base.json", """{ "id": "npc_x", "type": "entity", "name": "Base" }""");
        _host.WriteContent("mods/a/pack.json", """{ "id": "a", "priority": 10 }""");
        _host.WriteContent("mods/a/defs.json", """{ "id": "npc_x", "type": "entity", "name": "FromA" }""");
        _host.WriteContent("mods/b/pack.json", """{ "id": "b", "priority": 5 }""");
        _host.WriteContent("mods/b/defs.json", """{ "id": "npc_x", "type": "entity", "name": "FromB" }""");
        var session = _host.CreateSession();

        Assert.True(session.LoadContent().Ok);
        Assert.Equal("FromA", session.Defs.Get<EntityTemplateDef>("npc_x").Name); // priority 10 loads last
    }

    [Fact]
    public void Dependencies_LoadBeforeDependents()
    {
        // expansion depends on basegame even though its priority sorts it first
        _host.WriteContent("mods/expansion/pack.json", """{ "id": "expansion", "priority": -5, "dependencies": ["basegame"] }""");
        _host.WriteContent("mods/expansion/defs.json", """{ "id": "npc_x", "type": "entity", "name": "Expanded" }""");
        _host.WriteContent("mods/basegame/pack.json", """{ "id": "basegame" }""");
        _host.WriteContent("mods/basegame/defs.json", """{ "id": "npc_x", "type": "entity", "name": "Vanilla" }""");
        var session = _host.CreateSession();

        Assert.True(session.LoadContent().Ok);
        Assert.Equal("Expanded", session.Defs.Get<EntityTemplateDef>("npc_x").Name);
    }

    [Fact]
    public void MissingDependency_IsAnError()
    {
        _host.WriteContent("mods/orphan/pack.json", """{ "id": "orphan", "dependencies": ["nonexistent"] }""");
        _host.WriteContent("mods/orphan/defs.json", """{ "id": "npc_x", "type": "entity" }""");
        var session = _host.CreateSession();
        var report = session.LoadContent();

        Assert.Contains(report.Errors, e => e.Contains("depends on missing pack(s): nonexistent"));
    }

    [Fact]
    public void DuplicateIdsWithinOneScope_RemainErrors()
    {
        _host.WriteContent("a.json", """{ "id": "npc_x", "type": "entity" }""");
        _host.WriteContent("b.json", """{ "id": "npc_x", "type": "entity" }""");
        var session = _host.CreateSession();
        var report = session.LoadContent();

        Assert.Contains(report.Errors, e => e.Contains("Duplicate def ID 'npc_x'"));
    }

    [Fact]
    public void PackDefs_MayInheritBaseDefs()
    {
        _host.WriteContent("entities.json", """
            { "id": "npc_base", "type": "entity", "name": "Base", "stats": { "stat_con": 2 } }
            """);
        _host.WriteContent("mods/m/pack.json", """{ "id": "m" }""");
        _host.WriteContent("mods/m/defs.json", """
            { "id": "npc_modded", "type": "entity", "inherits": "npc_base", "name": "Modded" }
            """);
        var session = _host.CreateSession();

        Assert.True(session.LoadContent().Ok);
        var modded = session.Defs.Get<EntityTemplateDef>("npc_modded");
        Assert.Equal("Modded", modded.Name);
        Assert.Equal(2, modded.Stats!["stat_con"]);
    }
}
