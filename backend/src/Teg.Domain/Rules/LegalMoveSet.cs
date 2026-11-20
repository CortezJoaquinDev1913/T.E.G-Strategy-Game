using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.State;

namespace Teg.Domain.Rules;

/// <summary>
/// A pool of armies that can be placed, and where. <paramref name="ContinentId"/> is <c>null</c> for armies that may go on any
/// country of <paramref name="TerritoryIds"/> (the whole pool of initial placement, the free armies of Refuerzo); a continent
/// bonus (R§5.2) names its continent and only offers the participant's countries there. Territories are in map order.
/// </summary>
public sealed record PlacementPool(string? ContinentId, int Armies, ValueArray<string> TerritoryIds);

/// <summary>Three cards of the hand that can be traded (R§7), in hand order, and what the trade is worth.</summary>
public sealed record TradeOption(ValueArray<string> Cards, int Armies);

/// <summary>An attack that can be made: from the own country <paramref name="From"/> (two armies at least) to the adjacent enemy <paramref name="To"/> (R§6).</summary>
public sealed record AttackOption(string From, string To);

/// <summary>The armies the attacker may leave in the country just conquered, <paramref name="Min"/> to <paramref name="Max"/> in total (R§6).</summary>
public sealed record OccupationRange(int Min, int Max);

/// <summary>A regroup that can be made: from <paramref name="From"/> to <paramref name="To"/>, any number of armies from one to <paramref name="MaxArmies"/> (R§6).</summary>
public sealed record RegroupOption(string From, string To, int MaxArmies);

/// <summary>
/// What the ACTIVE participant can do in a state (A4, B1): descriptors, not every combination. It is built by
/// <see cref="LegalMoves.For"/>, is plain data with value equality, and holds nothing of any other participant (the trades
/// are made of the hand of <see cref="Seat"/> only). Which parts are filled depends on the <see cref="Phase"/>:
/// <list type="bullet">
/// <item>InitialPlacement: <see cref="Pools"/> holds the one pool of the round and <see cref="MustPlaceAll"/> is set.</item>
/// <item>Reinforcement: <see cref="Pools"/> are the pending free and continent armies (the free pool first, then the continents
/// in map order), <see cref="Trades"/> the trades that can be made now; <see cref="TradeMandatory"/> says a trade must come first,
/// and then <see cref="CanPlace"/> is <c>false</c>. The turn never ends from here.</item>
/// <item>Attack: <see cref="Attacks"/>, <see cref="CanStartRegroup"/> and <see cref="CanEndTurn"/>; while a conquest is pending
/// only <see cref="Occupation"/> is offered.</item>
/// <item>Regroup: <see cref="Regroups"/> and <see cref="CanEndTurn"/>.</item>
/// <item>Card and Finished: nothing.</item>
/// </list>
/// </summary>
/// <param name="Seat">The active participant: the descriptor is theirs.</param>
/// <param name="Phase">The phase of the state it describes.</param>
public sealed record LegalMoveSet(int Seat, GamePhase Phase)
{
    /// <summary><c>EndTurn</c> is accepted (Ataque and Reagrupe, with no occupation pending).</summary>
    public bool CanEndTurn { get; init; }

    /// <summary><c>StartRegroup</c> is accepted (Ataque, with no occupation pending).</summary>
    public bool CanStartRegroup { get; init; }

    /// <summary>
    /// A placement command (<see cref="PlaceInitialArmies"/> or <see cref="PlaceReinforcements"/>) can be accepted now. It is
    /// <c>false</c> when a trade is mandatory (R§7) or when no pool has a country to go to; the pools are still described.
    /// </summary>
    public bool CanPlace { get; init; }

    /// <summary>The placement must spend the whole pool in ONE command (initial placement); otherwise it may place a part of the pools.</summary>
    public bool MustPlaceAll { get; init; }

    /// <summary>Five or more cards in hand (R§7): a trade comes before any placement.</summary>
    public bool TradeMandatory { get; init; }

