using Lattice.Core.Content;
using Lattice.Core.Events;
using Lattice.Core.Formulas;
using Lattice.Core.Hosting;

namespace Lattice.Core.Simulation;

/// <summary>A simulation system ticked by the session in registration order (deterministic system order, plan/01 §3).</summary>
public interface ISimSystem
{
    string Name { get; }

    void Tick(GameSession session, float dt);
}

/// <summary>
/// The composition root of a running game: content registry, event bus,
/// global blackboard, deterministic RNG, formula engine, world, and the
/// ordered system list — booted from a JSON lifecycle def and advanced by
/// the host calling <see cref="AdvanceTick"/>.
/// </summary>
public sealed class GameSession
{
    private readonly List<ISimSystem> _systems = [];
    private readonly List<IContentValidator> _contentValidators = [];
    private readonly List<Persistence.ISaveSection> _saveSections = [];
    private readonly ContentLoader _loader;
    private HotReloadManager? _hotReload;

    private GameSession(HostServices services, DefTypeRegistry defTypes)
    {
        Services = services;
        DefTypes = defTypes;
        _loader = new ContentLoader(defTypes);
        Events = new EventBus(services.Host.Logger);
        Flags = new Blackboard(() => SimTimeSeconds);
        Rng = new LatticeRandom(services.Host.RandomSeed);
        Formulas = new NCalcFormulaEngine(Rng);
        Defs = new DefRegistry();
        World = new World(Defs, Events);
    }

    public HostServices Services { get; }

    public DefTypeRegistry DefTypes { get; }

    public DefRegistry Defs { get; }

    public EventBus Events { get; }

    /// <summary>Global blackboard: world flags, counters, every durable choice (plan/03 §1 owns the full pattern).</summary>
    public Blackboard Flags { get; }

    public LatticeRandom Rng { get; }

    public IFormulaEngine Formulas { get; }

    public World World { get; }

    public long Tick { get; internal set; }

    public double SimTimeSeconds { get; internal set; }

    public IReadOnlyList<ISimSystem> Systems => _systems;

    /// <summary>Module-registered save sections, captured/restored alongside the core world delta.</summary>
    public IReadOnlyList<Persistence.ISaveSection> SaveSections => _saveSections;

    /// <summary>Raised after <see cref="LoadContent"/> completes, so modules can index loaded defs.</summary>
    public event Action<ContentLoadReport>? ContentLoaded;

    public static GameSession Create(HostServices services, DefTypeRegistry? defTypes = null)
        => new(services, defTypes ?? DefTypeRegistry.CreateDefault());

    public void RegisterContentValidator(IContentValidator validator) => _contentValidators.Add(validator);

    public void RegisterSaveSection(Persistence.ISaveSection section) => _saveSections.Add(section);

    /// <summary>Load (or reload from scratch) all content and run the link + validation passes.</summary>
    public ContentLoadReport LoadContent()
    {
        var report = _loader.LoadAll(Services.Content, Defs);
        Defs.Validate(report, Formulas);
        foreach (var validator in _contentValidators)
        {
            validator.Validate(Defs, report, Formulas);
        }

        ContentLoaded?.Invoke(report);
        return report;
    }

    /// <summary>Apply a lifecycle def: initial flags and spawns. Returns false if the def is missing.</summary>
    public bool Boot(string lifecycleId)
    {
        if (!Defs.TryGet<LifecycleDef>(lifecycleId, out var lifecycle))
        {
            Services.Host.Logger.Warning($"Lifecycle def '{lifecycleId}' not found; nothing booted.");
            return false;
        }

        if (lifecycle.InitialScene is not null)
        {
            Flags.Write("current_scene", lifecycle.InitialScene);
        }

        foreach (var flag in lifecycle.GlobalFlags ?? [])
        {
            if (JsonValueHelper.TryToPlain(flag.Value, out var value))
            {
                Flags.Write(flag.Key, value);
            }
            else
            {
                Services.Host.Logger.Warning($"Lifecycle flag '{flag.Key}' has a non-scalar value; skipped.");
            }
        }

        foreach (var spawn in lifecycle.Spawns ?? [])
        {
            for (var i = 0; i < spawn.Count; i++)
            {
                var pos = spawn.Position is { Length: 3 } p
                    ? new System.Numerics.Vector3(p[0], p[1], p[2])
                    : default;
                World.Spawn(spawn.Entity, pos);
            }
        }

        Events.Publish("Game.Booted", EventPayload.Of(("lifecycle", lifecycleId)));
        Services.Host.Logger.Info($"Booted lifecycle '{lifecycleId}': {World.Count} entit(ies), {Flags.Count} flag(s).");
        return true;
    }

    public void RegisterSystem(ISimSystem system) => _systems.Add(system);

    /// <summary>Enable hot reload: content changes are applied at the next tick boundary.</summary>
    public void EnableHotReload(double debounceSeconds = 0.25)
    {
        _hotReload ??= new HotReloadManager(
            Services.Content, _loader, Defs, Events, Services.Host.Logger,
            () => Services.Host.WallClockSeconds, debounceSeconds);
    }

    /// <summary>
    /// Advance one fixed tick: pump hot reload, run systems in registration
    /// order, then drain the event queue (handlers run at a fixed point, never
    /// mid-system).
    /// </summary>
    public void AdvanceTick(float dt)
    {
        Tick++;
        SimTimeSeconds += dt;

        _hotReload?.Pump(Formulas);

        foreach (var system in _systems)
        {
            system.Tick(this, dt);
        }

        Events.DispatchPending();
    }
}
