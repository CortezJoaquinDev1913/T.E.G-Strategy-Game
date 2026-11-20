using Teg.Domain.Collections;

namespace Teg.Domain.State;

/// <summary>Armies that may only be placed on the countries of one continent (R§5.2).</summary>
public sealed record ContinentPool(string ContinentId, int Armies);

/// <summary>
/// What the active participant still has to place in the Refuerzo phase of the current turn (R§5). It is computed
/// ONCE, when the turn starts, and only shrinks (placements) or grows (trades) from then on.
/// </summary>
/// <param name="FreeArmies">Armies of the countries and of trades: any own country.</param>
/// <param name="ContinentPools">The bonus of every continent owned in full at the start, in map order.</param>
public sealed record TurnContext(int FreeArmies, ValueArray<ContinentPool> ContinentPools)
{
    /// <summary>Nothing to place: outside Refuerzo (initial placement, Ataque in round 1, and after the pool is spent).</summary>
    public static readonly TurnContext None = new(0, ValueArray<ContinentPool>.Empty);

    /// <summary>Every army still to be placed: free ones plus the continent bonuses.</summary>
    public int PendingArmies => FreeArmies + ContinentPools.Sum(pool => pool.Armies);
}
