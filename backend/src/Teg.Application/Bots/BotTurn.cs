using System.Security.Cryptography;
using System.Text;
using Teg.Application.Ports;
using Teg.Application.UseCases;
using Teg.Application.Views;
using Teg.Domain;
using Teg.Domain.Commands;
using Teg.Domain.Events;
using Teg.Domain.Map;
using Teg.Domain.Rules;
using Teg.Domain.State;

namespace Teg.Application.Bots;

/// <summary>
/// A request for ONE bot step. <paramref name="ExpectedVersion"/> is optional: when the caller (the UI) says which version it is
/// looking at, a game that has moved on is a <see cref="ErrorKind.VersionConflict"/> and the bot does not act.
/// </summary>
public sealed record BotTurnRequest(Guid GameId, int? ExpectedVersion = null);

/// <summary>What a bot step did: the new view of the game, the events of the step as the table may read them, and the command the bot sent.</summary>
public sealed record BotStepResult(GameView Game, IReadOnlyList<GameEvent> Events, GameCommand Command);

/// <summary>
/// The ids of the commands of the bot. A step's id is derived from the game and the version it was decided on, so repeating that step
/// (a retry after a crash, two requests that loaded the same version) yields the SAME id and the repository refuses the second as
/// already applied: a step is applied once. The id is a name-based UUID (a hash of <c>bot:{gameId}:{version}</c>), stable for ever.
/// </summary>
public static class BotCommandIds
{
    private static readonly Guid Namespace = new("5b8f6e0a-3c1d-4f6b-9d57-0e1c2a7b4d93");

    private const int VersionNibble = 0x80; // UUID version 8: an application-defined, name-based id
    private const int VariantBits = 0x80; // RFC 4122 variant

    public static Guid For(Guid gameId, int version)
    {
        var name = Encoding.UTF8.GetBytes(FormattableString.Invariant($"bot:{gameId:D}:{version}"));
        var hash = SHA256.HashData([.. Namespace.ToByteArray(bigEndian: true), .. name]);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | VersionNibble);
        bytes[8] = (byte)((bytes[8] & 0x3F) | VariantBits);

        return new Guid(bytes, bigEndian: true);
    }
}

/// <summary>
/// ONE step of a bot (B1-B4): the use case behind <c>bot-turn</c>. It loads the game, refuses when it is over
/// (<c>GAME_FINISHED</c>) or when the active participant is a human (<see cref="ErrorKind.NotBotTurn"/>), asks the policy for the
/// next command and sends it through <see cref="SubmitAction"/>, the very path a human's action takes: same validation, same single
/// save, with the version it loaded and the deterministic id of <see cref="BotCommandIds"/>. Every step commits on its own, so a bot
/// turn interrupted anywhere resumes from the last commit when called again: the count of the cap lives in the saved state.
/// Pacing between steps is the caller's business (the UI), not the server's.
/// </summary>
public sealed class BotTurn(IGameRepository games, SubmitAction submit, IBotPolicy policy, MapDefinition map)
{
    public async Task<Result<BotStepResult, UseCaseError>> ExecuteAsync(BotTurnRequest request, CancellationToken ct = default)
    {
        var state = await games.FindAsync(request.GameId, ct);
        if (state is null)
        {
            return Fail(UseCaseError.NotFound(request.GameId));
        }

        if (request.ExpectedVersion is { } expected && expected != state.Version)
        {
            return Fail(UseCaseError.VersionConflict(expected, state.Version));
        }

        if (state.Phase == GamePhase.Finished)
        {
            return Fail(new UseCaseError(ErrorKind.RuleViolation, RuleViolations.GameFinished, "The game is over: the bots have nothing left to do."));
        }

        var active = state.Participants[state.ActiveSeat];
        if (active.Kind != ParticipantKind.Bot)
        {
            return Fail(new UseCaseError(
                ErrorKind.NotBotTurn, ErrorCodes.NotBotTurn, $"It is the turn of {active.Name}, who is a human: a bot cannot act for them."));
        }

        var command = policy.Next(state, LegalMoves.For(state, map));
        var step = new SubmitActionRequest(request.GameId, state.Version, BotCommandIds.For(request.GameId, state.Version), command);
        var submitted = await submit.ExecuteAsync(step, ct);

        return submitted.IsSuccess
            ? Result<BotStepResult, UseCaseError>.Ok(new BotStepResult(submitted.Value.Game, submitted.Value.Events, command))
            : Fail(submitted.Error);
    }

    private static Result<BotStepResult, UseCaseError> Fail(UseCaseError error) =>
        Result<BotStepResult, UseCaseError>.Fail(error);
}
