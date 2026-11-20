using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Map;
using Teg.Domain.Rng;
using Teg.Domain.Rules;
using Teg.Domain.State;
using static Teg.Domain.Tests.Support.GameFixtures;

namespace Teg.Domain.Tests.Support;

/// <summary>
/// Whole games played by a seeded random policy that picks ONLY from <see cref="LegalMoves.For"/> (the playout tests, the
/// agreement sweeps and the replay tests share it). Every command goes through <see cref="GameEngine.Apply"/> and must be accepted
/// (<c>ApplyOk</c>), which is the first proof that the descriptors are sound; a state that is not Finished but offers nothing
/// fails the playout on the spot ("never stuck").
/// </summary>
internal static class Playout
{
    /// <summary>One accepted command: the state it started from, the descriptor the policy chose from, the command, and its result.</summary>
    internal sealed record Step(int Index, GameState Before, LegalMoveSet Moves, GameCommand Command, Transition After);

    /// <summary>
    /// Plays from <paramref name="start"/> (a dealt game, or any state that is not Finished) until somebody wins or
    /// <paramref name="maxCommands"/> were played. Lazy: the caller asserts after every step. With a <paramref name="conqueror"/>
    /// seat that seat plays to win (see <see cref="RandomPlayer"/>), so that games end in victory.
    /// </summary>
    public static IEnumerable<Step> Run(GameState start, long choiceSeed, int maxCommands, MapDefinition? map = null, int? conqueror = null)
    {
        var board = map ?? RealMap;
        var player = new RandomPlayer(choiceSeed, board, conqueror);
        var state = start;

        for (var index = 0; index < maxCommands && state.Phase != GamePhase.Finished; index++)
        {
            var moves = LegalMoves.For(state, board);
            Assert.True(
                moves.HasMoves,
                $"Stuck: nothing is legal at step {index} (version {state.Version}, {state.Phase}, seat {state.ActiveSeat}) and the game is not over.");

            var command = player.Next(state, moves);
            var after = ApplyOk(state, command, board);

            yield return new Step(index, state, moves, command, after);
            state = after.State;
        }
    }

    /// <summary>The steps of a whole game, in order.</summary>
    public static List<Step> Record(GameState start, long choiceSeed, int maxCommands, MapDefinition? map = null, int? conqueror = null) =>
        Run(start, choiceSeed, maxCommands, map, conqueror).ToList();

    /// <summary>The state a playout ended in (the start state when no command was played).</summary>
    public static GameState FinalState(GameState start, IReadOnlyList<Step> steps) =>
        steps.Count == 0 ? start : steps[^1].After.State;
}

/// <summary>
/// The random policy: every choice is made among what the descriptor offers. It trades when it must (and now and then when it
/// may), places pools on random countries (three times out of four on a country that borders an enemy, so that attacks keep
/// happening), now and then only a part of a pool, attacks four times out of five (the weakest neighbour three times out of
/// four), occupies with a random legal count, starts a regroup or ends the turn otherwise, and regroups a random legal count.
/// The seat named <paramref name="conqueror"/> (if any) plays to win instead, so that whole games end in victory: it trades whenever
/// it can, puts every pool on its strongest frontier country, attacks only with an advantage of two armies (the biggest one first),
/// occupies with as many armies as it may (a sturdy garrison), and regroups only to bring its biggest interior stack to the
/// frontier. Its choices are still taken from the descriptor.
/// </summary>
internal sealed class RandomPlayer(long choiceSeed, MapDefinition map, int? conqueror = null)
{
    private readonly SeededRng _choices = new(choiceSeed);

    public GameCommand Next(GameState state, LegalMoveSet moves) =>
        state.ActiveSeat == conqueror ? Conquer(state, moves) : state.Phase switch
        {
            GamePhase.InitialPlacement => PlaceInitially(state, moves),
            GamePhase.Reinforcement => Reinforce(state, moves),
            GamePhase.Attack => AttackOrDecide(state, moves),
            GamePhase.Regroup => RegroupOrEnd(moves),
            _ => throw new InvalidOperationException($"Nothing to play in {state.Phase}."),
        };

