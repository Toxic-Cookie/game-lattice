using System.Globalization;
using Lattice.Core.Hosting;
using Lattice.Core.Hosting.Standalone;

const float TickSeconds = 1f / 30f;

var contentRoot = args.Length > 0 ? args[0] : FindContentRoot();
var logger = new ConsoleLogger(LogLevel.Debug);
var host = new StandaloneHost(randomSeed: 12345, logger);

IContentSource content;
if (contentRoot is not null && Directory.Exists(contentRoot))
{
    content = new DirectoryContentSource(contentRoot);
    logger.Info($"Content root: {Path.GetFullPath(contentRoot)}");
}
else
{
    content = new EmptyContentSource();
    logger.Warning("No content directory found; running with empty content. Pass a path as the first argument.");
}

var animation = new TimedStubAnimationService();
var services = new HostServices
{
    Host = host,
    Content = content,
    Navigation = new StraightLineNavigationService(),
    Animation = animation,
    Physics = new PermissivePhysicsQueryService(),
};

content.Changed += change => logger.Info($"content {change.Kind}: {change.File.RelativePath}");

var tick = 0L;
var simTime = 0.0;

logger.Info($"Lattice demo host ready (seed {services.Host.RandomSeed}). Type 'help' for commands.");
while (true)
{
    Console.Write("lattice> ");
    var line = Console.ReadLine();
    if (line is null)
    {
        break;
    }

    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (parts.Length == 0)
    {
        continue;
    }

    switch (parts[0].ToLowerInvariant())
    {
        case "help":
            Console.WriteLine("""
                Commands:
                  help          show this help
                  content       list loaded content files
                  tick [n]      advance the simulation n ticks (default 1)
                  time          show tick count and simulation time
                  quit          exit
                (Game systems arrive with M1+; this is the M0 host shell.)
                """);
            break;

        case "content":
            var files = content.EnumerateFiles().ToList();
            foreach (var file in files)
            {
                Console.WriteLine($"  {file.RelativePath}");
            }

            Console.WriteLine($"{files.Count} file(s).");
            break;

        case "tick":
            var count = parts.Length > 1 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 1;
            for (var i = 0; i < count; i++)
            {
                tick++;
                simTime += TickSeconds;
                animation.Advance(TickSeconds);
                // M1: GameSession.Tick(TickSeconds) drives registered systems here.
            }

            Console.WriteLine($"advanced {count} tick(s) -> tick {tick}, t={simTime:F2}s");
            break;

        case "time":
            Console.WriteLine($"tick {tick}, t={simTime:F2}s, wall={services.Host.WallClockSeconds:F1}s");
            break;

        case "quit" or "exit":
            content.Dispose();
            return;

        default:
            Console.WriteLine($"unknown command '{parts[0]}' — try 'help'");
            break;
    }
}

content.Dispose();

static string? FindContentRoot()
{
    // Walk up from the working directory looking for a 'content' folder
    // (works for `dotnet run` from the repo root or the project directory).
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        var candidate = Path.Combine(dir.FullName, "content");
        if (Directory.Exists(candidate))
        {
            return candidate;
        }

        dir = dir.Parent;
    }

    return null;
}

/// <summary>Content source used when no content directory exists yet.</summary>
internal sealed class EmptyContentSource : IContentSource
{
    public event Action<ContentChange>? Changed
    {
        add { }
        remove { }
    }

    public IEnumerable<ContentFile> EnumerateFiles(string searchPattern = "*.json") => [];

    public string ReadAllText(ContentFile file) => throw new FileNotFoundException(file.RelativePath);

    public void Dispose()
    {
    }
}
