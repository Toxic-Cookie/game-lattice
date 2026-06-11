using System.Numerics;
using System.Text.Json;
using Lattice.Core.Content;
using Lattice.Core.Formulas;
using Lattice.Rpg.Effects;
using Lattice.World.Defs;

namespace Lattice.World;

/// <summary>
/// World-simulation validation (plan/05): calendar/phase sanity, weather
/// graph integrity, season redirect type-safety, grid shape, nav-profile
/// cost values, and unreachable-spawn warnings.
/// </summary>
public sealed class WorldContentValidator(EffectRegistry effects) : IContentValidator
{
    public void Validate(DefRegistry registry, ContentLoadReport report, IFormulaEngine formulas)
    {
        foreach (var time in registry.All<TimeDef>())
        {
            if (time.MinutesPerGameDay <= 0)
            {
                report.Errors.Add($"Time '{time.Id}' minutesPerGameDay must be positive.");
            }

            if (time.DaysPerSeason < 1)
            {
                report.Errors.Add($"Time '{time.Id}' daysPerSeason must be at least 1.");
            }

            if (time.StartHour is < 0 or >= 24)
            {
                report.Errors.Add($"Time '{time.Id}' startHour must be in [0, 24).");
            }

            foreach (var seasonId in time.Seasons)
            {
                if (registry.Contains(seasonId) && !registry.TryGet<SeasonDef>(seasonId, out _))
                {
                    report.Errors.Add($"Time '{time.Id}' season '{seasonId}' is not a season def.");
                }
            }
        }

        foreach (var phases in registry.All<DayPhaseDef>())
        {
            if (phases.Phases.Count == 0)
            {
                report.Errors.Add($"Day phases '{phases.Id}' declares no phases.");
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var phase in phases.Phases)
            {
                if (phase.Name.Length == 0)
                {
                    report.Errors.Add($"Day phases '{phases.Id}' has an unnamed phase.");
                }
                else if (!seen.Add(phase.Name))
                {
                    report.Errors.Add($"Day phases '{phases.Id}' declares phase '{phase.Name}' more than once.");
                }

                if (phase.FromHour is < 0 or > 24 || phase.ToHour is < 0 or > 24)
                {
                    report.Errors.Add($"Day phases '{phases.Id}' phase '{phase.Name}' hours must be in [0, 24].");
                }

                if (phase.Light is < 0 or > 1)
                {
                    report.Errors.Add($"Day phases '{phases.Id}' phase '{phase.Name}' light must be 0–1.");
                }
            }
        }

        foreach (var weather in registry.All<WeatherStateDef>())
        {
            if (weather.MinHours <= 0 || weather.MaxHours < weather.MinHours)
            {
                report.Errors.Add($"Weather '{weather.Id}' needs 0 < minHours <= maxHours.");
            }

            foreach (var pair in weather.Transitions)
            {
                if (pair.Value <= 0)
                {
                    report.Errors.Add($"Weather '{weather.Id}' transition to '{pair.Key}' has non-positive weight.");
                }

                if (!registry.TryGet<WeatherStateDef>(pair.Key, out _))
                {
                    report.Errors.Add($"Weather '{weather.Id}' transitions to unknown weather '{pair.Key}'.");
                }
            }

            foreach (var pair in weather.Flags ?? new Dictionary<string, JsonElement>())
            {
                if (!JsonValueHelper.TryToPlain(pair.Value, out _))
                {
                    report.Errors.Add($"Weather '{weather.Id}' flag '{pair.Key}' must be a bool, number, or string.");
                }
            }

            foreach (var batch in (weather.OnEnter ?? []).Concat(weather.OnExit ?? []))
            {
                if (batch.Tag.Length == 0)
                {
                    report.Errors.Add($"Weather '{weather.Id}' has a tagged-effects batch without a 'tag'.");
                }

                effects.ValidateList(batch.Effects, $"{weather.Id}.effects", registry, formulas, report);
            }
        }

        foreach (var season in registry.All<SeasonDef>())
        {
            foreach (var pair in season.Redirects ?? new Dictionary<string, string>())
            {
                if (registry.TryGet<Def>(pair.Key, out var fromDef)
                    && registry.TryGet<Def>(pair.Value, out var toDef)
                    && fromDef.GetType() != toDef.GetType())
                {
                    report.Errors.Add(
                        $"Season '{season.Id}' redirects '{pair.Key}' ({fromDef.GetType().Name}) to '{pair.Value}' ({toDef.GetType().Name}) — kinds must match.");
                }
            }

            foreach (var pair in season.WeatherBias ?? new Dictionary<string, double>())
            {
                if (pair.Value < 0)
                {
                    report.Errors.Add($"Season '{season.Id}' weather bias for '{pair.Key}' is negative.");
                }

                if (registry.Contains(pair.Key) && !registry.TryGet<WeatherStateDef>(pair.Key, out _))
                {
                    report.Errors.Add($"Season '{season.Id}' weather bias target '{pair.Key}' is not a weather def.");
                }
            }
        }

        foreach (var grid in registry.All<NavGridDef>())
        {
            ValidateGrid(grid, registry, report);
        }

        foreach (var profile in registry.All<NavProfileDef>())
        {
            foreach (var byState in profile.Costs.Values)
            {
                foreach (var value in byState.Values)
                {
                    var ok = value.ValueKind == JsonValueKind.Number && value.GetDouble() > 0
                             || (value.ValueKind == JsonValueKind.String && value.GetString() == "impassable");
                    if (!ok)
                    {
                        report.Errors.Add($"Nav profile '{profile.Id}' cost values must be positive numbers or \"impassable\".");
                    }
                }
            }
        }
    }

