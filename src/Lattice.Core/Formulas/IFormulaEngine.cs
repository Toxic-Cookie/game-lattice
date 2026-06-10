namespace Lattice.Core.Formulas;

/// <summary>Resolves bare identifiers (e.g. <c>Str</c>, <c>Level</c>) appearing in formulas.</summary>
public interface IFormulaContext
{
    bool TryResolve(string identifier, out double value);
}

/// <summary>
/// Evaluates content formula strings like <c>"(Str * 2) + Level"</c> or
/// <c>"1d10+5"</c>. Implementations wrap a third-party parser (NCalc) behind
/// this seam so it stays swappable and content stays library-agnostic
/// (plan/01 §4). Dice rolls draw from the simulation's deterministic RNG.
/// </summary>
public interface IFormulaEngine
{
    /// <summary>Evaluate a formula; identifiers resolve through <paramref name="context"/>.</summary>
    /// <exception cref="FormulaException">Syntax error or unresolvable identifier.</exception>
    double Evaluate(string formula, IFormulaContext? context = null);

    /// <summary>Syntax pre-flight for the validation suite. Returns false with an error message on parse failure.</summary>
    bool TryParse(string formula, out string? error);
}

/// <summary>Raised when a formula fails to parse or evaluate; the message names the formula.</summary>
public sealed class FormulaException : Exception
{
    public FormulaException(string message)
        : base(message)
    {
    }

    public FormulaException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>Chains contexts: the first one that resolves an identifier wins (entity stats → global flags).</summary>
public sealed class CompositeFormulaContext(params IFormulaContext[] contexts) : IFormulaContext
{
    public bool TryResolve(string identifier, out double value)
    {
        foreach (var context in contexts)
        {
            if (context.TryResolve(identifier, out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }
}
