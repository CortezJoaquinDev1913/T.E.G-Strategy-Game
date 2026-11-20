using Teg.Domain.Collections;

namespace Teg.Domain.State;

/// <summary>Armies that may only be placed on the countries of one continent (R§5.2).</summary>
public sealed record ContinentPool(string ContinentId, int Armies);

/// <summary>
/// A conquest waiting for the attacker's decision (R§6): <paramref name="To"/> was just taken from
/// <paramref name="From"/> and the attacker must say how many armies it ends up with.
/// </summary>
public sealed record PendingConquest(string From, string To);

/// <summary>
/// What belongs to the CURRENT TURN of the active participant. A fresh one is created every time a turn starts, so
/// everything in it begins from zero for each turn of each participant.
/// <list type="bullet">
/// <item>Refuerzo (R§5): the armies still to be placed. It is computed ONCE, when the turn starts, and only shrinks
/// (placements) or grows (trades) from then on.</item>
/// <item>Ataque (R§6): the conquests of the turn and the occupation waiting for the attacker's decision.</item>
/// </list>
/// </summary>
/// <param name="FreeArmies">Armies of the countries and of trades: any own country.</param>
/// <param name="ContinentPools">The bonus of every continent owned in full at the start, in map order.</param>
public sealed record TurnContext(int FreeArmies, ValueArray<ContinentPool> ContinentPools)
{
    /// <summary>Nothing to place: outside Refuerzo (initial placement, Ataque in round 1, and after the pool is spent).</summary>
    public static readonly TurnContext None = new(0, ValueArray<ContinentPool>.Empty);

    /// <summary>
    /// Countries the active participant conquered in this turn (R§6 <c>conquistasEnTurno</c>); the card phase reads it.
    /// Zero when the turn starts.
    /// </summary>
    public int ConquestsThisTurn { get; init; }

    /// <summary>
    /// The conquest waiting for <c>MoveAfterConquest</c>, or <c>null</c>. While it is set, no other command is accepted.
    /// </summary>
    public PendingConquest? PendingConquest { get; init; }

    /// <summary>Every army still to be placed: free ones plus the continent bonuses.</summary>
    public int PendingArmies => FreeArmies + ContinentPools.Sum(pool => pool.Armies);
}
