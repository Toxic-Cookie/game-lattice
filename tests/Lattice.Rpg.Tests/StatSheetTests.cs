using Lattice.Rpg.Stats;

namespace Lattice.Rpg.Tests;

public sealed class StatSheetTests : IDisposable
{
    private readonly RpgTestHost _host = new();

    public StatSheetTests() => _host.WriteStandardContent();

    public void Dispose() => _host.Dispose();

    [Fact]
    public void TemplateStats_ConvertToKeysAndApplyDefaults()
    {
        var (session, _) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player"));

        Assert.Equal(8, player.Stats["Str"]);          // id stat_str -> key Str
        Assert.Equal(30, player.Stats["HP"]);          // default "max" = Con*5+10 = 30
        Assert.False(player.Stats.ContainsKey("stat_str"));
    }

    [Fact]
    public void Modifiers_ApplyFlatThenPercentThenClamp()
    {
        var (session, rpg) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player"));
        var sheet = rpg.GetSheet(player)!;

        sheet.AddModifier(new StatModifier("test", "Str", 2, 0));    // (8+2) = 10
        sheet.AddModifier(new StatModifier("test", "Str", 0, 50));   // 10 * 1.5 = 15

        Assert.Equal(15, sheet.Current("Str"));
        Assert.Equal(8, sheet.GetBase("Str")); // base untouched

        sheet.AddModifier(new StatModifier("big", "Str", 1000, 0));
        Assert.Equal(99, sheet.Current("Str")); // clamped to max

        sheet.RemoveModifiersBySource("test");
        sheet.RemoveModifiersBySource("big");
        Assert.Equal(8, sheet.Current("Str"));
    }

    [Fact]
    public void DerivedStat_EvaluatesThroughModifiedDependencies()
    {
        var (session, rpg) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player"));
        var sheet = rpg.GetSheet(player)!;

        Assert.Equal(80, sheet.Current("Carry")); // Str 8 * 10

        sheet.AddModifier(new StatModifier("test", "Str", 2, 0));
        Assert.Equal(100, sheet.Current("Carry")); // derived sees modified Str
    }

    [Fact]
    public void FormulasSeeCurrentValues_ThroughEntityResolver()
    {
        var (session, rpg) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player"));

        rpg.GetSheet(player)!.AddModifier(new StatModifier("test", "Str", 1, 0));

        Assert.Equal(22, session.Formulas.Evaluate("Str * 2 + 4", player));
    }

    [Fact]
    public void SetBase_ClampsAndPublishesStatChanged()
    {
        var (session, rpg) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player"));
        var sheet = rpg.GetSheet(player)!;
        var changes = new List<(double Old, double New)>();
        session.Events.Subscribe("Stat.Changed", e =>
        {
            if ((string?)e.Payload["stat"] == "HP" && (string?)e.Payload["instanceId"] == player.InstanceId)
            {
                changes.Add(((double)e.Payload["old"]!, (double)e.Payload["new"]!));
            }
        });

        sheet.SetBase("HP", 999); // clamps to max 30
        Assert.Equal(30, sheet.Current("HP"));

        sheet.SetBase("HP", 12);
        Assert.Equal(12, sheet.Current("HP"));
        session.Events.DispatchPending();

        Assert.Equal([(30.0, 12.0)], changes); // the clamped no-op write published nothing
    }

    [Fact]
    public void VitalStatReachingMin_KillsEntity()
    {
        var (session, rpg) = _host.CreateLoadedSession();
        var wolf = session.World.All.Single(e => e.Tags.Contains("wolf"));
        string? diedId = null;
        session.Events.Subscribe("Entity.Died", e => diedId = e.Payload["instanceId"] as string);

        rpg.GetSheet(wolf)!.ModifyBase("HP", -999);
        session.Events.DispatchPending();

        Assert.Equal(wolf.InstanceId, diedId);
        Assert.False(session.World.TryGet(wolf.InstanceId, out _));
    }
}
