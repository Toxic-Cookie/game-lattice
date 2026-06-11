using System.Numerics;
using System.Text.Json;
using Lattice.Rpg.Defs;

namespace Lattice.World.Tests;

/// <summary>Markov weather and season overlays (plan/05 §3–4).</summary>
public sealed class WeatherSeasonTests : IDisposable
{
    private readonly WorldTestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public void Weather_AlternatesDeterministicallyAndHoldsItsFlags()
    {
        // a two-state clock: clear and rain always swap after exactly one hour
        _host.WriteStandardContent();
        _host.WriteContent("world.json", """
            [ { "id": "time_test", "type": "time", "minutesPerGameDay": 12, "daysPerSeason": 1,
                "seasons": ["season_summer", "season_winter"], "startHour": 8 },
              { "id": "weather_clear", "type": "weather",
                "transitions": { "weather_rain": 1 }, "minHours": 1, "maxHours": 1 },
              { "id": "weather_rain", "type": "weather",
                "flags": { "sense_auditory_mult": 0.5 },
                "transitions": { "weather_clear": 1 }, "minHours": 1, "maxHours": 1 },
              { "id": "season_summer", "type": "season" },
              { "id": "season_winter", "type": "season" } ]
            """);
        var (session, _, world) = _host.CreateLoadedSession();

        Assert.Equal("weather_clear", world.WeatherId);
        Assert.Equal(1.0, session.Flags.ReadNumber("sense_auditory_mult", 1.0));

        WorldTestHost.TickSeconds(session, 1.2 * 30); // past the first hour boundary
        Assert.Equal("weather_rain", world.WeatherId);
        Assert.Equal("weather_rain", session.Flags.ReadString("weather"));
        Assert.Equal(0.5, session.Flags.ReadNumber("sense_auditory_mult", 1.0));

        WorldTestHost.TickSeconds(session, 1.0 * 30);
        Assert.Equal("weather_clear", world.WeatherId);
        Assert.Equal(1.0, session.Flags.ReadNumber("sense_auditory_mult", 1.0)); // rain's flags cleared on exit
    }

    [Fact]
    public void WeatherBoundaries_RunTaggedEffects()
    {
        _host.WriteStandardContent();
        _host.WriteContent("statuses.json", """
            { "id": "status_soaked", "type": "status", "name": "Soaked", "duration": 0 }
            """);
        _host.WriteContent("entities.json", """
            { "id": "entity_npc", "type": "entity", "name": "NPC", "tags": ["npc"], "stats": { "stat_con": 3 } }
            """);
        _host.WriteContent("world.json", """
            [ { "id": "time_test", "type": "time", "minutesPerGameDay": 12, "daysPerSeason": 1,
                "seasons": ["season_summer", "season_winter"], "startHour": 8 },
              { "id": "weather_clear", "type": "weather",
                "transitions": { "weather_rain": 1 }, "minHours": 1, "maxHours": 1 },
              { "id": "weather_rain", "type": "weather",
                "onEnter": [ { "tag": "npc", "effects": [ { "type": "ApplyStatus", "status": "status_soaked" } ] } ],
                "onExit":  [ { "tag": "npc", "effects": [ { "type": "RemoveStatus", "status": "status_soaked" } ] } ],
                "transitions": { "weather_clear": 1 }, "minHours": 1, "maxHours": 1 },
              { "id": "season_summer", "type": "season" },
              { "id": "season_winter", "type": "season" } ]
            """);
        var (session, rpg, _) = _host.CreateLoadedSession();
        var npc = session.World.Spawn("entity_npc", Vector3.Zero);

        WorldTestHost.TickSeconds(session, 1.2 * 30); // rain begins
        Assert.Contains(rpg.GetStatusEffects(npc)!.Active, s => s.Def.Id == "status_soaked");

        WorldTestHost.TickSeconds(session, 1.0 * 30); // rain ends
        Assert.DoesNotContain(rpg.GetStatusEffects(npc)!.Active, s => s.Def.Id == "status_soaked");
    }

    [Fact]
    public void WinterOverlay_RedirectsTheLootTable()
    {
        _host.WriteStandardContent(); // daysPerSeason 1: day 2 = winter
        var (session, _, world) = _host.CreateLoadedSession();
        var seasonEvents = new List<string>();
        session.Events.Subscribe("Time.SeasonStarted", e => seasonEvents.Add((string)e.Payload["season"]!));

        Assert.Equal("season_summer", world.SeasonId);
        Assert.True(session.Defs.TryGet<LootTableDef>("loot_forest", out var summerTable));
        Assert.Equal("loot_forest", summerTable.Id);

        WorldTestHost.TickSeconds(session, 16.1 * 30); // into day 2

        Assert.Equal("season_winter", world.SeasonId);
        Assert.Contains("season_winter", seasonEvents);
        Assert.True(session.Defs.TryGet<LootTableDef>("loot_forest", out var winterTable));
        Assert.Equal("loot_forest_winter", winterTable.Id); // the overlay in action
        Assert.Equal("season_winter", session.Flags.ReadString("season"));
    }

    [Fact]
    public void Shop_RespectsOpenWhenHours()
    {
        _host.WriteStandardContent();
        _host.WriteContent("entities.json", """
            { "id": "entity_buyer", "type": "entity", "name": "Buyer", "stats": { "stat_con": 3 } }
            """);
        _host.WriteContent("shop.json", """
            { "id": "shop_test", "type": "shop", "currency": "item_gold",
              "buyPriceFormula": "BasePrice", "sellPriceFormula": "BasePrice",
              "openWhen": [ { "type": "Not", "condition": { "type": "FlagEquals", "flag": "is_night", "value": true } } ],
              "stock": [ { "item": "item_pelt", "count": 5 } ] }
            """);
        var (session, rpg, _) = _host.CreateLoadedSession();
        var buyer = session.World.Spawn("entity_buyer", Vector3.Zero);
        rpg.GiveItem(buyer, "item_gold", 100);
        var shop = session.Defs.Get<ShopDef>("shop_test");

        Assert.True(rpg.Trade.TryBuy(shop, buyer, "item_pelt", out _)); // daytime at boot

        WorldTestHost.TickSeconds(session, 14.1 * 30); // 22:06 — night

        Assert.False(rpg.Trade.TryBuy(shop, buyer, "item_pelt", out var error));
        Assert.Equal("the shop is closed", error);
    }
}
