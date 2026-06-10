using Lattice.Core.Formulas;

namespace Lattice.Core.Content;

/// <summary>
/// Module-supplied validation pass run after the core link pass. Modules
/// (RPG, AI, ...) implement cross-def rules here — slot existence, loot
/// cycles, stat dependency graphs — accreting toward the full
/// `lattice validate` rule registry (plan/06 §3).
/// </summary>
public interface IContentValidator
{
    void Validate(DefRegistry registry, ContentLoadReport report, IFormulaEngine formulas);
}
