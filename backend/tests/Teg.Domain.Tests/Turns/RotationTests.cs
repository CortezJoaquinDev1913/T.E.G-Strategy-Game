using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Events;
using Teg.Domain.Rules;
using Teg.Domain.State;
using Teg.Domain.Tests.Support;
using static Teg.Domain.Tests.Support.GameFixtures;

namespace Teg.Domain.Tests.Turns;

/// <summary>
/// E3 / R§4: the turn state machine and the rotation. Round 1 (hostilities) is Ataque, Reagrupe, Tarjeta with no
/// Refuerzo; from round 2 a turn opens with Refuerzo. Phases are ordered and irreversible. The turn ends with the
/// explicit <see cref="EndTurn"/>; the next LIVING participant in table order becomes active, and the round counter
/// grows when the rotation returns to the first player. Eliminated participants (R§2) are skipped for ever.
/// </summary>
public sealed class RotationTests
{
    /// <summary>
    /// Ends the turn of the active participant. A turn that opens with Refuerzo must first place the whole pool
    /// (R§5), which is done here with a real <see cref="PlaceReinforcements"/> command.
    /// </summary>
    private static GameState FinishTurn(GameState state)
    {
        if (state.Phase == GamePhase.Reinforcement)
        {
            state = PlaceWholePool(state);
        }

        return ApplyOk(state, new EndTurn(state.ActiveSeat)).State;
    }

    /// <summary>The pool announced when a turn opens with Refuerzo, for a participant without a whole continent.</summary>
    private static ReinforcementPoolComputed PoolOf(int seat, int armies) =>
        new(seat, armies, ValueArray<ContinentPool>.Empty);

    /// <summary>Who is active and in which round, at the start and after each of <paramref name="turns"/> ended turns.</summary>
    private static (int Seat, int Round)[] Walk(GameState state, int turns)
    {
        var visited = new List<(int Seat, int Round)> { (state.ActiveSeat, state.Round) };

        for (var turn = 0; turn < turns; turn++)
        {
            state = FinishTurn(state);
            visited.Add((state.ActiveSeat, state.Round));
        }

        return visited.ToArray();
    }

    // ---- phases of a turn -------------------------------------------------------------------------

    [Fact]
    public void FirstHostilitiesRound_HasNoReinforce_AndTheTurnRunsAttackThenRegroupThenCard()
    {
        var state = InPlay(3, GamePhase.Attack, round: 1, activeSeat: 0);

        var regroup = ApplyOk(state, new StartRegroup(0));

        Assert.Equal(GamePhase.Regroup, regroup.State.Phase);
        Assert.Equal(0, regroup.State.ActiveSeat);
        GameEvent[] regroupEvents = [new PhaseChanged(0, GamePhase.Attack, GamePhase.Regroup)];
        Assert.Equal(regroupEvents, regroup.Events.ToArray());

        var ended = ApplyOk(regroup.State, new EndTurn(0));

        GameEvent[] endEvents =
        [
            new PhaseChanged(0, GamePhase.Regroup, GamePhase.Card),
            new TurnEnded(0),
            new TurnStarted(1, Round: 1, GamePhase.Attack),
        ];
        Assert.Equal(endEvents, ended.Events.ToArray());
        Assert.Equal(GamePhase.Attack, ended.State.Phase); // next participant, still round 1: again no Refuerzo
        Assert.Equal(1, ended.State.ActiveSeat);
        Assert.Equal(1, ended.State.Round);
    }

