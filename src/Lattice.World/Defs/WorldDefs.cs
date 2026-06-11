using System.Text.Json;
using Lattice.Core.Content;

namespace Lattice.World.Defs;

/// <summary>
/// The world clock's shape (plan/05 §1): real-time scale, calendar, start.
/// One per world (the lowest-sorted ID wins if several load).
/// </summary>
public sealed class TimeDef : Def
{
    /// <summary>Real minutes per full game day (e.g. 20 = a day every 20 wall-clock minutes at 1× tick rate).</summary>
    public double MinutesPerGameDay { get; set; } = 20;

    public int DaysPerSeason { get; set; } = 7;

    /// <summary>Season def IDs in calendar order.</summary>
    public List<string> Seasons { get; set; } = [];

    /// <summary>Starting hour of day 1 (0–24).</summary>
    public double StartHour { get; set; } = 8;

    public override IEnumerable<DefReference> GetReferences()
    {
        foreach (var season in Seasons)
        {
            yield return new DefReference(season, $"{Id}.seasons");
        }
    }
}

/// <summary>
/// Day-phase trigger layer (plan/05 §2): named phases over hour ranges.
/// The active phase sets global flags (<c>is_night</c>, ...) and publishes
/// Time.PhaseChanged; the light curve is data hosts may sample for visuals.
/// </summary>
public sealed class DayPhaseDef : Def
{
    public List<Phase> Phases { get; set; } = [];

    public sealed class Phase
    {
        public string Name { get; set; } = "";

        /// <summary>Start hour, inclusive (ranges may wrap midnight: from 22 to 5).</summary>
        public double FromHour { get; set; }

        /// <summary>End hour, exclusive.</summary>
        public double ToHour { get; set; }

        /// <summary>Ambient light 0–1 while this phase is active (hosts may interpolate).</summary>
        public double Light { get; set; } = 1.0;
    }
}

/// <summary>
/// One weather state (plan/05 §3): Markov transitions with weights, a
/// duration range, global flags held while active (formulas and sensors
/// read them — <c>sense_auditory_mult</c> is how rain degrades hearing),
/// and tag-scoped effect primitives at the boundaries.
/// </summary>
public sealed class WeatherStateDef : Def
{
    /// <summary>Target weather ID → weight. Missing/empty = stays here.</summary>
    public Dictionary<string, double> Transitions { get; set; } = new(StringComparer.Ordinal);

    public double MinHours { get; set; } = 2;

    public double MaxHours { get; set; } = 6;

    /// <summary>Global blackboard entries held while active and removed on exit (bool/number/string).</summary>
    public Dictionary<string, JsonElement>? Flags { get; set; }

    /// <summary>Effect primitives run when this weather begins, per entity tag.</summary>
    public List<TaggedEffects>? OnEnter { get; set; }

    /// <summary>Effect primitives run when this weather ends, per entity tag.</summary>
    public List<TaggedEffects>? OnExit { get; set; }

    public sealed class TaggedEffects
    {
        /// <summary>Entities carrying this tag receive the effects (source = target = the entity).</summary>
        public string Tag { get; set; } = "";

        public List<JsonElement> Effects { get; set; } = [];
    }
}

/// <summary>
/// A season (plan/05 §4): a content layer activated by the calendar. Def
/// redirects are the v1 overlay mechanism (winter resolves loot_forest to
/// loot_forest_winter); weather bias multiplies transition weights;
/// prefabHints are opaque data for hosts.
/// </summary>
public sealed class SeasonDef : Def
{
    /// <summary>Def ID → replacement def ID while this season is active.</summary>
    public Dictionary<string, string>? Redirects { get; set; }

    /// <summary>Weather state ID → weight multiplier applied to Markov transitions.</summary>
    public Dictionary<string, double>? WeatherBias { get; set; }

    /// <summary>Engine-side content-swap hints (opaque to the simulation).</summary>
    public Dictionary<string, string>? PrefabHints { get; set; }

    public override IEnumerable<DefReference> GetReferences()
    {
        foreach (var pair in Redirects ?? new Dictionary<string, string>())
        {
            yield return new DefReference(pair.Key, $"{Id}.redirects");
            yield return new DefReference(pair.Value, $"{Id}.redirects");
        }
    }
}

/// <summary>
/// A walkable grid declared as rows of legend characters (plan/05 §5).
/// The lowest-sorted navgrid ID is the active grid.
/// </summary>
public sealed class NavGridDef : Def
{
    /// <summary>World position of the cell (0,0) corner.</summary>
    public float[] Origin { get; set; } = [0, 0, 0];

    public double CellSize { get; set; } = 1.0;

    /// <summary>Row strings, north to south; row index = Z, column = X.</summary>
    public List<string> Rows { get; set; } = [];

    /// <summary>Character → cell descriptor. '.' defaults to walkable cost 1; '#' to blocked.</summary>
    public Dictionary<string, Cell>? Legend { get; set; }

    public sealed class Cell
    {
        public bool Walkable { get; set; } = true;

        public double Cost { get; set; } = 1.0;

        public List<string>? Tags { get; set; }
    }
}

/// <summary>
/// Context-dependent traversal costs (HZD Part 9, scoped down): per cell
/// tag, per behavior state, a cost multiplier or "impassable". Binds to
/// agent profiles by ID — same map, different paths by behavior state.
/// </summary>
public sealed class NavProfileDef : Def
{
    /// <summary>Agent profile IDs using these costs.</summary>
    public List<string> AgentProfiles { get; set; } = [];

    /// <summary>cell tag → (behavior state or "default") → cost multiplier or "impassable".</summary>
    public Dictionary<string, Dictionary<string, JsonElement>> Costs { get; set; } = new(StringComparer.Ordinal);
}
