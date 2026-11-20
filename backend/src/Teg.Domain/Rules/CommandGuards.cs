using Teg.Domain.Commands;
using Teg.Domain.State;

namespace Teg.Domain.Rules;

/// <summary>The checks every command shares: the game is running, the actor is the active participant, the phase fits.</summary>
internal static class CommandGuards
{
    /// <summary>Returns the first violated guard, or <c>null</c> when the command may go on to its own validation.</summary>
    internal static RuleViolation? Check(GameState state, GameCommand command, params GamePhase[] allowedPhases)
    {
        if (state.Phase == GamePhase.Finished)
        {
            return new RuleViolation(RuleViolations.GameFinished, "The game is over: no more moves are accepted.");
        }

        if (command.Seat != state.ActiveSeat)
        {
            return new RuleViolation(
                RuleViolations.NotActiveParticipant,
                $"Seat {command.Seat} cannot act: the active participant is seat {state.ActiveSeat}.");
        }

        if (!allowedPhases.Contains(state.Phase))
        {
            return new RuleViolation(
                RuleViolations.WrongPhase,
                $"{command.GetType().Name} is not allowed in the {state.Phase} phase.");
        }

        return null;
    }
}
