using System.Globalization;
using System.Numerics;
using Lattice.Ai.Agents;
using Lattice.Core.Simulation;

namespace Lattice.Ai.Tests;

/// <summary>
/// BT semantics (plan/04 §5): memory composites, gate aborts, decorators,
/// subtree refs, and tick-rate decoupling. Trees are pure content JSON.
/// </summary>
public sealed class BehaviorTreeTests : IDisposable
{
    private readonly AiTestHost _host = new();

    public void Dispose() => _host.Dispose();

    /// <summary>Standard content plus a bot whose BT root is <paramref name="rootJson"/>.</summary>
    private (GameSession Session, AiRuntime Ai, Entity Bot, AgentComponent Agent) SpawnBot(
        string rootJson, double thinkInterval = 0, string? extraDefs = null)
    {
        _host.WriteStandardContent();
        var interval = thinkInterval.ToString(CultureInfo.InvariantCulture);
        var extra = extraDefs is null ? "" : $", {extraDefs}";
        _host.WriteContent("bots.json", $$"""
            [ { "id": "entity_bot", "type": "entity", "name": "Bot" },
              { "id": "profile_bot", "type": "agent", "entities": ["entity_bot"],
                "brain": "bt", "behaviorTree": "btree_test", "thinkInterval": {{interval}},
                "walkSpeed": 2.0, "runSpeed": 4.0 },
              { "id": "btree_test", "type": "btree", "root": {{rootJson}} }{{extra}} ]
            """);
        var (session, _, ai) = _host.CreateLoadedSession();
        var bot = session.World.Spawn("entity_bot", Vector3.Zero);
        var agent = ai.GetAgent(bot)!;
        return (session, ai, bot, agent);
    }

    [Fact]
    public void BtProfile_GetsABehaviorTreeBrain()
    {
        var (_, _, _, agent) = SpawnBot("""{ "task": "Wait", "seconds": 1 }""");

        Assert.IsType<BehaviorTreeBrain>(agent.Brain);
    }

    [Fact]
    public void Sequence_ResumesItsRunningChildAcrossTicks()
    {
        var (session, _, _, _) = SpawnBot("""
            { "node": "Sequence", "children": [
                { "task": "Wait", "seconds": 0.5 },
                { "task": "PublishEvent", "event": "Test.Done" } ] }
            """);
        var done = 0;
        session.Events.Subscribe("Test.Done", _ => done++);

        _host.TickSeconds(session, 0.4);
        Assert.Equal(0, done); // still inside the Wait

        _host.TickSeconds(session, 0.2);
        Assert.True(done >= 1, "sequence never reached its second child");
    }

    [Fact]
    public void Selector_FallsThroughToTheNextChildOnFailure()
    {
        var (session, _, _, _) = SpawnBot("""
            { "node": "Selector", "children": [
                { "node": "ConditionGate",
                  "when": [ { "type": "FlagEquals", "flag": "go", "value": true } ],
                  "child": { "task": "PublishEvent", "event": "Test.A" } },
                { "task": "PublishEvent", "event": "Test.B" } ] }
            """);
        int a = 0, b = 0;
        session.Events.Subscribe("Test.A", _ => a++);
        session.Events.Subscribe("Test.B", _ => b++);

        _host.TickSeconds(session, 0.2);
        Assert.Equal(0, a);
        Assert.True(b > 0, "selector never fell through to the second child");

        session.Flags.Write("go", true);
        _host.TickSeconds(session, 0.2);
        Assert.True(a > 0, "selector ignored the now-passing first child");
    }

    [Fact]
    public void ConditionGate_AbortsTheRunningSubtree()
    {
        var (session, _, _, agent) = SpawnBot("""
            { "node": "Selector", "children": [
                { "node": "ConditionGate",
                  "when": [ { "type": "FlagEquals", "flag": "alarm", "value": true } ],
                  "child": { "node": "Sequence", "children": [
                      { "task": "MoveTo", "target": [100, 0, 0], "speed": "walk" },
                      { "task": "PublishEvent", "event": "Test.Escaped" } ] } },
                { "task": "Wait", "seconds": 1 } ] }
            """);
        var escaped = 0;
        session.Events.Subscribe("Test.Escaped", _ => escaped++);

        session.Flags.Write("alarm", true);
        _host.TickSeconds(session, 0.3);
        Assert.True(agent.IsMoving, "gate open: the MoveTo should be running");

        session.Flags.Write("alarm", false);
        _host.TickSeconds(session, 0.1);
        Assert.False(agent.IsMoving, "failing gate must abort the running MoveTo");
        Assert.Equal(0, escaped);
        Assert.Contains(agent.Trace, t => t.Contains("subtree aborted"));
    }

