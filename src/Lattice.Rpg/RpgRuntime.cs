using Lattice.Core.Content;
using Lattice.Core.Events;
using Lattice.Core.Formulas;
using Lattice.Core.Hosting;
using Lattice.Core.Simulation;
using Lattice.Rpg.Conditions;
using Lattice.Rpg.Defs;
using Lattice.Rpg.Effects;
using Lattice.Rpg.Items;
using Lattice.Rpg.Loot;
using Lattice.Rpg.Stats;
using Lattice.Rpg.Status;
using Lattice.Rpg.Trade;

namespace Lattice.Rpg;

/// <summary>Fast stat lookups by def ID and by formula key; rebuilt when content (re)loads.</summary>
public sealed class StatCatalog
{
    private readonly Dictionary<string, StatDef> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StatDef> _byKey = new(StringComparer.Ordinal);

    public IEnumerable<StatDef> All => _byId.Values;

    public bool TryGetById(string id, out StatDef def) => _byId.TryGetValue(id, out def!);

    public bool TryGetByKey(string key, out StatDef def) => _byKey.TryGetValue(key, out def!);

    internal void Rebuild(DefRegistry registry)
    {
        _byId.Clear();
        _byKey.Clear();
        foreach (var def in registry.All<StatDef>())
        {
            _byId[def.Id] = def;
            _byKey[def.Key] = def;
        }
    }
}

/// <summary>
/// The RPG module attached to a session (plan/02): stat sheets, status
/// effects, the effect/condition vocabulary, inventory, loot, and trade.
/// Attach <em>before</em> <see cref="GameSession.LoadContent"/> so the
/// module sees content load and entity spawns.
/// </summary>
public sealed class RpgRuntime
{
    private readonly List<IDisposable> _restockSubscriptions = [];

    internal RpgRuntime(GameSession session)
    {
        Session = session;
        Effects = BuiltinEffects.CreateDefault();
        Conditions = ConditionRegistry.CreateDefault();
        Stats = new StatCatalog();
        Inventory = new InventoryManager(this);
        Loot = new LootResolver(this);
        Trade = new TradeService(this);
        Bindings = new Ui.BindingService(this);

        session.RegisterModule(this);
        session.World.EntityAdded += AttachEntity;
        session.ContentLoaded += _ => OnContentChanged();
        session.Events.Subscribe("Content.Reloaded", _ => OnContentChanged());
        session.Events.Subscribe("Entity.Died", OnEntityDied);
        session.RegisterSystem(new StatusEffectSystem(this));
        session.RegisterSaveSection(new RpgSaveSection(this));
        session.RegisterContentValidator(new RpgContentValidator(Effects, Conditions));
    }

    public GameSession Session { get; }

    public EffectRegistry Effects { get; }

    public ConditionRegistry Conditions { get; }

    public StatCatalog Stats { get; }

    public InventoryManager Inventory { get; }

    public LootResolver Loot { get; }

    public TradeService Trade { get; }

    /// <summary>Path-string UI data binding (plan/06 §6).</summary>
    public Ui.BindingService Bindings { get; }

    public StatSheet? GetSheet(Entity entity) => entity.GetComponent<StatSheet>();

    public StatusEffects? GetStatusEffects(Entity entity) => entity.GetComponent<StatusEffects>();

    public Inventory? GetInventory(Entity entity) => entity.GetComponent<Inventory>();

    public void GiveItem(Entity entity, string itemId, int amount) => Inventory.Give(entity, itemId, amount);

    public bool RemoveItem(Entity entity, string itemId, int amount) => Inventory.Remove(entity, itemId, amount);

    public int CountItem(Entity entity, string itemId) => Inventory.Count(entity, itemId);

    /// <summary>Run an effect list directly (demo/tests/M3 quests).</summary>
    public void RunEffects(IEnumerable<System.Text.Json.JsonElement>? effects, Entity? source, Entity? target)
        => Effects.Run(effects, new EffectContext { Session = Session, Rpg = this, Source = source, Target = target });

    /// <summary>Stat-change fanout: publish the event and run the vital-death check (plan/02 §1).</summary>
    internal void NotifyStatChanged(Entity entity, string key, double oldValue, double newValue, Entity? source, bool vitalCheck = true)
    {
        Session.Events.Publish("Stat.Changed", EventPayload.Of(
            ("instanceId", entity.InstanceId),
            ("stat", key),
            ("old", oldValue),
            ("new", newValue)), Session.Tick);

        if (!vitalCheck || !Stats.TryGetByKey(key, out var def) || !def.Vital)
        {
            return;
        }

        var sheet = GetSheet(entity);
        var minimum = def.Min is not null && sheet is not null
            ? Session.Formulas.Evaluate(def.Min, sheet)
            : 0;
        if (newValue <= minimum && Session.World.TryGet(entity.InstanceId, out _))
        {
            Session.Events.Publish("Entity.Died", EventPayload.Of(
                ("instanceId", entity.InstanceId),
                ("defId", entity.DefId),
                ("killerId", source?.InstanceId)), Session.Tick);
            Session.World.Despawn(entity.InstanceId);
        }
    }

