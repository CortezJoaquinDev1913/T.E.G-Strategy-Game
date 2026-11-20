using FsCheck.Xunit;
using Teg.Domain.Map;
using Teg.Domain.Rng;
using Teg.Domain.Setup;
using Teg.Domain.State;

namespace Teg.Domain.Tests.Setup;

/// <summary>
/// E2 / R§3: the initial deal. Placement itself (the 6+6 / 5+3 armies) is phase 3: here only the pool sizes
/// and the arithmetic of the resulting totals are checked.
/// </summary>
public sealed class DealTests
{
    private static readonly MapDefinition Map = MapDefinition.LoadEmbedded();

    private static readonly PlayerColor[] TableColors =
        [PlayerColor.Azul, PlayerColor.Rojo, PlayerColor.Negro, PlayerColor.Amarillo];

    private static GameOptions HumansOnly(int participants) =>
        new(Enumerable.Range(0, participants)
            .Select(i => new ParticipantSetup($"Jugador {i + 1}", ParticipantKind.Human, TableColors[i]))
            .ToArray());

    private static GameState Deal(int participants, long seed)
    {
        var result = GameSetup.Create(HumansOnly(participants), seed, Map);

        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Message);
        return result.Value;
    }

    private static int CountOwnedBy(GameState state, int seat) => state.Territories.Count(t => t.Owner == seat);

    /// <summary>Countries held by each participant, listed from rank 1 to the last rank.</summary>
    private static int[] CountsByRank(GameState state) =>
        state.Ranking.Select(seat => CountOwnedBy(state, seat)).ToArray();

    private static int[] ExpectedCountsByRank(int participants) => participants switch
    {
        2 => [25, 25],
        3 => [17, 17, 16],
        4 => [13, 13, 12, 12],
        _ => throw new ArgumentOutOfRangeException(nameof(participants)),
    };

    private static int[] ExpectedStartingTotalsByRank(int participants) => participants switch
    {
        2 => [37, 37],
        3 => [25, 25, 24],
        4 => [21, 21, 20, 20],
        _ => throw new ArgumentOutOfRangeException(nameof(participants)),
    };

    // ---- countries per participant -------------------------------------------------------------

    [Theory]
    [InlineData(2, new[] { 25, 25 })]
    [InlineData(3, new[] { 17, 17, 16 })]
    [InlineData(4, new[] { 13, 13, 12, 12 })]
    public void Deal_SplitsTheFiftyCountriesAccordingToTheTable_ByRank(int participants, int[] expectedByRank)
    {
        var state = Deal(participants, seed: 1);

        Assert.Equal(expectedByRank, CountsByRank(state));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Deal_GivesTheExtraCountriesToTheTwoBestRanked_NotToTheFirstRegistered(int participants)
    {
        var topTwoSeats = new HashSet<int>();

        for (var seed = 0L; seed < 200; seed++)
        {
            var state = Deal(participants, seed);
            var smallShare = ExpectedCountsByRank(participants).Min();

            Assert.Equal(smallShare + 1, CountOwnedBy(state, state.Ranking[0]));
            Assert.Equal(smallShare + 1, CountOwnedBy(state, state.Ranking[1]));
            Assert.All(state.Ranking.Skip(2), seat => Assert.Equal(smallShare, CountOwnedBy(state, seat)));

            topTwoSeats.Add(state.Ranking[0]);
            topTwoSeats.Add(state.Ranking[1]);
        }

        // Not vacuous: over 200 seeds the extra went to every seat at some point.
        Assert.Equal(Enumerable.Range(0, participants), topTwoSeats.Order());
    }

    [Property(MaxTest = 300)]
    public bool Deal_AlwaysAddsUpToFifty_AndEachParticipantMatchesTheTable(long seed, byte participantsPick)
    {
        var participants = 2 + participantsPick % 3;

        var state = Deal(participants, seed);
        var counts = CountsByRank(state);

        return counts.Sum() == 50
            && state.Territories.Count == 50
            && counts.SequenceEqual(ExpectedCountsByRank(participants));
    }

    // ---- ownership and armies ------------------------------------------------------------------

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Deal_EachCountryStartsWithExactlyOneArmyOfItsOwner(int participants)
    {
        var state = Deal(participants, seed: 7);

        Assert.Equal(50, state.Territories.Count);
        Assert.All(state.Territories, t => Assert.Equal(1, t.Armies));
        Assert.All(state.Territories, t => Assert.InRange(t.Owner, 0, participants - 1));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Deal_EveryMapCountryIsOwnedByExactlyOneParticipant(int participants)
    {
        var state = Deal(participants, seed: 99);

        var mapIds = Map.Territories.Select(t => t.Id).Order(StringComparer.Ordinal).ToArray();
        var dealtIds = state.Territories.Select(t => t.Id).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(50, mapIds.Length);
        Assert.Equal(mapIds, dealtIds); // all 50, none missing, none repeated => one owner each
        Assert.Equal(50, Enumerable.Range(0, participants).Sum(seat => CountOwnedBy(state, seat)));
    }

    // ---- initial placement pools and totals ----------------------------------------------------

    [Theory]
    [InlineData(2, 6, 6)]
    [InlineData(3, 5, 3)]
    [InlineData(4, 5, 3)]
    public void PlacementPools_AreSixPlusSixForTwo_AndFivePlusThreeForThreeOrFour(int participants, int round1, int round2)
    {
        Assert.Equal([round1, round2], GameSetup.PlacementPools(participants));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void PlacementPools_OutsideTwoToFourParticipants_Throws(int participants)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GameSetup.PlacementPools(participants));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void StartingTotals_AreCountriesPlusPools_AndMatchTheTable(int participants)
    {
        var state = Deal(participants, seed: 5);
        var poolTotal = GameSetup.PlacementPools(participants).Sum();

        var totalsByRank = CountsByRank(state).Select(countries => countries + poolTotal).ToArray();

        Assert.Equal(ExpectedStartingTotalsByRank(participants), totalsByRank);
    }

    // ---- ranking, first player and table order -------------------------------------------------

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Deal_TheRankingComesFromOneDieRollPerParticipant_WithTiesRerolled(int participants)
    {
        for (var seed = 0L; seed < 100; seed++)
        {
            var expected = InitialRanking.Determine(participants, new SeededRng(seed).RollDie);

            var state = Deal(participants, seed);

            Assert.Equal(expected.ToArray(), state.Ranking.ToArray());
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Deal_TheRankingIsAStrictPermutation_AndTheFirstPlayerIsRankOne(int participants)
    {
        var firstSeats = new HashSet<int>();

        for (var seed = 0L; seed < 200; seed++)
        {
            var state = Deal(participants, seed);

            Assert.Equal(Enumerable.Range(0, participants), state.Ranking.Order());
            Assert.Equal(state.Ranking[0], state.FirstSeat);
            Assert.Equal(state.FirstSeat, state.ActiveSeat);
            firstSeats.Add(state.FirstSeat);
        }

        Assert.Equal(Enumerable.Range(0, participants), firstSeats.Order()); // every seat can open
    }

    [Fact]
    public void TableOrder_IsRegistrationOrderStartingFromTheFirstPlayer()
    {
        var four = Deal(4, seed: 1);

        Assert.Equal([0, 1, 2, 3], (four with { FirstSeat = 0 }).TableOrder);
        Assert.Equal([2, 3, 0, 1], (four with { FirstSeat = 2 }).TableOrder);
        Assert.Equal([3, 0, 1, 2], (four with { FirstSeat = 3 }).TableOrder);

        var three = Deal(3, seed: 1);

        Assert.Equal([1, 2, 0], (three with { FirstSeat = 1 }).TableOrder);
    }

    [Fact]
    public void Deal_TableOrderOfTheDealtGame_StartsAtTheFirstPlayer()
    {
        for (var seed = 0L; seed < 50; seed++)
        {
            var state = Deal(4, seed);

            Assert.Equal(state.FirstSeat, state.TableOrder[0]);
            Assert.Equal(Enumerable.Range(0, 4), state.TableOrder.Order());
        }
    }

    // ---- state produced by the deal -------------------------------------------------------------

    [Fact]
    public void Deal_ParticipantsKeepRegistrationOrder_WithSeatsNameKindAndColour()
    {
        var options = new GameOptions(
        [
            new ParticipantSetup("Ana", ParticipantKind.Human, PlayerColor.Rojo),
            new ParticipantSetup("Bot Uno", ParticipantKind.Bot, PlayerColor.Azul),
            new ParticipantSetup("Luz", ParticipantKind.Human, PlayerColor.Verde),
        ]);

        var state = GameSetup.Create(options, seed: 3, Map).Value;

        Assert.Equal(
            [
                new Participant(0, "Ana", ParticipantKind.Human, PlayerColor.Rojo),
                new Participant(1, "Bot Uno", ParticipantKind.Bot, PlayerColor.Azul),
                new Participant(2, "Luz", ParticipantKind.Human, PlayerColor.Verde),
            ],
            state.Participants.ToArray());
    }

    [Fact]
    public void Deal_StartsInInitialPlacementRoundOne_BeforeAnyHostilitiesRound()
    {
        var state = Deal(3, seed: 11);

        Assert.Equal(GamePhase.InitialPlacement, state.Phase);
        Assert.Equal(1, state.PlacementRound);
        Assert.Equal(0, state.Round);
    }

    // ---- determinism and the RNG -----------------------------------------------------------------

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Deal_SameSeed_GivesTheSameState(int participants)
    {
        var first = Deal(participants, seed: 20260101);
        var second = Deal(participants, seed: 20260101);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Deal_DifferentSeeds_CanGiveDifferentDeals()
    {
        var first = Deal(4, seed: 1);
        var second = Deal(4, seed: 2);

        Assert.NotEqual(first, second);
        Assert.NotEqual(first.Territories, second.Territories);
    }

    [Fact]
    public void Deal_ConsumesTheRng_AndStoresSeedAndDrawsInTheState()
    {
        var state = Deal(4, seed: 123);

        Assert.Equal(123, state.Rng.Seed);
        // At least one die per participant, the 49 swaps of the 50-country shuffle of the deal, the 49 swaps of the
        // shuffle of the deck that follows it (R§3: the cards go back to the deck and are mixed again), and the 14 swaps of
        // the shuffle of the fifteen objective numbers that closes the creation of a four-participant game (R§8).
        Assert.True(state.Rng.Draws >= 4 + 49 + 49 + 14, $"Draws was {state.Rng.Draws}");
    }

    [Fact]
    public void Deal_DrawsAreDeterministicForASeed_AndVaryWithTheSeed()
    {
        var sameA = Deal(3, seed: 55).Rng.Draws;
        var sameB = Deal(3, seed: 55).Rng.Draws;
        var drawsPerSeed = Enumerable.Range(0, 40).Select(seed => Deal(3, seed).Rng.Draws).Distinct().ToArray();

        Assert.Equal(sameA, sameB);
        Assert.True(drawsPerSeed.Length > 1, "Rejection sampling and ranking re-rolls make the draw count seed-dependent.");
    }
}
