using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Events;
using Teg.Domain.Map;
using Teg.Domain.Rng;
using Teg.Domain.Rules;
using Teg.Domain.State;
using Teg.Domain.Tests.Support;
using static Teg.Domain.Tests.Support.CardFixtures;
using static Teg.Domain.Tests.Support.CombatFixtures;
using static Teg.Domain.Tests.Support.GameFixtures;
using static Teg.Domain.Tests.Support.ReinforcementFixtures;

namespace Teg.Domain.Tests.Cards;

/// <summary>
/// E8 / R§7: the card phase. It has no player decision: <see cref="EndTurn"/> resolves it between
/// <see cref="PhaseChanged"/> (to Tarjeta) and <see cref="TurnEnded"/>, in this order.
/// <list type="number">
/// <item>AWARD: one card, the top of the draw pile, when the turn had at least one conquest and the participant has made
/// fewer than 3 trades, or at least two conquests when they have made 3 or more. Never more than one per turn. An empty
/// draw pile is refilled by shuffling the discard pile with the seeded RNG; with both piles empty nothing is awarded.</item>
/// <item>PREMIUM: for every card in the hand whose country the participant owns and whose premium was not claimed yet,
/// 2 armies go to that country, once per card (the card just drawn included, and cards whose country was conquered in an
/// earlier turn). The card stays in the hand.</item>
/// </list>
/// Two participants on the real board: seat 0 owns the 1st, 3rd, 5th... country in map order and seat 1 the rest
/// (every country has 1 army); the deck of a hand-built state is whatever the test sets.
/// </summary>
public sealed class CardPhaseTests
{
    private const int Bonus = 2;

    private static readonly GameState Board = InPlay(2, GamePhase.Attack, round: 2, activeSeat: 0);

    /// <summary>Countries of <paramref name="seat"/> on <see cref="Board"/>, in map order (a card is its country's id).</summary>
    private static string[] Countries(int seat, int count, int skip = 0) => OwnedBy(Board, seat).Skip(skip).Take(count).ToArray();

    private static string[] Own(int count, int skip = 0) => Countries(0, count, skip);

    private static string[] Foreign(int count, int skip = 0) => Countries(1, count, skip);

    /// <summary>The pool seat 1 gets when its Refuerzo opens: 25 countries, no continent in full.</summary>
    private static readonly ReinforcementPoolComputed SeatOnePool = new(1, 12, ValueArray<ContinentPool>.Empty);

    private static readonly ReinforcementPoolComputed SeatZeroPool = new(0, 12, ValueArray<ContinentPool>.Empty);

    /// <summary>
    /// <paramref name="seat"/> is about to close its turn (round <paramref name="round"/>, from <paramref name="phase"/>)
    /// having made <paramref name="conquests"/> conquests, having traded <paramref name="tradesDone"/> times, with this deck.
    /// </summary>
    private static GameState Closing(
        int conquests = 0,
        int tradesDone = 0,
        string[]? draw = null,
        string[]? discard = null,
        GamePhase phase = GamePhase.Attack,
        int round = 2,
        int seat = 0)
    {
        var state = InPlay(2, phase, round, activeSeat: seat);

        return WithConquests(WithTrades(WithDeck(state, draw ?? [], discard ?? []), seat, tradesDone), conquests);
    }

    private static Transition Close(GameState state) => ApplyOk(state, new EndTurn(state.ActiveSeat));

    private static GameEvent[] CardPhaseEvents(Transition transition) =>
        transition.Events.Where(e => e is CardAwarded or BonusArmiesPlaced).ToArray();

