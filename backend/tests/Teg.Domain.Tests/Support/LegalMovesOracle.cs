using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Events;
using Teg.Domain.Map;
using Teg.Domain.Rng;
using Teg.Domain.Rules;
using Teg.Domain.Setup;
using Teg.Domain.State;
using static Teg.Domain.Tests.Support.GameFixtures;

namespace Teg.Domain.Tests.Support;

/// <summary>
/// The yardstick of the legal-moves tests (A4, B1), written WITHOUT the code under test:
/// <list type="bullet">
/// <item><see cref="Universe"/>: a finite, systematic list of candidate commands for the active participant of a state (every
/// attack between any two countries, every occupation count 0 to 5, every regroup with counts 0 to armies + 1, every trade of
/// three hand cards, EndTurn, StartRegroup, one army on every country, whole pools on every country, random multi-entry
/// placements...). The engine is asked about each of them.</item>
/// <item><see cref="Covers"/>: whether the descriptor, read the way a client would read it (and nothing else), allows a command.</item>
/// <item><see cref="AssertAgrees"/>: both answers must be the same for EVERY command of the universe (sound: nothing the
/// descriptor allows is refused; complete: nothing the engine accepts is missing), and the commands the descriptor expands to
/// must all be accepted and, for the finite kinds, be exactly the accepted ones.</item>
/// </list>
/// </summary>
internal static class LegalMovesOracle
{
    /// <summary>Occupation counts 0 to this one: more than the 1 to 3 of the rule, so the limits are crossed on both sides.</summary>
    private const int OccupationCandidates = 5;

    private const int RandomPlacementsPerState = 24;
    private const int MaxRandomParts = 3;

    /// <summary>How big the universe of a state was, how many of its commands the engine accepted, how many the descriptor expanded to.</summary>
    internal sealed record Agreement(int Universe, int Accepted, int Expanded);

    // ---- the descriptor, read from the outside --------------------------------------------------------------------

    /// <summary>Does the descriptor allow <paramref name="command"/>? Only what the descriptor says is used.</summary>
    public static bool Covers(LegalMoveSet set, GameCommand command)
    {
        if (command.Seat != set.Seat)
        {
            return false;
        }

        return command switch
        {
            EndTurn => set.CanEndTurn,
            StartRegroup => set.CanStartRegroup,
            Attack attack => set.Attacks.Any(option => option.From == attack.From && option.To == attack.To),
            MoveAfterConquest occupy => set.Occupation is { } range && occupy.Armies >= range.Min && occupy.Armies <= range.Max,
            Regroup regroup => regroup.Armies >= 1
                && set.Regroups.Any(option => option.From == regroup.From && option.To == regroup.To && regroup.Armies <= option.MaxArmies),
            TradeCards trade => set.Trades.Any(option => option.Cards.Order().SequenceEqual(trade.Cards.Order())),
            PlaceInitialArmies place => set.Phase == GamePhase.InitialPlacement && set.CanPlace && PlacesTheWholePool(set, place.Placements),
            PlaceReinforcements place => set.Phase == GamePhase.Reinforcement && set.CanPlace && PlacesWithinThePools(set, place.Placements),
            _ => throw new ArgumentException($"Unknown command {command.GetType().Name}.", nameof(command)),
        };
    }

    private static bool PlacesTheWholePool(LegalMoveSet set, IReadOnlyList<ArmyPlacement> placements)
    {
        var pool = set.Pools.Single();

        return placements.Count > 0
            && placements.All(p => p.Armies >= 1 && pool.TerritoryIds.Contains(p.TerritoryId))
            && placements.Sum(p => (long)p.Armies) == pool.Armies;
    }

