using Teg.Domain.Collections;

namespace Teg.Domain.State;

/// <summary>Who controls a participant. Bots use exactly the same actions and validations as humans (R§2).</summary>
public enum ParticipantKind
{
    Human,
    Bot,
}

/// <summary>The six army colours of R§2 (a game uses 2 to 4 of them, each at most once).</summary>
public enum PlayerColor
{
    Azul,
    Rojo,
    Negro,
    Amarillo,
    Verde,
    Magenta,
}

/// <summary>
/// One player at the table. <paramref name="Seat"/> is the registration index (0-based) and is the stable
/// identity used everywhere (owners, ranking, active player); table order is seat order, circular (R§2).
/// </summary>
/// <param name="Eliminated">
/// The participant lost every country (R§2). They own nothing, are skipped in the rotation for ever, and the
/// flag never goes back to <c>false</c>.
/// </param>
public sealed record Participant(int Seat, string Name, ParticipantKind Kind, PlayerColor Color, bool Eliminated = false)
{
    /// <summary>
    /// The country cards in hand (R§7), in the order they arrived, each with its premium flag. They come from the card
    /// phase (one per turn at most) or from an elimination, and leave through trades.
    /// </summary>
    public ValueArray<HeldCard> Cards { get; init; } = ValueArray<HeldCard>.Empty;

    /// <summary>How many trades this participant has made in the whole game: it sets the next trade's value (R§7).</summary>
    public int TradesDone { get; init; }

    /// <summary>
    /// The secret objective (R§8), fixed when the game is created. <see cref="Objective.None"/> unless the game has four
    /// participants (a hand-built participant has none either).
    /// </summary>
    public Objective Objective { get; init; } = Objective.None;
}
