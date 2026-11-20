using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Map;
using Teg.Domain.State;
using static Teg.Domain.Tests.Support.GameFixtures;

namespace Teg.Domain.Tests.Support;

/// <summary>
/// Builders of the reinforcement and trade tests: two participants on the real board, round 2, where seat 0 is
/// about to end its turn and seat 1 owns EXACTLY the countries the test names (every country has 1 army). Seat 1's
/// Reinforcement phase is reached the way the engine reaches it, with <see cref="ReinforcementOfSeatOne"/>.
/// </summary>
internal static class ReinforcementFixtures
{
    /// <summary>The countries of a continent of the real board, in map order.</summary>
    public static string[] ContinentCountries(string continentId) =>
        RealMap.Continents.Single(c => c.Id == continentId).TerritoryIds.ToArray();

    /// <summary>
    /// <paramref name="count"/> (at most 25) countries that NEVER complete a continent: every other country of the
    /// map order, so that only the countries part of the reinforcements is exercised.
    /// </summary>
    public static string[] SpreadCountries(int count) =>
        RealMap.Territories.Where((_, index) => index % 2 == 0).Take(count).Select(t => t.Id).ToArray();

    /// <summary>Seat 0 acts (Ataque, round 2) and owns all but <paramref name="ownedBySeatOne"/>; seat 1 comes next.</summary>
    public static GameState SeatOneTurnComesNext(IEnumerable<string> ownedBySeatOne)
    {
        var mine = ownedBySeatOne.ToHashSet();
        Assert.Empty(mine.Except(RealMap.Territories.Select(t => t.Id))); // a typo must not silently shrink the set

        var state = InPlay(2, GamePhase.Attack, round: 2, activeSeat: 0);
        var seatZeroOwnsNothing = mine.Count == RealMap.Territories.Count;

        return state with
        {
            Territories = RealMap.Territories
                .Select(t => new TerritoryState(t.Id, mine.Contains(t.Id) ? 1 : 0, Armies: 1))
                .ToValueArray(),
            Participants = state.Participants
                .Select(p => p.Seat == 0 && seatZeroOwnsNothing ? p with { Eliminated = true } : p)
                .ToValueArray(),
        };
    }

    /// <summary>Seat 1 starts the Reinforcement phase of round 2: seat 0 ends its turn and the engine computes the pool.</summary>
    public static GameState ReinforcementOfSeatOne(IEnumerable<string> ownedBySeatOne) =>
        ApplyOk(SeatOneTurnComesNext(ownedBySeatOne), new EndTurn(0)).State;

    public static GameState WithHand(GameState state, int seat, params string[] cards) =>
        state with
        {
            Participants = state.Participants
                .Select(p => p.Seat == seat ? p with { Cards = cards.ToValueArray() } : p)
                .ToValueArray(),
        };

    public static GameState WithTrades(GameState state, int seat, int tradesDone) =>
        state with
        {
            Participants = state.Participants
                .Select(p => p.Seat == seat ? p with { TradesDone = tradesDone } : p)
                .ToValueArray(),
        };

    /// <summary>
    /// <paramref name="count"/> cards (country ids) with this symbol, in map order, after skipping
    /// <paramref name="skip"/> of them (to build several disjoint hands).
    /// </summary>
    public static string[] CardsOf(CardSymbol symbol, int count, int skip = 0)
    {
        var cards = RealMap.Territories
            .Where(t => t.Symbol == symbol)
            .Skip(skip)
            .Take(count)
            .Select(t => t.Id)
            .ToArray();
        Assert.Equal(count, cards.Length);

        return cards;
    }

    /// <summary>One card of each symbol (the number <paramref name="skip"/> of each kind in map order).</summary>
    public static string[] OneOfEach(int skip = 0) =>
        [CardsOf(CardSymbol.Galeon, 1, skip)[0], CardsOf(CardSymbol.Globo, 1, skip)[0], CardsOf(CardSymbol.Canon, 1, skip)[0]];
}
