using System.Numerics;
using Lattice.Ai.Agents;
using Lattice.Ai.Defs;
using Lattice.Ai.Perception;
using Lattice.Core.Simulation;

namespace Lattice.Ai.Groups;

/// <summary>HZD Part 5 alert ladder. Escalation is immediate; decay steps down one stage at a time.</summary>
public enum GroupAlertLevel
{
    Relaxed,
    Alerted,
    Combat,
    Search,
}

/// <summary>
/// A non-physical group agent (HZD Part 2): a member list, a *scoped*
/// blackboard, an alert level, and role assignments. Members never read
/// each other — all coordination flows through the blackboard, and reads
/// honor per-key staleness, so an unwitnessed kill simply never lands here
/// (latency is the feature).
/// </summary>
public sealed class GroupAgent
{
    private readonly List<string> _log = [];

    public required string Id { get; init; }

    public required GroupDef Def { get; init; }

    /// <summary>Scoped shared knowledge (timestamps via the sim clock).</summary>
    public required Blackboard Board { get; init; }

    public List<string> Members { get; } = [];

    public GroupAlertLevel Alert { get; internal set; } = GroupAlertLevel.Relaxed;

    /// <summary>Member instance ID → role def ID.</summary>
    public Dictionary<string, string> RoleOf { get; } = new(StringComparer.Ordinal);

    internal double LastAlertChangeAt { get; set; }

    internal bool NeedsRebalance { get; set; } = true;

    public IReadOnlyList<string> Log => _log;

    internal void AddLog(long tick, string message)
    {
        _log.Add($"[{tick}] {message}");
        if (_log.Count > 32)
        {
            _log.RemoveAt(0);
        }
    }
}

/// <summary>
/// Group bookkeeping plus the /10-cadence group tick and /30-cadence
/// collective tick (the ch06 §6.9 tier scheduler: sensors and execution run
/// every frame on the agent system; groups and the collective think slower).
/// </summary>
public sealed class GroupManager
{
    public const string ThreatLevelKey = "threat_level";
    public const string GroupAlertCondition = "GROUP_ALERT";

    private readonly AiRuntime _ai;
    private readonly List<GroupAgent> _groups = [];
    private readonly Dictionary<string, GroupAgent> _groupOfMember = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _collectiveAgents = new(StringComparer.Ordinal);
    private int _nextGroupNumber = 1;

    internal GroupManager(AiRuntime ai) => _ai = ai;

    public IReadOnlyList<GroupAgent> Groups => _groups;

    public GroupAgent? GetGroupOf(string memberId)
        => _groupOfMember.TryGetValue(memberId, out var group) ? group : null;

    public GroupAgent? GetGroup(string groupId)
        => _groups.FirstOrDefault(g => g.Id == groupId);

    public GroupAgent CreateGroup(string groupDefId, string? id = null)
    {
        if (!_ai.Session.Defs.TryGet<GroupDef>(groupDefId, out var def))
        {
            throw new ArgumentException($"'{groupDefId}' is not a group def.", nameof(groupDefId));
        }

        var session = _ai.Session;
        var group = new GroupAgent
        {
            Id = id ?? $"{groupDefId}#{_nextGroupNumber++}",
            Def = def,
            Board = new Blackboard(() => session.SimTimeSeconds),
        };
        _groups.Add(group);
        return group;
    }

    public void AddMember(GroupAgent group, Entity entity)
    {
        if (_groupOfMember.TryGetValue(entity.InstanceId, out var previous))
        {
            previous.Members.Remove(entity.InstanceId);
            previous.RoleOf.Remove(entity.InstanceId);
            previous.NeedsRebalance = true;
        }

        group.Members.Add(entity.InstanceId);
        _groupOfMember[entity.InstanceId] = group;
        group.NeedsRebalance = true;
    }

    public void RemoveMember(string memberId)
    {
        if (_groupOfMember.Remove(memberId, out var group))
        {
            group.Members.Remove(memberId);
            group.RoleOf.Remove(memberId);
            group.NeedsRebalance = true;
        }
    }

