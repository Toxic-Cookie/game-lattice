using System.Text.Json;
using Lattice.Core.Content;

namespace Lattice.Ai.Defs;

/// <summary>
/// A behavior tree (plan/04 §5) as nested node objects. Node shapes:
/// composites <c>{"node":"Sequence"|"Selector","children":[...]}</c>,
/// decorators <c>{"node":"Inverter"|"RepeatUntilFail","child":{...}}</c>,
/// <c>{"node":"Cooldown","seconds":n,"child":{...}}</c>,
/// <c>{"node":"ConditionGate","when":[conditions],"child":{...}}</c>,
/// leaves <c>{"task":...}</c> / <c>{"condition":...}</c> (the same primitive
/// vocabularies as schedules — one vocabulary, three brain tiers), and
/// <c>{"subtree":"bt_other"}</c> references.
///
/// Semantics: Sequences remember their running child between ticks;
/// Selectors re-evaluate higher-priority children every tick and preempt a
/// running lower-priority branch; ConditionGate decorators abort their
/// running subtree when the gate fails — together the BT analog of schedule
/// interrupt masks.
/// </summary>
public sealed class BehaviorTreeDef : Def
{
    /// <summary>The root node object.</summary>
    public JsonElement Root { get; set; }

    public override IEnumerable<DefReference> GetReferences()
    {
        foreach (var subtree in CollectSubtrees(Root))
        {
            yield return new DefReference(subtree, $"{Id}.root");
        }
    }

    /// <summary>All "subtree" reference IDs in a node graph (link pass + cycle detection).</summary>
    internal static IEnumerable<string> CollectSubtrees(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (node.TryGetProperty("subtree", out var subtree) && subtree.ValueKind == JsonValueKind.String)
        {
            yield return subtree.GetString()!;
        }

        if (node.TryGetProperty("child", out var child))
        {
            foreach (var reference in CollectSubtrees(child))
            {
                yield return reference;
            }
        }

        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in children.EnumerateArray())
            {
                foreach (var reference in CollectSubtrees(element))
                {
                    yield return reference;
                }
            }
        }
    }
}
