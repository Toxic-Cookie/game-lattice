using System.Numerics;

namespace Lattice.Core.Hosting;

/// <summary>
/// Behavioral context passed with every navigation query. Context-dependent
/// traversal costs (HZD pattern: tall grass is impassable on patrol, passable
/// when investigating) key off these fields; see plan/05 §5.
/// </summary>
public sealed class NavQueryContext
{
    /// <summary>Agent profile ID (resolves to a nav profile in content). Null = default profile.</summary>
    public string? AgentProfileId { get; init; }

    /// <summary>Current behavior state tag (e.g. "patrol", "investigating"). Null = default costs.</summary>
    public string? BehaviorState { get; init; }

    /// <summary>Agent collision radius in world units.</summary>
    public float AgentRadius { get; init; } = 0.5f;

    /// <summary>A shared default context for queries that don't care.</summary>
    public static NavQueryContext Default { get; } = new();
}

/// <summary>
/// Pathfinding and spatial-exclusion seam. The built-in implementation is a
/// grid A* (M5); engine hosts substitute NavMesh-backed implementations.
/// Node reservation is part of the contract because exclusion is the
/// coordination mechanism that produces emergent flanking/spread
/// (research: F.E.A.R. Part 7, emergent-ai-guide ch01 §1.5).
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// Find a path and write its waypoints into <paramref name="waypoints"/>
    /// (cleared first). Returns false if no path exists.
    /// </summary>
    bool TryFindPath(Vector3 from, Vector3 to, NavQueryContext context, IList<Vector3> waypoints);

    /// <summary>Cheap reachability check without materializing waypoints.</summary>
    bool IsReachable(Vector3 from, Vector3 to, NavQueryContext context);

    /// <summary>
    /// Attempt to reserve a position for an agent. Fails if another agent
    /// holds it. Re-reserving a node you already hold succeeds.
    /// </summary>
    bool TryReserveNode(Vector3 node, string agentId);

    /// <summary>Release a reservation. No-op if <paramref name="agentId"/> doesn't hold it.</summary>
    void ReleaseNode(Vector3 node, string agentId);
}
