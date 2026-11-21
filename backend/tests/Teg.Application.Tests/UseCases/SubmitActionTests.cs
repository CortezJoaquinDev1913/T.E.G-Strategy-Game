using Teg.Application.Ports;
using Teg.Application.Tests.Support;
using Teg.Application.UseCases;
using Teg.Application.Views;
using Teg.Domain;
using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Events;
using Teg.Domain.Rules;
using Teg.Domain.State;
using static Teg.Application.Tests.Support.Games;

namespace Teg.Application.Tests.UseCases;

/// <summary>
/// A2 and A3 at the application level: an action carries the version it was made against and an id of its own; a stale version or an
/// id already applied is a conflict that changes nothing, a rule violation carries the engine's code and changes nothing, and an
/// accepted action saves the new state, its events and its id together, in ONE repository call.
/// </summary>
public class SubmitActionTests
{
    private readonly InMemoryGameRepository _repository = new();
    private readonly SubmitAction _submit;

    public SubmitActionTests()
    {
        _submit = new SubmitAction(_repository, Map);
    }

    private async Task<(Guid Id, GameState State)> GameAt(GameState state)
    {
        var id = await _repository.AddAsync(state);

        return (id, state);
    }

    private Task<Result<SubmitActionResult, UseCaseError>> Submit(Guid id, int expectedVersion, Guid commandId, GameCommand command) =>
        _submit.ExecuteAsync(new SubmitActionRequest(id, expectedVersion, commandId, command));

    [Fact]
    public async Task A2_AnAcceptedAction_SavesTheNewStateTheEventsAndTheCommandId_InOneCall()
    {
        var (id, start) = await GameAt(Deal(3, H, H));
        var command = WholePool(start);
        var expected = GameEngine.Apply(start, command, Map).Value;
        var commandId = Guid.NewGuid();

        var result = await Submit(id, 0, commandId, command);

        Assert.True(result.IsSuccess);
        var (_, change) = Assert.Single(_repository.SaveCalls);
        Assert.Equal(0, change.ExpectedVersion);
        Assert.Equal(commandId, change.CommandId);
        Assert.Equal(command.Seat, change.ActorSeat);
        Assert.Equal(expected.State, change.NewState);
        Assert.Equal(expected.Events, change.Events);
        Assert.Equal(expected.State, _repository.State(id));
        Assert.Equal(expected.Events, _repository.Stored(id).Select(e => e.Event));
        Assert.True(_repository.Applied(id, commandId));

        Assert.Equal(1, result.Value.Game.Version);
        Assert.Equal(id, result.Value.Game.Id);
        Assert.Equal(expected.State.ActiveSeat, result.Value.Game.ActiveSeat);
        Assert.Equal(expected.Events, result.Value.Events);
    }

    [Fact]
    public async Task A2_SeveralActions_AdvanceTheVersionOneByOne_AndTheHistoryKeepsGrowing()
    {
        var (id, state) = await GameAt(Deal(3, H, H));
        var events = 0;

        for (var version = 0; version < 4; version++)
        {
            var command = WholePool(state);
            var expected = GameEngine.Apply(state, command, Map).Value;

            var result = await Submit(id, version, Guid.NewGuid(), command);

            Assert.True(result.IsSuccess);
            Assert.Equal(version + 1, result.Value.Game.Version);
            state = expected.State;
            events += expected.Events.Count;
            Assert.Equal(state, _repository.State(id));
            Assert.Equal(events, _repository.Stored(id).Count);
        }

        Assert.Equal(GamePhase.Attack, state.Phase); // two placement rounds of two participants: the hostilities have begun
        Assert.Equal(Enumerable.Range(1, events), _repository.Stored(id).Select(e => e.Seq));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task A2_AStaleOrFutureExpectedVersion_IsAVersionConflict_AndNothingIsSaved(int expectedVersion)
    {
        var (id, start) = await GameAt(Deal(3, H, H));

        var result = await Submit(id, expectedVersion, Guid.NewGuid(), WholePool(start));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.VersionConflict, result.Error.Kind);
        Assert.Equal(ErrorCodes.VersionConflict, result.Error.Type);
        Assert.Equal(ErrorCodes.VersionConflict, result.Error.Code);
        Assert.Empty(_repository.SaveCalls);
        Assert.Equal(start, _repository.State(id));
        Assert.Empty(_repository.Stored(id));
    }

