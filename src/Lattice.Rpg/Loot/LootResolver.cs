using Lattice.Core.Formulas;
using Lattice.Core.Simulation;
using Lattice.Rpg.Conditions;
using Lattice.Rpg.Defs;

namespace Lattice.Rpg.Loot;

/// <summary>
/// Rolls weighted loot tables (plan/02 §5) through the deterministic
/// simulation RNG. Entries may nest via tableRef (cycles are rejected by
/// validation; a depth cap guards runtime regardless).
/// </summary>
public sealed class LootResolver
{
    private const int MaxDepth = 8;

    private readonly RpgRuntime _rpg;

    internal LootResolver(RpgRuntime rpg)
    {
        _rpg = rpg;
    }

    /// <summary>Roll a table; <paramref name="context"/> is the condition/formula subject (typically the looter).</summary>
    public List<(string ItemId, int Amount)> Roll(string tableId, Entity? context)
    {
        var results = new List<(string, int)>();
        RollInto(tableId, context, results, 0);
        return results;
    }

    private void RollInto(string tableId, Entity? context, List<(string, int)> results, int depth)
    {
        if (depth >= MaxDepth || !_rpg.Session.Defs.TryGet<LootTableDef>(tableId, out var table))
        {
            return;
        }

        var conditionContext = new ConditionContext { Session = _rpg.Session, Rpg = _rpg, Subject = context };

        for (var roll = 0; roll < table.Rolls; roll++)
        {
            var eligible = table.Entries
                .Where(e => _rpg.Conditions.EvaluateAll(e.Conditions, conditionContext))
                .ToList();
            var totalWeight = eligible.Sum(e => Math.Max(0, e.Weight));
            if (totalWeight <= 0)
            {
                continue;
            }

            var pick = _rpg.Session.Rng.NextDouble() * totalWeight;
            foreach (var entry in eligible)
            {
                pick -= Math.Max(0, entry.Weight);
                if (pick >= 0)
                {
                    continue;
                }

                if (entry.TableRef is not null)
                {
                    RollInto(entry.TableRef, context, results, depth + 1);
                }
                else if (entry.Item is not null)
                {
                    var amount = entry.Amount is null
                        ? 1
                        : (int)Math.Round(_rpg.Session.Formulas.Evaluate(entry.Amount, context));
                    if (amount > 0)
                    {
                        results.Add((entry.Item, amount));
                    }
                }

                break; // "nothing" entries fall through here too
            }
        }
    }
}
