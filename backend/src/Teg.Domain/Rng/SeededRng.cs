namespace Teg.Domain.Rng;

/// <summary>The persisted form of a <see cref="SeededRng"/>: seed plus number of raw draws taken so far.</summary>
public readonly record struct RngState(long Seed, long Draws);

/// <summary>
/// Deterministic random source: our own SplitMix64. <see cref="System.Random"/> is NOT used because its
/// seeded sequence is not a stable contract across .NET versions, and saved games must replay forever.
/// <para>
/// SplitMix64 advances its state by a constant (gamma) on every draw, so the state after <c>n</c> draws is
/// <c>seed + n * gamma</c>. Seed + draw counter therefore rebuilds the generator in O(1), with no replay.
/// </para>
/// Instances are mutable and not thread-safe: the engine creates one from the state, uses it, and stores
/// <see cref="State"/> back into the (immutable) game state.
/// </summary>
public sealed class SeededRng
{
    private const ulong Gamma = 0x9E3779B97F4A7C15UL;
    private const int DieFaces = 6;

    /// <param name="seed">Any value; negative seeds are reinterpreted as their unsigned two's complement.</param>
    /// <param name="draws">Raw draws already taken (0 for a fresh generator, or the stored counter to restore).</param>
    public SeededRng(long seed, long draws = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(draws);

        Seed = seed;
        Draws = draws;
    }

    public long Seed { get; }

    /// <summary>Raw 64-bit draws consumed so far, including values discarded by rejection sampling.</summary>
    public long Draws { get; private set; }

    public RngState State => new(Seed, Draws);

    /// <summary>Next raw 64-bit value.</summary>
    public ulong NextUInt64()
    {
        Draws++;

        return unchecked(Mix((ulong)Seed + (ulong)Draws * Gamma));
    }

    /// <summary>
    /// Uniform value in <c>[0, exclusiveUpperBound)</c>. Rejection sampling: raw values below
    /// <c>2^64 mod bound</c> are discarded, so every residue is reachable by exactly the same number of
    /// raw values and there is no modulo bias.
    /// </summary>
    public ulong NextBelow(ulong exclusiveUpperBound)
    {
        ArgumentOutOfRangeException.ThrowIfZero(exclusiveUpperBound);

        var threshold = unchecked(0UL - exclusiveUpperBound) % exclusiveUpperBound;
        ulong raw;
        do
        {
            raw = NextUInt64();
        }
        while (raw < threshold);

        return raw % exclusiveUpperBound;
    }

    /// <summary>Uniform value in <c>[0, exclusiveUpperBound)</c>.</summary>
    public int NextInt(int exclusiveUpperBound)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveUpperBound);

        return (int)NextBelow((ulong)exclusiveUpperBound);
    }

    /// <summary>One six-sided die: 1 to 6.</summary>
    public int RollDie() => NextInt(DieFaces) + 1;

    /// <summary>In-place Fisher-Yates shuffle (every permutation equally likely).</summary>
    public void Shuffle<T>(IList<T> items)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = NextInt(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    private static ulong Mix(ulong z)
    {
        unchecked
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;

            return z ^ (z >> 31);
        }
    }
}
