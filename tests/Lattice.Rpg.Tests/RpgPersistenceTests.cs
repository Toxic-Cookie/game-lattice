using Lattice.Core.Persistence;
using Lattice.Rpg.Defs;

namespace Lattice.Rpg.Tests;

public sealed class RpgPersistenceTests : IDisposable
{
    private readonly RpgTestHost _host = new(seed: 7);

    public RpgPersistenceTests() => _host.WriteStandardContent();

    public void Dispose() => _host.Dispose();

    [Fact]
    public void SaveLoad_RoundTripsStatusesInventoryAndShops()
    {
        var (session, rpg) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player"));
        var shop = session.Defs.Get<ShopDef>("shop_trader");

        rpg.Inventory.TryEquip(player, "item_iron_sword", out _);
        rpg.GetStatusEffects(player)!.Apply(session.Defs.Get<StatusEffectDef>("status_poison"), null);
        RpgTestHost.TickSeconds(session, 2);
        rpg.GiveItem(player, "item_gold", 70);
        rpg.Trade.TryBuy(shop, player, "item_healing_potion", out _);

        var json = SaveManager.Capture(session);

        var (restored, restoredRpg) = _host.CreateLoadedSession(boot: false);
        var report = SaveManager.Restore(restored, json);
        Assert.True(report.Ok, string.Join("; ", report.Errors));

        var restoredPlayer = restored.World.All.Single(e => e.Tags.Contains("player"));
        var sheet = restoredRpg.GetSheet(restoredPlayer)!;

        // equipped sword modifier re-derived (+1 Str), poison still ticking with remaining time
        Assert.Equal(9, sheet.Current("Str"));
        Assert.Contains("armed", restoredPlayer.Tags);
        var status = restoredRpg.GetStatusEffects(restoredPlayer)!.Active.Single();
        Assert.Equal("status_poison", status.Def.Id);
        Assert.InRange(status.Remaining, 3.5, 4.5);
        Assert.Contains("poisoned", restoredPlayer.Tags);

        Assert.Equal(1, restoredRpg.CountItem(restoredPlayer, "item_healing_potion"));
        Assert.Equal(4, restoredRpg.Trade.GetState(shop).Stock["item_healing_potion"]);
    }

    [Fact]
    public void StatusExpiryAfterLoad_RemovesGrantedTagsCleanly()
    {
        var (session, rpg) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player"));
        rpg.GetStatusEffects(player)!.Apply(session.Defs.Get<StatusEffectDef>("status_poison"), null);

        var json = SaveManager.Capture(session);
        var (restored, restoredRpg) = _host.CreateLoadedSession(boot: false);
        SaveManager.Restore(restored, json);

        RpgTestHost.TickSeconds(restored, 7); // let poison expire post-load

        var restoredPlayer = restored.World.All.Single(e => e.Tags.Contains("player"));
        Assert.Empty(restoredRpg.GetStatusEffects(restoredPlayer)!.Active);
        Assert.DoesNotContain("poisoned", restoredPlayer.Tags); // tag-strip on restore worked
    }

    [Fact]
    public void Determinism_SaveLoadTick_EqualsUninterruptedRun()
    {
        var (a, rpgA) = _host.CreateLoadedSession();
        var playerA = a.World.All.Single(e => e.Tags.Contains("player"));
        rpgA.GetStatusEffects(playerA)!.Apply(a.Defs.Get<StatusEffectDef>("status_poison"), null);
        RpgTestHost.TickSeconds(a, 1);
        var mid = SaveManager.Capture(a);
        RpgTestHost.TickSeconds(a, 2);
        var finalA = SaveManager.Capture(a);

        var (b, _) = _host.CreateLoadedSession(boot: false);
        SaveManager.Restore(b, mid);
        RpgTestHost.TickSeconds(b, 2);
        var finalB = SaveManager.Capture(b);

        Assert.Equal(finalA, finalB);
    }
}
