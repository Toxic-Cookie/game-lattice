using System.Numerics;
using Lattice.Ai.Groups;
using Lattice.Core.Simulation;

namespace Lattice.Ai.Tests;

/// <summary>
/// The M4d acceptance scenario (plan/04): herd structure from role slots,
/// blackboard latency hiding unwitnessed kills, witnessed threats
/// escalating the alert level, and passport recycling of stranded
/// survivors. Standard content spawns two herds via collective_plains:
/// 6 beasts at (-30,-30) and 4 at (-30,30).
/// </summary>
public sealed class HerdTests : IDisposable
{
    private readonly AiTestHost _host = new();

    public HerdTests() => _host.WriteStandardContent();

    public void Dispose() => _host.Dispose();

    private static GroupAgent HerdAt(AiRuntime ai, GameSession session, float z)
        => ai.Groups.Groups.Single(g =>
            g.Members.Count > 0
            && session.World.TryGet(g.Members[0], out var member)
            && Math.Abs(member.Position.Z - z) < 15);

    [Fact]
    public void RoleSlots_ProduceTheCoreRingStructure()
    {
        var (session, _, ai) = _host.CreateLoadedSession();

        _host.TickSeconds(session, 8.0); // populate + rebalance + watchers walk to posts

        var herd = HerdAt(ai, session, -30);
        Assert.Equal(6, herd.Members.Count);
        Assert.Equal(2, herd.RoleOf.Values.Count(r => r == "role_watcher"));
        Assert.Equal(4, herd.RoleOf.Values.Count(r => r == "role_grazer"));

        // watchers stand on the ring; grazers stay near the core
        var positions = herd.Members
            .Select(id => session.World.TryGet(id, out var e) ? e : null)
            .Where(e => e is not null)
            .ToDictionary(e => e!.InstanceId, e => e!.Position);
        var centroid = positions.Values.Aggregate(Vector3.Zero, (a, p) => a + p) / positions.Count;

        foreach (var (memberId, role) in herd.RoleOf)
        {
            var distance = Vector3.Distance(positions[memberId], centroid);
            if (role == "role_watcher")
            {
                Assert.True(distance > 3.0, $"watcher {memberId} sits {distance:F1} from the centroid — not on the ring");
            }
            else
            {
                Assert.True(distance < 5.0, $"grazer {memberId} wandered {distance:F1} from the core");
            }
        }
    }

    [Fact]
    public void UnwitnessedKill_DoesNotAlertTheHerd_AndTheSlotRefills()
    {
        var (session, _, ai) = _host.CreateLoadedSession();
        _host.TickSeconds(session, 4.0);
        var herd = HerdAt(ai, session, -30);
        var watcher = herd.RoleOf.First(p => p.Value == "role_watcher").Key;

        session.World.Despawn(watcher); // a silent kill nobody perceives
        _host.TickSeconds(session, 3.0);

        Assert.Equal(GroupAlertLevel.Relaxed, herd.Alert); // latency is the feature (HZD Part 5)
        Assert.Equal(5, herd.Members.Count);
        Assert.Equal(2, herd.RoleOf.Values.Count(r => r == "role_watcher")); // a grazer was promoted
        Assert.DoesNotContain(watcher, herd.RoleOf.Keys);
    }

    [Fact]
    public void WitnessedThreat_EscalatesAndSpreadsThroughTheBlackboard()
    {
        var (session, _, ai) = _host.CreateLoadedSession();
        _host.TickSeconds(session, 4.0);
        var herd = HerdAt(ai, session, -30);

        // drop a hostile right inside the herd: at least one member perceives it
        var members = herd.Members
            .Select(id => session.World.TryGet(id, out var e) ? e! : throw new InvalidOperationException())
            .ToList();
        var centroid = members.Aggregate(Vector3.Zero, (a, m) => a + m.Position) / members.Count;
        session.World.Spawn("entity_player", centroid);
        _host.TickSeconds(session, 1.5);

        Assert.True(herd.Alert >= GroupAlertLevel.Alerted, $"herd stayed {herd.Alert}");
        Assert.Contains(herd.Log, l => l.Contains("alert Relaxed ->"));

        // every member knows via the blackboard — even ones that saw nothing
        foreach (var memberId in herd.Members)
        {
            var entity = session.World.TryGet(memberId, out var e) ? e : null;
            var agent = entity is null ? null : ai.GetAgent(entity);
            Assert.NotNull(agent);
            Assert.True(agent.Conditions.IsSet(agent.Catalog, GroupManager.GroupAlertCondition)
                        || agent.Conditions.IsSet(agent.Catalog, "THREAT_KNOWN")
                        || agent.Conditions.IsSet(agent.Catalog, "CAN_SEE_ENEMY"),
                $"{memberId} never heard about the threat");
        }
    }

