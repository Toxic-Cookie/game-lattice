using System.Globalization;
using System.Numerics;
using Lattice.Core.Content;
using Lattice.Core.Formulas;
using Lattice.Core.Hosting;
using Lattice.Core.Hosting.Standalone;
using Lattice.Core.Persistence;
using Lattice.Core.Simulation;

const float TickSeconds = 1f / 30f;
const string DefaultLifecycle = "lifecycle_default";
const string DefaultSaveFile = "save.json";

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

var session = GameSession.Create(services);
var loadReport = session.LoadContent();
foreach (var error in loadReport.Errors)
{
    logger.Error(error);
}

logger.Info($"Content loaded: {loadReport.DefsLoaded} def(s), {loadReport.Errors.Count} error(s).");
session.EnableHotReload();
session.Boot(DefaultLifecycle);

logger.Info("Lattice demo host ready. Type 'help' for commands.");
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

    try
    {
        if (!RunCommand(parts))
        {
            break;
        }
    }
    catch (Exception ex) when (ex is FormulaException or KeyNotFoundException or InvalidCastException)
    {
        Console.WriteLine($"error: {ex.Message}");
    }
}

content.Dispose();
return;

bool RunCommand(string[] parts)
{
    switch (parts[0].ToLowerInvariant())
    {
        case "help":
            Console.WriteLine("""
                Commands:
                  help                       this help
                  content                    list loaded content files
                  defs                       list registered defs
                  tick [n]                   advance the simulation n ticks (default 1)
                  time                       tick count and simulation time
                  spawn <defId> [x y z]      spawn an entity from a template
                  despawn <instanceId>       remove an entity
                  entities                   list live entities
                  inspect <instanceId>       entity details
                  eval <formula> [entityId]  evaluate a formula (entity stats as identifiers)
                  set <flag> <value>         write a global flag (bool/number/string)
                  flags                      list global flags
                  events                     recent event trace
                  save [file] / load [file]  persist / restore the world delta
                  quit                       exit
                """);
            return true;

        case "content":
            foreach (var file in session.Services.Content.EnumerateFiles())
            {
                Console.WriteLine($"  {file.RelativePath}");
            }

            return true;

        case "defs":
            foreach (var def in session.Defs.AllDefs.OrderBy(d => d.Id, StringComparer.Ordinal))
            {
                Console.WriteLine($"  {def.Id}  ({def.GetType().Name}, {def.SourceFile})");
            }

            Console.WriteLine($"{session.Defs.Count} def(s).");
            return true;

        case "tick":
            var count = parts.Length > 1 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 1;
            for (var i = 0; i < count; i++)
            {
                session.AdvanceTick(TickSeconds);
                animation.Advance(TickSeconds);
            }

            Console.WriteLine($"tick {session.Tick}, t={session.SimTimeSeconds:F2}s");
            return true;

        case "time":
            Console.WriteLine($"tick {session.Tick}, t={session.SimTimeSeconds:F2}s, wall={session.Services.Host.WallClockSeconds:F1}s");
            return true;

        case "spawn":
            if (parts.Length < 2)
            {
                Console.WriteLine("usage: spawn <defId> [x y z]");
                return true;
            }

            var pos = parts.Length >= 5
                ? new Vector3(
                    float.Parse(parts[2], CultureInfo.InvariantCulture),
                    float.Parse(parts[3], CultureInfo.InvariantCulture),
                    float.Parse(parts[4], CultureInfo.InvariantCulture))
                : default;
            var spawned = session.World.Spawn(parts[1], pos);
            session.Events.DispatchPending();
            Console.WriteLine($"spawned {spawned.InstanceId} ({spawned.Name ?? spawned.DefId})");
            return true;

        case "despawn":
            Console.WriteLine(parts.Length > 1 && session.World.Despawn(parts[1]) ? "despawned" : "not found");
            session.Events.DispatchPending();
            return true;

        case "entities":
            foreach (var entity in session.World.All.OrderBy(e => e.InstanceId, StringComparer.Ordinal))
            {
                Console.WriteLine($"  {entity.InstanceId}  {entity.Name ?? entity.DefId}  pos=({entity.Position.X:F1}, {entity.Position.Y:F1}, {entity.Position.Z:F1})");
            }

            Console.WriteLine($"{session.World.Count} entit(ies).");
            return true;

        case "inspect":
            if (parts.Length < 2 || !session.World.TryGet(parts[1], out var target))
            {
                Console.WriteLine("not found");
                return true;
            }

            Console.WriteLine($"{target.InstanceId} (def {target.DefId}, name {target.Name ?? "-"})");
            Console.WriteLine($"  tags: {(target.Tags.Count > 0 ? string.Join(", ", target.Tags) : "-")}");
            foreach (var stat in target.Stats.OrderBy(s => s.Key, StringComparer.Ordinal))
            {
                Console.WriteLine($"  {stat.Key} = {stat.Value.ToString(CultureInfo.InvariantCulture)}");
            }

            return true;

        case "eval":
            if (parts.Length < 2)
            {
                Console.WriteLine("usage: eval <formula> [entityId]  (quote-free; last token may be an entity id)");
                return true;
            }

            IFormulaContext? ctx = null;
            var formulaParts = parts.Skip(1).ToArray();
            if (formulaParts.Length > 1 && session.World.TryGet(formulaParts[^1], out var evalEntity))
            {
                ctx = evalEntity;
                formulaParts = formulaParts[..^1];
            }

            var formula = string.Join(" ", formulaParts);
            Console.WriteLine(session.Formulas.Evaluate(formula, ctx).ToString(CultureInfo.InvariantCulture));
            return true;

        case "set":
            if (parts.Length < 3)
            {
                Console.WriteLine("usage: set <flag> <value>");
                return true;
            }

            session.Flags.Write(parts[1], JsonValueHelper.ParseLiteral(string.Join(" ", parts.Skip(2))));
            return true;

        case "flags":
            foreach (var key in session.Flags.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                Console.WriteLine($"  {key} = {session.Flags.Read(key)}");
            }

            Console.WriteLine($"{session.Flags.Count} flag(s).");
            return true;

        case "events":
            foreach (var evt in session.Events.Trace)
            {
                var payload = string.Join(", ", evt.Payload.Select(p => $"{p.Key}={p.Value}"));
                Console.WriteLine($"  [{evt.Tick}] {evt.Topic} {{{payload}}}");
            }

            return true;

        case "save":
            var savePath = parts.Length > 1 ? parts[1] : DefaultSaveFile;
            File.WriteAllText(savePath, SaveManager.Capture(session));
            Console.WriteLine($"saved -> {Path.GetFullPath(savePath)}");
            return true;

        case "load":
            var loadPath = parts.Length > 1 ? parts[1] : DefaultSaveFile;
            if (!File.Exists(loadPath))
            {
                Console.WriteLine($"no save file at {Path.GetFullPath(loadPath)}");
                return true;
            }

            var report = SaveManager.Restore(session, File.ReadAllText(loadPath));
            foreach (var error in report.Errors)
            {
                Console.WriteLine($"error: {error}");
            }

            Console.WriteLine(report.Ok
                ? $"loaded tick {session.Tick}: {session.World.Count} entit(ies), {session.Flags.Count} flag(s)"
                : "load completed with errors");
            return true;

        case "quit" or "exit":
            return false;

        default:
            Console.WriteLine($"unknown command '{parts[0]}' — try 'help'");
            return true;
    }
}

static string? FindContentRoot()
{
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
