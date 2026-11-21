using Teg.Application.Ports;
using Teg.Application.Views;
using Teg.Domain;
using Teg.Domain.Map;
using Teg.Domain.Rules;

namespace Teg.Application.UseCases;

/// <summary>Loads a game as a <see cref="GameView"/> (A4, A5: what was saved is what is shown, after a restart too).</summary>
public sealed class GetGame(IGameRepository games)
{
    public async Task<Result<GameView, UseCaseError>> ExecuteAsync(Guid gameId, CancellationToken ct = default)
    {
        var state = await games.FindAsync(gameId, ct);

        return state is null
            ? Result<GameView, UseCaseError>.Fail(UseCaseError.NotFound(gameId))
            : Result<GameView, UseCaseError>.Ok(GameView.From(gameId, state));
    }
}

/// <summary>What the active participant of a game may do (A4, B1): the domain's <see cref="LegalMoves"/> over the saved state.</summary>
public sealed class GetLegalMoves(IGameRepository games, MapDefinition map)
{
    public async Task<Result<LegalMoveSet, UseCaseError>> ExecuteAsync(Guid gameId, CancellationToken ct = default)
    {
        var state = await games.FindAsync(gameId, ct);

        return state is null
            ? Result<LegalMoveSet, UseCaseError>.Fail(UseCaseError.NotFound(gameId))
            : Result<LegalMoveSet, UseCaseError>.Ok(LegalMoves.For(state, map));
    }
}

/// <summary>The resume list: the games of a status (all when <c>null</c>), the most recently changed first.</summary>
public sealed class ListGames(IGameRepository games)
{
    public Task<IReadOnlyList<GameSummary>> ExecuteAsync(GameStatus? status = null, CancellationToken ct = default) =>
        games.ListAsync(status, ct);
}

/// <summary>The events of a game after a sequence number, in their public form (<see cref="PublicEvents"/>).</summary>
public sealed class GetHistory(IGameRepository games)
{
    public async Task<Result<IReadOnlyList<StoredEvent>, UseCaseError>> ExecuteAsync(Guid gameId, int afterSeq = 0, CancellationToken ct = default)
    {
        if (await games.FindAsync(gameId, ct) is null)
        {
            return Result<IReadOnlyList<StoredEvent>, UseCaseError>.Fail(UseCaseError.NotFound(gameId));
        }

        var events = await games.EventsAsync(gameId, afterSeq, ct);

        return Result<IReadOnlyList<StoredEvent>, UseCaseError>.Ok(PublicEvents.OfStored(events));
    }
}
