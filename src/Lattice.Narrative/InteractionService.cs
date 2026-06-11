using Lattice.Core.Content;
using Lattice.Core.Events;
using Lattice.Core.Simulation;
using Lattice.Narrative.Defs;
using Lattice.Rpg.Conditions;

namespace Lattice.Narrative;

/// <summary>
/// Smart-object interaction dispatch (plan/03 §5). Objects bind to entity
/// templates via <see cref="SmartObjectDef.Entity"/>; placing an object is
/// just spawning that entity. Reservation (maxUsers) is the exclusion
/// mechanism AI coordination builds on in M4; player interactions are
/// instantaneous and don't hold reservations.
/// </summary>
public sealed class InteractionService
{
    private readonly NarrativeRuntime _narrative;
    private readonly Dictionary<string, SmartObjectDef> _byEntityDef = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _reservations = new(StringComparer.Ordinal);

    internal InteractionService(NarrativeRuntime narrative)
    {
        _narrative = narrative;
    }

    /// <summary>The smart object bound to an entity's template, if any.</summary>
    public SmartObjectDef? GetBinding(Entity entity)
        => _byEntityDef.TryGetValue(entity.DefId, out var def) ? def : null;

    /// <summary>
    /// Perform a verb on a smart object: first interaction whose verb and
    /// conditions match runs its effects (source = the object entity,
    /// target = the actor).
    /// </summary>
    public bool TryInteract(Entity actor, Entity target, string verb, out string? error)
    {
        error = null;
        var def = GetBinding(target);
        if (def is null)
        {
            error = $"'{target.DefId}' is not a smart object.";
            return false;
        }

        var conditionContext = new ConditionContext
        {
            Session = _narrative.Session,
            Rpg = _narrative.Rpg,
            Subject = actor,
        };

        var sawVerb = false;
        foreach (var interaction in def.Interactions)
        {
            if (!string.Equals(interaction.Verb, verb, StringComparison.Ordinal))
            {
                continue;
            }

            sawVerb = true;
            if (!_narrative.Rpg.Conditions.EvaluateAll(interaction.Conditions, conditionContext))
            {
                continue;
            }

            _narrative.Rpg.RunEffects(interaction.Effects, source: target, target: actor);
            _narrative.Session.Events.Publish("Interaction.Performed", EventPayload.Of(
                ("actorId", actor.InstanceId),
                ("targetId", target.InstanceId),
                ("object", def.Id),
                ("verb", verb)), _narrative.Session.Tick);
            return true;
        }

        error = sawVerb
            ? $"conditions for '{verb}' not met"
            : $"'{def.Id}' has no '{verb}' interaction";
        return false;
    }

    /// <summary>Read-only reservation availability check (planners must not reserve while merely considering).</summary>
    public bool CanReserve(Entity target, string actorId)
    {
        var def = GetBinding(target);
        if (def is null)
        {
            return false;
        }

        return !_reservations.TryGetValue(target.InstanceId, out var users)
               || users.Contains(actorId)
               || users.Count < def.MaxUsers;
    }

    /// <summary>Reserve a slot on an object instance (AI exclusion, M4). Idempotent per actor.</summary>
    public bool TryReserve(Entity target, string actorId)
    {
        var def = GetBinding(target);
        if (def is null)
        {
            return false;
        }

        if (!_reservations.TryGetValue(target.InstanceId, out var users))
        {
            users = new HashSet<string>(StringComparer.Ordinal);
            _reservations[target.InstanceId] = users;
        }

        return users.Contains(actorId) || (users.Count < def.MaxUsers && users.Add(actorId));
    }

    public void Release(Entity target, string actorId)
    {
        if (_reservations.TryGetValue(target.InstanceId, out var users))
        {
            users.Remove(actorId);
        }
    }

    internal void RebuildIndex(DefRegistry registry)
    {
        _byEntityDef.Clear();
        foreach (var def in registry.All<SmartObjectDef>())
        {
            if (def.Entity is not null)
            {
                _byEntityDef[def.Entity] = def;
            }
        }
    }
}
