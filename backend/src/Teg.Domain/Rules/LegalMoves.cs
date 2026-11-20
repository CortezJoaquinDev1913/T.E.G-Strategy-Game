using Teg.Domain.Collections;
using Teg.Domain.Map;
using Teg.Domain.Setup;
using Teg.Domain.State;

namespace Teg.Domain.Rules;

/// <summary>
/// The legal-moves query (A4, B1): what the ACTIVE participant may do, as descriptors (<see cref="LegalMoveSet"/>). It is rule
/// logic, so it lives in the Domain, and it is pure. It restates no rule of its own where the engine already has one (the
/// trade combinations, the minimum armies to move, the occupation limit are the engine's); what it adds is the enumeration, and
/// the tests hold it to the engine in both directions: nothing it offers is refused, and nothing the engine accepts is missing.
/// Everything is listed in map order, so the result is deterministic.
/// </summary>
public static class LegalMoves
{
    /// <summary>
    /// The moves of the active participant of <paramref name="state"/>. A finished game, and the Card phase (which has no
    /// decision: <c>EndTurn</c> resolves it), offer nothing; while a conquest is pending only its occupation is offered.
    /// </summary>
    public static LegalMoveSet For(GameState state, MapDefinition map)
    {
        var none = new LegalMoveSet(state.ActiveSeat, state.Phase);

        if (state.Phase is GamePhase.Finished or GamePhase.Card)
        {
            return none;
        }

        if (state.Turn.PendingConquest is { } pending)
        {
            // Nothing else is accepted until the attacker decides, and the occupation itself only belongs to Ataque.
            return state.Phase == GamePhase.Attack ? none with { Occupation = OccupationOf(state, pending) } : none;
        }

        return state.Phase switch
        {
            GamePhase.InitialPlacement => InitialPlacementMoves(state, none),
            GamePhase.Reinforcement => ReinforcementMoves(state, map, none),
            GamePhase.Attack => none with { Attacks = AttacksOf(state, map), CanStartRegroup = true, CanEndTurn = true },
            GamePhase.Regroup => none with { Regroups = RegroupsOf(state, map), CanEndTurn = true },
            _ => none,
        };
    }

    /// <summary>The countries of the active participant, in map order.</summary>
    private static ValueArray<string> OwnedBy(GameState state) =>
        state.Territories.Where(t => t.Owner == state.ActiveSeat).Select(t => t.Id).ToValueArray();

    /// <summary>R§3: the whole pool of the round, on any own country, in one command.</summary>
    private static LegalMoveSet InitialPlacementMoves(GameState state, LegalMoveSet none)
    {
        var owned = OwnedBy(state);
        var pool = GameSetup.PlacementPools(state.Participants.Count)[state.PlacementRound - 1];

        return none with
        {
            CanPlace = owned.Count > 0,
            MustPlaceAll = true,
            Pools = new[] { new PlacementPool(ContinentId: null, pool, owned) }.ToValueArray(),
        };
    }

    /// <summary>
    /// R§5, R§7: the pending pools, the trades that can be made, and whether a trade must come first. The turn does not end
    /// from here (the phase closes itself when the last army is placed), so <c>CanEndTurn</c> stays <c>false</c>.
    /// </summary>
    private static LegalMoveSet ReinforcementMoves(GameState state, MapDefinition map, LegalMoveSet none)
    {
        var participant = state.Participants[state.ActiveSeat];
        var pools = PoolsOf(state, map);
        var tradeMandatory = Trade.IsMandatory(participant);

        return none with
        {
            Pools = pools,
            CanPlace = !tradeMandatory && pools.Any(pool => pool.TerritoryIds.Count > 0),
            TradeMandatory = tradeMandatory,
            Trades = TradesOf(participant, map),
        };
    }

    /// <summary>The free armies (any own country) and then each continent bonus (only the own countries of that continent), skipping the spent ones.</summary>
    private static ValueArray<PlacementPool> PoolsOf(GameState state, MapDefinition map)
    {
        var owned = OwnedBy(state);
        var pools = new List<PlacementPool>();

        if (state.Turn.FreeArmies > 0)
        {
            pools.Add(new PlacementPool(ContinentId: null, state.Turn.FreeArmies, owned));
        }

        foreach (var bonus in state.Turn.ContinentPools.Where(bonus => bonus.Armies > 0))
        {
            var inTheContinent = owned.Where(id => map.Territory(id).ContinentId == bonus.ContinentId).ToValueArray();
            pools.Add(new PlacementPool(bonus.ContinentId, bonus.Armies, inTheContinent));
        }

        return pools.ToValueArray();
    }

    /// <summary>Every triple of the hand (in hand order) that is a valid trade, each worth the next trade value of the participant.</summary>
    private static ValueArray<TradeOption> TradesOf(Participant participant, MapDefinition map)
    {
        var hand = participant.Cards.Select(card => card.TerritoryId).ToArray();
        var armies = Trade.ValueOf(participant.TradesDone + 1);
        var trades = new List<TradeOption>();

        for (var first = 0; first < hand.Length; first++)
        {
            for (var second = first + 1; second < hand.Length; second++)
            {
                for (var third = second + 1; third < hand.Length; third++)
                {
                    string[] cards = [hand[first], hand[second], hand[third]];
                    if (Trade.IsValidCombination(cards, map))
                    {
                        trades.Add(new TradeOption(cards.ToValueArray(), armies));
                    }
                }
            }
        }

        return trades.ToValueArray();
    }

    /// <summary>R§6: from an own country with at least two armies to an adjacent country (land or bridge) of anybody else.</summary>
    private static ValueArray<AttackOption> AttacksOf(GameState state, MapDefinition map)
    {
        var seat = state.ActiveSeat;
        var enemies = state.Territories.Where(t => t.Owner != seat).ToArray();

        return state.Territories
            .Where(origin => origin.Owner == seat && origin.Armies >= MoveValidator.MinArmiesToMove)
            .SelectMany(origin => enemies
                .Where(target => map.AreAdjacent(origin.Id, target.Id))
                .Select(target => new AttackOption(origin.Id, target.Id)))
            .ToValueArray();
    }

    /// <summary>
    /// R§6: from an own country to an adjacent own one, up to what the origin may still send: its quota of this phase, and never
    /// more than it can spare (one army stays). What a country received in this phase is not in its quota, so it cannot move on.
    /// </summary>
    private static ValueArray<RegroupOption> RegroupsOf(GameState state, MapDefinition map)
    {
        var seat = state.ActiveSeat;
        var own = state.Territories.Where(t => t.Owner == seat).ToArray();

        return own
            .Select(origin => (Origin: origin, Most: Math.Min(state.Turn.RegroupQuotaOf(origin.Id), origin.Armies - 1)))
            .Where(source => source.Most >= 1)
            .SelectMany(source => own
                .Where(target => map.AreAdjacent(source.Origin.Id, target.Id))
                .Select(target => new RegroupOption(source.Origin.Id, target.Id, source.Most)))
            .ToValueArray();
    }

    /// <summary>
    /// R§6: the conquering army is already in the conquered country, so what the origin had before the attack is what the two
    /// hold together; at most three stay, and the origin keeps one.
    /// </summary>
    private static OccupationRange OccupationOf(GameState state, PendingConquest pending)
    {
        var armiesBeforeTheAttack = state.FindTerritory(pending.From)!.Armies + state.FindTerritory(pending.To)!.Armies;

        return new OccupationRange(Conquest.MinOccupation, Conquest.MaxOccupation(armiesBeforeTheAttack));
    }
}
