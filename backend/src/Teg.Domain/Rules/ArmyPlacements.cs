using Teg.Domain.Collections;
using Teg.Domain.State;

namespace Teg.Domain.Rules;

/// <summary>What initial placement and reinforcements share: checking the entries and putting the armies on the board.</summary>
internal static class ArmyPlacements
{
    /// <summary>
    /// Every entry names a country of the board that <paramref name="seat"/> owns, with at least one army. The first
    /// problem found is returned; <c>null</c> means the entries are fine (the pool is the caller's business).
    /// </summary>
    internal static RuleViolation? ValidateEntries(GameState state, int seat, ValueArray<ArmyPlacement> placements)
    {
        foreach (var placement in placements)
        {
            var territory = state.FindTerritory(placement.TerritoryId);
            if (territory is null)
            {
                return new RuleViolation(
                    RuleViolations.UnknownTerritory,
                    $"The board has no country '{placement.TerritoryId}'.");
            }

            if (territory.Owner != seat)
            {
                return new RuleViolation(
                    RuleViolations.TerritoryNotOwned,
                    $"{placement.TerritoryId} does not belong to seat {seat}.");
            }
        }

        if (placements.Any(p => p.Armies < 1))
        {
            return new RuleViolation(RuleViolations.InvalidArmyCount, "Every placement needs at least one army.");
        }

        return null;
    }

    /// <summary>The board with the armies added; several entries for one country are summed.</summary>
    internal static ValueArray<TerritoryState> AddTo(
        ValueArray<TerritoryState> territories,
        ValueArray<ArmyPlacement> placements)
    {
        var added = placements
            .GroupBy(p => p.TerritoryId)
            .ToDictionary(group => group.Key, group => group.Sum(p => p.Armies));

        return territories
            .Select(t => added.TryGetValue(t.Id, out var armies) ? t with { Armies = t.Armies + armies } : t)
            .ToValueArray();
    }
}
