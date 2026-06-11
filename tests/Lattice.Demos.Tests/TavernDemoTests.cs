using System.Text;
using Lattice.Ai.Agents;
using Lattice.Core.Events;
using Lattice.Narrative;
using Lattice.Rpg.Defs;

namespace Lattice.Demos.Tests;

/// <summary>
/// Demo scene 1 — The Tavern (plan/07 §1). Exercises Yarn dialogue, trade
/// with Charisma pricing, the day-night schedule flip, needs-based utility
/// patrons, meta player awareness, and weather→narrative coupling — all on
/// the shipped content, deterministic under the fixed seed.
/// </summary>
public sealed class TavernDemoTests : IDisposable
{
    private readonly DemoSceneHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public void FullDayCycle_InnkeeperRoutineFlips_And_PatronsCompleteNeedLoops()
    {
        _host.Boot("lifecycle_tavern");
        var innkeeper = _host.Ai.GetAgent(_host.Single("entity_innkeeper"))!;
        var brain = Assert.IsType<ScheduleBrain>(innkeeper.Brain);
        var patrons = _host.AllOf("entity_patron");
        Assert.Equal(3, patrons.Count);

        // sample once per real second across one full game day (24 h × 30 s)
        var schedulesByPhase = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var needLoops = patrons.ToDictionary(p => p.InstanceId, _ => 0, StringComparer.Ordinal);
        var lastThirst = patrons.ToDictionary(p => p.InstanceId, _ => double.NaN, StringComparer.Ordinal);

        for (var second = 0; second < 24 * 30; second++)
        {
            _host.TickSeconds(1.0);

            var phase = _host.World.PhaseName ?? "?";
            if (brain.CurrentScheduleId is { } scheduleId)
            {
                if (!schedulesByPhase.TryGetValue(phase, out var set))
                {
                    schedulesByPhase[phase] = set = new HashSet<string>(StringComparer.Ordinal);
                }

                set.Add(scheduleId);
            }

            foreach (var patron in patrons)
            {
                // a completed drink applies its satisfy amount in one step —
                // a rise this large only happens via an activity completion
                var thirst = _host.Ai.GetAgent(patron)!.Needs["need_thirst"];
                if (!double.IsNaN(lastThirst[patron.InstanceId]) && thirst - lastThirst[patron.InstanceId] > 0.3)
                {
                    needLoops[patron.InstanceId]++;
                }

                lastThirst[patron.InstanceId] = thirst;
            }
        }

        Assert.Equal(2, _host.World.Day); // a full day elapsed
        Assert.Contains("schedule_tend_bar", schedulesByPhase["day"]);
        Assert.Contains("schedule_innkeeper_sweep", schedulesByPhase["dusk"]);
        Assert.Contains("schedule_innkeeper_bed", schedulesByPhase["night"]);

        foreach (var patron in patrons)
        {
            Assert.True(needLoops[patron.InstanceId] >= 1,
                $"{patron.InstanceId} completed no full need loop over the day");
        }
    }

    [Fact]
    public void Trade_CharismaLowersPrices_And_TheBarClosesAtNight()
    {
        _host.Boot("lifecycle_tavern");
        var shop = _host.Session.Defs.Get<ShopDef>("shop_innkeeper");
        var ale = _host.Session.Defs.Get<ItemDef>("item_ale");
        var player = _host.Single("entity_player");
        var patron = _host.AllOf("entity_patron")[0];

        // Charisma pricing (plan/02 §6): the smooth talker pays less at the same
        // bar (compared on the potion — ale is too cheap to survive rounding)
        var potion = _host.Session.Defs.Get<ItemDef>("item_healing_potion");
        var playerPrice = _host.Rpg.Trade.GetBuyPrice(shop, potion, player);
        var patronPrice = _host.Rpg.Trade.GetBuyPrice(shop, potion, patron);
        Assert.True(playerPrice < patronPrice, $"player {playerPrice} should beat patron {patronPrice}");

        var alePrice = _host.Rpg.Trade.GetBuyPrice(shop, ale, player);
        var goldBefore = _host.Rpg.Inventory.Count(player, "item_gold");
        Assert.True(_host.Rpg.Trade.TryBuy(shop, player, "item_ale", out var error), error);
        Assert.Equal(goldBefore - alePrice, _host.Rpg.Inventory.Count(player, "item_gold"));
        Assert.Equal(1, _host.Rpg.Inventory.Count(player, "item_ale"));

        // openWhen reads the is_night day-phase flag — closed is data, not code
        _host.Session.Flags.Write("is_night", true);
        Assert.False(_host.Rpg.Trade.TryBuy(shop, player, "item_ale", out error));
        Assert.NotNull(error);
    }

