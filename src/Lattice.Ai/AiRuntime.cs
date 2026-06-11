using System.Numerics;
using System.Text.Json;
using Lattice.Ai.Agents;
using Lattice.Ai.Defs;
using Lattice.Ai.Goap;
using Lattice.Ai.Groups;
using Lattice.Ai.Perception;
using Lattice.Ai.Tasks;
using Lattice.Ai.Utility;
using Lattice.Core.Content;
using Lattice.Core.Events;
using Lattice.Core.Hosting;
using Lattice.Core.Hosting.Standalone;
using Lattice.Core.Simulation;
using Lattice.Narrative;
using Lattice.Rpg;
using Lattice.Rpg.Conditions;
using Lattice.Rpg.Effects;

namespace Lattice.Ai;

/// <summary>
/// The AI module (plan/04, M4a): per-agent world models, sensors, and the
/// FSM/schedule brain tiers. Attach after RPG (and optionally Narrative),
/// before LoadContent. Agent runtime state is deliberately not saved —
/// brains re-perceive and re-decide after load.
/// </summary>
public sealed class AiRuntime
{
    private readonly Dictionary<string, AgentProfileDef> _profileByEntityDef = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ConditionCatalog> _catalogs = new(StringComparer.Ordinal);
    private readonly List<(StimulusPacket Packet, double ExpiresAt)> _stimuli = [];

    internal AiRuntime(GameSession session, RpgRuntime rpg, NarrativeRuntime? narrative)
    {
        Session = session;
        Rpg = rpg;
        Narrative = narrative;
        Tasks = TaskRegistry.CreateDefault();

        Groups = new GroupManager(this);
        MetaSensors = new MetaSensorTracker(this);

        session.RegisterModule(this);
        rpg.Conditions.Register(new AgentConditionEvaluator(this));
        rpg.Conditions.Register(new AgentMetaCondition(this));
        rpg.Conditions.Register(new BeliefEqualsCondition(this));
        rpg.Conditions.Register(new UtilityAtLeastCondition(this));
        rpg.Conditions.Register(new NeedBelowCondition(this));
        session.World.EntityAdded += AttachAgent;
        session.ContentLoaded += _ => RebuildIndexes();
        session.Events.Subscribe("Content.Reloaded", _ => RebuildIndexes());
        session.Events.Subscribe("Stimulus.Sound", e => OnStimulusEvent(e, StimulusType.Sound));
        session.Events.Subscribe("Stimulus.Scent", e => OnStimulusEvent(e, StimulusType.Scent));
        session.Events.Subscribe("Entity.Damaged", OnEntityDamaged);
        session.Events.Subscribe("Entity.Died", e =>
        {
            if (e.Payload.TryGetValue("instanceId", out var id) && id is string memberId)
            {
                Groups.RemoveMember(memberId);
            }
        });
        session.RegisterSystem(new AgentSystem(this));
        session.RegisterSystem(new GroupSystem(Groups));
        session.RegisterContentValidator(new AiContentValidator(rpg.Conditions, Tasks, rpg.Effects));
    }

    public GameSession Session { get; }

    public RpgRuntime Rpg { get; }

    public NarrativeRuntime? Narrative { get; }

    public TaskRegistry Tasks { get; }

    /// <summary>Group agents, role slots, and the collective (M4d).</summary>
    public GroupManager Groups { get; }

    /// <summary>Meta player-awareness detectors (M4d).</summary>
    public MetaSensorTracker MetaSensors { get; }

    /// <summary>Live transient stimuli (sounds/scents) for sensor evaluation.</summary>
    public IReadOnlyList<StimulusPacket> TransientStimuli { get; private set; } = [];

    public AgentComponent? GetAgent(Entity entity) => entity.GetComponent<AgentComponent>();

    /// <summary>Emit a transient sound stimulus (also reachable by publishing "Stimulus.Sound").</summary>
    public void EmitSound(Vector3 position, double loudness = 1.0, string? sourceId = null, double lifetimeSeconds = 0.6)
        => _stimuli.Add((new StimulusPacket
        {
            Type = StimulusType.Sound,
            Position = position,
            Loudness = loudness,
            SourceId = sourceId,
        }, Session.SimTimeSeconds + lifetimeSeconds));

    /// <summary>Perceivable entities within range of an agent (spatial query first — ch07 anti-pattern 2).</summary>
    public IEnumerable<Entity> QueryEntitiesNear(Entity self, double range)
    {
        var ids = new List<string>();
        Session.Services.Physics.QueryEntityIdsInRadius(self.Position, (float)range, ids);
        foreach (var id in ids)
        {
            if (id != self.InstanceId && Session.World.TryGet(id, out var entity))
            {
                yield return entity;
            }
        }
    }