    private void OnContentChanged()
    {
        Stats.Rebuild(Session.Defs);

        foreach (var subscription in _restockSubscriptions)
        {
            subscription.Dispose();
        }

        _restockSubscriptions.Clear();
        foreach (var topic in Session.Defs.All<ShopDef>()
                     .Select(s => s.RestockOn)
                     .Where(t => !string.IsNullOrEmpty(t))
                     .Distinct(StringComparer.Ordinal))
        {
            _restockSubscriptions.Add(Session.Events.Subscribe(topic!, _ => Trade.RestockFor(topic!)));
        }
    }

    private void AttachEntity(Entity entity, bool isRestore)
    {
        var sheet = new StatSheet(entity, this);
        entity.SetComponent(sheet);
        entity.StatResolver = sheet;
        entity.SetComponent(new Inventory());
        entity.SetComponent(new StatusEffects(entity, this));

        if (isRestore)
        {
            return; // saved state (bases already key-keyed; bag/statuses) is rebuilt by the save section
        }

        // template stats are authored with stat def IDs; bases live under formula keys
        foreach (var statId in entity.Stats.Keys.ToList())
        {
            if (Stats.TryGetById(statId, out var def))
            {
                var value = entity.Stats[statId];
                entity.Stats.Remove(statId);
                entity.Stats[def.Key] = value;
            }
        }

        // auto-defaults: stats whose default formula is satisfiable appear automatically
        foreach (var def in Stats.All)
        {
            if (def.IsDerived || def.Default is null || entity.Stats.ContainsKey(def.Key))
            {
                continue;
            }

            try
            {
                var formula = def.Default switch
                {
                    "min" => def.Min ?? "0",
                    "max" => def.Max ?? "0",
                    _ => def.Default,
                };
                entity.Stats[def.Key] = Session.Formulas.Evaluate(formula, sheet);
            }
            catch (FormulaException)
            {
                // dependencies missing on this entity: the stat simply doesn't apply
            }
        }

        if (Session.Defs.TryGet<RpgEntityTemplateDef>(entity.DefId, out var template))
        {
            foreach (var pair in template.Items ?? [])
            {
                Inventory.Give(entity, pair.Key, pair.Value);
            }

            foreach (var itemId in template.Equipment ?? [])
            {
                Inventory.Give(entity, itemId, 1);
                if (!Inventory.TryEquip(entity, itemId, out var error))
                {
                    Session.Services.Host.Logger.Warning(
                        $"Template '{entity.DefId}' equipment '{itemId}' could not be equipped: {error}");
                }
            }
        }
    }

    private void OnEntityDied(GameEvent evt)
    {
        var defId = evt.Payload.TryGetValue("defId", out var d) ? d as string : null;
        if (defId is null
            || !Session.Defs.TryGet<RpgEntityTemplateDef>(defId, out var template)
            || template.LootTable is null)
        {
            return;
        }

        Entity? killer = null;
        if (evt.Payload.TryGetValue("killerId", out var k) && k is string killerId)
        {
            Session.World.TryGet(killerId, out killer!);
        }

        var drops = Loot.Roll(template.LootTable, killer);
        foreach (var (itemId, amount) in drops)
        {
            if (killer is not null)
            {
                Inventory.Give(killer, itemId, amount);
            }
        }

        Session.Events.Publish("Loot.Dropped", EventPayload.Of(
            ("instanceId", evt.Payload.TryGetValue("instanceId", out var id) ? id : null),
            ("killerId", killer?.InstanceId),
            ("items", string.Join(",", drops.Select(x => $"{x.ItemId}:{x.Amount}")))), Session.Tick);
    }
}

/// <summary>Entry points for wiring the RPG module into a session.</summary>
public static class LatticeRpg
{
    /// <summary>Core def kinds plus the RPG vocabulary; "entity" is upgraded to the RPG template.</summary>
    public static DefTypeRegistry CreateDefTypes()
    {
        var types = DefTypeRegistry.CreateDefault();
        types.Register<RpgEntityTemplateDef>("entity", replace: true);
        types.Register<StatDef>("stat");
        types.Register<SlotDef>("slot");
        types.Register<ItemDef>("item");
        types.Register<StatusEffectDef>("status");
        types.Register<LootTableDef>("loot");
        types.Register<ShopDef>("shop");
        return types;
    }

    /// <summary>Attach the RPG module. Call after <see cref="GameSession.Create"/> and before LoadContent.</summary>
    public static RpgRuntime Attach(GameSession session) => new(session);
}
