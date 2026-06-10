using System.Numerics;
using Lattice.Ai.Agents;
using Lattice.Ai.Defs;
using Lattice.Core.Simulation;

namespace Lattice.Ai.Perception;

/// <summary>What a stimulus is (HZD information packets, ch05 §5.2).</summary>
public enum StimulusType
{
    Entity,
    Sound,
    Scent,
}

/// <summary>
/// A perceivable fact in the world: an entity's presence, or a transient
/// sound/scent event. Smell deliberately reuses the sound path — Half-Life's
/// "smell is an inaudible audio event" trick (ch02 case study Part 3).
/// </summary>
public readonly struct StimulusPacket
{
    public required StimulusType Type { get; init; }

    public required Vector3 Position { get; init; }

    public string? SourceId { get; init; }

    /// <summary>For entity stimuli: the source entity (tags, state).</summary>
    public Entity? SourceEntity { get; init; }

    /// <summary>Sound/scent: multiplies the listener's effective range.</summary>
    public double Loudness { get; init; }
}

/// <summary>Detection fidelity, gated by sensor sensitivity (ch05 §5.2: full / partial / minimal).</summary>
public enum PerceptionConfidence
{
    Minimal,
    Partial,
    Full,
}

/// <summary>One successful detection, ready for world-model integration.</summary>
public readonly struct Perception
{
    public required StimulusPacket Stimulus { get; init; }

    public required PerceptionConfidence Confidence { get; init; }

    /// <summary>True when the source carries one of the agent's hostile tags.</summary>
    public required bool IsHostile { get; init; }
}

/// <summary>
/// Evaluates an agent's sensor suite against entity and transient stimuli,
/// then integrates perceptions into the agent's conditions + beliefs.
/// Sensor kinds and calibration come from the agent profile (JSON) — the
/// Watcher-vs-Stalker dial is data (HZD case study Part 7).
/// </summary>
public static class SensorPipeline
{
    // standard condition names the integration step sets when the catalog declares them
    public const string CanSeeEnemy = "CAN_SEE_ENEMY";
    public const string ThreatKnown = "THREAT_KNOWN";
    public const string HearSound = "HEAR_SOUND";
    public const string SmellDetected = "SMELL_DETECTED";
    public const string Contact = "CONTACT";
    public const string Damaged = "DAMAGED";

    /// <summary>Refresh conditions + beliefs from current stimuli (clears sensor-derived state first).</summary>
    public static void Update(AgentContext ctx, IReadOnlyList<StimulusPacket> transientStimuli, double now)
    {
        var agent = ctx.Agent;
        agent.Conditions.ClearAll();
        agent.Beliefs.Remove("enemy_position");
        agent.Beliefs.Remove("enemy_id");
        agent.Beliefs.Remove("threat_position");
        agent.Beliefs.Remove("sound_position");
        agent.Beliefs.Remove("scent_position");

        foreach (var sensor in agent.Profile.Sensors ?? [])
        {
            switch (sensor.Kind)
            {
                case "visual":
                    foreach (var other in ctx.Ai.QueryEntitiesNear(ctx.Entity, sensor.Range))
                    {
                        SenseVisual(ctx, sensor, other);
                    }

                    break;

                case "proximity":
                    foreach (var other in ctx.Ai.QueryEntitiesNear(ctx.Entity, sensor.Range))
                    {
                        Integrate(ctx, new Perception
                        {
                            Stimulus = new StimulusPacket
                            {
                                Type = StimulusType.Entity,
                                Position = other.Position,
                                SourceId = other.InstanceId,
                                SourceEntity = other,
                            },
                            Confidence = PerceptionConfidence.Full,
                            IsHostile = IsHostile(agent.Profile, other),
                        }, contact: true);
                    }

                    break;

                case "auditory":
                case "smell":
                    var wanted = sensor.Kind == "smell" ? StimulusType.Scent : StimulusType.Sound;
                    foreach (var stimulus in transientStimuli)
                    {
                        if (stimulus.Type != wanted)
                        {
                            continue;
                        }

                        var effectiveRange = sensor.Range * Math.Max(0.01, stimulus.Loudness);
                        if (Vector3.DistanceSquared(ctx.Entity.Position, stimulus.Position) <= effectiveRange * effectiveRange)
                        {
                            Integrate(ctx, new Perception
                            {
                                Stimulus = stimulus,
                                Confidence = ToConfidence(sensor.Sensitivity),
                                IsHostile = false,
                            });
                        }
                    }

                    break;
            }
        }

        // recent damage is a perception too (drives interrupts like HEAVY_DAMAGE patterns)
        if (now - agent.LastDamagedAt <= 0.5)
        {
            agent.Conditions.Set(agent.Catalog, Damaged);
        }

        agent.Conditions.Or(agent.ManualConditions);
    }

