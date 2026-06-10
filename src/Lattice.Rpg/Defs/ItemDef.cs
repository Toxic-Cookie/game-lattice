using System.Text.Json;
using Lattice.Core.Content;

namespace Lattice.Rpg.Defs;

/// <summary>
/// A data-defined item (plan/02 §4): tags, optional equipment slot,
/// use-actions (effect primitives), and equip effects (modifier primitives
/// active while worn). Currency is just an item with the "currency" tag.
/// </summary>
public sealed class ItemDef : Def
{
    public string? Name { get; set; }

    /// <summary>Slot def ID this item equips into; null = not equippable.</summary>
    public string? Slot { get; set; }

    public int StackSize { get; set; } = 99;

    public List<string>? Tags { get; set; }

    /// <summary>Base price used by shop price formulas (the BasePrice identifier).</summary>
    public double BasePrice { get; set; }

    public bool ConsumeOnUse { get; set; }

    /// <summary>Effect primitives executed on use (source = user, target = use target).</summary>
    public List<JsonElement>? UseActions { get; set; }

    /// <summary>Modifier primitives active while equipped (PeriodicEffect not allowed here).</summary>
    public List<StatusLogicEntry>? EquipEffects { get; set; }

    public override IEnumerable<DefReference> GetReferences()
    {
        if (Slot is not null)
        {
            yield return new DefReference(Slot, $"{Id}.slot");
        }

        foreach (var entry in EquipEffects ?? [])
        {
            switch (entry)
            {
                case FlatModifierEntry flat:
                    yield return new DefReference(flat.Stat, $"{Id}.equipEffects");
                    break;
                case PercentModifierEntry pct:
                    yield return new DefReference(pct.Stat, $"{Id}.equipEffects");
                    break;
            }
        }
    }
}

/// <summary>
/// RPG-extended entity template, replacing the core "entity" def kind:
/// adds loot, starting items, and auto-equipped gear. Stat keys in
/// <c>stats</c> are stat def IDs under the RPG convention.
/// </summary>
public sealed class RpgEntityTemplateDef : EntityTemplateDef
{
    /// <summary>Loot table rolled when this entity dies.</summary>
    public string? LootTable { get; set; }

    /// <summary>Item IDs equipped at spawn.</summary>
    public List<string>? Equipment { get; set; }

    /// <summary>Starting bag contents: item ID → count.</summary>
    public Dictionary<string, int>? Items { get; set; }

    public override IEnumerable<DefReference> GetReferences()
    {
        foreach (var statId in Stats?.Keys ?? Enumerable.Empty<string>())
        {
            yield return new DefReference(statId, $"{Id}.stats");
        }

        if (LootTable is not null)
        {
            yield return new DefReference(LootTable, $"{Id}.lootTable");
        }

        foreach (var item in Equipment ?? [])
        {
            yield return new DefReference(item, $"{Id}.equipment");
        }

        foreach (var item in Items?.Keys ?? Enumerable.Empty<string>())
        {
            yield return new DefReference(item, $"{Id}.items");
        }
    }
}
