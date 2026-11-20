using Teg.Domain.Events;
using Teg.Domain.Rules;
using Teg.Domain.State;
using static Teg.Domain.Tests.Support.GameFixtures;

namespace Teg.Domain.Tests.Placement;

/// <summary>
/// E2 + E4 / R§3 "Vueltas de colocación inicial": placement round 1, then round 2, in table order from rank 1.
/// On each turn the active participant must place the ENTIRE pool of the round (5 / 3, or 6 / 6 with two
/// participants) on their own countries, distributed as they like. After the last participant of round 2 the
/// first hostilities round starts (R§4).
/// </summary>
public sealed class PlacementTests
{
    // ---- order of play ------------------------------------------------------------------------

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Placement_PlaysRoundOneThenRoundTwo_InTableOrderStartingAtRankOne(int participants)
    {
        var rankOnes = new HashSet<int>();

        for (var seed = 1L; seed <= 24; seed++)
        {
            var state = Deal(participants, seed);
            var rankOne = state.Ranking[0];
            var turns = new List<(int Seat, int Round)>();

            for (var turn = 0; turn < participants * 2; turn++)
            {
                Assert.Equal(GamePhase.InitialPlacement, state.Phase);
                turns.Add((state.ActiveSeat, state.PlacementRound));
                state = ApplyOk(state, WholePoolOnFirstCountry(state)).State;
            }

            // R§2/R§3: registration order, circular, starting from rank 1 (oracle built here, not taken from the state).
            var tableFromRankOne = Enumerable.Range(0, participants)
                .Select(offset => (rankOne + offset) % participants)
                .ToArray();

            Assert.Equal(tableFromRankOne.Concat(tableFromRankOne), turns.Select(t => t.Seat));
            Assert.Equal(
                Enumerable.Repeat(1, participants).Concat(Enumerable.Repeat(2, participants)),
                turns.Select(t => t.Round));
            rankOnes.Add(rankOne);
        }

        Assert.True(rankOnes.Count > 1, "Different seeds must open with different seats, so the wrap-around is exercised.");
    }

    // ---- accepted placements ------------------------------------------------------------------

    [Theory]
    [InlineData(2, 6)]
    [InlineData(3, 5)]
    [InlineData(4, 5)]
    public void RoundOne_TheActivePlayerPlacesTheWholePool_OnOneOwnCountry(int participants, int roundOnePool)
    {
        var state = Deal(participants);
        var seat = state.ActiveSeat;
        var target = OwnedBy(state, seat)[0];

        var next = ApplyOk(state, Place(seat, (target, roundOnePool))).State;

        Assert.Equal(1 + roundOnePool, ArmiesIn(next, target));
        Assert.Equal(ArmiesOf(state, seat) + roundOnePool, ArmiesOf(next, seat));
        Assert.Equal(
            state.Territories.Where(t => t.Id != target),
            next.Territories.Where(t => t.Id != target));
    }

    [Theory]
    [InlineData(2, 6)]
    [InlineData(3, 3)]
    [InlineData(4, 3)]
    public void RoundTwo_ThePoolIsTheRoundTwoPool_AndStartsAgainAtTheFirstPlayer(int participants, int roundTwoPool)
    {
        var state = FinishPlacementRound(Deal(participants));

        Assert.Equal(2, state.PlacementRound);
        Assert.Equal(state.Ranking[0], state.ActiveSeat);

        var seat = state.ActiveSeat;
        var target = OwnedBy(state, seat)[0];
        var armiesBefore = ArmiesIn(state, target);

        var next = ApplyOk(state, Place(seat, (target, roundTwoPool))).State;

        Assert.Equal(armiesBefore + roundTwoPool, ArmiesIn(next, target));
    }

    [Fact]
    public void RoundOne_AnyDistributionOfThePool_AcrossSeveralOwnCountries_IsAccepted()
    {
        var state = Deal(3, seed: 4);
        var seat = state.ActiveSeat;
        var own = OwnedBy(state, seat);

        var next = ApplyOk(state, Place(seat, (own[0], 2), (own[1], 1), (own[2], 2))).State;

        Assert.Equal([3, 2, 3, 1], own.Take(4).Select(id => ArmiesIn(next, id))); // 4th country untouched
        Assert.Equal(ArmiesOf(state, seat) + 5, ArmiesOf(next, seat));
    }

    [Fact]
    public void RoundOne_OneArmyOnEachOfFiveCountries_IsAccepted()
    {
        var state = Deal(4, seed: 2);
        var seat = state.ActiveSeat;
        var own = OwnedBy(state, seat);

        var next = ApplyOk(state, Place(seat, (own[0], 1), (own[1], 1), (own[2], 1), (own[3], 1), (own[4], 1))).State;

        Assert.Equal([2, 2, 2, 2, 2, 1], own.Take(6).Select(id => ArmiesIn(next, id)));
    }

