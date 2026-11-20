using FsCheck.Xunit;
using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Events;
using Teg.Domain.Rng;
using Teg.Domain.State;
using static Teg.Domain.Tests.Support.CardFixtures;
using static Teg.Domain.Tests.Support.CombatFixtures;
using static Teg.Domain.Tests.Support.GameFixtures;

namespace Teg.Domain.Tests.Cards;

/// <summary>
/// E8, E10 / R§7: properties of the cards over WHOLE random games, played by the engine from the deal on: every
/// participant trades when they must (and now and then when they may), places the pool, attacks at random with a
/// bias to the weakest neighbour, occupies, and closes the turn, so the card phase runs for every participant, round
/// after round. After EVERY command:
/// <list type="bullet">
/// <item>the 50 cards are exactly the 50 country ids, once each, spread over the draw pile, the discard pile and the
/// hands (an eliminated participant holds none);</item>
/// <item>a card kept in the same hand never loses its claimed premium;</item>
/// </list>
/// and after every <see cref="EndTurn"/> the card phase did exactly what R§7 says, checked against a model that does
/// not share code with the engine: one card at most, only when the conquests and trades say so, the top of the pile,
/// every unclaimed card of an owned country paid once (2 armies, in map order), and no unclaimed premium left on an owned
/// country of the participant who closed.
/// </summary>
public sealed class CardConservationTests
{
    private const int MaxCommands = 1200;

    private sealed class Tally
    {
        public int Commands;
        public int Awards;
        public int Reshuffles;
        public int Premiums;
        public int Trades;
        public int ForcedTrades;
        public int Closes;
    }

    /// <summary>A dealt game with both placement rounds played: round 1, the first player to attack.</summary>
    private static GameState Dealt(int participants, long seed) =>
        FinishPlacementRound(FinishPlacementRound(Deal(participants, seed)));

    /// <summary>
    /// <see cref="Dealt"/> with only <paramref name="drawLeft"/> cards in the draw pile and the rest already in the
    /// discard pile (as if they had been traded), so that the pile runs out, and is reshuffled, within a few turns.
    /// </summary>
    private static GameState ShortDeck(GameState state, int drawLeft) =>
        state with
        {
            Deck = new Deck(
                state.Deck.Draw.Take(drawLeft).ToValueArray(),
                state.Deck.Discard.Concat(state.Deck.Draw.Skip(drawLeft)).ToValueArray()),
        };

    // ---- the model ---------------------------------------------------------------------------------------------

    private static string[] EveryCard(GameState state) =>
        state.Deck.Draw
            .Concat(state.Deck.Discard)
            .Concat(state.Participants.SelectMany(p => p.Cards.Select(card => card.TerritoryId)))
            .ToArray();

    private static void AssertTheFiftyCardsAreSomewhere(GameState state)
    {
        var every = EveryCard(state);

        Assert.Equal(RealMap.Territories.Count, every.Length);
        Assert.True(
            every.ToHashSet().SetEquals(AllCardIds()),
            "The cards are not exactly the 50 country ids, once each."); // 50 entries that form the set of 50 ids: no repeat
        Assert.All(state.Participants.Where(p => p.Eliminated), p => Assert.Empty(p.Cards));
    }

    private static void AssertNoClaimIsEverLost(GameState before, GameState after)
    {
        foreach (var participant in before.Participants)
        {
            var kept = after.Participants[participant.Seat].Cards.ToDictionary(card => card.TerritoryId);
            foreach (var card in participant.Cards.Where(c => c.BonusClaimed && kept.ContainsKey(c.TerritoryId)))
            {
                Assert.True(
                    kept[card.TerritoryId].BonusClaimed,
                    $"Seat {participant.Seat} lost the claimed premium of {card.TerritoryId} while holding the card.");
            }
        }
    }

    private static bool AwardIsDue(int conquests, int trades) => (conquests >= 1 && trades < 3) || (conquests >= 2 && trades >= 3);

