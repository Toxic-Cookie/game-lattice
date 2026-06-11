using System.Text.Json;
using Lattice.Core.Content;
using Lattice.Core.Formulas;
using Lattice.Core.Hosting;
using Lattice.Core.Simulation;
using Lattice.Rpg.Defs;
using Lattice.Rpg.Effects;

namespace Lattice.Rpg.Conditions;

/// <summary>Evaluation context for condition primitives.</summary>
public sealed class ConditionContext
{
    private IFormulaContext? _scope;

    public required GameSession Session { get; init; }

    public required RpgRuntime Rpg { get; init; }

    /// <summary>The entity the condition is about (loot context, quest subject, ...). May be null.</summary>
    public Entity? Subject { get; init; }

    /// <summary>Formula scope: subject stats first, then global blackboard flags.</summary>
    public IFormulaContext Scope => _scope ??= Subject is null
        ? new BlackboardFormulaContext(Session.Flags)
        : new CompositeFormulaContext(Subject, new BlackboardFormulaContext(Session.Flags));
}

/// <summary>A condition primitive — the boolean half of the interpreter vocabulary, shared by loot, quests (M3), and AI preconditions (M4).</summary>
public interface IConditionEvaluator
{
    string Type { get; }

    bool Evaluate(ConditionContext context, JsonElement args);

    void Validate(JsonElement args, EffectValidationContext context);
}

/// <summary>Registry + combinators for condition primitives.</summary>
public sealed class ConditionRegistry
{
    private readonly Dictionary<string, IConditionEvaluator> _byType = new(StringComparer.Ordinal);

    public void Register(IConditionEvaluator evaluator) => _byType[evaluator.Type] = evaluator;

    /// <summary>Every registered evaluator, ordered by type (manifest exporter).</summary>
    public IEnumerable<IConditionEvaluator> All => _byType.Values.OrderBy(e => e.Type, StringComparer.Ordinal);

    /// <summary>True when every condition in the list holds (empty/null list = true).</summary>
    public bool EvaluateAll(IEnumerable<JsonElement>? conditions, ConditionContext context)
    {
        foreach (var condition in conditions ?? [])
        {
            if (!EvaluateOne(condition, context))
            {
                return false;
            }
        }

        return true;
    }

    public bool EvaluateOne(JsonElement condition, ConditionContext context)
    {
        if (!JsonArgs.TryGetString(condition, "type", out var type) || !_byType.TryGetValue(type, out var evaluator))
        {
            context.Session.Services.Host.Logger.Error($"Unknown condition type in: {condition.GetRawText()}");
            return false; // unknown conditions fail closed
        }

        return evaluator.Evaluate(context, condition);
    }

    public void ValidateList(IEnumerable<JsonElement>? conditions, string owner, DefRegistry registry, IFormulaEngine formulas, ContentLoadReport report)
    {
        foreach (var condition in conditions ?? [])
        {
            var context = new EffectValidationContext { Registry = registry, Formulas = formulas, Report = report, Owner = owner };
            if (!JsonArgs.TryGetString(condition, "type", out var type))
            {
                context.Error("condition payload missing 'type'.");
                continue;
            }

            if (!_byType.TryGetValue(type, out var evaluator))
            {
                context.Error($"unknown condition type '{type}'.");
                continue;
            }

            evaluator.Validate(condition, context);
        }
    }

    public static ConditionRegistry CreateDefault()
    {
        var registry = new ConditionRegistry();
        registry.Register(new StatAtLeastCondition());
        registry.Register(new HasItemCondition());
        registry.Register(new HasTagCondition());
        registry.Register(new FlagEqualsCondition());
        registry.Register(new FormulaTrueCondition());
        registry.Register(new AllCondition(registry));
        registry.Register(new AnyCondition(registry));
        registry.Register(new NotCondition(registry));
        return registry;
    }
}

[PrimitiveDoc("True when the subject's current stat meets a rolled threshold.",
    "stat: stat def id; value: threshold formula",
    """{"type":"StatAtLeast","stat":"stat_str","value":"5"}""")]
internal sealed class StatAtLeastCondition : IConditionEvaluator
{
    public string Type => "StatAtLeast";

    public bool Evaluate(ConditionContext ctx, JsonElement args)
    {
        if (ctx.Subject is null)
        {
            return false;
        }

        var statId = JsonArgs.GetString(args, "stat");
        var key = ctx.Rpg.Stats.TryGetById(statId, out var def) ? def.Key : statId;
        var sheet = ctx.Rpg.GetSheet(ctx.Subject);
        var current = sheet?.HasStat(key) == true ? sheet.Current(key) : 0;
        return current >= ctx.Session.Formulas.Evaluate(JsonArgs.GetFormula(args, "value"), ctx.Scope);
    }

    public void Validate(JsonElement args, EffectValidationContext v)
    {
        BuiltinEffects.ValidateStatRef(args, v);
        v.RequireFormula(args, "value");
    }
}

[PrimitiveDoc("True when the subject carries at least N of an item.",
    "item: item def id; count?: minimum (default 1)",
    """{"type":"HasItem","item":"item_key","count":1}""")]
internal sealed class HasItemCondition : IConditionEvaluator
{
    public string Type => "HasItem";

