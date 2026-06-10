using Lattice.Core.Events;
using Lattice.Core.Simulation;
using Lattice.Rpg.Defs;
using Lattice.Rpg.Effects;
using Lattice.Rpg.Stats;

namespace Lattice.Rpg.Status;

/// <summary>
/// Per-entity active status effects (plan/02 §2). Modifiers and tags are
/// applied through the stat sheet with the source key
/// <c>status:&lt;defId&gt;</c> and re-derived on save restore — never saved
/// directly. Stacks multiply modifier amounts; periodic effects fire once
/// per interval regardless of stacks.
/// </summary>
public sealed class StatusEffects
{
    private readonly Entity _entity;
    private readonly RpgRuntime _rpg;
    private readonly List<ActiveStatus> _active = [];

    internal StatusEffects(Entity entity, RpgRuntime rpg)
    {
        _entity = entity;
        _rpg = rpg;
    }

    public IReadOnlyList<ActiveStatus> Active => _active;

    public bool Has(string statusId) => _active.Any(s => s.Def.Id == statusId);

    public void Apply(StatusEffectDef def, Entity? source)
    {
        var existing = _active.FirstOrDefault(s => s.Def.Id == def.Id);
        if (existing is not null)
        {
            switch (def.Stacking)
            {
                case "ignore":
                    return;
                case "stack":
                    if (existing.Stacks < def.MaxStacks)
                    {
                        existing.Stacks++;
                        ReapplyModifiers(existing);
                    }

                    existing.Remaining = def.Duration;
                    return;
                default: // refresh
                    existing.Remaining = def.Duration;
                    return;
            }
        }

        var status = new ActiveStatus(def)
        {
            Remaining = def.Duration,
            SourceId = source?.InstanceId,
        };
        _active.Add(status);
        ApplyModifiers(status);
        _rpg.Session.Events.Publish("Status.Applied", EventPayload.Of(
            ("instanceId", _entity.InstanceId), ("status", def.Id), ("sourceId", source?.InstanceId)));
    }

    public bool Remove(string statusId)
    {
        var status = _active.FirstOrDefault(s => s.Def.Id == statusId);
        if (status is null)
        {
            return false;
        }

        _active.Remove(status);
        RemoveModifiers(status);
        _rpg.Session.Events.Publish("Status.Expired", EventPayload.Of(
            ("instanceId", _entity.InstanceId), ("status", statusId)));
        return true;
    }

    /// <summary>Restore path: re-add a saved status with its remaining time/stacks, without re-publishing Applied.</summary>
    internal void RestoreStatus(StatusEffectDef def, double remaining, int stacks, string? sourceId)
    {
        var status = new ActiveStatus(def)
        {
            Remaining = remaining,
            Stacks = Math.Max(1, stacks),
            SourceId = sourceId,
        };
        _active.Add(status);
        ApplyModifiers(status);
    }

    internal void Tick(double dt)
    {
        // snapshot: periodic effects may kill the entity and clear this list
        foreach (var status in _active.ToArray())
        {
            FirePeriodics(status, dt);
            if (!_rpg.Session.World.TryGet(_entity.InstanceId, out _))
            {
                return; // entity died mid-tick
            }

            if (status.Def.Duration > 0)
            {
                status.Remaining -= dt;
                if (status.Remaining <= 0)
                {
                    Remove(status.Def.Id);
                }
            }
        }
    }

    private void FirePeriodics(ActiveStatus status, double dt)
    {
        var periodics = status.Periodics;
        for (var i = 0; i < periodics.Count; i++)
        {
            status.PeriodicElapsed[i] += dt;
            while (status.PeriodicElapsed[i] >= periodics[i].Interval)
            {
                status.PeriodicElapsed[i] -= periodics[i].Interval;
                Entity? source = null;
                if (status.SourceId is not null)
                {
                    _rpg.Session.World.TryGet(status.SourceId, out source!);
                }

                _rpg.Effects.Run(periodics[i].Effects, new EffectContext
                {
                    Session = _rpg.Session,
                    Rpg = _rpg,
                    Source = source ?? _entity,
                    Target = _entity,
                });
            }
        }
    }

    private void ApplyModifiers(ActiveStatus status)
    {
        var sheet = _rpg.GetSheet(_entity);
        if (sheet is null)
        {
            return;
        }

        var source = SourceKey(status.Def);
        foreach (var entry in status.Def.Logic ?? [])
        {
            switch (entry)
            {
                case FlatModifierEntry flat when _rpg.Stats.TryGetById(flat.Stat, out var def):
                    sheet.AddModifier(new StatModifier(source, def.Key, flat.Amount * status.Stacks, 0));
                    break;
                case PercentModifierEntry pct when _rpg.Stats.TryGetById(pct.Stat, out var def):
                    sheet.AddModifier(new StatModifier(source, def.Key, 0, pct.Percent * status.Stacks));
                    break;
                case TagModifierEntry tags:
                    foreach (var tag in tags.AddTags)
                    {
                        sheet.GrantTag(tag);
                    }

                    break;
            }
        }
    }

    private void RemoveModifiers(ActiveStatus status)
    {
        var sheet = _rpg.GetSheet(_entity);
        if (sheet is null)
        {
            return;
        }

        sheet.RemoveModifiersBySource(SourceKey(status.Def));
        foreach (var entry in status.Def.Logic ?? [])
        {
            if (entry is TagModifierEntry tags)
            {
                foreach (var tag in tags.AddTags)
                {
                    sheet.RevokeTag(tag);
                }
            }
        }
    }

    private void ReapplyModifiers(ActiveStatus status)
    {
        RemoveModifiers(status);
        ApplyModifiers(status);
    }

    private static string SourceKey(StatusEffectDef def) => $"status:{def.Id}";

    public sealed class ActiveStatus
    {
        internal ActiveStatus(StatusEffectDef def)
        {
            Def = def;
            Periodics = (def.Logic ?? []).OfType<PeriodicEffectEntry>().ToList();
            PeriodicElapsed = new double[Periodics.Count];
        }

        public StatusEffectDef Def { get; }

        public double Remaining { get; set; }

        public int Stacks { get; set; } = 1;

        public string? SourceId { get; set; }

        internal List<PeriodicEffectEntry> Periodics { get; }

        /// <summary>Per-periodic accumulators; not saved (periodic phase resets on load, v1 limitation).</summary>
        internal double[] PeriodicElapsed { get; }
    }
}

/// <summary>Ticks every live entity's status effects each simulation tick.</summary>
public sealed class StatusEffectSystem(RpgRuntime rpg) : ISimSystem
{
    public string Name => "rpg.status";

    public void Tick(GameSession session, float dt)
    {
        foreach (var entity in session.World.All.ToList())
        {
            rpg.GetStatusEffects(entity)?.Tick(dt);
        }
    }
}
