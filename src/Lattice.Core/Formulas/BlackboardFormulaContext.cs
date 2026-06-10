using Lattice.Core.Simulation;

namespace Lattice.Core.Formulas;

/// <summary>
/// Exposes a blackboard's numeric and boolean entries to formulas, so
/// content can write conditions like <c>"wolves_killed >= 3"</c> against
/// global flags. Strings are not resolvable (formulas are numeric).
/// </summary>
public sealed class BlackboardFormulaContext(Blackboard blackboard) : IFormulaContext
{
    public bool TryResolve(string identifier, out double value)
    {
        if (blackboard.TryRead(identifier, out var stored))
        {
            switch (stored)
            {
                case double d:
                    value = d;
                    return true;
                case bool b:
                    value = b ? 1 : 0;
                    return true;
            }
        }

        value = 0;
        return false;
    }
}
