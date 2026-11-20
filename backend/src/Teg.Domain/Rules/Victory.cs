using Teg.Domain.Collections;
using Teg.Domain.Events;
using Teg.Domain.Map;
using Teg.Domain.State;

namespace Teg.Domain.Rules;

/// <summary>
/// Victory and what leads to it (R§8). Pure: everything is decided from the state the conquest left and the events of the
/// action.
/// <list type="bullet">
/// <item><see cref="FallBack"/>: when a participant is eliminated, every OTHER living participant whose destruction objective
/// aims at that colour sees it pass to the common one, without announcement. The one who eliminated the colour is not touched:
/// that was their own kill.</item>
/// <item><see cref="Evaluate"/>: right after a conquest (the only action that changes who owns a country) the ACTIVE
/// participant wins if, in this order of priority, every other participant is eliminated, or they own the countries of the
/// common objective, or their secret objective is met. Nobody else can win, so there are no ties.</item>
/// </list>
/// </summary>
internal static class Victory
{
    /// <summary>R§8: a game of this many participants plays the common objective of <see cref="FourParticipantsCommonObjective"/> countries.</summary>
    private const int FourParticipants = 4;

    /// <summary>R§8: the common objective of a four-participant game. With two or three participants it is every country of the board.</summary>
    private const int FourParticipantsCommonObjective = 30;

    /// <summary>
    /// Decides victory after the conquest in <paramref name="conquest"/>, made by <paramref name="actor"/> (the active
    /// participant). Without a winner the transition is returned untouched. With one, the game becomes
    /// <see cref="GamePhase.Finished"/> with the winner and the reason, <see cref="GameWon"/> closes the events, and the
    /// occupation the conquest left pending is cleared: the game is over, and the board is already valid because the
    /// conquering army is already on the country.
    /// </summary>
    internal static Transition Evaluate(Transition conquest, int actor, MapDefinition map)
    {
        var state = conquest.State;
        if (ReasonToWin(state, actor, conquest.Events, map) is not { } reason)
        {
            return conquest;
        }

        return new Transition(
            state with
            {
                Phase = GamePhase.Finished,
                Winner = actor,
                WinReason = reason,
                Turn = state.Turn with { PendingConquest = null },
            },
            conquest.Events.Append(new GameWon(actor, reason)).ToValueArray());
    }

    /// <summary>Why <paramref name="actor"/> wins in <paramref name="state"/>, in the order of priority of R§8; <c>null</c> when they do not.</summary>
    private static WinReason? ReasonToWin(GameState state, int actor, IEnumerable<GameEvent> events, MapDefinition map)
    {
        if (state.Participants.All(p => p.Seat == actor || p.Eliminated))
        {
            return WinReason.Elimination;
        }

        var owned = state.Territories.Where(t => t.Owner == actor).Select(t => t.Id).ToHashSet();
        var commonObjective = state.Participants.Count == FourParticipants
            ? FourParticipantsCommonObjective
            : state.Territories.Count; // the table decides it, not how many participants are still alive
        if (owned.Count >= commonObjective)
        {
            return WinReason.CommonObjective;
        }

        return SecretObjectiveIsMet(state, actor, owned, events, map) ? WinReason.SecretObjective : null;
    }

    /// <summary>
    /// An occupation objective is met by the countries owned; a destruction objective by the kill of its target in THIS action
    /// (a target eliminated by someone else turned the objective into the common one). <see cref="Objective.None"/> and
    /// <see cref="Objective.Common"/> have nothing of their own to meet: the common objective is the thirty countries of a
    /// four-participant game, which every participant plays for and which was already looked at.
    /// </summary>
    private static bool SecretObjectiveIsMet(
        GameState state,
        int actor,
        IReadOnlySet<string> owned,
        IEnumerable<GameEvent> events,
        MapDefinition map) =>
        state.Participants[actor].Objective switch
        {
            Objective.Destruction destruction => events
                .OfType<ParticipantEliminated>()
                .Any(kill => kill.By == actor && state.Participants[kill.Eliminated].Color == destruction.Target),
            var objective => Objectives.IsFulfilled(objective, owned, map),
        };

    /// <summary>
    /// <paramref name="eliminated"/> has just been eliminated by <paramref name="by"/>. A living participant other than
    /// <paramref name="by"/> whose objective is to destroy that colour (the FINAL colour of the objective, fixed at the deal)
    /// gets <see cref="Objective.Common"/> instead, and an <see cref="ObjectiveFellBackToCommon"/> goes to the history, in seat
    /// order. Objectives of eliminated participants, of other colours, and the common or occupation ones are left alone.
    /// </summary>
    internal static GameState FallBack(GameState state, int by, int eliminated, List<GameEvent> events)
    {
        var colour = state.Participants[eliminated].Color;
        var fallen = state.Participants
            .Where(p => p.Seat != by && !p.Eliminated && p.Objective is Objective.Destruction destruction && destruction.Target == colour)
            .Select(p => p.Seat)
            .ToArray();

        events.AddRange(fallen.Select(seat => new ObjectiveFellBackToCommon(seat)));

        return state with
        {
            Participants = state.Participants
                .Select(p => fallen.Contains(p.Seat) ? p with { Objective = Objective.Common } : p)
                .ToValueArray(),
        };
    }
}
