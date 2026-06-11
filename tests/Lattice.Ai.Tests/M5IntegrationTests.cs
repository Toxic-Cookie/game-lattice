using System.Numerics;
using Lattice.Ai.Agents;
using Lattice.Core.Events;

namespace Lattice.Ai.Tests;

/// <summary>
/// The M5 ↔ AI seams: environmental sense multipliers (rain dulls hearing)
/// and the flagConditions bridge (schedules flip on is_night). World state
/// reaches agents only through global flags — no project coupling.
/// </summary>
public sealed class M5IntegrationTests : IDisposable
{
    private readonly AiTestHost _host = new();

    public M5IntegrationTests() => _host.WriteStandardContent();

    public void Dispose() => _host.Dispose();

    [Fact]
    public void AuditoryMultiplierFlag_DegradesHearing()
    {
        // a stationary listener (a moving guard would chase the first noise
        // and end up close enough to hear anything)
        _host.WriteContent("bots.json", """
            [ { "id": "entity_listener", "type": "entity", "name": "Listener" },
              { "id": "btree_idle", "type": "btree", "root": { "task": "Wait", "seconds": 9 } },
              { "id": "profile_listener", "type": "agent", "entities": ["entity_listener"],
                "brain": "bt", "behaviorTree": "btree_idle",
                "sensors": [ { "kind": "auditory", "range": 12 } ] } ]
            """);
        var (session, _, ai) = _host.CreateLoadedSession();
        var listener = session.World.Spawn("entity_listener", Vector3.Zero);
        var agent = ai.GetAgent(listener)!;

        // clear weather: a noise 8 units out is well within range 12
        session.Events.Publish("Stimulus.Sound", EventPayload.Of(("x", 8.0), ("y", 0.0), ("z", 0.0)));
        _host.TickSeconds(session, 2.0 / 30);
        Assert.True(agent.Conditions.IsSet(agent.Catalog, "HEAR_SOUND"));

        _host.TickSeconds(session, 1.0); // let the stimulus expire
        session.Flags.Write("sense_auditory_mult", 0.5); // it starts raining (per weather data)

        session.Events.Publish("Stimulus.Sound", EventPayload.Of(("x", 8.0), ("y", 0.0), ("z", 0.0)));
        _host.TickSeconds(session, 2.0 / 30);
        Assert.False(agent.Conditions.IsSet(agent.Catalog, "HEAR_SOUND")); // 12 × 0.5 = 6 < 8
    }

    [Fact]
    public void IsNightFlag_FlipsTheGuardToTheSleepSchedule()
    {
        // a real clock ticking toward 22:00 (one game hour per 30s, start 21.95)
        _host.WriteContent("world.json", """
            [ { "id": "time_test", "type": "time", "minutesPerGameDay": 12, "daysPerSeason": 2,
                "seasons": [], "startHour": 21.95 },
              { "id": "dayphases_test", "type": "dayphases", "phases": [
                  { "name": "day",   "fromHour": 5,  "toHour": 22, "light": 1.0 },
                  { "name": "night", "fromHour": 22, "toHour": 5,  "light": 0.15 } ] } ]
            """);
        var (session, _, ai) = _host.CreateLoadedSession();
        var guard = session.World.Spawn("entity_guard", Vector3.Zero);
        var agent = ai.GetAgent(guard)!;

        _host.TickSeconds(session, 0.5);
        Assert.Equal("schedule_patrol", ((ScheduleBrain)agent.Brain).CurrentScheduleId);

        _host.TickSeconds(session, 6.0); // past 22:00, plus a patrol leg to finish

        Assert.True(session.Flags.ReadBool("is_night"));
        Assert.True(agent.Conditions.IsSet(agent.Catalog, "IS_NIGHT"));
        Assert.Contains(agent.Trace, t => t.Contains("selected schedule_sleep"));
    }
}
