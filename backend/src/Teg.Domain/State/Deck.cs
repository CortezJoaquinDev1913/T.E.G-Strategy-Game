using Teg.Domain.Collections;

namespace Teg.Domain.State;

/// <summary>
/// A card in a hand (R§7, R§9): the card is identified by its country id and its symbol comes from the map.
/// <paramref name="BonusClaimed"/> is the flag of the country premium: once the 2 armies were paid for this card the flag
/// is set and never goes back to <c>false</c> while the card stays in THIS hand. The claim belongs to the pair
/// holder-card: a card that leaves the hand (a trade sends it to the discard pile, an elimination hands it to the heir)
/// starts unclaimed again wherever it lands.
/// </summary>
public sealed record HeldCard(string TerritoryId, bool BonusClaimed = false);

/// <summary>
/// The cards that nobody holds (R§3, R§7): <paramref name="Draw"/> is the draw pile with the TOP card FIRST, and
/// <paramref name="Discard"/> the traded cards, oldest first. The 50 country cards are, at every moment, each in exactly
/// one of the draw pile, the discard pile and the hands.
/// </summary>
public sealed record Deck(ValueArray<string> Draw, ValueArray<string> Discard)
{
    /// <summary>No card anywhere: the deck of a state built by hand.</summary>
    public static readonly Deck Empty = new(ValueArray<string>.Empty, ValueArray<string>.Empty);
}
