using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Events;
using Teg.Domain.Rules;
using Teg.Domain.State;
using Teg.Domain.Tests.Support;
using static Teg.Domain.Tests.Support.CardFixtures;
using static Teg.Domain.Tests.Support.CombatFixtures;
using static Teg.Domain.Tests.Support.GameFixtures;
using static Teg.Domain.Tests.Support.ReinforcementFixtures;

namespace Teg.Domain.Tests.Combat;

/// <summary>
/// E5 / R§2, R§6, R§7: conquering the LAST country of a participant eliminates them. The attacker inherits their
/// cards (the defender's hand is appended to theirs and emptied); an inherited hand of five or more is NOT forced
/// mid-turn, it is settled at the attacker's next Refuerzo by the TRADE_REQUIRED rule. The eliminated participant is
/// skipped by the rotation for ever. Elimination happens when the country is conquered (the conquering army is
/// already in it), not when the attacker later decides the occupation. Victory is NOT decided here: phase 8 owns it.
/// Cards are ids of the <see cref="TinyBoard"/>: a, d are galeones, b, e globos, c, f cañones.
/// </summary>
public sealed class EliminationTests
{
    private static readonly string[] AttackersCards = ["a", "b", "c"];
    private static readonly string[] DefendersCards = ["d", "e", "f"];

    private static GameState WithHands(GameState state, string[] attacker, string[] defender) =>
        WithHand(WithHand(state, 0, attacker), 1, defender);

    private static bool OwnsNothing(GameState state, int seat) => state.Territories.All(t => t.Owner != seat);

    /// <summary>The next turn of seat 0 (round 2, Refuerzo) after it eliminated the middle participant of three.</summary>
    private static GameState NextReinforcementOfTheEliminator(string[] attackerCards, string[] defenderCards)
    {
        var state = Conquer(WithHands(ThreeSeats(8, 1), attackerCards, defenderCards), "a", "c");
        state = ApplyOk(state, new MoveAfterConquest(0, 2), TinyBoard.Map).State;
        state = ApplyOk(state, new EndTurn(0), TinyBoard.Map).State; // seat 2 (seat 1 is skipped)
        state = ApplyOk(state, new EndTurn(2), TinyBoard.Map).State; // round 2 starts

        Assert.Equal((0, 2, GamePhase.Reinforcement), (state.ActiveSeat, state.Round, state.Phase));
        return state;
    }

    /// <summary>Ends the turn of the active participant, placing the whole pool first when the turn opens with Refuerzo.</summary>
    private static GameState FinishTurn(GameState state)
    {
        if (state.Phase == GamePhase.Reinforcement)
        {
            state = PlaceWholePool(state, TinyBoard.Map);
        }

        return ApplyOk(state, new EndTurn(state.ActiveSeat), TinyBoard.Map).State;
    }

    // ---- who is eliminated ----------------------------------------------------------------------------

    [Fact]
    public void R2_ConqueringTheLastCountryOfAParticipant_EliminatesThem_AtTheMomentOfTheConquest()
    {
        var before = LastStand(6, 1);
        Assert.False(before.Participants[1].Eliminated);

        var transition = ConquerWithEvents(before, "a", "c");
        var next = transition.State;

        Assert.True(next.Participants[1].Eliminated);
        Assert.False(next.Participants[0].Eliminated);
        Assert.True(OwnsNothing(next, 1)); // the flag and the board agree
        Assert.NotNull(next.Turn.PendingConquest); // the attacker has not even decided the occupation yet
        Assert.Equal(
            [
                typeof(AttackResolved),
                typeof(TerritoryConquered),
                typeof(ParticipantEliminated),
            ],
            transition.Events.Select(e => e.GetType()).ToArray());
        Assert.Equal(new ParticipantEliminated(Eliminated: 1, By: 0, ValueArray<string>.Empty), transition.Events[2]);
    }