    // ---- AWARD: when a card is due ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 0, false)] // no conquest: no card, whatever the trades
    [InlineData(0, 2, false)]
    [InlineData(0, 3, false)]
    [InlineData(0, 7, false)]
    [InlineData(1, 0, true)] // one conquest is enough with fewer than 3 trades...
    [InlineData(1, 1, true)]
    [InlineData(1, 2, true)]
    [InlineData(1, 3, false)] // ...and not enough from the third trade on
    [InlineData(1, 4, false)]
    [InlineData(1, 9, false)]
    [InlineData(2, 0, true)]
    [InlineData(2, 2, true)]
    [InlineData(2, 3, true)] // two conquests are enough after any number of trades
    [InlineData(2, 4, true)]
    [InlineData(3, 3, true)]
    [InlineData(5, 0, true)]
    [InlineData(9, 12, true)]
    public void R7_TheCardIsAwarded_OnlyWhenTheConquestsReachTheBarSetByTheTradesDone(int conquests, int trades, bool awarded)
    {
        var pile = Foreign(3);

        var closed = Close(Closing(conquests, trades, draw: pile));

        if (awarded)
        {
            Assert.Equal(new[] { new HeldCard(pile[0]) }, HeldBy(closed.State, 0));
            Assert.Equal(pile.Skip(1).ToArray(), closed.State.Deck.Draw.ToArray());
            Assert.Contains<GameEvent>(new CardAwarded(0, pile[0]), closed.Events);
        }
        else
        {
            Assert.Empty(HeldBy(closed.State, 0));
            Assert.Equal(pile, closed.State.Deck.Draw.ToArray()); // the pile was there, untouched
            Assert.DoesNotContain(closed.Events, e => e is CardAwarded);
        }
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(5, 1)]
    [InlineData(12, 2)]
    [InlineData(3, 3)]
    [InlineData(9, 8)]
    public void R7_HoweverManyCountriesWereConquered_OnlyOneCardIsAwardedPerTurn(int conquests, int trades)
    {
        var closed = Close(Closing(conquests, trades, draw: Foreign(4)));

        Assert.Single(HeldBy(closed.State, 0));
        Assert.Equal(3, closed.State.Deck.Draw.Count);
        Assert.Single(closed.Events.OfType<CardAwarded>());
    }

    [Fact]
    public void R7_TheBarDependsOnTheTradesOfTheParticipantWhoPlays_NotOnThoseOfTheOthers()
    {
        var pile = Foreign(2);

        // Seat 0 has made no trade, seat 1 has made five: seat 0 still needs only one conquest.
        var fewTrades = Close(WithTrades(Closing(1, 0, draw: pile), 1, 5));
        Assert.Single(HeldBy(fewTrades.State, 0));

        // Seat 0 has made three trades, seat 1 none: one conquest is not enough for seat 0.
        var manyTrades = Close(WithTrades(Closing(1, 3, draw: pile), 1, 0));
        Assert.Empty(HeldBy(manyTrades.State, 0));
    }

    [Fact]
    public void R7_EachAwardTakesTheNextCardOfThePile_InOrder_AndOnlyTheActiveParticipantReceivesIt()
    {
        // The first card is a country of seat 1 (no premium for seat 0), the second one of seat 0 (none for seat 1).
        var pile = new[] { Foreign(1)[0], Own(1)[0], Own(1, skip: 1)[0] };

        var first = Close(Closing(1, draw: pile));
        var second = Close(WithConquests(first.State with { Phase = GamePhase.Attack }, 1));

        Assert.Equal([pile[0]], HandIds(first.State, 0));
        Assert.Empty(HandIds(first.State, 1));
        Assert.Equal([pile[0]], HandIds(second.State, 0)); // unchanged: it was seat 1's turn
        Assert.Equal([pile[1]], HandIds(second.State, 1));
        Assert.Equal([pile[2]], second.State.Deck.Draw.ToArray());
    }

    // ---- AWARD: the deck runs out -----------------------------------------------------------------------------------

