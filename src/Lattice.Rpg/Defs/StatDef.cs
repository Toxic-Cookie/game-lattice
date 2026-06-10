using Lattice.Core.Content;

namespace Lattice.Rpg.Defs;

/// <summary>
/// A stat declared in data (plan/02 §1). The <see cref="Key"/> is the
/// identifier formulas use (<c>Str</c>, <c>HP</c>); the def <c>id</c>
/// (<c>stat_str</c>) is what other defs reference. Convention: IDs in
/// structural fields, keys in formula strings.
/// </summary>
public sealed class StatDef : Def
{
    /// <summary>Formula identifier for this stat. Must be unique across stats.</summary>
    public string Key { get; set; } = "";

    public string? Name { get; set; }

    /// <summary>Lower clamp formula (evaluated against the owning entity). Null = unclamped.</summary>
    public string? Min { get; set; }

    /// <summary>Upper clamp formula. Null = unclamped.</summary>
    public string? Max { get; set; }

    /// <summary>Default base value: a formula, or the keywords "min" / "max".</summary>
    public string? Default { get; set; }

    /// <summary>Derived stat: computed from this formula instead of a stored base value.</summary>
    public string? Formula { get; set; }

    /// <summary>When true, an entity whose current value reaches the stat's minimum dies.</summary>
    public bool Vital { get; set; }

    public bool IsDerived => Formula is not null;

    public override IEnumerable<string> GetFormulas()
    {
        if (Min is not null)
        {
            yield return Min;
        }

        if (Max is not null)
        {
            yield return Max;
        }

        if (Default is not null && Default is not ("min" or "max"))
        {
            yield return Default;
        }

        if (Formula is not null)
        {
            yield return Formula;
        }
    }
}

/// <summary>An equipment slot, declared in data so slot sets are moddable (plan/02 §4).</summary>
public sealed class SlotDef : Def
{
    public string? Name { get; set; }
}
