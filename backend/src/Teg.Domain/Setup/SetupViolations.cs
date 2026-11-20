namespace Teg.Domain.Setup;

/// <summary>Stable codes of <see cref="RuleViolation"/> returned by <see cref="GameSetup.Create"/> (API contract).</summary>
public static class SetupViolations
{
    public const string InvalidParticipantCount = "INVALID_PARTICIPANT_COUNT";
    public const string BlankName = "BLANK_NAME";
    public const string InvalidColor = "INVALID_COLOR";
    public const string DuplicateColor = "DUPLICATE_COLOR";
    public const string NoHumanParticipant = "NO_HUMAN_PARTICIPANT";
}
