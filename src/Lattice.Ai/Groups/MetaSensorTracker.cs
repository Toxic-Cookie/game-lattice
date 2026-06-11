using Lattice.Ai.Agents;
using Lattice.Ai.Defs;
using Lattice.Core.Events;

namespace Lattice.Ai.Groups;

/// <summary>
/// Meta player awareness (concept Phase 4): declarative detectors over
/// player-behavior events. When a watched topic fires often enough within a
/// sliding window for a given agent, that agent's <c>setCondition</c> bit is
/// raised (and lowered again when the window drains) — and the agent's
/// ordinary brain machinery does the reacting. No special code path.
/// </summary>
public sealed class MetaSensorTracker
{
    private readonly AiRuntime _ai;
    private readonly HashSet<string> _subscribedTopics = new(StringComparer.Ordinal);
    private readonly Dictionary<(string AgentId, string SensorId), List<double>> _hits = [];

    internal MetaSensorTracker(AiRuntime ai) => _ai = ai;

    /// <summary>Subscribe to any newly declared watch topics (idempotent; called after content (re)loads).</summary>
    internal void RebuildSubscriptions()
    {
        foreach (var sensor in _ai.Session.Defs.All<MetaSensorDef>())
        {
            if (sensor.Watch.Length > 0 && _subscribedTopics.Add(sensor.Watch))
            {
                var topic = sensor.Watch;
                _ai.Session.Events.Subscribe(topic, evt => OnWatchedEvent(topic, evt));
            }
        }
    }

    private void OnWatchedEvent(string topic, GameEvent evt)
    {
        foreach (var sensor in _ai.Session.Defs.All<MetaSensorDef>())
        {
            if (sensor.Watch != topic
                || !evt.Payload.TryGetValue(sensor.AgentKey, out var idValue)
                || idValue is not string agentId
                || !_ai.Session.World.TryGet(agentId, out var entity)
                || _ai.GetAgent(entity) is not { } agent
                || agent.Profile.MetaSensors?.Contains(sensor.Id) != true)
            {
                continue;
            }

            var key = (agentId, sensor.Id);
            if (!_hits.TryGetValue(key, out var times))
            {
                times = [];
                _hits[key] = times;
            }

            times.Add(_ai.Session.SimTimeSeconds);
        }
    }

    /// <summary>Per-agent, per-tick: prune windows and raise/lower the sensor conditions.</summary>
    internal void Sync(AgentContext ctx)
    {
        foreach (var sensorId in ctx.Agent.Profile.MetaSensors ?? [])
        {
            if (!ctx.Session.Defs.TryGet<MetaSensorDef>(sensorId, out var sensor) || sensor.SetCondition.Length == 0)
            {
                continue;
            }

            var mask = ctx.Agent.Catalog.MaskOf([sensor.SetCondition]);
            if (mask == 0)
            {
                continue;
            }

            var tripped = false;
            if (_hits.TryGetValue((ctx.Entity.InstanceId, sensorId), out var times))
            {
                times.RemoveAll(t => ctx.Session.SimTimeSeconds - t > sensor.Window);
                tripped = times.Count >= sensor.Threshold;
            }

            var wasTripped = (ctx.Agent.ManualConditions & mask) != 0;
            if (tripped && !wasTripped)
            {
                ctx.Agent.AddTrace(ctx.Session.Tick, $"metasensor {sensorId} set {sensor.SetCondition}");
            }

            ctx.Agent.ManualConditions = tripped
                ? ctx.Agent.ManualConditions | mask
                : ctx.Agent.ManualConditions & ~mask;
        }
    }
}