    [Theory]
    [InlineData(1L, 0L)]
    [InlineData(99L, 7L)]
    [InlineData(-5L, 40L)]
    public void R7_WhenTheDrawPileIsEmpty_TheDiscardIsShuffledIntoANewPile_WithTheSeededRng(long seed, long draws)
    {
        var discard = Foreign(8);
        var state = Closing(conquests: 1, draw: [], discard: discard) with { Rng = new RngState(seed, draws) };
        var replay = new SeededRng(seed, draws);
        var expectedPile = discard.ToList();
        replay.Shuffle(expectedPile); // what the generator gives from where the state left it

        var closed = Close(state);

        Assert.Equal(new[] { new HeldCard(expectedPile[0]) }, HeldBy(closed.State, 0));
        Assert.Equal(expectedPile.Skip(1).ToArray(), closed.State.Deck.Draw.ToArray());
        Assert.Empty(closed.State.Deck.Discard);
        Assert.Equal(replay.State, closed.State.Rng);
        Assert.True(closed.State.Rng.Draws > draws, "The reshuffle must consume draws of the RNG.");
        Assert.Contains<GameEvent>(new CardAwarded(0, expectedPile[0]), closed.Events);
    }

    [Fact]
    public void R7_TheReshuffleReallyShuffles_TheAwardedCardChangesWithTheSeed()
    {
        var discard = Foreign(8);

        var awarded = Enumerable.Range(1, 30)
            .Select(seed => Closing(conquests: 1, draw: [], discard: discard) with { Rng = new RngState(seed, 0) })
            .Select(state => HandIds(Close(state).State, 0).Single())
            .ToArray();

        Assert.True(awarded.Distinct().Count() >= 5, $"Only {awarded.Distinct().Count()} different cards over 30 seeds.");
        Assert.All(awarded, card => Assert.Contains(card, discard));
    }

    [Fact]
    public void R7_WhenBothPilesAreEmpty_NothingIsAwarded_NoRandomnessIsUsed_AndTheTurnStillEnds()
    {
        var state = Closing(conquests: 3, tradesDone: 0); // a card is due, but there is none to give

        var closed = Close(state);

        Assert.Empty(HeldBy(closed.State, 0));
        Assert.Equal(state.Deck, closed.State.Deck);
        Assert.Equal(state.Rng, closed.State.Rng);
        Assert.DoesNotContain(closed.Events, e => e is CardAwarded);
        Assert.Contains<GameEvent>(new TurnEnded(0), closed.Events);
        Assert.Equal(1, closed.State.ActiveSeat);
    }

    [Fact]
    public void R7_WhileThereAreCardsToDraw_TheDiscardPileAndTheRngAreNotTouched()
    {
        var state = Closing(conquests: 1, draw: Foreign(2), discard: Foreign(3, skip: 2)) with { Rng = new RngState(5, 11) };

        var closed = Close(state);

        Assert.Single(HeldBy(closed.State, 0));
        Assert.Equal(state.Deck.Discard, closed.State.Deck.Discard);
        Assert.Equal(state.Rng, closed.State.Rng);
    }

    [Fact]
    public void R7_TheDiscardIsNotShuffled_WhenNoCardIsDue_EvenWithAnEmptyDrawPile()
    {
        var state = Closing(conquests: 0, draw: [], discard: Foreign(5)) with { Rng = new RngState(5, 11) };

        var closed = Close(state);

        Assert.Equal(state.Deck, closed.State.Deck);
        Assert.Equal(state.Rng, closed.State.Rng);
    }

    [Fact]
    public void R7_TakingTheLastCardOfThePile_DoesNotShuffleYet_TheNextAwardDoes()
    {
        var last = Foreign(1)[0];
        var discard = Own(4); // cards of seat 0's countries: no premium for seat 1, who draws them
        var state = Closing(conquests: 1, draw: [last], discard: discard) with { Rng = new RngState(3, 0) };

        var first = Close(state);

        Assert.Equal([last], HandIds(first.State, 0));
        Assert.Empty(first.State.Deck.Draw);
        Assert.Equal(discard, first.State.Deck.Discard.ToArray()); // not shuffled yet
        Assert.Equal(state.Rng, first.State.Rng);

        var second = Close(WithConquests(first.State with { Phase = GamePhase.Attack }, 1));

        var drawn = Assert.Single(HandIds(second.State, 1));
        Assert.Contains(drawn, discard);
        Assert.Equal(3, second.State.Deck.Draw.Count); // the new pile, one card less
        Assert.Empty(second.State.Deck.Discard);
        Assert.True(second.State.Rng.Draws > 0);
    }

