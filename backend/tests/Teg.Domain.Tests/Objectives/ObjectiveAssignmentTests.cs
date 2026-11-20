using Teg.Domain.Map;
using Teg.Domain.Rng;
using Teg.Domain.Rules;
using Teg.Domain.Setup;
using Teg.Domain.State;
using static Teg.Domain.Tests.Support.GameFixtures;

namespace Teg.Domain.Tests.ObjectiveRules;

/// <summary>
/// E9 / R§3, R§8: the secret objectives are drawn when a game of FOUR participants is created, one each, without repeats,
/// with the game's seeded RNG and AFTER every draw the creation already takes (ranking, deal, deck shuffle), so the
/// ranking, the ownership and the deck of a seed stay exactly what they were. The draw is one Fisher-Yates shuffle of the
/// fifteen objective numbers, handed out in seat order. A drawn objective that the participant's dealt countries already
/// fulfil is skipped (R§8: "se sortea otro"). With two or three participants nothing is drawn: their only goal is the
/// common one (fifty countries). The drawing logic is the pure function <see cref="Objectives.Assign"/>, so these tests
/// can force any draw sequence and any holdings.
/// </summary>
public sealed class ObjectiveAssignmentTests
{
    private static readonly MapDefinition Map = RealMap;

    private static readonly PlayerColor[] Table = [PlayerColor.Azul, PlayerColor.Rojo, PlayerColor.Negro, PlayerColor.Amarillo];

    // ---- helpers --------------------------------------------------------------------------------------

    private static int NumberOf(Objective objective) => objective switch
    {
        Objective.Occupation occupation => occupation.Number,
        Objective.Destruction destruction => destruction.Number,
        _ => throw new InvalidOperationException($"{objective} has no number."),
    };

    private static IReadOnlySet<string>[] Nobody(int seats = 4) =>
        Enumerable.Range(0, seats).Select(_ => (IReadOnlySet<string>)new HashSet<string>()).ToArray();

    private static HashSet<string> EveryCountry() => Map.Territories.Select(t => t.Id).ToHashSet();

    /// <summary>The countries of the first clauses of objective 1: Africa, 5 of North America and 4 of Europe (R§8).</summary>
    private static HashSet<string> HoldingsOfObjective1() =>
        Map.Continents.Single(c => c.Id == "africa").TerritoryIds
            .Concat(Map.Continents.Single(c => c.Id == "america-del-norte").TerritoryIds.Take(5))
            .Concat(Map.Continents.Single(c => c.Id == "europa").TerritoryIds.Take(4))
            .ToHashSet();

    private static int[] Numbers(IReadOnlyList<Objective> objectives) => objectives.Select(NumberOf).ToArray();

    private static IReadOnlySet<string>[] OwnedBySeat(GameState state) =>
        state.Participants
            .Select(p => (IReadOnlySet<string>)state.Territories.Where(t => t.Owner == p.Seat).Select(t => t.Id).ToHashSet())
            .ToArray();

    /// <summary>
    /// What creation did BEFORE the objectives existed, replayed from the seed the way R§3 states it: one die per participant
    /// (ties re-rolled), the shuffled countries dealt in rank order, then the shuffled deck. The generator ends where the
    /// objective draw has to start.
    /// </summary>
    private sealed record ReplayedCreation(int[] Ranking, Dictionary<string, int> Owners, string[] Deck, SeededRng Rng);

    private static ReplayedCreation ReplayDealAndDeck(int participants, long seed)
    {
        var rng = new SeededRng(seed);
        var ranking = InitialRanking.Determine(participants, rng.RollDie).ToArray();
        var shuffled = Map.Territories.Select(t => t.Id).ToArray();
        rng.Shuffle(shuffled);
        var owners = shuffled
            .Select((id, position) => (Id: id, Owner: ranking[position % ranking.Length]))
            .ToDictionary(deal => deal.Id, deal => deal.Owner);
        var deck = Map.Territories.Select(t => t.Id).ToArray();
        rng.Shuffle(deck);

        return new ReplayedCreation(ranking, owners, deck, rng);
    }

