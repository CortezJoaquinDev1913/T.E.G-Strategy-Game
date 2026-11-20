using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Events;
using Teg.Domain.Map;
using Teg.Domain.Rules;
using Teg.Domain.State;
using Teg.Domain.Tests.Support;
using static Teg.Domain.Tests.Support.GameFixtures;
using static Teg.Domain.Tests.Support.ReinforcementFixtures;

namespace Teg.Domain.Tests.Cards;

/// <summary>
/// E8 / R§7: in the Reinforcement phase a participant can trade 3 cards, all of one symbol or all different, for
/// armies: 4, 7, 10, then +5 every time, with a counter PER PARTICIPANT. The armies join the free pool; the cards
/// go to the discard pile. With 5 or more cards a trade is mandatory: reinforcements cannot be placed (so the
/// phase cannot end) until fewer than 5 remain. The tests hand the cards out themselves (the card phase that deals them
/// has its own tests, <c>CardPhaseTests</c>).
/// Seat 1 is the participant under test; every test starts with 8 free armies (16 countries).
/// </summary>
public sealed class TradeTests
{
    private const int StartingPool = 8;

    private static GameState Start(string[] hand, int tradesDone = 0) =>
        WithTrades(WithHand(ReinforcementOfSeatOne(SpreadCountries(16)), 1, hand), 1, tradesDone);

    /// <summary>On the <see cref="TinyBoard"/>: a (galeon), b (globo), c (canon).</summary>
    private static readonly string[] OneOfEachOfTinyBoard = ["a", "b", "c"];

    private static string[] HandOf(GameState state, int seat = 1) => CardFixtures.HandIds(state, seat);

    // ---- values and the per-participant counter --------------------------------------------------

    [Theory]
    [InlineData(0, 4)]
    [InlineData(1, 7)]
    [InlineData(2, 10)]
    [InlineData(3, 15)]
    [InlineData(4, 20)]
    [InlineData(5, 25)]
    [InlineData(6, 30)]
    [InlineData(9, 45)]
    public void R7_TheTradeIsWorth4_7_10_AndThenFiveMoreEachTime(int tradesAlreadyDone, int armies)
    {
        var state = Start(CardsOf(CardSymbol.Galeon, 3), tradesAlreadyDone);
        var cards = HandOf(state);

        var traded = ApplyOk(state, Trading(1, cards));

        Assert.Equal(Pool(StartingPool + armies), traded.State.Turn);
        Assert.Equal(tradesAlreadyDone + 1, traded.State.Participants[1].TradesDone);
        GameEvent[] expected = [new CardsTraded(1, cards.ToValueArray(), armies)];
        Assert.Equal(expected, traded.Events.ToArray());
        Assert.Equal(state.Version + 1, traded.State.Version);
    }

    [Fact]
    public void R7_TheTradeCounter_IsPerParticipant_AnotherOnesTradesDoNotCount()
    {
        var state = WithTrades(Start(CardsOf(CardSymbol.Galeon, 3)), seat: 0, tradesDone: 5);

        var traded = ApplyOk(state, Trading(1, HandOf(state)));

        Assert.Equal(Pool(StartingPool + 4), traded.State.Turn); // seat 1's FIRST trade, not the 6th (30)
        Assert.Equal((5, 1), (traded.State.Participants[0].TradesDone, traded.State.Participants[1].TradesDone));
    }

    // ---- which three cards are a trade -----------------------------------------------------------

    [Theory]
    [InlineData(CardSymbol.Galeon)]
    [InlineData(CardSymbol.Globo)]
    [InlineData(CardSymbol.Canon)]
    public void R7_ThreeCardsOfTheSameSymbol_AreATrade(CardSymbol symbol)
    {
        var state = Start(CardsOf(symbol, 3));

        var traded = ApplyOk(state, Trading(1, HandOf(state)));

        Assert.Equal(Pool(StartingPool + 4), traded.State.Turn);
    }