    [Fact]
    public async Task A2_TwoSubmitsAgainstTheSameVersion_TheFirstWins_TheSecondIsAVersionConflict()
    {
        var (id, state) = await GameAt(Deal(3, H, H));
        for (var version = 0; version < 4; version++)
        {
            Assert.True((await Submit(id, version, Guid.NewGuid(), WholePool(state))).IsSuccess);
            state = Advance(state, WholePool(state));
        }

        Assert.Equal(4, _repository.State(id).Version);
        var attack = new StartRegroup(state.ActiveSeat);

        var first = await Submit(id, 4, Guid.NewGuid(), attack);
        var second = await Submit(id, 4, Guid.NewGuid(), new EndTurn(state.ActiveSeat));

        Assert.True(first.IsSuccess);
        Assert.Equal(5, first.Value.Game.Version);
        Assert.False(second.IsSuccess);
        Assert.Equal(ErrorKind.VersionConflict, second.Error.Kind); // not a rule violation: the version is judged first
        Assert.Equal(5, _repository.State(id).Version);
        Assert.Equal(GamePhase.Regroup, _repository.State(id).Phase);
    }

    [Theory]
    [InlineData(1)] // the version the game has now
    [InlineData(0)] // the version it was made against: still an id already applied, not a stale version
    public async Task A2_ACommandIdAlreadyApplied_IsAlreadyApplied_AndTheStateIsNotChangedAgain(int expectedVersion)
    {
        var (id, start) = await GameAt(Deal(3, H, H));
        var command = WholePool(start);
        var commandId = Guid.NewGuid();
        Assert.True((await Submit(id, 0, commandId, command)).IsSuccess);
        var afterFirst = _repository.State(id);

        var again = await Submit(id, expectedVersion, commandId, command);

        Assert.False(again.IsSuccess);
        Assert.Equal(ErrorKind.AlreadyApplied, again.Error.Kind);
        Assert.Equal(ErrorCodes.AlreadyApplied, again.Error.Type);
        Assert.Equal(ErrorCodes.AlreadyApplied, again.Error.Code);
        Assert.Single(_repository.SaveCalls);
        Assert.Equal(afterFirst, _repository.State(id));
        Assert.Equal(1, _repository.State(id).Version);
    }

    public static IEnumerable<object[]> RuleViolationCases()
    {
        yield return ["wrong seat", new Func<GameState, GameCommand>(s => ((PlaceInitialArmies)WholePool(s)) with { Seat = 1 - s.ActiveSeat }), RuleViolations.NotActiveParticipant];
        yield return ["wrong phase", new Func<GameState, GameCommand>(s => new EndTurn(s.ActiveSeat)), RuleViolations.WrongPhase];
        yield return ["wrong total", new Func<GameState, GameCommand>(s => ((PlaceInitialArmies)WholePool(s)) with { Placements = new[] { new ArmyPlacement(Games.OwnedBy(s, s.ActiveSeat).First(), 1) }.ToValueArray() }), RuleViolations.WrongPlacementTotal];
        yield return ["not owned", new Func<GameState, GameCommand>(s => ((PlaceInitialArmies)WholePool(s)) with { Placements = new[] { new ArmyPlacement(Games.OwnedBy(s, 1 - s.ActiveSeat).First(), 6) }.ToValueArray() }), RuleViolations.TerritoryNotOwned];
    }

    [Theory]
    [MemberData(nameof(RuleViolationCases))]
    public async Task A4_ARuleViolation_CarriesTheEngineCode_AndNothingIsSaved(string why, Func<GameState, GameCommand> build, string code)
    {
        var (id, start) = await GameAt(Deal(3, H, H));
        var commandId = Guid.NewGuid();

        var result = await Submit(id, 0, commandId, build(start));

        Assert.False(result.IsSuccess, why);
        Assert.Equal(ErrorKind.RuleViolation, result.Error.Kind);
        Assert.Equal(ErrorCodes.RuleViolation, result.Error.Type);
        Assert.Equal(code, result.Error.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Error.Message));
        Assert.Empty(_repository.SaveCalls);
        Assert.Equal(start, _repository.State(id));
        Assert.False(_repository.Applied(id, commandId));

