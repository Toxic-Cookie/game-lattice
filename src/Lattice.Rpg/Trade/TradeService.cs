using Lattice.Core.Events;
using Lattice.Core.Formulas;
using Lattice.Core.Simulation;
using Lattice.Rpg.Defs;

namespace Lattice.Rpg.Trade;

/// <summary>Mutable per-shop stock state (def stock is the restock template).</summary>
public sealed class ShopState
{
    public Dictionary<string, int> Stock { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Buy/sell against data-defined shops (plan/02 §6). Prices come from the
/// shop's formulas with <c>BasePrice</c> (the item's) and the customer's
/// stats in scope — Charisma pricing is just content. Shops have infinite
/// currency in v1; stock is finite and restocks on the def's
/// <c>restockOn</c> event topic.
/// </summary>
public sealed class TradeService
{
    private readonly RpgRuntime _rpg;
    private readonly Dictionary<string, ShopState> _states = new(StringComparer.Ordinal);

    internal TradeService(RpgRuntime rpg)
    {
        _rpg = rpg;
    }

    internal IReadOnlyDictionary<string, ShopState> States => _states;

    public ShopState GetState(ShopDef shop)
    {
        if (!_states.TryGetValue(shop.Id, out var state))
        {
            state = new ShopState();
            Restock(shop, state);
            _states[shop.Id] = state;
        }

        return state;
    }

    public int GetBuyPrice(ShopDef shop, ItemDef item, Entity customer)
        => EvaluatePrice(shop.BuyPriceFormula, item, customer);

    public int GetSellPrice(ShopDef shop, ItemDef item, Entity customer)
        => EvaluatePrice(shop.SellPriceFormula, item, customer);

    /// <summary>Customer buys one unit from the shop.</summary>
    public bool TryBuy(ShopDef shop, Entity customer, string itemId, out string? error)
    {
        error = null;
        if (!_rpg.Session.Defs.TryGet<ItemDef>(itemId, out var item))
        {
            error = $"unknown item '{itemId}'";
            return false;
        }

        var state = GetState(shop);
        if (state.Stock.GetValueOrDefault(itemId) < 1)
        {
            error = $"'{itemId}' is out of stock";
            return false;
        }

        var price = GetBuyPrice(shop, item, customer);
        if (_rpg.Inventory.Count(customer, shop.Currency) < price)
        {
            error = $"needs {price} {shop.Currency}";
            return false;
        }

        _rpg.Inventory.Remove(customer, shop.Currency, price);
        state.Stock[itemId]--;
        _rpg.Inventory.Give(customer, itemId, 1);
        PublishTrade("buy", shop, customer, itemId, price);
        return true;
    }

    /// <summary>Customer sells one unit to the shop.</summary>
    public bool TrySell(ShopDef shop, Entity customer, string itemId, out string? error)
    {
        error = null;
        if (!_rpg.Session.Defs.TryGet<ItemDef>(itemId, out var item))
        {
            error = $"unknown item '{itemId}'";
            return false;
        }

        if (itemId == shop.Currency)
        {
            error = "cannot sell the currency itself";
            return false;
        }

        if (!_rpg.Inventory.Remove(customer, itemId, 1))
        {
            error = $"no '{itemId}' to sell";
            return false;
        }

        var state = GetState(shop);
        var price = GetSellPrice(shop, item, customer);
        _rpg.Inventory.Give(customer, shop.Currency, price);
        state.Stock[itemId] = state.Stock.GetValueOrDefault(itemId) + 1;
        PublishTrade("sell", shop, customer, itemId, price);
        return true;
    }

    /// <summary>Reset every shop whose <c>restockOn</c> equals the topic (wired to the bus by the runtime).</summary>
    internal void RestockFor(string topic)
    {
        foreach (var shop in _rpg.Session.Defs.All<ShopDef>())
        {
            if (shop.RestockOn == topic)
            {
                Restock(shop, GetState(shop));
            }
        }
    }

    internal void RestoreState(string shopId, Dictionary<string, int> stock)
    {
        var state = new ShopState();
        foreach (var pair in stock)
        {
            state.Stock[pair.Key] = pair.Value;
        }

        _states[shopId] = state;
    }

    internal void ClearStates() => _states.Clear();

    private static void Restock(ShopDef shop, ShopState state)
    {
        state.Stock.Clear();
        foreach (var entry in shop.Stock)
        {
            state.Stock[entry.Item] = entry.Count;
        }
    }

    private int EvaluatePrice(string formula, ItemDef item, Entity customer)
    {
        var scope = new CompositeFormulaContext(new BasePriceContext(item.BasePrice), customer);
        var price = _rpg.Session.Formulas.Evaluate(formula, scope);
        return Math.Max(0, (int)Math.Round(price));
    }

    private void PublishTrade(string kind, ShopDef shop, Entity customer, string itemId, int price)
        => _rpg.Session.Events.Publish("Trade.Completed", EventPayload.Of(
            ("kind", kind),
            ("shop", shop.Id),
            ("instanceId", customer.InstanceId),
            ("item", itemId),
            ("price", (double)price)));

    private sealed class BasePriceContext(double basePrice) : IFormulaContext
    {
        public bool TryResolve(string identifier, out double value)
        {
            if (identifier == "BasePrice")
            {
                value = basePrice;
                return true;
            }

            value = 0;
            return false;
        }
    }
}
