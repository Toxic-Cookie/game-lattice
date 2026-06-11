using System.Numerics;
using System.Text.Json;
using Lattice.Ai.Agents;
using Lattice.Rpg.Conditions;

namespace Lattice.Ai.Tests;

/// <summary>
/// M4b acceptance (plan/04): the tavern patron runs entirely on needs + BT
/// defined in JSON — drinks when thirsty, rests when tired — the utility
/// scoreboard explains every choice, and a perceived threat aborts whatever
/// the patron was doing.
/// </summary>
public sealed class TavernPatronTests : IDisposable
{
    private readonly AiTestHost _host = new();

    public TavernPatronTests() => _host.WriteStandardContent();

    public void Dispose() => _host.Dispose();

    [Fact]
    public void ThirstyPatron_WalksToTheBarAndDrinks()
    {
        var (session, _, ai) = _host.CreateLoadedSession();
        var patron = session.World.Spawn("entity_patron", new Vector3(0, 0, 0));
        var agent = ai.GetAgent(patron)!;
        agent.Needs["need_thirst"] = 0.1;
        agent.Needs["need_rest"] = 1.0;
        agent.Needs["need_social"] = 1.0;

        // the scoreboard explains the upcoming choice
        var scores = ai.ScoreActivities(patron);
        var best = scores.Where(s => s.Eligible).OrderByDescending(s => s.Score).First();
        Assert.Equal("activity_drink", best.Activity.Id);
        Assert.Contains("need_thirst", best.Breakdown);

        _host.TickSeconds(session, 4.0); // walk ~4.2 units at speed 2, then drink

        Assert.Contains(agent.Trace, t => t.Contains("activity activity_drink selected"));
        Assert.Contains(agent.Trace, t => t.Contains("activity activity_drink complete"));
        Assert.True(agent.Needs["need_thirst"] > 0.7, $"thirst was {agent.Needs["need_thirst"]:F2}");
        Assert.True(Vector3.Distance(patron.Position, new Vector3(3, 0, 3)) < 0.5, "patron should be at the bar");
    }

    [Fact]
    public void TiredPatron_PrefersTheChair()
    {
        var (session, _, ai) = _host.CreateLoadedSession();
        var patron = session.World.Spawn("entity_patron", new Vector3(0, 0, 0));
        var agent = ai.GetAgent(patron)!;
        agent.Needs["need_thirst"] = 1.0;
        agent.Needs["need_rest"] = 0.1;
        agent.Needs["need_social"] = 1.0;

        _host.TickSeconds(session, 4.0);

        Assert.Contains(agent.Trace, t => t.Contains("activity activity_rest selected"));
    }

    [Fact]
    public void SatisfiedPatron_LoitersInsteadOfGrindingActivities()
    {
        var (session, _, ai) = _host.CreateLoadedSession();
        var patron = session.World.Spawn("entity_patron", new Vector3(0, 0, 0));
        var agent = ai.GetAgent(patron)!;
        agent.Needs["need_thirst"] = 1.0;
        agent.Needs["need_rest"] = 1.0;
        agent.Needs["need_social"] = 1.0;

        _host.TickSeconds(session, 1.0);

        // the UtilityAtLeast gate (motivation < threshold) routes to the loiter subtree
        Assert.DoesNotContain(agent.Trace, t => t.Contains("selected (score"));
        var dump = ((BehaviorTreeBrain)agent.Brain).DescribeTree();
        Assert.Contains("Subtree bt_loiter", dump);
    }

    [Fact]
    public void AgentMetaCondition_ReadsTheMetaState()
    {
        var (session, rpg, ai) = _host.CreateLoadedSession();
        var patron = session.World.Spawn("entity_patron", new Vector3(0, 0, 0));
        var agent = ai.GetAgent(patron)!;
        var payload = JsonDocument.Parse("""{ "type": "AgentMeta", "is": "Alert" }""").RootElement;
        var ctx = new ConditionContext { Session = session, Rpg = rpg, Subject = patron };

        Assert.False(rpg.Conditions.EvaluateOne(payload, ctx));

        agent.Meta = MetaState.Alert;
        Assert.True(rpg.Conditions.EvaluateOne(payload, ctx));
    }

    [Fact]
    public void Threat_AbortsTheActivityAndThePatronFlees()
    {
        var (session, _, ai) = _host.CreateLoadedSession();
        var patron = session.World.Spawn("entity_patron", new Vector3(0, 0, 0));
        var agent = ai.GetAgent(patron)!;
        agent.Needs["need_thirst"] = 0.1;

        _host.TickSeconds(session, 0.5); // mid-walk to the bar
        Assert.Contains(agent.Trace, t => t.Contains("activity activity_drink selected"));

        // a hostile walks in; sensitivity 0.6 yields THREAT_KNOWN (partial confidence)
        session.World.Spawn("entity_player", new Vector3(1, 0, 0));
        _host.TickSeconds(session, 1.0);

        Assert.Contains(agent.Trace, t => t.Contains("preempted"));
        var toSafety = Vector3.Distance(patron.Position, new Vector3(12, 0, 12));
        Assert.True(toSafety < 16, "patron should be heading for the door");
        Assert.True(patron.Position.X > 1.5 || patron.Position.Z > 1.5, $"patron didn't flee: {patron.Position}");
    }
}
