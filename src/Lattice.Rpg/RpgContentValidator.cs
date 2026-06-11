using Lattice.Core.Content;
using Lattice.Core.Formulas;
using Lattice.Rpg.Conditions;
using Lattice.Rpg.Defs;
using Lattice.Rpg.Effects;

namespace Lattice.Rpg;

/// <summary>
/// RPG cross-def validation rules (plan/02 acceptance + plan/06 §3): stat
/// key uniqueness, stat formula identifier/cycle checks, effect/condition
/// payload validation, loot cycles, slot/type checks, shop formulas.
/// </summary>
public sealed class RpgContentValidator : IContentValidator
{
    private readonly EffectRegistry? _effects;
    private readonly ConditionRegistry? _conditions;

    public RpgContentValidator()
    {
    }

    /// <summary>Validate against a live module's registries so module-added primitives (e.g. StartQuest) are known.</summary>
    public RpgContentValidator(EffectRegistry effects, ConditionRegistry conditions)
    {
        _effects = effects;
        _conditions = conditions;
    }

    public void Validate(DefRegistry registry, ContentLoadReport report, IFormulaEngine formulas)
    {
        var effects = _effects ?? BuiltinEffects.CreateDefault();
        var conditions = _conditions ?? ConditionRegistry.CreateDefault();
        var statKeys = ValidateStats(registry, report, formulas);
        ValidateItems(registry, report, formulas, effects);
        ValidateStatuses(registry, report, formulas, effects);
        ValidateLoot(registry, report, formulas, conditions);
        ValidateShops(registry, report, formulas, statKeys, conditions);
        ValidateTemplates(registry, report);
    }

    private static HashSet<string> ValidateStats(DefRegistry registry, ContentLoadReport report, IFormulaEngine formulas)
    {
        var stats = registry.All<StatDef>().ToList();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stat in stats)
        {
            if (string.IsNullOrWhiteSpace(stat.Key))
            {
                report.Errors.Add($"Stat '{stat.Id}' has an empty 'key'.");
            }
            else if (!keys.Add(stat.Key))
            {
                report.Errors.Add($"Stat key '{stat.Key}' (on '{stat.Id}') is declared by more than one stat.");
            }

            if (stat.IsDerived && (stat.Default is not null || stat.Vital))
            {
                report.Warnings.Add($"Derived stat '{stat.Id}' ignores default/vital settings.");
            }
        }