    // ---- PREMIUM: 2 armies, once per card ----------------------------------------------------------------------------

    [Fact]
    public void R7_ACardDrawnForACountryTheParticipantAlreadyOwns_PaysTwoArmiesAtOnce_AndStaysInTheHand()
    {
        var mine = Own(1)[0];
        var state = Closing(conquests: 1, draw: [mine]);

        var closed = Close(state);

        Assert.Equal(new[] { new HeldCard(mine, BonusClaimed: true) }, HeldBy(closed.State, 0));
        Assert.Equal(ArmiesIn(state, mine) + Bonus, ArmiesIn(closed.State, mine));
        GameEvent[] expected =
        [
            new PhaseChanged(0, GamePhase.Attack, GamePhase.Card),
            new CardAwarded(0, mine),
            new BonusArmiesPlaced(0, mine, Bonus),
            new TurnEnded(0),
            new TurnStarted(1, 2, GamePhase.Reinforcement),
            SeatOnePool,
        ];
        Assert.Equal(expected, closed.Events.ToArray());
    }

    [Fact]
    public void R7_ACardOfACountryOwnedByAnotherParticipant_PaysNothing_AndItsPremiumStaysUnclaimed()
    {
        var theirs = Foreign(1)[0];
        var state = Closing(conquests: 1, draw: [theirs]);

        var closed = Close(state);

        Assert.Equal(new[] { new HeldCard(theirs, BonusClaimed: false) }, HeldBy(closed.State, 0));
        Assert.Equal(state.Territories, closed.State.Territories);
        Assert.DoesNotContain(closed.Events, e => e is BonusArmiesPlaced);
    }

    [Theory]
    [InlineData(0)] // no conquest this turn: the premium does not depend on one
    [InlineData(1)] // a conquest but an empty deck: nothing to draw, the held card still pays
    public void R7_ACardHeldSinceAnEarlierTurn_PaysItsPremiumAtTheCloseOfAnyLaterOwnTurn(int conquests)
    {
        var mine = Own(1, skip: 4)[0];
        var state = WithHeld(Closing(conquests), 0, new HeldCard(mine));

        var closed = Close(state);

        Assert.Equal(new[] { new HeldCard(mine, BonusClaimed: true) }, HeldBy(closed.State, 0));
        Assert.Equal(ArmiesIn(state, mine) + Bonus, ArmiesIn(closed.State, mine));
        Assert.Equal(new GameEvent[] { new BonusArmiesPlaced(0, mine, Bonus) }, CardPhaseEvents(closed));
    }

    [Fact]
    public void R7_AClaimedPremium_IsNeverPaidAgain_WhileTheCardIsHeld()
    {
        var mine = Own(1, skip: 4)[0];
        var state = WithHeld(Closing(conquests: 0), 0, new HeldCard(mine, BonusClaimed: true));

        var closed = Close(state);

        Assert.Equal(state.Territories, closed.State.Territories);
        Assert.Equal(HeldBy(state, 0), HeldBy(closed.State, 0));
        Assert.Empty(CardPhaseEvents(closed));
    }

