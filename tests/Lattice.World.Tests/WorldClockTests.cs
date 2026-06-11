using System.Text.Json;
using Lattice.Core.Persistence;
using Lattice.Rpg.Conditions;

namespace Lattice.World.Tests;

/// <summary>
/// Time, calendar, phases, and persistence (plan/05 §1–2). The standard
/// test clock runs one game hour per 30 simulated seconds, starting 08:00.
/// </summary>
public sealed class WorldClockTests : IDisposable
{
    private readonly WorldTestHost _host = new();

    public WorldClockTests() => _host.WriteStandardContent();

    public void Dispose() => _host.Dispose();

    [Fact]
    public void Clock_AdvancesAndPublishesCalendarEvents()
    {
        var (session, _, world) = _host.CreateLoadedSession();
        int minutes = 0, hours = 0;
        session.Events.Subscribe("Time.MinuteTick", _ => minutes++);
        session.Events.Subscribe("Time.HourStarted", _ => hours++);

        WorldTestHost.TickSeconds(session, 30.0); // one game hour

        Assert.Equal(1, world.Day);
        Assert.InRange(world.Hour, 8.99, 9.01);
        Assert.Equal(1, hours);
        Assert.InRange(minutes, 59, 61);
        Assert.InRange(session.Flags.ReadNumber("Hour"), 8.99, 9.01);
        Assert.Equal(1, session.Flags.ReadNumber("Day"));
    }

    [Fact]
    public void Formulas_ReadTheClock()
    {
        var (session, rpg, _) = _host.CreateLoadedSession();
        var ctx = new ConditionContext { Session = session, Rpg = rpg, Subject = null };
        var after8 = JsonDocument.Parse("""{ "type": "FormulaTrue", "formula": "Hour >= 8" }""").RootElement;
        var after20 = JsonDocument.Parse("""{ "type": "FormulaTrue", "formula": "Hour >= 20" }""").RootElement;

        Assert.True(rpg.Conditions.EvaluateOne(after8, ctx));
        Assert.False(rpg.Conditions.EvaluateOne(after20, ctx));

        WorldTestHost.TickSeconds(session, 12.5 * 30); // 08:00 -> 20:30
        Assert.True(rpg.Conditions.EvaluateOne(after20, ctx));
    }

    [Fact]
    public void Midnight_StartsANewDay()
    {
        var (session, _, world) = _host.CreateLoadedSession();
        var days = 0;
        session.Events.Subscribe("Time.DayStarted", _ => days++);

        WorldTestHost.TickSeconds(session, 16.1 * 30); // 08:00 + 16.1h -> past midnight

        Assert.Equal(2, world.Day);
        Assert.Equal(1, days);
    }

    [Fact]
    public void DayPhases_FlipFlagsAndLight()
    {
        var (session, _, world) = _host.CreateLoadedSession();
        var phaseChanges = new List<string>();
        session.Events.Subscribe("Time.PhaseChanged", e => phaseChanges.Add((string)e.Payload["phase"]!));

        Assert.Equal("day", world.PhaseName);
        Assert.True(session.Flags.ReadBool("is_day"));
        Assert.False(session.Flags.ReadBool("is_night"));
        Assert.Equal(1.0, world.AmbientLight);

        WorldTestHost.TickSeconds(session, 14.1 * 30); // 08:00 -> 22:06

        Assert.Equal("night", world.PhaseName);
        Assert.True(session.Flags.ReadBool("is_night"));
        Assert.False(session.Flags.ReadBool("is_day"));
        Assert.Equal(0.15, world.AmbientLight);
        Assert.Equal(["dusk", "night"], phaseChanges);
    }

    [Fact]
    public void SaveLoad_RestoresClockWeatherAndSeason()
    {
        var (session, _, world) = _host.CreateLoadedSession();
        WorldTestHost.TickSeconds(session, 16.5 * 30); // into day 2 (winter, daysPerSeason 1)
        var json = SaveManager.Capture(session);
        var expected = (world.TotalGameMinutes, world.Day, world.SeasonId, world.WeatherId, world.PhaseName);

        using var fresh = new WorldTestHost();
        fresh.WriteStandardContent();
        var (restoredSession, _, restoredWorld) = fresh.CreateLoadedSession();
        var report = SaveManager.Restore(restoredSession, json);

        Assert.True(report.Ok, string.Join("; ", report.Errors));
        Assert.Equal(expected.TotalGameMinutes, restoredWorld.TotalGameMinutes, 3);
        Assert.Equal(expected.Day, restoredWorld.Day);
        Assert.Equal(expected.SeasonId, restoredWorld.SeasonId);
        Assert.Equal(expected.WeatherId, restoredWorld.WeatherId);
        Assert.Equal(expected.PhaseName, restoredWorld.PhaseName);
        Assert.Equal(session.Flags.ReadNumber("Hour"), restoredSession.Flags.ReadNumber("Hour"), 3);
    }

    [Fact]
    public void Clock_IsDeterministicAcrossRuns()
    {
        string Run()
        {
            using var host = new WorldTestHost(seed: 7);
            host.WriteStandardContent();
            var (session, _, world) = host.CreateLoadedSession();
            var weatherLog = new List<string>();
            session.Events.Subscribe("Weather.Changed", e => weatherLog.Add((string)e.Payload["weather"]!));
            WorldTestHost.TickSeconds(session, 24 * 30); // a full game day
            return $"{world.TotalGameMinutes:F3}|{world.WeatherId}|{string.Join(",", weatherLog)}";
        }

        Assert.Equal(Run(), Run());
    }
}
