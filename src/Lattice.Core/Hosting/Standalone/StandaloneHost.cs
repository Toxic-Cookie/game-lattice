using System.Diagnostics;

namespace Lattice.Core.Hosting.Standalone;

/// <summary>Default <see cref="ILatticeHost"/> for console/headless runs.</summary>
public sealed class StandaloneHost : ILatticeHost
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public StandaloneHost(int randomSeed, ILatticeLogger? logger = null)
    {
        RandomSeed = randomSeed;
        Logger = logger ?? new ConsoleLogger();
    }

    public ILatticeLogger Logger { get; }

    public int RandomSeed { get; }

    public double WallClockSeconds => _clock.Elapsed.TotalSeconds;
}
