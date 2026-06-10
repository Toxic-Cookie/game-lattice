using Lattice.Core.Events;
using Lattice.Core.Simulation;
using Lattice.Rpg.Defs;
using Lattice.Rpg.Effects;
using Lattice.Rpg.Stats;

namespace Lattice.Rpg.Items;

/// <summary>Per-entity item state: a stacked bag plus equipped slots (plan/02 §4).</summary>
public sealed class Inventory
{
    /// <summary>Item def ID → count.</summary>
    public Dictionary<string, int> Bag { get; } = new(StringComparer.Ordinal);

    /// <summary>Slot def ID → equipped item def ID.</summary>
    public Dictionary<string, string> Equipped { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Inventory operations. Equip effects route through the stat sheet with
/// source key <c>equip:&lt;slotId&gt;</c>, so unequip is a clean removal and
/// save restore re-derives them.
/// </summary>
public sealed class InventoryManager
{
    private readonly RpgRuntime _rpg;

    internal InventoryManager(RpgRuntime rpg)
    {
        _rpg = rpg;
    }

    public int Count(Entity entity, string itemId)
        => _rpg.GetInventory(entity)?.Bag.GetValueOrDefault(itemId) ?? 0;

    public void Give(Entity entity, string itemId, int amount)
    {
        if (amount <= 0 || _rpg.GetInventory(entity) is not { } inventory)
        {
            return;
        }

        inventory.Bag[itemId] = inventory.Bag.GetValueOrDefault(itemId) + amount;
        _rpg.Session.Events.Publish("Item.Acquired", EventPayload.Of(
            ("instanceId", entity.InstanceId), ("item", itemId), ("amount", (double)amount)));
    }

    public bool Remove(Entity entity, string itemId, int amount)
    {
        if (amount <= 0 || _rpg.GetInventory(entity) is not { } inventory)
        {
            return false;
        }

        var have = inventory.Bag.GetValueOrDefault(itemId);
        if (have < amount)
        {
            return false;
        }

        if (have == amount)
        {
            inventory.Bag.Remove(itemId);
        }
        else
        {
            inventory.Bag[itemId] = have - amount;
        }

        _rpg.Session.Events.Publish("Item.Removed", EventPayload.Of(
            ("instanceId", entity.InstanceId), ("item", itemId), ("amount", (double)amount)));
        return true;
    }

    public bool TryEquip(Entity entity, string itemId, out string? error)
    {
        error = null;
        if (_rpg.GetInventory(entity) is not { } inventory)
        {
            error = "entity has no inventory";
            return false;
        }

        if (!_rpg.Session.Defs.TryGet<ItemDef>(itemId, out var item))
        {
            error = $"unknown item '{itemId}'";
            return false;
        }

        if (item.Slot is null)
        {
            error = $"item '{itemId}' is not equippable";
            return false;
        }

        if (inventory.Bag.GetValueOrDefault(itemId) < 1)
        {
            error = $"no '{itemId}' in bag";
            return false;
        }

        if (inventory.Equipped.ContainsKey(item.Slot) && !TryUnequip(entity, item.Slot, out error))
        {
            return false;
        }

        Remove(entity, itemId, 1);
        inventory.Equipped[item.Slot] = itemId;
        ApplyEquipEffects(entity, item);
        _rpg.Session.Events.Publish("Item.Equipped", EventPayload.Of(
            ("instanceId", entity.InstanceId), ("item", itemId), ("slot", item.Slot)));
        return true;
    }

    public bool TryUnequip(Entity entity, string slotId, out string? error)
    {
        error = null;
        if (_rpg.GetInventory(entity) is not { } inventory || !inventory.Equipped.TryGetValue(slotId, out var itemId))
        {
            error = $"nothing equipped in '{slotId}'";
            return false;
        }

        inventory.Equipped.Remove(slotId);
        if (_rpg.Session.Defs.TryGet<ItemDef>(itemId, out var item))
        {
            RemoveEquipEffects(entity, item);
        }

        Give(entity, itemId, 1);
        _rpg.Session.Events.Publish("Item.Unequipped", EventPayload.Of(
            ("instanceId", entity.InstanceId), ("item", itemId), ("slot", slotId)));
        return true;
    }

    public bool TryUse(Entity user, string itemId, Entity? target, out string? error)
    {
        error = null;
        if (!_rpg.Session.Defs.TryGet<ItemDef>(itemId, out var item))
        {
            error = $"unknown item '{itemId}'";
            return false;
        }

        var inventory = _rpg.GetInventory(user);
        var equipped = inventory?.Equipped.Values.Contains(itemId) == true;
        if (!equipped && Count(user, itemId) < 1)
        {
            error = $"no '{itemId}' in inventory";
            return false;
        }

        _rpg.Effects.Run(item.UseActions, new EffectContext
        {
            Session = _rpg.Session,
            Rpg = _rpg,
            Source = user,
            Target = target ?? user,
        });

        if (item.ConsumeOnUse && !equipped)
        {
            Remove(user, itemId, 1);
        }

        _rpg.Session.Events.Publish("Item.Used", EventPayload.Of(
            ("instanceId", user.InstanceId), ("item", itemId), ("targetId", (target ?? user).InstanceId)));
        return true;
    }

    /// <summary>Restore path: re-apply equip effects for already-equipped slots (no events, no bag changes).</summary>
    internal void ReapplyEquipped(Entity entity)
    {
        var inventory = _rpg.GetInventory(entity);
        if (inventory is null)
        {
            return;
        }

        foreach (var itemId in inventory.Equipped.Values)
        {
            if (_rpg.Session.Defs.TryGet<ItemDef>(itemId, out var item))
            {
                ApplyEquipEffects(entity, item);
            }
        }
    }

    internal IEnumerable<string> GrantableTags(Entity entity)
    {
        var inventory = _rpg.GetInventory(entity);
        foreach (var itemId in inventory?.Equipped.Values ?? Enumerable.Empty<string>())
        {
            if (!_rpg.Session.Defs.TryGet<ItemDef>(itemId, out var item))
            {
                continue;
            }

            foreach (var entry in item.EquipEffects?.OfType<TagModifierEntry>() ?? [])
            {
                foreach (var tag in entry.AddTags)
                {
                    yield return tag;
                }
            }
        }
    }

    private void ApplyEquipEffects(Entity entity, ItemDef item)
    {
        var sheet = _rpg.GetSheet(entity);
        if (sheet is null || item.Slot is null)
        {
            return;
        }

        var source = $"equip:{item.Slot}";
        foreach (var entry in item.EquipEffects ?? [])
        {
            switch (entry)
            {
                case FlatModifierEntry flat when _rpg.Stats.TryGetById(flat.Stat, out var def):
                    sheet.AddModifier(new StatModifier(source, def.Key, flat.Amount, 0));
                    break;
                case PercentModifierEntry pct when _rpg.Stats.TryGetById(pct.Stat, out var def):
                    sheet.AddModifier(new StatModifier(source, def.Key, 0, pct.Percent));
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

    private void RemoveEquipEffects(Entity entity, ItemDef item)
    {
        var sheet = _rpg.GetSheet(entity);
        if (sheet is null || item.Slot is null)
        {
            return;
        }

        sheet.RemoveModifiersBySource($"equip:{item.Slot}");
        foreach (var entry in item.EquipEffects?.OfType<TagModifierEntry>() ?? [])
        {
            foreach (var tag in entry.AddTags)
            {
                sheet.RevokeTag(tag);
            }
        }
    }
}
