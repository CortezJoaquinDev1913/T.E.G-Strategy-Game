using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Events;
using Teg.Domain.State;

namespace Teg.Domain.Rules;

/// <summary>
/// The conquest of a country (R§6) and what follows from it.
/// <list type="number">
/// <item><see cref="Take"/>: the attack emptied the destination. The country changes owner AT ONCE and the conquering
/// army (one, taken from the origin) enters it, so no country is ever left without armies and the persisted state is
/// always valid. The conquest is counted for the turn and left PENDING. If it was the last country of the defender, the
/// defender is eliminated on the spot and the attacker inherits their cards.</item>
/// <item><see cref="Occupy"/>: the attacker decides how many armies the conquered country ends up with (1 to
/// min(3, armies of the origin before the attack - 1)).</item>
/// </list>
/// Nothing here decides victory: that is the business of the victory phase of the change plan.
/// </summary>
internal static class Conquest
{
    /// <summary>R§6: at most this many armies move into a conquered country.</summary>
    private const int MaxArmiesToMove = 3;

    /// <summary>The army that enters the emptied country together with the conquest.</summary>
    private const int ConqueringArmy = 1;

    /// <summary>
    /// Applies the conquest to <paramref name="state"/>, where the attack has already taken the last armies of the
    /// destination, and appends its events to <paramref name="events"/>.
    /// </summary>
    internal static Transition Take(GameState state, Attack attack, int previousOwner, List<GameEvent> events)
    {
        var territories = state.Territories
            .Select(t => t.Id == attack.From ? t with { Armies = t.Armies - ConqueringArmy }
                : t.Id == attack.To ? t with { Owner = attack.Seat, Armies = ConqueringArmy }
                : t)
            .ToValueArray();
        events.Add(new TerritoryConquered(attack.Seat, attack.From, attack.To, previousOwner));

        var conquered = state with
        {
            Territories = territories,
            Turn = state.Turn with
            {
                ConquestsThisTurn = state.Turn.ConquestsThisTurn + 1,
                PendingConquest = new PendingConquest(attack.From, attack.To),
            },
        };

        var lostTheLastCountry = territories.All(t => t.Owner != previousOwner);
        if (lostTheLastCountry)
        {
            conquered = Eliminate(conquered, attack.Seat, previousOwner, events); // adds its own event
        }

        return new Transition(conquered, events.ToValueArray());
    }

    /// <summary>
    /// The participant who lost their last country is marked eliminated (for good: they own nothing and the rotation
    /// skips them) and their hand goes, after the attacker's own cards, to the attacker (R§6). Their trade counter is
    /// left alone: it no longer matters. A hand of five or more is NOT traded now: the attacker settles it at their next
    /// Refuerzo (R§7).
    /// </summary>
    private static GameState Eliminate(GameState state, int by, int eliminated, List<GameEvent> events)
    {
        var inherited = state.Participants[eliminated].Cards;
        events.Add(new ParticipantEliminated(eliminated, by, inherited));

        return state with
        {
            Participants = state.Participants
                .Select(p => p.Seat == eliminated ? p with { Eliminated = true, Cards = ValueArray<string>.Empty }
                    : p.Seat == by ? p with { Cards = p.Cards.Concat(inherited).ToValueArray() }
                    : p)
                .ToValueArray(),
        };
    }

    internal static Result<Transition, RuleViolation> Occupy(GameState state, MoveAfterConquest command)
    {
        var violation = CommandGuards.Check(state, command, GamePhase.Attack);
        if (violation is not null)
        {
            return Result<Transition, RuleViolation>.Fail(violation);
        }

        if (state.Turn.PendingConquest is not { } pending)
        {
            return Result<Transition, RuleViolation>.Fail(new RuleViolation(
                RuleViolations.NoConquestPending,
                "There is no conquest waiting for its armies."));
        }

        var origin = state.FindTerritory(pending.From)!;
        var conquered = state.FindTerritory(pending.To)!;

        // The conquering army already stands in the conquered country: together they are what the origin had.
        var armiesBeforeTheAttack = origin.Armies + conquered.Armies;
        violation = Validate(command.Armies, armiesBeforeTheAttack);
        if (violation is not null)
        {
            return Result<Transition, RuleViolation>.Fail(violation);
        }

        var occupied = state with
        {
            Territories = state.Territories
                .Select(t => t.Id == origin.Id ? t with { Armies = armiesBeforeTheAttack - command.Armies }
                    : t.Id == conquered.Id ? t with { Armies = command.Armies }
                    : t)
                .ToValueArray(),
            Turn = state.Turn with { PendingConquest = null },
        };

        return Result<Transition, RuleViolation>.Ok(new Transition(
            occupied,
            new GameEvent[] { new ArmiesMovedAfterConquest(command.Seat, origin.Id, conquered.Id, command.Armies) }
                .ToValueArray()));
    }

    /// <summary>1 to min(3, armies of the origin before the attack - 1): the origin keeps at least one army.</summary>
    private static RuleViolation? Validate(int armies, int armiesBeforeTheAttack)
    {
        if (armies < 1)
        {
            return new RuleViolation(RuleViolations.InvalidArmyCount, $"At least one army must move in, not {armies}.");
        }

        if (armies > armiesBeforeTheAttack - 1)
        {
            return new RuleViolation(
                RuleViolations.NotEnoughArmies,
                $"The origin had {armiesBeforeTheAttack} armies: at most {armiesBeforeTheAttack - 1} can move (one must stay), not {armies}.");
        }

        if (armies > MaxArmiesToMove)
        {
            return new RuleViolation(
                RuleViolations.OccupationLimitExceeded,
                $"At most {MaxArmiesToMove} armies can move into a conquered country, not {armies}.");
        }

        return null;
    }
}
