using Teg.Application.Views;
using Teg.Domain.Events;
using Teg.Domain.State;

namespace Teg.Application.Ports;

/// <summary>What <see cref="IGameRepository.SaveAsync"/> did.</summary>
public enum SaveOutcome
{
    /// <summary>The state, its events and the command id were saved, together.</summary>
    Saved,

    /// <summary>The stored version is not the one the change was made against: nothing was saved (409 <c>VERSION_CONFLICT</c>).</summary>
    VersionConflict,

    /// <summary>The command id was already applied to this game: nothing was saved (409 <c>ALREADY_APPLIED</c>).</summary>
    AlreadyApplied,
}

/// <summary>
/// Everything one accepted action changes, which <see cref="IGameRepository.SaveAsync"/> must save as ONE unit (A3).
/// </summary>
/// <param name="ExpectedVersion">The version the action was made against: the saved one must still be it.</param>
/// <param name="NewState">The state after the action (its <c>Version</c> is <paramref name="ExpectedVersion"/> + 1).</param>
/// <param name="Events">The events of the action, in order; the repository numbers them.</param>
/// <param name="CommandId">The id of the action: applying it twice is refused with <see cref="SaveOutcome.AlreadyApplied"/>.</param>
/// <param name="ActorSeat">The participant who acted.</param>
public sealed record GameChange(
    int ExpectedVersion,
    GameState NewState,
    IReadOnlyList<GameEvent> Events,
    Guid CommandId,
    int ActorSeat);

/// <summary>One saved event, as the history shows it: <paramref name="Seq"/> is its position in the game (from 1, no gaps in the store).</summary>
public sealed record StoredEvent(int Seq, int Version, Guid CommandId, int ActorSeat, GameEvent Event);

/// <summary>Where the seeds of new games come from. A port, so that a use case never reads a clock or a random generator (E10, tests).</summary>
public interface ISeedSource
{
    long NextSeed();
}

/// <summary>
/// The store of games. A port: the use cases know nothing of how it is kept. Games are identified by a <see cref="Guid"/> that the
/// store gives; a game is its saved <see cref="GameState"/> (the version lives in it), its events, and the ids of the commands applied.
/// </summary>
public interface IGameRepository
{
    /// <summary>Stores a game that has just been created (version 0, no events) and returns the id it was given.</summary>
    Task<Guid> AddAsync(GameState initial, CancellationToken ct = default);

    /// <summary>The saved state of the game, or <c>null</c> when there is no such game.</summary>
    Task<GameState?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>Whether this command id was already applied to the game.</summary>
    Task<bool> CommandAppliedAsync(Guid gameId, Guid commandId, CancellationToken ct = default);

    /// <summary>
    /// Saves one accepted action: the new state, its events and the command id, ALL OR NOTHING (one transaction). The command id is
    /// checked first and then the version; unless the answer is <see cref="SaveOutcome.Saved"/> nothing has changed. A failure of the
    /// store itself is an exception, and leaves nothing saved either.
    /// </summary>
    Task<SaveOutcome> SaveAsync(Guid gameId, GameChange change, CancellationToken ct = default);

    /// <summary>The games with this status (all of them when <c>null</c>), the most recently changed first.</summary>
    Task<IReadOnlyList<GameSummary>> ListAsync(GameStatus? status, CancellationToken ct = default);

    /// <summary>The saved events of the game that come after <paramref name="afterSeq"/> (0 for all of them), oldest first.</summary>
    Task<IReadOnlyList<StoredEvent>> EventsAsync(Guid gameId, int afterSeq, CancellationToken ct = default);
}
