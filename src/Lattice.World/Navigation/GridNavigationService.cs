using System.Numerics;
using System.Text.Json;
using Lattice.Core.Hosting;
using Lattice.Core.Simulation;
using Lattice.World.Defs;

namespace Lattice.World.Navigation;

/// <summary>
/// The v1 navigation backing (plan/05 §5): deterministic A* over a
/// content-declared grid, with context-dependent costs (cell tags ×
/// behavior state via nav profiles) and node reservation. Construct it in
/// the host, hand it to <see cref="HostServices"/>, and let
/// <see cref="WorldRuntime"/> bind it to the session; without a grid def it
/// degrades to straight-line paths, so hosts can wire it unconditionally.
/// </summary>
public sealed class GridNavigationService : INavigationService
{
    private const int MaxExpansions = 16384;

    private readonly struct CellInfo(bool walkable, double cost, string[] tags)
    {
        public bool Walkable { get; } = walkable;

        public double Cost { get; } = cost;

        public string[] Tags { get; } = tags;
    }

    private GameSession? _session;
    private NavGridDef? _grid;
    private CellInfo[]? _cells; // row-major: z * width + x
    private int _width;
    private int _height;
    private Vector3 _origin;
    private float _cellSize = 1;
    private readonly Dictionary<string, NavProfileDef> _navProfileByAgentProfile = new(StringComparer.Ordinal);
    private readonly Dictionary<(int X, int Z), string> _reservations = [];

    /// <summary>Called by the world module at attach; loads grids after content loads/reloads.</summary>
    public void Bind(GameSession session)
    {
        _session = session;
        session.ContentLoaded += _ => Rebuild();
        session.Events.Subscribe("Content.Reloaded", _ => Rebuild());
    }

    /// <summary>The active grid def, if any (debug views).</summary>
    public NavGridDef? Grid => _grid;

    private void Rebuild()
    {
        _grid = _session?.Defs.All<NavGridDef>().OrderBy(g => g.Id, StringComparer.Ordinal).FirstOrDefault();
        _navProfileByAgentProfile.Clear();
        foreach (var profile in _session?.Defs.All<NavProfileDef>() ?? [])
        {
            foreach (var agentProfile in profile.AgentProfiles)
            {
                _navProfileByAgentProfile[agentProfile] = profile;
            }
        }

        if (_grid is null || _grid.Rows.Count == 0 || _grid.Origin.Length != 3)
        {
            _cells = null;
            return;
        }

        _height = _grid.Rows.Count;
        _width = _grid.Rows[0].Length;
        _origin = new Vector3(_grid.Origin[0], _grid.Origin[1], _grid.Origin[2]);
        _cellSize = (float)Math.Max(0.01, _grid.CellSize);
        _cells = new CellInfo[_width * _height];
        for (var z = 0; z < _height; z++)
        {
            var row = _grid.Rows[z];
            for (var x = 0; x < _width; x++)
            {
                var symbol = x < row.Length ? row[x].ToString() : "#";
                CellInfo info;
                if (_grid.Legend is not null && _grid.Legend.TryGetValue(symbol, out var cell))
                {
                    info = new CellInfo(cell.Walkable, Math.Max(0.01, cell.Cost), cell.Tags?.ToArray() ?? []);
                }
                else
                {
                    info = symbol == "#" ? new CellInfo(false, 1, []) : new CellInfo(true, 1, []);
                }

                _cells[z * _width + x] = info;
            }
        }
    }

    // ── INavigationService ───────────────────────────────────────────────

    public bool TryFindPath(Vector3 from, Vector3 to, NavQueryContext context, IList<Vector3> waypoints)
    {
        waypoints.Clear();
        if (_cells is null || !TryCell(from, out var start) || !TryCell(to, out var goal))
        {
            // no grid (or off it): straight line keeps gridless hosts working
            waypoints.Add(from);
            waypoints.Add(to);
            return true;
        }

        var profile = ResolveNavProfile(context);
        if (!IsPassable(start, context, profile, treatReservedAsBlocked: false))
        {
            return false; // standing somewhere illegal: nothing sane to do
        }

        var agentId = context.AgentId;
        if (CellCost(goal, context, profile) is null)
        {
            return false; // physically or contextually impassable destination
        }

        // a destination reserved by someone else retargets to the nearest
        // free neighbor — two agents sent to one node split (exclusion)
        if (_reservations.TryGetValue(goal, out var holder) && holder != agentId)
        {
            if (FindNearestFree(goal, context, profile, agentId) is not { } retargeted)
            {
                return false;
            }

            goal = retargeted;
            to = CellCenter(goal);
        }

        var path = AStar(start, goal, context, profile, agentId);
        if (path is null)
        {
            return false;
        }

        waypoints.Add(from);
        foreach (var cell in path.Skip(1).Take(path.Count - 2))
        {
            waypoints.Add(CellCenter(cell));
        }

        if (path.Count > 1)
        {
            waypoints.Add(to);
        }

        return true;
    }

    public bool IsReachable(Vector3 from, Vector3 to, NavQueryContext context)
    {
        var waypoints = new List<Vector3>();
        return TryFindPath(from, to, context, waypoints);
    }

    public bool TryReserveNode(Vector3 node, string agentId)
    {
        var key = QuantizedKey(node);
        if (_reservations.TryGetValue(key, out var holder))
        {
            return holder == agentId;
        }

        _reservations[key] = agentId;
        return true;
    }

    public void ReleaseNode(Vector3 node, string agentId)
    {
        var key = QuantizedKey(node);
        if (_reservations.TryGetValue(key, out var holder) && holder == agentId)
        {
            _reservations.Remove(key);
        }
    }