    private GameCommand Conquer(GameState state, LegalMoveSet moves)
    {
        var seat = moves.Seat;
        int ArmiesIn(string id) => state.FindTerritory(id)!.Armies;

        switch (state.Phase)
        {
            case GamePhase.InitialPlacement or GamePhase.Reinforcement:
                return moves.Trades.Count > 0
                    ? new TradeCards(seat, moves.Trades[0].Cards)
                    : moves.Place(moves.Pools.Select(pool => new ArmyPlacement(Strongest(state, pool.TerritoryIds), pool.Armies)));

            case GamePhase.Attack when moves.Occupation is { } range:
                return new MoveAfterConquest(seat, range.Max); // a sturdy garrison: the weak ones are taken back at once

            case GamePhase.Attack:
                var best = moves.Attacks
                    .Where(option => ArmiesIn(option.From) >= ArmiesIn(option.To) + 2)
                    .MaxBy(option => ArmiesIn(option.From) - ArmiesIn(option.To));

                if (best is not null)
                {
                    return new Attack(seat, best.From, best.To);
                }

                return moves.CanStartRegroup ? new StartRegroup(seat) : new EndTurn(seat);

            default:
                // Bring the biggest interior stack to the frontier, where it can strike next turn.
                var march = moves.Regroups
                    .Where(option => IsFrontier(state, option.To) && !IsFrontier(state, option.From))
                    .MaxBy(option => option.MaxArmies);

                return march is null ? new EndTurn(seat) : new Regroup(seat, march.From, march.To, march.MaxArmies);
        }
    }

    private bool IsFrontier(GameState state, string id) =>
        map.Neighbors(id).Any(neighbour => state.FindTerritory(neighbour)!.Owner != state.ActiveSeat);

    /// <summary>The country of <paramref name="ids"/> with the most armies, among those that border an enemy when there are any.</summary>
    private string Strongest(GameState state, ValueArray<string> ids)
    {
        var frontier = ids.Where(id => IsFrontier(state, id)).ToArray();

        return (frontier.Length > 0 ? frontier : ids.ToArray()).MaxBy(id => state.FindTerritory(id)!.Armies)!;
    }

    private GameCommand PlaceInitially(GameState state, LegalMoveSet moves)
    {
        var pool = moves.Pools.Single();

        return moves.Place(Spread(state, pool.Armies, pool.TerritoryIds));
    }

    private GameCommand Reinforce(GameState state, LegalMoveSet moves)
    {
        if (moves.Trades.Count > 0 && (moves.TradeMandatory || _choices.NextInt(3) == 0))
        {
            return new TradeCards(moves.Seat, moves.Trades[_choices.NextInt(moves.Trades.Count)].Cards);
        }

        Assert.True(moves.CanPlace, "Nothing to trade and nothing to place in Refuerzo.");

        if (_choices.NextInt(4) == 0)
        {
            var pool = moves.Pools[_choices.NextInt(moves.Pools.Count)]; // only a part of one pool: the rest waits
            return moves.Place(Spread(state, 1 + _choices.NextInt(pool.Armies), pool.TerritoryIds));
        }

        return moves.Place(moves.Pools.SelectMany(pool => Spread(state, pool.Armies, pool.TerritoryIds)));
    }

    private GameCommand AttackOrDecide(GameState state, LegalMoveSet moves)
    {
        if (moves.Occupation is { } range)
        {
            return new MoveAfterConquest(moves.Seat, range.Min + _choices.NextInt(range.Max - range.Min + 1));
        }

        if (moves.Attacks.Count > 0 && _choices.NextInt(5) != 0)
        {
            var attack = _choices.NextInt(4) == 0
                ? moves.Attacks[_choices.NextInt(moves.Attacks.Count)]
                : moves.Attacks.MinBy(option => state.FindTerritory(option.To)!.Armies)!;

            return new Attack(moves.Seat, attack.From, attack.To);
        }

        return moves.CanStartRegroup && _choices.NextInt(2) == 0 ? new StartRegroup(moves.Seat) : new EndTurn(moves.Seat);
    }

    private GameCommand RegroupOrEnd(LegalMoveSet moves)
    {
        if (moves.Regroups.Count > 0 && _choices.NextInt(3) != 0)
        {
            var option = moves.Regroups[_choices.NextInt(moves.Regroups.Count)];

            return new Regroup(moves.Seat, option.From, option.To, 1 + _choices.NextInt(option.MaxArmies));
        }

        return new EndTurn(moves.Seat);
    }

    /// <summary><paramref name="armies"/> armies in one or a few chunks on countries of <paramref name="territories"/>, mostly on the frontier.</summary>
    private List<ArmyPlacement> Spread(GameState state, int armies, ValueArray<string> territories)
    {
        var all = territories.ToArray();
        var frontier = all
            .Where(id => map.Neighbors(id).Any(neighbour => state.FindTerritory(neighbour)!.Owner != state.ActiveSeat))
            .ToArray();
        var placements = new List<ArmyPlacement>();

        for (var left = armies; left > 0;)
        {
            var candidates = frontier.Length > 0 && _choices.NextInt(4) != 0 ? frontier : all;
            var chunk = _choices.NextInt(2) == 0 ? left : 1 + _choices.NextInt(left);
            placements.Add(new ArmyPlacement(candidates[_choices.NextInt(candidates.Length)], chunk));
            left -= chunk;
        }

        return placements;
    }
}
