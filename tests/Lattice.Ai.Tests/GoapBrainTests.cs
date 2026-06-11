using System.Diagnostics;
using System.Numerics;
using Lattice.Ai.Goap;
using Lattice.Core.Events;

namespace Lattice.Ai.Tests;

/// <summary>
/// The GOAP brain end-to-end (plan/04 §7–§11): goal relevance and
/// hysteresis, the three validation mechanisms, cost profiles, smart-object
/// actions, flanking by exclusion, and the perf guard.
/// </summary>
public sealed class GoapBrainTests : IDisposable
{
    private readonly AiTestHost _host = new();

    public GoapBrainTests() => _host.WriteStandardContent();

    public void Dispose() => _host.Dispose();

    [Fact]
    public void GoapProfile_GetsABrainAndSeededBeliefs()
    {
        var (session, _, ai) = _host.CreateLoadedSession();
        var soldier = session.World.Spawn("entity_soldier", Vector3.Zero);
        var agent = ai.GetAgent(soldier)!;

        Assert.IsType<GoapBrain>(agent.Brain);
        Assert.Equal(true, agent.Beliefs.Get("weapon_loaded"));
    }

    [Fact]
    public void NoPerceivedEnemy_MeansNoRelevantGoal()
    {
        var (session, _, ai) = _host.CreateLoadedSession();
        var soldier = session.World.Spawn("entity_soldier", Vector3.Zero);

        _host.TickSeconds(session, 0.2);

        Assert.Contains("no relevant goal", ai.GetAgent(soldier)!.Brain.Describe());
    }

    [Fact]
    public void Flanking_EmergesFromReservationExclusion()
    {
        // F.E.A.R. Part 7 as an assertion: two soldiers, one target, two
        // attack positions with maxUsers 1 — they must end on different nodes
        var (session, _, ai) = _host.CreateLoadedSession();
        var soldierA = session.World.Spawn("entity_soldier", new Vector3(0, 0, 1));
        var soldierB = session.World.Spawn("entity_soldier", new Vector3(0, 0, -1));
        session.World.Spawn("entity_dummy", new Vector3(8, 0, 0));
        var nodeA = session.World.Spawn("entity_attack_node", new Vector3(6, 0, 3));
        var nodeB = session.World.Spawn("entity_attack_node", new Vector3(6, 0, -3));

        _host.TickSeconds(session, 6.0);

        var positions = new[] { soldierA.Position, soldierB.Position };
        Assert.Contains(positions, p => Vector3.Distance(p, nodeA.Position) < 0.5);
        Assert.Contains(positions, p => Vector3.Distance(p, nodeB.Position) < 0.5);

        var traceA = string.Join("\n", ai.GetAgent(soldierA)!.Trace);
        var traceB = string.Join("\n", ai.GetAgent(soldierB)!.Trace);
        Assert.Contains(nodeA.InstanceId, traceA + traceB);
        Assert.Contains(nodeB.InstanceId, traceA + traceB);
        Assert.Contains("action_open_fire done", traceA);
        Assert.Contains("action_open_fire done", traceB);
    }

    [Fact]
    public void Damage_SwitchesToTheSurvivalGoalAndRetreats()
    {
        var (session, _, ai) = _host.CreateLoadedSession();
        var soldier = session.World.Spawn("entity_soldier", new Vector3(0, 0, 0));
        session.World.Spawn("entity_dummy", new Vector3(8, 0, 0));
        _host.TickSeconds(session, 0.2);
        Assert.Equal("goal_eliminate_intruder", ((GoapBrain)ai.GetAgent(soldier)!.Brain).CurrentGoalId);

        session.Events.Publish("Entity.Damaged", EventPayload.Of(("instanceId", soldier.InstanceId)));
        _host.TickSeconds(session, 0.5);

        var agent = ai.GetAgent(soldier)!;
        Assert.Equal("goal_survive", ((GoapBrain)agent.Brain).CurrentGoalId);
        Assert.Contains(agent.Trace, t => t.Contains("goal_eliminate_intruder -> goal_survive"));
        Assert.Contains(agent.Trace, t => t.Contains("plan goal_survive: action_retreat"));
    }

    [Fact]
    public void PreconditionRecheckAtActivation_CatchesStaleAssumptions()
    {
        // mechanism 3: the plan assumes weapon_loaded; yank it while the
        // soldier is still walking to the attack position
        var (session, _, ai) = _host.CreateLoadedSession();
        var soldier = session.World.Spawn("entity_soldier", new Vector3(0, 0, 0));
        session.World.Spawn("entity_dummy", new Vector3(10, 0, 0));
        session.World.Spawn("entity_attack_node", new Vector3(8, 0, 2));
        _host.TickSeconds(session, 0.3);

        var agent = ai.GetAgent(soldier)!;
        Assert.Contains(agent.Trace, t => t.Contains("plan goal_eliminate_intruder"));
        agent.Beliefs.Set("weapon_loaded", false);

        _host.TickSeconds(session, 4.0);

        Assert.Contains(agent.Trace, t => t.Contains("action_open_fire preconditions no longer hold"));
        // and the next plan inserts the reload the planner now knows it needs
        Assert.Contains(agent.Trace, t => t.Contains("action_reload"));
    }

