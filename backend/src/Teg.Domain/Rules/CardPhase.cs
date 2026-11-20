using Teg.Domain.Collections;
using Teg.Domain.Events;
using Teg.Domain.Rng;
using Teg.Domain.State;

namespace Teg.Domain.Rules;

/// <summary>
/// The card phase, Tarjeta (R§7). It has no player decision, so <see cref="TurnMachine.EndTurn"/> resolves it between the
/// move into the phase and the end of the turn, in this order.
/// <list type="number">
/// <item><see cref="Award"/>: one card, the top of the draw pile, when the turn had enough conquests: at least 1 while the
/// participant has made fewer than 3 trades, at least 2 from the third trade on. Never more than one per turn. When the
/// draw pile is empty the discard pile is shuffled into a new one with the seeded RNG; when both are empty nothing is
/// awarded.</item>
/// <item><see cref="PayPremiums"/>: every card in the hand whose country the participant owns and whose premium is still
/// unclaimed adds 2 armies to that country, once per card. This includes the card just drawn and cards whose country was
/// conquered in an earlier turn. The card stays in the hand.</item>
/// </list>
/// Pure: the only randomness is the reshuffle, drawn from the state's generator.
/// </summary>
internal static class CardPhase
{
    /// <summary>R§7: the armies a card adds to its country, once.</summary>
    internal const int PremiumArmies = 2;

    /// <summary>R§7: conquests that earn a card while the participant has made fewer than <see cref="TradesThatRaiseTheBar"/> trades.</summary>
    private const int ConquestsForACard = 1;

    /// <summary>R§7: conquests that earn a card from <see cref="TradesThatRaiseTheBar"/> trades on.</summary>
    private const int ConquestsForACardAfterManyTrades = 2;

    /// <summary>R§7: from this many trades on a card needs <see cref="ConquestsForACardAfterManyTrades"/> conquests.</summary>
    private const int TradesThatRaiseTheBar = 3;

    /// <summary>
    /// Resolves the card phase of <paramref name="seat"/>'s closing turn: the state after it and the events in the order they
    /// happened (the card awarded, if any, then the premiums in map order).
    /// </summary>
    internal static (GameState State, IReadOnlyList<GameEvent> Events) Resolve(GameState state, int seat)
    {
        var events = new List<GameEvent>();
        var awarded = Award(state, seat, events);

        return (PayPremiums(awarded, seat, events), events);
    }

    private static bool IsCardDue(GameState state, int seat)
    {
        var conquestsNeeded = state.Participants[seat].TradesDone < TradesThatRaiseTheBar
            ? ConquestsForACard
            : ConquestsForACardAfterManyTrades;

        return state.Turn.ConquestsThisTurn >= conquestsNeeded;
    }

    private static GameState Award(GameState state, int seat, List<GameEvent> events)
    {
        if (!IsCardDue(state, seat))
        {
            return state;
        }

        var withPile = RefillFromTheDiscard(state);
        if (withPile.Deck.Draw.Count == 0)
        {
            return withPile; // both piles are empty: there is nothing to award
        }

        var card = withPile.Deck.Draw[0];
        events.Add(new CardAwarded(seat, card));

        // The card arrives UNCLAIMED: the premium step below pays it in this very turn when its country is the holder's.
        return WithHand(
            withPile with { Deck = withPile.Deck with { Draw = withPile.Deck.Draw.Skip(1).ToValueArray() } },
            seat,
            hand => hand.Append(new HeldCard(card)));
    }

    /// <summary>
    /// An empty draw pile becomes the discard pile shuffled with the seeded RNG (R§7); only called when a card is due, so
    /// the generator is never touched otherwise.
    /// </summary>
    private static GameState RefillFromTheDiscard(GameState state)
    {
        if (state.Deck.Draw.Count > 0 || state.Deck.Discard.Count == 0)
        {
            return state;
        }

        var rng = new SeededRng(state.Rng.Seed, state.Rng.Draws);
        var shuffled = state.Deck.Discard.ToList();
        rng.Shuffle(shuffled);

        return state with
        {
            Deck = new Deck(shuffled.ToValueArray(), ValueArray<string>.Empty),
            Rng = rng.State,
        };
    }

    private static GameState PayPremiums(GameState state, int seat, List<GameEvent> events)
    {
        var unclaimed = state.Participants[seat].Cards.Where(card => !card.BonusClaimed).Select(card => card.TerritoryId).ToHashSet();

        // The territories are in map order, which is the order of the events.
        var paid = state.Territories
            .Where(t => t.Owner == seat && unclaimed.Contains(t.Id))
            .Select(t => t.Id)
            .ToList();
        if (paid.Count == 0)
        {
            return state;
        }

        events.AddRange(paid.Select(id => new BonusArmiesPlaced(seat, id, PremiumArmies)));

        var paidIds = paid.ToHashSet();
        var withArmies = state with
        {
            Territories = state.Territories
                .Select(t => paidIds.Contains(t.Id) ? t with { Armies = t.Armies + PremiumArmies } : t)
                .ToValueArray(),
        };

        return WithHand(
            withArmies,
            seat,
            hand => hand.Select(card => paidIds.Contains(card.TerritoryId) ? card with { BonusClaimed = true } : card));
    }

    /// <summary>The same state with <paramref name="seat"/>'s hand replaced by what <paramref name="change"/> makes of it.</summary>
    private static GameState WithHand(GameState state, int seat, Func<ValueArray<HeldCard>, IEnumerable<HeldCard>> change) =>
        state with
        {
            Participants = state.Participants
                .Select(p => p.Seat == seat ? p with { Cards = change(p.Cards).ToValueArray() } : p)
                .ToValueArray(),
        };
}
