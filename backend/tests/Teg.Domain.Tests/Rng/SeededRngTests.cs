using Teg.Domain.Rng;

namespace Teg.Domain.Tests.Rng;

/// <summary>
/// E10 (determinism). The expected numbers marked "oracle" were produced by an independent
/// implementation (BigInt SplitMix64 + rejection sampling + Fisher-Yates), so they lock the algorithm:
/// saved games depend on a seed meaning the same sequence forever.
/// </summary>
public sealed class SeededRngTests
{
    private static ulong[] Draw(SeededRng rng, int count) =>
        Enumerable.Range(0, count).Select(_ => rng.NextUInt64()).ToArray();

    // ---- SplitMix64 known answers -------------------------------------------------------------

    [Fact]
    public void NextUInt64_Seed0_MatchesSplitMix64ReferenceVector()
    {
        var rng = new SeededRng(0);

        Assert.Equal(
            [
                16294208416658607535UL,
                7960286522194355700UL,
                487617019471545679UL,
                17909611376780542444UL,
                1961750202426094747UL,
            ],
            Draw(rng, 5));
    }

    [Fact]
    public void NextUInt64_Seed1234567_MatchesSplitMix64ReferenceVector()
    {
        var rng = new SeededRng(1234567);

        Assert.Equal(
            [
                6457827717110365317UL,
                3203168211198807973UL,
                9817491932198370423UL,
                4593380528125082431UL,
                16408922859458223821UL,
            ],
            Draw(rng, 5));
    }

    [Fact]
    public void NextUInt64_NegativeSeed_IsTheSameAsItsUnsignedTwosComplement()
    {
        var rng = new SeededRng(-1);

        Assert.Equal(
            [16490336266968443936UL, 16834447057089888969UL, 4048727598324417001UL],
            Draw(rng, 3));
    }

    // ---- same seed, same sequence --------------------------------------------------------------

