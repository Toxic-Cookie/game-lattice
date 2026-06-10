using System.Globalization;
using System.Text.Json;
using Lattice.Core.Content;
using Lattice.Core.Formulas;
using Lattice.Core.Hosting;
using Lattice.Core.Simulation;

namespace Lattice.Rpg.Effects;

/// <summary>Execution context handed to every effect primitive.</summary>
public sealed class EffectContext
{
    public required GameSession Session { get; init; }

    public required RpgRuntime Rpg { get; init; }

    /// <summary>The acting entity (formula scope for damage/heal amounts). May be null (world-originated effects).</summary>
    public Entity? Source { get; init; }

    /// <summary>The entity the effect applies to.</summary>
    public Entity? Target { get; init; }

    /// <summary>Formula scope: source stats first, then target's.</summary>
    public IFormulaContext? Scope => (IFormulaContext?)Source ?? Target;
}

/// <summary>Validation context for load-time arg checking.</summary>
public sealed class EffectValidationContext
{
    public required DefRegistry Registry { get; init; }

    public required IFormulaEngine Formulas { get; init; }

    public required ContentLoadReport Report { get; init; }

    /// <summary>Where this effect list lives, for error messages ("item_iron_sword.useActions").</summary>
    public required string Owner { get; init; }

    public void Error(string message) => Report.Errors.Add($"{Owner}: {message}");

    public void RequireDef<TDef>(JsonElement args, string property)
        where TDef : Def
    {
        if (JsonArgs.TryGetString(args, property, out var id))
        {
            if (!Registry.TryGet<TDef>(id, out _))
            {
                Error($"'{property}' references missing {typeof(TDef).Name} '{id}'.");
            }
        }
        else
        {
            Error($"missing required string property '{property}'.");
        }
    }

    public void RequireFormula(JsonElement args, string property, bool required = true)
    {
        if (JsonArgs.TryGetFormula(args, property, out var formula))
        {
            if (!Formulas.TryParse(formula, out var error))
            {
                Error($"'{property}' formula \"{formula}\" — {error}");
            }
        }
        else if (required)
        {
            Error($"missing required formula property '{property}'.");
        }
    }
}

/// <summary>Helpers for reading effect/condition argument payloads.</summary>
public static class JsonArgs
{
    public static bool TryGetString(JsonElement args, string name, out string value)
    {
        if (args.ValueKind == JsonValueKind.Object
            && args.TryGetProperty(name, out var prop)
            && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString()!;
            return true;
        }

        value = "";
        return false;
    }

    public static string GetString(JsonElement args, string name)
        => TryGetString(args, name, out var value)
            ? value
            : throw new FormulaException($"Effect args missing required string '{name}': {args.GetRawText()}");

    /// <summary>A formula property may be a string or a bare number.</summary>
    public static bool TryGetFormula(JsonElement args, string name, out string formula)
    {
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.String)
            {
                formula = prop.GetString()!;
                return true;
            }

            if (prop.ValueKind == JsonValueKind.Number)
            {
                formula = prop.GetDouble().ToString(CultureInfo.InvariantCulture);
                return true;
            }
        }

        formula = "";
        return false;
    }

    public static string GetFormula(JsonElement args, string name, string? fallback = null)
        => TryGetFormula(args, name, out var formula)
            ? formula
            : fallback ?? throw new FormulaException($"Effect args missing required formula '{name}': {args.GetRawText()}");

    public static double GetDouble(JsonElement args, string name, double fallback)
        => args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetDouble()
            : fallback;
}

/// <summary>An effect primitive: one verb of the interpreter's vocabulary (plan/02 §3).</summary>
public interface IEffectExecutor
{
    /// <summary>The JSON "type" discriminator.</summary>
    string Type { get; }

    void Execute(EffectContext context, JsonElement args);

    /// <summary>Load-time arg validation; report problems via the context.</summary>
    void Validate(JsonElement args, EffectValidationContext context);
}

/// <summary>
/// The effect-primitive vocabulary. New content never adds executors; a new
/// genre of capability adds exactly one executor + schema + manifest entry.
/// </summary>
public sealed class EffectRegistry
{
    private readonly Dictionary<string, IEffectExecutor> _byType = new(StringComparer.Ordinal);

    public IEnumerable<IEffectExecutor> All => _byType.Values;

    public void Register(IEffectExecutor executor) => _byType[executor.Type] = executor;

    public void Run(IEnumerable<JsonElement>? effects, EffectContext context)
    {
        foreach (var effect in effects ?? [])
        {
            RunOne(effect, context);
        }
    }

    public void RunOne(JsonElement effect, EffectContext context)
    {
        if (!JsonArgs.TryGetString(effect, "type", out var type))
        {
            context.Session.Services.Host.Logger.Error($"Effect payload missing 'type': {effect.GetRawText()}");
            return;
        }

        if (!_byType.TryGetValue(type, out var executor))
        {
            context.Session.Services.Host.Logger.Error($"Unknown effect type '{type}'.");
            return;
        }

        executor.Execute(context, effect);
    }

    public void ValidateList(IEnumerable<JsonElement>? effects, string owner, DefRegistry registry, IFormulaEngine formulas, ContentLoadReport report)
    {
        foreach (var effect in effects ?? [])
        {
            var context = new EffectValidationContext { Registry = registry, Formulas = formulas, Report = report, Owner = owner };
            if (!JsonArgs.TryGetString(effect, "type", out var type))
            {
                context.Error("effect payload missing 'type'.");
                continue;
            }

            if (!_byType.TryGetValue(type, out var executor))
            {
                context.Error($"unknown effect type '{type}'.");
                continue;
            }

            executor.Validate(effect, context);
        }
    }
}
