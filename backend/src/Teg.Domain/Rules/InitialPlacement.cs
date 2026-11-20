using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Events;
using Teg.Domain.Setup;
using Teg.Domain.State;

namespace Teg.Domain.Rules;

/// <summary>
/// Initial placement (R§3 "Vueltas de colocación inicial"): round 1, then round 2, in table order from the first
/// player. On their turn a participant places the ENTIRE pool of the round on their own countries. After the last
/// participant of round 2 the first hostilities round starts (R§4).
/// </summary>
internal static class InitialPlacement
{
    internal static Result<Transition, RuleViolation> Apply(GameState state, PlaceInitialArmies command)
    {
        var violation = CommandGuards.Check(state, command, GamePhase.InitialPlacement)
            ?? ArmyPlacements.ValidateEntries(state, command.Seat, command.Placements)
            ?? ValidateTotal(state, command);
        if (violation is not null)
        {
            return Result<Transition, RuleViolation>.Fail(violation);
        }

        var placed = state with { Territories = ArmyPlacements.AddTo(state.Territories, command.Placements) };

        return Result<Transition, RuleViolation>.Ok(PassTheTurn(placed, command));
    }

    private static RuleViolation? ValidateTotal(GameState state, PlaceInitialArmies command)
    {
        var pool = GameSetup.PlacementPools(state.Participants.Count)[state.PlacementRound - 1];
        var total = command.Placements.Sum(p => (long)p.Armies); // long: absurd counts must be refused, not overflow
        if (total != pool)
        {
            return new RuleViolation(
                RuleViolations.WrongPlacementTotal,
                $"Round {state.PlacementRound} places exactly {pool} armies, not {total}.");
        }

        return null;
    }

    /// <summary>The turn goes to the next participant; after the last of a round, to the first player of the next stage.</summary>
    private static Transition PassTheTurn(GameState state, PlaceInitialArmies command)
    {
        var seat = command.Seat;
        var participants = state.Participants.Count;
        var position = state.TablePosition(seat);
        var events = new List<GameEvent>
        {
            new InitialArmiesPlaced(seat, state.PlacementRound, command.Placements),
            new TurnEnded(seat),
        };

        if (position < participants - 1)
        {
            var next = state.TableOrder[position + 1];
            events.Add(new TurnStarted(next, state.Round, GamePhase.InitialPlacement));

            return new Transition(state with { ActiveSeat = next }, events.ToValueArray());
        }

        if (state.PlacementRound < GameSetup.PlacementPools(participants).Count)
        {
            events.Add(new TurnStarted(state.FirstSeat, state.Round, GamePhase.InitialPlacement));

            return new Transition(
                state with { PlacementRound = state.PlacementRound + 1, ActiveSeat = state.FirstSeat },
                events.ToValueArray());
        }

        events.Add(new RoundAdvanced(GameState.HostilitiesRound));
        events.Add(new TurnStarted(state.FirstSeat, GameState.HostilitiesRound, GamePhase.Attack));

        return new Transition(
            state with { Phase = GamePhase.Attack, Round = GameState.HostilitiesRound, ActiveSeat = state.FirstSeat },
            events.ToValueArray());
    }
}