    private static void ValidateGrid(NavGridDef grid, DefRegistry registry, ContentLoadReport report)
    {
        if (grid.Origin.Length != 3)
        {
            report.Errors.Add($"Nav grid '{grid.Id}' origin must be [x, y, z].");
            return;
        }

        if (grid.Rows.Count == 0)
        {
            report.Errors.Add($"Nav grid '{grid.Id}' has no rows.");
            return;
        }

        if (grid.Rows.Any(row => row.Length != grid.Rows[0].Length))
        {
            report.Errors.Add($"Nav grid '{grid.Id}' rows must all have the same length.");
        }

        if (grid.CellSize <= 0)
        {
            report.Errors.Add($"Nav grid '{grid.Id}' cellSize must be positive.");
        }

        foreach (var pair in grid.Legend ?? new Dictionary<string, NavGridDef.Cell>())
        {
            if (pair.Key.Length != 1)
            {
                report.Errors.Add($"Nav grid '{grid.Id}' legend key '{pair.Key}' must be a single character.");
            }

            if (pair.Value.Cost <= 0)
            {
                report.Errors.Add($"Nav grid '{grid.Id}' legend '{pair.Key}' cost must be positive.");
            }
        }

        // unreachable-spawn warnings (plan/05 §5 checklist)
        var origin = new Vector3(grid.Origin[0], grid.Origin[1], grid.Origin[2]);
        var cellSize = Math.Max(0.01, grid.CellSize);
        foreach (var lifecycle in registry.All<LifecycleDef>())
        {
            foreach (var spawn in lifecycle.Spawns ?? [])
            {
                if (spawn.Position is not { Length: 3 } p)
                {
                    continue;
                }

                var x = (int)Math.Floor((p[0] - origin.X) / cellSize);
                var z = (int)Math.Floor((p[2] - origin.Z) / cellSize);
                if (x < 0 || z < 0 || z >= grid.Rows.Count || x >= grid.Rows[0].Length)
                {
                    continue; // off-grid spawns are the host's business
                }

                var symbol = grid.Rows[z][x].ToString();
                var walkable = grid.Legend is not null && grid.Legend.TryGetValue(symbol, out var cell)
                    ? cell.Walkable
                    : symbol != "#";
                if (!walkable)
                {
                    report.Warnings.Add(
                        $"Lifecycle '{lifecycle.Id}' spawns '{spawn.Entity}' at [{p[0]}, {p[1]}, {p[2]}], an unwalkable cell of nav grid '{grid.Id}'.");
                }
            }
        }
    }
}
