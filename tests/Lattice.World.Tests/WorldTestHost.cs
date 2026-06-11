using Lattice.Core.Hosting;
using Lattice.Core.Hosting.Standalone;
using Lattice.Core.Simulation;
using Lattice.Rpg;
using Lattice.World.Navigation;

namespace Lattice.World.Tests;

internal sealed class NullLogger : ILatticeLogger
{
    public static readonly NullLogger Instance = new();

    public void Log(LogLevel level, string message)
    {
    }
}

/// <summary>Test host with RPG + World attached over a temp content directory and a grid nav service.</summary>
internal sealed class WorldTestHost : IDisposable
{
    public WorldTestHost(int seed = 1)
    {
        ContentRoot = Path.Combine(Path.GetTempPath(), "lattice-m5-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ContentRoot);
        Content = new DirectoryContentSource(ContentRoot, watch: false);
        Navigation = new GridNavigationService();
        Services = new HostServices
        {
            Host = new StandaloneHost(seed, NullLogger.Instance),
            Content = Content,
            Navigation = Navigation,
            Animation = new TimedStubAnimationService(animationDurationSeconds: 0.4),
            Physics = new PermissivePhysicsQueryService(),
        };
    }

    public string ContentRoot { get; }

    public DirectoryContentSource Content { get; }

    public GridNavigationService Navigation { get; }

    public HostServices Services { get; }

    public void WriteContent(string relativePath, string text)
    {
        var path = Path.Combine(ContentRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>Clock (12 real min/day = 1 game hour per 30s), phases, two weathers, two seasons with a loot redirect.</summary>
    public void WriteStandardContent()
    {
        WriteContent("stats.json", """
            { "id": "stat_con", "type": "stat", "key": "Con", "min": "0", "max": "99", "default": "3" }
            """);
        WriteContent("items.json", """
            [ { "id": "item_gold", "type": "item", "name": "Gold" },
              { "id": "item_pelt", "type": "item", "name": "Pelt" },
              { "id": "item_thick_pelt", "type": "item", "name": "Thick Pelt" } ]
            """);
        WriteContent("loot.json", """
            [ { "id": "loot_forest", "type": "loot", "entries": [ { "item": "item_pelt", "weight": 1 } ] },
              { "id": "loot_forest_winter", "type": "loot", "entries": [ { "item": "item_thick_pelt", "weight": 1 } ] } ]
            """);
        WriteContent("world.json", """
            [ { "id": "time_test", "type": "time", "minutesPerGameDay": 12, "daysPerSeason": 1,
                "seasons": ["season_summer", "season_winter"], "startHour": 8 },
              { "id": "dayphases_test", "type": "dayphases", "phases": [
                  { "name": "dawn",  "fromHour": 5,  "toHour": 7,  "light": 0.4 },
                  { "name": "day",   "fromHour": 7,  "toHour": 20, "light": 1.0 },
                  { "name": "dusk",  "fromHour": 20, "toHour": 22, "light": 0.5 },
                  { "name": "night", "fromHour": 22, "toHour": 5,  "light": 0.15 } ] },
              { "id": "weather_clear", "type": "weather",
                "transitions": { "weather_clear": 7, "weather_rain": 3 }, "minHours": 2, "maxHours": 4 },
              { "id": "weather_rain", "type": "weather",
                "flags": { "sense_auditory_mult": 0.5 },
                "transitions": { "weather_clear": 6, "weather_rain": 4 }, "minHours": 1, "maxHours": 3 },
              { "id": "season_summer", "type": "season", "weatherBias": { "weather_rain": 0.5 } },
              { "id": "season_winter", "type": "season",
                "redirects": { "loot_forest": "loot_forest_winter" }, "weatherBias": { "weather_rain": 2.0 } } ]
            """);
        WriteContent("lifecycle.json", """
            { "id": "lifecycle_test", "type": "lifecycle", "spawns": [] }
            """);
    }

    public (GameSession Session, RpgRuntime Rpg, WorldRuntime World) CreateLoadedSession()
    {
        var session = GameSession.Create(Services, LatticeWorld.AddDefTypes(LatticeRpg.CreateDefTypes()));
        var rpg = LatticeRpg.Attach(session);
        var world = LatticeWorld.Attach(session, rpg);
        var report = session.LoadContent();
        Assert.True(report.Ok, string.Join("; ", report.Errors));
        session.Boot("lifecycle_test");
        session.Events.DispatchPending();
        return (session, rpg, world);
    }

    /// <summary>Advance simulation seconds (30 ticks/s). One game hour = 30 real seconds with the standard clock.</summary>
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
