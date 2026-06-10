namespace Lattice.Ai.Perception;

/// <summary>
/// The Half-Life world model (research ch02 §2.5): condition names map to
/// bit positions, declared in content. The 32-bit cap is a feature — it
/// budgets how much an agent can "know" (ch01 §1.7 principle 4).
/// </summary>
public sealed class ConditionCatalog
{
    private readonly Dictionary<string, int> _bits = new(StringComparer.Ordinal);

    public ConditionCatalog(IEnumerable<string> names)
    {
        var bit = 0;
        foreach (var name in names)
        {
            if (bit >= 32)
            {
                throw new InvalidOperationException("A condition catalog holds at most 32 conditions.");
            }

            _bits[name] = bit++;
        }
    }

    public IEnumerable<string> Names => _bits.Keys;

    public int Count => _bits.Count;

    public bool TryGetBit(string name, out int bit) => _bits.TryGetValue(name, out bit);

    /// <summary>OR the bits of the given names; unknown names are skipped (validation reports them at load).</summary>
    public uint MaskOf(IEnumerable<string>? names)
    {
        uint mask = 0;
        foreach (var name in names ?? [])
        {
            if (_bits.TryGetValue(name, out var bit))
            {
                mask |= 1u << bit;
            }
        }

        return mask;
    }

    /// <summary>An empty catalog for agents without one configured.</summary>
    public static ConditionCatalog Empty { get; } = new([]);
}

/// <summary>Fast 32-bit condition flags, refreshed from sensors every agent update.</summary>
public struct ConditionSet
{
    public uint Bits { get; private set; }

    public void Set(int bit) => Bits |= 1u << bit;

    public void Clear(int bit) => Bits &= ~(1u << bit);

    public readonly bool IsSet(int bit) => (Bits & (1u << bit)) != 0;

    public void ClearAll() => Bits = 0;

    public void Or(uint mask) => Bits |= mask;

    public readonly bool HasAnyOf(uint mask) => (Bits & mask) != 0;

    public readonly bool HasAllOf(uint mask) => (Bits & mask) == mask;

    public readonly bool IsSet(ConditionCatalog catalog, string name)
        => catalog.TryGetBit(name, out var bit) && IsSet(bit);

    public void Set(ConditionCatalog catalog, string name)
    {
        if (catalog.TryGetBit(name, out var bit))
        {
            Set(bit);
        }
    }

    /// <summary>Names of currently set conditions (debug views).</summary>
    public readonly IEnumerable<string> SetNames(ConditionCatalog catalog)
    {
        var self = this; // structs cannot capture 'this' in lambdas
        return catalog.Names.Where(name => self.IsSet(catalog, name));
    }
}
