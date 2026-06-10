using Lattice.Core.Hosting;
using Lattice.Core.Hosting.Standalone;
using Lattice.Core.Simulation;

namespace Lattice.Rpg.Tests;

internal sealed class NullLogger : ILatticeLogger
{
    public static readonly NullLogger Instance = new();

    public void Log(LogLevel level, string message)
    {
    }
}

/// <summary>
/// Test host with the RPG module attached and a standard content set
/// matching the demo content (stats, slots, items, poison, loot, shop).
/// </summary>
internal sealed class RpgTestHost : IDisposable
{
    public RpgTestHost(int seed = 1)
    {
        ContentRoot = Path.Combine(Path.GetTempPath(), "lattice-m2-" + Guid.NewGuid().ToString("N"));
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

    public void WriteContent(string relativePath, string json)
    {
        var path = Path.Combine(ContentRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    public void WriteStandardContent()
    {
        WriteContent("stats.json", """
            [
              { "id": "stat_con", "type": "stat", "key": "Con", "min": "0", "max": "99", "default": "5" },
              { "id": "stat_str", "type": "stat", "key": "Str", "min": "0", "max": "99" },
              { "id": "stat_cha", "type": "stat", "key": "Charisma", "min": "0", "max": "99" },
              { "id": "stat_hp", "type": "stat", "key": "HP", "min": "0", "max": "Con * 5 + 10", "default": "max", "vital": true },
              { "id": "stat_carry", "type": "stat", "key": "Carry", "formula": "Str * 10" }
            ]
            """);
        WriteContent("slots.json", """
            [ { "id": "slot_main_hand", "type": "slot" }, { "id": "slot_chest", "type": "slot" } ]
            """);
        WriteContent("items.json", """
            [
              { "id": "item_gold", "type": "item", "tags": ["currency"], "stackSize": 99999, "basePrice": 1 },
              { "id": "item_iron_sword", "type": "item", "slot": "slot_main_hand", "basePrice": 25,
                "useActions": [ { "type": "DealDamage", "formula": "Str * 2 + 4" } ],
                "equipEffects": [ { "type": "FlatModifier", "stat": "stat_str", "amount": 1 },
                                  { "type": "TagModifier", "addTags": ["armed"] } ] },
              { "id": "item_wolf_fang", "type": "item", "basePrice": 3,
                "useActions": [ { "type": "DealDamage", "formula": "Str * 2" },
                                { "type": "ApplyStatus", "status": "status_poison" } ] },
              { "id": "item_wolf_pelt", "type": "item", "basePrice": 8 },
              { "id": "item_healing_potion", "type": "item", "basePrice": 12, "consumeOnUse": true,
                "useActions": [ { "type": "Heal", "formula": "10" },
                                { "type": "RemoveStatus", "status": "status_poison" } ] }
            ]
            """);
        WriteContent("statuses.json", """
            [
              { "id": "status_poison", "type": "status", "duration": 6.0, "stacking": "refresh",
                "logic": [
                  { "type": "PeriodicEffect", "interval": 1.0, "effects": [ { "type": "DealDamage", "formula": "2" } ] },
                  { "type": "TagModifier", "addTags": ["poisoned"] } ] },
              { "id": "status_might", "type": "status", "duration": 10.0, "stacking": "stack", "maxStacks": 3,
                "logic": [ { "type": "FlatModifier", "stat": "stat_str", "amount": 2 } ] }
            ]
            """);
        WriteContent("loot.json", """
            [
              { "id": "loot_wolf", "type": "loot", "rolls": 2, "entries": [
                  { "item": "item_gold", "weight": 50, "amount": "1d10+5" },
                  { "item": "item_wolf_pelt", "weight": 40 },
                  { "tableRef": "loot_rare", "weight": 5 },
                  { "weight": 5 } ] },
              { "id": "loot_rare", "type": "loot", "entries": [ { "item": "item_healing_potion", "weight": 1 } ] }
            ]
            """);
        WriteContent("shops.json", """
            [
              { "id": "shop_trader", "type": "shop",
                "buyPriceFormula": "BasePrice * (2.0 - Charisma * 0.01)",
                "sellPriceFormula": "BasePrice * (0.4 + Charisma * 0.01)",
                "restockOn": "Time.DayStarted",
                "stock": [ { "item": "item_iron_sword", "count": 2 }, { "item": "item_healing_potion", "count": 5 } ] }
            ]
            """);
        WriteContent("entities.json", """
            [
              { "id": "entity_player", "type": "entity", "name": "Player", "tags": ["player"],
                "stats": { "stat_con": 4, "stat_str": 8, "stat_cha": 10 },
                "items": { "item_gold": 30, "item_iron_sword": 1 } },
              { "id": "entity_wolf", "type": "entity", "name": "Wolf", "tags": ["wolf"],
                "stats": { "stat_con": 1, "stat_str": 3 },
                "items": { "item_wolf_fang": 1 },
                "lootTable": "loot_wolf" }
            ]
            """);
        WriteContent("lifecycle.json", """
            { "id": "lifecycle_test", "type": "lifecycle",
              "spawns": [ { "entity": "entity_player" }, { "entity": "entity_wolf" } ] }
            """);
    }

    /// <summary>Create a session with the RPG module attached and content loaded.</summary>
    public (GameSession Session, RpgRuntime Rpg) CreateLoadedSession(bool boot = true)
    {
        var session = GameSession.Create(Services, LatticeRpg.CreateDefTypes());
        var rpg = LatticeRpg.Attach(session);
        var report = session.LoadContent();
        Assert.True(report.Ok, string.Join("; ", report.Errors));
        if (boot)
        {
            session.Boot("lifecycle_test");
            session.Events.DispatchPending();
        }

        return (session, rpg);
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
