using Lattice.Core.Content;
using Lattice.Core.Formulas;
using Lattice.Narrative.Defs;
using Lattice.Rpg.Conditions;
using Lattice.Rpg.Effects;

namespace Lattice.Narrative;

/// <summary>
/// Narrative validation rules (plan/03 acceptance): dialogue-tree node
/// integrity and reachability, quest step structure, smart-object bindings.
/// Yarn compile diagnostics are reported by the runtime/tooling compile
/// path, not here (the validator interface has no file access).
/// </summary>
public sealed class NarrativeContentValidator(EffectRegistry effects, ConditionRegistry conditions) : IContentValidator
{
    public void Validate(DefRegistry registry, ContentLoadReport report, IFormulaEngine formulas)
    {
        ValidateTrees(registry, report, formulas);
        ValidateQuests(registry, report, formulas);
        ValidateSmartObjects(registry, report, formulas);
    }

    private void ValidateTrees(DefRegistry registry, ContentLoadReport report, IFormulaEngine formulas)
    {
        foreach (var tree in registry.All<DialogueTreeDef>())
        {
            if (!tree.Nodes.ContainsKey(tree.Start))
            {
                report.Errors.Add($"Dialogue tree '{tree.Id}' start node '{tree.Start}' does not exist.");
            }

            var reachable = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            queue.Enqueue(tree.Start);
            while (queue.Count > 0)
            {
                var nodeId = queue.Dequeue();
                if (!reachable.Add(nodeId) || !tree.Nodes.TryGetValue(nodeId, out var node))
                {
                    continue;
                }

                foreach (var next in new[] { node.Next }.Concat((node.Options ?? []).Select(o => o.Next)))
                {
                    if (next is null)
                    {
                        continue;
                    }

                    if (!tree.Nodes.ContainsKey(next))
                    {
                        report.Errors.Add($"Dialogue tree '{tree.Id}' node '{nodeId}' links to missing node '{next}'.");
                    }
                    else
                    {
                        queue.Enqueue(next);
                    }
                }
            }

            foreach (var orphan in tree.Nodes.Keys.Where(k => !reachable.Contains(k)))
            {
                report.Warnings.Add($"Dialogue tree '{tree.Id}' node '{orphan}' is unreachable from '{tree.Start}'.");
            }

            foreach (var pair in tree.Nodes)
            {
                effects.ValidateList(pair.Value.Effects, $"{tree.Id}.{pair.Key}", registry, formulas, report);
                foreach (var option in pair.Value.Options ?? [])
                {
                    conditions.ValidateList(option.Conditions, $"{tree.Id}.{pair.Key}.options", registry, formulas, report);
                    effects.ValidateList(option.Effects, $"{tree.Id}.{pair.Key}.options", registry, formulas, report);
                }
            }
        }
    }

    private void ValidateQuests(DefRegistry registry, ContentLoadReport report, IFormulaEngine formulas)
    {
        foreach (var quest in registry.All<QuestDef>())
        {
            if (quest.Steps.Count == 0)
            {
                report.Errors.Add($"Quest '{quest.Id}' has no steps.");
            }

            var stepIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var step in quest.Steps)
            {
                if (!stepIds.Add(step.Id))
                {
                    report.Errors.Add($"Quest '{quest.Id}' has duplicate step id '{step.Id}'.");
                }

                if (step.Complete is null)
                {
                    report.Errors.Add($"Quest '{quest.Id}' step '{step.Id}' has no 'complete' condition.");
                }
                else
                {
                    conditions.ValidateList([step.Complete.Value], $"{quest.Id}.{step.Id}", registry, formulas, report);
                }

                if (step.Count is not null)
                {
                    if (string.IsNullOrWhiteSpace(step.Count.On) || string.IsNullOrWhiteSpace(step.Count.Counter))
                    {
                        report.Errors.Add($"Quest '{quest.Id}' step '{step.Id}' counter needs both 'on' and 'counter'.");
                    }
                }

                effects.ValidateList(step.OnComplete, $"{quest.Id}.{step.Id}.onComplete", registry, formulas, report);
            }

            effects.ValidateList(quest.OnComplete, $"{quest.Id}.onComplete", registry, formulas, report);
        }
    }

    private void ValidateSmartObjects(DefRegistry registry, ContentLoadReport report, IFormulaEngine formulas)
    {
        foreach (var smartObject in registry.All<SmartObjectDef>())
        {
            if (smartObject.Entity is null)
            {
                report.Errors.Add($"Smart object '{smartObject.Id}' has no 'entity' binding.");
            }
            else if (registry.Contains(smartObject.Entity) && !registry.TryGet<EntityTemplateDef>(smartObject.Entity, out _))
            {
                report.Errors.Add($"Smart object '{smartObject.Id}' entity '{smartObject.Entity}' is not an entity template.");
            }

            if (smartObject.MaxUsers < 1)
            {
                report.Errors.Add($"Smart object '{smartObject.Id}' maxUsers must be >= 1.");
            }

            foreach (var interaction in smartObject.Interactions)
            {
                conditions.ValidateList(interaction.Conditions, $"{smartObject.Id}.{interaction.Verb}", registry, formulas, report);
                effects.ValidateList(interaction.Effects, $"{smartObject.Id}.{interaction.Verb}", registry, formulas, report);
            }
        }
    }
}