    [Fact]
    public void R7_OverTwoOwnTurns_TheSameCardPaysItsPremiumOnce()
    {
        var mine = Own(1, skip: 4)[0]; // far from the country where the reinforcements go (the first one)
        var state = WithHeld(Closing(), 0, new HeldCard(mine));

        var firstClose = Close(state); // seat 0 closes: the premium is paid
        var seatOneTurn = Close(PlaceWholePool(firstClose.State)); // seat 1 plays; the round turns, seat 0 opens Refuerzo
        var secondClose = Close(PlaceWholePool(seatOneTurn.State)); // seat 0 closes again

        Assert.Equal(ArmiesIn(state, mine) + Bonus, ArmiesIn(firstClose.State, mine));
        Assert.Equal((0, 3, GamePhase.Reinforcement), (seatOneTurn.State.ActiveSeat, seatOneTurn.State.Round, seatOneTurn.State.Phase));
        Assert.Equal(ArmiesIn(state, mine) + Bonus, ArmiesIn(secondClose.State, mine)); // still +2, not +4
        Assert.Empty(CardPhaseEvents(secondClose));
        Assert.True(HeldBy(secondClose.State, 0).Single().BonusClaimed);
    }

    [Fact]
    public void R7_EveryUnclaimedCardOfAnOwnedCountryPays_InMapOrder_AndTheOthersDoNot()
    {
        var owned = Own(6);
        var foreign = Foreign(1)[0];
        // Hand order is NOT map order, and one card is of a country of seat 1.
        var hand = new[] { owned[5], foreign, owned[1], owned[3] }.Select(id => new HeldCard(id)).ToArray();
        var state = WithHeld(Closing(), 0, hand);

        var closed = Close(state);

        Assert.Equal(
            new GameEvent[]
            {
                new BonusArmiesPlaced(0, owned[1], Bonus),
                new BonusArmiesPlaced(0, owned[3], Bonus),
                new BonusArmiesPlaced(0, owned[5], Bonus),
            },
            CardPhaseEvents(closed));
        Assert.Equal(
            new[]
            {
                new HeldCard(owned[5], true),
                new HeldCard(foreign, false),
                new HeldCard(owned[1], true),
                new HeldCard(owned[3], true),
            },
            HeldBy(closed.State, 0)); // same order in the hand: nothing leaves it
        Assert.Equal(ArmiesOf(state, 0) + 3 * Bonus, ArmiesOf(closed.State, 0));
        Assert.Equal(ArmiesOf(state, 1), ArmiesOf(closed.State, 1));
        Assert.All(state.Territories, t => Assert.Equal(t.Owner, closed.State.FindTerritory(t.Id)!.Owner));
    }

    [Fact]
    public void R7_OnlyTheClosingParticipantsCardsPay_TheOthersWaitForTheirOwnTurn()
    {
        var seatOneCountry = Foreign(1)[0];
        var state = WithHeld(Closing(), 1, new HeldCard(seatOneCountry)); // seat 1 holds its OWN country's card

        var closed = Close(state); // seat 0 closes

        Assert.Equal(HeldBy(state, 1), HeldBy(closed.State, 1));
        Assert.Equal(ArmiesIn(state, seatOneCountry), ArmiesIn(closed.State, seatOneCountry));
        Assert.Empty(CardPhaseEvents(closed));
    }

    [Fact]
    public void R7_TheCountryMustBeOwnedWhenTheTurnCloses_ItIsNotEnoughToHaveOwnedItBefore()
    {
        var mine = Own(1, skip: 3)[0];
        var former = WithHeld(Closing(), 0, new HeldCard(mine));
        var lost = former with
        {
            Territories = former.Territories.Select(t => t.Id == mine ? t with { Owner = 1 } : t).ToValueArray(),
        };

        var closed = Close(lost);

        Assert.Equal(new[] { new HeldCard(mine, BonusClaimed: false) }, HeldBy(closed.State, 0));
        Assert.Equal(lost.Territories, closed.State.Territories);
    }

    // ---- when the card phase runs ---------------------------------------------------------------------------------------

    [Fact]
    public void R7_TheHostilitiesRound_AlsoGoesThroughTheCardPhase()
    {
        var mine = Own(1)[0];
        var state = Closing(conquests: 1, draw: [mine], round: 1);

        var closed = Close(state);

        GameEvent[] expected =
        [
            new PhaseChanged(0, GamePhase.Attack, GamePhase.Card),
            new CardAwarded(0, mine),
            new BonusArmiesPlaced(0, mine, Bonus),
            new TurnEnded(0),
            new TurnStarted(1, 1, GamePhase.Attack), // round 1: no Refuerzo, so no pool either
        ];
        Assert.Equal(expected, closed.Events.ToArray());
    }

