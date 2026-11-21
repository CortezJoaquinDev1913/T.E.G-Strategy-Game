using Teg.Application.Ports;
using Teg.Application.Views;
using Teg.Domain;
using Teg.Domain.Commands;
using Teg.Domain.Events;
using Teg.Domain.Map;
using Teg.Domain.State;

namespace Teg.Application.UseCases;

/// <summary>
/// An action of a participant (A2): the game, the version it was made against, an id of its own (so that repeating it is harmless)
/// and the command.
/// </summary>
public sealed record SubmitActionRequest(Guid GameId, int ExpectedVersion, Guid CommandId, GameCommand Command);

/// <summary>What an accepted action gives back: the new view of the game and the events of the action, as the table may read them (<see cref="PublicEvents"/>).</summary>
public sealed record SubmitActionResult(GameView Game, IReadOnlyList<GameEvent> Events);

/// <summary>
/// The one way to change a game, for a human and for a bot alike (B1): load, judge the id and the version, let the engine decide, and
/// save the new state, its events and the command id in ONE repository call (A3). Everything that can refuse is a value:
/// <see cref="ErrorKind.NotFound"/>, <see cref="ErrorKind.AlreadyApplied"/> (the id was applied: judged before the version, so a
/// repeat of an applied action is recognised even when it is also stale), <see cref="ErrorKind.VersionConflict"/>, and
/// <see cref="ErrorKind.RuleViolation"/> with the engine's code. A refused action saves nothing and does not use up its id.
/// A save that fails (the store breaks down) is an exception, and nothing is saved either.
/// <para>
/// BOT COUNTER. When the acting participant is a bot and the action does not end the turn, the new state is saved with
/// <c>Turn.BotActions</c> one higher, so the count of the turn lives in the saved state and survives a reload (B2, B3). A turn that
/// ends (<c>EndTurn</c>, or a placement of the initial rounds, which is a whole turn) leaves the next participant a fresh count, and
/// the actions of a human are never counted.
/// </para>
/// </summary>
public sealed class SubmitAction(IGameRepository games, MapDefinition map)
{
    public async Task<Result<SubmitActionResult, UseCaseError>> ExecuteAsync(SubmitActionRequest request, CancellationToken ct = default)
    {
        var state = await games.FindAsync(request.GameId, ct);
        if (state is null)
        {
            return Fail(UseCaseError.NotFound(request.GameId));
        }

        // The id first, then the version: a repeat of an applied action is a repeat even when the game has moved on since.
        if (await games.CommandAppliedAsync(request.GameId, request.CommandId, ct))
        {
            return Fail(UseCaseError.AlreadyApplied(request.GameId, request.CommandId));
        }

        if (state.Version != request.ExpectedVersion)
        {
            return Fail(UseCaseError.VersionConflict(request.ExpectedVersion, state.Version));
        }

        var applied = GameEngine.Apply(state, request.Command, map);
        if (!applied.IsSuccess)
        {
            return Fail(UseCaseError.FromRule(ErrorKind.RuleViolation, applied.Error));
        }

        var transition = applied.Value;
        var next = CountBotAction(state, request.Command, transition.State);
        var change = new GameChange(request.ExpectedVersion, next, transition.Events, request.CommandId, request.Command.Seat);

        return await games.SaveAsync(request.GameId, change, ct) switch
        {
            SaveOutcome.Saved => Result<SubmitActionResult, UseCaseError>.Ok(
                new SubmitActionResult(GameView.From(request.GameId, next), PublicEvents.OfAll(transition.Events))),
            SaveOutcome.AlreadyApplied => Fail(UseCaseError.AlreadyApplied(request.GameId, request.CommandId)),
            SaveOutcome.VersionConflict => Fail(UseCaseError.VersionConflict(request.ExpectedVersion, state.Version)),
            var unknown => throw new InvalidOperationException($"Unknown save outcome {unknown}."),
        };
    }

    /// <summary>
    /// The bot counter (see the class remarks): one more when a bot acted and it is still the same participant's turn afterwards.
    /// Whoever receives a turn gets the fresh context the engine builds, which starts from zero.
    /// </summary>
    private static GameState CountBotAction(GameState before, GameCommand command, GameState after)
    {
        var botActed = before.Participants[command.Seat].Kind == ParticipantKind.Bot;
        var sameTurn = after.ActiveSeat == command.Seat;

        return botActed && sameTurn
            ? after with { Turn = after.Turn with { BotActions = after.Turn.BotActions + 1 } }
            : after;
    }

    private static Result<SubmitActionResult, UseCaseError> Fail(UseCaseError error) =>
        Result<SubmitActionResult, UseCaseError>.Fail(error);
}
