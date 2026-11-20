using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Events;
using Teg.Domain.Map;
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

        // The quotas are fixed NOW, from the armies on the board at the start of the phase; `with` keeps the rest of the
        // turn context (the conquest counter is still needed by the card phase).
        return Result<Transition, RuleViolation>.Ok(new Transition(
            state with
            {
                Phase = GamePhase.Regroup,
                Turn = state.Turn with { RegroupQuotas = Regrouping.ComputeQuotas(state) },
            },
            new GameEvent[] { new PhaseChanged(command.Seat, GamePhase.Attack, GamePhase.Regroup) }.ToValueArray()));
    }

    /// <summary>
    /// Allowed from Ataque (Reagrupe is skipped) and from Reagrupe. From Refuerzo it is refused with
    /// <see cref="RuleViolations.ArmiesPending"/>: that phase ends by itself, once everything is placed (R§5).
    /// The turn then goes through Tarjeta (<see cref="CardPhase"/>: the card awarded and the premiums, R§7) and ends; the
    /// events come in this order: the move into Tarjeta, the card phase, the end of the turn, the new round if the rotation
    /// wrapped, the start of the next turn. When the next turn opens with Refuerzo, its pool is computed here, from the
    /// board as it stands.
    /// </summary>
    internal static Result<Transition, RuleViolation> EndTurn(GameState state, EndTurn command, MapDefinition map)
    {
        var violation = CommandGuards.Check(state, command, GamePhase.Reinforcement, GamePhase.Attack, GamePhase.Regroup);
        if (violation is not null)
        {
            return Result<Transition, RuleViolation>.Fail(violation);
        }

        if (state.Phase == GamePhase.Reinforcement)
        {
            return Result<Transition, RuleViolation>.Fail(new RuleViolation(
                RuleViolations.ArmiesPending,
                $"{state.Turn.PendingArmies} reinforcements are still to be placed: the turn cannot end before."));
        }

        var (next, wrapped) = NextLivingSeat(state);
        var round = wrapped ? state.Round + 1 : state.Round;
        var opening = round == GameState.HostilitiesRound ? GamePhase.Attack : GamePhase.Reinforcement;

        // Tarjeta has no player decision: the turn passes through it on the way out. The card phase resolves here, after
        // the move into the phase and before the turn is over (card awarded, then the premiums); the rotation below is
        // computed from the board it leaves, which only the premium armies change.
        var events = new List<GameEvent> { new PhaseChanged(command.Seat, state.Phase, GamePhase.Card) };
        var (closed, cardEvents) = CardPhase.Resolve(state, command.Seat);
        events.AddRange(cardEvents);
        events.Add(new TurnEnded(command.Seat));
        if (wrapped)
        {
            events.Add(new RoundAdvanced(round));
        }

        events.Add(new TurnStarted(next, round, opening));

        var pool = TurnContext.None;
        if (opening == GamePhase.Reinforcement)
        {
            pool = Reinforce.ComputePool(closed, next, map);
            events.Add(new ReinforcementPoolComputed(next, pool.FreeArmies, pool.ContinentPools));
        }

        return Result<Transition, RuleViolation>.Ok(new Transition(
            closed with { ActiveSeat = next, Round = round, Phase = opening, Turn = pool },
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
