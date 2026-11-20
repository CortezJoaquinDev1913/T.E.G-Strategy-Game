using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Events;
using Teg.Domain.Rng;
using Teg.Domain.Rules;
using Teg.Domain.State;
using Teg.Domain.Tests.Support;
using static Teg.Domain.Tests.Support.CombatFixtures;
using static Teg.Domain.Tests.Support.GameFixtures;
using static Teg.Domain.Tests.Support.ReinforcementFixtures;

namespace Teg.Domain.Tests.Combat;

/// <summary>
/// E5 / R§6, the conquest. When an attack leaves the destination without armies the country changes owner AT ONCE
/// (the conquering army enters it, so no country is ever empty and the state stays valid), the occupation is PENDING and
/// the attacker MUST decide how many armies the conquered country ends up with: 1 to min(3, origin armies before the
/// attack - 1), leaving at least one army at the origin (<see cref="MoveAfterConquest"/> carries that TOTAL). While the
/// occupation is pending ONLY that command is accepted. The conquered country can attack on in the same turn when it
/// has two armies or more, and the turn counts its conquests (<see cref="TurnContext.ConquestsThisTurn"/>, read by the
/// card phase), starting again from zero with every turn. The attacker never loses an army in the attack that conquers
/// (the defender dies pair by pair, and a pair the defender loses costs the attacker nothing).
/// </summary>
public sealed class ConquestTests
{
    /// <summary>"a" (seat 0, <paramref name="origin"/> armies) conquers "c" (seat 1, <paramref name="defenders"/> armies): the occupation is pending.</summary>
    private static GameState AfterConquest(int origin, int defenders = 1) => Conquer(Arena(origin, defenders), "a", "c");

