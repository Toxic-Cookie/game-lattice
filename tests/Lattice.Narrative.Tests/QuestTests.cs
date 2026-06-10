using Lattice.Core.Events;
using Lattice.Core.Persistence;

namespace Lattice.Narrative.Tests;

public sealed class QuestTests : IDisposable
{
    private readonly NarrativeTestHost _host = new();

    public QuestTests() => _host.WriteStandardContent();

    public void Dispose() => _host.Dispose();

    [Fact]
    public void FullLifecycle_CountersStepsRewards()
    {
        var (session, rpg, narrative) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player"));
        var topics = new List<string>();
        session.Events.Subscribe("Quest*", e => topics.Add(e.Topic));

        Assert.True(narrative.Quests.Start("quest_wolves"));
        NarrativeTestHost.TickSeconds(session, 0.1);

        // kill three wolves through real combat
        for (var i = 0; i < 3; i++)
        {
            var wolf = session.World.Spawn("entity_wolf");
            session.Events.DispatchPending();
            rpg.Inventory.TryUse(player, "item_sword", wolf, out _);
            session.AdvanceTick(1f / 30f); // dispatch death + counter, check progress
        }

        Assert.Equal(3.0, session.Flags.ReadNumber("wolves_killed"));
        session.AdvanceTick(1f / 30f);
        Assert.Equal(1, narrative.Quests.GetStepIndex("quest_wolves")); // step 1 done, on "report"

        var goldBefore = rpg.CountItem(player, "item_gold");
        session.Flags.Write("reported_to_innkeeper", true);
        NarrativeTestHost.TickSeconds(session, 0.1);

        Assert.Equal(QuestStatus.Completed, narrative.Quests.GetStatus("quest_wolves"));
        Assert.Equal(goldBefore + 25, rpg.CountItem(player, "item_gold"));
        Assert.True(session.Flags.ReadBool("quest_wolves_done"));
        Assert.Contains("Quest.Started", topics);
        Assert.Contains("Quest.StepCompleted", topics);
        Assert.Contains("Quest.Completed", topics);
    }

    [Fact]
    public void Counter_IgnoresNonMatchingEvents_AndInactiveQuests()
    {
        var (session, _, narrative) = _host.CreateLoadedSession();

        // not started: kills don't count
        session.Events.Publish("Entity.Died", EventPayload.Of(("defId", "entity_wolf")));
        session.AdvanceTick(1f / 30f);
        Assert.Equal(0.0, session.Flags.ReadNumber("wolves_killed"));

        narrative.Quests.Start("quest_wolves");
        session.AdvanceTick(1f / 30f);

        // wrong defId doesn't count
        session.Events.Publish("Entity.Died", EventPayload.Of(("defId", "entity_chest")));
        session.AdvanceTick(1f / 30f);
        Assert.Equal(0.0, session.Flags.ReadNumber("wolves_killed"));

        session.Events.Publish("Entity.Died", EventPayload.Of(("defId", "entity_wolf")));
        session.AdvanceTick(1f / 30f);
        Assert.Equal(1.0, session.Flags.ReadNumber("wolves_killed"));
    }

    [Fact]
    public void Start_IsIdempotentAndRequiresDef()
    {
        var (_, _, narrative) = _host.CreateLoadedSession();

        Assert.True(narrative.Quests.Start("quest_wolves"));
        Assert.False(narrative.Quests.Start("quest_wolves")); // already active
        Assert.False(narrative.Quests.Start("quest_unknown"));
    }

    [Fact]
    public void SaveLoad_PreservesQuestProgressAndCounters()
    {
        var (session, _, narrative) = _host.CreateLoadedSession();
        narrative.Quests.Start("quest_wolves");
        session.Events.Publish("Entity.Died", EventPayload.Of(("defId", "entity_wolf")));
        session.AdvanceTick(1f / 30f);
        session.Events.Publish("Entity.Died", EventPayload.Of(("defId", "entity_wolf")));
        session.AdvanceTick(1f / 30f);

        var json = SaveManager.Capture(session);

        var (restored, _, restoredNarrative) = _host.CreateLoadedSession(boot: false);
        var report = SaveManager.Restore(restored, json);
        Assert.True(report.Ok, string.Join("; ", report.Errors));

        Assert.Equal(QuestStatus.Active, restoredNarrative.Quests.GetStatus("quest_wolves"));
        Assert.Equal(0, restoredNarrative.Quests.GetStepIndex("quest_wolves"));
        Assert.Equal(2.0, restored.Flags.ReadNumber("wolves_killed"));

        // the third kill after load completes the step
        restored.Events.Publish("Entity.Died", EventPayload.Of(("defId", "entity_wolf")));
        restored.AdvanceTick(1f / 30f);
        restored.AdvanceTick(1f / 30f);
        Assert.Equal(1, restoredNarrative.Quests.GetStepIndex("quest_wolves"));
    }
}
