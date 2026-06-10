namespace Lattice.Core.Simulation;

/// <summary>
/// Deterministic PCG32 random source. Unlike <see cref="Random"/>, its full
/// state is exportable/restorable, which the save system requires: same seed
/// + same content + same tick count must reproduce the same world state.
/// </summary>
public sealed class LatticeRandom
{
    private const ulong Multiplier = 6364136223846793005UL;
    private const ulong Increment = 0xDA3E39CB94B95BDBUL;

    private ulong _state;

    public LatticeRandom(int seed)
    {
        _state = unchecked((ulong)seed * Multiplier + Increment);
        NextUInt(); // scramble the seed-derived state once
    }

    /// <summary>Full generator state, for save/load round-trips.</summary>
    public ulong State
    {
        get => _state;
        set => _state = value;
    }

    public uint NextUInt()
    {
        var old = _state;
        _state = unchecked(old * Multiplier + Increment);
        var xorshifted = (uint)(((old >> 18) ^ old) >> 27);
        var rot = (int)(old >> 59);
        return (xorshifted >> rot) | (xorshifted << (-rot & 31));
    }

    /// <summary>Uniform double in [0, 1).</summary>
    public double NextDouble() => NextUInt() * (1.0 / 4294967296.0);

    /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
        {
            return minInclusive;
        }

        var range = (uint)(maxExclusive - minInclusive);
        return minInclusive + (int)(NextUInt() % range);
    }

    /// <summary>Sum of <paramref name="count"/> rolls of a <paramref name="sides"/>-sided die (dice notation NdM).</summary>
    public int RollDice(int count, int sides)
    {
        var total = 0;
        for (var i = 0; i < count; i++)
        {
            total += NextInt(1, sides + 1);
        }

        return total;
    }
}