    [Fact]
    public void AlertDecays_WhenTheThreatGoesStale()
    {
        var (session, _, ai) = _host.CreateLoadedSession();
        _host.TickSeconds(session, 4.0);
        var herd = HerdAt(ai, session, -30);

        var members = herd.Members.Select(id => session.World.TryGet(id, out var e) ? e! : throw new InvalidOperationException()).ToList();
        var centroid = members.Aggregate(Vector3.Zero, (a, m) => a + m.Position) / members.Count;
        var threat = session.World.Spawn("entity_player", centroid);
        _host.TickSeconds(session, 1.5);
        Assert.True(herd.Alert >= GroupAlertLevel.Alerted);

        session.World.Despawn(threat.InstanceId);
        _host.TickSeconds(session, 16.0); // staleness (3s) + two decay stages (4s each) + flee time

        Assert.Equal(GroupAlertLevel.Relaxed, herd.Alert);
    }

    [Fact]
    public void IsolatedSurvivor_IsRecycledIntoACompatibleGroup()
    {
        var (session, _, ai) = _host.CreateLoadedSession();
        _host.TickSeconds(session, 2.0);
        var smallHerd = HerdAt(ai, session, 30);   // 4 members
        var bigHerd = HerdAt(ai, session, -30);    // 6 members

        // silent catastrophe: all but one of the small herd vanishes
        foreach (var memberId in smallHerd.Members.Skip(1).ToList())
        {
            session.World.Despawn(memberId);
        }

        var survivor = smallHerd.Members[0];
        _host.TickSeconds(session, 3.0); // collective tick runs at /30

        Assert.Same(bigHerd, ai.Groups.GetGroupOf(survivor));
        Assert.Equal(7, bigHerd.Members.Count);
        Assert.Contains(bigHerd.Log, l => l.Contains("recycled") && l.Contains("passport herd_beast"));
    }

    [Fact]
    public void BudgetEnforcement_DespawnsTheFarthestFromThePlayer()
    {
        _host.WriteContent("tinybudget.json", """
            [ { "id": "group_pack", "type": "group", "roles": ["role_grazer"],
                "minMembers": 1, "maxMembers": 12 },
              { "id": "collective_pack", "type": "collective", "budget": 3, "sites": [
                  { "position": [40, 0, 0], "group": "group_pack",
                    "members": [ { "entity": "entity_beast", "count": 3 } ], "spawnRadius": 1 },
                  { "position": [80, 0, 0], "group": "group_pack",
                    "members": [ { "entity": "entity_beast", "count": 2 } ], "spawnRadius": 1 } ] } ]
            """);
        var (session, _, ai) = _host.CreateLoadedSession();
        session.World.Spawn("entity_player", new Vector3(40, 0, 0));

        _host.TickSeconds(session, 3.0);

        var packMembers = ai.Groups.Groups
            .Where(g => g.Def.Id == "group_pack")
            .SelectMany(g => g.Members)
            .Select(id => session.World.TryGet(id, out var e) ? e! : throw new InvalidOperationException())
            .ToList();
        Assert.Equal(3, packMembers.Count);
        Assert.All(packMembers, e => Assert.True(e.Position.X < 60, $"{e.InstanceId} at {e.Position} survived over a nearer beast"));
    }
}