    [Fact]
    public void R7_ATurnClosedFromRegroup_GoesThroughTheCardPhaseToo()
    {
        var mine = Own(1)[0];
        var state = Closing(conquests: 1, draw: [mine], phase: GamePhase.Regroup);

        var closed = Close(state);

        GameEvent[] expected =
        [
            new PhaseChanged(0, GamePhase.Regroup, GamePhase.Card),
            new CardAwarded(0, mine),
            new BonusArmiesPlaced(0, mine, Bonus),
            new TurnEnded(0),
            new TurnStarted(1, 2, GamePhase.Reinforcement),
            SeatOnePool,
        ];
        Assert.Equal(expected, closed.Events.ToArray());
    }

    [Fact]
    public void R7_TheCardPhaseEvents_SitBetweenPhaseChangedAndTurnEnded_InAFixedOrder_AndTheVersionGrowsByOne()
    {
        var own = Countries(seat: 1, count: 4);
        // Seat 1 closes the last turn of round 2: it draws own[2] and holds own[3] and own[1] (in that hand order).
        var state = WithHeld(Closing(conquests: 1, draw: [own[2]], seat: 1), 1, new HeldCard(own[3]), new HeldCard(own[1]));

        var closed = Close(state);

        GameEvent[] expected =
        [
            new PhaseChanged(1, GamePhase.Attack, GamePhase.Card),
            new CardAwarded(1, own[2]),
            new BonusArmiesPlaced(1, own[1], Bonus), // map order, not hand order
            new BonusArmiesPlaced(1, own[2], Bonus),
            new BonusArmiesPlaced(1, own[3], Bonus),
            new TurnEnded(1),
            new RoundAdvanced(3),
            new TurnStarted(0, 3, GamePhase.Reinforcement),
            SeatZeroPool,
        ];
        Assert.Equal(expected, closed.Events.ToArray());
        Assert.Equal(state.Version + 1, closed.State.Version);
        Assert.Equal((0, 3, GamePhase.Reinforcement), (closed.State.ActiveSeat, closed.State.Round, closed.State.Phase));
    }

    [Fact]
    public void R7_ACommandRefusedBeforeTheTurnEnds_NeitherDrawsNorPays()
    {
        var mine = Own(1)[0];

        // From Refuerzo with armies pending (R§5)...
        AssertRejected(
            RuleViolations.ArmiesPending,
            () => WithHeld(Closing(conquests: 1, draw: [mine], phase: GamePhase.Reinforcement), 0, new HeldCard(Own(1, skip: 1)[0])) with
            {
                Turn = Pool(5) with { ConquestsThisTurn = 1 },
            },
            _ => new EndTurn(0));

        // ...and with a conquest waiting for its occupation (R§6).
        AssertRejected(
            RuleViolations.ConquestPending,
            () => WithHeld(Closing(conquests: 1, draw: [mine]), 0, new HeldCard(Own(1, skip: 1)[0])) with
            {
                Turn = Closing(conquests: 1).Turn with { PendingConquest = new PendingConquest(mine, Foreign(1)[0]) },
            },
            _ => new EndTurn(0));
    }

    // ---- through the real commands ---------------------------------------------------------------------------------------

    [Fact]
    public void R7_AConquestMadeWithRealAttacks_CountsForTheCard_AndTheTurnWithoutOneDoesNot()
    {
        var withDeck = WithDeck(Arena(6, 1), ["d"]); // "d" belongs to seat 1: no premium for the attacker

        var conquered = Conquer(withDeck, "a", "c");
        var occupied = ApplyOk(conquered, new MoveAfterConquest(0, 2), TinyBoard.Map).State;
        var closed = ApplyOk(occupied, new EndTurn(0), TinyBoard.Map);

        Assert.Equal(["d"], HandIds(closed.State, 0));
        Assert.Contains<GameEvent>(new CardAwarded(0, "d"), closed.Events);
        Assert.Empty(closed.State.Deck.Draw);

        var peaceful = ApplyOk(withDeck, new EndTurn(0), TinyBoard.Map);

        Assert.Empty(HandIds(peaceful.State, 0));
        Assert.Equal(["d"], peaceful.State.Deck.Draw.ToArray());
    }