    /// <summary>The card phase of one EndTurn, against the model: what R§7 says, written without the engine's code.</summary>
    private static void AssertTheCardPhaseOfTheClose(GameState before, Transition closed, Tally tally)
    {
        var actor = before.ActiveSeat;
        var held = before.Participants[actor].Cards.ToList();
        var piles = before.Deck.Draw.Count + before.Deck.Discard.Count;
        var due = AwardIsDue(before.Turn.ConquestsThisTurn, before.Participants[actor].TradesDone);
        var expectedAwards = due && piles > 0 ? 1 : 0;

        var awards = closed.Events.OfType<CardAwarded>().ToList();
        Assert.Equal(expectedAwards, awards.Count);
        Assert.Equal(held.Count + expectedAwards, closed.State.Participants[actor].Cards.Count);

        if (awards.Count == 1)
        {
            tally.Awards++;
            Assert.Equal(actor, awards[0].Seat);
            if (before.Deck.Draw.Count > 0)
            {
                Assert.Equal(before.Deck.Draw[0], awards[0].Card); // the top of the pile
                Assert.Equal(before.Deck.Draw.Skip(1), closed.State.Deck.Draw);
                Assert.Equal(before.Deck.Discard, closed.State.Deck.Discard);
                Assert.Equal(before.Rng, closed.State.Rng);
            }
            else
            {
                tally.Reshuffles++; // the discard became the pile: it holds the same cards, minus the one awarded
                Assert.Empty(closed.State.Deck.Discard);
                Assert.Equal(
                    before.Deck.Discard.Order(StringComparer.Ordinal).ToArray(),
                    closed.State.Deck.Draw.Append(awards[0].Card).Order(StringComparer.Ordinal).ToArray());
                Assert.Equal(before.Deck.Discard.Count >= 2, closed.State.Rng.Draws > before.Rng.Draws);
            }

            held.Add(new HeldCard(awards[0].Card));
        }
        else
        {
            Assert.Equal(before.Deck, closed.State.Deck);
            Assert.Equal(before.Rng, closed.State.Rng);
        }

        // Premium: every card of the hand (the new one included) whose country the actor owns and was unclaimed.
        var expectedPaid = held
            .Where(card => !card.BonusClaimed && before.FindTerritory(card.TerritoryId)!.Owner == actor)
            .Select(card => card.TerritoryId)
            .OrderBy(MapIndex)
            .ToArray();
        var paid = closed.Events.OfType<BonusArmiesPlaced>().ToList();

        Assert.Equal(expectedPaid, paid.Select(p => p.TerritoryId).ToArray());
        Assert.All(paid, p => Assert.Equal((actor, 2), (p.Seat, p.Armies)));
        Assert.All(
            paid,
            p => Assert.Equal(before.FindTerritory(p.TerritoryId)!.Armies + 2, closed.State.FindTerritory(p.TerritoryId)!.Armies));
        Assert.Equal(TotalArmies(before) + 2 * paid.Count, TotalArmies(closed.State));
        tally.Premiums += paid.Count;

        // Nothing is left unclaimed on a country the closing participant owns.
        Assert.All(
            closed.State.Participants[actor].Cards,
            card =>
            {
                var owned = closed.State.FindTerritory(card.TerritoryId)!.Owner == actor;
                Assert.False(owned && !card.BonusClaimed, $"{card.TerritoryId} is owned by seat {actor} and still unclaimed.");
            });

        // The card phase touches nobody else's hand, no owner, and no trade counter.
        Assert.All(
            before.Participants.Where(p => p.Seat != actor),
            p => Assert.Equal(p.Cards, closed.State.Participants[p.Seat].Cards));
        Assert.Equal(
            before.Participants.Select(p => p.TradesDone).ToArray(),
            closed.State.Participants.Select(p => p.TradesDone).ToArray());
        Assert.Equal(
            before.Territories.Select(t => t.Owner).ToArray(),
            closed.State.Territories.Select(t => t.Owner).ToArray());
    }

    // ---- the player --------------------------------------------------------------------------------------------

    private static string[]? FindTrade(string[] hand)
    {
        for (var i = 0; i < hand.Length; i++)
        {
            for (var j = i + 1; j < hand.Length; j++)
            {
                for (var k = j + 1; k < hand.Length; k++)
                {
                    var symbols = new[] { hand[i], hand[j], hand[k] }.Select(id => RealMap.Territory(id).Symbol).Distinct().Count();
                    if (symbols is 1 or 3)
                    {
                        return [hand[i], hand[j], hand[k]];
                    }
                }
            }
        }

        return null;
    }

    private static List<(string From, string To)> LegalAttacks(GameState state) =>
        state.Territories
            .Where(origin => origin.Owner == state.ActiveSeat && origin.Armies >= 2)
            .SelectMany(origin => RealMap.Neighbors(origin.Id)
                .Where(id => state.FindTerritory(id)!.Owner != state.ActiveSeat)
                .Order(StringComparer.Ordinal)
                .Select(id => (origin.Id, id)))
            .ToList();

