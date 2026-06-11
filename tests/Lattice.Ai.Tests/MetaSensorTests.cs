using System.Numerics;
using Lattice.Core.Events;

namespace Lattice.Ai.Tests;

/// <summary>
/// Meta player awareness (plan/04 §15): declarative detectors over player
/// events raise ordinary conditions, and ordinary brains do the reacting.
/// The standard guard watches Player.Poked (3 hits / 5s → ANNOYED).
/// </summary>
public sealed class MetaSensorTests : IDisposable
{
    private readonly AiTestHost _host = new();

    public MetaSensorTests() => _host.WriteStandardContent();

    public void Dispose() => _host.Dispose();

    private void Poke(Lattice.Core.Simulation.GameSession session, string agentId)
        => session.Events.Publish("Player.Poked", EventPayload.Of(("agentId", agentId)));

    [Fact]
    public void ThresholdWithinWindow_SetsTheCondition()
    {
        var (session, _, ai) = _host.CreateLoadedSession();
        var guard = session.World.Spawn("entity_guard", new Vector3(50, 0, 50)); // away from anything
        var agent = ai.GetAgent(guard)!;

        Poke(session, guard.InstanceId);
        Poke(session, guard.InstanceId);
        _host.TickSeconds(session, 0.2);
        Assert.False(agent.Conditions.IsSet(agent.Catalog, "ANNOYED")); // two is tolerable

        Poke(session, guard.InstanceId);
        _host.TickSeconds(session, 0.2);
        Assert.True(agent.Conditions.IsSet(agent.Catalog, "ANNOYED"));
        Assert.Contains(agent.Trace, t => t.Contains("metasensor metasensor_poke set ANNOYED"));
    }

    [Fact]
    public void Condition_ClearsWhenTheWindowDrains()
    {
        var (session, _, ai) = _host.CreateLoadedSession();
        var guard = session.World.Spawn("entity_guard", new Vector3(50, 0, 50));
        var agent = ai.GetAgent(guard)!;

        for (var i = 0; i < 3; i++)
        {
            Poke(session, guard.InstanceId);
        }

        _host.TickSeconds(session, 0.2);
        Assert.True(agent.Conditions.IsSet(agent.Catalog, "ANNOYED"));

        _host.TickSeconds(session, 5.5); // the 5s window drains

        Assert.False(agent.Conditions.IsSet(agent.Catalog, "ANNOYED"));
    }

    [Fact]
    public void EventsForOtherAgents_DoNotTrip()
    {
        var (session, _, ai) = _host.CreateLoadedSession();
        var guard = session.World.Spawn("entity_guard", new Vector3(50, 0, 50));
        var other = session.World.Spawn("entity_guard", new Vector3(60, 0, 60));
        var agent = ai.GetAgent(guard)!;

        for (var i = 0; i < 5; i++)
        {
            Poke(session, other.InstanceId);
        }

        _host.TickSeconds(session, 0.2);

        Assert.False(agent.Conditions.IsSet(agent.Catalog, "ANNOYED"));
        Assert.True(ai.GetAgent(other)!.Conditions.IsSet(ai.GetAgent(other)!.Catalog, "ANNOYED"));
    }
}