    // ── grid internals ───────────────────────────────────────────────────

    private (int X, int Z) QuantizedKey(Vector3 position)
        => TryCell(position, out var cell)
            ? cell
            : ((int)Math.Floor(position.X * 2), (int)Math.Floor(position.Z * 2)); // off-grid: half-unit buckets

    private bool TryCell(Vector3 position, out (int X, int Z) cell)
    {
        var x = (int)Math.Floor((position.X - _origin.X) / _cellSize);
        var z = (int)Math.Floor((position.Z - _origin.Z) / _cellSize);
        cell = (x, z);
        return _cells is not null && x >= 0 && x < _width && z >= 0 && z < _height;
    }

    private Vector3 CellCenter((int X, int Z) cell)
        => new(
            _origin.X + (cell.X + 0.5f) * _cellSize,
            _origin.Y,
            _origin.Z + (cell.Z + 0.5f) * _cellSize);

    private NavProfileDef? ResolveNavProfile(NavQueryContext context)
        => context.AgentProfileId is { } id && _navProfileByAgentProfile.TryGetValue(id, out var profile)
            ? profile
            : null;

    /// <summary>Effective traversal cost for a cell under a context, or null when impassable.</summary>
    private double? CellCost((int X, int Z) cell, NavQueryContext context, NavProfileDef? profile)
    {
        var info = _cells![cell.Z * _width + cell.X];
        if (!info.Walkable)
        {
            return null;
        }

        var cost = info.Cost;
        if (profile is not null)
        {
            foreach (var tag in info.Tags)
            {
                if (!profile.Costs.TryGetValue(tag, out var byState))
                {
                    continue;
                }

                JsonElement value = default;
                var found = (context.BehaviorState is { } state && byState.TryGetValue(state, out value))
                            || byState.TryGetValue("default", out value);
                if (!found)
                {
                    continue;
                }

                if (value.ValueKind == JsonValueKind.String && value.GetString() == "impassable")
                {
                    return null;
                }

                if (value.ValueKind == JsonValueKind.Number)
                {
                    cost *= Math.Max(0.01, value.GetDouble());
                }
            }
        }

        return cost;
    }

    private bool IsPassable((int X, int Z) cell, NavQueryContext context, NavProfileDef? profile, bool treatReservedAsBlocked, string? selfId = null)
    {
        if (CellCost(cell, context, profile) is null)
        {
            return false;
        }

        return !treatReservedAsBlocked
               || !_reservations.TryGetValue(cell, out var holder)
               || holder == selfId;
    }

    private (int X, int Z)? FindNearestFree((int X, int Z) around, NavQueryContext context, NavProfileDef? profile, string? selfId)
    {
        for (var radius = 1; radius <= 3; radius++)
        {
            for (var dz = -radius; dz <= radius; dz++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius)
                    {
                        continue; // ring only, deterministic scan order
                    }

                    var candidate = (X: around.X + dx, Z: around.Z + dz);
                    if (candidate.X >= 0 && candidate.X < _width && candidate.Z >= 0 && candidate.Z < _height
                        && IsPassable(candidate, context, profile, treatReservedAsBlocked: true, selfId))
                    {
                        return candidate;
                    }
                }
            }
        }

        return null;
    }

    private List<(int X, int Z)>? AStar(
        (int X, int Z) start, (int X, int Z) goal, NavQueryContext context, NavProfileDef? profile, string? selfId)
    {
        if (start == goal)
        {
            return [start];
        }

        var open = new List<(int X, int Z)> { start };
        var cameFrom = new Dictionary<(int X, int Z), (int X, int Z)>();
        var g = new Dictionary<(int X, int Z), double> { [start] = 0 };
        var expansions = 0;

        double H((int X, int Z) c) => Math.Abs(c.X - goal.X) + Math.Abs(c.Z - goal.Z);

        while (open.Count > 0 && expansions++ < MaxExpansions)
        {
            var bestIndex = 0;
            var bestF = g[open[0]] + H(open[0]);
            for (var i = 1; i < open.Count; i++)
            {
                var f = g[open[i]] + H(open[i]);
                if (f < bestF)
                {
                    bestF = f;
                    bestIndex = i;
                }
            }

            var current = open[bestIndex];
            open.RemoveAt(bestIndex);
            if (current == goal)
            {
                var path = new List<(int X, int Z)> { current };
                while (cameFrom.TryGetValue(current, out var previous))
                {
                    current = previous;
                    path.Add(current);
                }

                path.Reverse();
                return path;
            }

            Span<(int X, int Z)> neighbors =
            [
                (current.X + 1, current.Z), (current.X - 1, current.Z),
                (current.X, current.Z + 1), (current.X, current.Z - 1),
            ];
            foreach (var next in neighbors)
            {
                if (next.X < 0 || next.X >= _width || next.Z < 0 || next.Z >= _height)
                {
                    continue;
                }

                if (CellCost(next, context, profile) is not { } stepCost)
                {
                    continue;
                }

                // cells reserved by someone else are obstacles (exclusion, ch01 §1.5)
                if (_reservations.TryGetValue(next, out var holder) && holder != selfId)
                {
                    continue;
                }

                var tentative = g[current] + stepCost;
                if (!g.TryGetValue(next, out var known) || tentative < known)
                {
                    g[next] = tentative;
                    cameFrom[next] = current;
                    if (!open.Contains(next))
                    {
                        open.Add(next);
                    }
                }
            }
        }

        return null;
    }
}
