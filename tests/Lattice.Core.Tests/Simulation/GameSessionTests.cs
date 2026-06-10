using Lattice.Core.Simulation;

namespace Lattice.Core.Tests.Simulation;

public sealed class GameSessionTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    private void WriteStandardContent()
    {
        _host.WriteContent("lifecycle.json", """
            { "id": "lifecycle_test", "type": "lifecycle",
              "initialScene": "scene_alpha",
              "globalFlags": { "tutorial_done": false, "difficulty": 2, "mode": "story" },
              "spawns": [
                { "entity": "entity_player", "position": [1, 0, 2] },
                { "entity": "entity_wolf", "count": 2 }
              ] }
            """);
        _host.WriteContent("player.json", """
            { "id": "entity_player", "type": "entity", "name": "Player",
              "tags": ["player"], "stats": { "Str": 8, "Level": 3, "HP": 30 } }
            """);
        _host.WriteContent("wolf.json", """
            { "id": "entity_wolf", "type": "entity", "name": "Wolf",
              "tags": ["wolf"], "stats": { "HP": 12 } }
            """);
    }

    [Fact]
    public void Boot_AppliesFlagsAndSpawns()
    {
        WriteStandardContent();
        var session = _host.CreateSession();
        Assert.True(session.LoadContent().Ok);

        Assert.True(session.Boot("lifecycle_test"));

        Assert.Equal("scene_alpha", session.Flags.ReadString("current_scene"));
        Assert.False(session.Flags.ReadBool("tutorial_done", fallback: true));
        Assert.Equal(2.0, session.Flags.ReadNumber("difficulty"));
        Assert.Equal("story", session.Flags.ReadString("mode"));
        Assert.Equal(3, session.World.Count);

        var player = session.World.All.Single(e => e.Tags.Contains("player"));
        Assert.Equal(1, player.Position.X);
        Assert.Equal(8, player.Stats["Str"]);
    }

    [Fact]
    public void Boot_MissingLifecycle_ReturnsFalse()
    {
        var session = _host.CreateSession();
        session.LoadContent();

        Assert.False(session.Boot("lifecycle_nope"));
    }

    [Fact]
    public void AdvanceTick_RunsSystemsInRegistrationOrder_ThenDispatchesEvents()
    {
        var session = _host.CreateSession();
        var order = new List<string>();
        session.Events.Subscribe("FromSystem", _ => order.Add("event"));
        session.RegisterSystem(new CallbackSystem("a", s =>
        {
            order.Add("a");
            s.Events.Publish("FromSystem");
        }));
        session.RegisterSystem(new CallbackSystem("b", _ => order.Add("b")));

        session.AdvanceTick(1f / 30f);

        Assert.Equal(["a", "b", "event"], order); // events strictly after all systems
        Assert.Equal(1, session.Tick);
    }

    [Fact]
    public void EntityStats_DriveFormulas()
    {
        WriteStandardContent();
        var session = _host.CreateSession();
        session.LoadContent();
        session.Boot("lifecycle_test");
        var player = session.World.All.Single(e => e.Tags.Contains("player"));

        Assert.Equal(19, session.Formulas.Evaluate("(Str * 2) + Level", player));
    }

    [Fact]
    public void Spawn_PublishesEvent()
    {
        WriteStandardContent();
        var session = _host.CreateSession();
        session.LoadContent();
        string? spawnedDef = null;
        session.Events.Subscribe("Entity.Spawned", e => spawnedDef = e.Payload["defId"] as string);

        session.World.Spawn("entity_wolf");
        session.Events.DispatchPending();

        Assert.Equal("entity_wolf", spawnedDef);
    }

    private sealed class CallbackSystem(string name, Action<GameSession> action) : ISimSystem
    {
        public string Name => name;

        public void Tick(GameSession session, float dt) => action(session);
    }
}
