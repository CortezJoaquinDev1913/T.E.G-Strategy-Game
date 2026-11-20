using Teg.Domain.Commands;
using Teg.Domain.Map;
using Teg.Domain.Rules;
using Teg.Domain.State;

namespace Teg.Domain;

/// <summary>
/// The rules engine: <c>Apply(state, command, map)</c> is pure and deterministic. It either refuses the command
/// with a typed <see cref="RuleViolation"/> (the input state is never touched) or returns the new state with the
/// events that explain the change. Every accepted command raises <see cref="GameState.Version"/> by one.
/// </summary>
public static class GameEngine
{
    public static Result<Transition, RuleViolation> Apply(GameState state, GameCommand command, MapDefinition map)
    {
        var outcome = command switch
        {
            PlaceInitialArmies place => InitialPlacement.Apply(state, place),
            PlaceReinforcements reinforce => Reinforce.Place(state, reinforce, map),
            TradeCards trade => Trade.Apply(state, trade, map),
            StartRegroup start => TurnMachine.StartRegroup(state, start),
            EndTurn end => TurnMachine.EndTurn(state, end, map),
            Attack attack => Combat.Apply(state, attack, map),
            MoveAfterConquest occupy => Conquest.Occupy(state, occupy),
            Regroup regroup => Regrouping.Apply(state, regroup, map),
            _ => throw new ArgumentException($"Unknown command {command.GetType().Name}.", nameof(command)),
        };

        return outcome.IsSuccess
            ? Result<Transition, RuleViolation>.Ok(outcome.Value with
            {
                State = outcome.Value.State with { Version = state.Version + 1 },
            })
            : outcome;
    }
}