    [Fact]
    public void R7_ThreeCardsOfThreeDifferentSymbols_AreATrade()
    {
        var state = Start(OneOfEach());

        var traded = ApplyOk(state, Trading(1, HandOf(state)));

        Assert.Equal(Pool(StartingPool + 4), traded.State.Turn);
    }

    [Theory]
    [InlineData(CardSymbol.Galeon, CardSymbol.Globo)]
    [InlineData(CardSymbol.Galeon, CardSymbol.Canon)]
    [InlineData(CardSymbol.Globo, CardSymbol.Galeon)]
    [InlineData(CardSymbol.Globo, CardSymbol.Canon)]
    [InlineData(CardSymbol.Canon, CardSymbol.Galeon)]
    [InlineData(CardSymbol.Canon, CardSymbol.Globo)]
    public void R7_TwoCardsOfASymbolAndOneOfAnother_AreNotATrade(CardSymbol pair, CardSymbol other)
    {
        AssertRejected(
            RuleViolations.InvalidCardCombination,
            () => Start([.. CardsOf(pair, 2), .. CardsOf(other, 1)]),
            state => Trading(1, HandOf(state)));
    }

    // ---- the command names three cards the participant holds -------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)] // a valid trade plus one more card is still not "three cards"
    public void R7_ATradeNamesExactlyThreeCards(int named)
    {
        AssertRejected(
            RuleViolations.InvalidCardCount,
            () => Start(CardsOf(CardSymbol.Galeon, 4)),
            state => Trading(1, HandOf(state).Take(named).ToArray()));
    }

    [Fact]
    public void R7_ACardThatIsNotInTheHand_IsRejected()
    {
        AssertRejected(
            RuleViolations.CardNotInHand,
            () => Start(CardsOf(CardSymbol.Galeon, 2)),
            state => Trading(1, [.. HandOf(state), CardsOf(CardSymbol.Galeon, 1, skip: 2)[0]]));
    }

    [Fact]
    public void R7_ACardThatIsNotOnTheBoard_IsRejected()
    {
        AssertRejected(
            RuleViolations.CardNotInHand,
            () => Start(CardsOf(CardSymbol.Galeon, 2)),
            state => Trading(1, [.. HandOf(state), "atlantis"]));
    }