    // ───────────────────────── group tick (/10) ─────────────────────────

    internal void TickGroups()
    {
        foreach (var group in _groups)
        {
            PruneMembers(group);
            IngestPerceptions(group);
            UpdateAlert(group);
            if (group.NeedsRebalance)
            {
                Rebalance(group);
            }

            SyncMembers(group);
        }
    }

    private void PruneMembers(GroupAgent group)
    {
        for (var i = group.Members.Count - 1; i >= 0; i--)
        {
            if (!_ai.Session.World.TryGet(group.Members[i], out _))
            {
                var gone = group.Members[i];
                group.AddLog(_ai.Session.Tick, $"member {gone} lost");
                group.Members.RemoveAt(i);
                group.RoleOf.Remove(gone);
                _groupOfMember.Remove(gone);
                group.NeedsRebalance = true;
            }
        }
    }

    /// <summary>Members post what *they* perceive; nobody writes another agent's mind.</summary>
    private void IngestPerceptions(GroupAgent group)
    {
        foreach (var memberId in group.Members)
        {
            if (!_ai.Session.World.TryGet(memberId, out var entity) || _ai.GetAgent(entity) is not { } agent)
            {
                continue;
            }

            var sees = agent.Conditions.IsSet(agent.Catalog, SensorPipeline.CanSeeEnemy);
            var knows = agent.Conditions.IsSet(agent.Catalog, SensorPipeline.ThreatKnown)
                        || agent.Conditions.IsSet(agent.Catalog, SensorPipeline.Damaged);
            if (!sees && !knows)
            {
                continue;
            }

            var threat = agent.Beliefs.GetPosition("enemy_position")
                         ?? agent.Beliefs.GetPosition("threat_position")
                         ?? entity.Position;
            group.Board.Write(ThreatLevelKey, sees ? "combat" : "alerted");
            group.Board.Write("threat_x", (double)threat.X);
            group.Board.Write("threat_y", (double)threat.Y);
            group.Board.Write("threat_z", (double)threat.Z);
        }
    }

    private void UpdateAlert(GroupAgent group)
    {
        var session = _ai.Session;
        var staleness = group.Def.Staleness?.TryGetValue(ThreatLevelKey, out var s) == true ? s : 3.0;
        var (value, age) = group.Board.ReadWithAge(ThreatLevelKey);

        var previous = group.Alert;
        if (age <= staleness)
        {
            var target = Equals(value, "combat") ? GroupAlertLevel.Combat : GroupAlertLevel.Alerted;
            if (target > group.Alert || group.Alert == GroupAlertLevel.Search)
            {
                group.Alert = target;
            }
        }
        else if (group.Alert != GroupAlertLevel.Relaxed
                 && session.SimTimeSeconds - group.LastAlertChangeAt > group.Def.AlertDecaySeconds)
        {
            // step down: Combat -> Search -> Relaxed; Alerted -> Relaxed
            group.Alert = group.Alert == GroupAlertLevel.Combat ? GroupAlertLevel.Search : GroupAlertLevel.Relaxed;
        }

        if (group.Alert != previous)
        {
            group.LastAlertChangeAt = session.SimTimeSeconds;
            group.AddLog(session.Tick, $"alert {previous} -> {group.Alert}");
            group.NeedsRebalance = true;
        }
    }

