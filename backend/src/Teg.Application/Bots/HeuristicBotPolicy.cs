using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Map;
using Teg.Domain.Rules;
using Teg.Domain.State;

namespace Teg.Application.Bots;

/// <summary>
/// The bot of v1 (B1-B3): a cheap, deterministic heuristic that picks ONE command per call from what <see cref="LegalMoves.For"/>
/// offers. A bot thinks only about the board in front of it; ties are broken by map order, so the same state always gives the same
/// command (nothing here is random and nothing is kept between calls).
/// <list type="number">
/// <item><b>Trades</b> whenever a trade is possible (a mandatory one comes first by the rules), giving up the triple that loses the
/// fewest country premiums: a card of a country the bot owns whose +2 is still unpaid is worth keeping.</item>
/// <item><b>Places</b> every pool on the FRONTIER country (one with an enemy neighbour) with the best advantage: its armies minus those
/// of its weakest enemy neighbour; an interior country is only chosen when there is no frontier one. Initial placement works the same
/// way. Each pool goes to one country (continent armies inside their continent), so a pool is one move of the bot.</item>
/// <item><b>Attacks</b> only when the attacker has at least two more armies than the defender; it prefers the attack that completes a
/// continent, then the weakest defender, then the biggest stack, then map order.</item>
/// <item><b>Occupies</b> with the maximum when the conquered country borders an enemy and with the minimum when it is interior.</item>
/// <item><b>Regroups</b> only to bring an interior stack to a border (the biggest first, to the border with the best advantage), and
/// ends the turn when there is no favourable attack and nothing worth moving.</item>
/// </list>
/// <para>
/// THE CAP (B2). The bot counts the actions of its turn in the saved state (<c>Turn.BotActions</c>, written by <c>SubmitAction</c>). Once
/// <paramref name="maxActionsPerTurn"/> of them are done it only WINDS DOWN, with the fewest steps the rules leave: a pending
/// occupation with the minimum, a mandatory trade (nothing can be placed before it), every pending pool on its first country in a single
/// placement, and then <c>EndTurn</c> (from Ataque or Reagrupe; it is never legal in Refuerzo, so the placement comes first). A turn
/// therefore never goes beyond the cap by more than those closing steps. Initial placement is one command per turn and is not counted.
/// </para>
/// </summary>
public sealed class HeuristicBotPolicy : IBotPolicy
{
    /// <summary>The design's cap of bot actions per turn.</summary>
    public const int DefaultMaxActionsPerTurn = 60;

    /// <summary>The bot attacks when its origin has at least this many armies more than the target (R§6: the origin keeps one, rolls up to three dice).</summary>
    private const int MinimumAttackAdvantage = 2;

    /// <summary>An army must stay behind in the origin, so a stack must have at least this many armies to be worth moving out of an interior country.</summary>
    private const int MinimumStackToMove = 2;

    private readonly MapDefinition _map;
    private readonly int _maxActionsPerTurn;