    /// <summary>
    /// Several commands may place part of the pools: every entry on a country some pool offers, at least one army in all, no more
    /// than the pools hold, and the continent armies only absorbed by their own continent (the rest must come from the free pool).
    /// </summary>
    private static bool PlacesWithinThePools(LegalMoveSet set, IReadOnlyList<ArmyPlacement> placements)
    {
        if (placements.Count == 0 || placements.Any(p => p.Armies < 1))
        {
            return false;
        }

        var offered = set.Pools.SelectMany(pool => pool.TerritoryIds).ToHashSet();
        if (placements.Any(p => !offered.Contains(p.TerritoryId)))
        {
            return false;
        }

        var free = set.Pools.Where(pool => pool.ContinentId is null).Sum(pool => (long)pool.Armies);
        var continents = set.Pools.Where(pool => pool.ContinentId is not null).ToArray();
        var total = placements.Sum(p => (long)p.Armies);
        var absorbed = continents.Sum(pool =>
            Math.Min(placements.Where(p => pool.TerritoryIds.Contains(p.TerritoryId)).Sum(p => (long)p.Armies), pool.Armies));

        return total <= free + continents.Sum(pool => (long)pool.Armies) && total - absorbed <= free;
    }

    // ---- the agreement ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The descriptor of the active participant and the engine give the same answer for every command of the
    /// <see cref="Universe"/>, and every command the descriptor expands to is accepted by the engine. For the finite kinds (not
    /// the placements, which are a combinatorial space left as a descriptor) the expansion is exactly the accepted commands.
    /// </summary>
    public static Agreement AssertAgrees(GameState state, MapDefinition map, string context)
    {
        var set = LegalMoves.For(state, map);
        Assert.Equal((state.ActiveSeat, state.Phase), (set.Seat, set.Phase));

        var universe = Universe(state, map);
        var accepted = new List<GameCommand>();
        foreach (var command in universe)
        {
            var isAccepted = GameEngine.Apply(state, command, map).IsSuccess;
            var isCovered = Covers(set, command);

            Assert.True(
                isAccepted == isCovered,
                $"{context}: the engine {(isAccepted ? "accepts" : "refuses")} {Describe(command)} but the descriptor {(isCovered ? "allows" : "does not allow")} it.");
            if (isAccepted)
            {
                accepted.Add(command);
            }
        }

        var expanded = set.ToCommands().ToList();
        Assert.Equal(expanded.Count, expanded.Distinct().Count());
        foreach (var command in expanded)
        {
            Assert.True(GameEngine.Apply(state, command, map).IsSuccess, $"{context}: the expansion {Describe(command)} is refused by the engine.");
            Assert.True(Covers(set, command), $"{context}: the expansion {Describe(command)} is not covered by its own descriptor.");
        }

        // What the descriptor says a trade is worth is what the engine pays.
        foreach (var trade in set.Trades)
        {
            var paid = GameEngine.Apply(state, new TradeCards(set.Seat, trade.Cards), map).Value.Events.OfType<CardsTraded>().Single();
            Assert.True(trade.Armies == paid.Armies, $"{context}: the trade of {string.Join(",", trade.Cards)} is described as {trade.Armies} armies but the engine pays {paid.Armies}.");
        }

        var acceptedFinite = Finite(accepted);
        var expandedFinite = Finite(expanded);
        Assert.True(
            acceptedFinite.SetEquals(expandedFinite),
            $"{context}: {acceptedFinite.Count} finite commands accepted, {expandedFinite.Count} expanded; " +
            $"missing from the expansion: [{string.Join("; ", acceptedFinite.Except(expandedFinite).Take(5).Select(Describe))}], " +
            $"not accepted: [{string.Join("; ", expandedFinite.Except(acceptedFinite).Take(5).Select(Describe))}].");
        Assert.Equal(accepted.Count > 0, set.HasMoves);

        return new Agreement(universe.Count, accepted.Count, expanded.Count);
    }

    private static HashSet<GameCommand> Finite(IEnumerable<GameCommand> commands) =>
        commands.Where(command => command is not PlaceInitialArmies and not PlaceReinforcements).ToHashSet();

    // ---- the universe ----------------------------------------------------------------------------------------------

