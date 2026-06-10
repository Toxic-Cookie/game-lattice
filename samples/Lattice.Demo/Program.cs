using System.Globalization;
using System.Numerics;
using Lattice.Core.Content;
using Lattice.Core.Formulas;
using Lattice.Core.Hosting;
using Lattice.Core.Hosting.Standalone;
using Lattice.Core.Persistence;
using Lattice.Core.Simulation;
using Lattice.Rpg;
using Lattice.Rpg.Defs;

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

var session = GameSession.Create(services, LatticeRpg.CreateDefTypes());
var rpg = LatticeRpg.Attach(session);
var loadReport = session.LoadContent();
foreach (var error in loadReport.Errors)
{
    logger.Error(error);
}

logger.Info($"Content loaded: {loadReport.DefsLoaded} def(s), {loadReport.Errors.Count} error(s).");
session.EnableHotReload();
session.Boot(DefaultLifecycle);
session.Events.DispatchPending();

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
    catch (Exception ex) when (ex is FormulaException or KeyNotFoundException or InvalidCastException or InvalidOperationException)
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
                World:
                  defs | content | entities | flags | events | time
                  tick [n]                     advance n ticks
                  spawn <defId> [x y z]        spawn from template
                  despawn <id>                 remove entity
                  inspect <id>                 stats (base->current), tags, statuses, inventory
                  eval <formula> [entityId]    evaluate a formula
                  set <flag> <value>           write a global flag
                  save [file] / load [file]    world delta persistence
                RPG:
                  give <id> <itemId> [n]       add items to an entity
                  equip <id> <itemId>          equip from bag
                  unequip <id> <slotId>        unequip to bag
                  use <userId> <itemId> [targetId]
                  loot <tableId> <looterId>    roll a loot table into an inventory
                  shop <shopId> <customerId>   list stock with personalized prices
                  buy <shopId> <customerId> <itemId>
                  sell <shopId> <customerId> <itemId>
                  quit
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
        {
            if (parts.Length < 2 || !session.World.TryGet(parts[1], out var target))
            {
                Console.WriteLine("not found");
                return true;
            }

            Console.WriteLine($"{target.InstanceId} (def {target.DefId}, name {target.Name ?? "-"})");
            Console.WriteLine($"  tags: {(target.Tags.Count > 0 ? string.Join(", ", target.Tags) : "-")}");
            var sheet = rpg.GetSheet(target);
            foreach (var stat in target.Stats.OrderBy(s => s.Key, StringComparer.Ordinal))
            {
                var current = sheet?.Current(stat.Key) ?? stat.Value;
                var suffix = current.Equals(stat.Value) ? "" : $"  (base {stat.Value.ToString(CultureInfo.InvariantCulture)})";
                Console.WriteLine($"  {stat.Key} = {current.ToString(CultureInfo.InvariantCulture)}{suffix}");
            }

            foreach (var status in rpg.GetStatusEffects(target)?.Active ?? [])
            {
                Console.WriteLine($"  status: {status.Def.Id} x{status.Stacks} ({status.Remaining:F1}s left)");
            }

            var inventory = rpg.GetInventory(target);
            if (inventory is not null)
            {
                foreach (var pair in inventory.Bag.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    Console.WriteLine($"  bag: {pair.Key} x{pair.Value}");
                }

                foreach (var pair in inventory.Equipped.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    Console.WriteLine($"  equipped: {pair.Key} = {pair.Value}");
                }
            }

            return true;
        }

        case "eval":
        {
            if (parts.Length < 2)
            {
                Console.WriteLine("usage: eval <formula> [entityId]");
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
        }

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

            return true;

        case "events":
            foreach (var evt in session.Events.Trace)
            {
                var payload = string.Join(", ", evt.Payload.Select(p => $"{p.Key}={p.Value}"));
                Console.WriteLine($"  [{evt.Tick}] {evt.Topic} {{{payload}}}");
            }

            return true;

        case "give":
        {
            if (parts.Length < 3 || !session.World.TryGet(parts[1], out var entity))
            {
                Console.WriteLine("usage: give <entityId> <itemId> [n]");
                return true;
            }

            var amount = parts.Length > 3 ? int.Parse(parts[3], CultureInfo.InvariantCulture) : 1;
            rpg.GiveItem(entity, parts[2], amount);
            session.Events.DispatchPending();
            Console.WriteLine($"gave {amount}x {parts[2]}");
            return true;
        }

        case "equip":
        {
            if (parts.Length < 3 || !session.World.TryGet(parts[1], out var entity))
            {
                Console.WriteLine("usage: equip <entityId> <itemId>");
                return true;
            }

            Console.WriteLine(rpg.Inventory.TryEquip(entity, parts[2], out var error) ? "equipped" : $"error: {error}");
            session.Events.DispatchPending();
            return true;
        }

        case "unequip":
        {
            if (parts.Length < 3 || !session.World.TryGet(parts[1], out var entity))
            {
                Console.WriteLine("usage: unequip <entityId> <slotId>");
                return true;
            }

            Console.WriteLine(rpg.Inventory.TryUnequip(entity, parts[2], out var error) ? "unequipped" : $"error: {error}");
            session.Events.DispatchPending();
            return true;
        }

        case "use":
        {
            if (parts.Length < 3 || !session.World.TryGet(parts[1], out var user))
            {
                Console.WriteLine("usage: use <userId> <itemId> [targetId]");
                return true;
            }

            Entity? target = null;
            if (parts.Length > 3)
            {
                session.World.TryGet(parts[3], out target!);
            }

            Console.WriteLine(rpg.Inventory.TryUse(user, parts[2], target, out var error) ? "used" : $"error: {error}");
            session.Events.DispatchPending();
            return true;
        }

        case "loot":
        {
            if (parts.Length < 3 || !session.World.TryGet(parts[2], out var looter))
            {
                Console.WriteLine("usage: loot <tableId> <looterId>");
                return true;
            }

            var drops = rpg.Loot.Roll(parts[1], looter);
            foreach (var (itemId, amount) in drops)
            {
                rpg.GiveItem(looter, itemId, amount);
                Console.WriteLine($"  {itemId} x{amount}");
            }

            session.Events.DispatchPending();
            Console.WriteLine($"{drops.Count} drop(s).");
            return true;
        }

        case "shop":
        {
            if (parts.Length < 3
                || !session.Defs.TryGet<ShopDef>(parts[1], out var shop)
                || !session.World.TryGet(parts[2], out var customer))
            {
                Console.WriteLine("usage: shop <shopId> <customerId>");
                return true;
            }

            var state = rpg.Trade.GetState(shop);
            foreach (var pair in state.Stock.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var item = session.Defs.Get<ItemDef>(pair.Key);
                Console.WriteLine($"  {pair.Key} x{pair.Value}  buy {rpg.Trade.GetBuyPrice(shop, item, customer)} / sell {rpg.Trade.GetSellPrice(shop, item, customer)}");
            }

            return true;
        }

        case "buy" or "sell":
        {
            if (parts.Length < 4
                || !session.Defs.TryGet<ShopDef>(parts[1], out var shop)
                || !session.World.TryGet(parts[2], out var customer))
            {
                Console.WriteLine($"usage: {parts[0]} <shopId> <customerId> <itemId>");
                return true;
            }

            var ok = parts[0] == "buy"
                ? rpg.Trade.TryBuy(shop, customer, parts[3], out var error)
                : rpg.Trade.TrySell(shop, customer, parts[3], out error);
            Console.WriteLine(ok ? "done" : $"error: {error}");
            session.Events.DispatchPending();
            return true;
        }

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