    public HeuristicBotPolicy(MapDefinition map, int maxActionsPerTurn = DefaultMaxActionsPerTurn)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxActionsPerTurn, 1);

        _map = map;
        _maxActionsPerTurn = maxActionsPerTurn;
    }

    public GameCommand Next(GameState state, LegalMoveSet legal)
    {
        if (!legal.HasMoves)
        {
            throw new InvalidOperationException($"The bot has nothing to do in the {state.Phase} phase.");
        }

        var board = new Board(state, _map);

        if (state.Phase == GamePhase.InitialPlacement)
        {
            return legal.Place(Spread(legal.Pools, pool => board.BestTarget(pool.TerritoryIds)));
        }

        if (state.Turn.BotActions >= _maxActionsPerTurn)
        {
            return WindDown(legal, board);
        }

        return state.Phase switch
        {
            GamePhase.Reinforcement => Reinforce(legal, board),
            GamePhase.Attack => AttackOrClose(legal, board),
            GamePhase.Regroup => RegroupOrEnd(legal, board),
            _ => throw new InvalidOperationException($"The bot has nothing to do in the {state.Phase} phase."),
        };
    }

    // ---- Refuerzo ---------------------------------------------------------------------------------------------------------------

    private static GameCommand Reinforce(LegalMoveSet legal, Board board) =>
        legal.Trades.Count > 0
            ? Trade(legal, board)
            : legal.Place(Spread(legal.Pools, pool => board.BestTarget(pool.TerritoryIds)));

    /// <summary>The trade that gives up the fewest country premiums still to be paid (the first one when several do).</summary>
    private static GameCommand Trade(LegalMoveSet legal, Board board)
    {
        var premiumsToPay = board.Hand.Where(card => !card.BonusClaimed && board.IsMine(card.TerritoryId)).Select(card => card.TerritoryId).ToHashSet();
        var cheapest = legal.Trades.MinBy(trade => trade.Cards.Count(premiumsToPay.Contains))!;

        return new TradeCards(legal.Seat, cheapest.Cards);
    }

    /// <summary>One placement for every pool that has a country to go to, each pool on the country <paramref name="target"/> names.</summary>
    private static IEnumerable<ArmyPlacement> Spread(IEnumerable<PlacementPool> pools, Func<PlacementPool, string> target) =>
        pools.Where(pool => pool.TerritoryIds.Count > 0).Select(pool => new ArmyPlacement(target(pool), pool.Armies));

    // ---- Ataque -----------------------------------------------------------------------------------------------------------------

    private static GameCommand AttackOrClose(LegalMoveSet legal, Board board)
    {
        if (legal.Occupation is { } range)
        {
            // Armies where an enemy is near strike next; in an interior country they would only be locked away from the front.
            var conquered = board.PendingConquest!.To;

            return new MoveAfterConquest(legal.Seat, board.IsFrontier(conquered) ? range.Max : range.Min);
        }

        if (BestAttack(legal.Attacks, board) is { } attack)
        {
            return new Attack(legal.Seat, attack.From, attack.To);
        }

        return legal.CanStartRegroup && board.HasInteriorStackNextToTheFront()
            ? new StartRegroup(legal.Seat)
            : new EndTurn(legal.Seat);
    }

    /// <summary>
    /// The attack that completes a continent, then the weakest defender, then the biggest attacker, then the first in map order
    /// (<paramref name="attacks"/> are listed in map order and only a strictly better one replaces the best so far); only attacks
    /// where the origin has at least <see cref="MinimumAttackAdvantage"/> armies more than the target are considered.
    /// </summary>
    private static AttackOption? BestAttack(IEnumerable<AttackOption> attacks, Board board)
    {
        AttackOption? best = null;
        (int Completes, int Defender, int Attacker) bestKey = default;

        foreach (var attack in attacks)
        {
            if (board.Armies(attack.From) < board.Armies(attack.To) + MinimumAttackAdvantage)
            {
                continue;
            }

            var key = (board.CompletesContinent(attack.To) ? 0 : 1, board.Armies(attack.To), -board.Armies(attack.From));
            if (best is null || key.CompareTo(bestKey) < 0)
            {
                best = attack;
                bestKey = key;
            }
        }

        return best;
    }

    // ---- Reagrupe ---------------------------------------------------------------------------------------------------------------

    /// <summary>Moves the biggest stack out of an interior country to the border country with the best advantage; with none, the turn ends.</summary>
    private static GameCommand RegroupOrEnd(LegalMoveSet legal, Board board)
    {
        RegroupOption? best = null;
        (int Armies, int Advantage) bestKey = default;

        foreach (var option in legal.Regroups)
        {
            if (board.IsFrontier(option.From) || !board.IsFrontier(option.To))
            {
                continue;
            }

            var key = (option.MaxArmies, board.Advantage(option.To));
            if (best is null || key.CompareTo(bestKey) > 0)
            {
                best = option;
                bestKey = key;
            }
        }

        return best is null
            ? new EndTurn(legal.Seat)
            : new Regroup(legal.Seat, best.From, best.To, best.MaxArmies);
    }

    // ---- the cap ----------------------------------------------------------------------------------------------------------------

    /// <summary>The cap is reached: the fewest steps that close the turn (see the class remarks).</summary>
    private static GameCommand WindDown(LegalMoveSet legal, Board board)
    {
        if (legal.Occupation is { } range)
        {
            return new MoveAfterConquest(legal.Seat, range.Min);
        }

        if (legal.TradeMandatory)
        {
            return Trade(legal, board);
        }

        if (legal.CanPlace)
        {
            return legal.Place(Spread(legal.Pools, pool => pool.TerritoryIds[0]));
        }

        return new EndTurn(legal.Seat);
    }

    // ---- what the bot sees of the board -----------------------------------------------------------------------------------------

    /// <summary>The board from the point of view of the active participant. Nothing here depends on the order neighbours are listed in.</summary>
    private sealed class Board
    {
        private readonly MapDefinition _map;
        private readonly Dictionary<string, TerritoryState> _territories;
        private readonly Dictionary<string, int> _notMineByContinent;

        public Board(GameState state, MapDefinition map)
        {
            _map = map;
            _territories = state.Territories.ToDictionary(t => t.Id);
            Seat = state.ActiveSeat;
            Hand = state.Participants[Seat].Cards;
            PendingConquest = state.Turn.PendingConquest;
            Mine = state.Territories.Where(t => t.Owner == Seat).Select(t => t.Id).ToArray();
            _notMineByContinent = map.Continents.ToDictionary(
                continent => continent.Id,
                continent => continent.TerritoryIds.Count(id => _territories[id].Owner != Seat));
        }

        public int Seat { get; }

        public ValueArray<HeldCard> Hand { get; }

        public PendingConquest? PendingConquest { get; }

        /// <summary>The countries of the active participant, in map order.</summary>
        public IReadOnlyList<string> Mine { get; }

        public int Armies(string id) => _territories[id].Armies;

        public bool IsMine(string id) => _territories[id].Owner == Seat;

        /// <summary>The country has an enemy neighbour: it can attack, and it can be attacked.</summary>
        public bool IsFrontier(string id) => _map.Neighbors(id).Any(neighbour => !IsMine(neighbour));

        /// <summary>Its armies minus those of its weakest enemy neighbour (the target it would attack first); just its armies when it has none.</summary>
        public int Advantage(string id)
        {
            var enemies = _map.Neighbors(id).Where(neighbour => !IsMine(neighbour)).Select(Armies).ToArray();

            return enemies.Length == 0 ? Armies(id) : Armies(id) - enemies.Min();
        }

        /// <summary>
        /// The country of <paramref name="ids"/> (in map order) where armies are best put: a frontier country before an interior
        /// one, and among equals the best <see cref="Advantage"/>; the first in map order when still equal.
        /// </summary>
        public string BestTarget(IReadOnlyList<string> ids)
        {
            var best = ids[0];
            var bestKey = (IsFrontier(best) ? 1 : 0, Advantage(best));

            foreach (var id in ids.Skip(1))
            {
                var key = (IsFrontier(id) ? 1 : 0, Advantage(id));
                if (key.CompareTo(bestKey) > 0)
                {
                    best = id;
                    bestKey = key;
                }
            }

            return best;
        }

        /// <summary>Taking <paramref name="targetId"/> would leave no country of its continent in other hands.</summary>
        public bool CompletesContinent(string targetId) => _notMineByContinent[_map.Territory(targetId).ContinentId] == 1;

        /// <summary>A regroup is worth making: an interior country with a stack to spare whose neighbours include a frontier one.</summary>
        public bool HasInteriorStackNextToTheFront() =>
            Mine.Any(id => !IsFrontier(id) && Armies(id) >= MinimumStackToMove && _map.Neighbors(id).Any(IsFrontier));
    }
}
