using Teg.Domain.Collections;
using Teg.Domain.State;
using static Teg.Domain.Tests.Support.GameFixtures;

namespace Teg.Domain.Tests.Support;

/// <summary>
/// Builders of the card tests: hands, deck and the conquest counter of a hand-built state. A hand-built state has an
/// EMPTY deck unless a test gives it one, so every test written before the card phase existed keeps its meaning (an
/// empty deck awards nothing).
/// </summary>
internal static class CardFixtures
{
    /// <summary>The country ids of the cards in <paramref name="seat"/>'s hand, in hand order.</summary>
    public static string[] HandIds(GameState state, int seat) =>
        state.Participants[seat].Cards.Select(card => card.TerritoryId).ToArray();

    /// <summary>The cards (with their premium flag) in <paramref name="seat"/>'s hand, in hand order.</summary>
    public static HeldCard[] HeldBy(GameState state, int seat) => state.Participants[seat].Cards.ToArray();

    /// <summary>
    /// <paramref name="seat"/> holds exactly these cards. The same cards are taken out of the draw pile and the discard
    /// pile when they are there, so that a dealt game keeps all its cards somewhere (and only once).
    /// </summary>
    public static GameState WithHeld(GameState state, int seat, params HeldCard[] cards)
    {
        var dealt = cards.Select(card => card.TerritoryId).ToHashSet();

        return state with
        {
            Participants = state.Participants
                .Select(p => p.Seat == seat ? p with { Cards = cards.ToValueArray() } : p)
                .ToValueArray(),
            Deck = new Deck(
                state.Deck.Draw.Where(id => !dealt.Contains(id)).ToValueArray(),
                state.Deck.Discard.Where(id => !dealt.Contains(id)).ToValueArray()),
        };
    }

    /// <summary>The draw pile (top card FIRST) and the discard pile are exactly these.</summary>
    public static GameState WithDeck(GameState state, string[] draw, string[]? discard = null) =>
        state with { Deck = new Deck(draw.ToValueArray(), (discard ?? []).ToValueArray()) };

    /// <summary>The active participant has conquered <paramref name="conquests"/> countries in this turn (R§6).</summary>
    public static GameState WithConquests(GameState state, int conquests) =>
        state with { Turn = state.Turn with { ConquestsThisTurn = conquests } };

    private static readonly string[] CardIdsInMapOrder = RealMap.Territories.Select(t => t.Id).ToArray();

    private static readonly Dictionary<string, int> MapPositions =
        CardIdsInMapOrder.Select((id, position) => (id, position)).ToDictionary(entry => entry.id, entry => entry.position);

    /// <summary>Every card of the map exactly once, in map order.</summary>
    public static string[] AllCardIds() => [.. CardIdsInMapOrder];

    /// <summary>Position of the country in map order: the order in which per-country events are emitted.</summary>
    public static int MapIndex(string territoryId) => MapPositions[territoryId];
}
