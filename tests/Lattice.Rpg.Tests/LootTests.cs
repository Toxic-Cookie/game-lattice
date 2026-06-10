namespace Lattice.Rpg.Tests;

public sealed class LootTests : IDisposable
{
    private readonly RpgTestHost _host = new(seed: 42);

    public LootTests() => _host.WriteStandardContent();

    public void Dispose() => _host.Dispose();

    [Fact]
    public void Roll_IsDeterministicPerSeed()
    {
        var (sessionA, rpgA) = _host.CreateLoadedSession();
        var playerA = sessionA.World.All.Single(e => e.Tags.Contains("player"));
        var dropsA = Enumerable.Range(0, 20).Select(_ => rpgA.Loot.Roll("loot_wolf", playerA)).ToList();

        using var hostB = new RpgTestHost(seed: 42);
        hostB.WriteStandardContent();
        var (sessionB, rpgB) = hostB.CreateLoadedSession();
        var playerB = sessionB.World.All.Single(e => e.Tags.Contains("player"));
        var dropsB = Enumerable.Range(0, 20).Select(_ => rpgB.Loot.Roll("loot_wolf", playerB)).ToList();

        Assert.Equal(
            dropsA.Select(d => string.Join(",", d.Select(x => $"{x.ItemId}:{x.Amount}"))),
            dropsB.Select(d => string.Join(",", d.Select(x => $"{x.ItemId}:{x.Amount}"))));
    }

    [Fact]
    public void GoldAmounts_StayInDiceRange()
    {
        var (session, rpg) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player"));

        for (var i = 0; i < 100; i++)
        {
            foreach (var (itemId, amount) in rpg.Loot.Roll("loot_wolf", player))
            {
                if (itemId == "item_gold")
                {
                    Assert.InRange(amount, 6, 15); // 1d10+5
                }
            }
        }
    }

    [Fact]
    public void NestedTable_ResolvesThroughTableRef()
    {
        var (session, rpg) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player"));

        // loot_rare is reachable only via tableRef; roll enough times to hit it
        var sawPotion = false;
        for (var i = 0; i < 500 && !sawPotion; i++)
        {
            sawPotion = rpg.Loot.Roll("loot_wolf", player).Any(d => d.ItemId == "item_healing_potion");
        }

        Assert.True(sawPotion, "tableRef entry never resolved in 500 rolls");
    }

    [Fact]
    public void Conditions_FilterEntries()
    {
        _host.WriteContent("loot-conditional.json", """
            { "id": "loot_conditional", "type": "loot", "entries": [
                { "item": "item_wolf_pelt", "weight": 1,
                  "conditions": [ { "type": "HasTag", "tag": "wolf" } ] } ] }
            """);
        var (session, rpg) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player"));
        var wolf = session.World.All.Single(e => e.Tags.Contains("wolf"));

        Assert.Empty(rpg.Loot.Roll("loot_conditional", player));
        Assert.Single(rpg.Loot.Roll("loot_conditional", wolf));
    }

    [Fact]
    public void WolfDeath_DropsLootToKiller()
    {
        var (session, rpg) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player"));
        var wolf = session.World.All.Single(e => e.Tags.Contains("wolf"));
        var goldBefore = rpg.CountItem(player, "item_gold");

        // kill via sword use (player Str 8 -> 20 damage > wolf HP 15)
        Assert.True(rpg.Inventory.TryUse(player, "item_iron_sword", wolf, out _));
        session.Events.DispatchPending();

        Assert.False(session.World.TryGet(wolf.InstanceId, out _));
        var gained = rpg.CountItem(player, "item_gold") - goldBefore
                     + rpg.CountItem(player, "item_wolf_pelt")
                     + rpg.CountItem(player, "item_healing_potion");
        Assert.True(gained > 0, "killer received no loot");
    }
}
