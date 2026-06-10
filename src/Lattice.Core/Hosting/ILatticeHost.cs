namespace Lattice.Core.Hosting;

/// <summary>
/// The root host seam: ambient services the embedding application (console,
/// Unity, Godot, ...) provides to the framework. The simulation itself is
/// advanced by the host calling into the framework's tick; this interface
/// only exposes what the framework must *ask* the host for.
/// </summary>
public interface ILatticeHost
{
    /// <summary>Host-provided log sink.</summary>
    ILatticeLogger Logger { get; }

    /// <summary>
    /// Seed for the simulation's deterministic RNG. Same seed + same content
    /// + same tick count must reproduce the same world state.
    /// </summary>
    int RandomSeed { get; }

    /// <summary>
    /// Monotonic real-time seconds since host startup. For diagnostics and
    /// content-watcher debouncing only — simulation time is driven by ticks,
    /// never by the wall clock.
    /// </summary>
    double WallClockSeconds { get; }
}
