using System.Reflection;
using System.Text;
using System.Text.Json;
using Lattice.Ai.Defs;
using Lattice.Ai.Tasks;
using Lattice.Core.Content;
using Lattice.Rpg.Conditions;
using Lattice.Rpg.Defs;
using Lattice.Rpg.Effects;

namespace Lattice.Tooling;

/// <summary>
/// The system manifest (plan/06 §1): the text "dictionary" an LLM reads
/// before writing content — every def, the full primitive vocabularies
/// (sourced from [PrimitiveDoc] on the registered executors, so they cannot
/// drift), the catalogs, and the formula scope. Markdown by default;
/// <see cref="GenerateJson"/> emits the same data for tooling.
/// </summary>
public static class ManifestGenerator
{
    /// <summary>Engine-published event topics (content may subscribe via restockOn, quest counters, meta sensors, ...).</summary>
    private static readonly (string Topic, string Meaning)[] EngineTopics =
    [
        ("Entity.Damaged", "an entity took damage {instanceId, amount}"),
        ("Entity.Died", "a vital stat hit zero {instanceId, defId, killerId}"),
        ("Stat.Changed", "a stat's current value changed {instanceId, stat, old, new}"),
        ("Item.Acquired / Item.Removed / Item.Equipped / Item.Unequipped / Item.Used", "inventory changes {instanceId, item, ...}"),
        ("Trade.Completed", "a shop transaction {shop, customerId, item, price, kind}"),
        ("Interaction.Performed", "a smart-object verb ran {actorId, targetId, object, verb}"),
        ("Quest.Started / Quest.StepCompleted / Quest.Completed", "quest progress {quest, step?}"),
        ("Dialogue.Started / Dialogue.Ended", "conversation lifecycle {node}"),
        ("Stimulus.Sound / Stimulus.Scent", "world stimuli for AI senses {x, y, z, loudness}"),
        ("Time.MinuteTick / Time.HourStarted / Time.DayStarted / Time.SeasonStarted / Time.PhaseChanged", "calendar (M5)"),
        ("Weather.Changed", "weather transition {weather}"),
        ("Content.Reloaded", "hot reload applied"),
    ];

    private static readonly (string Type, string Description, string Args, string Example)[] SteeringDocs =
    [
        ("Idle", "Stand still.", "(no args)", """{"type":"Idle"}"""),
        ("Wander", "Drift randomly around the spawn point.",
            "radius?: default 4; speed?: default walk; interval?: seconds between legs (default 1.5)",
            """{"type":"Wander","radius":4,"speed":1.2}"""),
        ("FleeFrom", "Run away from the current threat/enemy/group-threat belief.",
            "distance?: flee leg length (default 6); speed?: default run",
            """{"type":"FleeFrom","distance":8,"speed":3.5}"""),
        ("MoveTo", "Walk toward a target spec (same specs as the MoveTo task).",
            "target: symbol or [x,y,z]; speed?: number",
            """{"type":"MoveTo","target":"post","speed":1.4}"""),
    ];

    public static string GenerateMarkdown(
        DefRegistry registry, DefTypeRegistry types,
        EffectRegistry effects, ConditionRegistry conditions, TaskRegistry tasks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Game Lattice — Content Manifest");
        sb.AppendLine();
        sb.AppendLine($"{registry.Count} defs. Every def needs `id`, `type`, and ideally a one-line `description`.");
        sb.AppendLine("Defs may declare `\"inherits\": \"<parent id>\"` (same kind): objects deep-merge, scalars override,");
        sb.AppendLine("arrays replace — or patch the parent's array with `{\"$append\": [...], \"$remove\": [...]}`.");
        sb.AppendLine();

        AppendDefSections(sb, registry, types);
        AppendBlueprints(sb, registry);
        AppendPrimitives(sb, "Effect primitives (`\"type\"` discriminator)",
            effects.All.Select(e => (e.Type, Doc: Doc(e))));
        AppendPrimitives(sb, "Condition primitives (`\"type\"` discriminator)",
            conditions.All.Select(c => (c.Type, Doc: Doc(c))));
        AppendPrimitives(sb, "Task primitives (`\"task\"` discriminator — schedules, BT leaves, activities)",
            tasks.All.Select(t => (t.Type, Doc: Doc(t))));
        AppendPrimitives(sb, "FSM steering primitives (`\"type\"` in fsmbrain states)",
            SteeringDocs.Select(s => (s.Type, Doc: (s.Description, s.Args, s.Example))));

        AppendCatalogs(sb, registry);
        AppendFormulaScope(sb, registry);
        return sb.ToString();
    }