    [Fact]
    public void EndTurn_FromAttack_SkipsTheOptionalRegroup_AndStillPassesThroughCard()
    {
        var state = InPlay(3, GamePhase.Attack, round: 1, activeSeat: 0);

        var ended = ApplyOk(state, new EndTurn(0));

        GameEvent[] expected =
        [
            new PhaseChanged(0, GamePhase.Attack, GamePhase.Card),
            new TurnEnded(0),
            new TurnStarted(1, Round: 1, GamePhase.Attack),
        ];
        Assert.Equal(expected, ended.Events.ToArray());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void FromRoundTwoOn_EveryTurnStartsWithReinforce(int round)
    {
        var state = InPlay(3, GamePhase.Attack, round, activeSeat: 0);

        var ended = ApplyOk(state, new EndTurn(0));

        GameEvent[] expected =
        [
            new PhaseChanged(0, GamePhase.Attack, GamePhase.Card),
            new TurnEnded(0),
            new TurnStarted(1, round, GamePhase.Reinforcement),
            PoolOf(1, armies: 8), // seat 1 owns 17 countries: 17 / 2 = 8
        ];
        Assert.Equal(expected, ended.Events.ToArray());
        Assert.Equal(Pool(8), ended.State.Turn);
        Assert.Equal(GamePhase.Reinforcement, ended.State.Phase);
        Assert.Equal(round, ended.State.Round);
        Assert.False(ended.State.IsHostilitiesRound);
    }

    [Fact]
    public void WhenTheRotationReturnsToTheFirstPlayer_TheRoundAdvancesToTwo_AndTheTurnOpensWithReinforce()
    {
        var state = InPlay(3, GamePhase.Regroup, round: 1, activeSeat: 2);

        var ended = ApplyOk(state, new EndTurn(2));

        GameEvent[] expected =
        [
            new PhaseChanged(2, GamePhase.Regroup, GamePhase.Card),
            new TurnEnded(2),
            new RoundAdvanced(2),
            new TurnStarted(0, Round: 2, GamePhase.Reinforcement),
            PoolOf(0, armies: 8), // seat 0 owns 17 countries
        ];
        Assert.Equal(expected, ended.Events.ToArray());
        Assert.Equal((0, 2, GamePhase.Reinforcement), (ended.State.ActiveSeat, ended.State.Round, ended.State.Phase));
        Assert.False(ended.State.IsHostilitiesRound);
    }

    [Fact]
    public void EndTurn_ChangesOnlyTheTurnFields_AndTheVersion()
    {
        var state = InPlay(3, GamePhase.Attack, round: 2, activeSeat: 0);

        var next = ApplyOk(state, new EndTurn(0)).State;

        Assert.Equal(
            state with { ActiveSeat = 1, Phase = GamePhase.Reinforcement, Version = 1, Turn = Pool(8) },
            next);
    }

    // ---- rotation -----------------------------------------------------------------------------------

    [Fact]
    public void ThreeParticipants_TheActivePlayerRotatesABCA_AndTheRoundGrowsEachTimeItReturnsToA()
    {
        var state = InPlay(3, GamePhase.Attack, round: 1, activeSeat: 0);

        var visited = Walk(state, turns: 9);

        Assert.Equal(
            [(0, 1), (1, 1), (2, 1), (0, 2), (1, 2), (2, 2), (0, 3), (1, 3), (2, 3), (0, 4)],
            visited);
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 0)]
    [InlineData(4, 2)]
    [InlineData(4, 3)]
    public void TheRotationFollowsTableOrderFromTheFirstPlayer_AndTheRoundGrowsOnlyOnReturningToIt(
        int participants,
        int firstSeat)
    {
        var state = InPlay(participants, GamePhase.Attack, round: 1, activeSeat: firstSeat, firstSeat: firstSeat);

        var visited = Walk(state, turns: participants * 3);

        var expected = Enumerable.Range(0, participants * 3 + 1)
            .Select(turn => (Seat: (firstSeat + turn) % participants, Round: 1 + turn / participants))
            .ToArray();
        Assert.Equal(expected, visited);
    }

    // ---- eliminated participants ------------------------------------------------------------------------

