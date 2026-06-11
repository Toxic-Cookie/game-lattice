using Lattice.Core.Content;

namespace Lattice.Core.Tests;

/// <summary>
/// Blueprint inheritance (plan/06 §4): scalar override, object deep-merge,
/// array replace vs $append/$remove, cross-file chains, and the failure
/// modes (unknown parent, cycle, kind mismatch, depth).
/// </summary>
public sealed class BlueprintTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public void Child_OverridesScalarsAndMergesObjects()
    {
        _host.WriteContent("entities.json", """
            [ { "id": "npc_base", "type": "entity", "name": "Base", "tags": ["npc"],
                "stats": { "stat_str": 3, "stat_con": 3 } },
              { "id": "npc_elite", "type": "entity", "inherits": "npc_base", "name": "Elite",
                "stats": { "stat_str": 8 } } ]
            """);
        var session = _host.CreateSession();
        var report = session.LoadContent();
        Assert.True(report.Ok, string.Join("; ", report.Errors));

        var elite = session.Defs.Get<EntityTemplateDef>("npc_elite");
        Assert.Equal("Elite", elite.Name);                  // scalar override
        Assert.Equal(["npc"], elite.Tags);                  // array inherited untouched
        Assert.Equal(8, elite.Stats!["stat_str"]);          // object deep-merge: overridden key
        Assert.Equal(3, elite.Stats["stat_con"]);           // object deep-merge: inherited key
        Assert.Equal("npc_base", elite.Inherits);

        var baseDef = session.Defs.Get<EntityTemplateDef>("npc_base");
        Assert.Equal("Base", baseDef.Name); // the parent is itself untouched
        Assert.Equal(3, baseDef.Stats!["stat_str"]);
    }

    [Fact]
    public void ArrayOperators_AppendAndRemove()
    {
        _host.WriteContent("entities.json", """
            [ { "id": "npc_base", "type": "entity", "tags": ["npc", "guard", "human"] },
              { "id": "npc_elite", "type": "entity", "inherits": "npc_base",
                "tags": { "$append": ["elite"], "$remove": ["human"] } },
              { "id": "npc_plain", "type": "entity", "inherits": "npc_base",
                "tags": ["other"] } ]
            """);
        var session = _host.CreateSession();
        Assert.True(session.LoadContent().Ok);

        Assert.Equal(["npc", "guard", "elite"], session.Defs.Get<EntityTemplateDef>("npc_elite").Tags);
        Assert.Equal(["other"], session.Defs.Get<EntityTemplateDef>("npc_plain").Tags); // plain arrays replace
    }

    [Fact]
    public void Chains_ResolveAcrossFilesInAnyOrder()
    {
        // the grandparent sorts *after* the children's file on purpose
        _host.WriteContent("a-children.json", """
            { "id": "npc_elite", "type": "entity", "inherits": "npc_mid", "stats": { "stat_str": 9 } }
            """);
        _host.WriteContent("z-bases.json", """
            [ { "id": "npc_mid", "type": "entity", "inherits": "npc_root", "tags": { "$append": ["soldier"] } },
              { "id": "npc_root", "type": "entity", "name": "Root", "tags": ["npc"], "stats": { "stat_str": 1, "stat_con": 2 } } ]
            """);
        var session = _host.CreateSession();
        Assert.True(session.LoadContent().Ok);

        var elite = session.Defs.Get<EntityTemplateDef>("npc_elite");
        Assert.Equal("Root", elite.Name);
        Assert.Equal(["npc", "soldier"], elite.Tags);
        Assert.Equal(9, elite.Stats!["stat_str"]);
        Assert.Equal(2, elite.Stats["stat_con"]);
    }

    [Fact]
    public void UnknownParent_IsAnError()
    {
        _host.WriteContent("entities.json", """
            { "id": "npc_x", "type": "entity", "inherits": "npc_ghost" }
            """);
        var session = _host.CreateSession();
        var report = session.LoadContent();

        Assert.Contains(report.Errors, e => e.Contains("inherits unknown def 'npc_ghost'"));
    }

    [Fact]
    public void Cycle_IsAnError()
    {
        _host.WriteContent("entities.json", """
            [ { "id": "npc_a", "type": "entity", "inherits": "npc_b" },
              { "id": "npc_b", "type": "entity", "inherits": "npc_a" } ]
            """);
        var session = _host.CreateSession();
        var report = session.LoadContent();

        Assert.Contains(report.Errors, e => e.Contains("inheritance cycle"));
    }

    [Fact]
    public void KindMismatch_IsAnError()
    {
        _host.WriteContent("mixed.json", """
            [ { "id": "lifecycle_base", "type": "lifecycle" },
              { "id": "npc_x", "type": "entity", "inherits": "lifecycle_base" } ]
            """);
        var session = _host.CreateSession();
        var report = session.LoadContent();

        Assert.Contains(report.Errors, e => e.Contains("kinds must match"));
    }

    [Fact]
    public void MalformedArrayPatch_IsAnError()
    {
        _host.WriteContent("entities.json", """
            [ { "id": "npc_base", "type": "entity", "tags": ["npc"] },
              { "id": "npc_x", "type": "entity", "inherits": "npc_base",
                "tags": { "$append": ["a"], "extra": true } } ]
            """);
        var session = _host.CreateSession();
        var report = session.LoadContent();

        Assert.Contains(report.Errors, e => e.Contains("may only contain $append/$remove"));
    }

    [Fact]
    public void SchemaHeaderWrapper_LoadsItsDefs()
    {
        _host.WriteContent("wrapped.json", """
            { "$schema": "../schemas/lattice.schema.json",
              "defs": [
                { "id": "npc_a", "type": "entity", "name": "A" },
                { "id": "npc_b", "type": "entity", "name": "B" } ] }
            """);
        var session = _host.CreateSession();
        var report = session.LoadContent();

        Assert.True(report.Ok, string.Join("; ", report.Errors));
        Assert.Equal(2, report.DefsLoaded);
        Assert.True(session.Defs.Contains("npc_b"));
    }
}
