using Teg.Domain.Commands;
using Teg.Domain.Map;
using Teg.Domain.State;
using static Teg.Domain.Tests.Support.CombatFixtures;
using static Teg.Domain.Tests.Support.GameFixtures;

namespace Teg.Domain.Tests.Support;

/// <summary>
/// Builders of the regroup tests. States are reached through the real <see cref="StartRegroup"/> command (so the
/// quotas are the ones the engine fixes), on the <see cref="TinyBoard"/> (seat 0 owns a 5, b 3, e 1, f 2; land borders
/// a-b and e-f, bridge b-f: the chain a - b - f - e) or on the real board (the chain <see cref="X"/> - <see cref="Y"/> -
/// <see cref="Z"/>: a land border and then a bridge).
/// </summary>
internal static class RegroupFixtures
{
    /// <summary>
    /// Three own countries in a row on the real map (the chain of the rules' example): X touches Y by land
    /// (Francia-España), Y touches Z by a bridge (España-Sahara), X and Z do not touch.
    /// </summary>
    public const string X = "francia";

    public const string Y = "espana";
    public const string Z = "sahara";

    public static Regroup Move(string from, string to, int armies, int seat = 0) => new(seat, from, to, armies);

    /// <summary>The <see cref="TinyBoard"/> in Ataque, with these armies changed, moved on to Reagrupe by the engine.</summary>
    public static GameState TinyInRegroup(params (string Id, int Armies)[] armies) =>
        ApplyOk(Armies(TinyBoard.State(GamePhase.Attack), armies), new StartRegroup(0), TinyBoard.Map).State;

    /// <summary>Applies the regroups one after the other (each must be accepted).</summary>
    public static GameState Moved(GameState state, MapDefinition map, params Regroup[] moves) =>
        moves.Aggregate(state, (current, move) => ApplyOk(current, move, map).State);

    /// <summary>
    /// The real board with seat 0 owning X, Y and Z (the given armies) plus every other country at one army, in Reagrupe
    /// (2 participants, round 2); the three countries of the chain are the only ones of seat 0 with room to move.
    /// </summary>
    public static GameState ChainInRegroup(int xArmies, int yArmies, int zArmies)
    {
        var attack = Armies(
            Owners(InPlay(2, GamePhase.Attack, round: 2, activeSeat: 0), (X, 0), (Y, 0), (Z, 0)),
            (X, xArmies),
            (Y, yArmies),
            (Z, zArmies));

        return ApplyOk(attack, new StartRegroup(0)).State;
    }
}