    [Fact]
    public void RoundOne_SeveralEntriesForTheSameCountry_AreAddedUp()
    {
        var state = Deal(3, seed: 4);
        var seat = state.ActiveSeat;
        var own = OwnedBy(state, seat);

        var next = ApplyOk(state, Place(seat, (own[0], 2), (own[0], 3))).State;

        Assert.Equal(1 + 5, ArmiesIn(next, own[0]));
    }

    // ---- rejections (each leaves the state untouched, E4) ------------------------------------------

    private static string EnemyCountry(GameState state, int seat) =>
        state.Territories.First(t => t.Owner != seat).Id;

    [Theory]
    [InlineData(2, 6)]
    [InlineData(3, 5)]
    [InlineData(4, 5)]
    public void PlacingThePoolOnACountryOfAnotherParticipant_IsRejected(int participants, int pool)
    {
        AssertRejected(
            RuleViolations.TerritoryNotOwned,
            () => Deal(participants, seed: 9),
            state => Place(state.ActiveSeat, (EnemyCountry(state, state.ActiveSeat), pool)));
    }

    [Fact]
    public void PlacingPartOfThePoolOnAnEnemyCountry_RejectsTheWholeAction()
    {
        AssertRejected(
            RuleViolations.TerritoryNotOwned,
            () => Deal(3, seed: 9),
            state => Place(
                state.ActiveSeat,
                (OwnedBy(state, state.ActiveSeat)[0], 4),
                (EnemyCountry(state, state.ActiveSeat), 1)));
    }

