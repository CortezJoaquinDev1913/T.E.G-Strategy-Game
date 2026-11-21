using Teg.Domain.Collections;

namespace Teg.Domain.State;

/// <summary>Armies that may only be placed on the countries of one continent (R§5.2).</summary>
public sealed record ContinentPool(string ContinentId, int Armies);

/// <summary>
/// A conquest waiting for the attacker's decision (R§6): <paramref name="To"/> was just taken from
/// <paramref name="From"/> and the attacker must say how many armies it ends up with.
/// </summary>
public sealed record PendingConquest(string From, string To);

/// <summary>How many more armies the country <paramref name="TerritoryId"/> may send out in Reagrupe (R§6).</summary>
public sealed record RegroupQuota(string TerritoryId, int Movable);

/// <summary>
/// What belongs to the CURRENT TURN of the active participant. A fresh one is created every time a turn starts, so
/// everything in it begins from zero for each turn of each participant.
/// <list type="bullet">
/// <item>Refuerzo (R§5): the armies still to be placed. It is computed ONCE, when the turn starts, and only shrinks
/// (placements) or grows (trades) from then on.</item>
/// <item>Ataque (R§6): the conquests of the turn and the occupation waiting for the attacker's decision.</item>
/// <item>Reagrupe (R§6): the quota of every own country, fixed when the phase starts (<see cref="RegroupQuotas"/>).</item>
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

    /// <summary>
    /// The quotas of Reagrupe (R§6): one entry per country of the active participant, fixed ONCE when the phase starts as
    /// <c>armies then - 1</c> (map order, a country with one army has a quota of zero) and spent by the regroups that
    /// leave it. Empty outside Reagrupe.
    /// </summary>
    public ValueArray<RegroupQuota> RegroupQuotas { get; init; } = ValueArray<RegroupQuota>.Empty;

    /// <summary>
    /// How many actions a BOT has taken in this turn (design: the bot cap of 60 per turn). It is data for the Application layer:
    /// the engine never raises it, it only carries it. It is zero when the turn starts (<c>EndTurn</c> builds a fresh context for
    /// whoever receives the turn, so it starts from zero for every turn) and it survives every command that does not end the turn
    /// (the rules copy the context with <c>with</c>). It lives in the saved state so that a bot turn interrupted by a reload resumes
    /// with the count intact. Initial placement never builds a context (one command per turn): the Application does not count there.
    /// </summary>
    public int BotActions { get; init; }

    /// <summary>
    /// How many more armies <paramref name="territoryId"/> may send out in this Reagrupe; zero when it has spent its
    /// quota, when it is not the active participant's, or when the phase has not started.
    /// </summary>
    public int RegroupQuotaOf(string territoryId) =>
        RegroupQuotas.FirstOrDefault(quota => quota.TerritoryId == territoryId)?.Movable ?? 0;

    /// <summary>Every army still to be placed: free ones plus the continent bonuses.</summary>
    public int PendingArmies => FreeArmies + ContinentPools.Sum(pool => pool.Armies);
}