    /// <summary>Path an agent to a destination through the navigation seam; returns false when unreachable.</summary>
    public bool RequestPath(AgentContext ctx, Vector3 destination, double speed)
    {
        var waypoints = new List<Vector3>();
        var navContext = new NavQueryContext
        {
            AgentProfileId = ctx.Agent.Profile.Id,
            BehaviorState = ctx.Agent.Meta.ToString().ToLowerInvariant(),
        };
        if (!Session.Services.Navigation.TryFindPath(ctx.Entity.Position, destination, navContext, waypoints))
        {
            return false;
        }

        ctx.Agent.SetPath(waypoints, speed);
        return true;
    }

    /// <summary>The utility scoreboard for an agent (ch07: every choice explainable). Empty for non-agents.</summary>
    public IReadOnlyList<ActivityScore> ScoreActivities(Entity entity)
        => GetAgent(entity) is { } agent
            ? UtilityScoring.ScoreActivities(new AgentContext { Ai = this, Entity = entity, Agent = agent })
            : [];

    /// <summary>The GOAP decision dump for an agent (ch07 §7.4), or null for non-GOAP brains.</summary>
    public string? DumpGoap(Entity entity)
        => GetAgent(entity) is { Brain: GoapBrain brain } agent
            ? brain.Dump(new AgentContext { Ai = this, Entity = entity, Agent = agent })
            : null;

    internal ConditionCatalog GetCatalog(string catalogId)
        => _catalogs.TryGetValue(catalogId, out var catalog) ? catalog : ConditionCatalog.Empty;

    private void RebuildIndexes()
    {
        MetaSensors.RebuildSubscriptions();
        _profileByEntityDef.Clear();
        foreach (var profile in Session.Defs.All<AgentProfileDef>())
        {
            foreach (var entityDef in profile.Entities)
            {
                _profileByEntityDef[entityDef] = profile;
            }
        }

        _catalogs.Clear();
        foreach (var catalogDef in Session.Defs.All<ConditionCatalogDef>())
        {
            if (catalogDef.Names.Count <= 32)
            {
                _catalogs[catalogDef.Id] = new ConditionCatalog(catalogDef.Names);
            }
        }
    }

    private void AttachAgent(Entity entity, bool isRestore)
    {
        if (!_profileByEntityDef.TryGetValue(entity.DefId, out var profile))
        {
            return;
        }

        var catalog = GetCatalog(profile.Conditions);
        IBrain brain = profile.Brain switch
        {
            "fsm" when profile.FsmBrain is not null && Session.Defs.TryGet<FsmBrainDef>(profile.FsmBrain, out var fsmDef) =>
                new DataFsmBrain(fsmDef),
            "schedules" => new ScheduleBrain(),
            "bt" when profile.BehaviorTree is not null && Session.Defs.TryGet<BehaviorTreeDef>(profile.BehaviorTree, out var btDef) =>
                new BehaviorTreeBrain(btDef, Session.Defs),
            "goap" => new GoapBrain(),
            "htn" => new HtnBrain(),
            _ => new NullBrain(profile.Brain),
        };

        var agent = new AgentComponent(profile, catalog, brain);
        agent.Beliefs.Set("spawn_position", entity.Position);
        foreach (var belief in profile.InitialBeliefs ?? new Dictionary<string, JsonElement>())
        {
            if (JsonValueHelper.TryToPlain(belief.Value, out var value))
            {
                agent.Beliefs.Set(belief.Key, value);
            }
        }

        foreach (var needId in profile.Needs ?? [])
        {
            if (Session.Defs.TryGet<NeedDef>(needId, out var need))
            {
                agent.Needs[needId] = Math.Clamp(need.Initial, 0, 1);
            }
        }

        entity.SetComponent(agent);
    }

    private void OnStimulusEvent(GameEvent evt, StimulusType type)
    {
        var x = evt.Payload.TryGetValue("x", out var xv) && xv is double xd ? xd : 0;
        var y = evt.Payload.TryGetValue("y", out var yv) && yv is double yd ? yd : 0;
        var z = evt.Payload.TryGetValue("z", out var zv) && zv is double zd ? zd : 0;
        var loudness = evt.Payload.TryGetValue("loudness", out var lv) && lv is double ld ? ld : 1.0;
        _stimuli.Add((new StimulusPacket
        {
            Type = type,
            Position = new Vector3((float)x, (float)y, (float)z),
            Loudness = loudness,
            SourceId = evt.Payload.TryGetValue("sourceId", out var s) ? s as string : null,
        }, Session.SimTimeSeconds + 0.6));
    }

