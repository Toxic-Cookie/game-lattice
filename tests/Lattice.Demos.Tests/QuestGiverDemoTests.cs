using System.Text.Json;
using Lattice.Core.Persistence;
using Lattice.Core.Simulation;
using Lattice.Narrative;

namespace Lattice.Demos.Tests;

/// <summary>
/// Demo scene 3 — The Quest-Giver (plan/07 §3). The quest_wolves chain
/// end-to-end: accepted in Yarn dialogue, counted from Entity.Died events
/// on the bus, reported back for the reward — with a mid-quest save/load
/// proving the whole chain is persistence-safe.
/// </summary>
public sealed class QuestGiverDemoTests : IDisposable
{
    private readonly DemoSceneHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public void QuestWolves_FullLifecycle_WithMidQuestSaveLoad()
    {
        _host.Boot("lifecycle_quest");
        var player = _host.Single("entity_player");

        // ── accept: blackboard-gated Yarn branches drive StartQuest ──────
        Assert.True(_host.Narrative.Dialogue.StartYarn("Innkeeper", out var error), error);
        _host.Session.Events.DispatchPending();
        ChooseOption("wolf problem");
        AdvanceLines();
        ChooseOption("I'll do it");
        AdvanceLines();
        Assert.Equal(DialogueState.Ended, _host.Narrative.Dialogue.State);

        Assert.Equal(QuestStatus.Active, _host.Narrative.Quests.GetStatus("quest_wolves"));
        Assert.Contains(_host.Session.Events.Trace, e => e.Topic == "Quest.Started");
        Assert.Equal(0.0, _host.Session.Flags.ReadNumber("wolves_killed")); // counter pre-initialized

        // ── hunt: Entity.Died events drive the counter ───────────────────
        KillWolf(player);
        KillWolf(player);
        Assert.Equal(2.0, _host.Session.Flags.ReadNumber("wolves_killed"));

        // ── mid-quest save, then keep playing past the save point ────────
        var save = SaveManager.Capture(_host.Session);
        KillWolf(player);
        Assert.Equal(3.0, _host.Session.Flags.ReadNumber("wolves_killed"));

        // ── load: back to two kills, the third wolf alive again ──────────
        var report = SaveManager.Restore(_host.Session, save);
        Assert.True(report.Ok, string.Join("; ", report.Errors));
        Assert.Equal(QuestStatus.Active, _host.Narrative.Quests.GetStatus("quest_wolves"));
        Assert.Equal(0, _host.Narrative.Quests.GetStepIndex("quest_wolves"));
        Assert.Equal(2.0, _host.Session.Flags.ReadNumber("wolves_killed"));
        Assert.Single(_host.AllOf("entity_wolf"));

        // ── finish the hunt and report back ──────────────────────────────
        var restoredPlayer = _host.Single("entity_player");
        KillWolf(restoredPlayer);
        _host.TickSeconds(0.1); // step completion is evaluated per tick
        Assert.Equal(1, _host.Narrative.Quests.GetStepIndex("quest_wolves"));

        var goldBefore = _host.Rpg.Inventory.Count(restoredPlayer, "item_gold");
        Assert.True(_host.Narrative.Dialogue.StartYarn("Innkeeper", out error), error);
        _host.Session.Events.DispatchPending();
        ChooseOption("wolf problem");
        AdvanceLines(); // "Three wolves down!" → <<flag reported_to_innkeeper true>> → payout line
        _host.TickSeconds(0.1);

        Assert.Equal(QuestStatus.Completed, _host.Narrative.Quests.GetStatus("quest_wolves"));
        Assert.Equal(goldBefore + 25, _host.Rpg.Inventory.Count(restoredPlayer, "item_gold"));
        Assert.True(Assert.IsType<bool>(_host.Session.Flags.Read("quest_wolves_done")));

        // ── the event bus tells the whole story (plan/07 §3) ─────────────
        var topics = _host.Session.Events.Trace.Select(e => e.Topic).ToList();
        Assert.Contains("Quest.StepCompleted", topics);
        Assert.Contains("Quest.Completed", topics);
        Assert.Contains("Quest.WolvesDone", topics);
    }

    private void KillWolf(Entity killer)
    {
        var wolf = _host.AllOf("entity_wolf")[0];
        var killshot = JsonDocument.Parse("""[ { "type": "DealDamage", "formula": "999" } ]""");
        _host.Rpg.RunEffects(killshot.RootElement.EnumerateArray(), source: killer, target: wolf);
        _host.Session.Events.DispatchPending();
    }

    /// <summary>Advance through lines until options appear, then pick the one containing the text.</summary>
    private void ChooseOption(string containing)
    {
        AdvanceLines();
        Assert.Equal(DialogueState.Options, _host.Narrative.Dialogue.State);
        var pick = _host.Narrative.Dialogue.Options.First(
            o => o.Text.Contains(containing, StringComparison.OrdinalIgnoreCase));
        _host.Narrative.Dialogue.Choose(pick.Id);
        _host.Session.Events.DispatchPending();
    }

    private void AdvanceLines()
    {
        while (_host.Narrative.Dialogue.State == DialogueState.Line)
        {
            _host.Narrative.Dialogue.Advance();
            _host.Session.Events.DispatchPending();
        }
    }
}