    [Fact]
    public void R7_ACardOfAnEnemyCountry_PaysNothing_UntilTheParticipantConquersItAndClosesATurn()
    {
        // Seat 0 holds the card of "c", which seat 1 owns; "d" waits in the deck.
        var start = WithDeck(WithHeld(Arena(6, 1), 0, new HeldCard("c")), ["d"]);

        // Round 1: seat 0 and seat 1 close without conquests. "c" is not seat 0's: nothing is paid.
        var seatZeroClosed = ApplyOk(start, new EndTurn(0), TinyBoard.Map);
        Assert.Equal(ArmiesIn(start, "c"), ArmiesIn(seatZeroClosed.State, "c"));
        Assert.Empty(CardPhaseEvents(seatZeroClosed));
        var seatOneClosed = ApplyOk(seatZeroClosed.State, new EndTurn(1), TinyBoard.Map);

        // Round 2: seat 0 places its reinforcements, conquers "c", occupies it with 2 armies and closes.
        var attackReady = PlaceWholePool(seatOneClosed.State, TinyBoard.Map);
        var occupied = ApplyOk(Conquer(attackReady, "a", "c"), new MoveAfterConquest(0, 2), TinyBoard.Map).State;
        var closed = ApplyOk(occupied, new EndTurn(0), TinyBoard.Map);

        Assert.Equal(2, ArmiesIn(occupied, "c"));
        Assert.Equal(2 + Bonus, ArmiesIn(closed.State, "c"));
        Assert.Equal(new[] { new HeldCard("c", true), new HeldCard("d", false) }, HeldBy(closed.State, 0));
        Assert.Equal(
            new GameEvent[] { new CardAwarded(0, "d"), new BonusArmiesPlaced(0, "c", Bonus) },
            CardPhaseEvents(closed));
    }

    // ---- traded cards ----------------------------------------------------------------------------------------------------

    [Fact]
    public void R7_ATradedCardGoesToTheDiscardPile_AndWhenDrawnAgainItsPremiumIsPaidAgain()
    {
        // Seat 0 trades three galeones it owns, with the premium already claimed on each.
        var galeons = OwnedBy(Board, 0).Where(id => RealMap.Territory(id).Symbol == CardSymbol.Galeon).Take(3).ToArray();
        Assert.Equal(3, galeons.Length);
        var reinforcing = WithHeld(
            InPlay(2, GamePhase.Reinforcement, round: 2, activeSeat: 0) with { Turn = Pool(8) },
            0,
            galeons.Select(id => new HeldCard(id, BonusClaimed: true)).ToArray());

        var traded = ApplyOk(reinforcing, Trading(0, galeons)).State;

        Assert.Empty(HeldBy(traded, 0));
        Assert.Equal(galeons, traded.Deck.Discard.ToArray()); // only the country id travels: the claim is dropped

        // Some turns later the draw pile is empty and a conquest is made: the discard is shuffled and a card drawn.
        var drawing = WithConquests(traded with { Phase = GamePhase.Attack, Turn = TurnContext.None }, 1);
        var closed = Close(drawing);

        var redrawn = Assert.Single(HeldBy(closed.State, 0));
        Assert.Contains(redrawn.TerritoryId, galeons);
        Assert.True(redrawn.BonusClaimed); // claimed NOW, because it arrived unclaimed...
        Assert.Equal(ArmiesIn(drawing, redrawn.TerritoryId) + Bonus, ArmiesIn(closed.State, redrawn.TerritoryId)); // ...and paid again
        Assert.Contains<GameEvent>(new BonusArmiesPlaced(0, redrawn.TerritoryId, Bonus), closed.Events);
    }

