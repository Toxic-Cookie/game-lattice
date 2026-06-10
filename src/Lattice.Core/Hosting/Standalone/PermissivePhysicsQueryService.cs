using System.Numerics;

namespace Lattice.Core.Hosting.Standalone;

/// <summary>
/// Geometry-free physics stub: line of sight always holds, and radius
/// queries run over an entity-position registry the simulation maintains.
/// Replaced per-engine by raycast-backed implementations.
/// </summary>
public sealed class PermissivePhysicsQueryService : IPhysicsQueryService
{
    private readonly Dictionary<string, Vector3> _positions = new(StringComparer.Ordinal);

    /// <summary>Register or move an entity in the spatial registry.</summary>
    public void SetEntityPosition(string entityId, Vector3 position) => _positions[entityId] = position;

    /// <summary>Remove an entity from the spatial registry.</summary>
    public void RemoveEntity(string entityId) => _positions.Remove(entityId);

    public bool HasLineOfSight(Vector3 from, Vector3 to) => true;

    public void QueryEntityIdsInRadius(Vector3 center, float radius, ICollection<string> results)
    {
        var radiusSquared = radius * radius;
        foreach (var pair in _positions)
        {
            if (Vector3.DistanceSquared(center, pair.Value) <= radiusSquared)
            {
                results.Add(pair.Key);
            }
        }
    }
}