    /// <summary>The armies still to be placed and the countries each pool can go to (see <see cref="PlacementPool"/>). Empty when nothing is to be placed.</summary>
    public ValueArray<PlacementPool> Pools { get; init; } = ValueArray<PlacementPool>.Empty;

    /// <summary>Every valid triple of the hand of <see cref="Seat"/>, in hand order (Refuerzo).</summary>
    public ValueArray<TradeOption> Trades { get; init; } = ValueArray<TradeOption>.Empty;

    /// <summary>Every attack, in map order of the origin and then of the destination (Ataque, with no occupation pending).</summary>
    public ValueArray<AttackOption> Attacks { get; init; } = ValueArray<AttackOption>.Empty;

    /// <summary>The occupation that must be decided now, or <c>null</c> when no conquest is pending. It is then the only thing that can be done.</summary>
    public OccupationRange? Occupation { get; init; }

    /// <summary>Every regroup, in map order of the origin and then of the destination (Reagrupe).</summary>
    public ValueArray<RegroupOption> Regroups { get; init; } = ValueArray<RegroupOption>.Empty;

    /// <summary>Something can be done: false in a finished game and in the (decision-free) Card phase.</summary>
    public bool HasMoves =>
        CanEndTurn || CanStartRegroup || CanPlace || Trades.Count > 0 || Attacks.Count > 0 || Occupation is not null || Regroups.Count > 0;

    /// <summary>
    /// The commands that need no choice of distribution: every trade, attack, regroup count (1 to its maximum), occupation count,
    /// <c>StartRegroup</c> and <c>EndTurn</c>, plus, for the placements, each whole pool put on each of its countries (so the
    /// engine accepts every one of them). A placement is a combinatorial space: the bot chooses a distribution from
    /// <see cref="Pools"/> and builds it with <see cref="Place"/>.
    /// </summary>
    public IEnumerable<GameCommand> ToCommands()
    {
        if (Occupation is { } range)
        {
            for (var armies = range.Min; armies <= range.Max; armies++)
            {
                yield return new MoveAfterConquest(Seat, armies);
            }
        }

        foreach (var trade in Trades)
        {
            yield return new TradeCards(Seat, trade.Cards);
        }

        if (CanPlace)
        {
            // Two pools of the same size over the same countries (3 free armies and a bonus of 3) give the same command: once.
            var wholePools = Pools.SelectMany(pool => pool.TerritoryIds.Select(territory => Place([new ArmyPlacement(territory, pool.Armies)])));
            foreach (var command in wholePools.Distinct())
            {
                yield return command;
            }
        }

        foreach (var attack in Attacks)
        {
            yield return new Attack(Seat, attack.From, attack.To);
        }

        foreach (var regroup in Regroups)
        {
            for (var armies = 1; armies <= regroup.MaxArmies; armies++)
            {
                yield return new Regroup(Seat, regroup.From, regroup.To, armies);
            }
        }

        if (CanStartRegroup)
        {
            yield return new StartRegroup(Seat);
        }

        if (CanEndTurn)
        {
            yield return new EndTurn(Seat);
        }
    }

    /// <summary>
    /// The placement command of this phase for the distribution <paramref name="placements"/> (<see cref="PlaceInitialArmies"/> in
    /// initial placement, <see cref="PlaceReinforcements"/> in Refuerzo), for <see cref="Seat"/>. It does not check the
    /// distribution: <see cref="GameEngine.Apply"/> does.
    /// </summary>
    /// <exception cref="InvalidOperationException">The phase places no armies.</exception>
    public GameCommand Place(IEnumerable<ArmyPlacement> placements) => Phase switch
    {
        GamePhase.InitialPlacement => new PlaceInitialArmies(Seat, placements.ToValueArray()),
        GamePhase.Reinforcement => new PlaceReinforcements(Seat, placements.ToValueArray()),
        _ => throw new InvalidOperationException($"No armies are placed in the {Phase} phase."),
    };
}
