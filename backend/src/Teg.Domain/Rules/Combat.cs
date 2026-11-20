using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Events;
using Teg.Domain.Map;
using Teg.Domain.Rng;
using Teg.Domain.State;

namespace Teg.Domain.Rules;

/// <summary>The armies each side loses in one fight (R§6): one for every compared pair it lost.</summary>
public sealed record DiceOutcome(int AttackerLosses, int DefenderLosses);

/// <summary>
/// An attack (R§6). The attacker rolls <c>min(3, origin - 1)</c> dice and the defender <c>min(3, destination)</c>, from
/// the seeded RNG of the game state: ATTACKER dice first, then the defender dice, one draw per die, so the generator
/// advances by exactly the number of dice rolled and a replay of the same state fights the same fight (E10). Each side's
/// dice are sorted from the highest to the lowest and compared pair by pair; only as many pairs as the side with fewer
/// dice has are compared; a TIE goes to the DEFENDER; every pair a side loses costs it one army.
/// <para>
/// <see cref="Resolve"/> and the dice counts are pure and public (the UI and the legal-moves query read them); the
/// execution against a game state is internal and goes through <see cref="GameEngine.Apply"/>.
/// </para>
/// </summary>
public static class Combat
{
    /// <summary>Most dice a side rolls in one attack.</summary>
    public const int MaxDice = 3;

    private const int DieFaces = 6;

    /// <summary>Dice of the attacker: one less than the armies of the origin (one must stay), at most three.</summary>
    public static int AttackerDiceCount(int originArmies)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(originArmies, 2);

        return Math.Min(MaxDice, originArmies - 1);
    }

    /// <summary>Dice of the defender: one per army of the destination, at most three.</summary>
    public static int DefenderDiceCount(int destinationArmies)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(destinationArmies, 1);

        return Math.Min(MaxDice, destinationArmies);
    }

    /// <summary>
    /// Compares the dice of both sides (in any order: they are sorted here, the arguments are not touched) and returns
    /// the losses of each side.
    /// </summary>
    /// <exception cref="ArgumentException">A side without dice, with more than three, or a die outside 1 to 6.</exception>
    public static DiceOutcome Resolve(IReadOnlyList<int> attackerDice, IReadOnlyList<int> defenderDice)
    {
        RequireRealDice(attackerDice, nameof(attackerDice));
        RequireRealDice(defenderDice, nameof(defenderDice));

        var attacker = attackerDice.OrderDescending().ToArray();
        var defender = defenderDice.OrderDescending().ToArray();
        var pairs = Math.Min(attacker.Length, defender.Length);
        var defenderLosses = Enumerable.Range(0, pairs).Count(pair => attacker[pair] > defender[pair]);

        return new DiceOutcome(AttackerLosses: pairs - defenderLosses, DefenderLosses: defenderLosses);
    }

    private static void RequireRealDice(IReadOnlyList<int> dice, string side)
    {
        if (dice.Count is < 1 or > MaxDice)
        {
            throw new ArgumentException($"A side rolls between 1 and {MaxDice} dice, not {dice.Count}.", side);
        }

        if (dice.Any(die => die is < 1 or > DieFaces))
        {
            throw new ArgumentException($"Every die shows a number from 1 to {DieFaces}.", side);
        }
    }

    internal static Result<Transition, RuleViolation> Apply(GameState state, Attack command, MapDefinition map)
    {
        var violation = MoveValidator.ValidateAttack(state, command, map);
        if (violation is not null)
        {
            return Result<Transition, RuleViolation>.Fail(violation);
        }

        var origin = state.FindTerritory(command.From)!;
        var destination = state.FindTerritory(command.To)!;

        var rng = new SeededRng(state.Rng.Seed, state.Rng.Draws);
        var attackerDice = Roll(rng, AttackerDiceCount(origin.Armies));
        var defenderDice = Roll(rng, DefenderDiceCount(destination.Armies));
        var outcome = Resolve(attackerDice, defenderDice);

        var fought = state with
        {
            Rng = rng.State,
            Territories = state.Territories
                .Select(t => t.Id == origin.Id ? t with { Armies = t.Armies - outcome.AttackerLosses }
                    : t.Id == destination.Id ? t with { Armies = t.Armies - outcome.DefenderLosses }
                    : t)
                .ToValueArray(),
        };
        var events = new List<GameEvent>
        {
            new AttackResolved(
                command.Seat,
                command.From,
                command.To,
                attackerDice,
                defenderDice,
                outcome.AttackerLosses,
                outcome.DefenderLosses),
        };

        return destination.Armies > outcome.DefenderLosses
            ? Result<Transition, RuleViolation>.Ok(new Transition(fought, events.ToValueArray()))
            : Result<Transition, RuleViolation>.Ok(Conquest.Take(fought, command, destination.Owner, events, map));
    }

    /// <summary>The dice, rolled one after the other from <paramref name="rng"/>, sorted from the highest to the lowest.</summary>
    private static ValueArray<int> Roll(SeededRng rng, int dice) =>
        Enumerable.Range(0, dice).Select(_ => rng.RollDie()).OrderDescending().ToValueArray();
}
