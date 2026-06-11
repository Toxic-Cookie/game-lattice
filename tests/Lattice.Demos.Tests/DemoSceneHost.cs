using Lattice.Ai;
using Lattice.Core.Hosting;
using Lattice.Core.Hosting.Standalone;
using Lattice.Core.Simulation;
using Lattice.Narrative;
using Lattice.Rpg;
using Lattice.World;
using Lattice.World.Navigation;

namespace Lattice.Demos.Tests;

internal sealed class NullLogger : ILatticeLogger
{
    public static readonly NullLogger Instance = new();

    public void Log(LogLevel level, string message)
    {
    }
}

/// <summary>
/// Boots the *shipped* repository content (<c>content/</c>) — the demos are
/// the integration test suite, so unlike the per-module test hosts nothing
/// here is synthesized. Each scene is a lifecycle def (plan/07).
/// </summary>
internal sealed class DemoSceneHost : IDisposable
{
    private const float TickStep = 1f / 30f;

    public DemoSceneHost(int seed = 12345)
    {
        ContentRoot = FindRepoContentRoot();
        Content = new DirectoryContentSource(ContentRoot);
        Animation = new TimedStubAnimationService(animationDurationSeconds: 0.4);
        Services = new HostServices
        {
            Host = new StandaloneHost(seed, NullLogger.Instance),
            Content = Content,
            Navigation = new GridNavigationService(),
            Animation = Animation,
            Physics = new PermissivePhysicsQueryService(),
        };
    }

    public string ContentRoot { get; }

    public DirectoryContentSource Content { get; }

    public TimedStubAnimationService Animation { get; }

    public HostServices Services { get; }

    public GameSession Session { get; private set; } = null!;

    public RpgRuntime Rpg { get; private set; } = null!;

    public NarrativeRuntime Narrative { get; private set; } = null!;

    public AiRuntime Ai { get; private set; } = null!;

    public WorldRuntime World { get; private set; } = null!;

    /// <summary>Attach all modules, load the shipped content, and boot one demo scene lifecycle.</summary>
    public GameSession Boot(string lifecycleId)
    {
        Session = GameSession.Create(Services, LatticeWorld.AddDefTypes(LatticeAi.CreateDefTypes()));
        Rpg = LatticeRpg.Attach(Session);
        Narrative = LatticeNarrative.Attach(Session, Rpg);
        Ai = LatticeAi.Attach(Session, Rpg, Narrative);
        World = LatticeWorld.Attach(Session, Rpg);

        var report = Session.LoadContent();
        Assert.True(report.Ok, string.Join("; ", report.Errors));
        Session.Boot(lifecycleId);
        Session.Events.DispatchPending();
        return Session;
    }

    /// <summary>The single spawned instance of a template (demo scenes spawn most casts once).</summary>
    public Entity Single(string defId)
        => Session.World.All.Single(e => e.DefId == defId);

    public IReadOnlyList<Entity> AllOf(string defId)
        => Session.World.All.Where(e => e.DefId == defId).OrderBy(e => e.InstanceId, StringComparer.Ordinal).ToList();

    /// <summary>Advance simulation + the animation stub together.</summary>
    public void TickSeconds(double seconds)
    {
        var ticks = (int)Math.Round(seconds / TickStep);
        for (var i = 0; i < ticks; i++)
        {
            Session.AdvanceTick(TickStep);
            Animation.Advance(TickStep);
        }
    }

    /// <summary>
    /// Advance by in-game hours. The shipped clock (time_default) runs a game
    /// day in 12 real minutes, so one game hour is 30 real seconds.
    /// </summary>
    public void TickGameHours(double hours) => TickSeconds(hours * 30.0);

    /// <summary>Walk up from the test binaries to the repository's content directory.</summary>
    public static string FindRepoContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "content");
            if (File.Exists(Path.Combine(candidate, "lifecycle.json")))
            {
                return candidate;
            }

            dir = dir.Parent!;
        }

        throw new DirectoryNotFoundException("repository content/ directory not found above " + AppContext.BaseDirectory);
    }

    /// <summary>Repository root (for golden files).</summary>
    public static string RepoRoot => Path.GetDirectoryName(FindRepoContentRoot())!;

    public void Dispose() => Content.Dispose();
}
