using System.Numerics;

namespace Lattice.Core.Hosting.Standalone;

/// <summary>
/// Obstacle-free navigation stub: every point is reachable in a straight
/// line. Node reservation is fully functional — it is the exclusion
/// mechanism AI coordination depends on, so even the stub honors it.
/// Replaced by grid A* in M5.
/// </summary>
public sealed class StraightLineNavigationService : INavigationService
{
    private readonly Dictionary<Vector3, string> _reservations = new();

    public bool TryFindPath(Vector3 from, Vector3 to, NavQueryContext context, IList<Vector3> waypoints)
    {
        waypoints.Clear();
        waypoints.Add(from);
        waypoints.Add(to);
        return true;
    }

    public bool IsReachable(Vector3 from, Vector3 to, NavQueryContext context) => true;

    public bool TryReserveNode(Vector3 node, string agentId)
    {
        if (_reservations.TryGetValue(node, out var owner))
        {
            return string.Equals(owner, agentId, StringComparison.Ordinal);
        }

        _reservations[node] = agentId;
        return true;
    }

    public void ReleaseNode(Vector3 node, string agentId)
    {
        if (_reservations.TryGetValue(node, out var owner) && string.Equals(owner, agentId, StringComparison.Ordinal))
        {
            _reservations.Remove(node);
        }
    }
}