    /// <summary>The finite candidate commands of the active participant of <paramref name="state"/> (see the type summary).</summary>
    public static List<GameCommand> Universe(GameState state, MapDefinition map)
    {
        var seat = state.ActiveSeat;
        var everyCountry = map.Territories.Select(t => t.Id).ToArray();
        var owned = OwnedBy(state, seat);
        var commands = new List<GameCommand>
        {
            new EndTurn(seat),
            new EndTurn((seat + 1) % state.Participants.Count), // somebody else: never accepted
            new StartRegroup(seat),
        };
        commands.AddRange(Enumerable.Range(0, OccupationCandidates + 1).Select(armies => new MoveAfterConquest(seat, armies)));

        var hand = state.Participants[seat].Cards.Select(card => card.TerritoryId).ToArray();
        commands.AddRange(ThreeCards(hand).Select(cards => new TradeCards(seat, cards.ToValueArray())));
        commands.Add(new TradeCards(seat, hand.Take(2).ToValueArray())); // two cards: never a trade

        AddPlacements(commands, state, map, everyCountry, owned);
        AddAttacks(commands, state, map, everyCountry, owned);
        AddRegroups(commands, state, map, everyCountry, owned);

        return commands;
    }

    private static IEnumerable<string[]> ThreeCards(string[] hand)
    {
        for (var first = 0; first < hand.Length; first++)
        {
            for (var second = first + 1; second < hand.Length; second++)
            {
                for (var third = second + 1; third < hand.Length; third++)
                {
                    yield return [hand[first], hand[second], hand[third]];
                }
            }
        }
    }

    private static ValueArray<ArmyPlacement> Entry(string territory, int armies) =>
        new[] { new ArmyPlacement(territory, armies) }.ToValueArray();

    private static void AddPlacements(
        List<GameCommand> commands,
        GameState state,
        MapDefinition map,
        string[] everyCountry,
        string[] owned)
    {
        var seat = state.ActiveSeat;
        var picks = new SeededRng(unchecked(state.Version * 7919L + state.Rng.Draws));

        // One army on every country (owned or not), as both kinds of placement: the phase decides which one can be right.
        foreach (var id in everyCountry)
        {
            commands.Add(new PlaceInitialArmies(seat, Entry(id, 1)));
            commands.Add(new PlaceReinforcements(seat, Entry(id, 1)));
        }

        if (state.Phase is not (GamePhase.InitialPlacement or GamePhase.Reinforcement))
        {
            return;
        }

        var bonusCountries = state.Turn.ContinentPools
            .SelectMany(pool => map.Continents.Where(c => c.Id == pool.ContinentId).SelectMany(c => c.TerritoryIds))
            .ToArray();

        string Country() => picks.NextInt(10) switch
        {
            < 5 when owned.Length > 0 => owned[picks.NextInt(owned.Length)],
            < 8 when bonusCountries.Length > 0 => bonusCountries[picks.NextInt(bonusCountries.Length)],
            _ => everyCountry[picks.NextInt(everyCountry.Length)],
        };

        var initial = state.Phase == GamePhase.InitialPlacement;
        var pending = initial
            ? GameSetup.PlacementPools(state.Participants.Count)[state.PlacementRound - 1]
            : state.Turn.PendingArmies;
        var amounts = initial
            ? new[] { pending - 1, pending, pending + 1 }
            : new[] { state.Turn.FreeArmies, pending, pending + 1 }.Concat(state.Turn.ContinentPools.Select(pool => pool.Armies)).ToArray();

        // A whole amount on a single country: the pool, the free armies, each continent bonus, one too many.
        foreach (var amount in amounts.Where(amount => amount > 1).Distinct())
        {
            foreach (var id in everyCountry)
            {
                commands.Add(initial
                    ? new PlaceInitialArmies(seat, Entry(id, amount))
                    : new PlaceReinforcements(seat, Entry(id, amount)));
            }
        }

        // The combinatorial space: random splits over one to three countries, half of them with the exact total.
        for (var attempt = 0; attempt < RandomPlacementsPerState; attempt++)
        {
            var total = attempt % 2 == 0 ? pending : 1 + picks.NextInt(pending + 1);
            if (total < 1)
            {
                continue;
            }

            var placements = Compose(picks, Country, total);
            commands.Add(initial ? new PlaceInitialArmies(seat, placements) : new PlaceReinforcements(seat, placements));
        }
    }

