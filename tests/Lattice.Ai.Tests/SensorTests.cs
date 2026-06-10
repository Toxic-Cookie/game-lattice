using System.Numerics;
using Lattice.Ai.Perception;
using Lattice.Core.Events;

namespace Lattice.Ai.Tests;

public sealed class SensorTests : IDisposable
{
    private readonly AiTestHost _host = new();

    public SensorTests() => _host.WriteStandardContent();

    public void Dispose() => _host.Dispose();

    [Fact]
    public void Visual_FullSensitivity_SetsCanSeeEnemyWithPosition()
    {
        var (session, _, ai) = _host.CreateLoadedSession();
        var guard = session.World.Spawn("entity_guard", new Vector3(0, 0, 0));
        var player = session.World.Spawn("entity_player", new Vector3(5, 0, 0));
        _host.TickSeconds(session, 0.1);

        var agent = ai.GetAgent(guard)!;
        Assert.True(agent.Conditions.IsSet(agent.Catalog, SensorPipeline.CanSeeEnemy));
        Assert.Equal(player.Position, agent.Beliefs.GetPosition("enemy_position"));
        Assert.Equal(player.InstanceId, agent.Beliefs.GetString("enemy_id"));
    }

    [Fact]
    public void Visual_OutOfRange_SeesNothing()
    {
        var (session, _, ai) = _host.CreateLoadedSession();
        var guard = session.World.Spawn("entity_guard", new Vector3(0, 0, 0));
        session.World.Spawn("entity_player", new Vector3(50, 0, 0));
        _host.TickSeconds(session, 0.1);

        var agent = ai.GetAgent(guard)!;
        Assert.False(agent.Conditions.HasAnyOf(uint.MaxValue));
    }

    [Fact]
    public void Visual_PartialSensitivity_YieldsThreatKnownInstead()
    {
        var (session, _, ai) = _host.CreateLoadedSession();
        var rat = session.World.Spawn("entity_rat", new Vector3(0, 0, 0)); // sensitivity 0.5
        session.World.Spawn("entity_player", new Vector3(3, 0, 0));
        _host.TickSeconds(session, 0.1);

        var agent = ai.GetAgent(rat)!;
        Assert.False(agent.Conditions.IsSet(agent.Catalog, SensorPipeline.CanSeeEnemy));
        Assert.True(agent.Conditions.IsSet(agent.Catalog, SensorPipeline.ThreatKnown));
        Assert.NotNull(agent.Beliefs.GetPosition("threat_position"));
    }

    [Fact]
    public void Visual_NonHostileEntities_AreIgnored()
    {
        var (session, _, ai) = _host.CreateLoadedSession();
        var guard = session.World.Spawn("entity_guard", new Vector3(0, 0, 0));
        session.World.Spawn("entity_rat", new Vector3(2, 0, 0)); // rats aren't hostile to guards
        _host.TickSeconds(session, 0.1);

        var agent = ai.GetAgent(guard)!;
        Assert.False(agent.Conditions.IsSet(agent.Catalog, SensorPipeline.CanSeeEnemy));
    }

    [Fact]
    public void Auditory_TransientSound_SetsHearSoundThenExpires()
    {
        var (session, _, ai) = _host.CreateLoadedSession();
        var guard = session.World.Spawn("entity_guard", new Vector3(0, 0, 0));
        var agent = ai.GetAgent(guard)!;

        session.Events.Publish("Stimulus.Sound", EventPayload.Of(
            ("x", 4.0), ("y", 0.0), ("z", 0.0), ("loudness", 1.0)));
        _host.TickSeconds(session, 0.1);

        Assert.True(agent.Conditions.IsSet(agent.Catalog, SensorPipeline.HearSound));
        Assert.Equal(new Vector3(4, 0, 0), agent.Beliefs.GetPosition("sound_position"));

        _host.TickSeconds(session, 1.0); // stimulus lifetime 0.6s
        Assert.False(agent.Conditions.IsSet(agent.Catalog, SensorPipeline.HearSound));
    }

    [Fact]
    public void Damage_SetsDamagedConditionBriefly()
    {
        var (session, rpg, ai) = _host.CreateLoadedSession();
        var guard = session.World.Spawn("entity_guard", new Vector3(0, 0, 0));
        var agent = ai.GetAgent(guard)!;

        rpg.RunEffects([System.Text.Json.JsonDocument.Parse("""{ "type": "DealDamage", "formula": "1" }""").RootElement.Clone()], null, guard);
        _host.TickSeconds(session, 0.1);
        Assert.True(agent.Conditions.IsSet(agent.Catalog, SensorPipeline.Damaged));

        _host.TickSeconds(session, 1.0);
        Assert.False(agent.Conditions.IsSet(agent.Catalog, SensorPipeline.Damaged));
    }

    [Fact]
    public void ThreatPerception_DrivesMetaStateAlertAndDecay()
    {
        var (session, _, ai) = _host.CreateLoadedSession();
        var guard = session.World.Spawn("entity_guard", new Vector3(0, 0, 0));
        var player = session.World.Spawn("entity_player", new Vector3(5, 0, 0));
        var agent = ai.GetAgent(guard)!;

        _host.TickSeconds(session, 0.1);
        Assert.Equal(Agents.MetaState.Alert, agent.Meta);

        session.World.Despawn(player.InstanceId);
        _host.TickSeconds(session, 3.0); // alertDecaySeconds 2.0
        Assert.Equal(Agents.MetaState.Idle, agent.Meta);
    }
}
