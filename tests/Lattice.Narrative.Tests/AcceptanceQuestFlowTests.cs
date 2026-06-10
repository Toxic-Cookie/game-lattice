namespace Lattice.Narrative.Tests;

/// <summary>
/// The M3 acceptance scenario (plan/03): accept a quest in Yarn dialogue,
/// kill wolves (event-driven counter), report back through dialogue
/// branches that read live world state, collect the reward, and loot a
/// condition-gated chest — with a save/load mid-quest. Zero scenario C#.
/// </summary>
public sealed class AcceptanceQuestFlowTests : IDisposable
{
    private readonly NarrativeTestHost _host = new(seed: 99);

    public AcceptanceQuestFlowTests()
    {
        _host.WriteStandardContent();
        _host.WriteContent("dialogue/innkeeper.yarn", """
            title: Innkeeper
            ---
            Innkeeper: Welcome to the Wolf's Rest.
            <<if flag_bool("quest_wolves_done")>>
                Innkeeper: Thanks again for handling those wolves.
            <<elseif quest_active("quest_wolves")>>
                <<if flag_number("wolves_killed") >= 3>>
                    Innkeeper: Three wolves down! Here's your pay.
                    <<flag reported_to_innkeeper true>>
                <<else>>
                    Innkeeper: Still wolves out there.
                <<endif>>
            <<else>>
                Innkeeper: Cull three wolves and I'll pay you 25 gold.
                -> I'll do it.
                    <<start_quest quest_wolves>>
                    Innkeeper: Good hunting.
                -> Not my problem.
                    Innkeeper: Suit yourself.
            <<endif>>
            ===
            """);
    }

    public void Dispose() => _host.Dispose();

    [Fact]
    public void FullQuestLoop_ThroughDialogueCombatAndChest()
    {
        var (session, rpg, narrative) = _host.CreateLoadedSession();
        var player = session.World.All.Single(e => e.Tags.Contains("player"));
        var dialogue = narrative.Dialogue;

        // --- accept the quest via dialogue
        Assert.True(dialogue.StartYarn("Innkeeper", out _));
        dialogue.Advance(); // "Welcome" -> offer line
        Assert.Contains("Cull three wolves", dialogue.Line);
        dialogue.Advance();
        Assert.Equal(DialogueState.Options, dialogue.State);
        dialogue.Choose(dialogue.Options[0].Id); // "I'll do it."
        Assert.Equal("Good hunting.", dialogue.Line);
        dialogue.Advance();
        Assert.Equal(DialogueState.Ended, dialogue.State);
        Assert.Equal(QuestStatus.Active, narrative.Quests.GetStatus("quest_wolves"));

        // --- mid-quest revisit shows the in-progress branch
        dialogue.StartYarn("Innkeeper", out _);
        dialogue.Advance();
        Assert.Equal("Still wolves out there.", dialogue.Line);
        dialogue.Stop();

        // --- kill three wolves (real combat -> Entity.Died -> counter)
        for (var i = 0; i < 3; i++)
        {
            var wolf = session.World.Spawn("entity_wolf");
            session.Events.DispatchPending();
            rpg.Inventory.TryUse(player, "item_sword", wolf, out _);
            session.AdvanceTick(1f / 30f);
        }

        session.AdvanceTick(1f / 30f);
        Assert.Equal(1, narrative.Quests.GetStepIndex("quest_wolves"));

        // --- save/load mid-quest
        var save = Lattice.Core.Persistence.SaveManager.Capture(session);
        var (restored, restoredRpg, restoredNarrative) = _host.CreateLoadedSession(boot: false);
        Assert.True(Lattice.Core.Persistence.SaveManager.Restore(restored, save).Ok);
        var restoredPlayer = restored.World.All.Single(e => e.Tags.Contains("player"));
        var goldBefore = restoredRpg.CountItem(restoredPlayer, "item_gold");

        // --- report back: dialogue branch reads the counter, sets the flag
        Assert.True(restoredNarrative.Dialogue.StartYarn("Innkeeper", out _));
        restoredNarrative.Dialogue.Advance();
        Assert.Contains("Three wolves down", restoredNarrative.Dialogue.Line);
        restoredNarrative.Dialogue.Advance();
        Assert.Equal(DialogueState.Ended, restoredNarrative.Dialogue.State);

        NarrativeTestHost.TickSeconds(restored, 0.1); // quest system completes + rewards

        Assert.Equal(QuestStatus.Completed, restoredNarrative.Quests.GetStatus("quest_wolves"));
        Assert.Equal(goldBefore + 25, restoredRpg.CountItem(restoredPlayer, "item_gold"));
        Assert.True(restored.Flags.ReadBool("quest_wolves_done"));

        // --- completed branch on the next visit
        restoredNarrative.Dialogue.StartYarn("Innkeeper", out _);
        restoredNarrative.Dialogue.Advance();
        Assert.Contains("Thanks again", restoredNarrative.Dialogue.Line);
        restoredNarrative.Dialogue.Stop();

        // --- chest: gated one-time loot
        var chest = restored.World.All.Single(e => e.DefId == "entity_chest");
        Assert.True(restoredNarrative.Interactions.TryInteract(restoredPlayer, chest, "open", out _));
        Assert.False(restoredNarrative.Interactions.TryInteract(restoredPlayer, chest, "open", out _));
    }
}
