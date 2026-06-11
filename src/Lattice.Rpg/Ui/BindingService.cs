using Lattice.Core.Formulas;
using Lattice.Core.Simulation;

namespace Lattice.Rpg.Ui;

/// <summary>
/// Engine-agnostic UI data binding (plan/06 §6): a widget needs only a path
/// string. Backed by the existing change events (Stat.Changed,
/// Item.Acquired/Removed, blackboard subscriptions) — never polling.
///
/// Path grammar:
/// <code>
///   flags.&lt;key&gt;                       a global blackboard flag
///   &lt;target&gt;.stats.&lt;stat&gt;[.max]      a stat's current (or max) value
///   &lt;target&gt;.inventory.&lt;itemId&gt;      an item count
/// </code>
/// where <c>&lt;target&gt;</c> is <c>Player</c> (first entity tagged "player")
/// or an entity instance ID, and <c>&lt;stat&gt;</c> is a stat def ID or key.
/// </summary>
public sealed class BindingService
{
    private readonly RpgRuntime _rpg;
    private readonly List<Binding> _bindings = [];

    internal BindingService(RpgRuntime rpg)
    {
        _rpg = rpg;
        rpg.Session.Events.Subscribe("Stat.Changed", e => Notify("stats", e.Payload));
        rpg.Session.Events.Subscribe("Item.Acquired", e => Notify("inventory", e.Payload));
        rpg.Session.Events.Subscribe("Item.Removed", e => Notify("inventory", e.Payload));
    }

    /// <summary>Read a path's current value (null when the path doesn't resolve).</summary>
    public object? Resolve(string path)
    {
        var parts = path.Split('.');
        if (parts.Length == 2 && parts[0] == "flags")
        {
            return _rpg.Session.Flags.Read(parts[1]);
        }

        if (parts.Length is < 3 or > 4 || ResolveTarget(parts[0]) is not { } entity)
        {
            return null;
        }

        switch (parts[1])
        {
            case "stats":
            {
                var key = StatKey(parts[2]);
                if (parts.Length == 4 && parts[3] == "max")
                {
                    return _rpg.Stats.TryGetByKey(key, out var def) && def.Max is { Length: > 0 }
                        ? _rpg.Session.Formulas.Evaluate(
                            def.Max, new CompositeFormulaContext(entity, new BlackboardFormulaContext(_rpg.Session.Flags)))
                        : null;
                }

                var sheet = _rpg.GetSheet(entity);
                return sheet?.HasStat(key) == true ? sheet.Current(key) : null;
            }

            case "inventory":
                return (double)_rpg.CountItem(entity, parts[2]);

            default:
                return null;
        }
    }

    /// <summary>
    /// Watch a path: the callback fires immediately with the current value,
    /// then again whenever the underlying change event lands. Dispose to stop.
    /// </summary>
    public IDisposable Subscribe(string path, Action<object?> callback)
    {
        var parts = path.Split('.');
        if (parts.Length == 2 && parts[0] == "flags")
        {
            callback(_rpg.Session.Flags.Read(parts[1]));
            return _rpg.Session.Flags.Subscribe(parts[1], (_, value) => callback(value));
        }

        var binding = new Binding
        {
            Path = path,
            Domain = parts.Length >= 3 ? parts[1] : "",
            Target = parts.Length >= 3 ? parts[0] : "",
            Key = parts.Length >= 3 ? parts[2] : "",
            Callback = callback,
        };
        _bindings.Add(binding);
        callback(Resolve(path));
        return new Subscription(_bindings, binding);
    }

    private void Notify(string domain, IReadOnlyDictionary<string, object?> payload)
    {
        if (_bindings.Count == 0
            || !payload.TryGetValue("instanceId", out var idValue) || idValue is not string instanceId)
        {
            return;
        }

        var changedKey = payload.TryGetValue(domain == "stats" ? "stat" : "item", out var k) ? k as string : null;
        foreach (var binding in _bindings.ToArray())
        {
            if (binding.Domain != domain)
            {
                continue;
            }

            var matchesTarget = binding.Target == instanceId
                                || (binding.Target == "Player" && ResolveTarget("Player")?.InstanceId == instanceId);
            var matchesKey = domain == "stats"
                ? StatKey(binding.Key) == changedKey
                : binding.Key == changedKey;
            if (matchesTarget && matchesKey)
            {
                binding.Callback(Resolve(binding.Path));
            }
        }
    }

    private Entity? ResolveTarget(string target)
        => target == "Player"
            ? _rpg.Session.World.All.FirstOrDefault(e => e.Tags.Contains("player"))
            : _rpg.Session.World.TryGet(target, out var entity)
                ? entity
                : null;

    private string StatKey(string statRef)
        => _rpg.Stats.TryGetById(statRef, out var def) ? def.Key : statRef;

    private sealed class Binding
    {
        public required string Path { get; init; }

        public required string Target { get; init; }

        public required string Domain { get; init; }

        public required string Key { get; init; }

        public required Action<object?> Callback { get; init; }
    }

    private sealed class Subscription(List<Binding> bindings, Binding binding) : IDisposable
    {
        public void Dispose() => bindings.Remove(binding);
    }
}
