namespace Teg.Domain.Rules;

/// <summary>
/// Stable codes of the <see cref="RuleViolation"/>s returned by <see cref="GameEngine.Apply"/> (API contract:
/// the HTTP layer exposes them as is).
/// </summary>
public static class RuleViolations
{
    /// <summary>The game is over (R§8): no command is accepted any more.</summary>
    public const string GameFinished = "GAME_FINISHED";

    /// <summary>The command does not belong to the current phase of the turn (R§4: phases are ordered and irreversible).</summary>
    public const string WrongPhase = "WRONG_PHASE";

    /// <summary>Someone other than the active participant tried to act (R§4).</summary>
    public const string NotActiveParticipant = "NOT_ACTIVE_PARTICIPANT";

    /// <summary>A country id that the board does not have.</summary>
    public const string UnknownTerritory = "UNKNOWN_TERRITORY";

    /// <summary>Armies placed on a country of another participant (R§3).</summary>
    public const string TerritoryNotOwned = "TERRITORY_NOT_OWNED";

    /// <summary>The origin country of an attack or regroup is not the actor's (R§6).</summary>
    public const string OriginNotOwned = "ORIGIN_NOT_OWNED";

    /// <summary>An attack aimed at the attacker's own country: the target must be an enemy (R§6).</summary>
    public const string DestinationNotEnemy = "DESTINATION_NOT_ENEMY";

    /// <summary>A regroup aimed at an enemy country: the destination must be the participant's own (R§6).</summary>
    public const string DestinationNotOwn = "DESTINATION_NOT_OWN";

    /// <summary>Origin and destination do not touch, by land or by bridge (R§6).</summary>
    public const string NotAdjacent = "NOT_ADJACENT";

    /// <summary>The origin has fewer than 2 armies (R§6).</summary>
    public const string OriginTooFewArmies = "ORIGIN_TOO_FEW_ARMIES";

    /// <summary>An army count that is zero or negative.</summary>
    public const string InvalidArmyCount = "INVALID_ARMY_COUNT";

    /// <summary>More armies than the origin can spare: at least one must stay behind (R§6).</summary>
    public const string NotEnoughArmies = "NOT_ENOUGH_ARMIES";

    /// <summary>The placed armies do not add up to the pool of the initial placement round (R§3).</summary>
    public const string WrongPlacementTotal = "WRONG_PLACEMENT_TOTAL";

    /// <summary>
    /// The command is valid, but the engine cannot execute it yet. Attack resolution arrives with the combat
    /// phase and regroup execution with the regroup phase of the change plan, which replace this answer.
    /// It is never a silent success.
    /// </summary>
    public const string NotImplementedYet = "NOT_IMPLEMENTED_YET";
}
