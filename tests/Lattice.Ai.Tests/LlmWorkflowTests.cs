using System.Numerics;
using Lattice.Core.Content;
using Lattice.Core.Simulation;
using Lattice.Narrative;

namespace Lattice.Ai.Tests;

/// <summary>
/// The M6 acceptance workflow (plan/06): content shaped like what an LLM
/// would author from manifest.md + schemas/ — a new spell item, a
/// blueprint-inheriting NPC, and a 2-step quest — hot-loads into a *running*
/// session and functions. No C# changes, no restart.
/// </summary>
public sealed class LlmWorkflowTests : IDisposable
{
    private readonly AiTestHost _host = new(watch: true);

    public void Dispose() => _host.Dispose();

    private static async Task PumpUntil(Func<bool> done, GameSession session, int maxMs = 5000)
    {
        var waited = 0;
        while (!done() && waited < maxMs)
        {
            session.AdvanceTick(1f / 30f);
            await Task.Delay(25);
            waited += 25;
        }

        Assert.True(done(), "hot reload never landed");
    }

    [Fact]
    public async Task LlmAuthoredContent_HotLoadsIntoARunningSessionAndFunctions()
    {
        _host.WriteStandardContent();
        var (session, rpg, ai) = _host.CreateLoadedSession();
        session.EnableHotReload(debounceSeconds: 0.05);
        var player = session.World.Spawn("entity_player", Vector3.Zero);
        Assert.False(session.Defs.Contains("item_frost_bomb"));

        // ── the "LLM output", authored against manifest + schemas only ──
        _host.WriteContent("llm-mod.json", """
            { "$schema": "../schemas/lattice.schema.json",
              "defs": [
                { "id": "item_frost_bomb", "type": "item", "name": "Frost Bomb",
                  "description": "Single-use cold burst around the target.",
                  "basePrice": 30, "consumeOnUse": true,
                  "useActions": [
                    { "type": "AreaDamage", "formula": "3d6 + 4", "radius": 4 },
                    { "type": "SetFlag", "flag": "frost_used", "value": true } ] },
                { "id": "entity_guard_captain", "type": "entity", "inherits": "entity_guard",
                  "description": "The watch captain: a tougher guard via blueprint.",
                  "name": "Guard Captain",
                  "stats": { "stat_con": 9 },
                  "tags": { "$append": ["captain"] } },
                { "id": "quest_initiation", "type": "quest", "name": "Initiation",
                  "description": "Prove yourself to the watch.",
                  "steps": [
                    { "id": "train", "description": "Finish training",
                      "complete": { "type": "FlagEquals", "flag": "training_done", "value": true } },
                    { "id": "report", "description": "Report to the captain",
                      "complete": { "type": "FlagEquals", "flag": "reported_in", "value": true },
                      "onComplete": [ { "type": "GiveItem", "item": "item_frost_bomb", "amount": "1" } ] } ] } ] }
            """);
        await PumpUntil(() => session.Defs.Contains("quest_initiation"), session);

        // 1. the blueprint NPC spawns with merged data
        var captain = session.World.Spawn("entity_guard_captain", new Vector3(60, 0, 60));
        Assert.Equal("Guard Captain", session.Defs.Get<EntityTemplateDef>("entity_guard_captain").Name);
        Assert.Contains("guard", captain.Tags);   // inherited
        Assert.Contains("captain", captain.Tags); // appended
        Assert.Equal(9 * 5 + 10, rpg.GetSheet(captain)!.Current("HP")); // overridden Con feeds the max formula

        // 2. the quest runs end to end
        var narrative = session.GetModule<NarrativeRuntime>()!;
        Assert.True(narrative.Quests.Start("quest_initiation"));
        session.Flags.Write("training_done", true);
        _host.TickSeconds(session, 0.2);
        Assert.Equal(QuestStatus.Active, narrative.Quests.GetStatus("quest_initiation"));
        Assert.Equal(1, narrative.Quests.GetStepIndex("quest_initiation"));

        session.Flags.Write("reported_in", true);
        _host.TickSeconds(session, 0.2);
        Assert.Equal(QuestStatus.Completed, narrative.Quests.GetStatus("quest_initiation"));
        Assert.Equal(1, rpg.CountItem(player, "item_frost_bomb")); // the reward

        // 3. the spell functions: the bomb hurts a bystander and consumes itself
        var victim = session.World.Spawn("entity_guard", player.Position + new Vector3(1, 0, 0));
        var hpBefore = rpg.GetSheet(victim)!.Current("HP");
        Assert.True(rpg.Inventory.TryUse(player, "item_frost_bomb", target: player, out var error), error);
        _host.TickSeconds(session, 0.1);

        Assert.True(rpg.GetSheet(victim)!.Current("HP") < hpBefore, "the area damage missed");
        Assert.Equal(true, session.Flags.Read("frost_used"));
        Assert.Equal(0, rpg.CountItem(player, "item_frost_bomb")); // consumed
    }
}
