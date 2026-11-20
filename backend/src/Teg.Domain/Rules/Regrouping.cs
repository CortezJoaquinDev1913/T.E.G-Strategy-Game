using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Events;
using Teg.Domain.Map;
using Teg.Domain.State;

namespace Teg.Domain.Rules;

/// <summary>
/// Reagrupe (R§6). Armies move from an own country to an adjacent own one, leaving at least one behind, as often as the
/// participant likes, and NEVER CHAINED: when the phase starts every own country gets a quota of <c>armies - 1</c>
/// (<see cref="ComputeQuotas"/>, stored in the turn context); moving spends the quota of the ORIGIN and gives nothing to
/// the destination, so the armies that arrived in this phase cannot move on. The class is named after the activity
/// because <c>Regroup</c> is the command.
/// </summary>
internal static class Regrouping
{
    /// <summary>
    /// The quota of every country the ACTIVE participant owns, from the armies on the board right now: the start of the
    /// phase. A country with a single army has a quota of zero.
    /// </summary>
    internal static ValueArray<RegroupQuota> ComputeQuotas(GameState state) =>
        state.Territories
            .Where(t => t.Owner == state.ActiveSeat)
            .Select(t => new RegroupQuota(t.Id, t.Armies - 1))
            .ToValueArray();

    /// <summary>
    /// The shared checks of <see cref="MoveValidator"/> come first (phase, actor, ownership, adjacency, at least one army
    /// left behind); then the quota of the origin, which is what forbids chaining.
    /// </summary>
    internal static Result<Transition, RuleViolation> Apply(GameState state, Regroup command, MapDefinition map)
    {
        var violation = MoveValidator.ValidateRegroup(state, command, map);
        if (violation is not null)
        {
            return Result<Transition, RuleViolation>.Fail(violation);
        }

        var quota = state.Turn.RegroupQuotaOf(command.From);
        if (command.Armies > quota)
        {
            return Result<Transition, RuleViolation>.Fail(new RuleViolation(
                RuleViolations.RegroupQuotaExceeded,
                $"{command.From} can still send {quota} armies in this phase, not {command.Armies}: armies that arrived in Reagrupe cannot move on."));
        }

        var moved = state with
        {
            Territories = state.Territories
                .Select(t => t.Id == command.From ? t with { Armies = t.Armies - command.Armies }
                    : t.Id == command.To ? t with { Armies = t.Armies + command.Armies }
                    : t)
                .ToValueArray(),
            Turn = state.Turn with
            {
                // Only the origin pays: what the destination receives does not become movable.
                RegroupQuotas = state.Turn.RegroupQuotas
                    .Select(q => q.TerritoryId == command.From ? q with { Movable = q.Movable - command.Armies } : q)
                    .ToValueArray(),
            },
        };

        return Result<Transition, RuleViolation>.Ok(new Transition(
            moved,
            new GameEvent[] { new ArmiesRegrouped(command.Seat, command.From, command.To, command.Armies) }.ToValueArray()));
    }
}