    [Fact]
    public void TavernTranscript_MatchesGoldenFile()
    {
        _host.Boot("lifecycle_tavern");
        var innkeeperEntity = _host.Single("entity_innkeeper");
        var innkeeper = _host.Ai.GetAgent(innkeeperEntity)!;
        var transcript = new StringBuilder();

        // ── beat 1: blackboard-gated Yarn dialogue, order an ale ─────────
        Assert.True(_host.Narrative.Dialogue.StartYarn("Innkeeper", out var startError), startError);
        _host.Session.Events.DispatchPending();
        PlayDialogue(transcript, chooseContaining: "drink");

        // ── beat 2: trade at the bar ─────────────────────────────────────
        var shop = _host.Session.Defs.Get<ShopDef>("shop_innkeeper");
        var ale = _host.Session.Defs.Get<ItemDef>("item_ale");
        var player = _host.Single("entity_player");
        var price = _host.Rpg.Trade.GetBuyPrice(shop, ale, player);
        Assert.True(_host.Rpg.Trade.TryBuy(shop, player, "item_ale", out var buyError), buyError);
        transcript.AppendLine($"[bought item_ale for {price} gold]");

        // ── beat 3: meta awareness — look away twice mid-conversation ────
        Assert.True(_host.Narrative.Dialogue.StartYarn("Innkeeper", out startError), startError);
        _host.Session.Events.DispatchPending();
        transcript.AppendLine($"{_host.Narrative.Dialogue.Speaker}: {_host.Narrative.Dialogue.Line}");

        for (var i = 0; i < 2; i++)
        {
            _host.Session.Events.Publish("Player.LookedAway",
                EventPayload.Of(("agentId", innkeeperEntity.InstanceId)));
            _host.Session.Events.DispatchPending();
        }

        _host.TickSeconds(1.0);
        Assert.True(innkeeper.Conditions.IsSet(innkeeper.Catalog, "ANNOYED"), "look-away meta-sensor should set ANNOYED");
        Assert.Equal("schedule_innkeeper_scold", ((ScheduleBrain)innkeeper.Brain).CurrentScheduleId);
        Assert.Contains(_host.Session.Events.Trace, e => e.Topic == "Npc.Scold");

        // the host's reaction to Npc.Scold: the NPC broke off the conversation
        _host.Narrative.Dialogue.Stop();
        transcript.AppendLine("[the innkeeper notices you looking away and breaks off the conversation]");

        // ── beat 4: rain degrades hearing; a patron comments ─────────────
        var hours = 0.0;
        while (_host.World.WeatherId != "weather_rain" && hours < 72)
        {
            _host.TickGameHours(0.5);
            hours += 0.5;
        }

        Assert.Equal("weather_rain", _host.World.WeatherId);
        Assert.Equal(0.5, Assert.IsType<double>(_host.Session.Flags.Read("sense_auditory_mult")));

        Assert.True(_host.Narrative.Dialogue.StartYarn("PatronRain", out startError), startError);
        _host.Session.Events.DispatchPending();
        PlayDialogue(transcript, chooseContaining: null);

        // ── golden compare ───────────────────────────────────────────────
        var goldenPath = Path.Combine(DemoSceneHost.RepoRoot, "tests", "Lattice.Demos.Tests", "golden", "tavern-transcript.txt");
        if (!File.Exists(goldenPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            File.WriteAllText(goldenPath, transcript.ToString().ReplaceLineEndings("\n"));
            Assert.Fail($"golden file bootstrapped at {goldenPath} — review it and re-run");
        }

        var expected = File.ReadAllText(goldenPath).ReplaceLineEndings("\n").TrimEnd();
        Assert.Equal(expected, transcript.ToString().ReplaceLineEndings("\n").TrimEnd());
    }

    /// <summary>Drive the pull-based dialogue runner to the end, recording every line and option.</summary>
    private void PlayDialogue(StringBuilder transcript, string? chooseContaining)
    {
        var guard = 0;
        while (guard++ < 50)
        {
            switch (_host.Narrative.Dialogue.State)
            {
                case DialogueState.Line:
                    var speaker = _host.Narrative.Dialogue.Speaker is { } s ? $"{s}: " : "";
                    transcript.AppendLine($"{speaker}{_host.Narrative.Dialogue.Line}");
                    _host.Narrative.Dialogue.Advance();
                    _host.Session.Events.DispatchPending();
                    break;

                case DialogueState.Options:
                    var options = _host.Narrative.Dialogue.Options;
                    foreach (var option in options)
                    {
                        transcript.AppendLine($"  > {option.Text}");
                    }

                    var pick = chooseContaining is not null
                        ? options.First(o => o.Text.Contains(chooseContaining, StringComparison.OrdinalIgnoreCase))
                        : options[0];
                    transcript.AppendLine($"[chose: {pick.Text}]");
                    _host.Narrative.Dialogue.Choose(pick.Id);
                    _host.Session.Events.DispatchPending();
                    break;

                default:
                    return;
            }
        }

        Assert.Fail("dialogue did not finish within the step guard");
    }
}
