using System.Text.Json;
using Lattice.Core.Content;
using Lattice.Core.Persistence;
using Lattice.Core.Simulation;
using Lattice.Rpg.Defs;

namespace Lattice.Rpg;

/// <summary>
/// The "rpg" slice of the save file: active statuses, inventories, and shop
/// stock. Stat modifiers and granted tags are deliberately not saved — they
/// are re-derived from statuses and equipped items on restore.
/// </summary>
internal sealed class RpgSaveSection(RpgRuntime rpg) : ISaveSection
{
    public string Key => "rpg";

    public JsonElement Capture(GameSession session)
    {
        var data = new SectionData();

        foreach (var entity in session.World.All.OrderBy(e => e.InstanceId, StringComparer.Ordinal))
        {
            var saved = new SavedEntityRpg();
            var statuses = rpg.GetStatusEffects(entity);
            foreach (var status in statuses?.Active ?? [])
            {
                saved.Statuses.Add(new SavedStatus
                {
                    Def = status.Def.Id,
                    Remaining = status.Remaining,
                    Stacks = status.Stacks,
                    SourceId = status.SourceId,
                });
            }

            var inventory = rpg.GetInventory(entity);
            if (inventory is not null)
            {
                foreach (var pair in inventory.Bag.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    saved.Bag[pair.Key] = pair.Value;
                }

                foreach (var pair in inventory.Equipped.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    saved.Equipped[pair.Key] = pair.Value;
                }
            }

            if (saved.Statuses.Count > 0 || saved.Bag.Count > 0 || saved.Equipped.Count > 0)
            {
                data.Entities[entity.InstanceId] = saved;
            }
        }

        foreach (var pair in rpg.Trade.States.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            data.Shops[pair.Key] = pair.Value.Stock
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        }

        return JsonSerializer.SerializeToElement(data, ContentLoader.JsonOptions);
    }

    public void Restore(GameSession session, JsonElement data, ContentLoadReport report)
    {
        SectionData? section;
        try
        {
            section = data.Deserialize<SectionData>(ContentLoader.JsonOptions);
        }
        catch (JsonException ex)
        {
            report.Errors.Add($"rpg save section is invalid: {ex.Message}");
            return;
        }

        if (section is null)
        {
            return;
        }

        rpg.Trade.ClearStates();
        foreach (var pair in section.Shops)
        {
            rpg.Trade.RestoreState(pair.Key, pair.Value);
        }

        foreach (var pair in section.Entities)
        {
            if (!session.World.TryGet(pair.Key, out var entity))
            {
                report.Warnings.Add($"rpg save section references missing entity '{pair.Key}'.");
                continue;
            }

            var saved = pair.Value;
            var sheet = rpg.GetSheet(entity);
            var inventory = rpg.GetInventory(entity);
            var statuses = rpg.GetStatusEffects(entity);
            if (sheet is null || inventory is null || statuses is null)
            {
                continue;
            }

            // saved entity tags include granted ones; strip what's about to be re-granted
            var grantable = new List<string>();
            foreach (var savedStatus in saved.Statuses)
            {
                if (session.Defs.TryGet<StatusEffectDef>(savedStatus.Def, out var def))
                {
                    grantable.AddRange((def.Logic ?? []).OfType<TagModifierEntry>().SelectMany(t => t.AddTags));
                }
            }

            foreach (var slotPair in saved.Equipped)
            {
                inventory.Equipped[slotPair.Key] = slotPair.Value;
            }

            grantable.AddRange(rpg.Inventory.GrantableTags(entity));
            sheet.StripGrantableTags(grantable);

            foreach (var bagPair in saved.Bag)
            {
                inventory.Bag[bagPair.Key] = bagPair.Value;
            }

            rpg.Inventory.ReapplyEquipped(entity);

            foreach (var savedStatus in saved.Statuses)
            {
                if (session.Defs.TryGet<StatusEffectDef>(savedStatus.Def, out var def))
                {
                    statuses.RestoreStatus(def, savedStatus.Remaining, savedStatus.Stacks, savedStatus.SourceId);
                }
                else
                {
                    report.Warnings.Add($"Entity {pair.Key} had unknown status '{savedStatus.Def}'; dropped.");
                }
            }
        }
    }

    private sealed class SectionData
    {
        public Dictionary<string, SavedEntityRpg> Entities { get; set; } = new(StringComparer.Ordinal);

        public Dictionary<string, Dictionary<string, int>> Shops { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class SavedEntityRpg
    {
        public List<SavedStatus> Statuses { get; set; } = [];

        public Dictionary<string, int> Bag { get; set; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> Equipped { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class SavedStatus
    {
        public string Def { get; set; } = "";

        public double Remaining { get; set; }

        public int Stacks { get; set; } = 1;

        public string? SourceId { get; set; }
    }
}
