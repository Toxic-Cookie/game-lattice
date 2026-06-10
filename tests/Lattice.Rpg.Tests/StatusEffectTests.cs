using Lattice.Rpg.Defs;

namespace Lattice.Rpg.Tests;

public sealed class StatusEffectTests : IDisposable
{
    private readonly RpgTestHost _host = new();

    public StatusEffectTests() => _host.WriteStandardContent();

    public void Dispose() => _host.Dispose();

    [Fact]
    public void Poison_TicksDamageAndExpires()
    {
        var (session, rpg) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player"));
        var def = session.Defs.Get<StatusEffectDef>("status_poison");

        rpg.GetStatusEffects(player)!.Apply(def, source: null);
        Assert.Contains("poisoned", player.Tags);

        RpgTestHost.TickSeconds(session, 6.5); // 6s duration, 2 dmg/s

        Assert.Equal(30 - 12, rpg.GetSheet(player)!.Current("HP"));
        Assert.Empty(rpg.GetStatusEffects(player)!.Active);
        Assert.DoesNotContain("poisoned", player.Tags);
    }

    [Fact]
    public void Refresh_ResetsDuration()
    {
        var (session, rpg) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player"));
        var def = session.Defs.Get<StatusEffectDef>("status_poison");
        var statuses = rpg.GetStatusEffects(player)!;

        statuses.Apply(def, null);
        RpgTestHost.TickSeconds(session, 4);
        statuses.Apply(def, null); // refresh

        Assert.Single(statuses.Active);
        Assert.True(statuses.Active[0].Remaining > 5.5);
    }

    [Fact]
    public void Stacking_MultipliesModifiersUpToCap()
    {
        var (session, rpg) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player"));
        var def = session.Defs.Get<StatusEffectDef>("status_might");
        var statuses = rpg.GetStatusEffects(player)!;
        var sheet = rpg.GetSheet(player)!;

        statuses.Apply(def, null);
        Assert.Equal(10, sheet.Current("Str")); // 8 + 2

        statuses.Apply(def, null);
        statuses.Apply(def, null);
        Assert.Equal(14, sheet.Current("Str")); // 8 + 2*3

        statuses.Apply(def, null); // cap 3
        Assert.Equal(3, statuses.Active[0].Stacks);
    }

    [Fact]
    public void Remove_RestoresModifiersAndTags()
    {
        var (session, rpg) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player"));
        var statuses = rpg.GetStatusEffects(player)!;
        statuses.Apply(session.Defs.Get<StatusEffectDef>("status_might"), null);
        statuses.Apply(session.Defs.Get<StatusEffectDef>("status_poison"), null);

        statuses.Remove("status_might");
        statuses.Remove("status_poison");

        Assert.Equal(8, rpg.GetSheet(player)!.Current("Str"));
        Assert.DoesNotContain("poisoned", player.Tags);
    }

    [Fact]
    public void PoisonKill_AttributesKillerFromStatusSource()
    {
        var (session, rpg) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player"));
        var wolf = session.World.All.Single(e => e.Tags.Contains("wolf"));
        string? killerId = null;
        session.Events.Subscribe("Entity.Died", e => killerId = e.Payload["killerId"] as string);

        rpg.GetSheet(player)!.SetBase("HP", 3);
        rpg.GetStatusEffects(player)!.Apply(session.Defs.Get<StatusEffectDef>("status_poison"), wolf);
        RpgTestHost.TickSeconds(session, 2.5);

        Assert.False(session.World.TryGet(player.InstanceId, out _));
        Assert.Equal(wolf.InstanceId, killerId);
    }
}