    private void OnEntityDamaged(GameEvent evt)
    {
        if (evt.Payload.TryGetValue("instanceId", out var id)
            && id is string instanceId
            && Session.World.TryGet(instanceId, out var entity)
            && GetAgent(entity) is { } agent)
        {
            agent.LastDamagedAt = Session.SimTimeSeconds;
        }
    }

    /// <summary>Per-tick agent pipeline: perceive → meta-state → decide (brain) → move.</summary>
    private sealed class AgentSystem(AiRuntime ai) : ISimSystem
    {
        public string Name => "ai.agents";

        public void Tick(GameSession session, float dt)
        {
            var now = session.SimTimeSeconds;

            // standalone hosts: mirror entity positions into the permissive spatial index
            if (session.Services.Physics is PermissivePhysicsQueryService permissive)
            {
                foreach (var entity in session.World.All)
                {
                    permissive.SetEntityPosition(entity.InstanceId, entity.Position);
                }
            }

            ai._stimuli.RemoveAll(s => s.ExpiresAt <= now);
            ai.TransientStimuli = ai._stimuli.Select(s => s.Packet).ToList();

            foreach (var entity in session.World.All.ToList())
            {
                if (ai.GetAgent(entity) is not { } agent)
                {
                    continue;
                }

                var ctx = new AgentContext { Ai = ai, Entity = entity, Agent = agent };

                ai.MetaSensors.Sync(ctx); // before the sensor refresh: it ORs ManualConditions in
                SensorPipeline.Update(ctx, ai.TransientStimuli, now);
                UpdateMetaState(ctx, now);
                DecayNeeds(session, agent, dt);

                // tick-rate decoupling (ch06 §6.9): perception and movement run
                // every tick; thinking runs at the profile's cadence
                agent.ThinkAccumulator += dt;
                if (agent.Profile.ThinkInterval <= 0 || agent.ThinkAccumulator >= agent.Profile.ThinkInterval)
                {
                    agent.Brain.Tick(ctx, (float)agent.ThinkAccumulator);
                    agent.ThinkAccumulator = 0;
                }

                Move(ctx, dt);
            }
        }

        /// <summary>The Sims pattern (ch05 §5.4): motives fall over time, so urgency rises until an activity wins.</summary>
        private static void DecayNeeds(GameSession session, AgentComponent agent, float dt)
        {
            if (agent.Needs.Count == 0)
            {
                return;
            }

            foreach (var needId in agent.Needs.Keys.ToList())
            {
                if (session.Defs.TryGet<NeedDef>(needId, out var need) && need.DecayPerSecond > 0)
                {
                    agent.Needs[needId] = Math.Clamp(agent.Needs[needId] - need.DecayPerSecond * dt, 0, 1);
                }
            }
        }

        private static void UpdateMetaState(AgentContext ctx, double now)
        {
            var agent = ctx.Agent;
            var threatMask = agent.Catalog.MaskOf([
                SensorPipeline.CanSeeEnemy, SensorPipeline.ThreatKnown, SensorPipeline.HearSound, SensorPipeline.Damaged,
            ]);

            if (agent.Conditions.HasAnyOf(threatMask))
            {
                if (agent.Meta != MetaState.Alert)
                {
                    agent.Meta = MetaState.Alert;
                    agent.AddTrace(ctx.Session.Tick, "meta Idle -> Alert");
                }
            }
            else if (agent.Meta == MetaState.Alert && now - agent.LastThreatAt > agent.Profile.AlertDecaySeconds)
            {
                agent.Meta = MetaState.Idle;
                agent.AddTrace(ctx.Session.Tick, "meta Alert -> Idle");
            }
        }

        private static void Move(AgentContext ctx, float dt)
        {
            var agent = ctx.Agent;
            var entity = ctx.Entity;
            var remaining = (float)(agent.MoveSpeed * dt);

            // spend the full step budget across waypoints (paths begin at the
            // agent's own position; zero-length legs must not eat a tick)
            while (agent.IsMoving && remaining > 0)
            {
                var target = agent.Path[agent.PathIndex];
                var toTarget = target - entity.Position;
                var distance = toTarget.Length();

                if (distance <= remaining || distance < 0.01f)
                {
                    entity.Position = target;
                    remaining -= distance;
                    agent.PathIndex++;
                    if (!agent.IsMoving)
                    {
                        agent.HasArrived = true;
                        agent.StopMoving();
                    }
                }
                else
                {
                    var direction = toTarget / distance;
                    entity.Position += direction * remaining;
                    agent.Facing = direction;
                    remaining = 0;
                }
            }
        }
    }

    private sealed class NullBrain(string requestedKind) : IBrain
    {
        public string Kind => "null";

        public void Tick(AgentContext ctx, float dt)
        {
        }

        public string Describe() => $"null (unknown brain '{requestedKind}')";
    }
}

