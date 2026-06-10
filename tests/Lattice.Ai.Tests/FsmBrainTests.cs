using System.Numerics;
using Lattice.Ai.Agents;

namespace Lattice.Ai.Tests;

public sealed class FsmBrainTests : IDisposable
{
    private readonly AiTestHost _host = new(seed: 7);

    public FsmBrainTests() => _host.WriteStandardContent();

    public void Dispose() => _host.Dispose();

    private static DataFsmBrain BrainOf(AgentComponent agent) => (DataFsmBrain)agent.Brain;

    [Fact]
    public void Rat_WandersNearSpawn()
    {
        var (session, _, ai) = _host.CreateLoadedSession();
        var rat = session.World.Spawn("entity_rat", new Vector3(0, 0, 0));

        _host.TickSeconds(session, 3.0);

        Assert.Equal("wander", BrainOf(ai.GetAgent(rat)!).State);
        Assert.NotEqual(Vector3.Zero, rat.Position); // it moved
        Assert.True(rat.Position.Length() <= 5.0f, $"wandered too far: {rat.Position}"); // radius 4 + slack
    }

    [Fact]
    public void Rat_FleesFromThreat_AndCalmsDown()
    {
        var (session, _, ai) = _host.CreateLoadedSession();
        var rat = session.World.Spawn("entity_rat", new Vector3(0, 0, 0));
        var agent = ai.GetAgent(rat)!;

        var player = session.World.Spawn("entity_player", new Vector3(2, 0, 0));
        _host.TickSeconds(session, 0.2);
        Assert.Equal("flee", BrainOf(agent).State);

        _host.TickSeconds(session, 1.5);
        var fledDistance = Vector3.Distance(rat.Position, player.Position);
        Assert.True(fledDistance > 2.0f, $"rat only {fledDistance} away");

        session.World.Despawn(player.InstanceId);
        _host.TickSeconds(session, 0.5);
        Assert.Equal("wander", BrainOf(agent).State);
        Assert.Contains(agent.Trace, t => t.Contains("fsm wander -> flee"));
        Assert.Contains(agent.Trace, t => t.Contains("fsm flee -> wander"));
    }

    [Fact]
    public void Wander_IsDeterministicPerSeed()
    {
        var (sessionA, _, _) = _host.CreateLoadedSession();
        var ratA = sessionA.World.Spawn("entity_rat", new Vector3(0, 0, 0));
        _host.TickSeconds(sessionA, 2.0);

        using var hostB = new AiTestHost(seed: 7);
        hostB.WriteStandardContent();
        var (sessionB, _, _) = hostB.CreateLoadedSession();
        var ratB = sessionB.World.Spawn("entity_rat", new Vector3(0, 0, 0));
        hostB.TickSeconds(sessionB, 2.0);

        Assert.Equal(ratA.Position, ratB.Position);
    }
}
