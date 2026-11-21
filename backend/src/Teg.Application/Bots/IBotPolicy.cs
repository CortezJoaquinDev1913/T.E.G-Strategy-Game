using Teg.Domain.Commands;
using Teg.Domain.Rules;
using Teg.Domain.State;

namespace Teg.Application.Bots;

/// <summary>
/// What a bot decides (B1). Given the saved state and the legal moves of the active participant (<see cref="LegalMoves.For"/>) it
/// returns the ONE command to send next. A policy is a pure function of its two arguments: it keeps no state of its own and draws no
/// random number, so the same state always gives the same command. It never decides for a seat that is not active.
/// </summary>
public interface IBotPolicy
{
    /// <exception cref="InvalidOperationException">There is nothing to do: the game is finished, or the phase has no decision.</exception>
    GameCommand Next(GameState state, LegalMoveSet legal);
}
