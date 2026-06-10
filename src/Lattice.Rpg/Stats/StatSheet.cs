using Lattice.Core.Formulas;
using Lattice.Core.Simulation;
using Lattice.Rpg.Defs;

namespace Lattice.Rpg.Stats;

/// <summary>One stat modifier from a tracked source ("status:status_poison", "equip:slot_main_hand").</summary>
public sealed record StatModifier(string Source, string StatKey, double Flat, double Percent);

/// <summary>
/// Per-entity stat resolution (plan/02 §1). Base values live in
/// <see cref="Entity.Stats"/> (keyed by stat key, so the core save system
/// persists them untouched); this sheet layers modifiers and clamping on
/// top: current = (base + Σflat) · (1 + Σpercent/100), clamped to the
/// def's min/max formulas. Derived stats evaluate their formula instead.
/// Installed as the entity's <see cref="Entity.StatResolver"/> so every
/// formula in the game sees modifier-adjusted values.
/// </summary>
public sealed class StatSheet : IFormulaContext
{
    private readonly Entity _entity;
    private readonly RpgRuntime _rpg;
    private readonly List<StatModifier> _modifiers = [];
    private readonly Dictionary<string, int> _tagGrants = new(StringComparer.Ordinal);
    private readonly HashSet<string> _baseTags;
    private readonly HashSet<string> _evaluating = new(StringComparer.Ordinal);

    internal StatSheet(Entity entity, RpgRuntime rpg)
    {
        _entity = entity;
        _rpg = rpg;
        _baseTags = new HashSet<string>(entity.Tags, StringComparer.Ordinal);
    }

    public Entity Entity => _entity;

    public IReadOnlyList<StatModifier> Modifiers => _modifiers;

    public double GetBase(string key) => _entity.Stats.TryGetValue(key, out var value) ? value : 0;

    public bool HasStat(string key)
        => _entity.Stats.ContainsKey(key)
           || (_rpg.Stats.TryGetByKey(key, out var def) && def.IsDerived);

    /// <summary>Modifier- and clamp-adjusted current value.</summary>
    public double Current(string key)
    {
        if (!_rpg.Stats.TryGetByKey(key, out var def))
        {
            return GetBase(key); // stat not declared in content: raw passthrough
        }

        if (!_evaluating.Add(key))
        {
            _evaluating.Clear();
            throw new FormulaException($"Stat cycle detected while evaluating '{key}' on {_entity.InstanceId}.");
        }

        try
        {
            if (def.IsDerived)
            {
                return _rpg.Session.Formulas.Evaluate(def.Formula!, this);
            }

            double flat = 0, percent = 0;
            foreach (var modifier in _modifiers)
            {
                if (modifier.StatKey == key)
                {
                    flat += modifier.Flat;
                    percent += modifier.Percent;
                }
            }

            var value = (GetBase(key) + flat) * (1 + percent / 100.0);
            if (def.Min is not null)
            {
                value = Math.Max(value, _rpg.Session.Formulas.Evaluate(def.Min, this));
            }

            if (def.Max is not null)
            {
                value = Math.Min(value, _rpg.Session.Formulas.Evaluate(def.Max, this));
            }

            return value;
        }
        finally
        {
            _evaluating.Remove(key);
        }
    }

    /// <summary>
    /// Set a base value (clamped to the stat's min/max), publish
    /// <c>Stat.Changed</c>, and run the vital-death check.
    /// </summary>
    public void SetBase(string key, double value, Entity? source = null)
    {
        _rpg.Stats.TryGetByKey(key, out var def);
        var oldCurrent = HasStat(key) ? Current(key) : 0;

        if (def is not null)
        {
            if (def.IsDerived)
            {
                throw new InvalidOperationException($"Stat '{key}' is derived; its base cannot be set.");
            }

            if (def.Min is not null)
            {
                value = Math.Max(value, _rpg.Session.Formulas.Evaluate(def.Min, this));
            }

            if (def.Max is not null)
            {
                value = Math.Min(value, _rpg.Session.Formulas.Evaluate(def.Max, this));
            }
        }

        _entity.Stats[key] = value;
        var newCurrent = Current(key);
        if (!newCurrent.Equals(oldCurrent))
        {
            _rpg.NotifyStatChanged(_entity, key, oldCurrent, newCurrent, source);
        }
    }

    public void ModifyBase(string key, double delta, Entity? source = null) => SetBase(key, GetBase(key) + delta, source);

    public void AddModifier(StatModifier modifier)
    {
        var old = HasStat(modifier.StatKey) ? Current(modifier.StatKey) : 0;
        _modifiers.Add(modifier);
        var now = Current(modifier.StatKey);
        if (!now.Equals(old))
        {
            _rpg.NotifyStatChanged(_entity, modifier.StatKey, old, now, source: null, vitalCheck: false);
        }
    }

    public void RemoveModifiersBySource(string source)
    {
        var affectedKeys = _modifiers.Where(m => m.Source == source).Select(m => m.StatKey).Distinct().ToList();
        var before = affectedKeys.ToDictionary(k => k, Current, StringComparer.Ordinal);
        _modifiers.RemoveAll(m => m.Source == source);
        foreach (var key in affectedKeys)
        {
            var now = Current(key);
            if (!now.Equals(before[key]))
            {
                _rpg.NotifyStatChanged(_entity, key, before[key], now, source: null, vitalCheck: false);
            }
        }
    }

    /// <summary>Grant a tag from a refcounted source (status/equip). Template tags are never revoked.</summary>
    public void GrantTag(string tag)
    {
        _tagGrants[tag] = _tagGrants.TryGetValue(tag, out var count) ? count + 1 : 1;
        _entity.Tags.Add(tag);
    }

    public void RevokeTag(string tag)
    {
        if (!_tagGrants.TryGetValue(tag, out var count))
        {
            return;
        }

        if (count <= 1)
        {
            _tagGrants.Remove(tag);
            if (!_baseTags.Contains(tag))
            {
                _entity.Tags.Remove(tag);
            }
        }
        else
        {
            _tagGrants[tag] = count - 1;
        }
    }

    /// <summary>
    /// Save-restore support: saved entity tags include granted ones, so the
    /// restore path strips tags that are about to be re-granted before
    /// re-applying statuses/equipment.
    /// </summary>
    public void StripGrantableTags(IEnumerable<string> tags)
    {
        foreach (var tag in tags)
        {
            _baseTags.Remove(tag);
            _entity.Tags.Remove(tag);
        }
    }

    /// <summary>Formula identifiers resolve to current stat values (key match), else raw base entries.</summary>
    public bool TryResolve(string identifier, out double value)
    {
        if (_rpg.Stats.TryGetByKey(identifier, out var def) && (def.IsDerived || _entity.Stats.ContainsKey(identifier)))
        {
            value = Current(identifier);
            return true;
        }

        return _entity.Stats.TryGetValue(identifier, out value);
    }
}
