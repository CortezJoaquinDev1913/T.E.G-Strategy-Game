using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Rng;
using Teg.Domain.State;
using static Teg.Domain.Tests.Support.GameFixtures;

namespace Teg.Domain.Tests.Support;

/// <summary>
/// Builders and oracles of the combat, conquest and elimination tests. The boards are the <see cref="TinyBoard"/>
/// (seat 0 owns a b e f, seat 1 owns c d; land borders a-b a-c c-d c-e e-f, bridges b-d b-f).
/// <para>
/// Dice are never fixed with magic seeds: <see cref="WithDice"/> SEARCHES for a seed whose first dice satisfy what the
/// test needs, replaying the documented draw order (attacker dice first, then defender dice, one raw draw each), so
/// the tests stay deterministic and say what outcome they force.
/// </para>
/// </summary>
internal static class CombatFixtures
{
    private const int SeedSearchLimit = 50_000;
    private const int MaxDice = 3;

    /// <summary>
    /// R§6 written a DIFFERENT way than the engine does it: the best remaining die of each side face each other until
    /// one side runs out of dice; a tie goes to the defender. (The engine sorts both lists and zips them.)
    /// </summary>
    public static (int AttackerLosses, int DefenderLosses) Oracle(IEnumerable<int> attackerDice, IEnumerable<int> defenderDice)
    {
        var attacker = attackerDice.ToList();
        var defender = defenderDice.ToList();
        var attackerLosses = 0;
        var defenderLosses = 0;

        while (attacker.Count > 0 && defender.Count > 0)
        {
            var attackerBest = attacker.Max();
            var defenderBest = defender.Max();
            attacker.Remove(attackerBest);
            defender.Remove(defenderBest);

            if (attackerBest > defenderBest)
            {
                defenderLosses++;
            }
            else
            {
                attackerLosses++;
            }
        }

        return (attackerLosses, defenderLosses);
    }

    /// <summary>
    /// The dice the engine must roll from <paramref name="from"/>: attacker dice FIRST, then defender dice, each list
    /// sorted from the highest to the lowest die, plus the generator state after rolling them.
    /// </summary>
    public static (int[] Attacker, int[] Defender, RngState After) ReplayDice(RngState from, int attackerDice, int defenderDice)
    {
        var rng = new SeededRng(from.Seed, from.Draws);
        var attacker = Enumerable.Range(0, attackerDice).Select(_ => rng.RollDie()).OrderDescending().ToArray();
        var defender = Enumerable.Range(0, defenderDice).Select(_ => rng.RollDie()).OrderDescending().ToArray();

        return (attacker, defender, rng.State);
    }

    /// <summary>Dice counts of R§6 for an attack from <paramref name="origin"/> to <paramref name="destination"/> in this state.</summary>
    public static (int Attacker, int Defender) DiceCounts(GameState state, string origin, string destination) =>
        (Math.Min(MaxDice, state.FindTerritory(origin)!.Armies - 1), Math.Min(MaxDice, state.FindTerritory(destination)!.Armies));

    /// <summary>
    /// The same state with a seed (no draws taken) whose dice for an attack <paramref name="origin"/> to
    /// <paramref name="destination"/> satisfy <paramref name="wanted"/> (attacker dice, defender dice; both sorted
    /// from the highest to the lowest).
    /// </summary>
    public static GameState WithDice(GameState state, string origin, string destination, Func<int[], int[], bool> wanted)
    {
        var (attackerDice, defenderDice) = DiceCounts(state, origin, destination);

        for (var seed = 1L; seed <= SeedSearchLimit; seed++)
        {
            var rng = new RngState(seed, Draws: 0);
            var (attacker, defender, _) = ReplayDice(rng, attackerDice, defenderDice);
            if (wanted(attacker, defender))
            {
                return state with { Rng = rng };
            }
        }

        throw new InvalidOperationException(
            $"No seed in 1..{SeedSearchLimit} gives the wanted dice ({attackerDice} against {defenderDice} dice).");
    }

    /// <summary>The attack empties the destination: the defender loses every army it has (so the attacker loses none).</summary>
    public static Func<int[], int[], bool> Conquers(int defenderArmies) =>
        (attacker, defender) => Oracle(attacker, defender).DefenderLosses == defenderArmies;

    /// <summary>
    /// Plays the conquering attack: dice are chosen to empty <paramref name="destination"/>, the attack is applied on
    /// the <see cref="TinyBoard"/> and the state with the occupation PENDING is returned.
    /// </summary>
    public static GameState Conquer(GameState state, string origin, string destination) =>
        ConquerWithEvents(state, origin, destination).State;

    /// <summary>Like <see cref="Conquer"/>, keeping the events of the conquering attack.</summary>
    public static Transition ConquerWithEvents(GameState state, string origin, string destination)
    {
        var rigged = WithDice(state, origin, destination, Conquers(state.FindTerritory(destination)!.Armies));

        return ApplyOk(rigged, new Attack(state.ActiveSeat, origin, destination), TinyBoard.Map);
    }

    public static int TotalArmies(GameState state) => state.Territories.Sum(t => t.Armies);

    public static GameState Armies(GameState state, params (string Id, int Armies)[] armies)
    {
        var byId = armies.ToDictionary(a => a.Id, a => a.Armies);

        return state with
        {
            Territories = state.Territories
                .Select(t => byId.TryGetValue(t.Id, out var count) ? t with { Armies = count } : t)
                .ToValueArray(),
        };
    }

    public static GameState Owners(GameState state, params (string Id, int Owner)[] owners)
    {
        var byId = owners.ToDictionary(o => o.Id, o => o.Owner);

        return state with
        {
            Territories = state.Territories
                .Select(t => byId.TryGetValue(t.Id, out var owner) ? t with { Owner = owner } : t)
                .ToValueArray(),
        };
    }

    /// <summary>
    /// Seat 0 attacks from "a" (<paramref name="attackerArmies"/> armies) the country "c" of seat 1
    /// (<paramref name="defenderArmies"/>), over a land border. Seat 1 also owns "d" (2 armies), so conquering "c"
    /// does not eliminate it. Phase Ataque, round 1.
    /// </summary>
    public static GameState Arena(int attackerArmies, int defenderArmies) =>
        Armies(TinyBoard.State(GamePhase.Attack), ("a", attackerArmies), ("c", defenderArmies));

    /// <summary>Like <see cref="Arena"/>, but "c" is the ONLY country of seat 1: conquering it eliminates them.</summary>
    public static GameState LastStand(int attackerArmies, int defenderArmies) =>
        Owners(Arena(attackerArmies, defenderArmies), ("d", 0));

    /// <summary>
    /// Three participants: seat 0 as in <see cref="LastStand"/> (owns a b e f), seat 1 owns ONLY "c", seat 2 owns
    /// "d" (2 armies). Seat 1 is the middle participant of the table.
    /// </summary>
    public static GameState ThreeSeats(int attackerArmies, int defenderArmies)
    {
        var state = Owners(LastStand(attackerArmies, defenderArmies), ("d", 2));

        return state with
        {
            Participants = state.Participants
                .Append(new Participant(2, "Cata", ParticipantKind.Human, PlayerColor.Verde))
                .ToValueArray(),
            Ranking = new[] { 0, 1, 2 }.ToValueArray(),
        };
    }
}
