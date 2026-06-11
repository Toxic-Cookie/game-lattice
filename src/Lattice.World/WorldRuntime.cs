using System.Text.Json;
using Lattice.Core.Content;
using Lattice.Core.Events;
using Lattice.Core.Persistence;
using Lattice.Core.Simulation;
using Lattice.Rpg;
using Lattice.World.Defs;
using Lattice.World.Navigation;

namespace Lattice.World;

/// <summary>
/// The world-simulation module (plan/05): clock and calendar, day phases,
/// Markov weather, season overlays, and the grid navigation backing. It is
/// deliberately a *publisher*: everything downstream (shops, quests, AI
/// schedules, sensors) consumes bus events and global flags, never this
/// module directly.
///
/// Flags written: <c>Hour</c> (fractional), <c>Day</c>, <c>Season</c>
/// (index), <c>season</c> (name), <c>day_phase</c>, <c>is_&lt;phase&gt;</c>,
/// <c>weather</c>, plus whatever the active weather state declares.
/// </summary>
public sealed class WorldRuntime
{
    private TimeDef? _time;
    private DayPhaseDef? _phases;
    private double _totalGameMinutes;
    private string? _phaseName;
    private int _seasonIndex = -1;
    private string? _weatherId;
    private double _weatherUntilMinutes;

    internal WorldRuntime(GameSession session, RpgRuntime rpg)
    {
        Session = session;
        Rpg = rpg;

        session.RegisterModule(this);
        session.ContentLoaded += _ => OnContentLoaded();
        session.Events.Subscribe("Content.Reloaded", _ => OnContentLoaded());
        session.RegisterSystem(new WorldSystem(this));
        session.RegisterSaveSection(new WorldSaveSection(this));
        session.RegisterContentValidator(new WorldContentValidator(rpg.Effects));

        if (session.Services.Navigation is GridNavigationService grid)
        {
            grid.Bind(session);
        }
    }

    public GameSession Session { get; }

    public RpgRuntime Rpg { get; }

    // ── clock state ──────────────────────────────────────────────────────

    /// <summary>Game minutes elapsed since day 1, 00:00 (the single persisted clock value).</summary>
    public double TotalGameMinutes => _totalGameMinutes;

    /// <summary>1-based day number.</summary>
    public int Day => (int)(_totalGameMinutes / MinutesPerDay) + 1;

    /// <summary>Fractional hour of day, 0–24.</summary>
    public double Hour => _totalGameMinutes % MinutesPerDay / 60.0;

    public string? SeasonId => _time is { Seasons.Count: > 0 }
        ? _time.Seasons[SeasonIndex]
        : null;

    public int SeasonIndex => _time is { Seasons.Count: > 0 }
        ? (Day - 1) / Math.Max(1, _time.DaysPerSeason) % _time.Seasons.Count
        : 0;

    public string? PhaseName => _phaseName;

    public string? WeatherId => _weatherId;

    /// <summary>Ambient light 0–1 from the active phase (hosts may drive visuals from this).</summary>
    public double AmbientLight => ActivePhase()?.Light ?? 1.0;

    private const double MinutesPerDay = 24 * 60;

    private double GameMinutesPerSecond
        => _time is { MinutesPerGameDay: > 0 } ? MinutesPerDay / (_time.MinutesPerGameDay * 60.0) : 0;

    private void OnContentLoaded()
    {
        _time = Session.Defs.All<TimeDef>().OrderBy(t => t.Id, StringComparer.Ordinal).FirstOrDefault();
        _phases = Session.Defs.All<DayPhaseDef>().OrderBy(p => p.Id, StringComparer.Ordinal).FirstOrDefault();
        if (_time is not null && _totalGameMinutes == 0)
        {
            _totalGameMinutes = Math.Clamp(_time.StartHour, 0, 24) * 60;
        }

        RefreshDerivedState(publishEvents: false);
    }

    // ── per-tick advance ─────────────────────────────────────────────────

