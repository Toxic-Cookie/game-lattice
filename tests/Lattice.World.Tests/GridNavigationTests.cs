using System.Numerics;
using Lattice.Core.Content;
using Lattice.Core.Formulas;
using Lattice.Core.Hosting;
using Lattice.Core.Hosting.Standalone;
using Lattice.Core.Simulation;
using Lattice.Rpg.Effects;

namespace Lattice.World.Tests;

/// <summary>
/// Grid A* (plan/05 §5): walls, context-dependent costs by behavior state,
/// destination-reservation splitting, off-grid fallback, and the
/// unreachable-spawn validation warning. Grid: 8×8 at origin (0,0,0), a
/// vertical wall at X=4 with a single stealth-grass gap at Z=4.
/// </summary>
public sealed class GridNavigationTests : IDisposable
{
    private readonly WorldTestHost _host = new();

    public GridNavigationTests()
    {
        _host.WriteStandardContent();
        _host.WriteContent("nav.json", """
            [ { "id": "navgrid_test", "type": "navgrid", "origin": [0, 0, 0], "cellSize": 1,
                "legend": { ".": { "cost": 1 },
                            "#": { "walkable": false },
                            "g": { "cost": 1, "tags": ["stealth_vegetation"] } },
                "rows": [
                  "........",
                  "....#...",
                  "....#...",
                  "....#...",
                  "....g...",
                  "....#...",
                  "....#...",
                  "....#..."
                ] },
              { "id": "navprofile_test", "type": "navprofile",
                "agentProfiles": ["profile_sneaky"],
                "costs": { "stealth_vegetation": { "idle": "impassable", "alert": 2.0 } } } ]
            """);
    }

    public void Dispose() => _host.Dispose();

    private static readonly Vector3 West = new(2.5f, 0, 4.5f);
    private static readonly Vector3 East = new(6.5f, 0, 4.5f);

    [Fact]
    public void AStar_RoutesThroughTheOnlyGap()
    {
        var (_, _, _) = _host.CreateLoadedSession();
        var waypoints = new List<Vector3>();

        // row 0 is open: from (2.5, 1.5) to (6.5, 1.5) must go around the wall via Z<1 or the gap
        Assert.True(_host.Navigation.TryFindPath(new Vector3(2.5f, 0, 2.5f), new Vector3(6.5f, 0, 2.5f), NavQueryContext.Default, waypoints));
        Assert.True(waypoints.Count > 2, "a wall detour needs intermediate waypoints");
        // no waypoint may sit inside the wall column (X in [4,5))
        Assert.DoesNotContain(waypoints, w => w.X is >= 4 and < 5 && w.Z is >= 1 and < 4);
    }

    [Fact]
    public void ContextCosts_GateTheGrassGapByBehaviorState()
    {
        var (_, _, _) = _host.CreateLoadedSession();
        var idle = new NavQueryContext { AgentProfileId = "profile_sneaky", BehaviorState = "idle" };
        var alert = new NavQueryContext { AgentProfileId = "profile_sneaky", BehaviorState = "alert" };
        var waypoints = new List<Vector3>();

        // West/East at Z=4.5 sit between wall rows; the grass gap is the short way through.
        // On idle the grass is impassable — the path must dodge up to the open row 0.
        Assert.True(_host.Navigation.TryFindPath(West, East, idle, waypoints));
        Assert.DoesNotContain(waypoints, w => w.X is >= 4 and < 5 && w.Z is >= 4 and < 5);

        // alert wades straight through the gap
        Assert.True(_host.Navigation.TryFindPath(West, East, alert, waypoints));
        Assert.Contains(waypoints, w => w.X is >= 4 and < 5 && w.Z is >= 4 and < 5);
    }

    [Fact]
    public void ReservedDestination_SplitsAgents()
    {
        var (_, _, _) = _host.CreateLoadedSession();
        var node = new Vector3(6.5f, 0, 6.5f);
        Assert.True(_host.Navigation.TryReserveNode(node, "agent_a"));
        Assert.False(_host.Navigation.TryReserveNode(node, "agent_b"));

        var waypointsA = new List<Vector3>();
        var waypointsB = new List<Vector3>();
        var ctxA = new NavQueryContext { AgentId = "agent_a" };
        var ctxB = new NavQueryContext { AgentId = "agent_b" };

        Assert.True(_host.Navigation.TryFindPath(new Vector3(5.5f, 0, 0.5f), node, ctxA, waypointsA));
        Assert.True(_host.Navigation.TryFindPath(new Vector3(7.5f, 0, 0.5f), node, ctxB, waypointsB));

        Assert.Equal(node, waypointsA[^1]); // the holder lands on its node
        Assert.NotEqual(node, waypointsB[^1]); // the other splits to a neighbor
        Assert.True(Vector3.Distance(waypointsB[^1], node) <= 1.6f, "the retarget should be adjacent");

        _host.Navigation.ReleaseNode(node, "agent_a");
        Assert.True(_host.Navigation.TryReserveNode(node, "agent_b"));
    }

    [Fact]
    public void OffGridEndpoints_FallBackToAStraightLine()
    {
        var (_, _, _) = _host.CreateLoadedSession();
        var waypoints = new List<Vector3>();

        Assert.True(_host.Navigation.TryFindPath(new Vector3(-50, 0, -50), new Vector3(-40, 0, -40), NavQueryContext.Default, waypoints));
        Assert.Equal(2, waypoints.Count);
    }

    [Fact]
    public void WalledDestination_IsUnreachable()
    {
        var (_, _, _) = _host.CreateLoadedSession();

        Assert.False(_host.Navigation.IsReachable(new Vector3(0.5f, 0, 0.5f), new Vector3(4.5f, 0, 2.5f), NavQueryContext.Default));
        Assert.True(_host.Navigation.IsReachable(West, East, NavQueryContext.Default));
    }

    [Fact]
    public void SpawnOnAWall_ProducesAValidationWarning()
    {
        _host.WriteContent("lifecycle.json", """
            { "id": "lifecycle_test", "type": "lifecycle", "spawns": [
                { "entity": "entity_walled", "position": [4.5, 0, 2.5] } ] }
            """);
        _host.WriteContent("entities.json", """
            { "id": "entity_walled", "type": "entity", "name": "Stuck" }
            """);

        using var source = new DirectoryContentSource(_host.ContentRoot, watch: false);
        var registry = new DefRegistry();
        var report = new ContentLoader(LatticeWorld.AddDefTypes(Lattice.Rpg.LatticeRpg.CreateDefTypes()))
            .LoadAll(source, registry);
        new WorldContentValidator(BuiltinEffects.CreateDefault())
            .Validate(registry, report, new NCalcFormulaEngine(new LatticeRandom(0)));

        Assert.Contains(report.Warnings, w => w.Contains("entity_walled") && w.Contains("unwalkable"));
    }
}