    /// <summary>Commands that would be valid in Ataque, were the occupation not pending.</summary>
    private static GameCommand OtherCommand(string name) => name switch
    {
        "Attack" => new Attack(0, "b", "d"), // b has 3 armies, d belongs to seat 1: a fine attack
        "StartRegroup" => new StartRegroup(0),
        "EndTurn" => new EndTurn(0),
        "Regroup" => new Regroup(0, "a", "b", 1),
        "PlaceReinforcements" => Reinforcing(0, ("a", 1)),
        "TradeCards" => Trading(0, "a", "b", "c"),
        "PlaceInitialArmies" => Place(0, ("a", 6)),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    // ---- the conquest itself -----------------------------------------------------------------------------

    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 3)]
    [InlineData(9, 1)]
    [InlineData(9, 3)]
    public void R6_WhenTheDestinationReachesZero_TheCountryChangesOwner_AndTheOccupationIsPending(int origin, int defenders)
    {
        var before = WithDice(Arena(origin, defenders), "a", "c", Conquers(defenders));
        var (attackerDice, defenderDice, _) = ReplayDice(before.Rng, Math.Min(3, origin - 1), Math.Min(3, defenders));

        var transition = ApplyOk(before, new Attack(0, "a", "c"), TinyBoard.Map);
        var next = transition.State;

        GameEvent[] expected =
        [
            new AttackResolved(0, "a", "c", attackerDice.ToValueArray(), defenderDice.ToValueArray(), AttackerLosses: 0, DefenderLosses: defenders),
            new TerritoryConquered(0, "a", "c", PreviousOwner: 1),
        ];
        Assert.Equal(expected, transition.Events.ToArray());
        Assert.Equal(0, next.FindTerritory("c")!.Owner);
        Assert.Equal(1, ArmiesIn(next, "c")); // the conquering army is already in
        Assert.Equal(origin - 1, ArmiesIn(next, "a"));
        Assert.Equal(new PendingConquest("a", "c"), next.Turn.PendingConquest);
        Assert.Equal(1, next.Turn.ConquestsThisTurn);
        Assert.Equal((GamePhase.Attack, 0), (next.Phase, next.ActiveSeat));
        Assert.Equal(before.Version + 1, next.Version);
    }

    [Fact]
    public void R6_TheConquestChangesNothingElse_TheOtherCountriesAndParticipantsStayAsTheyWere()
    {
        var before = WithDice(Arena(6, 2), "a", "c", Conquers(2));
        var (attackerDice, defenderDice, after) = ReplayDice(before.Rng, 3, 2);

        var next = ApplyOk(before, new Attack(0, "a", "c"), TinyBoard.Map).State;

        var expected = Owners(Armies(before, ("a", 5), ("c", 1)), ("c", 0)) with
        {
            Rng = after,
            Version = 1,
            Turn = before.Turn with { ConquestsThisTurn = 1, PendingConquest = new PendingConquest("a", "c") },
        };
        Assert.Equal(expected, next);
        Assert.NotEmpty(attackerDice);
        Assert.NotEmpty(defenderDice);
    }

    [Fact]
    public void R6_TheAttackerLosesNothingInTheAttackThatConquers_OverManyCases()
    {
        var conquests = 0;

        foreach (var (origin, defenders) in new[] { (2, 1), (3, 1), (3, 2), (4, 3), (5, 2), (9, 3) })
        {
            for (var seed = 1L; seed <= 200; seed++)
            {
                var state = Arena(origin, defenders) with { Rng = new RngState(seed, Draws: 0) };

                var transition = ApplyOk(state, new Attack(0, "a", "c"), TinyBoard.Map);

                var resolved = Assert.Single(transition.Events.OfType<AttackResolved>());
                if (transition.Events.OfType<TerritoryConquered>().Any())
                {
                    Assert.Equal(0, resolved.AttackerLosses);
                    Assert.Equal(defenders, resolved.DefenderLosses);
                    conquests++;
                }
            }
        }

        Assert.True(conquests > 100, $"Only {conquests} conquests were reached, the check would be vacuous.");
    }

    [Fact]
    public void R6_AFightThatLeavesTheDefenderWithAnArmy_IsNotAConquest()
    {
        var state = WithDice(Arena(8, 2), "a", "c", (attacker, defender) => Oracle(attacker, defender) == (1, 1));

        var transition = ApplyOk(state, new Attack(0, "a", "c"), TinyBoard.Map);

        Assert.DoesNotContain(transition.Events, e => e is TerritoryConquered);
        Assert.Equal(1, transition.State.FindTerritory("c")!.Owner);
        Assert.Null(transition.State.Turn.PendingConquest);
        Assert.Equal(0, transition.State.Turn.ConquestsThisTurn);
    }

    // ---- while the occupation is pending, only MoveAfterConquest is accepted ---------------------------------------

    [Theory]
    [InlineData("Attack")]
    [InlineData("StartRegroup")]
    [InlineData("EndTurn")]
    [InlineData("Regroup")]
    [InlineData("PlaceReinforcements")]
    [InlineData("TradeCards")]
    [InlineData("PlaceInitialArmies")]
    public void R6_WhileTheOccupationIsPending_EveryOtherCommandIsRejected_AndTheStateIsUntouched(string command)
    {
        AssertRejected(
            RuleViolations.ConquestPending,
            () => AfterConquest(origin: 6),
            _ => OtherCommand(command),
            TinyBoard.Map);
    }

    [Fact]
    public void R6_AnAttackThatWouldBeFineIsRejectedAsPending_ButFineOnceTheOccupationIsDone()
    {
        var pending = AfterConquest(origin: 6);
        Assert.Equal(RuleViolations.ConquestPending, GameEngine.Apply(pending, OtherCommand("Attack"), TinyBoard.Map).Error.Code);

        var occupied = ApplyOk(pending, new MoveAfterConquest(0, 1), TinyBoard.Map).State;

        Assert.True(GameEngine.Apply(occupied, OtherCommand("Attack"), TinyBoard.Map).IsSuccess);
        Assert.True(GameEngine.Apply(occupied, new EndTurn(0), TinyBoard.Map).IsSuccess);
        Assert.True(GameEngine.Apply(occupied, new StartRegroup(0), TinyBoard.Map).IsSuccess);
    }

    [Fact]
    public void R6_ACommandFromAnotherParticipant_IsStillRefusedAsNotTheirTurn_WhileTheOccupationIsPending()
    {
        AssertRejected(
            RuleViolations.NotActiveParticipant,
            () => AfterConquest(origin: 6),
            _ => new MoveAfterConquest(1, 1),
            TinyBoard.Map);
    }

    // ---- moving armies after the conquest ------------------------------------------------------------------

    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 1)]
    [InlineData(4, 3)]
    [InlineData(5, 2)]
    [InlineData(10, 1)]
    [InlineData(10, 3)]
    public void R6_TheAttackerLeavesBetweenOneAndMinThreeOriginMinusOneArmies_OnTheConqueredCountry(int origin, int moved)
    {
        var state = AfterConquest(origin);

        var transition = ApplyOk(state, new MoveAfterConquest(0, moved), TinyBoard.Map);
        var next = transition.State;

        Assert.Equal(moved, ArmiesIn(next, "c"));
        Assert.Equal(origin - moved, ArmiesIn(next, "a"));
        Assert.Equal(0, next.FindTerritory("c")!.Owner);
        Assert.Null(next.Turn.PendingConquest);
        Assert.Equal(1, next.Turn.ConquestsThisTurn);
        Assert.Equal((GamePhase.Attack, 0), (next.Phase, next.ActiveSeat));
        GameEvent[] expected = [new ArmiesMovedAfterConquest(0, "a", "c", moved)];
        Assert.Equal(expected, transition.Events.ToArray());

        var untouched = Armies(state, ("a", origin - moved), ("c", moved)) with
        {
            Version = state.Version + 1,
            Turn = state.Turn with { PendingConquest = null },
        };
        Assert.Equal(untouched, next);
    }

    [Fact]
    public void R6_EveryAmountFromOneToTheLimitIsAccepted_AndTheNextOneIsRefused_ForEveryOriginSize()
    {
        for (var origin = 2; origin <= 12; origin++)
        {
            var limit = Math.Min(3, origin - 1);

            for (var moved = 1; moved <= limit; moved++)
            {
                Assert.True(
                    GameEngine.Apply(AfterConquest(origin), new MoveAfterConquest(0, moved), TinyBoard.Map).IsSuccess,
                    $"{moved} of {origin} should be allowed");
            }

            var tooMany = GameEngine.Apply(AfterConquest(origin), new MoveAfterConquest(0, limit + 1), TinyBoard.Map);
            Assert.False(tooMany.IsSuccess, $"{limit + 1} of {origin} should be refused");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-7)]
    public void R6_MovingNoArmiesOrANegativeNumber_IsRejected(int moved)
    {
        AssertRejected(
            RuleViolations.InvalidArmyCount,
            () => AfterConquest(origin: 8),
            _ => new MoveAfterConquest(0, moved),
            TinyBoard.Map);
    }

    [Theory]
    [InlineData(10, 4)] // plenty of armies at the origin, but the limit is three
    [InlineData(10, 9)]
    [InlineData(5, 4)]
    public void R6_MovingMoreThanThree_IsRejected(int origin, int moved)
    {
        AssertRejected(
            RuleViolations.OccupationLimitExceeded,
            () => AfterConquest(origin),
            _ => new MoveAfterConquest(0, moved),
            TinyBoard.Map);
    }

    [Theory]
    [InlineData(2, 2)] // 2 armies: one must stay
    [InlineData(3, 3)]
    [InlineData(4, 4)] // a conqueror with 4 armies can send 3 and keep 1, never all 4
    [InlineData(3, 5)] // over both limits: the origin cannot spare them
    public void R6_MovingEveryArmyOfTheOrigin_IsRejected_OneMustStayBehind(int origin, int moved)
    {
        AssertRejected(
            RuleViolations.NotEnoughArmies,
            () => AfterConquest(origin),
            _ => new MoveAfterConquest(0, moved),
            TinyBoard.Map);
    }

    [Fact]
    public void R6_ARejectedOccupation_KeepsItPending_SoTheAttackerCanDecideAgain()
    {
        var state = AfterConquest(origin: 6);

        var refused = GameEngine.Apply(state, new MoveAfterConquest(0, 9), TinyBoard.Map);
        var accepted = GameEngine.Apply(state, new MoveAfterConquest(0, 3), TinyBoard.Map);

        Assert.False(refused.IsSuccess);
        Assert.True(accepted.IsSuccess);
        Assert.Equal(3, ArmiesIn(accepted.Value.State, "c"));
    }

    [Fact]
    public void R6_TheOccupationCanOnlyBeDecidedOnce()
    {
        AssertRejected(
            RuleViolations.NoConquestPending,
            () => ApplyOk(AfterConquest(origin: 6), new MoveAfterConquest(0, 2), TinyBoard.Map).State,
            _ => new MoveAfterConquest(0, 1),
            TinyBoard.Map);
    }

    [Fact]
    public void R6_WithNoConquestPending_MovingArmiesAfterAConquestIsRejected()
    {
        AssertRejected(
            RuleViolations.NoConquestPending,
            () => Arena(6, 3),
            _ => new MoveAfterConquest(0, 1),
            TinyBoard.Map);
    }

    [Theory]
    [InlineData(GamePhase.InitialPlacement)]
    [InlineData(GamePhase.Reinforcement)]
    [InlineData(GamePhase.Regroup)]
    [InlineData(GamePhase.Card)]
    public void R6_MovingArmiesAfterAConquest_OutsideTheAttackPhase_IsAWrongPhase(GamePhase phase)
    {
        AssertRejected(
            RuleViolations.WrongPhase,
            () => TinyBoard.State(phase),
            _ => new MoveAfterConquest(0, 1),
            TinyBoard.Map);
    }

    [Fact]
    public void R6_MovingArmiesAfterAConquest_OnAFinishedGame_IsRefused()
    {
        AssertRejected(
            RuleViolations.GameFinished,
            () => TinyBoard.State(GamePhase.Finished),
            _ => new MoveAfterConquest(0, 1),
            TinyBoard.Map);
    }

    // ---- the conquered country attacks on ---------------------------------------------------------------------

    [Theory]
    [InlineData(2, 1)] // two armies: one die
    [InlineData(3, 2)] // three armies: two dice
    public void R6_TheConqueredCountry_CanAttackInTheSameTurn_WhenItHasTwoArmiesOrMore(int moved, int attackerDice)
    {
        var occupied = ApplyOk(AfterConquest(origin: 8), new MoveAfterConquest(0, moved), TinyBoard.Map).State;

        var transition = ApplyOk(occupied, new Attack(0, "c", "d"), TinyBoard.Map); // c touches d by land

        var resolved = Assert.Single(transition.Events.OfType<AttackResolved>());
        Assert.Equal(("c", "d"), (resolved.From, resolved.To));
        Assert.Equal(attackerDice, resolved.AttackerDice.Count);
    }

    [Fact]
    public void R6_TheConqueredCountry_WithASingleArmy_CannotAttackYet()
    {
        AssertRejected(
            RuleViolations.OriginTooFewArmies,
            () => ApplyOk(AfterConquest(origin: 8), new MoveAfterConquest(0, 1), TinyBoard.Map).State,
            _ => new Attack(0, "c", "d"),
            TinyBoard.Map);
    }

    [Fact]
    public void R6_ConquestsCanChain_TheConqueredCountryConquersTheNextOne_AndEachCountsInTheTurn()
    {
        // Seat 1 keeps "f": taking c and d does not eliminate it, so the chain does not win the game (R§8).
        var state = Owners(Armies(Arena(9, 1), ("d", 1)), ("f", 1));
        state = ApplyOk(Conquer(state, "a", "c"), new MoveAfterConquest(0, 3), TinyBoard.Map).State;
        Assert.Equal(1, state.Turn.ConquestsThisTurn);

        state = Conquer(state, "c", "d"); // c has 3 armies: two dice against one

        Assert.Equal(2, state.Turn.ConquestsThisTurn);
        Assert.Equal(new PendingConquest("c", "d"), state.Turn.PendingConquest);
        Assert.Equal(0, state.FindTerritory("d")!.Owner);
        Assert.Equal(2, ArmiesIn(state, "c")); // the conquering army of d came from c
    }

    // ---- the per-turn conquest counter -------------------------------------------------------------------------

    [Fact]
    public void R6_TheCounterOfTheTurn_StartsAtZero_AndOnlyConquestsMoveIt()
    {
        var start = Arena(8, 9);
        Assert.Equal(0, start.Turn.ConquestsThisTurn);

        var fought = ApplyOk(start, new Attack(0, "a", "c"), TinyBoard.Map).State;
        Assert.Equal(0, fought.Turn.ConquestsThisTurn); // an attack without conquest is not counted

        var conquered = AfterConquest(origin: 8);
        Assert.Equal(1, conquered.Turn.ConquestsThisTurn);

        var occupied = ApplyOk(conquered, new MoveAfterConquest(0, 2), TinyBoard.Map).State;
        Assert.Equal(1, occupied.Turn.ConquestsThisTurn); // moving the armies does not count twice
    }

    [Fact]
    public void R6_TheCounter_IsResetAtTheStartOfEveryTurn_ForEveryParticipant()
    {
        // Three participants: A conquers B's only country (eliminating B), ends the turn, C plays, and A comes back.
        var state = ApplyOk(AfterConquestOnThreeSeats(), new MoveAfterConquest(0, 2), TinyBoard.Map).State;
        Assert.Equal(1, state.Turn.ConquestsThisTurn);

        state = ApplyOk(state, new EndTurn(0), TinyBoard.Map).State;
        Assert.Equal((2, 1, GamePhase.Attack), (state.ActiveSeat, state.Round, state.Phase));
        Assert.Equal(0, state.Turn.ConquestsThisTurn); // C starts with nothing

        state = ApplyOk(state, new EndTurn(2), TinyBoard.Map).State;
        Assert.Equal((0, 2, GamePhase.Reinforcement), (state.ActiveSeat, state.Round, state.Phase));
        Assert.Equal(0, state.Turn.ConquestsThisTurn); // so does A, in its next turn

        state = PlaceWholePool(state, TinyBoard.Map);
        Assert.Equal(GamePhase.Attack, state.Phase);
        Assert.Equal(0, state.Turn.ConquestsThisTurn);

        state = Conquer(Armies(state, ("b", 3)), "b", "d"); // one conquest in A's second turn: counted from zero
        Assert.Equal(1, state.Turn.ConquestsThisTurn);
    }

    [Fact]
    public void TheTurnContext_KeepsItsConquests_WhenReinforcementsAreSpent()
    {
        // Not reachable in play (nobody conquers during Refuerzo): the pool code must still not rebuild the context
        // from scratch, or every field added to it later would be lost on the first placement.
        var state = ReinforcementOfSeatOne(SpreadCountries(10)); // 5 armies to place
        state = state with { Turn = state.Turn with { ConquestsThisTurn = 2 } };

        var partly = ApplyOk(state, Reinforcing(1, (OwnedBy(state, 1)[0], 2))).State;
        var fully = ApplyOk(partly, Reinforcing(1, (OwnedBy(state, 1)[0], 3))).State;

        Assert.Equal((3, 2), (partly.Turn.PendingArmies, partly.Turn.ConquestsThisTurn));
        Assert.Equal((0, 2), (fully.Turn.PendingArmies, fully.Turn.ConquestsThisTurn));
    }

    private static GameState AfterConquestOnThreeSeats() => Conquer(ThreeSeats(8, 1), "a", "c");
}