    internal void Advance(float dt)
    {
        if (_time is null)
        {
            return;
        }

        var before = _totalGameMinutes;
        _totalGameMinutes += GameMinutesPerSecond * dt;

        if ((long)before != (long)_totalGameMinutes)
        {
            Session.Events.Publish("Time.MinuteTick", EventPayload.Of(
                ("minute", (double)((long)_totalGameMinutes % 60)),
                ("hour", Math.Floor(Hour)),
                ("day", (double)Day)), Session.Tick);
        }

        var hourBefore = (long)(before / 60);
        var hourAfter = (long)(_totalGameMinutes / 60);
        if (hourBefore != hourAfter)
        {
            Session.Events.Publish("Time.HourStarted", EventPayload.Of(
                ("hour", Math.Floor(Hour)), ("day", (double)Day)), Session.Tick);
            AdvanceWeather();
        }

        var dayBefore = (long)(before / MinutesPerDay);
        var dayAfter = (long)(_totalGameMinutes / MinutesPerDay);
        if (dayBefore != dayAfter)
        {
            Session.Events.Publish("Time.DayStarted", EventPayload.Of(("day", (double)Day)), Session.Tick);
        }

        RefreshDerivedState(publishEvents: true);
    }

    private void RefreshDerivedState(bool publishEvents)
    {
        if (_time is null)
        {
            return;
        }

        var flags = Session.Flags;
        flags.Write("Hour", Hour);
        flags.Write("Day", (double)Day);
        flags.Write("Season", (double)SeasonIndex);

        // season overlay
        if (SeasonId is { } seasonId && _seasonIndex != SeasonIndex)
        {
            _seasonIndex = SeasonIndex;
            flags.Write("season", seasonId);
            Session.Defs.TryGet<SeasonDef>(seasonId, out var season);
            Session.Defs.SetRedirects(season?.Redirects);
            if (publishEvents)
            {
                Session.Events.Publish("Time.SeasonStarted", EventPayload.Of(
                    ("season", seasonId), ("index", (double)SeasonIndex)), Session.Tick);
            }
        }

        // day phase
        if (ActivePhase() is { } phase && phase.Name != _phaseName)
        {
            var previous = _phaseName;
            _phaseName = phase.Name;
            foreach (var p in _phases!.Phases)
            {
                flags.Write($"is_{p.Name}", p.Name == phase.Name);
            }

            flags.Write("day_phase", phase.Name);
            if (publishEvents && previous is not null)
            {
                Session.Events.Publish("Time.PhaseChanged", EventPayload.Of(("phase", phase.Name)), Session.Tick);
            }
        }

        // first-run weather
        if (_weatherId is null)
        {
            var initial = Session.Defs.All<WeatherStateDef>().OrderBy(w => w.Id, StringComparer.Ordinal).FirstOrDefault();
            if (initial is not null)
            {
                EnterWeather(initial, publishEvents);
            }
        }
    }

    private DayPhaseDef.Phase? ActivePhase()
    {
        var hour = Hour;
        foreach (var phase in _phases?.Phases ?? [])
        {
            var inRange = phase.FromHour <= phase.ToHour
                ? hour >= phase.FromHour && hour < phase.ToHour
                : hour >= phase.FromHour || hour < phase.ToHour; // wraps midnight
            if (inRange)
            {
                return phase;
            }
        }

        return null;
    }

    // ── weather (Markov on the hour) ─────────────────────────────────────

    private void AdvanceWeather()
    {
        if (_weatherId is null
            || _totalGameMinutes < _weatherUntilMinutes
            || !Session.Defs.TryGet<WeatherStateDef>(_weatherId, out var current))
        {
            return;
        }

        var bias = SeasonId is { } seasonId && Session.Defs.TryGet<SeasonDef>(seasonId, out var season)
            ? season.WeatherBias
            : null;
        var candidates = current.Transitions
            .Select(pair => (pair.Key,
                Weight: pair.Value * (bias?.TryGetValue(pair.Key, out var multiplier) == true ? multiplier : 1)))
            .Where(c => c.Weight > 0 && Session.Defs.TryGet<WeatherStateDef>(c.Key, out _))
            .OrderBy(c => c.Key, StringComparer.Ordinal)
            .ToList();
        if (candidates.Count == 0)
        {
            _weatherUntilMinutes = _totalGameMinutes + 60; // nothing to go to; check again next hour
            return;
        }

        var roll = Session.Rng.NextDouble() * candidates.Sum(c => c.Weight);
        var next = candidates[^1].Key;
        foreach (var (key, weight) in candidates)
        {
            roll -= weight;
            if (roll <= 0)
            {
                next = key;
                break;
            }
        }

        if (next != _weatherId && Session.Defs.TryGet<WeatherStateDef>(next, out var target))
        {
            ExitWeather(current);
            EnterWeather(target, publishEvents: true);
        }
        else
        {
            _weatherUntilMinutes = _totalGameMinutes + RollDuration(current);
        }
    }