    /// <summary>
    /// The whole pool in one command: every continent bonus on a random country of that continent, the free armies on one
    /// random own country, three times out of four on a country that borders an enemy (so that attacks, conquests and
    /// eliminations keep happening instead of every stack sitting still).
    /// </summary>
    private static PlaceReinforcements PoolCommand(GameState state, SeededRng choices)
    {
        var seat = state.ActiveSeat;
        var placements = new List<(string Territory, int Armies)>();

        foreach (var bonus in state.Turn.ContinentPools)
        {
            var countries = RealMap.Continents.Single(c => c.Id == bonus.ContinentId).TerritoryIds.ToArray();
            placements.Add((countries[choices.NextInt(countries.Length)], bonus.Armies));
        }

        if (state.Turn.FreeArmies > 0)
        {
            var mine = OwnedBy(state, seat);
            var frontier = mine.Where(id => RealMap.Neighbors(id).Any(n => state.FindTerritory(n)!.Owner != seat)).ToArray();
            var candidates = frontier.Length > 0 && choices.NextInt(4) != 0 ? frontier : mine;
            placements.Add((candidates[choices.NextInt(candidates.Length)], state.Turn.FreeArmies));
        }

        return Reinforcing(seat, placements.ToArray());
    }

    /// <summary>The next legal command of the active participant, chosen with <paramref name="choices"/>.</summary>
    private static GameCommand NextCommand(GameState state, SeededRng choices)
    {
        var seat = state.ActiveSeat;

        switch (state.Phase)
        {
            case GamePhase.Reinforcement:
                var hand = HandIds(state, seat);
                var trade = FindTrade(hand);
                if (trade is not null && (hand.Length >= 5 || choices.NextInt(3) == 0))
                {
                    return Trading(seat, trade);
                }

                return PoolCommand(state, choices);

            case GamePhase.Attack when state.Turn.PendingConquest is { } pending:
                var limit = Math.Min(3, state.FindTerritory(pending.From)!.Armies + state.FindTerritory(pending.To)!.Armies - 1);
                return new MoveAfterConquest(seat, 1 + choices.NextInt(limit));

            case GamePhase.Attack:
                var attacks = LegalAttacks(state);
                if (attacks.Count > 0 && choices.NextInt(5) != 0)
                {
                    var (origin, target) = choices.NextInt(4) == 0
                        ? attacks[choices.NextInt(attacks.Count)]
                        : attacks.MinBy(a => state.FindTerritory(a.To)!.Armies);
                    return new Attack(seat, origin, target);
                }

                return choices.NextInt(2) == 0 ? new StartRegroup(seat) : new EndTurn(seat);

            case GamePhase.Regroup:
                return new EndTurn(seat);

            default:
                throw new InvalidOperationException($"Nothing to play in {state.Phase}.");
        }
    }

    /// <summary>
    /// Plays the game from <paramref name="start"/> until one participant is left or the command cap, checking every step.
    /// </summary>
    private static (GameState Final, Tally Tally) Play(GameState start, long choiceSeed, List<GameEvent>? history = null)
    {
        var choices = new SeededRng(choiceSeed);
        var tally = new Tally();
        var state = start;

        AssertTheFiftyCardsAreSomewhere(state);

        for (var step = 0; step < MaxCommands && state.Participants.Count(p => !p.Eliminated) > 1; step++)
        {
            var command = NextCommand(state, choices);

            var before = state;
            var transition = ApplyOk(before, command);
            state = transition.State;
            history?.AddRange(transition.Events);
            tally.Commands++;

            Assert.Equal(before.Version + 1, state.Version);
            AssertTheFiftyCardsAreSomewhere(state);
            AssertNoClaimIsEverLost(before, state);

            if (command is EndTurn)
            {
                tally.Closes++;
                AssertTheCardPhaseOfTheClose(before, transition, tally);
            }
            else if (command is TradeCards)
            {
                tally.Trades++;
                tally.ForcedTrades += HandIds(before, before.ActiveSeat).Length >= 5 ? 1 : 0;
            }
        }

        return (state, tally);
    }

