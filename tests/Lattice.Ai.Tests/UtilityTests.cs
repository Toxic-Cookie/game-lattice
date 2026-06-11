using System.Numerics;
using System.Text.Json;
using Lattice.Ai.Agents;
using Lattice.Core.Simulation;
using Lattice.Rpg;
using Lattice.Rpg.Conditions;

namespace Lattice.Ai.Tests;

/// <summary>
/// Utility system (plan/04 §6): need decay, the Sims selector
/// (score = Σ urgency × satisfaction / cost), activity execution, and the
/// evaluator threshold conditions.
/// </summary>
public sealed class UtilityTests : IDisposable
{
    private readonly AiTestHost _host = new();

    public void Dispose() => _host.Dispose();

    /// <summary>A bot with two needs (decay 0 unless given) and drink/chat activities, brain = bare PerformActivity selector.</summary>
    private (GameSession Session, RpgRuntime Rpg, AiRuntime Ai, Entity Bot, AgentComponent Agent) SpawnBot(
        double thirstDecay = 0, string drinkConditions = "")
    {
        _host.WriteStandardContent();
        var conditions = drinkConditions.Length == 0 ? "" : $""", "conditions": [{drinkConditions}]""";
        _host.WriteContent("bots.json", $$"""
            [ { "id": "entity_bot", "type": "entity", "name": "Bot" },
              { "id": "need_a", "type": "need", "key": "ThirstA", "initial": 1.0, "decayPerSecond": {{thirstDecay}} },
              { "id": "need_b", "type": "need", "key": "SocialB", "initial": 1.0, "decayPerSecond": 0 },
              { "id": "activity_a_drink", "type": "activity", "satisfies": { "need_a": 0.7 }, "cost": "1"{{conditions}},
                "tasks": [ { "task": "Wait", "seconds": 0.2 } ] },
              { "id": "activity_b_chat", "type": "activity", "satisfies": { "need_b": 0.6 }, "cost": "1.2",
                "tasks": [ { "task": "Wait", "seconds": 0.2 } ] },
              { "id": "utility_test", "type": "utility",
                "factors": [ { "formula": "1 - ThirstA", "weight": 1 } ] },
              { "id": "profile_bot", "type": "agent", "entities": ["entity_bot"],
                "brain": "bt", "behaviorTree": "btree_bot",
                "needs": ["need_a", "need_b"], "activities": ["activity_a_drink", "activity_b_chat"] },
              { "id": "btree_bot", "type": "btree", "root": {
                  "node": "Selector", "children": [
                    { "task": "PerformActivity" },
                    { "task": "PublishEvent", "event": "Test.Idle" } ] } } ]
            """);
        var (session, rpg, ai) = _host.CreateLoadedSession();
        var bot = session.World.Spawn("entity_bot", Vector3.Zero);
        return (session, rpg, ai, bot, ai.GetAgent(bot)!);
    }

    [Fact]
    public void Needs_DecayTowardZero()
    {
        // bar closed: nothing can restore the decaying need
        var (session, _, _, _, agent) = SpawnBot(
            thirstDecay: 0.1,
            drinkConditions: """{ "type": "FlagEquals", "flag": "bar_open", "value": true }""");

        _host.TickSeconds(session, 2.0);

        Assert.InRange(agent.Needs["need_a"], 0.78, 0.82); // 1.0 − 0.1 × 2s
    }

    [Fact]
    public void Selector_ScoresUrgencyTimesSatisfactionOverCost()
    {
        var (_, _, ai, bot, agent) = SpawnBot();
        agent.Needs["need_a"] = 0.2; // urgency 0.8
        agent.Needs["need_b"] = 0.9; // urgency 0.1

        var scores = ai.ScoreActivities(bot).ToDictionary(s => s.Activity.Id, s => s);

        Assert.Equal(0.8 * 0.7 / 1.0, scores["activity_a_drink"].Score, 3);
        Assert.Equal(0.1 * 0.6 / 1.2, scores["activity_b_chat"].Score, 3);
        Assert.True(scores["activity_a_drink"].Score > scores["activity_b_chat"].Score);
    }

