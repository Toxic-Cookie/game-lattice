using System.Numerics;

namespace Lattice.Core.Hosting;

/// <summary>
/// Read-only physics queries the AI perception layer depends on: line of
/// sight for visual sensors, radius queries so sensors never scan the whole
/// entity list (research: ch07 anti-pattern 2). Hosts back this with engine
/// raycasts/spatial indices; the standalone implementation is permissive.
/// </summary>
public interface IPhysicsQueryService
{
    /// <summary>Is the straight segment between two points free of opaque geometry?</summary>
    bool HasLineOfSight(Vector3 from, Vector3 to);

    /// <summary>
    /// Collect IDs of perceivable entities within <paramref name="radius"/> of
    /// <paramref name="center"/> into <paramref name="results"/> (not cleared).
    /// </summary>
    void QueryEntityIdsInRadius(Vector3 center, float radius, ICollection<string> results);
}
