using System.Numerics;
using Lattice.Ai.Agents;
using Lattice.Core.Events;

namespace Lattice.Ai.Tests;

public sealed class ScheduleBrainTests : IDisposable
{
    private readonly AiTestHost _host = new();

    public ScheduleBrainTests() => _host.WriteStandardContent();

    public void Dispose() => _host.Dispose();

    private static ScheduleBrain BrainOf(AgentComponent agent) => (ScheduleBrain)agent.Brain;

    [Fact]
    public void Patrol_WalksTheRouteAndCycles()
    {
        var (session, _, ai) = _host.CreateLoadedSession();
        var guard = session.World.Spawn("entity_guard", new Vector3(0, 0, 0));
        var agent = ai.GetAgent(guard)!;

        _host.TickSeconds(session, 0.2);
        Assert.Equal("schedule_patrol", BrainOf(agent).CurrentScheduleId);

        _host.TickSeconds(session, 2.5); // walk 3 units at speed 2 + wait 0.3
        Assert.True(agent.PatrolIndex >= 1, $"patrol index was {agent.PatrolIndex}");
        Assert.Contains(agent.Trace, t => t.Contains("schedule_patrol complete"));
        Assert.NotEqual(new Vector3(0, 0, 0), guard.Position);
    }

    [Fact]
    public void HalfLifeSequence_PatrolNoiseInvestigateCombatSearchPatrol()
    {
        var (session, _, ai) = _host.CreateLoadedSession();
        var guard = session.World.Spawn("entity_guard", new Vector3(0, 0, 0));
        var agent = ai.GetAgent(guard)!;
        _host.TickSeconds(session, 0.2);
        Assert.Equal("schedule_patrol", BrainOf(agent).CurrentScheduleId);

        // 1. noise -> patrol invalidated by HEAR_SOUND -> investigate
        session.Events.Publish("Stimulus.Sound", EventPayload.Of(("x", 4.0), ("y", 0.0), ("z", 0.0)));
        _host.TickSeconds(session, 0.2);
        Assert.Equal("schedule_investigate", BrainOf(agent).CurrentScheduleId);
        Assert.Contains(agent.Trace, t => t.Contains("invalidated by") && t.Contains("HEAR_SOUND"));

        // 2. enemy appears -> investigate invalidated by CAN_SEE_ENEMY -> combat
        var player = session.World.Spawn("entity_player", new Vector3(6, 0, 0));
        _host.TickSeconds(session, 0.2);
        Assert.Equal("schedule_combat", BrainOf(agent).CurrentScheduleId);

        // 3. combat closes distance toward the enemy
        var before = Vector3.Distance(guard.Position, player.Position);
        _host.TickSeconds(session, 0.6);
        Assert.True(Vector3.Distance(guard.Position, player.Position) < before);

        // 4. enemy vanishes mid-combat -> search (Alert), at last known position
        session.World.Despawn(player.InstanceId);
        _host.TickSeconds(session, 1.5);
        Assert.Contains(agent.Trace, t => t.Contains("selected schedule_search"));

        // 5. alert decays -> back to patrol
        _host.TickSeconds(session, 4.0);
        Assert.Equal(MetaState.Idle, agent.Meta);
        Assert.Contains(agent.Trace.TakeLast(6), t => t.Contains("selected schedule_patrol"));
    }

    [Fact]
    public void Combat_TakesPriorityOverInvestigate()
    {
        var (session, _, ai) = _host.CreateLoadedSession();
        var guard = session.World.Spawn("entity_guard", new Vector3(0, 0, 0));
        session.World.Spawn("entity_player", new Vector3(5, 0, 0));
        session.Events.Publish("Stimulus.Sound", EventPayload.Of(("x", -4.0), ("y", 0.0), ("z", 0.0)));

        _host.TickSeconds(session, 0.2);

        // both HEAR_SOUND and CAN_SEE_ENEMY are set; combat (priority 100) wins
        Assert.Equal("schedule_combat", BrainOf(ai.GetAgent(guard)!).CurrentScheduleId);
    }

    [Fact]
    public void RatBrain_NeverUsesSchedules()
    {
        var (session, _, ai) = _host.CreateLoadedSession();
        var rat = session.World.Spawn("entity_rat", new Vector3(0, 0, 0));

        Assert.IsType<DataFsmBrain>(ai.GetAgent(rat)!.Brain); // the F.E.A.R. rat lesson: cheapest brain that works
    }
}
