using Lattice.Rpg.Defs;

namespace Lattice.Rpg.Tests;

public sealed class InventoryAndTradeTests : IDisposable
{
    private readonly RpgTestHost _host = new();

    public InventoryAndTradeTests() => _host.WriteStandardContent();

    public void Dispose() => _host.Dispose();

    [Fact]
    public void TemplateItems_ArriveAtSpawn()
    {
        var (session, rpg) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player"));

        Assert.Equal(30, rpg.CountItem(player, "item_gold"));
        Assert.Equal(1, rpg.CountItem(player, "item_iron_sword"));
    }

    [Fact]
    public void Equip_AppliesModifiersAndTags_UnequipRemoves()
    {
        var (session, rpg) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player"));
        var sheet = rpg.GetSheet(player)!;

        Assert.True(rpg.Inventory.TryEquip(player, "item_iron_sword", out _));
        Assert.Equal(9, sheet.Current("Str"));
        Assert.Contains("armed", player.Tags);
        Assert.Equal(0, rpg.CountItem(player, "item_iron_sword")); // moved out of bag

        Assert.True(rpg.Inventory.TryUnequip(player, "slot_main_hand", out _));
        Assert.Equal(8, sheet.Current("Str"));
        Assert.DoesNotContain("armed", player.Tags);
        Assert.Equal(1, rpg.CountItem(player, "item_iron_sword"));
    }

    [Fact]
    public void Equip_RequiresSlotAndPossession()
    {
        var (session, rpg) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player"));

        Assert.False(rpg.Inventory.TryEquip(player, "item_wolf_pelt", out var error));
        Assert.Contains("not equippable", error);

        Assert.False(rpg.Inventory.TryEquip(player, "item_healing_potion", out error));
        Assert.Contains("not equippable", error);
    }

    [Fact]
    public void UseConsumable_RunsEffectsAndConsumes()
    {
        var (session, rpg) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player"));
        var sheet = rpg.GetSheet(player)!;
        sheet.SetBase("HP", 10);
        rpg.GiveItem(player, "item_healing_potion", 1);

        Assert.True(rpg.Inventory.TryUse(player, "item_healing_potion", target: null, out _));

        Assert.Equal(20, sheet.Current("HP"));
        Assert.Equal(0, rpg.CountItem(player, "item_healing_potion"));
    }

    [Fact]
    public void CharismaPricing_AffectsBuyAndSell()
    {
        var (session, rpg) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player")); // Charisma 10
        var shop = session.Defs.Get<ShopDef>("shop_trader");
        var sword = session.Defs.Get<ItemDef>("item_iron_sword");
        var pelt = session.Defs.Get<ItemDef>("item_wolf_pelt");

        // buy: 25 * (2.0 - 0.10) = 47.5 -> 48 ; sell pelt: 8 * (0.4 + 0.10) = 4
        Assert.Equal(48, rpg.Trade.GetBuyPrice(shop, sword, player));
        Assert.Equal(4, rpg.Trade.GetSellPrice(shop, pelt, player));

        rpg.GetSheet(player)!.SetBase("Charisma", 50);
        Assert.Equal(38, rpg.Trade.GetBuyPrice(shop, sword, player)); // 25 * 1.5 = 37.5 -> 38
    }

    [Fact]
    public void Buy_TransfersGoldStockAndItem()
    {
        var (session, rpg) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player"));
        var shop = session.Defs.Get<ShopDef>("shop_trader");
        rpg.GiveItem(player, "item_gold", 100); // 130 total

        Assert.True(rpg.Trade.TryBuy(shop, player, "item_healing_potion", out _)); // price 12*1.9=22.8 -> 23

        Assert.Equal(107, rpg.CountItem(player, "item_gold"));
        Assert.Equal(1, rpg.CountItem(player, "item_healing_potion"));
        Assert.Equal(4, rpg.Trade.GetState(shop).Stock["item_healing_potion"]);
    }

    [Fact]
    public void Buy_FailsWithoutGoldOrStock()
    {
        var (session, rpg) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player")); // 30 gold

        var shop = session.Defs.Get<ShopDef>("shop_trader");
        Assert.False(rpg.Trade.TryBuy(shop, player, "item_iron_sword", out var error)); // price 48
        Assert.Contains("needs 48", error);
    }

    [Fact]
    public void Sell_PaysAndRestockEventResetsStock()
    {
        var (session, rpg) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player"));
        var shop = session.Defs.Get<ShopDef>("shop_trader");
        rpg.GiveItem(player, "item_wolf_pelt", 1);

        Assert.True(rpg.Trade.TrySell(shop, player, "item_wolf_pelt", out _));
        Assert.Equal(34, rpg.CountItem(player, "item_gold")); // 30 + 4
        Assert.Equal(1, rpg.Trade.GetState(shop).Stock["item_wolf_pelt"]);

        session.Events.Publish("Time.DayStarted");
        session.Events.DispatchPending();

        Assert.False(rpg.Trade.GetState(shop).Stock.ContainsKey("item_wolf_pelt")); // back to def stock
        Assert.Equal(2, rpg.Trade.GetState(shop).Stock["item_iron_sword"]);
    }
}
