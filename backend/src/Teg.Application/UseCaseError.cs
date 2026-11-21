using Teg.Domain;

namespace Teg.Application;

/// <summary>
/// What kind of refusal a use case gives. The API maps it to a status: <see cref="NotFound"/> 404, <see cref="Validation"/> 400,
/// <see cref="VersionConflict"/> and <see cref="AlreadyApplied"/> 409, <see cref="RuleViolation"/> and <see cref="NotBotTurn"/> 422.
/// </summary>
public enum ErrorKind
{
    /// <summary>The game does not exist.</summary>
    NotFound,

    /// <summary>The request itself is not acceptable (a game cannot be created from it): the code is the domain's <c>SetupViolations</c>.</summary>
    Validation,

    /// <summary>The action was made against a version of the game that is no longer the current one (A2).</summary>
    VersionConflict,

    /// <summary>The id of the action was already applied: the action is a repeat and changes nothing (A2).</summary>
    AlreadyApplied,

    /// <summary>The engine refused the action: the code is the engine's <c>RuleViolations</c> (A4).</summary>
    RuleViolation,

    /// <summary>A bot step was asked for while the active participant is a human (B4).</summary>
    NotBotTurn,
}

/// <summary>The stable <see cref="UseCaseError.Type"/> codes (API contract).</summary>
public static class ErrorCodes
{
    public const string NotFound = "NOT_FOUND";
    public const string Validation = "VALIDATION";
    public const string VersionConflict = "VERSION_CONFLICT";
    public const string AlreadyApplied = "ALREADY_APPLIED";
    public const string RuleViolation = "RULE_VIOLATION";
    public const string NotBotTurn = "NOT_BOT_TURN";
}

/// <summary>
/// A refused use case: a value, never an exception (the use cases return <c>Result&lt;T, UseCaseError&gt;</c>).
/// <paramref name="Code"/> is the most specific stable code: the same as <see cref="Type"/> for a missing game, a conflict or a
/// bot step on a human turn, and the domain's code (<c>NO_HUMAN_PARTICIPANT</c>, <c>NOT_ADJACENT</c>, <c>GAME_FINISHED</c>...)
/// for <see cref="ErrorKind.Validation"/> and <see cref="ErrorKind.RuleViolation"/>. <paramref name="Message"/> is for humans.
/// </summary>
public sealed record UseCaseError(ErrorKind Kind, string Code, string Message)
{
    /// <summary>The stable code of the kind (see <see cref="ErrorCodes"/>).</summary>
    public string Type => Kind switch
    {
        ErrorKind.NotFound => ErrorCodes.NotFound,
        ErrorKind.Validation => ErrorCodes.Validation,
        ErrorKind.VersionConflict => ErrorCodes.VersionConflict,
        ErrorKind.AlreadyApplied => ErrorCodes.AlreadyApplied,
        ErrorKind.RuleViolation => ErrorCodes.RuleViolation,
        ErrorKind.NotBotTurn => ErrorCodes.NotBotTurn,
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "Unknown error kind."),
    };

    internal static UseCaseError NotFound(Guid gameId) =>
        new(ErrorKind.NotFound, ErrorCodes.NotFound, $"There is no game {gameId}.");

    internal static UseCaseError FromRule(ErrorKind kind, RuleViolation violation) =>
        new(kind, violation.Code, violation.Message);

    internal static UseCaseError VersionConflict(int expectedVersion, int currentVersion) =>
        new(
            ErrorKind.VersionConflict,
            ErrorCodes.VersionConflict,
            $"The action was made against version {expectedVersion} but the game is at version {currentVersion}.");

    internal static UseCaseError AlreadyApplied(Guid gameId, Guid commandId) =>
        new(ErrorKind.AlreadyApplied, ErrorCodes.AlreadyApplied, $"The action {commandId} was already applied to game {gameId}.");
}