    [Fact]
    public void AnEliminatedParticipant_IsSkipped_SoThreePlayersWithOneEliminatedRotateACA()
    {
        var state = InPlay(3, GamePhase.Attack, round: 1, activeSeat: 0, eliminated: [1]);

        var first = ApplyOk(state, new EndTurn(0));

        GameEvent[] expectedFirst =
        [
            new PhaseChanged(0, GamePhase.Attack, GamePhase.Card),
            new TurnEnded(0),
            new TurnStarted(2, Round: 1, GamePhase.Attack), // B is skipped: C is next
        ];
        Assert.Equal(expectedFirst, first.Events.ToArray());

        var second = ApplyOk(first.State, new EndTurn(2));

        GameEvent[] expectedSecond =
        [
            new PhaseChanged(2, GamePhase.Attack, GamePhase.Card),
            new TurnEnded(2),
            new RoundAdvanced(2), // the round ends with C: nobody waits for B
            new TurnStarted(0, Round: 2, GamePhase.Reinforcement),
            PoolOf(0, armies: 12), // A and C share the board: 25 countries each
        ];
        Assert.Equal(expectedSecond, second.Events.ToArray());
    }

    [Fact]
    public void AnEliminatedParticipant_IsNeverCounted_ForTurnsNorRounds_AcrossManyRounds()
    {
        var state = InPlay(3, GamePhase.Attack, round: 1, activeSeat: 0, eliminated: [1]);

        var visited = Walk(state, turns: 8);

        Assert.Equal([(0, 1), (2, 1), (0, 2), (2, 2), (0, 3), (2, 3), (0, 4), (2, 4), (0, 5)], visited);
        Assert.DoesNotContain(visited, turn => turn.Seat == 1);
    }

    [Fact]
    public void WhenTheFirstPlayerIsEliminated_TheRoundStillGrowsWhenTheRotationWrapsAround()
    {
        var state = InPlay(3, GamePhase.Attack, round: 1, activeSeat: 1, firstSeat: 0, eliminated: [0]);

        var visited = Walk(state, turns: 4);

        Assert.Equal([(1, 1), (2, 1), (1, 2), (2, 2), (1, 3)], visited);
    }

    [Fact]
    public void WhenTheLastSeatsInTableOrderAreEliminated_TheTurnWrapsStraightToTheFirstPlayer()
    {
        var state = InPlay(4, GamePhase.Attack, round: 1, activeSeat: 2, eliminated: [3]);

        var next = ApplyOk(state, new EndTurn(2)).State;

        Assert.Equal((0, 2), (next.ActiveSeat, next.Round));
    }

    [Fact]
    public void SeveralEliminatedParticipants_AreAllSkipped()
    {
        var state = InPlay(4, GamePhase.Attack, round: 1, activeSeat: 0, eliminated: [1, 2]);

        var visited = Walk(state, turns: 4);

        Assert.Equal([(0, 1), (3, 1), (0, 2), (3, 2), (0, 3)], visited);
    }

    // ---- irreversible phases and version ----------------------------------------------------------------------

    [Fact]
    public void PhasesAreIrreversible_OnceInRegroup_ItIsNotPossibleToAttackAgain()
    {
        AssertRejected(
            RuleViolations.WrongPhase,
            () => ApplyOk(TinyBoard.State(GamePhase.Attack), new StartRegroup(0), TinyBoard.Map).State,
            _ => new Attack(0, "a", "c"), // would be a valid attack in the Attack phase
            TinyBoard.Map);
    }

    [Fact]
    public void PhasesAreIrreversible_RegroupCannotBeStartedTwice_NorReopenedFromRegroup()
    {
        AssertRejected(
            RuleViolations.WrongPhase,
            () => ApplyOk(TinyBoard.State(GamePhase.Attack), new StartRegroup(0), TinyBoard.Map).State,
            _ => new StartRegroup(0),
            TinyBoard.Map);
    }

    [Fact]
    public void Version_GrowsByExactlyOnePerAppliedTurnCommand()
    {
        var state = InPlay(3, GamePhase.Attack, round: 1, activeSeat: 0);
        Assert.Equal(0, state.Version);

        state = ApplyOk(state, new StartRegroup(0)).State;
        Assert.Equal(1, state.Version);

        state = ApplyOk(state, new EndTurn(0)).State;
        Assert.Equal(2, state.Version);

        state = ApplyOk(state, new EndTurn(1)).State;
        Assert.Equal(3, state.Version);
    }
}