    [Fact]
    public void R7_ACardInAnotherParticipantsHand_IsRejected()
    {
        var theirs = CardsOf(CardSymbol.Globo, 3);

        AssertRejected(
            RuleViolations.CardNotInHand,
            () => WithHand(Start(CardsOf(CardSymbol.Galeon, 3)), seat: 0, theirs),
            _ => Trading(1, theirs));
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 1)]
    public void R7_TheSameCardCannotBeNamedTwice(int first, int second, int third)
    {
        AssertRejected(
            RuleViolations.DuplicateCard,
            () => Start(CardsOf(CardSymbol.Galeon, 3)),
            state =>
            {
                var hand = HandOf(state);
                return Trading(1, hand[first], hand[second], hand[third]);
            });
    }

    // ---- what a trade changes ------------------------------------------------------------------------

    [Fact]
    public void R7_TheTradedCards_LeaveTheHandAndGoToTheDiscardPile_InTheOrderNamed()
    {
        var galeons = CardsOf(CardSymbol.Galeon, 3);
        var globo = CardsOf(CardSymbol.Globo, 1);
        var state = Start([.. galeons, .. globo]);

        var traded = ApplyOk(state, Trading(1, galeons[2], galeons[0], galeons[1])).State;

        Assert.Equal(globo, HandOf(traded));
        Assert.Equal([galeons[2], galeons[0], galeons[1]], traded.Deck.Discard.ToArray());
        Assert.Equal(1, traded.Participants[1].TradesDone);
    }

    [Fact]
    public void R7_ATrade_ChangesNothingElse()
    {
        var state = WithHand(Start(CardsOf(CardSymbol.Galeon, 3)), seat: 0, CardsOf(CardSymbol.Canon, 2));
        var cards = HandOf(state);

        var traded = ApplyOk(state, Trading(1, cards)).State;

        var expected = state with
        {
            Version = state.Version + 1,
            Turn = Pool(StartingPool + 4),
            Deck = state.Deck with { Discard = cards.ToValueArray() },
            Participants = state.Participants
                .Select(p => p.Seat == 1 ? p with { Cards = ValueArray<HeldCard>.Empty, TradesDone = 1 } : p)
                .ToValueArray(),
        };
        Assert.Equal(expected, traded);
    }

    [Fact]
    public void R7_SeveralTradesInTheSameReinforcement_AreAllowed_EachWorthMore()
    {
        var first = OneOfEach(skip: 0);
        var second = OneOfEach(skip: 1);
        var state = Start([.. first, .. second]);

        var afterFirst = ApplyOk(state, Trading(1, first));
        var afterSecond = ApplyOk(afterFirst.State, Trading(1, second));

        Assert.Equal(Pool(StartingPool + 4), afterFirst.State.Turn);
        Assert.Equal(Pool(StartingPool + 4 + 7), afterSecond.State.Turn);
        GameEvent[] expected = [new CardsTraded(1, second.ToValueArray(), 7)];
        Assert.Equal(expected, afterSecond.Events.ToArray());
        Assert.Empty(HandOf(afterSecond.State));
        Assert.Equal([.. first, .. second], afterSecond.State.Deck.Discard.ToArray());
        Assert.Equal(2, afterSecond.State.Participants[1].TradesDone);
    }

    [Fact]
    public void R7_ATradeAfterAPartialPlacement_AddsItsArmiesToWhatRemains()
    {
        var state = Start(CardsOf(CardSymbol.Globo, 3));
        var placed = ApplyOk(state, Reinforcing(1, (OwnedBy(state, 1)[0], 5))).State; // 3 remain

        var traded = ApplyOk(placed, Trading(1, HandOf(placed))).State;

        Assert.Equal(Pool(3 + 4), traded.Turn);
        Assert.Equal(GamePhase.Reinforcement, traded.Phase);
    }

    [Fact]
    public void R7_TheArmiesOfATrade_AreFree_TheyCanGoOutsideAContinentBonus()
    {
        var asiaAndOthers = ContinentCountries("asia")
            .Concat(new[] { "america-del-norte", "america-del-sur", "europa", "africa" }.Select(c => ContinentCountries(c)[0]));
        var cannons = CardsOf(CardSymbol.Canon, 3);
        GameState AfterTrade() => ApplyOk(
            WithHand(ReinforcementOfSeatOne(asiaAndOthers), 1, cannons), // 9 free + 7 for Asia
            Trading(1, cannons)).State;
        var traded = AfterTrade();
        var outside = OwnedBy(traded, 1).Except(ContinentCountries("asia")).First();

        Assert.Equal(Pool(13, ("asia", 7)), traded.Turn);
        var placed = ApplyOk(traded, Reinforcing(1, (outside, 13))).State; // 9 + 4 free armies, all outside Asia
        Assert.Equal(Pool(0, ("asia", 7)), placed.Turn);
        AssertRejected(
            RuleViolations.ContinentBonusMisplaced,
            AfterTrade,
            _ => Reinforcing(1, (outside, 14)));
    }

    // ---- when a trade can be made ---------------------------------------------------------------------

    [Theory]
    [InlineData(GamePhase.InitialPlacement)]
    [InlineData(GamePhase.Attack)]
    [InlineData(GamePhase.Regroup)]
    [InlineData(GamePhase.Card)]
    public void R7_CardsCanOnlyBeTradedInTheReinforcementPhase(GamePhase phase)
    {
        AssertRejected(
            RuleViolations.WrongPhase,
            () => WithHand(TinyBoard.State(phase), 0, OneOfEachOfTinyBoard),
            _ => Trading(0, OneOfEachOfTinyBoard),
            TinyBoard.Map);
    }

    [Fact]
    public void R7_NoTradeInAFinishedGame()
    {
        AssertRejected(
            RuleViolations.GameFinished,
            () => WithHand(TinyBoard.State(GamePhase.Finished), 0, OneOfEachOfTinyBoard),
            _ => Trading(0, OneOfEachOfTinyBoard),
            TinyBoard.Map);
    }

    [Fact]
    public void R7_TheSameTrade_InTheReinforcementPhase_IsAccepted_ThePhaseIsTheOnlyDifference()
    {
        var state = WithHand(TinyBoard.State(GamePhase.Reinforcement), 0, OneOfEachOfTinyBoard);

        var traded = ApplyOk(state, Trading(0, OneOfEachOfTinyBoard), TinyBoard.Map);

        Assert.Equal(Pool(4), traded.State.Turn);
    }

    [Fact]
    public void R7_OnlyTheActiveParticipantCanTrade()
    {
        var theirs = CardsOf(CardSymbol.Globo, 3);

        AssertRejected(
            RuleViolations.NotActiveParticipant,
            () => WithHand(Start(CardsOf(CardSymbol.Galeon, 3)), seat: 0, theirs),
            _ => Trading(0, theirs)); // a perfectly valid trade, from seat 0, while seat 1 is the one playing
    }

    // ---- mandatory trade with 5 or more cards ----------------------------------------------------------

    [Theory]
    [InlineData(3, 0)] // 3 and 4 cards: the trade is optional
    [InlineData(4, 0)]
    [InlineData(5, 1)]
    [InlineData(6, 1)] // by inheritance (R§7): the same rule, trade until fewer than 5
    [InlineData(7, 1)]
    [InlineData(8, 2)]
    [InlineData(9, 2)]
    [InlineData(10, 2)]
    [InlineData(11, 3)]
    public void R7_WithFiveOrMoreCards_NothingCanBePlaced_UntilTradesLeaveFewerThanFive(int handSize, int tradesRequired)
    {
        var state = Start(CardsOf(CardSymbol.Galeon, handSize));
        var probe = Reinforcing(1, (OwnedBy(state, 1)[0], 1));
        var trades = 0;

        var attempt = GameEngine.Apply(state, probe, RealMap);
        while (!attempt.IsSuccess)
        {
            Assert.Equal(RuleViolations.TradeRequired, attempt.Error.Code);
            state = ApplyOk(state, Trading(1, HandOf(state).Take(3).ToArray())).State;
            trades++;
            attempt = GameEngine.Apply(state, probe, RealMap);
        }

        Assert.Equal(tradesRequired, trades);
        Assert.True(HandOf(state).Length < 5);
        Assert.Equal(handSize - 3 * tradesRequired, HandOf(state).Length);
    }

    [Fact]
    public void R7_WithFiveCards_ThePhaseCannotBeFinished_ByPlacingEverythingEither()
    {
        AssertRejected(
            RuleViolations.TradeRequired,
            () => Start(CardsOf(CardSymbol.Galeon, 5)),
            state => Reinforcing(1, (OwnedBy(state, 1)[0], StartingPool)));
    }

    [Fact]
    public void R7_WithFiveCards_TheTurnCannotBeEnded_ArmiesArePending()
    {
        AssertRejected(
            RuleViolations.ArmiesPending,
            () => Start(CardsOf(CardSymbol.Galeon, 5)),
            _ => new EndTurn(1));
    }

    [Fact]
    public void R7_WithFourCards_ReinforcementsCanBePlacedWithoutTrading_AndTheyAreKept()
    {
        var state = Start(CardsOf(CardSymbol.Globo, 4));

        var placed = ApplyOk(state, Reinforcing(1, (OwnedBy(state, 1)[0], StartingPool))).State;

        Assert.Equal(GamePhase.Attack, placed.Phase);
        Assert.Equal(4, HandOf(placed).Length);
        Assert.Equal(0, placed.Participants[1].TradesDone);
    }

    [Fact]
    public void R7_WithFourCards_ATradeIsOptional_ButAllowed()
    {
        var state = Start(CardsOf(CardSymbol.Globo, 4));

        var traded = ApplyOk(state, Trading(1, HandOf(state).Take(3).ToArray())).State;

        Assert.Single(HandOf(traded));
    }

    [Fact]
    public void R7_TheMandatoryTradeOfAnotherParticipant_DoesNotBlockThisOne()
    {
        var state = WithHand(Start(CardsOf(CardSymbol.Galeon, 3)), seat: 0, CardsOf(CardSymbol.Globo, 7));

        var placed = ApplyOk(state, Reinforcing(1, (OwnedBy(state, 1)[0], 1))).State;

        Assert.Equal(Pool(StartingPool - 1), placed.Turn);
    }

    [Fact]
    public void R7_AfterTheMandatoryTrade_TheReinforcementsIncludeItsArmies_AndCanBePlaced()
    {
        var state = Start(CardsOf(CardSymbol.Galeon, 5));
        var traded = ApplyOk(state, Trading(1, HandOf(state).Take(3).ToArray())).State; // 8 + 4 = 12 armies

        var placed = ApplyOk(traded, Reinforcing(1, (OwnedBy(traded, 1)[0], 12))).State;

        Assert.Equal(GamePhase.Attack, placed.Phase);
        Assert.Equal(2, HandOf(placed).Length);
    }

    [Fact]
    public void R7_WithFiveCards_ThereIsAlwaysAValidTrade_WhateverTheSymbols()
    {
        var symbolSplits = 0;

        for (var galeons = 0; galeons <= 5; galeons++)
        {
            for (var globes = 0; globes <= 5 - galeons; globes++)
            {
                var cannons = 5 - galeons - globes;
                var hand = new[]
                {
                    CardsOf(CardSymbol.Galeon, galeons),
                    CardsOf(CardSymbol.Globo, globes),
                    CardsOf(CardSymbol.Canon, cannons),
                }.SelectMany(cards => cards).ToArray();
                var state = Start(hand);

                var accepted = 0;
                foreach (var triple in Triples(hand))
                {
                    var symbols = triple.Select(card => RealMap.Territory(card).Symbol).Distinct().Count();
                    var result = GameEngine.Apply(state, Trading(1, triple), RealMap);

                    // Valid exactly when all the same (1 symbol) or all different (3 symbols).
                    Assert.Equal(symbols is 1 or 3, result.IsSuccess);
                    accepted += result.IsSuccess ? 1 : 0;
                }

                Assert.True(accepted > 0, $"{galeons}/{globes}/{cannons}: no valid trade found.");
                symbolSplits++;
            }
        }

        Assert.Equal(21, symbolSplits); // every way of splitting 5 cards over 3 symbols
    }

    private static IEnumerable<string[]> Triples(string[] hand)
    {
        for (var i = 0; i < hand.Length; i++)
        {
            for (var j = i + 1; j < hand.Length; j++)
            {
                for (var k = j + 1; k < hand.Length; k++)
                {
                    yield return [hand[i], hand[j], hand[k]];
                }
            }
        }
    }

    // ---- the codes ------------------------------------------------------------------------------

    [Theory]
    [InlineData(RuleViolations.InvalidCardCount, "INVALID_CARD_COUNT")]
    [InlineData(RuleViolations.DuplicateCard, "DUPLICATE_CARD")]
    [InlineData(RuleViolations.CardNotInHand, "CARD_NOT_IN_HAND")]
    [InlineData(RuleViolations.InvalidCardCombination, "INVALID_CARD_COMBINATION")]
    public void TheTradeViolationCodes_AreStable_BecauseTheApiExposesThem(string code, string literal)
    {
        Assert.Equal(literal, code);
    }
}