    // ---- the pure function: the next drawn objective goes to each seat --------------------------------------

    [Theory]
    [InlineData(3, 8, 5, 1)]
    [InlineData(9, 2, 7, 4)]
    [InlineData(6, 1, 2, 3)]
    public void R8_Assign_GivesEachSeatTheNextDrawnObjective_InSeatOrder(int first, int second, int third, int fourth)
    {
        var assigned = Objectives.Assign(Table, Nobody(), [first, second, third, fourth, 15, 14], Map);

        Assert.Equal([first, second, third, fourth], Numbers(assigned));
        Assert.All(assigned, objective => Assert.IsType<Objective.Occupation>(objective));
    }

    [Fact]
    public void R8_Assign_ReturnsOneObjectivePerSeat_TheOnesOfTheCatalogue()
    {
        // The destruction objectives drawn here name a colour that is played and is not the participant's own, so they
        // stay as drawn (what happens otherwise is the business of the destruction tests).
        var assigned = Objectives.Assign(Table, Nobody(), [11, 3, 10, 7], Map);

        Assert.Equal(4, assigned.Count);
        Assert.Equal(new Objective.Destruction(11, PlayerColor.Rojo), assigned[0]); // seat 0 is azul, rojo is seat 1
        Assert.Equal(new Objective.Occupation(3), assigned[1]);
        Assert.Equal(new Objective.Destruction(10, PlayerColor.Azul), assigned[2]); // seat 2 is negro, azul is seat 0
        Assert.Equal(new Objective.Occupation(7), assigned[3]);
    }

    // ---- redraw: an objective already fulfilled at the deal is skipped ----------------------------------------

    [Fact]
    public void R8_Assign_AnObjectiveAlreadyFulfilledByTheSeatsCountries_IsRedrawn_TheNextCandidateIsTaken()
    {
        IReadOnlySet<string>[] owned = [HoldingsOfObjective1(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>()];

        var assigned = Objectives.Assign(Table, owned, [1, 5, 9, 3, 7], Map);

        // Seat 0 would have drawn objective 1, which its own countries fulfil: it takes the next candidate (5), and
        // nobody gets 1 (the rejected objective is out of the draw).
        Assert.Equal([5, 9, 3, 7], Numbers(assigned));
    }

    [Fact]
    public void R8_Assign_EachSeatIsJudgedByItsOwnCountries_NotByAnotherSeatsHoldings()
    {
        IReadOnlySet<string>[] objective1OnSeat2 = [new HashSet<string>(), new HashSet<string>(), HoldingsOfObjective1(), new HashSet<string>()];
        IReadOnlySet<string>[] objective1OnSeat3 = [new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), HoldingsOfObjective1()];
        int[] draws = [4, 6, 1, 3, 7];

        // Seat 2 holds objective 1's countries and draws 1 third: redrawn, it takes 3, and seat 3 gets 7.
        Assert.Equal([4, 6, 3, 7], Numbers(Objectives.Assign(Table, objective1OnSeat2, draws, Map)));

        // Seat 3 holds them instead: seat 2 draws 1 and keeps it (its own countries do not fulfil it); seat 3 draws 3.
        Assert.Equal([4, 6, 1, 3], Numbers(Objectives.Assign(Table, objective1OnSeat3, draws, Map)));
    }

