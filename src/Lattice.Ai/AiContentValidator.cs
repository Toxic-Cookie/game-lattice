using System.Text.Json;
using Lattice.Ai.Defs;
using Lattice.Ai.Tasks;
using Lattice.Core.Content;
using Lattice.Core.Formulas;
using Lattice.Rpg.Conditions;
using Lattice.Rpg.Effects;

namespace Lattice.Ai;

/// <summary>
/// AI validation rules (plan/04): catalog budgets, profile structure,
/// schedule condition names checked against each using profile's catalog,
/// task payloads, FSM state graph integrity, behavior-tree node graphs
/// (including subtree cycles), the utility vocabulary, and the GOAP
/// action/goal/cost-profile vocabulary.
/// </summary>
public sealed class AiContentValidator(ConditionRegistry conditions, TaskRegistry tasks, EffectRegistry? effects = null) : IContentValidator
{
    private static readonly string[] ValidMetaStates = ["Idle", "Alert"];
    private static readonly string[] ValidBrains = ["fsm", "schedules", "bt", "goap", "htn"];

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

        foreach (var tree in registry.All<BehaviorTreeDef>())
        {
            ValidateBehaviorTree(tree, registry, report, formulas);
        }

        foreach (var evaluator in registry.All<UtilityEvaluatorDef>())
        {
            ValidateUtilityEvaluator(evaluator, report, formulas);
        }

        foreach (var need in registry.All<NeedDef>())
        {
            ValidateNeed(need, report);
        }

        foreach (var activity in registry.All<ActivityDef>())
        {
            ValidateActivity(activity, registry, report, formulas);
        }

        foreach (var action in registry.All<GoapActionDef>())
        {
            ValidateGoapAction(action, registry, report, formulas);
        }

        foreach (var goal in registry.All<GoapGoalDef>())
        {
            if (goal.Desired.Count == 0)
            {
                report.Errors.Add($"GOAP goal '{goal.Id}' has an empty 'desired' state.");
            }

            if (!formulas.TryParse(goal.Priority, out var error))
            {
                report.Errors.Add($"GOAP goal '{goal.Id}' priority formula: {error}");
            }
        }

        foreach (var costProfile in registry.All<CostProfileDef>())
        {
            foreach (var pair in costProfile.Overrides)
            {
                if (registry.Contains(pair.Key) && !registry.TryGet<GoapActionDef>(pair.Key, out _))
                {
                    report.Errors.Add($"Cost profile '{costProfile.Id}' overrides '{pair.Key}', which is not a GOAP action.");
                }

                if (!formulas.TryParse(pair.Value, out var error))
                {
                    report.Errors.Add($"Cost profile '{costProfile.Id}' override for '{pair.Key}': {error}");
                }
            }
        }

        foreach (var compound in registry.All<HtnCompoundDef>())
        {
            if (compound.Methods.Count == 0)
            {
                report.Errors.Add($"HTN compound '{compound.Id}' declares no methods.");
            }

            foreach (var method in compound.Methods)
            {
                foreach (var subtask in method.Subtasks)
                {
                    if (registry.Contains(subtask)
                        && !registry.TryGet<GoapActionDef>(subtask, out _)
                        && !registry.TryGet<HtnCompoundDef>(subtask, out _))
                    {
                        report.Errors.Add($"HTN compound '{compound.Id}' subtask '{subtask}' is neither a GOAP action nor a compound.");
                    }
                }
            }
        }

        foreach (var role in registry.All<RoleDef>())
        {
            if (role.Slots < 1)
            {
                report.Errors.Add($"Role '{role.Id}' must have at least 1 slot.");
            }

            if (role.RingRadius is <= 0)
            {
                report.Errors.Add($"Role '{role.Id}' ringRadius must be positive.");
            }
        }

        foreach (var group in registry.All<GroupDef>())
        {
            if (group.Roles.Count == 0)
            {
                report.Errors.Add($"Group '{group.Id}' declares no roles.");
            }

            foreach (var roleId in group.Roles)
            {
                if (registry.Contains(roleId) && !registry.TryGet<RoleDef>(roleId, out _))
                {
                    report.Errors.Add($"Group '{group.Id}' role '{roleId}' is not a role def.");
                }
            }

            if (group.MinMembers > group.MaxMembers)
            {
                report.Errors.Add($"Group '{group.Id}' minMembers exceeds maxMembers.");
            }

            foreach (var pair in group.Staleness ?? new Dictionary<string, double>())
            {
                if (pair.Value < 0)
                {
                    report.Errors.Add($"Group '{group.Id}' staleness for '{pair.Key}' is negative.");
                }
            }
        }