    // ---- sweeps ----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void R7_OverWholeGames_TheFiftyCardsAreAlwaysSomewhere_AndEveryCardPhaseFollowsTheRules(int participants)
    {
        var total = new Tally();

        for (var seed = 1L; seed <= 20; seed++)
        {
            var (_, tally) = Play(Dealt(participants, seed), choiceSeed: seed * 7919);
            Accumulate(total, tally);
        }

        // Measured over these 20 games per size (at most 1200 commands each): 1185-1610 awards, 570-707 premiums, 363-470
        // trades (171-260 of them forced), 19-20 natural reshuffles. The floors leave a wide margin and still prove that the
        // sweep exercised the card phase.
        AssertNotVacuous(total, awards: 700, premiums: 350, trades: 200, forcedTrades: 90);
        Assert.True(total.Reshuffles > 5, $"Only {total.Reshuffles} reshuffles in {total.Awards} awards.");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void R7_OverGamesWithAShortDrawPile_TheDiscardIsReshuffledOverAndOver_AndTheCardsStayAccountedFor(int participants)
    {
        var total = new Tally();

        for (var seed = 1L; seed <= 20; seed++)
        {
            var (_, tally) = Play(ShortDeck(Dealt(participants, seed), drawLeft: 3), choiceSeed: seed * 104729);
            Accumulate(total, tally);
        }

        // Measured as above, with 40-41 reshuffles per size.
        AssertNotVacuous(total, awards: 700, premiums: 350, trades: 200, forcedTrades: 90);
        Assert.True(total.Reshuffles > 20, $"Only {total.Reshuffles} reshuffles in {total.Awards} awards.");
    }

    private static void Accumulate(Tally total, Tally tally)
    {
        total.Commands += tally.Commands;
        total.Awards += tally.Awards;
        total.Reshuffles += tally.Reshuffles;
        total.Premiums += tally.Premiums;
        total.Trades += tally.Trades;
        total.ForcedTrades += tally.ForcedTrades;
        total.Closes += tally.Closes;
    }

    private static void AssertNotVacuous(Tally total, int awards, int premiums, int trades, int forcedTrades)
    {
        var message = $"{total.Commands} commands, {total.Closes} closes, {total.Awards} awards, {total.Reshuffles} reshuffles, " +
            $"{total.Premiums} premiums, {total.Trades} trades ({total.ForcedTrades} forced)";
        Assert.True(total.Awards > awards, $"Too few awards: {message}");
        Assert.True(total.Premiums > premiums, $"Too few premiums: {message}");
        Assert.True(total.Trades > trades, $"Too few trades: {message}");
        Assert.True(total.ForcedTrades > forcedTrades, $"Too few forced trades: {message}");
    }

    [Property(MaxTest = 25)]
    public bool AnyRandomGame_KeepsTheFiftyCards_AndEveryCardPhaseFollowsTheRules(long dealSeed, long choiceSeed, byte participantsPick, bool shortPile)
    {
        var participants = 2 + participantsPick % 3;
        var start = Dealt(participants, dealSeed);

        var (_, tally) = Play(shortPile ? ShortDeck(start, drawLeft: 2) : start, choiceSeed);

        return tally.Closes > 0; // the invariants are asserted inside Play
    }

    // ---- determinism -------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(2, 11L)]
    [InlineData(3, 12L)]
    [InlineData(4, 13L)]
    public void E10_TheSameSeedAndTheSameCommands_DrawTheSameCards(int participants, long seed)
    {
        var firstHistory = new List<GameEvent>();
        var secondHistory = new List<GameEvent>();

        var (first, _) = Play(Dealt(participants, seed), choiceSeed: seed, firstHistory);
        var (second, _) = Play(Dealt(participants, seed), choiceSeed: seed, secondHistory);

        var cardsDrawn = firstHistory.OfType<CardAwarded>().ToArray();
        Assert.True(cardsDrawn.Length > 5, $"Only {cardsDrawn.Length} cards were drawn.");
        Assert.Equal(cardsDrawn, secondHistory.OfType<CardAwarded>().ToArray());
        Assert.Equal(firstHistory, secondHistory);
        Assert.Equal(first, second);
    }

    [Fact]
    public void E10_ADifferentSeed_DrawsDifferentCards()
    {
        var first = new List<GameEvent>();
        var other = new List<GameEvent>();

        Play(Dealt(3, seed: 12), choiceSeed: 12, first);
        Play(Dealt(3, seed: 13), choiceSeed: 12, other);

        Assert.NotEqual(
            first.OfType<CardAwarded>().Select(a => a.Card).ToArray(),
            other.OfType<CardAwarded>().Select(a => a.Card).ToArray());
    }
}