    /// <summary><paramref name="total"/> armies split in one to three entries (each of at least one army) on countries of <paramref name="country"/>.</summary>
    private static ValueArray<ArmyPlacement> Compose(SeededRng picks, Func<string> country, int total)
    {
        var parts = 1 + picks.NextInt(Math.Min(MaxRandomParts, total));
        var left = total;
        var entries = new List<ArmyPlacement>();

        for (var part = 0; part < parts; part++)
        {
            var armies = part == parts - 1 ? left : 1 + picks.NextInt(left - (parts - part - 1));
            entries.Add(new ArmyPlacement(country(), armies));
            left -= armies;
        }

        return entries.ToValueArray();
    }

    private static void AddAttacks(
        List<GameCommand> commands,
        GameState state,
        MapDefinition map,
        string[] everyCountry,
        string[] owned)
    {
        var seat = state.ActiveSeat;
        if (state.Phase == GamePhase.Attack)
        {
            // Any two countries, any owners, any armies: the engine decides which of them is an attack.
            foreach (var from in everyCountry)
            {
                foreach (var to in everyCountry)
                {
                    commands.Add(new Attack(seat, from, to));
                }
            }

            return;
        }

        // Elsewhere the phase guard is the thing being probed: a few real attacks of each owned country are enough.
        foreach (var from in owned.Take(2))
        {
            commands.AddRange(everyCountry.Where(to => map.AreAdjacent(from, to)).Select(to => new Attack(seat, from, to)));
        }
    }

    private static void AddRegroups(
        List<GameCommand> commands,
        GameState state,
        MapDefinition map,
        string[] everyCountry,
        string[] owned)
    {
        var seat = state.ActiveSeat;
        var ownedSet = owned.ToHashSet();

        if (state.Phase != GamePhase.Regroup)
        {
            foreach (var from in owned.Take(2))
            {
                commands.AddRange(everyCountry.Where(to => map.AreAdjacent(from, to)).Select(to => new Regroup(seat, from, to, 1)));
            }

            return;
        }

        foreach (var from in everyCountry)
        {
            // Counts 0 to armies + 1 for the participant's own countries: every count the engine could accept and the first it must refuse.
            var counts = ownedSet.Contains(from) ? Enumerable.Range(0, state.FindTerritory(from)!.Armies + 2) : new[] { 1 };
            var neighbours = everyCountry.Where(to => map.AreAdjacent(from, to)).ToArray();

            foreach (var to in neighbours)
            {
                commands.AddRange(counts.Select(armies => new Regroup(seat, from, to, armies)));
            }

            // And one country that does not touch it (the phase must refuse it whatever the count).
            var stranger = everyCountry.FirstOrDefault(to => to != from && !neighbours.Contains(to));
            if (stranger is not null && ownedSet.Contains(from))
            {
                commands.Add(new Regroup(seat, from, stranger, 1));
            }
        }
    }

    // ---- boards and messages --------------------------------------------------------------------------------------

    /// <summary>
    /// A game on the real board in <paramref name="phase"/> where a random seat acts and owns about 60% of the countries; every
    /// other seat owns at least one; every country holds 1 to 9 armies. Everything is decided by <paramref name="seed"/>.
    /// </summary>
    public static GameState RandomBoard(int participants, long seed, GamePhase phase)
    {
        var rng = new SeededRng(seed);
        var active = rng.NextInt(participants);
        var template = InPlay(participants, phase, round: 2, activeSeat: active);

        return template with
        {
            Territories = template.Territories
                .Select((t, index) => new TerritoryState(
                    t.Id,
                    Owner: index < participants ? index : rng.NextInt(10) < 6 ? active : rng.NextInt(participants),
                    Armies: 1 + rng.NextInt(9)))
                .ToValueArray(),
        };
    }

    public static string Describe(GameCommand command) => command switch
    {
        PlaceInitialArmies place => $"PlaceInitialArmies(seat {place.Seat}: {Entries(place.Placements)})",
        PlaceReinforcements place => $"PlaceReinforcements(seat {place.Seat}: {Entries(place.Placements)})",
        TradeCards trade => $"TradeCards(seat {trade.Seat}: {string.Join(",", trade.Cards)})",
        _ => command.ToString() ?? command.GetType().Name,
    };

    private static string Entries(IEnumerable<ArmyPlacement> placements) =>
        string.Join(" ", placements.Select(p => $"{p.TerritoryId}x{p.Armies}"));
}
