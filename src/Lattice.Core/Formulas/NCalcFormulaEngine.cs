using System.Globalization;
using System.Text.RegularExpressions;
using Lattice.Core.Simulation;
using NCalc;
using NCalc.Factories;

namespace Lattice.Core.Formulas;

/// <summary>
/// <see cref="IFormulaEngine"/> over NCalc. Parsed expressions are cached per
/// formula string (formulas sit in hot paths: damage, loot, utility scores).
/// Dice notation (<c>2d6</c>) is rewritten to a <c>dice(2,6)</c> function
/// call in a pre-pass and rolls through the deterministic simulation RNG.
/// </summary>
public sealed class NCalcFormulaEngine : IFormulaEngine
{
    private static readonly Regex DicePattern = new(
        @"\b(\d+)[dD](\d+)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Dictionary<string, LogicalExpression> _cache = new(StringComparer.Ordinal);
    private readonly LatticeRandom _rng;

    public NCalcFormulaEngine(LatticeRandom rng)
    {
        _rng = rng;
    }

    public double Evaluate(string formula, IFormulaContext? context = null)
    {
        var parsed = GetParsed(formula);
        var expression = new Expression(parsed);

        expression.EvaluateFunction += (name, args) =>
        {
            if (string.Equals(name, "dice", StringComparison.OrdinalIgnoreCase))
            {
                var count = ToInt(args.Parameters.Evaluate(0), formula);
                var sides = ToInt(args.Parameters.Evaluate(1), formula);
                args.Result = (double)_rng.RollDice(count, sides);
            }
        };

        expression.EvaluateParameter += (name, args) =>
        {
            if (context is not null && context.TryResolve(name, out var value))
            {
                args.Result = value;
            }
            else
            {
                throw new FormulaException($"Unknown identifier '{name}' in formula \"{formula}\".");
            }
        };

        try
        {
            var result = expression.Evaluate();
            return ToDouble(result, formula);
        }
        catch (FormulaException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new FormulaException($"Failed to evaluate formula \"{formula}\": {ex.Message}", ex);
        }
    }

    public IReadOnlyCollection<string> GetIdentifiers(string formula)
        => new Expression(GetParsed(formula)).GetParameterNames();

    public bool TryParse(string formula, out string? error)
    {
        try
        {
            GetParsed(formula);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.InnerException?.Message ?? ex.Message;
            return false;
        }
    }

    private LogicalExpression GetParsed(string formula)
    {
        if (_cache.TryGetValue(formula, out var cached))
        {
            return cached;
        }

        var rewritten = DicePattern.Replace(formula, "dice($1,$2)");
        LogicalExpression parsed;
        try
        {
            parsed = LogicalExpressionFactory.Create(rewritten);
        }
        catch (Exception ex)
        {
            throw new FormulaException($"Failed to parse formula \"{formula}\": {ex.Message}", ex);
        }

        _cache[formula] = parsed;
        return parsed;
    }

    private static double ToDouble(object? value, string formula)
    {
        return value switch
        {
            double d => d,
            null => throw new FormulaException($"Formula \"{formula}\" evaluated to null."),
            bool b => b ? 1 : 0,
            IConvertible c => c.ToDouble(CultureInfo.InvariantCulture),
            _ => throw new FormulaException($"Formula \"{formula}\" evaluated to non-numeric {value.GetType().Name}."),
        };
    }

    private static int ToInt(object? value, string formula) => (int)ToDouble(value, formula);
}