    [Fact]
    public void PerformActivity_RunsTheBestActivityAndRestoresTheNeed()
    {
        var (session, _, _, _, agent) = SpawnBot();
        agent.Needs["need_a"] = 0.3;

        _host.TickSeconds(session, 1.0);

        Assert.Equal(1.0, agent.Needs["need_a"], 3); // 0.3 + 0.7, clamped
        Assert.Contains(agent.Trace, t => t.Contains("activity activity_a_drink selected"));
        Assert.Contains(agent.Trace, t => t.Contains("activity activity_a_drink complete"));
    }

    [Fact]
    public void PerformActivity_FailsWhenNothingIsWorthDoing()
    {
        var (session, _, _, _, _) = SpawnBot(); // all needs fully satisfied, decay 0
        var idle = 0;
        session.Events.Subscribe("Test.Idle", _ => idle++);

        _host.TickSeconds(session, 0.3);

        Assert.True(idle > 0, "the BT selector should fall through when no activity scores > 0");
    }

    [Fact]
    public void IneligibleActivity_IsSkippedBySelection()
    {
        var (session, _, ai, bot, agent) = SpawnBot(
            drinkConditions: """{ "type": "FlagEquals", "flag": "bar_open", "value": true }""");
        agent.Needs["need_a"] = 0.0; // maximum thirst urgency
        agent.Needs["need_b"] = 0.5;

        var scores = ai.ScoreActivities(bot).ToDictionary(s => s.Activity.Id, s => s);
        Assert.False(scores["activity_a_drink"].Eligible);

        _host.TickSeconds(session, 1.0);
        Assert.Contains(agent.Trace, t => t.Contains("activity activity_b_chat selected"));
        Assert.DoesNotContain(agent.Trace, t => t.Contains("activity_a_drink selected"));

        session.Flags.Write("bar_open", true);
        Assert.True(ai.ScoreActivities(bot).Single(s => s.Activity.Id == "activity_a_drink").Eligible);
    }

    [Fact]
    public void UtilityAtLeast_GatesOnTheEvaluatorScore()
    {
        var (session, rpg, _, bot, agent) = SpawnBot();
        var payload = JsonDocument.Parse(
            """{ "type": "UtilityAtLeast", "evaluator": "utility_test", "threshold": 0.6 }""").RootElement;
        var ctx = new ConditionContext { Session = session, Rpg = rpg, Subject = bot };

        agent.Needs["need_a"] = 0.2; // score = 1 − 0.2 = 0.8
        Assert.True(rpg.Conditions.EvaluateOne(payload, ctx));

        agent.Needs["need_a"] = 0.9; // score = 0.1
        Assert.False(rpg.Conditions.EvaluateOne(payload, ctx));
    }

    [Fact]
    public void NeedBelow_ReadsTheAgentNeedValue()
    {
        var (session, rpg, _, bot, agent) = SpawnBot();
        var payload = JsonDocument.Parse(
            """{ "type": "NeedBelow", "need": "need_a", "threshold": 0.4 }""").RootElement;
        var ctx = new ConditionContext { Session = session, Rpg = rpg, Subject = bot };

        agent.Needs["need_a"] = 0.3;
        Assert.True(rpg.Conditions.EvaluateOne(payload, ctx));

        agent.Needs["need_a"] = 0.5;
        Assert.False(rpg.Conditions.EvaluateOne(payload, ctx));
    }

    [Fact]
    public void Scoreboard_ExplainsEveryCandidate()
    {
        var (_, _, ai, bot, agent) = SpawnBot();
        agent.Needs["need_a"] = 0.4;

        var scores = ai.ScoreActivities(bot);

        Assert.Equal(2, scores.Count);
        Assert.All(scores, s => Assert.False(string.IsNullOrEmpty(s.Breakdown)));
        Assert.Contains(scores, s => s.Breakdown.Contains("need_a"));
    }
}