    [Fact]
    public void ReplanRequiredConditions_InvalidateTheRunningPlan()
    {
        // mechanism 2 in isolation: a goal that must replan on HEAR_SOUND,
        // with no competing goal to mask the trigger
        _host.WriteContent("bots.json", """
            [ { "id": "entity_walker", "type": "entity", "name": "Walker" },
              { "id": "goal_walk", "type": "goapgoal", "desired": { "arrived": true },
                "priority": "1", "replanRequired": ["HEAR_SOUND"] },
              { "id": "action_walk_far", "type": "goapaction",
                "effects": { "arrived": true }, "cost": "1", "moveTo": [100, 0, 0] },
              { "id": "profile_walker", "type": "agent", "entities": ["entity_walker"],
                "brain": "goap", "goals": ["goal_walk"], "actions": ["action_walk_far"],
                "sensors": [ { "kind": "auditory", "range": 20 } ] } ]
            """);
        var (session, _, ai) = _host.CreateLoadedSession();
        var walker = session.World.Spawn("entity_walker", Vector3.Zero);
        _host.TickSeconds(session, 0.3);
        Assert.True(ai.GetAgent(walker)!.IsMoving);

        session.Events.Publish("Stimulus.Sound", EventPayload.Of(("x", 2.0), ("y", 0.0), ("z", 0.0)));
        _host.TickSeconds(session, 0.2);

        Assert.Contains(ai.GetAgent(walker)!.Trace, t => t.Contains("replan required by HEAR_SOUND"));
    }

    [Fact]
    public void NonInterruptibleAnimation_BlocksGoalSwitching()
    {
        var (session, _, ai) = _host.CreateLoadedSession();
        var soldier = session.World.Spawn("entity_soldier", new Vector3(0, 0, 0));
        session.World.Spawn("entity_dummy", new Vector3(6, 0, 0));
        session.World.Spawn("entity_attack_node", new Vector3(0.5f, 0, 0)); // right here: fire starts fast
        var agent = ai.GetAgent(soldier)!;

        // run until the blocking shoot animation is in flight
        var deadline = 0;
        while (!session.Services.Animation.IsPlayingNonInterruptible(soldier.InstanceId) && deadline++ < 120)
        {
            _host.TickSeconds(session, 1.0 / 30);
        }

        Assert.True(deadline < 120, "soldier never started firing");

        session.Events.Publish("Entity.Damaged", EventPayload.Of(("instanceId", soldier.InstanceId)));
        _host.TickSeconds(session, 2.0 / 30);
        Assert.Equal("goal_eliminate_intruder", ((GoapBrain)agent.Brain).CurrentGoalId); // committed (ch05 §5.6)

        // re-damage so the 0.5s DAMAGED window outlives the 0.4s animation
        session.Events.Publish("Entity.Damaged", EventPayload.Of(("instanceId", soldier.InstanceId)));
        _host.TickSeconds(session, 0.5);
        Assert.Equal("goal_survive", ((GoapBrain)agent.Brain).CurrentGoalId);
    }

    [Fact]
    public void CostProfile_IsThePersonality()
    {
        // same action set, override flips the choice (ch03 §3.7)
        _host.WriteContent("bots.json", """
            [ { "id": "entity_bot", "type": "entity", "name": "Bot" },
              { "id": "goal_done", "type": "goapgoal", "desired": { "done": true }, "priority": "1" },
              { "id": "action_easy", "type": "goapaction", "effects": { "done": true }, "cost": "1" },
              { "id": "action_hard", "type": "goapaction", "effects": { "done": true }, "cost": "5" },
              { "id": "costprofile_contrarian", "type": "costprofile",
                "overrides": { "action_easy": "50" } },
              { "id": "profile_bot", "type": "agent", "entities": ["entity_bot"],
                "brain": "goap", "goals": ["goal_done"],
                "actions": ["action_easy", "action_hard"],
                "costProfile": "costprofile_contrarian" } ]
            """);
        var (session, _, ai) = _host.CreateLoadedSession();
        var bot = session.World.Spawn("entity_bot", Vector3.Zero);

        _host.TickSeconds(session, 0.2);

        Assert.Contains(ai.GetAgent(bot)!.Trace, t => t.Contains("plan goal_done: action_hard"));
    }

    [Fact]
    public void Dump_ExplainsEveryDecision()
    {
        var (session, _, ai) = _host.CreateLoadedSession();
        var soldier = session.World.Spawn("entity_soldier", new Vector3(0, 0, 0));
        session.World.Spawn("entity_dummy", new Vector3(8, 0, 0));
        session.World.Spawn("entity_attack_node", new Vector3(6, 0, 2));
        _host.TickSeconds(session, 0.2);

        var dump = ai.DumpGoap(soldier)!;

        Assert.Contains("CAN_SEE_ENEMY = true", dump);
        Assert.Contains("> goal_eliminate_intruder", dump);
        Assert.Contains("plan: use:so_attack_node@", dump);
        Assert.Contains("missing: in_attack_position=true", dump); // action_open_fire's report
        Assert.Contains("action_retreat", dump);
    }

    [Fact]
    public void TwentyAgents_ReplanWithinBudget()
    {
        // the rat-problem guard at GOAP scale: 20 planners + a hostile, 3
        // simulated seconds, bounded wall time (generous for CI noise)
        var (session, _, _) = _host.CreateLoadedSession();
        for (var i = 0; i < 20; i++)
        {
            session.World.Spawn("entity_soldier", new Vector3(i % 5, 0, i / 5f));
        }

        session.World.Spawn("entity_dummy", new Vector3(8, 0, 0));
        session.World.Spawn("entity_attack_node", new Vector3(6, 0, 2));
        session.World.Spawn("entity_attack_node", new Vector3(6, 0, -2));

        var stopwatch = Stopwatch.StartNew();
        _host.TickSeconds(session, 3.0);
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 5000, $"90 ticks of 20 GOAP agents took {stopwatch.ElapsedMilliseconds}ms");
    }
}