    public bool Evaluate(ConditionContext ctx, JsonElement args)
    {
        if (ctx.Subject is null)
        {
            return false;
        }

        var count = (int)JsonArgs.GetDouble(args, "count", 1);
        return ctx.Rpg.CountItem(ctx.Subject, JsonArgs.GetString(args, "item")) >= count;
    }

    public void Validate(JsonElement args, EffectValidationContext v) => v.RequireDef<ItemDef>(args, "item");
}

[PrimitiveDoc("True when the subject entity carries a tag.",
    "tag: tag string",
    """{"type":"HasTag","tag":"undead"}""")]
internal sealed class HasTagCondition : IConditionEvaluator
{
    public string Type => "HasTag";

    public bool Evaluate(ConditionContext ctx, JsonElement args)
        => ctx.Subject?.Tags.Contains(JsonArgs.GetString(args, "tag")) == true;

    public void Validate(JsonElement args, EffectValidationContext v)
    {
        if (!JsonArgs.TryGetString(args, "tag", out _))
        {
            v.Error("missing required string property 'tag'.");
        }
    }
}

[PrimitiveDoc("True when a global blackboard flag equals a scalar value.",
    "flag: flag key; value: bool | number | string",
    """{"type":"FlagEquals","flag":"is_night","value":true}""")]
internal sealed class FlagEqualsCondition : IConditionEvaluator
{
    public string Type => "FlagEquals";

    public bool Evaluate(ConditionContext ctx, JsonElement args)
    {
        var current = ctx.Session.Flags.Read(JsonArgs.GetString(args, "flag"));
        if (!args.TryGetProperty("value", out var valueProp) || !JsonValueHelper.TryToPlain(valueProp, out var expected))
        {
            return false;
        }

        return Equals(current, expected);
    }

    public void Validate(JsonElement args, EffectValidationContext v)
    {
        if (!JsonArgs.TryGetString(args, "flag", out _))
        {
            v.Error("missing required string property 'flag'.");
        }

        if (!args.TryGetProperty("value", out var valueProp) || !JsonValueHelper.TryToPlain(valueProp, out _))
        {
            v.Error("'value' must be a bool, number, or string.");
        }
    }
}

[PrimitiveDoc("True when a formula evaluates non-zero (subject stats + global flags in scope).",
    "formula: expression (e.g. \"Hour >= 20\")",
    """{"type":"FormulaTrue","formula":"Str * 2 > Con"}""")]
internal sealed class FormulaTrueCondition : IConditionEvaluator
{
    public string Type => "FormulaTrue";

    public bool Evaluate(ConditionContext ctx, JsonElement args)
        => ctx.Session.Formulas.Evaluate(JsonArgs.GetFormula(args, "formula"), ctx.Scope) != 0;

    public void Validate(JsonElement args, EffectValidationContext v) => v.RequireFormula(args, "formula");
}

[PrimitiveDoc("True when every nested condition holds.",
    "conditions: array of condition payloads",
    """{"type":"All","conditions":[{"type":"HasTag","tag":"npc"}]}""")]
internal sealed class AllCondition(ConditionRegistry registry) : IConditionEvaluator
{
    public string Type => "All";

    public bool Evaluate(ConditionContext ctx, JsonElement args)
        => GetList(args).All(c => registry.EvaluateOne(c, ctx));

    public void Validate(JsonElement args, EffectValidationContext v)
        => registry.ValidateList(GetList(args), v.Owner, v.Registry, v.Formulas, v.Report);

    internal static IEnumerable<JsonElement> GetList(JsonElement args)
        => args.TryGetProperty("conditions", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().ToList()
            : [];
}

[PrimitiveDoc("True when at least one nested condition holds.",
    "conditions: array of condition payloads",
    """{"type":"Any","conditions":[{"type":"HasTag","tag":"wolf"},{"type":"HasTag","tag":"bear"}]}""")]
internal sealed class AnyCondition(ConditionRegistry registry) : IConditionEvaluator
{
    public string Type => "Any";

    public bool Evaluate(ConditionContext ctx, JsonElement args)
        => AllCondition.GetList(args).Any(c => registry.EvaluateOne(c, ctx));

    public void Validate(JsonElement args, EffectValidationContext v)
        => registry.ValidateList(AllCondition.GetList(args), v.Owner, v.Registry, v.Formulas, v.Report);
}

[PrimitiveDoc("Inverts a nested condition.",
    "condition: a condition payload",
    """{"type":"Not","condition":{"type":"FlagEquals","flag":"is_night","value":true}}""")]
internal sealed class NotCondition(ConditionRegistry registry) : IConditionEvaluator
{
    public string Type => "Not";

    public bool Evaluate(ConditionContext ctx, JsonElement args)
        => args.TryGetProperty("condition", out var inner) && !registry.EvaluateOne(inner, ctx);

    public void Validate(JsonElement args, EffectValidationContext v)
    {
        if (args.TryGetProperty("condition", out var inner))
        {
            registry.ValidateList([inner], v.Owner, v.Registry, v.Formulas, v.Report);
        }
        else
        {
            v.Error("'Not' requires a 'condition' property.");
        }
    }
}
