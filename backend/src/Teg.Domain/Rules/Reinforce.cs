using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Events;
using Teg.Domain.Map;
using Teg.Domain.State;

namespace Teg.Domain.Rules;

/// <summary>
/// Reinforcements (R§5). The pool is computed once, when the turn starts: armies for the countries plus the bonus
/// of every continent owned in full at that moment. Placing it can be done in several commands; the continent
/// armies only go to their continent, the rest anywhere. The phase ends by itself when the pool is empty.
/// </summary>
internal static class Reinforce
{
    /// <summary>R§5.1: the countries give at least this many armies...</summary>
    private const int MinCountryArmies = 3;

    /// <summary>...and otherwise one army for every this many countries, rounded down.</summary>
    private const int CountriesPerArmy = 2;

    /// <summary>The pool of <paramref name="seat"/>'s turn, computed from the board as it is NOW (the start of the turn).</summary>
    internal static TurnContext ComputePool(GameState state, int seat, MapDefinition map)
    {
        var owned = state.Territories.Where(t => t.Owner == seat).Select(t => t.Id).ToHashSet();
        var continentBonuses = map.Continents
            .Where(continent => continent.TerritoryIds.All(owned.Contains))
            .Select(continent => new ContinentPool(continent.Id, continent.Bonus))
            .ToValueArray();

        return new TurnContext(Math.Max(MinCountryArmies, owned.Count / CountriesPerArmy), continentBonuses);
    }

    internal static Result<Transition, RuleViolation> Place(GameState state, PlaceReinforcements command, MapDefinition map)
    {
        var violation = CommandGuards.Check(state, command, GamePhase.Reinforcement)
            ?? RequireNoPendingTrade(state, command.Seat)
            ?? ValidatePlacements(state, command);
        if (violation is not null)
        {
            return Result<Transition, RuleViolation>.Fail(violation);
        }

        var spent = Spend(state.Turn, command.Placements, map);
        if (!spent.IsSuccess)
        {
            return Result<Transition, RuleViolation>.Fail(spent.Error);
        }

        var remaining = spent.Value;
        var placed = state with
        {
            Territories = ArmyPlacements.AddTo(state.Territories, command.Placements),
            Turn = remaining,
        };
        var events = new List<GameEvent> { new ReinforcementsPlaced(command.Seat, command.Placements) };

        if (remaining.PendingArmies > 0)
        {
            return Result<Transition, RuleViolation>.Ok(new Transition(placed, events.ToValueArray()));
        }

        events.Add(new PhaseChanged(command.Seat, GamePhase.Reinforcement, GamePhase.Attack));

        return Result<Transition, RuleViolation>.Ok(new Transition(
            placed with { Phase = GamePhase.Attack },
            events.ToValueArray()));
    }

    /// <summary>R§7: with five or more cards a trade comes first, so nothing can be placed until fewer than five remain.</summary>
    private static RuleViolation? RequireNoPendingTrade(GameState state, int seat) =>
        Trade.IsMandatory(state.Participants[seat])
            ? new RuleViolation(
                RuleViolations.TradeRequired,
                $"With {Trade.MandatoryFrom} or more cards a trade is required before placing reinforcements.")
            : null;

    /// <summary>Unlike initial placement a command may place only part of the pool, but it must place something.</summary>
    private static RuleViolation? ValidatePlacements(GameState state, PlaceReinforcements command) =>
        command.Placements.Count == 0
            ? new RuleViolation(RuleViolations.InvalidArmyCount, "Place at least one army.")
            : ArmyPlacements.ValidateEntries(state, command.Seat, command.Placements);

    /// <summary>
    /// Takes the placed armies out of the pool. Armies placed in a continent that has a bonus use that bonus first
    /// (it is the more restricted kind, so using it first never hurts); whatever is left must come from the free
    /// armies, which can go anywhere.
    /// </summary>
    private static Result<TurnContext, RuleViolation> Spend(
        TurnContext pool,
        ValueArray<ArmyPlacement> placements,
        MapDefinition map)
    {
        var total = placements.Sum(p => (long)p.Armies); // long: absurd counts must be refused, not overflow
        if (total > pool.PendingArmies)
        {
            return Result<TurnContext, RuleViolation>.Fail(new RuleViolation(
                RuleViolations.ReinforcementsExceeded,
                $"{pool.PendingArmies} reinforcements are left to place, not {total}."));
        }

        var placedByContinent = placements
            .GroupBy(p => map.Territory(p.TerritoryId).ContinentId)
            .ToDictionary(group => group.Key, group => group.Sum(p => p.Armies));

        var fromFree = (int)total;
        var remainingBonuses = new List<ContinentPool>();
        foreach (var bonus in pool.ContinentPools)
        {
            var used = Math.Min(placedByContinent.GetValueOrDefault(bonus.ContinentId), bonus.Armies);
            fromFree -= used;
            if (used < bonus.Armies)
            {
                remainingBonuses.Add(bonus with { Armies = bonus.Armies - used });
            }
        }

        if (fromFree > pool.FreeArmies)
        {
            return Result<TurnContext, RuleViolation>.Fail(new RuleViolation(
                RuleViolations.ContinentBonusMisplaced,
                "Continent bonus armies can only go to the countries of their continent."));
        }

        return Result<TurnContext, RuleViolation>.Ok(
            new TurnContext(pool.FreeArmies - fromFree, remainingBonuses.ToValueArray()));
    }
}
