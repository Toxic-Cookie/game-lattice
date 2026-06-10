using System.Text.Json;
using Lattice.Core.Content;

namespace Lattice.Rpg.Defs;

/// <summary>
/// Weighted loot table (plan/02 §5). Each roll picks one eligible entry by
/// weight; entries may grant an item (amount = formula, dice allowed),
/// recurse into another table, or grant nothing (weight-only entry).
/// </summary>
public sealed class LootTableDef : Def
{
    /// <summary>How many independent picks one Roll() performs.</summary>
    public int Rolls { get; set; } = 1;

    public List<LootEntry> Entries { get; set; } = [];

    public override IEnumerable<DefReference> GetReferences()
    {
        foreach (var entry in Entries)
        {
            if (entry.Item is not null)
            {
                yield return new DefReference(entry.Item, $"{Id}.entries");
            }

            if (entry.TableRef is not null)
            {
                yield return new DefReference(entry.TableRef, $"{Id}.entries");
            }
        }
    }

    public override IEnumerable<string> GetFormulas()
    {
        foreach (var entry in Entries)
        {
            if (entry.Amount is not null)
            {
                yield return entry.Amount;
            }
        }
    }

    public sealed class LootEntry
    {
        /// <summary>Item def ID granted by this entry. Mutually exclusive with <see cref="TableRef"/>.</summary>
        public string? Item { get; set; }

        /// <summary>Nested table rolled when this entry is picked.</summary>
        public string? TableRef { get; set; }

        public double Weight { get; set; } = 1;

        /// <summary>Amount formula (dice allowed); default 1.</summary>
        public string? Amount { get; set; }

        /// <summary>Condition primitives gating eligibility (evaluated against the roll context entity).</summary>
        public List<JsonElement>? Conditions { get; set; }
    }
}

/// <summary>
/// Shop definition (plan/02 §6): stock plus buy/sell price formulas evaluated
/// with the item's BasePrice and the trading entity's stats in scope.
/// </summary>
public sealed class ShopDef : Def
{
    public string? Name { get; set; }

    /// <summary>Currency item ID exchanged in trades.</summary>
    public string Currency { get; set; } = "item_gold";

    /// <summary>Price the customer pays when buying from the shop.</summary>
    public string BuyPriceFormula { get; set; } = "BasePrice";

    /// <summary>Price the customer receives when selling to the shop.</summary>
    public string SellPriceFormula { get; set; } = "BasePrice * 0.5";

    public List<StockEntry> Stock { get; set; } = [];

    /// <summary>Event topic that resets stock to this def (e.g. "Time.DayStarted", live in M5).</summary>
    public string? RestockOn { get; set; }

    public override IEnumerable<DefReference> GetReferences()
    {
        yield return new DefReference(Currency, $"{Id}.currency");
        foreach (var entry in Stock)
        {
            yield return new DefReference(entry.Item, $"{Id}.stock");
        }
    }

    public override IEnumerable<string> GetFormulas()
    {
        yield return BuyPriceFormula;
        yield return SellPriceFormula;
    }

    public sealed class StockEntry
    {
        public string Item { get; set; } = "";

        public int Count { get; set; } = 1;
    }
}