    [Fact]
    public void PlacingOnACountryThatDoesNotExist_IsRejected()
    {
        AssertRejected(
            RuleViolations.UnknownTerritory,
            () => Deal(3, seed: 9),
            state => Place(state.ActiveSeat, (OwnedBy(state, state.ActiveSeat)[0], 4), ("no-existe", 1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(10)]
    public void PlacingLessOrMoreThanTheWholePool_IsRejected(int total)
    {
        // 3 participants: the round-1 pool is exactly 5.
        AssertRejected(
            RuleViolations.WrongPlacementTotal,
            () => Deal(3, seed: 9),
            state => total == 0
                ? Place(state.ActiveSeat)
                : Place(state.ActiveSeat, (OwnedBy(state, state.ActiveSeat)[0], total)));
    }

    [Fact]
    public void PlacingAbsurdlyLargeCounts_IsRefusedAsAWrongTotal_InsteadOfOverflowing()
    {
        AssertRejected(
            RuleViolations.WrongPlacementTotal,
            () => Deal(3, seed: 9),
            state =>
            {
                var own = OwnedBy(state, state.ActiveSeat);
                return Place(state.ActiveSeat, (own[0], int.MaxValue), (own[1], int.MaxValue));
            });
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void TheRoundOnePool_IsNotValidInRoundTwo(int participants)
    {
        // Round 1 pool is 5, round 2 pool is 3: placing 5 in round 2 is too much.
        AssertRejected(
            RuleViolations.WrongPlacementTotal,
            () => FinishPlacementRound(Deal(participants, seed: 9)),
            state => Place(state.ActiveSeat, (OwnedBy(state, state.ActiveSeat)[0], 5)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-3)]
    public void AnEntryWithNoArmiesOrNegativeArmies_IsRejected_EvenWhenTheTotalAddsUp(int offsettingEntry)
    {
        // Entries sum to the pool of 5, but one of them is zero or negative.
        AssertRejected(
            RuleViolations.InvalidArmyCount,
            () => Deal(3, seed: 9),
            state =>
            {
                var own = OwnedBy(state, state.ActiveSeat);
                return Place(state.ActiveSeat, (own[0], 5 - offsettingEntry), (own[1], offsettingEntry));
            });
    }

    [Theory]
    [InlineData(2, 6)]
    [InlineData(3, 5)]
    [InlineData(4, 5)]
    public void AnotherParticipantActingOutOfTurn_IsRejected(int participants, int roundOnePool)
    {
        var activeSeat = Deal(participants, seed: 9).ActiveSeat;
        var others = Enumerable.Range(0, participants).Where(seat => seat != activeSeat).ToArray();

        Assert.Equal(participants - 1, others.Length);

        foreach (var intruder in others)
        {
            AssertRejected(
                RuleViolations.NotActiveParticipant,
                () => Deal(participants, seed: 9),
                state => Place(intruder, (OwnedBy(state, intruder)[0], roundOnePool)));
        }
    }

    [Fact]
    public void AParticipantWhoAlreadyPlacedInTheRound_CannotPlaceAgain()
    {
        AssertRejected(
            RuleViolations.NotActiveParticipant,
            () =>
            {
                var state = Deal(3, seed: 9);
                return ApplyOk(state, WholePoolOnFirstCountry(state)).State; // the first player is done; the turn moved on
            },
            state => Place(state.Ranking[0], (OwnedBy(state, state.Ranking[0])[0], 5)));
    }

    [Fact]
    public void Placing_AfterBothPlacementRounds_IsInTheWrongPhase()
    {
        AssertRejected(
            RuleViolations.WrongPhase,
            () => FinishPlacementRound(FinishPlacementRound(Deal(3, seed: 9))),
            state => Place(state.ActiveSeat, (OwnedBy(state, state.ActiveSeat)[0], 5)));
    }

    // ---- end of the placement rounds ---------------------------------------------------------------

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void WhenTheLastParticipantFinishesRoundTwo_TheFirstHostilitiesRoundStarts_WithoutReinforce(int participants)
    {
        for (var seed = 1L; seed <= 12; seed++)
        {
            var dealt = Deal(participants, seed);

            var state = FinishPlacementRound(FinishPlacementRound(dealt));

            Assert.Equal(GamePhase.Attack, state.Phase); // R§4: the first round has no Refuerzo
            Assert.Equal(dealt.Ranking[0], state.ActiveSeat);
            Assert.Equal(1, state.Round);
            Assert.True(state.IsHostilitiesRound);
        }
    }

    [Theory]
    [InlineData(2, new[] { 37, 37 })]
    [InlineData(3, new[] { 25, 25, 24 })]
    [InlineData(4, new[] { 21, 21, 20, 20 })]
    public void WhenEveryParticipantPlacesBothPools_TheArmiesEndAtTheTableTotals_ByRank(int participants, int[] totalsByRank)
    {
        for (var seed = 1L; seed <= 20; seed++)
        {
            var state = FinishPlacementRound(FinishPlacementRound(Deal(participants, seed)));

            Assert.Equal(totalsByRank, state.Ranking.Select(seat => ArmiesOf(state, seat)));
            Assert.Equal(totalsByRank.Sum(), state.Territories.Sum(t => t.Armies));
        }
    }

    // ---- events and version -------------------------------------------------------------------------

    [Fact]
    public void AMidRoundPlacement_IsRecorded_ThenTheTurnPassesToTheNextParticipantInTableOrder()
    {
        var state = Deal(3, seed: 4);
        var seat = state.ActiveSeat;
        var next = (seat + 1) % 3;
        var own = OwnedBy(state, seat);
        var command = Place(seat, (own[0], 3), (own[1], 2));

        var transition = ApplyOk(state, command);

        GameEvent[] expected =
        [
            new InitialArmiesPlaced(seat, PlacementRound: 1, command.Placements),
            new TurnEnded(seat),
            new TurnStarted(next, Round: 0, GamePhase.InitialPlacement),
        ];
        Assert.Equal(expected, transition.Events.ToArray());
        Assert.Equal(next, transition.State.ActiveSeat);
        Assert.Equal(state.Rng, transition.State.Rng); // placing takes no dice
    }

    [Fact]
    public void TheLastPlacementOfRoundOne_PassesTheTurnBackToTheFirstPlayer_ForRoundTwo()
    {
        var state = Deal(3, seed: 4);
        var first = state.Ranking[0];
        state = ApplyOk(state, WholePoolOnFirstCountry(state)).State;
        state = ApplyOk(state, WholePoolOnFirstCountry(state)).State;
        var last = state.ActiveSeat;
        var command = Place(last, (OwnedBy(state, last)[0], 5));

        var transition = ApplyOk(state, command);

        GameEvent[] expected =
        [
            new InitialArmiesPlaced(last, PlacementRound: 1, command.Placements),
            new TurnEnded(last),
            new TurnStarted(first, Round: 0, GamePhase.InitialPlacement),
        ];
        Assert.Equal(expected, transition.Events.ToArray());
        Assert.Equal(2, transition.State.PlacementRound);
        Assert.Equal(GamePhase.InitialPlacement, transition.State.Phase);
    }

    [Fact]
    public void TheLastPlacementOfRoundTwo_AdvancesTheRoundCounterToOne_AndStartsTheFirstTurnInAttack()
    {
        var state = FinishPlacementRound(Deal(3, seed: 4));
        var first = state.Ranking[0];
        state = ApplyOk(state, WholePoolOnFirstCountry(state)).State;
        state = ApplyOk(state, WholePoolOnFirstCountry(state)).State;
        var last = state.ActiveSeat;
        var command = Place(last, (OwnedBy(state, last)[0], 3));

        var transition = ApplyOk(state, command);

        GameEvent[] expected =
        [
            new InitialArmiesPlaced(last, PlacementRound: 2, command.Placements),
            new TurnEnded(last),
            new RoundAdvanced(1),
            new TurnStarted(first, Round: 1, GamePhase.Attack),
        ];
        Assert.Equal(expected, transition.Events.ToArray());
    }

    [Fact]
    public void Version_StartsAtZero_AndGrowsByExactlyOnePerAppliedPlacement()
    {
        var state = Deal(3, seed: 9);
        Assert.Equal(0, state.Version);

        for (var turn = 1; turn <= 6; turn++)
        {
            state = ApplyOk(state, WholePoolOnFirstCountry(state)).State;

            Assert.Equal(turn, state.Version);
        }
    }
}
