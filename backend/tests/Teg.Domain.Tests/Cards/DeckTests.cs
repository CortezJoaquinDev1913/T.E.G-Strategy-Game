using FsCheck.Xunit;
using Teg.Domain.Map;
using Teg.Domain.Rng;
using Teg.Domain.Setup;
using Teg.Domain.State;
using static Teg.Domain.Tests.Support.GameFixtures;

namespace Teg.Domain.Tests.Cards;

/// <summary>
/// E8 / R§3, R§7: the deck. At creation the 50 cards (one per country) form the draw pile, shuffled with the game's
/// seeded RNG RIGHT AFTER the deal (R§3: "las tarjetas vuelven al mazo y se mezclan de nuevo"), the discard pile is
/// empty and nobody holds a card. The deck shuffle comes after every draw the deal takes, so the deal of a seed
/// (ranking and ownership) is exactly what it was before the deck existed.
/// </summary>
public sealed class DeckTests
{
    private static readonly MapDefinition Map = RealMap;

    /// <summary>
    /// The deal ALONE, replayed from the seed the way R§3 states it: one die per participant (ties re-rolled), a shuffle
    /// of the 50 country ids, dealt one by one in rank order. This is what <c>GameSetup.Create</c> did before the deck.
    /// </summary>
    private sealed record ReplayedDeal(int[] Ranking, Dictionary<string, int> Owners, SeededRng Rng);

    private static ReplayedDeal ReplayTheDealAlone(int participants, long seed)
    {
        var rng = new SeededRng(seed);
        var ranking = InitialRanking.Determine(participants, rng.RollDie).ToArray();
        var shuffled = Map.Territories.Select(t => t.Id).ToArray();
        rng.Shuffle(shuffled);

        var owners = shuffled
            .Select((id, position) => (Id: id, Owner: ranking[position % ranking.Length]))
            .ToDictionary(deal => deal.Id, deal => deal.Owner);

        return new ReplayedDeal(ranking, owners, rng);
    }

    // ---- what the deck is at creation -----------------------------------------------------------------

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void R3_TheDeckStartsAsTheFiftyCountryCards_OneEach(int participants)
    {
        var state = Deal(participants, seed: 21);

        Assert.Equal(50, state.Deck.Draw.Count);
        Assert.Equal(
            Map.Territories.Select(t => t.Id).Order(StringComparer.Ordinal).ToArray(),
            state.Deck.Draw.Order(StringComparer.Ordinal).ToArray()); // all 50, none missing, none repeated
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void R3_TheDiscardPileStartsEmpty_AndNobodyHoldsACard_TheDealtCardsWentBackToTheDeck(int participants)
    {
        var state = Deal(participants, seed: 8);

        Assert.Equal(50, state.Deck.Draw.Count); // so "empty" below is not an artefact of a missing deck
        Assert.Empty(state.Deck.Discard);
        Assert.All(state.Participants, p => Assert.Empty(p.Cards));
        Assert.All(state.Participants, p => Assert.Equal(0, p.TradesDone));
    }

    // ---- the deal stays what it was --------------------------------------------------------------------

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void R3_TheDealOfASeed_IsIdenticalToTheDealAlone_RankingAndOwnership(int participants)
    {
        for (var seed = 0L; seed < 60; seed++)
        {
            var expected = ReplayTheDealAlone(participants, seed);

            var state = Deal(participants, seed);

            Assert.Equal(expected.Ranking, state.Ranking.ToArray());
            Assert.Equal(
                Map.Territories.Select(t => (t.Id, Owner: expected.Owners[t.Id])).ToArray(),
                state.Territories.Select(t => (t.Id, t.Owner)).ToArray());
        }
    }

    // ---- the shuffle ------------------------------------------------------------------------------------

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void R3_TheDeckIsTheMapCardsShuffledWithTheSeededRng_RightAfterTheDeal(int participants)
    {
        for (var seed = 1L; seed <= 40; seed++)
        {
            var replay = ReplayTheDealAlone(participants, seed);
            var expectedDeck = Map.Territories.Select(t => t.Id).ToArray();
            replay.Rng.Shuffle(expectedDeck); // the very next draws of the generator after the deal

            var afterTheDeck = replay.Rng.State;

            var state = Deal(participants, seed);

            Assert.Equal(expectedDeck, state.Deck.Draw.ToArray());

            // The state carries the generator after the deck shuffle... and, with four participants only, after the draw of the
            // secret objectives (R§8), which comes LAST: one shuffle of the fifteen objective numbers. With two or three
            // participants nothing is drawn after the deck, so the generator is exactly where the deck shuffle left it.
            if (participants == 4)
            {
                replay.Rng.Shuffle(Enumerable.Range(1, 15).ToArray());
                Assert.NotEqual(afterTheDeck, state.Rng);
                Assert.Equal(replay.Rng.State, state.Rng);
            }
            else
            {
                Assert.Equal(afterTheDeck, state.Rng);
            }
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void R3_TheDeckShuffle_ConsumesRngDraws_OnTopOfTheDeal(int participants)
    {
        for (var seed = 1L; seed <= 20; seed++)
        {
            var dealAloneDraws = ReplayTheDealAlone(participants, seed).Rng.Draws;

            var state = Deal(participants, seed);

            // A Fisher-Yates over 50 cards takes 49 draws at least (rejection sampling can take more).
            Assert.True(
                state.Rng.Draws >= dealAloneDraws + 49,
                $"seed {seed}: {state.Rng.Draws} draws after the deck, {dealAloneDraws} after the deal alone.");
        }
    }

    [Fact]
    public void R3_TheDeckOrder_IsNotTheMapOrder_AndVariesWithTheSeed()
    {
        var mapOrder = Map.Territories.Select(t => t.Id).ToArray();

        var decks = Enumerable.Range(1, 20).Select(seed => Deal(3, seed).Deck.Draw.ToArray()).ToArray();

        Assert.All(decks, deck => Assert.NotEqual(mapOrder, deck));
        Assert.Equal(20, decks.Select(deck => string.Join(",", deck)).Distinct().Count());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void E10_TheSameSeedGivesTheSameDeck_ADifferentSeedDoesNot(int participants)
    {
        var first = Deal(participants, seed: 20260101);
        var again = Deal(participants, seed: 20260101);
        var other = Deal(participants, seed: 20260102);

        Assert.Equal(first.Deck, again.Deck);
        Assert.NotEqual(first.Deck.Draw.ToArray(), other.Deck.Draw.ToArray());
    }

    // ---- invariant at creation ---------------------------------------------------------------------------

    [Property(MaxTest = 200)]
    public bool AnyDeal_StartsWithAllFiftyCardsInTheDrawPile_AndNothingElseAnywhere(long seed, byte participantsPick)
    {
        var participants = 2 + participantsPick % 3;

        var state = Deal(participants, seed);

        return state.Deck.Draw.Count == 50
            && state.Deck.Draw.Distinct().Count() == 50
            && state.Deck.Draw.All(id => Map.Territories.Any(t => t.Id == id))
            && state.Deck.Discard.Count == 0
            && state.Participants.All(p => p.Cards.Count == 0);
    }
}
