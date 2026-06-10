using System.Numerics;
using Lattice.Core.Content;
using Lattice.Core.Events;
using Lattice.Core.Formulas;

namespace Lattice.Core.Simulation;

/// <summary>
/// A live entity: mutable instance state referencing an immutable template
/// def by ID. Stats are copied from the template at spawn so they can change
/// independently; only instance state is ever saved (world-delta principle).
/// </summary>
public sealed class Entity : IFormulaContext
{
    public Entity(string instanceId, string defId)
    {
        InstanceId = instanceId;
        DefId = defId;
    }

    public string InstanceId { get; }

    public string DefId { get; }

    public string? Name { get; set; }

    public HashSet<string> Tags { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, double> Stats { get; } = new(StringComparer.Ordinal);

    public Vector3 Position { get; set; }

    /// <summary>Entities act as formula contexts: identifiers resolve to stat values.</summary>
    public bool TryResolve(string identifier, out double value) => Stats.TryGetValue(identifier, out value);
}

/// <summary>
/// The set of live entities. Spawning resolves templates through the def
/// registry and publishes lifecycle events on the bus.
/// </summary>
public sealed class World
{
    private readonly Dictionary<string, Entity> _entities = new(StringComparer.Ordinal);
    private readonly DefRegistry _defs;
    private readonly EventBus _events;

    public World(DefRegistry defs, EventBus events)
    {
        _defs = defs;
        _events = events;
    }

    public int Count => _entities.Count;

    public IEnumerable<Entity> All => _entities.Values;

    /// <summary>Ordinal for the next spawned instance ID; persisted so IDs stay unique across save/load.</summary>
    public long NextEntityOrdinal { get; set; } = 1;

    public bool TryGet(string instanceId, out Entity entity) => _entities.TryGetValue(instanceId, out entity!);

    public Entity Spawn(string defId, Vector3 position = default)
    {
        var template = _defs.Get<EntityTemplateDef>(defId);
        var entity = new Entity($"e_{NextEntityOrdinal++:D4}", defId)
        {
            Name = template.Name,
            Position = position,
        };

        foreach (var tag in template.Tags ?? [])
        {
            entity.Tags.Add(tag);
        }

        foreach (var stat in template.Stats ?? [])
        {
            entity.Stats[stat.Key] = stat.Value;
        }

        _entities[entity.InstanceId] = entity;
        _events.Publish("Entity.Spawned", EventPayload.Of(("instanceId", entity.InstanceId), ("defId", defId)));
        return entity;
    }

    /// <summary>Re-create a saved entity verbatim (no template re-copy, no spawn event).</summary>
    public void RestoreEntity(Entity entity) => _entities[entity.InstanceId] = entity;

    public bool Despawn(string instanceId)
    {
        if (!_entities.TryGetValue(instanceId, out var entity))
        {
            return false;
        }

        _entities.Remove(instanceId);
        _events.Publish("Entity.Despawned", EventPayload.Of(("instanceId", instanceId), ("defId", entity.DefId)));
        return true;
    }

    public void Clear()
    {
        _entities.Clear();
        NextEntityOrdinal = 1;
    }
}