        // identifier + dependency cycle checks over the key graph
        var dependencies = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var stat in stats)
        {
            var deps = new HashSet<string>(StringComparer.Ordinal);
            foreach (var formula in stat.GetFormulas())
            {
                IReadOnlyCollection<string> identifiers;
                try
                {
                    identifiers = formulas.GetIdentifiers(formula);
                }
                catch (FormulaException)
                {
                    continue; // parse error already reported by the core pass
                }

                foreach (var identifier in identifiers)
                {
                    if (!keys.Contains(identifier))
                    {
                        report.Errors.Add($"Stat '{stat.Id}' formula \"{formula}\" references unknown stat key '{identifier}'.");
                    }
                    else
                    {
                        deps.Add(identifier);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(stat.Key))
            {
                dependencies[stat.Key] = deps;
            }
        }

        DetectCycles(
            dependencies.Keys,
            key => dependencies.TryGetValue(key, out var deps) ? deps : [],
            cycle => report.Errors.Add($"Stat dependency cycle: {cycle}."),
            report);

        return keys;
    }

    private static void ValidateItems(DefRegistry registry, ContentLoadReport report, IFormulaEngine formulas, EffectRegistry effects)
    {
        foreach (var item in registry.All<ItemDef>())
        {
            if (item.Slot is not null && registry.Contains(item.Slot) && !registry.TryGet<SlotDef>(item.Slot, out _))
            {
                report.Errors.Add($"Item '{item.Id}' slot '{item.Slot}' is not a slot def.");
            }

            if (item.EquipEffects?.OfType<PeriodicEffectEntry>().Any() == true)
            {
                report.Errors.Add($"Item '{item.Id}' equipEffects may not contain PeriodicEffect entries.");
            }

            effects.ValidateList(item.UseActions, $"{item.Id}.useActions", registry, formulas, report);
        }
    }

    private static void ValidateStatuses(DefRegistry registry, ContentLoadReport report, IFormulaEngine formulas, EffectRegistry effects)
    {
        foreach (var status in registry.All<StatusEffectDef>())
        {
            if (status.Stacking is not ("refresh" or "stack" or "ignore"))
            {
                report.Errors.Add($"Status '{status.Id}' has unknown stacking policy '{status.Stacking}'.");
            }

            foreach (var periodic in (status.Logic ?? []).OfType<PeriodicEffectEntry>())
            {
                if (periodic.Interval <= 0)
                {
                    report.Errors.Add($"Status '{status.Id}' has a PeriodicEffect with non-positive interval.");
                }

                effects.ValidateList(periodic.Effects, $"{status.Id}.logic", registry, formulas, report);
            }
        }
    }

    private static void ValidateLoot(DefRegistry registry, ContentLoadReport report, IFormulaEngine formulas, ConditionRegistry conditions)
    {
        var tables = registry.All<LootTableDef>().ToList();
        foreach (var table in tables)
        {
            foreach (var entry in table.Entries)
            {
                if (entry.Item is not null && entry.TableRef is not null)
                {
                    report.Errors.Add($"Loot table '{table.Id}' has an entry with both 'item' and 'tableRef'.");
                }

                if (entry.Weight < 0)
                {
                    report.Errors.Add($"Loot table '{table.Id}' has a negative weight.");
                }

                conditions.ValidateList(entry.Conditions, $"{table.Id}.entries", registry, formulas, report);
            }
        }

        DetectCycles(
            tables.Select(t => t.Id),
            id => registry.TryGet<LootTableDef>(id, out var t)
                ? t.Entries.Where(e => e.TableRef is not null).Select(e => e.TableRef!)
                : [],
            cycle => report.Errors.Add($"Loot table cycle: {cycle}."),
            report);
    }

    private static void ValidateShops(DefRegistry registry, ContentLoadReport report, IFormulaEngine formulas, HashSet<string> statKeys, ConditionRegistry conditions)
    {
        foreach (var shop in registry.All<ShopDef>())
        {
            if (registry.Contains(shop.Currency) && !registry.TryGet<ItemDef>(shop.Currency, out _))
            {
                report.Errors.Add($"Shop '{shop.Id}' currency '{shop.Currency}' is not an item def.");
            }

            conditions.ValidateList(shop.OpenWhen, $"{shop.Id}.openWhen", registry, formulas, report);

            foreach (var formula in new[] { shop.BuyPriceFormula, shop.SellPriceFormula })
            {
                IReadOnlyCollection<string> identifiers;
                try
                {
                    identifiers = formulas.GetIdentifiers(formula);
                }
                catch (FormulaException)
                {
                    continue;
                }

                foreach (var identifier in identifiers)
                {
                    if (identifier != "BasePrice" && !statKeys.Contains(identifier))
                    {
                        report.Errors.Add(
                            $"Shop '{shop.Id}' price formula \"{formula}\" references unknown identifier '{identifier}'.");
                    }
                }
            }
        }
    }

    private static void ValidateTemplates(DefRegistry registry, ContentLoadReport report)
    {
        foreach (var template in registry.All<RpgEntityTemplateDef>())
        {
            foreach (var statId in template.Stats?.Keys ?? Enumerable.Empty<string>())
            {
                if (registry.Contains(statId) && !registry.TryGet<StatDef>(statId, out _))
                {
                    report.Errors.Add($"Template '{template.Id}' stat '{statId}' is not a stat def.");
                }
            }

            foreach (var itemId in template.Equipment ?? [])
            {
                if (registry.TryGet<ItemDef>(itemId, out var item) && item.Slot is null)
                {
                    report.Errors.Add($"Template '{template.Id}' equipment '{itemId}' has no slot and cannot be equipped.");
                }
            }
        }
    }

    /// <summary>DFS cycle detection over an ID graph; reports each cycle once.</summary>
    private static void DetectCycles(
        IEnumerable<string> nodes,
        Func<string, IEnumerable<string>> edges,
        Action<string> reportCycle,
        ContentLoadReport report)
    {
        var state = new Dictionary<string, int>(StringComparer.Ordinal); // 0 unvisited, 1 in-stack, 2 done
        var stack = new List<string>();

        foreach (var node in nodes)
        {
            Visit(node);
        }

        return;

        void Visit(string node)
        {
            if (state.TryGetValue(node, out var s))
            {
                if (s == 1)
                {
                    var start = stack.IndexOf(node);
                    reportCycle(string.Join(" -> ", stack.Skip(start).Append(node)));
                }

                return;
            }

            state[node] = 1;
            stack.Add(node);
            foreach (var next in edges(node))
            {
                Visit(next);
            }

            stack.RemoveAt(stack.Count - 1);
            state[node] = 2;
        }
    }
}