    /// <summary>Fill role slots in declared priority order; ring roles get evenly spaced posts around the centroid.</summary>
    private void Rebalance(GroupAgent group)
    {
        group.NeedsRebalance = false;
        group.RoleOf.Clear();

        var members = group.Members
            .Select(id => _ai.Session.World.TryGet(id, out var e) ? e : null)
            .Where(e => e is not null)
            .Select(e => e!)
            .OrderBy(e => e.InstanceId, StringComparer.Ordinal)
            .ToList();
        if (members.Count == 0)
        {
            return;
        }

        var centroid = Vector3.Zero;
        foreach (var member in members)
        {
            centroid += member.Position;
        }

        centroid /= members.Count;

        var unassigned = new Queue<Entity>(members);
        foreach (var roleId in group.Def.Roles)
        {
            if (!_ai.Session.Defs.TryGet<RoleDef>(roleId, out var role))
            {
                continue;
            }

            var take = Math.Min(role.Slots, unassigned.Count);
            for (var i = 0; i < take; i++)
            {
                var member = unassigned.Dequeue();
                group.RoleOf[member.InstanceId] = roleId;

                if (_ai.GetAgent(member) is { } agent)
                {
                    agent.Beliefs.Set("role", roleId);
                    if (role.RingRadius is { } radius)
                    {
                        var angle = take <= 1 ? 0 : Math.PI * 2 * i / take;
                        agent.Beliefs.Set("post_position", centroid + new Vector3(
                            (float)(Math.Cos(angle) * radius), 0, (float)(Math.Sin(angle) * radius)));
                    }
                    else
                    {
                        agent.Beliefs.Remove("post_position");
                    }
                }
            }
        }

        // members beyond all slot capacity hold no role
        while (unassigned.Count > 0)
        {
            var leftover = unassigned.Dequeue();
            if (_ai.GetAgent(leftover) is { } agent)
            {
                agent.Beliefs.Remove("role");
                agent.Beliefs.Remove("post_position");
            }
        }

        group.AddLog(_ai.Session.Tick, "roles: " + string.Join(", ",
            group.Def.Roles.Select(r => $"{r}×{group.RoleOf.Count(p => p.Value == r)}")));
    }

    /// <summary>Members read *from* the blackboard with staleness thresholds — the only inbound channel.</summary>
    private void SyncMembers(GroupAgent group)
    {
        var threatStaleness = group.Def.Staleness?.TryGetValue("threat_position", out var s) == true ? s : 6.0;
        var threatFresh = !group.Board.IsStale("threat_x", threatStaleness);

        foreach (var memberId in group.Members)
        {
            if (!_ai.Session.World.TryGet(memberId, out var entity) || _ai.GetAgent(entity) is not { } agent)
            {
                continue;
            }

            agent.Beliefs.Set("group_alert", group.Alert.ToString());
            if (threatFresh)
            {
                agent.Beliefs.Set("group_threat_position", new Vector3(
                    (float)group.Board.ReadNumber("threat_x"),
                    (float)group.Board.ReadNumber("threat_y"),
                    (float)group.Board.ReadNumber("threat_z")));
            }
            else
            {
                agent.Beliefs.Remove("group_threat_position");
            }

            // manual condition hygiene: clear every group-managed bit, then set the current ones
            var managed = agent.Catalog.MaskOf(
                group.Def.Roles
                    .Select(r => _ai.Session.Defs.TryGet<RoleDef>(r, out var role) ? role.Condition : null)
                    .Where(c => c is not null)
                    .Select(c => c!)
                    .Append(GroupAlertCondition));
            agent.ManualConditions &= ~managed;

            if (group.Alert >= GroupAlertLevel.Alerted)
            {
                agent.ManualConditions |= agent.Catalog.MaskOf([GroupAlertCondition]);
            }

            if (group.RoleOf.TryGetValue(memberId, out var roleId)
                && _ai.Session.Defs.TryGet<RoleDef>(roleId, out var memberRole)
                && memberRole.Condition is { } condition)
            {
                agent.ManualConditions |= agent.Catalog.MaskOf([condition]);
            }
        }
    }

    // ─────────────────────── collective tick (/30) ───────────────────────

    internal void TickCollective()
    {
        foreach (var collective in _ai.Session.Defs.All<CollectiveDef>().OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            if (!_collectiveAgents.TryGetValue(collective.Id, out var spawned))
            {
                spawned = new HashSet<string>(StringComparer.Ordinal);
                _collectiveAgents[collective.Id] = spawned;
                Populate(collective, spawned);
            }

            EnforceBudget(collective, spawned);
            RecycleStrays();
        }
    }