    [Fact]
    public void Selector_PreemptsARunningBranchWhenAHigherPriorityOneBecomesViable()
    {
        var (session, _, _, agent) = SpawnBot("""
            { "node": "Selector", "children": [
                { "node": "ConditionGate",
                  "when": [ { "type": "FlagEquals", "flag": "urgent", "value": true } ],
                  "child": { "task": "MoveTo", "target": [100, 0, 0], "speed": "run" } },
                { "task": "Wait", "seconds": 5 } ] }
            """);

        _host.TickSeconds(session, 0.2);
        Assert.False(agent.IsMoving); // sitting in the long Wait

        session.Flags.Write("urgent", true);
        _host.TickSeconds(session, 0.2);

        Assert.True(agent.IsMoving, "the urgent branch should preempt the running Wait");
        Assert.Contains(agent.Trace, t => t.Contains("preempted"));
    }

    [Fact]
    public void Cooldown_BlocksReentryUntilElapsed()
    {
        var (session, _, _, _) = SpawnBot("""
            { "node": "Selector", "children": [
                { "node": "Cooldown", "seconds": 1.0,
                  "child": { "task": "PublishEvent", "event": "Test.Ping" } },
                { "task": "Wait", "seconds": 0.1 } ] }
            """);
        var pings = 0;
        session.Events.Subscribe("Test.Ping", _ => pings++);

        _host.TickSeconds(session, 0.5);
        Assert.Equal(1, pings); // fired once, then cooling down

        _host.TickSeconds(session, 0.8); // past the 1s cooldown
        Assert.Equal(2, pings);
    }

    [Fact]
    public void Inverter_FlipsItsConditionChild()
    {
        var (session, _, _, _) = SpawnBot("""
            { "node": "Sequence", "children": [
                { "node": "Inverter",
                  "child": { "condition": { "type": "FlagEquals", "flag": "x", "value": true } } },
                { "task": "PublishEvent", "event": "Test.NotX" } ] }
            """);
        var notX = 0;
        session.Events.Subscribe("Test.NotX", _ => notX++);

        _host.TickSeconds(session, 0.2);
        Assert.True(notX > 0, "inverted false condition should let the sequence continue");

        session.Flags.Write("x", true);
        _host.TickSeconds(session, 0.1); // let in-flight ticks settle
        var before = notX;
        _host.TickSeconds(session, 0.2);
        Assert.Equal(before, notX); // inverted true condition now blocks the sequence
    }

    [Fact]
    public void RepeatUntilFail_CompletesWhenTheChildFails()
    {
        var (session, _, _, _) = SpawnBot("""
            { "node": "Sequence", "children": [
                { "node": "RepeatUntilFail",
                  "child": { "condition": { "type": "FlagEquals", "flag": "keep_going", "value": true } } },
                { "task": "PublishEvent", "event": "Test.Stopped" } ] }
            """);
        var stopped = 0;
        session.Events.Subscribe("Test.Stopped", _ => stopped++);
        session.Flags.Write("keep_going", true);

        _host.TickSeconds(session, 0.3);
        Assert.Equal(0, stopped); // repeating forever while the flag holds

        session.Flags.Write("keep_going", false);
        _host.TickSeconds(session, 0.2);
        Assert.True(stopped > 0, "repeat-until-fail never completed");
    }

    [Fact]
    public void Subtree_ExpandsByReference()
    {
        var (session, _, _, agent) = SpawnBot(
            """{ "subtree": "btree_sub" }""",
            extraDefs: """
                { "id": "btree_sub", "type": "btree", "root": { "task": "PublishEvent", "event": "Test.Sub" } }
                """);
        var sub = 0;
        session.Events.Subscribe("Test.Sub", _ => sub++);

        _host.TickSeconds(session, 0.2);

        Assert.True(sub > 0, "subtree leaf never ran");
        Assert.Contains("Subtree btree_sub", ((BehaviorTreeBrain)agent.Brain).DescribeTree());
    }

    [Fact]
    public void ThinkInterval_ThrottlesBrainTicks()
    {
        var (session, _, _, _) = SpawnBot(
            """{ "task": "PublishEvent", "event": "Test.Think" }""", thinkInterval: 0.5);
        var thinks = 0;
        session.Events.Subscribe("Test.Think", _ => thinks++);

        _host.TickSeconds(session, 2.0); // 60 sim ticks

        Assert.InRange(thinks, 3, 5); // ~one think per 0.5s, not one per tick
    }

    [Fact]
    public void DescribeTree_ShowsPerNodeStatus()
    {
        var (session, _, _, agent) = SpawnBot("""
            { "node": "Sequence", "children": [
                { "task": "Wait", "seconds": 5 },
                { "task": "PublishEvent", "event": "Test.Never" } ] }
            """);

        _host.TickSeconds(session, 0.2);
        var dump = ((BehaviorTreeBrain)agent.Brain).DescribeTree();

        Assert.Contains("… Sequence", dump);
        Assert.Contains("… Wait", dump);
        Assert.Contains("· PublishEvent", dump); // never reached
    }
}
