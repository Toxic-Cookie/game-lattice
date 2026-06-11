using System.Numerics;
using System.Text.Json;
using Lattice.Ai.Goap;

namespace Lattice.Demos.Tests;

/// <summary>
/// Demo scene 2 — The Dungeon (plan/07 §2). Exercises GOAP combat, flanking
/// emerging from smart-object reservation (the F.E.A.R. Part 7 test at demo
/// scale), the rat-problem brain-tier audit, loot-on-kill, the poison trap,
/// and HTN method selection on the boss.
/// </summary>
public sealed class DungeonDemoTests : IDisposable
{
    private readonly DemoSceneHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public void Soldiers_FlankViaNodeReservation_And_RatsNeverTouchThePlanner()
    {
        _host.Boot("lifecycle_dungeon");
        var soldiers = _host.AllOf("entity_soldier");
        var nodes = _host.AllOf("entity_attack_node");
        var rats = _host.AllOf("entity_rat");
        Assert.Equal(3, soldiers.Count);
        Assert.Equal(3, nodes.Count);

        // tick until two soldiers hold *distinct* attack nodes at the same time
        var flanked = false;
        for (var tick = 0; tick < 12 * 30 && !flanked; tick++)
        {
            _host.TickSeconds(1.0 / 30.0);

            var holders = new HashSet<string>(StringComparer.Ordinal);
            var reservedNodes = 0;
            foreach (var node in nodes)
            {
                // reserved-by-other shows up as a denied probe reservation
                if (_host.Narrative.Interactions.CanReserve(node, "__probe__"))
                {
                    continue;
                }

                reservedNodes++;
                foreach (var soldier in soldiers)
                {
                    if (_host.Narrative.Interactions.CanReserve(node, soldier.InstanceId))
                    {
                        holders.Add(soldier.InstanceId); // self-reservation reads as reservable
                    }
                }
            }

            flanked = reservedNodes >= 2 && holders.Count >= 2;
        }

        Assert.True(flanked, "two soldiers never held distinct attack nodes simultaneously");

        // the rat problem (ch03): deliberative planning happened — but never for the rat tier
        Assert.True(_host.Ai.PlannerInvocations > 0, "the soldiers should have planned by now");
        foreach (var rat in rats)
        {
            var agent = _host.Ai.GetAgent(rat)!;
            Assert.Equal("fsm", agent.Brain.Kind);
            Assert.False(_host.Ai.PlannerInvocationsByAgent.ContainsKey(rat.InstanceId),
                $"{rat.InstanceId} is rat-tier and must never invoke the planner");
        }
    }

    [Fact]
    public void Kills_RollLootTables_ToTheKiller()
    {
        _host.Boot("lifecycle_dungeon");
        var delver = _host.Single("entity_delver");
        var soldier = _host.AllOf("entity_soldier")[0];
        var goldBefore = _host.Rpg.Inventory.Count(delver, "item_gold");

        var killshot = JsonDocument.Parse("""[ { "type": "DealDamage", "formula": "999" } ]""");
        _host.Rpg.RunEffects(killshot.RootElement.EnumerateArray(), source: delver, target: soldier);
        _host.Session.Events.DispatchPending();

        Assert.False(_host.Session.World.TryGet(soldier.InstanceId, out _));
        Assert.Contains(_host.Session.Events.Trace, e => e.Topic == "Entity.Died");
        Assert.Contains(_host.Session.Events.Trace, e => e.Topic == "Loot.Dropped");
        Assert.True(_host.Rpg.Inventory.Count(delver, "item_gold") > goldBefore,
            "loot_soldier always pays out gold to the killer");
    }

    [Fact]
    public void PoisonTrap_AppliesTheStatus_AndItTicks()
    {
        _host.Boot("lifecycle_dungeon");
        var delver = _host.Single("entity_delver");
        var trap = _host.Single("entity_trap");
        var sheet = _host.Rpg.GetSheet(delver)!;
        var hpBefore = sheet.Current("HP");

        Assert.True(_host.Narrative.Interactions.TryInteract(delver, trap, "step", out var error), error);
        _host.Session.Events.DispatchPending();

        Assert.Contains(_host.Session.Events.Trace, e => e.Topic == "Trap.Triggered");
        Assert.Contains(_host.Rpg.GetStatusEffects(delver)!.Active, s => s.Def.Id == "status_poison");
        Assert.Contains("poisoned", delver.Tags);

        _host.TickSeconds(2.5);
        Assert.True(sheet.Current("HP") < hpBefore, "poison should tick damage over time");
    }

    [Fact]
    public void Boss_PrefersRanged_ThenFallsBackToMelee_WhenTheArrowIsSpent()
    {
        _host.Boot("lifecycle_dungeon");
        var delver = _host.Single("entity_delver");
        var boss = _host.Single("entity_boss");
        var brain = Assert.IsType<HtnBrain>(_host.Ai.GetAgent(boss)!.Brain);

        // walk into the lair — the boss's visual sensor does the rest
        delver.Position = boss.Position + new Vector3(4, 0, 0);
        var sheet = _host.Rpg.GetSheet(delver)!;
        var hpBefore = sheet.Current("HP");

        var sawRanged = false;
        var sawMelee = false;
        for (var tick = 0; tick < 15 * 30 && !(sawRanged && sawMelee); tick++)
        {
            _host.TickSeconds(1.0 / 30.0);
            var steps = brain.CurrentPlan?.Steps.Select(s => s.Id).ToList() ?? [];
            sawRanged |= steps.Contains("action_shoot_bow");
            sawMelee |= !sawRanged ? false : steps.Contains("action_charge") && steps.Contains("action_claw");
        }

        Assert.True(sawRanged, "the first decomposition with an enemy in sight should pick the ranged method");
        Assert.True(sawMelee, "after the arrow is spent the melee method should be selected");
        Assert.Contains(brain.DecompositionTrace, line => line.Contains("melee"));
        Assert.True(sheet.Current("HP") < hpBefore, "the boss should have landed at least one hit");
    }
}