        foreach (var collective in registry.All<CollectiveDef>())
        {
            if (collective.Budget < 1)
            {
                report.Errors.Add($"Collective '{collective.Id}' budget must be positive.");
            }

            foreach (var site in collective.Sites)
            {
                if (site.Position.Length != 3)
                {
                    report.Errors.Add($"Collective '{collective.Id}' has a site position that is not [x, y, z].");
                }

                if (registry.Contains(site.Group) && !registry.TryGet<GroupDef>(site.Group, out _))
                {
                    report.Errors.Add($"Collective '{collective.Id}' site group '{site.Group}' is not a group def.");
                }

                foreach (var member in site.Members)
                {
                    if (member.Count < 1)
                    {
                        report.Errors.Add($"Collective '{collective.Id}' member '{member.Entity}' count must be positive.");
                    }

                    if (registry.Contains(member.Entity) && !registry.TryGet<EntityTemplateDef>(member.Entity, out _))
                    {
                        report.Errors.Add($"Collective '{collective.Id}' member '{member.Entity}' is not an entity template.");
                    }
                }
            }
        }

        foreach (var sensor in registry.All<MetaSensorDef>())
        {
            if (sensor.Watch.Length == 0)
            {
                report.Errors.Add($"Meta sensor '{sensor.Id}' has no 'watch' topic.");
            }

            if (sensor.SetCondition.Length == 0)
            {
                report.Errors.Add($"Meta sensor '{sensor.Id}' has no 'setCondition'.");
            }

            if (sensor.Threshold < 1 || sensor.Window <= 0)
            {
                report.Errors.Add($"Meta sensor '{sensor.Id}' needs threshold >= 1 and window > 0.");
            }
        }
    }

    private void ValidateProfile(AgentProfileDef profile, DefRegistry registry, ContentLoadReport report, IFormulaEngine formulas)
    {
        if (!ValidBrains.Contains(profile.Brain))
        {
            report.Errors.Add($"Agent profile '{profile.Id}' has unknown brain tier '{profile.Brain}' (supported: {string.Join(", ", ValidBrains)}).");
        }

        if (profile.Brain == "fsm" && profile.FsmBrain is null)
        {
            report.Errors.Add($"Agent profile '{profile.Id}' uses brain 'fsm' but declares no 'fsmBrain'.");
        }

        if (profile.Brain == "schedules" && (profile.Schedules is not { Count: > 0 }))
        {
            report.Errors.Add($"Agent profile '{profile.Id}' uses brain 'schedules' but declares no 'schedules'.");
        }

        if (profile.Brain == "bt" && profile.BehaviorTree is null)
        {
            report.Errors.Add($"Agent profile '{profile.Id}' uses brain 'bt' but declares no 'behaviorTree'.");
        }

        if (profile.Brain == "goap" && (profile.Goals is not { Count: > 0 }))
        {
            report.Errors.Add($"Agent profile '{profile.Id}' uses brain 'goap' but declares no 'goals'.");
        }

        if (profile.Brain == "htn" && profile.RootTask is null)
        {
            report.Errors.Add($"Agent profile '{profile.Id}' uses brain 'htn' but declares no 'rootTask'.");
        }

        if (profile.RootTask is { } rootTask
            && registry.Contains(rootTask)
            && !registry.TryGet<HtnCompoundDef>(rootTask, out _)
            && !registry.TryGet<GoapActionDef>(rootTask, out _))
        {
            report.Errors.Add($"Agent profile '{profile.Id}' rootTask '{rootTask}' is neither a GOAP action nor an HTN compound.");
        }

        foreach (var sensorId in profile.MetaSensors ?? [])
        {
            if (registry.Contains(sensorId) && !registry.TryGet<MetaSensorDef>(sensorId, out _))
            {
                report.Errors.Add($"Agent profile '{profile.Id}' meta sensor '{sensorId}' is not a metasensor def.");
            }
        }

        foreach (var goalId in profile.Goals ?? [])
        {
            if (registry.Contains(goalId) && !registry.TryGet<GoapGoalDef>(goalId, out _))
            {
                report.Errors.Add($"Agent profile '{profile.Id}' goal '{goalId}' is not a GOAP goal def.");
            }
        }

        foreach (var actionId in profile.Actions ?? [])
        {
            if (registry.Contains(actionId) && !registry.TryGet<GoapActionDef>(actionId, out _))
            {
                report.Errors.Add($"Agent profile '{profile.Id}' action '{actionId}' is not a GOAP action def.");
            }
        }

        if (profile.ThinkInterval < 0)
        {
            report.Errors.Add($"Agent profile '{profile.Id}' has a negative 'thinkInterval'.");
        }

        foreach (var needId in profile.Needs ?? [])
        {
            if (registry.Contains(needId) && !registry.TryGet<NeedDef>(needId, out _))
            {
                report.Errors.Add($"Agent profile '{profile.Id}' need '{needId}' is not a need def.");
            }
        }

        foreach (var activityId in profile.Activities ?? [])
        {
            if (registry.Contains(activityId) && !registry.TryGet<ActivityDef>(activityId, out _))
            {
                report.Errors.Add($"Agent profile '{profile.Id}' activity '{activityId}' is not an activity def.");
            }
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

        foreach (var goalId in profile.Goals ?? [])
        {
            if (!registry.TryGet<GoapGoalDef>(goalId, out var goal))
            {
                continue;
            }

            foreach (var condition in goal.ReplanRequired ?? [])
            {
                if (!names.Contains(condition))
                {
                    report.Errors.Add(
                        $"GOAP goal '{goal.Id}' (used by '{profile.Id}') replan condition '{condition}' missing from catalog '{catalog.Id}'.");
                }
            }
        }

        foreach (var condition in profile.HtnInterrupt ?? [])
        {
            if (!names.Contains(condition))
            {
                report.Errors.Add(
                    $"Agent profile '{profile.Id}' htnInterrupt condition '{condition}' missing from catalog '{catalog.Id}'.");
            }
        }
    }

    private void ValidateGoapAction(GoapActionDef action, DefRegistry registry, ContentLoadReport report, IFormulaEngine formulas)
    {
        if (action.Effects.Count == 0)
        {
            report.Errors.Add($"GOAP action '{action.Id}' has no effects — the planner can never use it.");
        }

        if (!formulas.TryParse(action.Cost, out var error))
        {
            report.Errors.Add($"GOAP action '{action.Id}' cost formula: {error}");
        }

        if (action.MoveTo is { } moveTo
            && moveTo.ValueKind != JsonValueKind.String
            && !(moveTo.ValueKind == JsonValueKind.Array && moveTo.GetArrayLength() == 3))
        {
            report.Errors.Add($"GOAP action '{action.Id}' moveTo must be a symbol string or [x, y, z].");
        }

        if (action.Speed is { } speed && speed is not ("walk" or "run"))
        {
            report.Errors.Add($"GOAP action '{action.Id}' speed must be \"walk\" or \"run\".");
        }

        effects?.ValidateList(action.RunEffects, $"{action.Id}.runEffects", registry, formulas, report);
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

    private void ValidateBehaviorTree(BehaviorTreeDef tree, DefRegistry registry, ContentLoadReport report, IFormulaEngine formulas)
    {
        if (tree.Root.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            report.Errors.Add($"Behavior tree '{tree.Id}' has no 'root'.");
            return;
        }

        ValidateBtNode(tree.Root, $"{tree.Id}.root", registry, report, formulas);

        // subtree reference cycles (the builder degrades them to fail leaves,
        // but they are always an authoring error)
        var visiting = new HashSet<string>(StringComparer.Ordinal) { tree.Id };
        DetectSubtreeCycle(tree, tree.Id, visiting, registry, report);
    }

    private static void DetectSubtreeCycle(BehaviorTreeDef tree, string rootId, HashSet<string> visiting, DefRegistry registry, ContentLoadReport report)
    {
        foreach (var subtreeId in BehaviorTreeDef.CollectSubtrees(tree.Root))
        {
            if (!visiting.Add(subtreeId))
            {
                report.Errors.Add($"Behavior tree '{rootId}' has a subtree reference cycle through '{subtreeId}'.");
                continue;
            }

            if (registry.TryGet<BehaviorTreeDef>(subtreeId, out var subtree))
            {
                DetectSubtreeCycle(subtree, rootId, visiting, registry, report);
            }

            visiting.Remove(subtreeId);
        }
    }

    private void ValidateBtNode(JsonElement node, string owner, DefRegistry registry, ContentLoadReport report, IFormulaEngine formulas)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            report.Errors.Add($"[{owner}] behavior tree node is not an object.");
            return;
        }

        if (node.TryGetProperty("task", out _))
        {
            tasks.ValidateList([node], owner, registry, formulas, report);
            return;
        }

        if (node.TryGetProperty("condition", out var condition))
        {
            List<JsonElement> list = condition.ValueKind == JsonValueKind.Array ? condition.EnumerateArray().ToList() : [condition];
            conditions.ValidateList(list, owner, registry, formulas, report);
            return;
        }

        if (node.TryGetProperty("subtree", out var subtree))
        {
            if (subtree.ValueKind != JsonValueKind.String)
            {
                report.Errors.Add($"[{owner}] 'subtree' must be a behavior tree ID string.");
            }
            else if (registry.Contains(subtree.GetString()!) && !registry.TryGet<BehaviorTreeDef>(subtree.GetString()!, out _))
            {
                report.Errors.Add($"[{owner}] subtree '{subtree.GetString()}' is not a behavior tree def.");
            }

            return; // dangling refs are caught by the link pass
        }

        if (!Rpg.Effects.JsonArgs.TryGetString(node, "node", out var kind))
        {
            report.Errors.Add($"[{owner}] behavior tree node needs one of 'node', 'task', 'condition', or 'subtree'.");
            return;
        }

        switch (kind)
        {
            case "Sequence" or "Selector":
                if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array && children.GetArrayLength() > 0)
                {
                    var index = 0;
                    foreach (var child in children.EnumerateArray())
                    {
                        ValidateBtNode(child, $"{owner}.{kind}[{index++}]", registry, report, formulas);
                    }
                }
                else
                {
                    report.Errors.Add($"[{owner}] {kind} requires a non-empty 'children' array.");
                }

                break;

            case "Inverter" or "RepeatUntilFail" or "Cooldown" or "ConditionGate":
                if (kind == "Cooldown" && Rpg.Effects.JsonArgs.GetDouble(node, "seconds", 1.0) <= 0)
                {
                    report.Errors.Add($"[{owner}] Cooldown 'seconds' must be positive.");
                }

                if (kind == "ConditionGate")
                {
                    if (node.TryGetProperty("when", out var when) && when.ValueKind == JsonValueKind.Array)
                    {
                        conditions.ValidateList(when.EnumerateArray().ToList(), owner, registry, formulas, report);
                    }
                    else
                    {
                        report.Errors.Add($"[{owner}] ConditionGate requires a 'when' condition array.");
                    }
                }

                if (node.TryGetProperty("child", out var only))
                {
                    ValidateBtNode(only, $"{owner}.{kind}", registry, report, formulas);
                }
                else
                {
                    report.Errors.Add($"[{owner}] {kind} requires a 'child'.");
                }

                break;

            default:
                report.Errors.Add($"[{owner}] unknown behavior tree node '{kind}'.");
                break;
        }
    }

    private static void ValidateUtilityEvaluator(UtilityEvaluatorDef evaluator, ContentLoadReport report, IFormulaEngine formulas)
    {
        if (evaluator.Factors.Count == 0)
        {
            report.Errors.Add($"Utility evaluator '{evaluator.Id}' declares no factors.");
        }

        foreach (var factor in evaluator.Factors)
        {
            if (!formulas.TryParse(factor.Formula, out var error))
            {
                report.Errors.Add($"Utility evaluator '{evaluator.Id}' factor formula '{factor.Formula}': {error}");
            }

            if (factor.Weight <= 0)
            {
                report.Errors.Add($"Utility evaluator '{evaluator.Id}' factor '{factor.Formula}' has non-positive weight.");
            }
        }
    }

    private static void ValidateNeed(NeedDef need, ContentLoadReport report)
    {
        if (string.IsNullOrWhiteSpace(need.Key))
        {
            report.Errors.Add($"Need '{need.Id}' has no 'key' (the identifier formulas use).");
        }

        if (need.Initial is < 0 or > 1)
        {
            report.Errors.Add($"Need '{need.Id}' initial value must be 0–1.");
        }

        if (need.DecayPerSecond < 0)
        {
            report.Errors.Add($"Need '{need.Id}' has a negative decay rate.");
        }
    }

    private void ValidateActivity(ActivityDef activity, DefRegistry registry, ContentLoadReport report, IFormulaEngine formulas)
    {
        if (activity.Satisfies.Count == 0)
        {
            report.Errors.Add($"Activity '{activity.Id}' satisfies no needs — the selector can never score it.");
        }

        foreach (var pair in activity.Satisfies)
        {
            if (registry.Contains(pair.Key) && !registry.TryGet<NeedDef>(pair.Key, out _))
            {
                report.Errors.Add($"Activity '{activity.Id}' satisfies '{pair.Key}', which is not a need def.");
            }
        }

        if (!formulas.TryParse(activity.Cost, out var error))
        {
            report.Errors.Add($"Activity '{activity.Id}' cost formula: {error}");
        }

        conditions.ValidateList(activity.Conditions, $"{activity.Id}.conditions", registry, formulas, report);
        tasks.ValidateList(activity.Tasks, $"{activity.Id}.tasks", registry, formulas, report);
    }
}
