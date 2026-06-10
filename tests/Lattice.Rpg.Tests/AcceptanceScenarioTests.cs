using Lattice.Rpg.Defs;

namespace Lattice.Rpg.Tests;

/// <summary>
/// The M2 acceptance scenario from plan/02: spawn player + wolf; the wolf
/// bites (formula damage + poison); the player equips the iron sword and
/// kills the wolf (equip-modified formula damage); the wolf's loot table
/// rolls to the killer; the player sells a pelt at Charisma-adjusted prices.
/// All content JSON — zero scenario-specific C#.
/// </summary>
public sealed class AcceptanceScenarioTests : IDisposable
{
    private readonly RpgTestHost _host = new(seed: 1234);

    public AcceptanceScenarioTests() => _host.WriteStandardContent();

    public void Dispose() => _host.Dispose();

    [Fact]
    public void FullCombatLootTradeLoop()
    {
        var (session, rpg) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player"));
        var wolf = session.World.All.Single(e => e.Tags.Contains("wolf"));
        var playerSheet = rpg.GetSheet(player)!;

        // --- wolf bites the player: Str 3 * 2 = 6 damage + poison applied
        Assert.True(rpg.Inventory.TryUse(wolf, "item_wolf_fang", player, out _));
        Assert.Equal(24, playerSheet.Current("HP")); // 30 - 6
        Assert.True(rpg.GetStatusEffects(player)!.Has("status_poison"));
        Assert.Contains("poisoned", player.Tags);

        // --- poison ticks: 2 dmg/s for 2 seconds
        RpgTestHost.TickSeconds(session, 2);
        Assert.Equal(20, playerSheet.Current("HP"));

        // --- player equips the sword (+1 Str) and strikes back
        Assert.True(rpg.Inventory.TryEquip(player, "item_iron_sword", out _));
        Assert.Equal(9, playerSheet.Current("Str"));

        Assert.True(rpg.Inventory.TryUse(player, "item_iron_sword", wolf, out _));
        // damage = Str*2+4 = 22 >= wolf HP 15 -> death, loot to killer
        session.Events.DispatchPending();
        Assert.False(session.World.TryGet(wolf.InstanceId, out _));

        var lootEvents = session.Events.Trace.Where(e => e.Topic == "Loot.Dropped").ToList();
        Assert.Single(lootEvents);

        // --- cure the poison with a looted-or-bought potion path: buy one
        var shop = session.Defs.Get<ShopDef>("shop_trader");
        rpg.GiveItem(player, "item_gold", 50);
        Assert.True(rpg.Trade.TryBuy(shop, player, "item_healing_potion", out _)); // 23 gold at Cha 10
        Assert.True(rpg.Inventory.TryUse(player, "item_healing_potion", null, out _));
        Assert.False(rpg.GetStatusEffects(player)!.Has("status_poison"));
        Assert.DoesNotContain("poisoned", player.Tags);

        // --- sell any looted pelts at Charisma-adjusted price (4 gold each at Cha 10)
        var pelts = rpg.CountItem(player, "item_wolf_pelt");
        var goldBefore = rpg.CountItem(player, "item_gold");
        for (var i = 0; i < pelts; i++)
        {
            Assert.True(rpg.Trade.TrySell(shop, player, "item_wolf_pelt", out _));
        }

        Assert.Equal(goldBefore + pelts * 4, rpg.CountItem(player, "item_gold"));

        // --- the whole exchange surfaced as events
        session.Events.DispatchPending();
        var topics = session.Events.Trace.Select(e => e.Topic).Distinct().ToList();
        Assert.Contains("Entity.Damaged", topics);
        Assert.Contains("Status.Applied", topics);
        Assert.Contains("Entity.Died", topics);
        Assert.Contains("Item.Equipped", topics);
        if (pelts > 0)
        {
            Assert.Contains("Trade.Completed", topics);
        }
    }
}