        // A refused command does not burn its id: the same id works for the correction.
        Assert.True((await Submit(id, 0, commandId, WholePool(start))).IsSuccess);
    }

    [Fact]
    public async Task A4_AnUnknownGame_IsNotFound_AndNothingIsSaved()
    {
        var result = await Submit(Guid.NewGuid(), 0, Guid.NewGuid(), new EndTurn(0));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
        Assert.Equal(ErrorCodes.NotFound, result.Error.Type);
        Assert.Equal(ErrorCodes.NotFound, result.Error.Code);
        Assert.Empty(_repository.SaveCalls);
    }

    [Fact]
    public async Task A3_AGameThatIsFinished_RejectsEveryAction_WithGameFinished()
    {
        var finished = Deal(3, H, H) with { Phase = GamePhase.Finished, Winner = 0, WinReason = WinReason.Elimination };
        var (id, _) = await GameAt(finished);

        var result = await Submit(id, 0, Guid.NewGuid(), new EndTurn(finished.ActiveSeat));

        Assert.Equal(ErrorKind.RuleViolation, result.Error.Kind);
        Assert.Equal(RuleViolations.GameFinished, result.Error.Code);
        Assert.Empty(_repository.SaveCalls);
    }

    [Fact]
    public async Task A3_AConcurrentWriterThatGetsInFirst_MakesThisActionAVersionConflict()
    {
        var (id, start) = await GameAt(Deal(3, H, H));
        var theirs = WholePool(start, country: 1);
        var theirChange = GameEngine.Apply(start, theirs, Map).Value;
        _repository.BeforeSave = (gameId, _) =>
        {
            _repository.BeforeSave = null;
            var outcome = _repository
                .SaveAsync(gameId, new GameChange(0, theirChange.State, theirChange.Events, Guid.NewGuid(), start.ActiveSeat))
                .GetAwaiter().GetResult();
            Assert.Equal(SaveOutcome.Saved, outcome);
        };

        var mine = await Submit(id, 0, Guid.NewGuid(), WholePool(start, country: 0));

        Assert.False(mine.IsSuccess);
        Assert.Equal(ErrorKind.VersionConflict, mine.Error.Kind);
        Assert.Equal(theirChange.State, _repository.State(id)); // only the writer that got in first is saved
        Assert.Equal(theirChange.Events, _repository.Stored(id).Select(e => e.Event));
    }

    [Fact]
    public async Task A3_TheSameCommandAppliedByAConcurrentRequest_IsAlreadyApplied()
    {
        var (id, start) = await GameAt(Deal(3, H, H));
        var command = WholePool(start);
        var commandId = Guid.NewGuid();
        var applied = GameEngine.Apply(start, command, Map).Value;
        _repository.BeforeSave = (gameId, _) =>
        {
            _repository.BeforeSave = null;
            _repository.SaveAsync(gameId, new GameChange(0, applied.State, applied.Events, commandId, start.ActiveSeat)).GetAwaiter().GetResult();
        };

        var late = await Submit(id, 0, commandId, command);

        Assert.False(late.IsSuccess);
        Assert.Equal(ErrorKind.AlreadyApplied, late.Error.Kind);
        Assert.Equal(1, _repository.State(id).Version);
        Assert.Equal(applied.Events.Count, _repository.Stored(id).Count); // the events were saved once, not twice
    }

    [Fact]
    public async Task A3_AFailingSave_IsNotSwallowed_AndLeavesTheStoreUnchanged()
    {
        var (id, start) = await GameAt(Deal(3, H, H));
        var commandId = Guid.NewGuid();
        _repository.FailNextSave = new InvalidOperationException("the database went away");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Submit(id, 0, commandId, WholePool(start)));

        Assert.Equal(start, _repository.State(id)); // version and countries unchanged
        Assert.Empty(_repository.Stored(id));
        Assert.False(_repository.Applied(id, commandId));

        // Nothing was consumed: the retry with the same id and version goes through.
        var retry = await Submit(id, 0, commandId, WholePool(start));
        Assert.True(retry.IsSuccess);
        Assert.Equal(1, _repository.State(id).Version);
    }

    [Fact]
    public async Task A4_TheEventsOfTheResult_AreThePublicOnes_WhileTheStoreKeepsTheFullRecord()
    {
        // Ending a turn with a conquest awards the top card of the deck: who got a card is public, which one is not.
        var position = Position(GamePhase.Attack, restArmies: 20, ("francia", 3)) with
        {
            Deck = new Deck(new[] { "alaska", "yukon" }.ToValueArray(), ValueArray<string>.Empty),
        };
        var counted = position with { Turn = position.Turn with { ConquestsThisTurn = 1 } };
        var (id, _) = await GameAt(counted);

        var result = await Submit(id, counted.Version, Guid.NewGuid(), new EndTurn(counted.ActiveSeat));

        Assert.True(result.IsSuccess);
        Assert.Contains(new CardAwarded(counted.ActiveSeat, PublicEvents.HiddenCard), result.Value.Events);
        Assert.DoesNotContain(result.Value.Events, e => e is CardAwarded { Card: "alaska" });
        Assert.Contains(_repository.Stored(id), stored => stored.Event is CardAwarded { Card: "alaska" });
        Assert.Contains(counted.ActiveSeat, _repository.State(id).Participants.Where(p => p.Cards.Any(c => c.TerritoryId == "alaska")).Select(p => p.Seat));
    }

    // ---- the bot counter (B2): the Application writes it into the saved turn ----------------------------------------------

    private async Task<(Guid Id, GameState State)> InAttack(ParticipantKind activeKind)
    {
        var playing = FinishInitialPlacement(Deal(3, H, H));
        var kinds = Enumerable.Range(0, 2).Select(seat => seat == playing.ActiveSeat ? activeKind : ParticipantKind.Human).ToArray();

        return await GameAt(WithKinds(playing, kinds));
    }

    [Fact]
    public async Task B2_AnActionOfABot_CountsInTheSavedTurn_AndTheCountKeepsGrowing()
    {
        var (_, playing) = await InAttack(ParticipantKind.Bot);
        var seat = playing.ActiveSeat;
        var state = Own(playing, seat, ("francia", 5), ("espana", 1)); // two own countries side by side: a regroup is possible
        var (id, _) = await GameAt(state);
        Assert.Equal(0, state.Turn.BotActions);

        var first = await Submit(id, state.Version, Guid.NewGuid(), new StartRegroup(seat));
        Assert.Equal(1, _repository.State(id).Turn.BotActions);
        Assert.Equal(1, first.Value.Game.Turn.BotActions);

        var second = await Submit(id, state.Version + 1, Guid.NewGuid(), new Regroup(seat, "francia", "espana", 2));
        Assert.True(second.IsSuccess);
        Assert.Equal(2, _repository.State(id).Turn.BotActions);
    }

    [Fact]
    public async Task B2_TheCountContinuesFromWhatIsSaved_NotFromZero()
    {
        var (id, state) = await InAttack(ParticipantKind.Bot);
        var resumed = state with { Turn = state.Turn with { BotActions = 41 } };
        var (resumedId, _) = await GameAt(resumed);

        await Submit(resumedId, resumed.Version, Guid.NewGuid(), new StartRegroup(resumed.ActiveSeat));

        Assert.Equal(42, _repository.State(resumedId).Turn.BotActions);
        Assert.Equal(0, _repository.State(id).Turn.BotActions); // another game: untouched
    }

    [Fact]
    public async Task B2_AnActionOfAHuman_DoesNotCount()
    {
        var (id, state) = await InAttack(ParticipantKind.Human);

        await Submit(id, state.Version, Guid.NewGuid(), new StartRegroup(state.ActiveSeat));

        Assert.Equal(GamePhase.Regroup, _repository.State(id).Phase);
        Assert.Equal(0, _repository.State(id).Turn.BotActions);
    }

    [Fact]
    public async Task B2_WhenABotEndsItsTurn_TheNextTurnStartsFromZero()
    {
        var (id, state) = await InAttack(ParticipantKind.Bot);
        var counted = state with { Turn = state.Turn with { BotActions = 9 } };
        var (countedId, _) = await GameAt(counted);

        var result = await Submit(countedId, counted.Version, Guid.NewGuid(), new EndTurn(counted.ActiveSeat));

        Assert.True(result.IsSuccess);
        var after = _repository.State(countedId);
        Assert.NotEqual(counted.ActiveSeat, after.ActiveSeat);
        Assert.Equal(0, after.Turn.BotActions);
        Assert.Equal(0, result.Value.Game.Turn.BotActions);
        Assert.Equal(0, _repository.State(id).Turn.BotActions);
    }

    [Fact]
    public async Task B2_ABotsInitialPlacement_IsNotCounted_ItIsOneCommandPerTurn()
    {
        var (id, state) = await GameAt(AllBots(Deal(3, H, H)));

        var result = await Submit(id, 0, Guid.NewGuid(), WholePool(state));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, _repository.State(id).Turn.BotActions);
    }

    [Fact]
    public async Task B2_ABotsReinforcementActions_Count()
    {
        var (_, playing) = await InAttack(ParticipantKind.Bot);
        var reinforcing = playing with
        {
            Phase = GamePhase.Reinforcement,
            Round = 2,
            Turn = new TurnContext(5, ValueArray<ContinentPool>.Empty),
        };
        var (id, _) = await GameAt(reinforcing);
        var seat = reinforcing.ActiveSeat;
        var own = Games.OwnedBy(reinforcing, seat).First();

        await Submit(id, reinforcing.Version, Guid.NewGuid(), new PlaceReinforcements(seat, new[] { new ArmyPlacement(own, 2) }.ToValueArray()));
        Assert.Equal(1, _repository.State(id).Turn.BotActions);
        Assert.Equal(GamePhase.Reinforcement, _repository.State(id).Phase);

        await Submit(id, reinforcing.Version + 1, Guid.NewGuid(), new PlaceReinforcements(seat, new[] { new ArmyPlacement(own, 3) }.ToValueArray()));
        Assert.Equal(GamePhase.Attack, _repository.State(id).Phase);
        Assert.Equal(2, _repository.State(id).Turn.BotActions); // the turn went on from Refuerzo to Ataque: still the same turn
    }
}
