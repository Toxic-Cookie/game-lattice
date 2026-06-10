using Lattice.Core.Hosting;
using Lattice.Core.Hosting.Standalone;
using Lattice.Core.Simulation;
using Lattice.Rpg;

namespace Lattice.Narrative.Tests;

internal sealed class NullLogger : ILatticeLogger
{
    public static readonly NullLogger Instance = new();

    public void Log(LogLevel level, string message)
    {
    }
}

/// <summary>Test host with RPG + Narrative attached over a temp content directory.</summary>
internal sealed class NarrativeTestHost : IDisposable
{
    public NarrativeTestHost(int seed = 1)
    {
        ContentRoot = Path.Combine(Path.GetTempPath(), "lattice-m3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ContentRoot);
        Content = new DirectoryContentSource(ContentRoot, watch: false);
        Services = new HostServices
        {
            Host = new StandaloneHost(seed, NullLogger.Instance),
            Content = Content,
            Navigation = new StraightLineNavigationService(),
            Animation = new TimedStubAnimationService(),
            Physics = new PermissivePhysicsQueryService(),
        };
    }

    public string ContentRoot { get; }

    public DirectoryContentSource Content { get; }

    public HostServices Services { get; }

    public void WriteContent(string relativePath, string text)
    {
        var path = Path.Combine(ContentRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>Base world: stats, items, wolf/player/chest templates, the wolves quest, chest object.</summary>
    public void WriteStandardContent()
    {
        WriteContent("stats.json", """
            [
              { "id": "stat_con", "type": "stat", "key": "Con", "min": "0", "max": "99", "default": "5" },
              { "id": "stat_str", "type": "stat", "key": "Str", "min": "0", "max": "99" },
              { "id": "stat_hp", "type": "stat", "key": "HP", "min": "0", "max": "Con * 5 + 10", "default": "max", "vital": true }
            ]
            """);
        WriteContent("items.json", """
            [
              { "id": "item_gold", "type": "item", "tags": ["currency"], "stackSize": 99999, "basePrice": 1 },
              { "id": "item_sword", "type": "item", "basePrice": 25,
                "useActions": [ { "type": "DealDamage", "formula": "Str * 2 + 4" } ] }
            ]
            """);
        WriteContent("entities.json", """
            [
              { "id": "entity_player", "type": "entity", "name": "Player", "tags": ["player"],
                "stats": { "stat_con": 4, "stat_str": 8 }, "items": { "item_gold": 30, "item_sword": 1 } },
              { "id": "entity_wolf", "type": "entity", "name": "Wolf", "tags": ["wolf"],
                "stats": { "stat_con": 1, "stat_str": 3 } },
              { "id": "entity_chest", "type": "entity", "name": "Chest", "tags": ["object"] }
            ]
            """);
        WriteContent("quests.json", """
            [
              { "id": "quest_wolves", "type": "quest", "name": "Wolves at the Door",
                "steps": [
                  { "id": "cull", "description": "Cull 3 wolves",
                    "count": { "counter": "wolves_killed", "on": "Entity.Died", "where": { "defId": "entity_wolf" } },
                    "complete": { "type": "FormulaTrue", "formula": "wolves_killed >= 3" } },
                  { "id": "report",
                    "complete": { "type": "FlagEquals", "flag": "reported_to_innkeeper", "value": true },
                    "onComplete": [ { "type": "GiveItem", "item": "item_gold", "amount": "25" } ] }
                ],
                "onComplete": [ { "type": "SetFlag", "flag": "quest_wolves_done", "value": true } ] }
            ]
            """);
        WriteContent("objects.json", """
            [
              { "id": "so_chest", "type": "smartobject", "entity": "entity_chest", "maxUsers": 1,
                "interactions": [
                  { "verb": "open",
                    "conditions": [ { "type": "Not", "condition": { "type": "FlagEquals", "flag": "chest_looted", "value": true } } ],
                    "effects": [ { "type": "GiveItem", "item": "item_gold", "amount": "2d6" },
                                 { "type": "SetFlag", "flag": "chest_looted", "value": true } ] } ] }
            ]
            """);
        WriteContent("lifecycle.json", """
            { "id": "lifecycle_test", "type": "lifecycle",
              "spawns": [ { "entity": "entity_player" }, { "entity": "entity_chest" } ] }
            """);
    }

    public (GameSession Session, RpgRuntime Rpg, NarrativeRuntime Narrative) CreateLoadedSession(bool boot = true)
    {
        var session = GameSession.Create(Services, LatticeNarrative.CreateDefTypes());
        var rpg = LatticeRpg.Attach(session);
        var narrative = LatticeNarrative.Attach(session, rpg);
        var report = session.LoadContent();
        Assert.True(report.Ok, string.Join("; ", report.Errors));
        Assert.Empty(narrative.Yarn.LastErrors);
        if (boot)
        {
            session.Boot("lifecycle_test");
            session.Events.DispatchPending();
        }

        return (session, rpg, narrative);
    }

    public static void TickSeconds(GameSession session, double seconds)
    {
        var ticks = (int)Math.Round(seconds * 30);
        for (var i = 0; i < ticks; i++)
        {
            session.AdvanceTick(1f / 30f);
        }
    }

    public void Dispose()
    {
        Content.Dispose();
        try
        {
            Directory.Delete(ContentRoot, recursive: true);
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
    }
}
