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

    /// <summary>The turn cannot end from Refuerzo: reinforcements are still to be placed (R§5).</summary>
    public const string ArmiesPending = "ARMIES_PENDING";

    /// <summary>Five or more cards in hand: a trade is mandatory before any reinforcement can be placed (R§7).</summary>
    public const string TradeRequired = "TRADE_REQUIRED";

    /// <summary>More reinforcements placed than the pool still holds (R§5).</summary>
    public const string ReinforcementsExceeded = "REINFORCEMENTS_EXCEEDED";

    /// <summary>The armies of a continent bonus must go to that continent (R§5.2).</summary>
    public const string ContinentBonusMisplaced = "CONTINENT_BONUS_MISPLACED";

    /// <summary>A trade names a number of cards other than three (R§7).</summary>
    public const string InvalidCardCount = "INVALID_CARD_COUNT";

    /// <summary>A trade names the same card more than once (R§7).</summary>
    public const string DuplicateCard = "DUPLICATE_CARD";

    /// <summary>A card named in a trade is not in the participant's hand (R§7).</summary>
    public const string CardNotInHand = "CARD_NOT_IN_HAND";

    /// <summary>The three cards are neither all of one symbol nor all of different symbols (R§7).</summary>
    public const string InvalidCardCombination = "INVALID_CARD_COMBINATION";

    /// <summary>A conquest is waiting for the attacker's decision: only <c>MoveAfterConquest</c> is accepted now (R§6).</summary>
    public const string ConquestPending = "CONQUEST_PENDING";

    /// <summary>Armies were moved after a conquest, but there is no conquest waiting for them (R§6).</summary>
    public const string NoConquestPending = "NO_CONQUEST_PENDING";

    /// <summary>More than three armies asked for the conquered country: at most 3 can move in after a conquest (R§6).</summary>
    public const string OccupationLimitExceeded = "OCCUPATION_LIMIT_EXCEEDED";

    /// <summary>
    /// A regroup asks for more armies than the origin may still move: its quota (armies when Reagrupe started - 1, minus
    /// what it already sent out) does not cover them. Armies that arrived in this phase cannot be moved on (R§6).
    /// </summary>
    public const string RegroupQuotaExceeded = "REGROUP_QUOTA_EXCEEDED";
}