    [Fact]
    public void R8_Assign_SeveralFulfilledObjectivesInARow_AreAllSkipped()
    {
        // Seat 0 owns every country: it fulfils each occupation objective, so it skips 1, 2 and 3 and takes the first
        // destruction objective that follows (negro is played by seat 2, so the target stays as drawn).
        IReadOnlySet<string>[] owned = [EveryCountry(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>()];

        var assigned = Objectives.Assign(Table, owned, [1, 2, 3, 12, 5, 6, 7, 8], Map);

        Assert.Equal([12, 5, 6, 7], Numbers(assigned));
        Assert.IsType<Objective.Destruction>(assigned[0]);
    }

    [Fact]
    public void R8_Assign_ADestructionObjectiveIsNeverJudgedFulfilledByTheCountries()
    {
        IReadOnlySet<string>[] owned = [EveryCountry(), EveryCountry(), EveryCountry(), EveryCountry()];

        var assigned = Objectives.Assign(Table, owned, [10, 11, 12, 13], Map);

        Assert.Equal([10, 11, 12, 13], Numbers(assigned)); // nothing was skipped
    }

    // ---- never repeated, and the draw can run out ----------------------------------------------------------------

    [Fact]
    public void R8_Assign_NeverRepeatsAnObjective_ANumberDrawnTwiceIsSkipped()
    {
        var assigned = Objectives.Assign(Table, Nobody(), [3, 3, 5, 3, 8, 8, 1, 2], Map);

        Assert.Equal([3, 5, 8, 1], Numbers(assigned));
    }

    [Fact]
    public void R8_Assign_WithTooFewCandidates_ForTheSeats_IsAnError()
    {
        Assert.Throws<InvalidOperationException>(() => Objectives.Assign(Table, Nobody(), [1, 2, 3], Map));
        Assert.Throws<InvalidOperationException>(() => Objectives.Assign(Table, Nobody(), [1, 2, 2, 1], Map));
    }

    // ---- the draw: one shuffle of the fifteen numbers --------------------------------------------------------------

    [Fact]
    public void R8_TheDrawSequence_IsAPermutationOfOneToFifteen()
    {
        var sequence = Objectives.DrawSequence(new SeededRng(7));

        Assert.Equal(Enumerable.Range(1, 15), sequence.Order());
        Assert.Equal(15, sequence.Distinct().Count());
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(42L)]
    [InlineData(-9L)]
    public void R8_TheDrawSequence_IsTheFisherYatesShuffleOfTheFifteenNumbers_AndConsumesExactlyThoseDraws(long seed)
    {
        var oracle = new SeededRng(seed);
        var expected = Enumerable.Range(1, 15).ToArray();
        oracle.Shuffle(expected);

        var rng = new SeededRng(seed);
        var sequence = Objectives.DrawSequence(rng);

        Assert.Equal(expected, sequence);
        Assert.Equal(oracle.State, rng.State);
        Assert.True(rng.Draws >= 14, $"A shuffle of 15 takes 14 draws at least, took {rng.Draws}.");
    }

    [Fact]
    public void R8_TheDrawSequence_DependsOnTheSeed_AndEveryObjectiveCanComeFirst()
    {
        var first = Enumerable.Range(0, 600).Select(seed => Objectives.DrawSequence(new SeededRng(seed))[0]).ToArray();

        Assert.Equal(Enumerable.Range(1, 15), first.Distinct().Order()); // all fifteen led a draw at some seed
        Assert.NotEqual(
            Objectives.DrawSequence(new SeededRng(1)),
            Objectives.DrawSequence(new SeededRng(2)));
        Assert.Equal(
            Objectives.DrawSequence(new SeededRng(5)),
            Objectives.DrawSequence(new SeededRng(5)));
    }

    // ---- creation of a game ----------------------------------------------------------------------------------------

    [Fact]
    public void R8_Create_WithFourParticipants_GivesEachOneADistinctSecretObjective()
    {
        var state = Deal(4, seed: 11);

        var numbers = state.Participants.Select(p => NumberOf(p.Objective)).ToArray();

        Assert.All(state.Participants, p => Assert.NotEqual(Objective.None, p.Objective));
        Assert.Equal(4, numbers.Distinct().Count());
        Assert.All(numbers, number => Assert.InRange(number, 1, 15));
    }

    [Fact]
    public void R8_Create_TheObjectivesAreTheFirstFourOfTheSeededDraw_AfterTheDealAndTheDeck()
    {
        for (var seed = 1L; seed <= 60; seed++)
        {
            var replay = ReplayDealAndDeck(4, seed);
            var sequence = Enumerable.Range(1, 15).ToArray();
            replay.Rng.Shuffle(sequence); // the very next draws of the generator after the deal and the deck

            var state = Deal(4, seed);

            var expected = Objectives.Assign(Table, OwnedBySeat(state), sequence, Map);
            Assert.Equal(expected, state.Participants.Select(p => p.Objective).ToArray());
            Assert.Equal(sequence.Take(4), state.Participants.Select(p => NumberOf(p.Objective))); // nobody needed a redraw
            Assert.Equal(replay.Rng.State, state.Rng); // the state carries the generator AFTER the objective draw
        }
    }

    [Fact]
    public void R8_Create_TheRankingOwnershipAndDeckOfASeed_AreIdenticalToBeforeTheObjectivesExisted()
    {
        for (var seed = 1L; seed <= 60; seed++)
        {
            var replay = ReplayDealAndDeck(4, seed);

            var state = Deal(4, seed);

            Assert.Equal(replay.Ranking, state.Ranking.ToArray());
            Assert.Equal(
                Map.Territories.Select(t => (t.Id, Owner: replay.Owners[t.Id])).ToArray(),
                state.Territories.Select(t => (t.Id, t.Owner)).ToArray());
            Assert.Equal(replay.Deck, state.Deck.Draw.ToArray());
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void R8_Create_WithTwoOrThreeParticipants_NobodyHasASecretObjective_AndTheRngTakesNoExtraDraw(int participants)
    {
        for (var seed = 1L; seed <= 40; seed++)
        {
            var replay = ReplayDealAndDeck(participants, seed);

            var state = Deal(participants, seed);

            Assert.Equal(participants, state.Participants.Count);
            Assert.All(state.Participants, p => Assert.Equal(Objective.None, p.Objective));
            Assert.Equal(replay.Rng.State, state.Rng); // the generator ends right after the deck shuffle
        }
    }

    [Fact]
    public void R8_AParticipantBuiltByHand_HasNoObjective_SoOlderFixturesKeepTheirMeaning()
    {
        var participant = new Participant(0, "Ana", ParticipantKind.Human, PlayerColor.Azul);

        Assert.Equal(Objective.None, participant.Objective);
        Assert.Equal(participant, participant with { Objective = Objective.None });
        Assert.NotEqual(participant, participant with { Objective = new Objective.Occupation(1) });
    }

    // ---- over many seeds ----------------------------------------------------------------------------------------------

    [Fact]
    public void R8_Create_OverManySeeds_ObjectivesAreDistinctPerGame_AndNoneIsFulfilledAtTheDeal()
    {
        var seen = new HashSet<int>();

        for (var seed = 0L; seed < 400; seed++)
        {
            var state = Deal(4, seed);
            var owned = OwnedBySeat(state);

            var numbers = state.Participants.Select(p => NumberOf(p.Objective)).ToArray();
            Assert.Equal(4, numbers.Distinct().Count());
            foreach (var participant in state.Participants)
            {
                Assert.False(
                    Objectives.IsFulfilled(participant.Objective, owned[participant.Seat], Map),
                    $"seed {seed}: seat {participant.Seat} was dealt {participant.Objective}, which its countries already fulfil.");
                seen.Add(NumberOf(participant.Objective));
            }
        }

        Assert.Equal(Enumerable.Range(1, 15), seen.Order()); // not vacuous: every objective is dealt at some seed
    }

    [Fact]
    public void E10_TheSameSeedGivesTheSameObjectives_ADifferentSeedCanGiveOthers()
    {
        var first = Deal(4, seed: 20260101).Participants.Select(p => p.Objective).ToArray();
        var again = Deal(4, seed: 20260101).Participants.Select(p => p.Objective).ToArray();
        var other = Deal(4, seed: 20260102).Participants.Select(p => p.Objective).ToArray();

        Assert.Equal(first, again);
        Assert.NotEqual(first, other);
    }
}
