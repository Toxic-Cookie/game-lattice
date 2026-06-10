using System.Text.Json;
using Lattice.Core.Content;
using Lattice.Core.Events;
using Lattice.Core.Formulas;
using Lattice.Core.Hosting;
using Lattice.Core.Simulation;
using Lattice.Narrative.Defs;
using Lattice.Rpg.Conditions;
using Lattice.Rpg.Effects;

namespace Lattice.Narrative;

public enum QuestStatus
{
    Available,
    Active,
    Completed,
}

/// <summary>
/// Quest progression (plan/03 §4). Counters are event-driven (the current
/// step's <c>count</c> spec listens on the bus and increments a global
/// blackboard counter); step completion conditions are evaluated once per
/// tick against the player + flags scope.
/// </summary>
public sealed class QuestService
{
    private readonly NarrativeRuntime _narrative;
    private readonly Dictionary<string, QuestState> _states = new(StringComparer.Ordinal);

    internal QuestService(NarrativeRuntime narrative)
    {
        _narrative = narrative;
    }

    public QuestStatus GetStatus(string questId)
        => _states.TryGetValue(questId, out var state) ? state.Status : QuestStatus.Available;

    public int GetStepIndex(string questId)
        => _states.TryGetValue(questId, out var state) ? state.StepIndex : 0;

    public IEnumerable<(string QuestId, QuestStatus Status, int StepIndex)> All
        => _states.Select(p => (p.Key, p.Value.Status, p.Value.StepIndex));

    public bool Start(string questId)
    {
        if (GetStatus(questId) != QuestStatus.Available
            || !_narrative.Session.Defs.TryGet<QuestDef>(questId, out var def))
        {
            return false;
        }

        // counters declared by steps exist from the start, so completion
        // formulas can reference them before the first increment
        foreach (var step in def.Steps)
        {
            if (step.Count is { Counter.Length: > 0 } count && !_narrative.Session.Flags.HasKey(count.Counter))
            {
                _narrative.Session.Flags.Write(count.Counter, 0.0);
            }
        }

        _states[questId] = new QuestState { Status = QuestStatus.Active, StepIndex = 0 };
        _narrative.Session.Events.Publish("Quest.Started", EventPayload.Of(("quest", questId)), _narrative.Session.Tick);
        return true;
    }

    /// <summary>Event-driven counters: called for every dispatched event.</summary>
    internal void OnAnyEvent(GameEvent evt)
    {
        foreach (var pair in _states)
        {
            if (pair.Value.Status != QuestStatus.Active
                || !_narrative.Session.Defs.TryGet<QuestDef>(pair.Key, out var def)
                || pair.Value.StepIndex >= def.Steps.Count)
            {
                continue;
            }

            var count = def.Steps[pair.Value.StepIndex].Count;
            if (count is null || count.On != evt.Topic || !WhereMatches(count.Where, evt.Payload))
            {
                continue;
            }

            var flags = _narrative.Session.Flags;
            flags.Write(count.Counter, flags.ReadNumber(count.Counter) + count.Amount);
        }
    }

    /// <summary>Evaluate completion conditions for all active quests (ticked by the narrative system).</summary>
    internal void CheckProgress()
    {
        foreach (var questId in _states.Keys.ToList())
        {
            var state = _states[questId];
            if (state.Status != QuestStatus.Active
                || !_narrative.Session.Defs.TryGet<QuestDef>(questId, out var def))
            {
                continue;
            }

            // a completing step may immediately satisfy the next; bounded by step count
            var guard = def.Steps.Count + 1;
            while (state.Status == QuestStatus.Active && guard-- > 0)
            {
                if (state.StepIndex >= def.Steps.Count)
                {
                    CompleteQuest(questId, def, state);
                    break;
                }

                var step = def.Steps[state.StepIndex];
                if (step.Complete is null || !EvaluateCondition(step.Complete.Value))
                {
                    break;
                }

                _narrative.Rpg.RunEffects(step.OnComplete, source: null, target: _narrative.Dialogue.Player);
                _narrative.Session.Events.Publish("Quest.StepCompleted", EventPayload.Of(
                    ("quest", questId), ("step", step.Id)), _narrative.Session.Tick);
                state.StepIndex++;

                if (state.StepIndex >= def.Steps.Count)
                {
                    CompleteQuest(questId, def, state);
                }
            }
        }
    }

    internal IReadOnlyDictionary<string, QuestState> Export() => _states;

    internal void RestoreState(string questId, QuestStatus status, int stepIndex)
        => _states[questId] = new QuestState { Status = status, StepIndex = stepIndex };

    internal void ClearStates() => _states.Clear();

    private void CompleteQuest(string questId, QuestDef def, QuestState state)
    {
        state.Status = QuestStatus.Completed;
        _narrative.Rpg.RunEffects(def.OnComplete, source: null, target: _narrative.Dialogue.Player);
        _narrative.Session.Events.Publish("Quest.Completed", EventPayload.Of(("quest", questId)), _narrative.Session.Tick);
    }

    private bool EvaluateCondition(JsonElement condition)
    {
        try
        {
            return _narrative.Rpg.Conditions.EvaluateOne(condition, new ConditionContext
            {
                Session = _narrative.Session,
                Rpg = _narrative.Rpg,
                Subject = _narrative.Dialogue.Player,
            });
        }
        catch (FormulaException ex)
        {
            // a step condition referencing not-yet-existing state is "not yet complete", not a crash
            _narrative.Session.Services.Host.Logger.Debug($"Quest condition not evaluable yet: {ex.Message}");
            return false;
        }
    }

    private static bool WhereMatches(Dictionary<string, JsonElement>? where, IReadOnlyDictionary<string, object?> payload)
    {
        foreach (var filter in where ?? [])
        {
            if (!payload.TryGetValue(filter.Key, out var actual)
                || !JsonValueHelper.TryToPlain(filter.Value, out var expected)
                || !Equals(actual, expected))
            {
                return false;
            }
        }

        return true;
    }

    internal sealed class QuestState
    {
        public QuestStatus Status { get; set; }

        public int StepIndex { get; set; }
    }
}

/// <summary>Effect primitive starting a quest — usable from dialogue, items, or interactions.</summary>
public sealed class StartQuestEffect : IEffectExecutor
{
    public string Type => "StartQuest";

    public void Execute(EffectContext ctx, JsonElement args)
    {
        var narrative = ctx.Session.GetModule<NarrativeRuntime>();
        if (narrative is null)
        {
            ctx.Session.Services.Host.Logger.Error("StartQuest effect requires the Narrative module.");
            return;
        }

        narrative.Quests.Start(JsonArgs.GetString(args, "quest"));
    }

    public void Validate(JsonElement args, EffectValidationContext v) => v.RequireDef<QuestDef>(args, "quest");
}