    [Fact]
    public void R2_ConqueringACountryThatIsNotTheLastOne_EliminatesNobody_AndTheDefenderKeepsItsCards()
    {
        var before = WithHands(Arena(6, 1), AttackersCards, DefendersCards); // seat 1 also owns d

        var transition = ConquerWithEvents(before, "a", "c");
        var next = transition.State;

        Assert.False(next.Participants[1].Eliminated);
        Assert.Equal(DefendersCards, HandIds(next, 1));
        Assert.Equal(AttackersCards, HandIds(next, 0));
        Assert.DoesNotContain(transition.Events, e => e is ParticipantEliminated);
        Assert.False(OwnsNothing(next, 1));
    }

    [Fact]
    public void R2_AnAttackThatDoesNotEmptyTheLastCountry_EliminatesNobody()
    {
        var state = WithDice(LastStand(8, 2), "a", "c", (attacker, defender) => Oracle(attacker, defender) == (1, 1));

        var next = ApplyOk(state, new Attack(0, "a", "c"), TinyBoard.Map).State;

        Assert.False(next.Participants[1].Eliminated);
        Assert.Equal(1, next.FindTerritory("c")!.Owner);
    }

    // ---- the cards ------------------------------------------------------------------------------------------

    [Fact]
    public void R6_TheAttackerInheritsTheCardsOfTheEliminatedParticipant_AfterItsOwn_AndTheirHandIsEmptied()
    {
        var before = WithHands(LastStand(6, 1), AttackersCards, DefendersCards);

        var transition = ConquerWithEvents(before, "a", "c");
        var next = transition.State;

        Assert.Equal(["a", "b", "c", "d", "e", "f"], HandIds(next, 0));
        Assert.Empty(next.Participants[1].Cards);
        Assert.Equal(
            new ParticipantEliminated(1, 0, DefendersCards.ToValueArray()),
            transition.Events.OfType<ParticipantEliminated>().Single());
    }

    [Theory]
    [InlineData(new string[0], new[] { "e", "f" }, new[] { "e", "f" })] // the attacker held nothing
    [InlineData(new[] { "a" }, new string[0], new[] { "a" })] // the defender held nothing
    [InlineData(new[] { "c", "a" }, new[] { "f", "d" }, new[] { "c", "a", "f", "d" })] // hand orders are kept
    [InlineData(new string[0], new string[0], new string[0])]
    public void R6_TheInheritedHand_IsTheAttackersFollowedByTheDefenders_WhateverTheirSizes(
        string[] attackerCards,
        string[] defenderCards,
        string[] expectedHand)
    {
        var before = WithHands(LastStand(6, 1), attackerCards, defenderCards);

        var next = Conquer(before, "a", "c");

        Assert.Equal(expectedHand, HandIds(next, 0));
        Assert.Empty(next.Participants[1].Cards);
    }

    [Fact]
    public void R6_TheTradeCountersOfBothParticipants_DoNotChangeWithAnElimination()
    {
        var before = WithTrades(WithTrades(WithHands(LastStand(6, 1), AttackersCards, DefendersCards), 0, 1), 1, 3);

        var next = Conquer(before, "a", "c");

        Assert.Equal(1, next.Participants[0].TradesDone); // the heir's next trade is still worth 7
        Assert.Equal(3, next.Participants[1].TradesDone);
    }

    [Fact]
    public void R7_AnInheritedHandOfFiveOrMore_IsNotForcedMidTurn_TheAttackerKeepsPlayingTheTurn()
    {
        var state = Conquer(WithHands(ThreeSeats(8, 1), AttackersCards, DefendersCards), "a", "c");
        state = ApplyOk(state, new MoveAfterConquest(0, 2), TinyBoard.Map).State;
        Assert.Equal(6, state.Participants[0].Cards.Count); // 3 own + 3 inherited

        var attacked = ApplyOk(state, new Attack(0, "b", "d"), TinyBoard.Map); // still free to attack...
        Assert.True(GameEngine.Apply(attacked.State, new StartRegroup(0), TinyBoard.Map).IsSuccess); // ...to regroup...
        Assert.True(GameEngine.Apply(attacked.State, new EndTurn(0), TinyBoard.Map).IsSuccess); // ...and to end the turn

        Assert.DoesNotContain(attacked.Events, e => e is CardsTraded);
        Assert.Equal(6, attacked.State.Participants[0].Cards.Count);
    }

