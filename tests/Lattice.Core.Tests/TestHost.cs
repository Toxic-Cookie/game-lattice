using Lattice.Core.Hosting;
using Lattice.Core.Hosting.Standalone;
using Lattice.Core.Simulation;

namespace Lattice.Core.Tests;

/// <summary>Silent logger for tests.</summary>
internal sealed class NullLogger : ILatticeLogger
{
    public static readonly NullLogger Instance = new();

    public void Log(LogLevel level, string message)
    {
    }
}

/// <summary>
/// Builds a <see cref="GameSession"/> over a throwaway temp content
/// directory. Dispose deletes the directory.
/// </summary>
internal sealed class TestHost : IDisposable
{
    public TestHost(int seed = 1, bool watch = false)
    {
        ContentRoot = Path.Combine(Path.GetTempPath(), "lattice-m1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ContentRoot);
        Content = new DirectoryContentSource(ContentRoot, watch);
        Services = new HostServices
        {
            Host = new StandaloneHost(seed, NullLogger.Instance),
            Content = Content,
            Navigation = new StraightLineNavigationService(),
            Animation = new TimedStubAnimationService(),
            Physics = new PermissivePhysicsQueryService(),
        };
    }

    public string ContentRoot { get; }

    public DirectoryContentSource Content { get; }

    public HostServices Services { get; }

    public void WriteContent(string relativePath, string json)
    {
        var path = Path.Combine(ContentRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    public GameSession CreateSession() => GameSession.Create(Services);

    public void Dispose()
    {
        Content.Dispose();
        try
        {
            Directory.Delete(ContentRoot, recursive: true);
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
    }
}