    private static void SenseVisual(AgentContext ctx, AgentProfileDef.SensorSpec sensor, Entity other)
    {
        var agent = ctx.Agent;
        var toTarget = other.Position - ctx.Entity.Position;
        if (sensor.Fov < 360 && toTarget.LengthSquared() > 0.0001f)
        {
            var cos = Vector3.Dot(Vector3.Normalize(toTarget), Vector3.Normalize(agent.Facing));
            var halfFovRadians = sensor.Fov * Math.PI / 360.0;
            if (cos < Math.Cos(halfFovRadians))
            {
                return; // outside the view cone
            }
        }

        if (!ctx.Session.Services.Physics.HasLineOfSight(ctx.Entity.Position, other.Position))
        {
            return;
        }

        // concealment: hidden targets need a sharp eye (ch05 §5.2)
        if (other.Tags.Contains("concealed") && sensor.Sensitivity < 0.7)
        {
            return;
        }

        Integrate(ctx, new Perception
        {
            Stimulus = new StimulusPacket
            {
                Type = StimulusType.Entity,
                Position = other.Position,
                SourceId = other.InstanceId,
                SourceEntity = other,
            },
            Confidence = ToConfidence(sensor.Sensitivity),
            IsHostile = IsHostile(agent.Profile, other),
        });
    }

    private static void Integrate(AgentContext ctx, Perception perception, bool contact = false)
    {
        var agent = ctx.Agent;
        var now = ctx.Session.SimTimeSeconds;

        switch (perception.Stimulus.Type)
        {
            case StimulusType.Entity when perception.IsHostile:
                if (perception.Confidence == PerceptionConfidence.Full)
                {
                    agent.Conditions.Set(agent.Catalog, CanSeeEnemy);
                    agent.Beliefs.Set("enemy_position", perception.Stimulus.Position);
                    agent.Beliefs.Set("enemy_id", perception.Stimulus.SourceId ?? "");
                    agent.Beliefs.Set("last_enemy_position", perception.Stimulus.Position);
                }
                else
                {
                    agent.Conditions.Set(agent.Catalog, ThreatKnown);
                    agent.Beliefs.Set("threat_position", perception.Stimulus.Position);
                }

                agent.LastThreatAt = now;
                break;

            case StimulusType.Sound:
                agent.Conditions.Set(agent.Catalog, HearSound);
                agent.Beliefs.Set("sound_position", perception.Stimulus.Position);
                agent.LastThreatAt = now;
                break;

            case StimulusType.Scent:
                agent.Conditions.Set(agent.Catalog, SmellDetected);
                agent.Beliefs.Set("scent_position", perception.Stimulus.Position);
                break;
        }

        if (contact)
        {
            agent.Conditions.Set(agent.Catalog, Contact);
        }
    }

    private static bool IsHostile(AgentProfileDef profile, Entity other)
        => (profile.HostileTags ?? []).Any(other.Tags.Contains);

    private static PerceptionConfidence ToConfidence(double sensitivity) => sensitivity switch
    {
        >= 0.8 => PerceptionConfidence.Full,
        >= 0.4 => PerceptionConfidence.Partial,
        _ => PerceptionConfidence.Minimal,
    };
}
