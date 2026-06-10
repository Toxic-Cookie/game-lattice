using Lattice.Core.Persistence;
using Lattice.Core.Simulation;

namespace Lattice.Core.Tests.Persistence;

public sealed class SaveManagerTests : IDisposable
{
    private readonly TestHost _host = new(seed: 77);

    public void Dispose() => _host.Dispose();

    private GameSession BootedSession()
    {
        _host.WriteContent("content.json", """
            [
              { "id": "lifecycle_test", "type": "lifecycle",
                "globalFlags": { "tutorial_done": false },
                "spawns": [ { "entity": "entity_wolf", "count": 2, "position": [3, 0, 1] } ] },
              { "id": "entity_wolf", "type": "entity", "name": "Wolf",
                "tags": ["wolf"], "stats": { "HP": 12 } }
            ]
            """);
        var session = _host.CreateSession();
        session.LoadContent();
        session.Boot("lifecycle_test");
        return session;
    }

    [Fact]
    public void CaptureRestore_RoundTripsWorldDelta()
    {
        var session = BootedSession();
        session.AdvanceTick(1f / 30f);
        session.Flags.Write("wolves_killed", 1);
        var wolf = session.World.All.First();
        wolf.Stats["HP"] = 5; // diverge from template
        session.World.Despawn(session.World.All.Last().InstanceId);

        var json = SaveManager.Capture(session);

        // restore into a fresh session over the same content
        var restored = _host.CreateSession();
        restored.LoadContent();
        var report = SaveManager.Restore(restored, json);

        Assert.True(report.Ok, string.Join("; ", report.Errors));
        Assert.Equal(session.Tick, restored.Tick);
        Assert.Equal(session.Rng.State, restored.Rng.State);
        Assert.Equal(1.0, restored.Flags.ReadNumber("wolves_killed"));
        Assert.Equal(1, restored.World.Count);
        Assert.True(restored.World.TryGet(wolf.InstanceId, out var restoredWolf));
        Assert.Equal(5, restoredWolf.Stats["HP"]);
        Assert.Contains("wolf", restoredWolf.Tags);
    }

    [Fact]
    public void Restore_MissingDef_IsReported()
    {
        var session = BootedSession();
        var json = SaveManager.Capture(session).Replace("entity_wolf", "entity_gone");

        var restored = _host.CreateSession();
        restored.LoadContent();
        var report = SaveManager.Restore(restored, json);

        Assert.False(report.Ok);
        Assert.Contains(report.Errors, e => e.Contains("entity_gone"));
    }

    [Fact]
    public void Restore_UnsupportedVersion_IsRejected()
    {
        var session = BootedSession();
        var json = SaveManager.Capture(session).Replace("\"version\": 1", "\"version\": 99");

        var report = SaveManager.Restore(_host.CreateSession(), json);

        Assert.False(report.Ok);
        Assert.Contains(report.Errors, e => e.Contains("version 99"));
    }

    [Fact]
    public void Determinism_SaveLoadTick_EqualsUninterruptedRun()
    {
        // run A: boot, tick K, then tick K more
        var a = BootedSession();
        var diceSystem = new DiceSystem();
        a.RegisterSystem(diceSystem);
        TickN(a, 30);
        var midSave = SaveManager.Capture(a);
        TickN(a, 30);
        var finalA = SaveManager.Capture(a);

        // run B: restore from the mid-save into a fresh session, tick the same K more
        var b = _host.CreateSession();
        b.LoadContent();
        b.RegisterSystem(new DiceSystem());
        SaveManager.Restore(b, midSave);
        TickN(b, 30);
        var finalB = SaveManager.Capture(b);

        Assert.Equal(finalA, finalB); // byte-identical world delta
    }

    private static void TickN(GameSession session, int n)
    {
        for (var i = 0; i < n; i++)
        {
            session.AdvanceTick(1f / 30f);
        }
    }

    /// <summary>Mutates world state through the RNG + formulas every tick, so divergence would show.</summary>
    private sealed class DiceSystem : ISimSystem
    {
        public string Name => "dice";

        public void Tick(GameSession session, float dt)
        {
            var roll = session.Formulas.Evaluate("1d6");
            session.Flags.Write("last_roll", roll);
            session.Flags.Write("roll_total", session.Flags.ReadNumber("roll_total") + roll);
            foreach (var entity in session.World.All)
            {
                entity.Stats["HP"] = Math.Max(0, entity.Stats.GetValueOrDefault("HP") - roll * 0.01);
            }
        }
    }
}