    [Fact]
    public void R7_TheInheritedHandIsSettledAtTheNextReinforcement_TradeRequiredUntilFewerThanFiveRemain()
    {
        AssertRejected(
            RuleViolations.TradeRequired,
            () => NextReinforcementOfTheEliminator(AttackersCards, DefendersCards),
            _ => Reinforcing(0, ("a", 1)),
            TinyBoard.Map);

        var state = NextReinforcementOfTheEliminator(AttackersCards, DefendersCards);
        var traded = ApplyOk(state, Trading(0, "a", "b", "c"), TinyBoard.Map).State; // galeon + globo + cañon

        Assert.Equal(["d", "e", "f"], HandIds(traded, 0));
        Assert.Equal(3 + 4, traded.Turn.FreeArmies); // 5 countries = 3 armies, plus the first trade of the heir
        Assert.Equal(GamePhase.Attack, PlaceWholePool(traded, TinyBoard.Map).Phase); // and now the pool can be placed
    }

    // ---- the rotation ----------------------------------------------------------------------------------------

    [Fact]
    public void R2_EliminatingTheMiddleParticipantOfThree_TheRotationCyclesAThenCThenAAgain()
    {
        var state = ApplyOk(Conquer(ThreeSeats(8, 1), "a", "c"), new MoveAfterConquest(0, 2), TinyBoard.Map).State;
        Assert.True(state.Participants[1].Eliminated);

        var ended = ApplyOk(state, new EndTurn(0), TinyBoard.Map);

        GameEvent[] expected =
        [
            new PhaseChanged(0, GamePhase.Attack, GamePhase.Card),
            new TurnEnded(0),
            new TurnStarted(2, Round: 1, GamePhase.Attack), // seat 1 (B) is skipped
        ];
        Assert.Equal(expected, ended.Events.ToArray());

        var visited = new List<(int Seat, int Round)> { (state.ActiveSeat, state.Round) };
        state = ended.State;
        for (var turn = 0; turn < 4; turn++)
        {
            visited.Add((state.ActiveSeat, state.Round));
            state = FinishTurn(state);
        }

        visited.Add((state.ActiveSeat, state.Round));
        Assert.Equal([(0, 1), (2, 1), (0, 2), (2, 2), (0, 3), (2, 3)], visited);
        Assert.DoesNotContain(visited, turn => turn.Seat == 1);
    }

    [Fact]
    public void R2_AnEliminatedParticipant_CanNeverActAgain()
    {
        AssertRejected(
            RuleViolations.NotActiveParticipant,
            () =>
            {
                var state = ApplyOk(Conquer(ThreeSeats(8, 1), "a", "c"), new MoveAfterConquest(0, 2), TinyBoard.Map).State;
                return ApplyOk(state, new EndTurn(0), TinyBoard.Map).State; // seat 2 is the active one now
            },
            _ => new EndTurn(1),
            TinyBoard.Map);
    }

    // ---- victory is not decided here (phase 8) ---------------------------------------------------------------------

    /// <summary>
    /// PHASE-8 STAND-IN, pinned on purpose: eliminating the last opponent does not finish the game yet because victory
    /// (R§8: Winner, WinReason, Finished) is owned by phase 8 (8.5/8.6). When victory exists, rewrite this test: the
    /// conquest must end in <see cref="GamePhase.Finished"/> with the attacker as the winner.
    /// </summary>
    [Fact]
    public void Phase8StandIn_EliminatingTheLastOpponent_DoesNotDecideVictoryYet()
    {
        var next = Conquer(LastStand(6, 1), "a", "c");

        Assert.True(next.Participants[1].Eliminated);
        Assert.Equal(GamePhase.Attack, next.Phase); // not Finished: nobody wins until phase 8
    }
}
