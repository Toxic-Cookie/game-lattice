using Lattice.Ai.Defs;
using Lattice.Ai.Tasks;
using Lattice.Core.Content;
using Lattice.Core.Formulas;
using Lattice.Rpg.Conditions;

namespace Lattice.Ai;

/// <summary>
/// AI validation rules (plan/04): catalog budgets, profile structure,
/// schedule condition names checked against each using profile's catalog,
/// task payloads, FSM state graph integrity.
/// </summary>
public sealed class AiContentValidator(ConditionRegistry conditions, TaskRegistry tasks) : IContentValidator
{
    private static readonly string[] ValidMetaStates = ["Idle", "Alert"];
    private static readonly string[] ValidBrains = ["fsm", "schedules"];

    public void Validate(DefRegistry registry, ContentLoadReport report, IFormulaEngine formulas)
    {
        foreach (var catalog in registry.All<ConditionCatalogDef>())
        {
            if (catalog.Names.Count > 32)
            {
                report.Errors.Add($"Condition catalog '{catalog.Id}' declares {catalog.Names.Count} conditions; the budget is 32 (and that's a feature).");
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in catalog.Names.Where(n => !seen.Add(n)))
            {
                report.Errors.Add($"Condition catalog '{catalog.Id}' declares '{name}' more than once.");
            }
        }

        foreach (var profile in registry.All<AgentProfileDef>())
        {
            ValidateProfile(profile, registry, report, formulas);
        }

        foreach (var brain in registry.All<FsmBrainDef>())
        {
            ValidateFsmBrain(brain, registry, report, formulas);
        }

        foreach (var schedule in registry.All<ScheduleDef>())
        {
            tasks.ValidateList(schedule.Tasks, $"{schedule.Id}.tasks", registry, formulas, report);
            foreach (var meta in schedule.MetaStates ?? [])
            {
                if (!ValidMetaStates.Contains(meta))
                {
                    report.Errors.Add($"Schedule '{schedule.Id}' has unknown meta state '{meta}'.");
                }
            }
        }
    }

    private void ValidateProfile(AgentProfileDef profile, DefRegistry registry, ContentLoadReport report, IFormulaEngine formulas)
    {
        if (!ValidBrains.Contains(profile.Brain))
        {
            report.Errors.Add($"Agent profile '{profile.Id}' has unknown brain tier '{profile.Brain}' (M4a supports: {string.Join(", ", ValidBrains)}).");
        }

        if (profile.Brain == "fsm" && profile.FsmBrain is null)
        {
            report.Errors.Add($"Agent profile '{profile.Id}' uses brain 'fsm' but declares no 'fsmBrain'.");
        }

        if (profile.Brain == "schedules" && (profile.Schedules is not { Count: > 0 }))
        {
            report.Errors.Add($"Agent profile '{profile.Id}' uses brain 'schedules' but declares no 'schedules'.");
        }

        foreach (var entityDef in profile.Entities)
        {
            if (registry.Contains(entityDef) && !registry.TryGet<EntityTemplateDef>(entityDef, out _))
            {
                report.Errors.Add($"Agent profile '{profile.Id}' entity '{entityDef}' is not an entity template.");
            }
        }

        foreach (var sensor in profile.Sensors ?? [])
        {
            if (sensor.Kind is not ("visual" or "auditory" or "smell" or "proximity"))
            {
                report.Errors.Add($"Agent profile '{profile.Id}' has unknown sensor kind '{sensor.Kind}'.");
            }
        }

        foreach (var point in profile.PatrolPoints ?? [])
        {
            if (point.Length != 3)
            {
                report.Errors.Add($"Agent profile '{profile.Id}' has a patrol point that is not [x, y, z].");
            }
        }

        // schedule condition names must exist in this profile's catalog
        if (!registry.TryGet<ConditionCatalogDef>(profile.Conditions, out var catalog))
        {
            return; // dangling ref already reported by the link pass
        }

        var names = new HashSet<string>(catalog.Names, StringComparer.Ordinal);
        foreach (var scheduleId in profile.Schedules ?? [])
        {
            if (!registry.TryGet<ScheduleDef>(scheduleId, out var schedule))
            {
                continue;
            }

            foreach (var condition in (schedule.Require ?? []).Concat(schedule.Interrupt ?? []))
            {
                if (!names.Contains(condition))
                {
                    report.Errors.Add(
                        $"Schedule '{schedule.Id}' (used by '{profile.Id}') references condition '{condition}' missing from catalog '{catalog.Id}'.");
                }
            }
        }
    }

    private void ValidateFsmBrain(FsmBrainDef brain, DefRegistry registry, ContentLoadReport report, IFormulaEngine formulas)
    {
        if (!brain.States.ContainsKey(brain.Initial))
        {
            report.Errors.Add($"FSM brain '{brain.Id}' initial state '{brain.Initial}' does not exist.");
        }

        foreach (var pair in brain.States)
        {
            foreach (var transition in pair.Value.Transitions ?? [])
            {
                if (!brain.States.ContainsKey(transition.To))
                {
                    report.Errors.Add($"FSM brain '{brain.Id}' state '{pair.Key}' transitions to missing state '{transition.To}'.");
                }

                conditions.ValidateList(transition.When, $"{brain.Id}.{pair.Key}", registry, formulas, report);
            }

            if (pair.Value.Steering is { } steering
                && Rpg.Effects.JsonArgs.TryGetString(steering, "type", out var steeringType)
                && steeringType is not ("Idle" or "Wander" or "FleeFrom" or "MoveTo"))
            {
                report.Errors.Add($"FSM brain '{brain.Id}' state '{pair.Key}' has unknown steering type '{steeringType}'.");
            }
        }
    }
}
