using System.Text.Json;
using Lattice.Core.Content;

namespace Lattice.Narrative.Defs;

/// <summary>
/// JSON node-tree dialogue (plan/03 §3) — the secondary, machine-friendly
/// dialogue format. Yarn is the primary authoring format; both run through
/// the same <see cref="DialogueRunner"/>. Options and effects reuse the
/// shared condition/effect primitives.
/// </summary>
public sealed class DialogueTreeDef : Def
{
    public string Start { get; set; } = "start";

    public Dictionary<string, TreeNode> Nodes { get; set; } = new(StringComparer.Ordinal);

    public sealed class TreeNode
    {
        public string? Speaker { get; set; }

        public string? Line { get; set; }

        /// <summary>Effects run when the node is entered (before the line shows).</summary>
        public List<JsonElement>? Effects { get; set; }

        public List<TreeOption>? Options { get; set; }

        /// <summary>Auto-advance target when there are no (eligible) options. Null = end.</summary>
        public string? Next { get; set; }
    }

    public sealed class TreeOption
    {
        public string Text { get; set; } = "";

        /// <summary>Condition primitives gating visibility (subject = the player).</summary>
        public List<JsonElement>? Conditions { get; set; }

        public List<JsonElement>? Effects { get; set; }

        public string? Next { get; set; }
    }
}

/// <summary>
/// Step-based quest (plan/03 §4): each step has a completion condition and
/// completion effects; optional event-driven counters feed the global
/// blackboard (no entity polling).
/// </summary>
public sealed class QuestDef : Def
{
    public string? Name { get; set; }

    public List<QuestStep> Steps { get; set; } = [];

    /// <summary>Effects run when the whole quest completes (target = player).</summary>
    public List<JsonElement>? OnComplete { get; set; }

    public sealed class QuestStep
    {
        public string Id { get; set; } = "";

        public string? Description { get; set; }

        /// <summary>Optional event-driven counter active while this step is current.</summary>
        public CounterSpec? Count { get; set; }

        /// <summary>Condition primitive deciding step completion (subject = player, flags in scope).</summary>
        public JsonElement? Complete { get; set; }

        public List<JsonElement>? OnComplete { get; set; }
    }

    public sealed class CounterSpec
    {
        /// <summary>Global blackboard key incremented on matches.</summary>
        public string Counter { get; set; } = "";

        /// <summary>Event topic to listen for (e.g. "Entity.Died").</summary>
        public string On { get; set; } = "";

        /// <summary>Payload equality filters (e.g. { "defId": "entity_wolf" }).</summary>
        public Dictionary<string, JsonElement>? Where { get; set; }

        public int Amount { get; set; } = 1;
    }
}

/// <summary>
/// Smart object (plan/03 §5): self-describing interactions bound to an
/// entity template. Player-facing verbs carry conditions + effects; the
/// AI-facing fields (approach, animation, world-state pre/effects) are
/// consumed by the M4 planners.
/// </summary>
public sealed class SmartObjectDef : Def
{
    public string? Name { get; set; }

    /// <summary>The entity template this object's behavior binds to.</summary>
    public string? Entity { get; set; }

    public int MaxUsers { get; set; } = 1;

    public List<Interaction> Interactions { get; set; } = [];

    /// <summary>AI: where an agent should stand, relative to the object ([x,y,z], M4).</summary>
    public float[]? ApproachOffset { get; set; }

    /// <summary>AI: animation played while using the object (M4).</summary>
    public string? Animation { get; set; }

    /// <summary>AI: world-state predicates required to plan a use of this object (M4).</summary>
    public Dictionary<string, bool>? Preconditions { get; set; }

    /// <summary>AI: world-state predicates that using this object establishes (M4).</summary>
    public Dictionary<string, bool>? AiEffects { get; set; }

    public override IEnumerable<DefReference> GetReferences()
    {
        if (Entity is not null)
        {
            yield return new DefReference(Entity, $"{Id}.entity");
        }
    }

    public sealed class Interaction
    {
        public string Verb { get; set; } = "interact";

        public List<JsonElement>? Conditions { get; set; }

        public List<JsonElement>? Effects { get; set; }
    }
}