    [Fact]
    public void SameSeed_ProducesTheSameSequence()
    {
        var first = Draw(new SeededRng(987654321), 100);
        var second = Draw(new SeededRng(987654321), 100);

        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentSequences()
    {
        var first = Draw(new SeededRng(1), 20);
        var second = Draw(new SeededRng(2), 20);

        Assert.NotEqual(first, second);
    }

    // ---- Seed / Draws and O(1) rebuild ---------------------------------------------------------

    [Fact]
    public void NewGenerator_ExposesSeedAndStartsWithZeroDraws()
    {
        var rng = new SeededRng(42);

        Assert.Equal(42, rng.Seed);
        Assert.Equal(0, rng.Draws);
    }

    [Fact]
    public void Draws_CountsEveryRawDraw()
    {
        var rng = new SeededRng(42);

        Draw(rng, 5);

        Assert.Equal(5, rng.Draws);
        Assert.Equal(42, rng.Seed);
    }

    [Theory]
    [InlineData(0L, 0L)]
    [InlineData(42L, 1L)]
    [InlineData(42L, 37L)]
    [InlineData(-1L, 1000L)]
    [InlineData(long.MaxValue, 5L)]
    [InlineData(long.MinValue, 64L)]
    public void RestoredFromSeedAndDraws_ContinuesExactlyWhereTheOriginalWas(long seed, long draws)
    {
        var original = new SeededRng(seed);
        Draw(original, (int)draws);

        var restored = new SeededRng(seed, draws);

        Assert.Equal(draws, restored.Draws);
        Assert.Equal(Draw(original, 50), Draw(restored, 50));
    }

    [Fact]
    public void RestoredFarIntoTheStream_IsInstantAndMatchesTheClosedForm()
    {
        // 10^13 draws: a replay-by-looping rebuild would never finish. State after n draws = seed + n*gamma.
        var rng = new SeededRng(42, 10_000_000_000_000);

        Assert.Equal(5045931555727282777UL, rng.NextUInt64()); // oracle
        Assert.Equal(10_000_000_000_001, rng.Draws);
    }

    [Fact]
    public void State_RoundTripsThroughSeedAndDraws()
    {
        var rng = new SeededRng(-77);
        Draw(rng, 12);

        Assert.Equal(new RngState(-77, 12), rng.State);

        var restored = new SeededRng(rng.State.Seed, rng.State.Draws);
        Assert.Equal(Draw(rng, 10), Draw(restored, 10));
    }

    [Fact]
    public void Constructor_NegativeDraws_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SeededRng(1, -1));
    }

    // ---- bounded values (rejection sampling) ---------------------------------------------------

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(6UL)]
    [InlineData(7UL)]
    [InlineData(50UL)]
    [InlineData(1000UL)]
    [InlineData(3UL << 62)]
    public void NextBelow_StaysWithinTheExclusiveBound(ulong bound)
    {
        var rng = new SeededRng(2024);

        for (var i = 0; i < 2000; i++)
        {
            Assert.True(rng.NextBelow(bound) < bound);
        }
    }

    [Fact]
    public void NextBelow_BoundOfOne_IsAlwaysZero()
    {
        var rng = new SeededRng(5);

        Assert.All(Enumerable.Range(0, 50), _ => Assert.Equal(0UL, rng.NextBelow(1)));
    }

    [Fact]
    public void NextBelow_ZeroBound_Throws()
    {
        var rng = new SeededRng(5);

        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextBelow(0));
    }

    [Fact]
    public void NextBelow_RejectsRawValuesInTheBiasedRegion()
    {
        // bound = 3 * 2^62: 2^64 mod bound = 2^62, so the first 2^62 raw values must be thrown away.
        // Seed 7: raw draws are 7191..., 3096... (< 2^62, rejected), 1661..., 1075...; hence 3 results cost 4 draws. (oracle)
        var rng = new SeededRng(7);
        const ulong bound = 3UL << 62;

        var results = new[] { rng.NextBelow(bound), rng.NextBelow(bound), rng.NextBelow(bound) };

        Assert.Equal(
            [7191089600892374487UL, 2781043691533445634UL, 10753165928301472203UL],
            results);
        Assert.Equal(4, rng.Draws);
    }

    [Fact]
    public void NextBelow_HasNoModuloBias()
    {
        // A uniform value below 3*2^62 falls under 2^62 one third of the time.
        // Plain `raw % bound` would double the density of [0, 2^62) and give one half.
        var rng = new SeededRng(7);
        const ulong bound = 3UL << 62;
        const int samples = 20_000;

        var low = 0;
        for (var i = 0; i < samples; i++)
        {
            if (rng.NextBelow(bound) < 1UL << 62)
            {
                low++;
            }
        }

        var fraction = (double)low / samples;
        Assert.InRange(fraction, 0.30, 0.36);
        // 25% of raw draws are rejected => about 1 / 0.75 = 1.33 draws per value.
        Assert.InRange(rng.Draws, samples * 1.2, samples * 1.5);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(6)]
    [InlineData(50)]
    [InlineData(int.MaxValue)]
    public void NextInt_StaysWithinTheExclusiveBound(int bound)
    {
        var rng = new SeededRng(11);

        for (var i = 0; i < 2000; i++)
        {
            var value = rng.NextInt(bound);

            Assert.InRange(value, 0, bound - 1);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void NextInt_NonPositiveBound_Throws(int bound)
    {
        var rng = new SeededRng(11);

        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(bound));
    }

    // ---- dice ----------------------------------------------------------------------------------

    [Fact]
    public void RollDie_Seed42_MatchesTheOracleSequence()
    {
        var rng = new SeededRng(42);

        var rolls = Enumerable.Range(0, 10).Select(_ => rng.RollDie()).ToArray();

        Assert.Equal([2, 2, 1, 1, 5, 1, 2, 3, 2, 3], rolls);
        Assert.Equal(10, rng.Draws);
    }

    [Fact]
    public void RollDie_IsAlwaysBetweenOneAndSix()
    {
        var rng = new SeededRng(31337);

        for (var i = 0; i < 60_000; i++)
        {
            Assert.InRange(rng.RollDie(), 1, 6);
        }
    }

    [Fact]
    public void RollDie_EveryFaceAppearsAboutOneSixthOfTheTime()
    {
        var rng = new SeededRng(31337);
        var counts = new int[7];

        for (var i = 0; i < 60_000; i++)
        {
            counts[rng.RollDie()]++;
        }

        // 10,000 expected per face, sigma ~ 91: +-450 is ~5 sigma. Deterministic for a fixed seed.
        for (var face = 1; face <= 6; face++)
        {
            Assert.InRange(counts[face], 9_550, 10_450);
        }
    }

    [Fact]
    public void RollDie_IsDeterministicPerSeed()
    {
        var first = new SeededRng(99);
        var second = new SeededRng(99);

        Assert.Equal(
            Enumerable.Range(0, 200).Select(_ => first.RollDie()),
            Enumerable.Range(0, 200).Select(_ => second.RollDie()));
    }

    // ---- shuffle (Fisher-Yates) ----------------------------------------------------------------

    [Fact]
    public void Shuffle_Seed42_MatchesTheFisherYatesOracle()
    {
        var rng = new SeededRng(42);
        var items = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

        rng.Shuffle(items);

        Assert.Equal([0, 9, 5, 8, 6, 4, 7, 2, 1, 3], items);
        Assert.Equal(9, rng.Draws);
    }

    [Fact]
    public void Shuffle_ProducesAPermutationOfTheInput()
    {
        var rng = new SeededRng(2025);
        var items = Enumerable.Range(0, 50).ToArray();

        rng.Shuffle(items);

        Assert.Equal(Enumerable.Range(0, 50), items.Order());
        Assert.NotEqual(Enumerable.Range(0, 50), items);
    }

    [Fact]
    public void Shuffle_SameSeed_GivesTheSameOrder()
    {
        var first = Enumerable.Range(0, 50).ToArray();
        var second = Enumerable.Range(0, 50).ToArray();

        new SeededRng(123).Shuffle(first);
        new SeededRng(123).Shuffle(second);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Shuffle_DifferentSeeds_GiveDifferentOrders()
    {
        var first = Enumerable.Range(0, 50).ToArray();
        var second = Enumerable.Range(0, 50).ToArray();

        new SeededRng(1).Shuffle(first);
        new SeededRng(2).Shuffle(second);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Shuffle_AllPermutationsOfThreeAreEquallyLikely()
    {
        // A "swap with any index" shuffle gives 4/27 and 5/27 instead of 1/6 and fails this.
        var rng = new SeededRng(2024);
        var counts = new Dictionary<string, int>();

        for (var i = 0; i < 60_000; i++)
        {
            var items = new[] { 0, 1, 2 };
            rng.Shuffle(items);
            var key = string.Join(',', items);
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        Assert.Equal(6, counts.Count);
        foreach (var count in counts.Values)
        {
            Assert.InRange(count, 9_600, 10_400); // 10,000 expected, sigma ~ 91
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Shuffle_FewerThanTwoItems_ConsumesNoDraws(int length)
    {
        var rng = new SeededRng(8);
        var items = new int[length];

        rng.Shuffle(items);

        Assert.Equal(0, rng.Draws);
    }

    [Fact]
    public void Shuffle_WorksOnLists()
    {
        var items = new List<string> { "a", "b", "c", "d", "e" };

        new SeededRng(42).Shuffle(items);

        Assert.Equal(["b", "c", "a", "e", "d"], items); // oracle
        Assert.Equal(["a", "b", "c", "d", "e"], items.Order());
    }
}
