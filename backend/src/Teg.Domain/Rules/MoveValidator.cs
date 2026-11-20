using Teg.Domain.Commands;
using Teg.Domain.Map;
using Teg.Domain.State;

namespace Teg.Domain.Rules;

/// <summary>Validation of attacks and regroups (E4 / R§6). Pure: it only inspects, it never changes anything.</summary>
public static class MoveValidator
{
    /// <summary>A country must keep one army, so only a country with at least this many can send armies out (R§6).</summary>
    internal const int MinArmiesToMove = 2;

    /// <summary>Returns the reason the attack is refused, or <c>null</c> when it is allowed.</summary>
    public static RuleViolation? ValidateAttack(GameState state, Attack command, MapDefinition map) =>
        CommandGuards.Check(state, command, GamePhase.Attack)
        ?? CheckEndpoints(state, map, command.Seat, command.From, command.To, destinationMustBeEnemy: true);

    /// <summary>Returns the reason the regroup is refused, or <c>null</c> when it is allowed.</summary>
    public static RuleViolation? ValidateRegroup(GameState state, Regroup command, MapDefinition map)
    {
        var violation = CommandGuards.Check(state, command, GamePhase.Regroup)
            ?? CheckEndpoints(state, map, command.Seat, command.From, command.To, destinationMustBeEnemy: false);
        if (violation is not null)
        {
            return violation;
        }

        if (command.Armies < 1)
        {
            return new RuleViolation(
                RuleViolations.InvalidArmyCount,
                $"A regroup moves at least one army, not {command.Armies}.");
        }

        var spare = state.FindTerritory(command.From)!.Armies - 1;
        if (command.Armies > spare)
        {
            return new RuleViolation(
                RuleViolations.NotEnoughArmies,
                $"{command.From} can send at most {spare} armies (one must stay), not {command.Armies}.");
        }

        return null;
    }

    /// <summary>
    /// Ownership, adjacency (by land or by bridge) and size of the origin: what attacks and regroups have in
    /// common. An attack needs an enemy destination, a regroup an own one.
    /// </summary>
    private static RuleViolation? CheckEndpoints(
        GameState state,
        MapDefinition map,
        int seat,
        string originId,
        string destinationId,
        bool destinationMustBeEnemy)
    {
        var origin = state.FindTerritory(originId);
        var destination = state.FindTerritory(destinationId);
        if (origin is null || destination is null)
        {
            var unknown = origin is null ? originId : destinationId;
            return new RuleViolation(RuleViolations.UnknownTerritory, $"The board has no country '{unknown}'.");
        }

        if (origin.Owner != seat)
        {
            return new RuleViolation(RuleViolations.OriginNotOwned, $"{originId} does not belong to seat {seat}.");
        }

        if (destinationMustBeEnemy && destination.Owner == seat)
        {
            return new RuleViolation(
                RuleViolations.DestinationNotEnemy,
                $"{destinationId} is your own country: an attack needs an enemy target.");
        }

        if (!destinationMustBeEnemy && destination.Owner != seat)
        {
            return new RuleViolation(
                RuleViolations.DestinationNotOwn,
                $"{destinationId} is not your country: a regroup needs an own destination.");
        }

        if (!map.AreAdjacent(originId, destinationId))
        {
            return new RuleViolation(
                RuleViolations.NotAdjacent,
                $"{originId} and {destinationId} do not touch, by land or by bridge.");
        }

        if (origin.Armies < MinArmiesToMove)
        {
            return new RuleViolation(
                RuleViolations.OriginTooFewArmies,
                $"{originId} has {origin.Armies} army: at least {MinArmiesToMove} are needed.");
        }

        return null;
    }
}