    private void Populate(CollectiveDef collective, HashSet<string> spawned)
    {
        var session = _ai.Session;
        foreach (var site in collective.Sites)
        {
            if (site.Position.Length != 3 || !session.Defs.TryGet<GroupDef>(site.Group, out _))
            {
                continue;
            }

            var group = CreateGroup(site.Group);
            var origin = new Vector3(site.Position[0], site.Position[1], site.Position[2]);
            foreach (var spec in site.Members)
            {
                for (var i = 0; i < spec.Count; i++)
                {
                    var angle = session.Rng.NextDouble() * Math.PI * 2;
                    var distance = session.Rng.NextDouble() * site.SpawnRadius;
                    var position = origin + new Vector3(
                        (float)(Math.Cos(angle) * distance), 0, (float)(Math.Sin(angle) * distance));
                    var entity = session.World.Spawn(spec.Entity, position);
                    spawned.Add(entity.InstanceId);
                    AddMember(group, entity);
                }
            }

            group.AddLog(session.Tick, $"populated at {origin} ({group.Members.Count} members)");
        }
    }

    private void EnforceBudget(CollectiveDef collective, HashSet<string> spawned)
    {
        var session = _ai.Session;
        spawned.RemoveWhere(id => !session.World.TryGet(id, out _));
        if (spawned.Count <= collective.Budget)
        {
            return;
        }

        var player = session.World.All.FirstOrDefault(e => e.Tags.Contains("player"));
        var anchor = player?.Position ?? Vector3.Zero;
        var doomed = spawned
            .Select(id => session.World.TryGet(id, out var e) ? e : null)
            .Where(e => e is not null)
            .Select(e => e!)
            .OrderByDescending(e => Vector3.DistanceSquared(e.Position, anchor))
            .ThenBy(e => e.InstanceId, StringComparer.Ordinal)
            .Take(spawned.Count - collective.Budget)
            .ToList();
        foreach (var entity in doomed)
        {
            GetGroupOf(entity.InstanceId)?.AddLog(session.Tick, $"member {entity.InstanceId} despawned (budget)");
            RemoveMember(entity.InstanceId);
            spawned.Remove(entity.InstanceId);
            session.World.Despawn(entity.InstanceId);
        }
    }

    /// <summary>Survivors of a collapsed group transfer to a passport-compatible one (HZD Part 4).</summary>
    private void RecycleStrays()
    {
        var session = _ai.Session;
        foreach (var group in _groups.Where(g => g.Members.Count > 0 && g.Members.Count < g.Def.MinMembers).ToList())
        {
            foreach (var memberId in group.Members.ToList())
            {
                if (!session.World.TryGet(memberId, out var entity)
                    || _ai.GetAgent(entity) is not { } agent
                    || agent.Profile.Passport is not { } passport)
                {
                    continue;
                }

                var destination = _groups
                    .Where(g => g != group
                                && g.Members.Count >= g.Def.MinMembers
                                && g.Members.Count < g.Def.MaxMembers
                                && g.Def.Passports?.Contains(passport) == true)
                    .OrderBy(g => g.Id, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (destination is null)
                {
                    continue;
                }

                AddMember(destination, entity);
                destination.AddLog(session.Tick, $"recycled {memberId} from {group.Id} (passport {passport})");

                // re-anchor so the stray physically migrates toward its new group
                if (destination.Members.Count > 1
                    && session.World.TryGet(destination.Members[0], out var anchorMember))
                {
                    agent.Beliefs.Set("spawn_position", anchorMember.Position);
                }
            }
        }
    }
}

/// <summary>The /10 and /30 cadence driver (ch06 §6.9 tiers).</summary>
internal sealed class GroupSystem(GroupManager manager) : ISimSystem
{
    public string Name => "ai.groups";

    public void Tick(GameSession session, float dt)
    {
        if (session.Tick % 10 == 0)
        {
            manager.TickGroups();
        }

        if (session.Tick % 30 == 0)
        {
            manager.TickCollective();
        }
    }
}
