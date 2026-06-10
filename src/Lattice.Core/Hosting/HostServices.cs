namespace Lattice.Core.Hosting;

/// <summary>
/// The complete bundle of host-provided services a simulation needs.
/// Implementing these five seams is the entire cost of porting Lattice to a
/// new engine (see plan/00 §3.3); <c>samples/Lattice.Godot</c> is the proof.
/// </summary>
public sealed class HostServices
{
    public required ILatticeHost Host { get; init; }

    public required IContentSource Content { get; init; }

    public required INavigationService Navigation { get; init; }

    public required IAnimationService Animation { get; init; }

    public required IPhysicsQueryService Physics { get; init; }
}
