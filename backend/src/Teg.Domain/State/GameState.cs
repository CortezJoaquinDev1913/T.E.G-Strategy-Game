using Teg.Domain.Collections;
using Teg.Domain.Rng;

namespace Teg.Domain.State;

/// <summary>Phases of the game (R§3-R§7). <see cref="Finished"/> accepts no more moves.</summary>
public enum GamePhase
{
    InitialPlacement,
    Reinforcement,
    Attack,
    Regroup,
    Card,
    Finished,
}

/// <summary>
/// Immutable snapshot of a game. Every collection has value equality, so two states built independently
/// from the same seed and commands are equal (E10).
/// </summary>
/// <param name="Rng">Seed and draws taken so far; enough to rebuild the generator in O(1).</param>
/// <param name="Phase">Current phase.</param>
/// <param name="PlacementRound">Initial placement round (1 or 2) while in <see cref="GamePhase.InitialPlacement"/>.</param>
/// <param name="Round">Round of play; 0 until the first (hostilities) round starts.</param>
/// <param name="FirstSeat">Rank 1 of the initial dice ranking: opens every round (R§3).</param>
/// <param name="ActiveSeat">The participant who acts now.</param>
/// <param name="Ranking">Seats from rank 1 to the last rank of the initial dice ranking.</param>
/// <param name="Participants">In registration order: index = <see cref="Participant.Seat"/>.</param>
/// <param name="Territories">One entry per map country, in map order.</param>
public sealed record GameState(
    RngState Rng,
    GamePhase Phase,
    int PlacementRound,
    int Round,
    int FirstSeat,
    int ActiveSeat,
    ValueArray<int> Ranking,
    ValueArray<Participant> Participants,
    ValueArray<TerritoryState> Territories)
{
    /// <summary>Seats in turn order: registration order, circular, starting from <see cref="FirstSeat"/> (R§2, R§3).</summary>
    public IReadOnlyList<int> TableOrder =>
        Enumerable.Range(0, Participants.Count)
            .Select(offset => (FirstSeat + offset) % Participants.Count)
            .ToArray();
}