/// <summary>
/// Condition primitive reading an agent's condition bitmask:
/// {"type":"AgentCondition","condition":"CAN_SEE_ENEMY"}. Constructible
/// without a runtime for validation-only use (tooling).
/// </summary>
public sealed class AgentConditionEvaluator(AiRuntime? ai = null) : IConditionEvaluator
{
    public string Type => "AgentCondition";

    public bool Evaluate(ConditionContext ctx, JsonElement args)
    {
        if (ai is null || ctx.Subject is null || ai.GetAgent(ctx.Subject) is not { } agent)
        {
            return false;
        }

        return agent.Conditions.IsSet(agent.Catalog, JsonArgs.GetString(args, "condition"));
    }

    public void Validate(JsonElement args, EffectValidationContext v)
    {
        if (!JsonArgs.TryGetString(args, "condition", out _))
        {
            v.Error("AgentCondition requires 'condition'.");
        }
    }
}

/// <summary>
/// Meta-state gate: {"type":"AgentMeta","is":"Alert"}. Because the meta
/// state persists for AlertDecaySeconds after the last threat perception,
/// this smooths the per-tick flicker of instantaneous sensor conditions —
/// the BT analog of the schedule brain's metaStates filter.
/// </summary>
public sealed class AgentMetaCondition(AiRuntime? ai = null) : IConditionEvaluator
{
    public string Type => "AgentMeta";

    public bool Evaluate(ConditionContext ctx, JsonElement args)
        => ai is not null
           && ctx.Subject is not null
           && ai.GetAgent(ctx.Subject) is { } agent
           && string.Equals(agent.Meta.ToString(), JsonArgs.GetString(args, "is"), StringComparison.OrdinalIgnoreCase);

    public void Validate(JsonElement args, EffectValidationContext v)
    {
        if (!JsonArgs.TryGetString(args, "is", out var meta) || meta is not ("Idle" or "Alert"))
        {
            v.Error("AgentMeta requires 'is': \"Idle\" or \"Alert\".");
        }
    }
}

/// <summary>
/// Scalar belief comparison: {"type":"BeliefEquals","key":"role","value":"role_watcher"}.
/// The bridge that lets FSM transitions and BT gates branch on group role
/// assignments and other belief facts.
/// </summary>
public sealed class BeliefEqualsCondition(AiRuntime? ai = null) : IConditionEvaluator
{
    public string Type => "BeliefEquals";

    public bool Evaluate(ConditionContext ctx, JsonElement args)
    {
        if (ai is null
            || ctx.Subject is null
            || ai.GetAgent(ctx.Subject) is not { } agent
            || !args.TryGetProperty("value", out var valueProp)
            || !JsonValueHelper.TryToPlain(valueProp, out var expected))
        {
            return false;
        }

        return Equals(agent.Beliefs.Get(JsonArgs.GetString(args, "key")), expected);
    }

    public void Validate(JsonElement args, EffectValidationContext v)
    {
        if (!JsonArgs.TryGetString(args, "key", out _))
        {
            v.Error("BeliefEquals requires 'key'.");
        }

        if (!args.TryGetProperty("value", out var valueProp) || !JsonValueHelper.TryToPlain(valueProp, out _))
        {
            v.Error("BeliefEquals 'value' must be a bool, number, or string.");
        }
    }
}

/// <summary>Entry points for wiring the AI module into a session.</summary>
public static class LatticeAi
{
    /// <summary>Narrative def kinds plus the AI vocabulary.</summary>
    public static DefTypeRegistry CreateDefTypes()
    {
        var types = LatticeNarrative.CreateDefTypes();
        types.Register<ConditionCatalogDef>("conditions");
        types.Register<AgentProfileDef>("agent");
        types.Register<FsmBrainDef>("fsmbrain");
        types.Register<ScheduleDef>("schedule");
        types.Register<BehaviorTreeDef>("btree");
        types.Register<UtilityEvaluatorDef>("utility");
        types.Register<NeedDef>("need");
        types.Register<ActivityDef>("activity");
        types.Register<GoapActionDef>("goapaction");
        types.Register<GoapGoalDef>("goapgoal");
        types.Register<CostProfileDef>("costprofile");
        types.Register<HtnCompoundDef>("htncompound");
        types.Register<RoleDef>("role");
        types.Register<GroupDef>("group");
        types.Register<CollectiveDef>("collective");
        types.Register<MetaSensorDef>("metasensor");
        return types;
    }

    /// <summary>Attach the AI module. Call after RPG (and Narrative, if used), before LoadContent.</summary>
    public static AiRuntime Attach(GameSession session, RpgRuntime rpg, NarrativeRuntime? narrative = null)
        => new(session, rpg, narrative);
}