    // ---- inherited cards -------------------------------------------------------------------------------------------------

    [Fact]
    public void R6_TheCardsInheritedByAnElimination_ArriveUnclaimedForTheirNewHolder_TheHeirsOwnKeepTheirFlags()
    {
        // The heir already holds the claimed card of "a"; the defender holds "e" (claimed) and "f" (unclaimed).
        var before = WithHeld(
            WithHeld(LastStand(6, 1), 0, new HeldCard("a", BonusClaimed: true)),
            1,
            new HeldCard("e", BonusClaimed: true),
            new HeldCard("f", BonusClaimed: false));

        var next = Conquer(before, "a", "c");

        Assert.Equal(
            new[]
            {
                new HeldCard("a", true), // the heir's own card keeps its flag
                new HeldCard("e", false), // the claim belongs to the holder-card pair: it starts afresh
                new HeldCard("f", false),
            },
            HeldBy(next, 0));
        Assert.Empty(HeldBy(next, 1));
    }

    [Fact]
    public void R6_AnInheritedCard_OfACountryTheHeirOwns_PaysItsPremiumAtTheCloseOfThatTurn()
    {
        var before = WithHeld(
            ThreeSeats(6, 1), // seat 2 survives the elimination, so the turn can be closed
            1,
            new HeldCard("e", BonusClaimed: true), // seat 1 had cashed it in already...
            new HeldCard("f"));
        var occupied = ApplyOk(Conquer(before, "a", "c"), new MoveAfterConquest(0, 2), TinyBoard.Map).State;

        var closed = ApplyOk(occupied, new EndTurn(0), TinyBoard.Map);

        // ...but the heir owns e and f, so each pays 2 armies to the heir.
        Assert.Equal(
            new GameEvent[] { new BonusArmiesPlaced(0, "e", Bonus), new BonusArmiesPlaced(0, "f", Bonus) },
            CardPhaseEvents(closed));
        Assert.Equal(ArmiesIn(occupied, "e") + Bonus, ArmiesIn(closed.State, "e"));
        Assert.Equal(ArmiesIn(occupied, "f") + Bonus, ArmiesIn(closed.State, "f"));
        Assert.All(HeldBy(closed.State, 0), card => Assert.True(card.BonusClaimed));
    }

    // ---- hand size ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void R7_AHandThatReachesFiveByTheAward_MustBeTradedAtTheNextOwnReinforcement()
    {
        // Seat 0 holds 3 galeones and a globo; the award is a second globo: five cards at the end of the turn.
        var galeons = CardsOf(CardSymbol.Galeon, 3);
        var globos = CardsOf(CardSymbol.Globo, 2);
        var start = WithHand(Closing(conquests: 1, draw: [globos[1]]), 0, [.. galeons, globos[0]]);

        var seatZeroClosed = Close(start);
        Assert.Equal(5, HandIds(seatZeroClosed.State, 0).Length);

        var seatOneClosed = Close(PlaceWholePool(seatZeroClosed.State)); // round 2 ends, round 3 opens for seat 0
        var reinforcing = seatOneClosed.State;
        Assert.Equal((0, 3, GamePhase.Reinforcement), (reinforcing.ActiveSeat, reinforcing.Round, reinforcing.Phase));

        var refused = GameEngine.Apply(reinforcing, Reinforcing(0, (OwnedBy(reinforcing, 0)[0], 1)), RealMap);
        Assert.False(refused.IsSuccess);
        Assert.Equal(RuleViolations.TradeRequired, refused.Error.Code);

        var traded = ApplyOk(reinforcing, Trading(0, galeons)).State;
        Assert.Equal(2, HandIds(traded, 0).Length);
        Assert.Equal(GamePhase.Attack, PlaceWholePool(traded).Phase); // and now the whole pool can be placed
    }
}