    private void EnterWeather(WeatherStateDef state, bool publishEvents)
    {
        _weatherId = state.Id;
        _weatherUntilMinutes = _totalGameMinutes + RollDuration(state);
        Session.Flags.Write("weather", state.Id);
        foreach (var pair in state.Flags ?? new Dictionary<string, JsonElement>())
        {
            if (JsonValueHelper.TryToPlain(pair.Value, out var value))
            {
                Session.Flags.Write(pair.Key, value);
            }
        }

        RunBoundaryEffects(state.OnEnter);
        if (publishEvents)
        {
            Session.Events.Publish("Weather.Changed", EventPayload.Of(("weather", state.Id)), Session.Tick);
        }
    }

    private void ExitWeather(WeatherStateDef state)
    {
        foreach (var key in (state.Flags ?? new Dictionary<string, JsonElement>()).Keys)
        {
            Session.Flags.Clear(key);
        }

        RunBoundaryEffects(state.OnExit);
    }

    private void RunBoundaryEffects(List<WeatherStateDef.TaggedEffects>? batches)
    {
        foreach (var batch in batches ?? [])
        {
            foreach (var entity in Session.World.All.Where(e => e.Tags.Contains(batch.Tag)).ToList())
            {
                Rpg.RunEffects(batch.Effects, source: entity, target: entity);
            }
        }
    }

    private double RollDuration(WeatherStateDef state)
    {
        var minMinutes = Math.Max(0.01, state.MinHours) * 60;
        var maxMinutes = Math.Max(state.MinHours, state.MaxHours) * 60;
        return minMinutes + Session.Rng.NextDouble() * (maxMinutes - minMinutes);
    }

    // ── persistence ──────────────────────────────────────────────────────

    private sealed class WorldSaveSection(WorldRuntime world) : ISaveSection
    {
        public string Key => "world";

        public JsonElement Capture(GameSession session)
            => JsonSerializer.SerializeToElement(new Snapshot
            {
                TotalGameMinutes = world._totalGameMinutes,
                Weather = world._weatherId,
                WeatherUntilMinutes = world._weatherUntilMinutes,
            });

        public void Restore(GameSession session, JsonElement data, ContentLoadReport report)
        {
            var snapshot = data.Deserialize<Snapshot>();
            if (snapshot is null)
            {
                return;
            }

            world._totalGameMinutes = snapshot.TotalGameMinutes;
            world._seasonIndex = -1; // force overlay + flag refresh
            world._phaseName = null;
            if (snapshot.Weather is not null && session.Defs.TryGet<WeatherStateDef>(snapshot.Weather, out var weather))
            {
                world.EnterWeather(weather, publishEvents: false);
                world._weatherUntilMinutes = snapshot.WeatherUntilMinutes;
            }

            world.RefreshDerivedState(publishEvents: false);
        }

        private sealed class Snapshot
        {
            public double TotalGameMinutes { get; set; }

            public string? Weather { get; set; }

            public double WeatherUntilMinutes { get; set; }
        }
    }

    private sealed class WorldSystem(WorldRuntime world) : ISimSystem
    {
        public string Name => "world.clock";

        public void Tick(GameSession session, float dt) => world.Advance(dt);
    }
}

/// <summary>Entry points for wiring the world module into a session.</summary>
public static class LatticeWorld
{
    /// <summary>Add the world def kinds to an existing registry (composes with any module chain).</summary>
    public static DefTypeRegistry AddDefTypes(DefTypeRegistry types)
    {
        types.Register<TimeDef>("time");
        types.Register<DayPhaseDef>("dayphases");
        types.Register<WeatherStateDef>("weather");
        types.Register<SeasonDef>("season");
        types.Register<NavGridDef>("navgrid");
        types.Register<NavProfileDef>("navprofile");
        return types;
    }

    /// <summary>Attach the world module. Call after RPG, before LoadContent.</summary>
    public static WorldRuntime Attach(GameSession session, RpgRuntime rpg) => new(session, rpg);
}
