namespace Lattice.Rpg.Tests;

/// <summary>
/// UI data binding (plan/06 §6): path resolution over stats, inventory, and
/// flags, plus event-driven (never polled) change notification.
/// </summary>
public sealed class BindingTests : IDisposable
{
    private readonly RpgTestHost _host = new();

    public BindingTests() => _host.WriteStandardContent();

    public void Dispose() => _host.Dispose();

    [Fact]
    public void Resolve_ReadsStatsInventoryAndFlags()
    {
        var (session, rpg) = _host.CreateLoadedSession();

        Assert.Equal(30.0, rpg.Bindings.Resolve("Player.stats.stat_hp"));      // Con 4 -> 4*5+10
        Assert.Equal(30.0, rpg.Bindings.Resolve("Player.stats.HP"));           // key form
        Assert.Equal(30.0, rpg.Bindings.Resolve("Player.stats.stat_hp.max"));
        Assert.Equal(30.0, rpg.Bindings.Resolve("Player.inventory.item_gold"));

        session.Flags.Write("weather", "rain");
        Assert.Equal("rain", rpg.Bindings.Resolve("flags.weather"));

        Assert.Null(rpg.Bindings.Resolve("Player.stats.NoSuchStat"));
        Assert.Null(rpg.Bindings.Resolve("ghost.stats.HP"));
    }

    [Fact]
    public void Subscribe_FiresImmediatelyAndOnStatChanges()
    {
        var (session, rpg) = _host.CreateLoadedSession();
        var player = session.World.All.First(e => e.Tags.Contains("player"));
        var values = new List<object?>();
        rpg.Bindings.Subscribe("Player.stats.stat_hp", values.Add);
        Assert.Equal([30.0], values); // immediate initial value

        rpg.RunEffects([System.Text.Json.JsonDocument.Parse(
            """{ "type": "DealDamage", "formula": "10" }""").RootElement], source: player, target: player);
        session.Events.DispatchPending(); // bindings ride the (deferred) event bus

        Assert.Equal(2, values.Count);
        Assert.Equal(20.0, values[^1]);
    }

    [Fact]
    public void Subscribe_FiresOnInventoryAndFlagChanges()
    {
        var (session, rpg) = _host.CreateLoadedSession();
        var player = session.World.All.First(e => e.Tags.Contains("player"));

        var gold = new List<object?>();
        rpg.Bindings.Subscribe("Player.inventory.item_gold", gold.Add);
        rpg.GiveItem(player, "item_gold", 5);
        session.Events.DispatchPending();
        Assert.Equal(35.0, gold[^1]);

        var weather = new List<object?>();
        rpg.Bindings.Subscribe("flags.weather", weather.Add);
        session.Flags.Write("weather", "rain");
        Assert.Equal([null, "rain"], weather);
    }

    [Fact]
    public void DisposedSubscription_StopsFiring()
    {
        var (session, rpg) = _host.CreateLoadedSession();
        var player = session.World.All.First(e => e.Tags.Contains("player"));
        var count = 0;
        var subscription = rpg.Bindings.Subscribe("Player.inventory.item_gold", _ => count++);
        subscription.Dispose();

        rpg.GiveItem(player, "item_gold", 1);
        session.Events.DispatchPending();

        Assert.Equal(1, count); // only the initial callback
    }
}
