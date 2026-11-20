using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Events;
using Teg.Domain.State;

namespace Teg.Domain.Rules;

/// <summary>
/// The turn state machine and the rotation (R§4). A turn runs Refuerzo (from round 2), Ataque, Reagrupe, Tarjeta:
/// ordered and irreversible, each optional except Refuerzo. <see cref="EndTurn"/> closes the turn through Tarjeta
/// and passes it to the next LIVING participant in table order; the round counter grows when the rotation wraps
/// back to the first player.
/// </summary>
internal static class TurnMachine
{
    internal static Result<Transition, RuleViolation> StartRegroup(GameState state, StartRegroup command)
    {
        var violation = CommandGuards.Check(state, command, GamePhase.Attack);
        if (violation is not null)
        {
            return Result<Transition, RuleViolation>.Fail(violation);
        }

        return Result<Transition, RuleViolation>.Ok(new Transition(
            state with { Phase = GamePhase.Regroup },
            new GameEvent[] { new PhaseChanged(command.Seat, GamePhase.Attack, GamePhase.Regroup) }.ToValueArray()));
    }

    /// <summary>Allowed from Ataque (Reagrupe is skipped) and from Reagrupe; never from Refuerzo, which must be completed.</summary>
    internal static Result<Transition, RuleViolation> EndTurn(GameState state, EndTurn command)
    {
        var violation = CommandGuards.Check(state, command, GamePhase.Attack, GamePhase.Regroup);
        if (violation is not null)
        {
            return Result<Transition, RuleViolation>.Fail(violation);
        }

        var (next, wrapped) = NextLivingSeat(state);
        var round = wrapped ? state.Round + 1 : state.Round;
        var opening = round == GameState.HostilitiesRound ? GamePhase.Attack : GamePhase.Reinforcement;

        // Tarjeta has no player decision: the turn passes through it on the way out.
        var events = new List<GameEvent>
        {
            new PhaseChanged(command.Seat, state.Phase, GamePhase.Card),
            new TurnEnded(command.Seat),
        };
        if (wrapped)
        {
            events.Add(new RoundAdvanced(round));
        }

        events.Add(new TurnStarted(next, round, opening));

        return Result<Transition, RuleViolation>.Ok(new Transition(
            state with { ActiveSeat = next, Round = round, Phase = opening },
            events.ToValueArray()));
    }

    /// <summary>
    /// The next participant after the active one in table order who is not eliminated, and whether getting there
    /// went past the end of the table (back to the first player's position), which closes the round.
    /// </summary>
    private static (int Seat, bool Wrapped) NextLivingSeat(GameState state)
    {
        var order = state.TableOrder;
        var position = state.TablePosition(state.ActiveSeat);

        for (var step = 1; step <= order.Count; step++)
        {
            var candidate = position + step;
            var seat = order[candidate % order.Count];
            if (!state.Participants[seat].Eliminated)
            {
                return (seat, Wrapped: candidate >= order.Count);
            }
        }

        throw new InvalidOperationException("A game in progress always has a living participant.");
    }
}