    /// <summary>The same data as structured JSON (tooling mode).</summary>
    public static string GenerateJson(
        DefRegistry registry, DefTypeRegistry types,
        EffectRegistry effects, ConditionRegistry conditions, TaskRegistry tasks)
    {
        var data = new
        {
            defs = types.All
                .OrderBy(t => t.Key, StringComparer.Ordinal)
                .Select(t => new
                {
                    kind = t.Key,
                    entries = registry.AllDefs
                        .Where(d => d.GetType() == t.Value)
                        .OrderBy(d => d.Id, StringComparer.Ordinal)
                        .Select(d => new { id = d.Id, description = d.Description, inherits = d.Inherits })
                        .ToList(),
                })
                .Where(k => k.entries.Count > 0),
            effects = effects.All.Select(e => Primitive(e.Type, Doc(e))),
            conditions = conditions.All.Select(c => Primitive(c.Type, Doc(c))),
            tasks = tasks.All.Select(t => Primitive(t.Type, Doc(t))),
            steering = SteeringDocs.Select(s => Primitive(s.Type, (s.Description, s.Args, s.Example))),
        };
        return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });

        static object Primitive(string type, (string Description, string Args, string Example) doc)
            => new { type, description = doc.Description, args = doc.Args, example = doc.Example };
    }

    private static (string Description, string Args, string Example) Doc(object executor)
    {
        var attribute = executor.GetType().GetCustomAttribute<PrimitiveDocAttribute>();
        return attribute is null
            ? ("(undocumented)", "", "")
            : (attribute.Description, attribute.Args, attribute.Example);
    }

    private static void AppendDefSections(StringBuilder sb, DefRegistry registry, DefTypeRegistry types)
    {
        sb.AppendLine("## Registered defs by kind");
        sb.AppendLine();
        foreach (var (typeName, clrType) in types.All.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            var defs = registry.AllDefs
                .Where(d => d.GetType() == clrType)
                .OrderBy(d => d.Id, StringComparer.Ordinal)
                .ToList();
            if (defs.Count == 0)
            {
                continue;
            }

            sb.AppendLine($"### `{typeName}` — {defs.Count}");
            sb.AppendLine();
            foreach (var def in defs)
            {
                var inherits = def.Inherits is null ? "" : $" (inherits `{def.Inherits}`)";
                var description = def.Description is null ? "" : $" — {def.Description}";
                sb.AppendLine($"- `{def.Id}`{inherits}{description}");
            }

            sb.AppendLine();
        }
    }

    private static void AppendBlueprints(StringBuilder sb, DefRegistry registry)
    {
        var children = registry.AllDefs
            .Where(d => d.Inherits is not null)
            .OrderBy(d => d.Id, StringComparer.Ordinal)
            .ToList();
        if (children.Count == 0)
        {
            return;
        }

        sb.AppendLine("## Blueprint hierarchies");
        sb.AppendLine();
        foreach (var def in children)
        {
            sb.AppendLine($"- `{def.Inherits}` -> `{def.Id}`");
        }

        sb.AppendLine();
    }

    private static void AppendPrimitives(
        StringBuilder sb, string heading, IEnumerable<(string Type, (string Description, string Args, string Example) Doc)> primitives)
    {
        sb.AppendLine($"## {heading}");
        sb.AppendLine();
        foreach (var (type, doc) in primitives)
        {
            sb.AppendLine($"- **{type}** — {doc.Description}");
            if (doc.Args.Length > 0)
            {
                sb.AppendLine($"  - args: {doc.Args}");
            }

            if (doc.Example.Length > 0)
            {
                sb.AppendLine($"  - example: `{doc.Example}`");
            }
        }

        sb.AppendLine();
    }

    private static void AppendCatalogs(StringBuilder sb, DefRegistry registry)
    {
        sb.AppendLine("## Catalogs");
        sb.AppendLine();

        var stats = registry.All<StatDef>().OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
        if (stats.Count > 0)
        {
            sb.AppendLine("### Stats (use the def id in structural fields, the key in formulas)");
            foreach (var stat in stats)
            {
                sb.AppendLine($"- `{stat.Id}` -> formula key `{stat.Key}`{(stat.Vital ? " (vital)" : "")}");
            }

            sb.AppendLine();
        }

        var slots = registry.All<SlotDef>().OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
        if (slots.Count > 0)
        {
            sb.AppendLine("### Equipment slots");
            foreach (var slot in slots)
            {
                sb.AppendLine($"- `{slot.Id}`{(slot.Description is null ? "" : $" — {slot.Description}")}");
            }

            sb.AppendLine();
        }

        foreach (var catalog in registry.All<ConditionCatalogDef>().OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            sb.AppendLine($"### Condition catalog `{catalog.Id}` ({catalog.Names.Count}/32 bits)");
            sb.AppendLine(string.Join(", ", catalog.Names.Select(n => $"`{n}`")));
            sb.AppendLine();
        }

        sb.AppendLine("### Event topics");
        foreach (var (topic, meaning) in EngineTopics)
        {
            sb.AppendLine($"- `{topic}` — {meaning}");
        }

        sb.AppendLine();
    }

    private static void AppendFormulaScope(StringBuilder sb, DefRegistry registry)
    {
        sb.AppendLine("## Formula identifier scope");
        sb.AppendLine();
        sb.AppendLine("Formulas (NCalc syntax, dice like `2d6+1` allowed) resolve identifiers in this order:");
        sb.AppendLine("1. **Stat keys** of the subject entity (see the stat catalog above).");
        sb.AppendLine("2. **Need keys** of the subject agent (utility/GOAP scopes), e.g. `Thirst`.");
        sb.AppendLine("3. **Condition names** of the subject agent as 0/1 (GOAP priorities/costs), e.g. `CAN_SEE_ENEMY * 50`.");
        sb.AppendLine("4. **Numeric/bool beliefs** of the subject agent (GOAP/utility scopes).");
        sb.AppendLine("5. **Global flags** (blackboard), including the clock: `Hour` (fractional), `Day`, `Season` (index),");
        sb.AppendLine("   plus `is_<phase>` booleans and whatever content/weather writes.");
        sb.AppendLine("Item price formulas additionally see `BasePrice`.");
        sb.AppendLine();
    }
}
