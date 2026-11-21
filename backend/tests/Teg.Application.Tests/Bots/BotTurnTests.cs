using Teg.Application.Bots;
using Teg.Application.Tests.Support;
using Teg.Application.UseCases;
using Teg.Domain;
using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Map;
using Teg.Domain.Rng;
using Teg.Domain.Rules;
using Teg.Domain.State;
using static Teg.Application.Tests.Support.Games;

namespace Teg.Application.Tests.Bots;

/// <summary>
/// The <c>BotTurn</c> use case (B1-B4): ONE bot step per call, through the same submit-action path a human uses, committed on its own;
/// refused when the active participant is a human or the game is over; capped at 60 actions per turn, with the count in the saved
/// state; resumed from whatever was last committed.
/// </summary>
public class BotTurnTests
{
    private readonly InMemoryGameRepository _repository = new();

    private BotTurn NewBotTurn(IBotPolicy? policy = null) =>
        new(_repository, new SubmitAction(_repository, Map), policy ?? new HeuristicBotPolicy(Map), Map);

    private async Task<(Guid Id, GameState State)> GameAt(GameState state) => (await _repository.AddAsync(state), state);

    /// <summary>
    /// The bot owns china with 400 armies facing 49 countries of 30 armies each: every attack from china is favourable, and taking
    /// a country costs it dozens of attacks, so favourable attacks do not run out within a turn.
    /// </summary>
    private static GameState Overwhelming() => Position(GamePhase.Attack, restArmies: 30, ("china", 400));

    /// <summary>The same china with 400 armies facing 49 countries of ONE army each: an attack with the right dice takes the country at once.</summary>
    private static GameState Walkover() => Position(GamePhase.Attack, restArmies: 1, ("china", 400));

    private static BotTurnRequest Step(Guid id, int? expectedVersion = null) => new(id, expectedVersion);

    /// <summary>Steps until the turn passes to somebody else, checking that the saved count follows every step; returns what the bot did.</summary>
    private async Task<List<BotStepResult>> PlayTurn(BotTurn bot, Guid id, int maxSteps = 300)
    {
        var seat = _repository.State(id).ActiveSeat;
        var alreadyDone = _repository.State(id).Turn.BotActions;
        var steps = new List<BotStepResult>();

        while (steps.Count < maxSteps && _repository.State(id).ActiveSeat == seat && _repository.State(id).Phase != GamePhase.Finished)
        {
            var result = await bot.ExecuteAsync(Step(id));
            Assert.True(result.IsSuccess, result.IsSuccess ? null : $"{result.Error.Code}: {result.Error.Message}");
            steps.Add(result.Value);

            var saved = _repository.State(id);
            if (saved.ActiveSeat == seat)
            {
                Assert.Equal(alreadyDone + steps.Count, saved.Turn.BotActions); // the saved count follows every step
            }
        }

        return steps;
    }

    /// <summary>The same position with a seed under which this attack takes the country (the one-army defender loses its only army).</summary>
    private static GameState WithConqueringDice(GameState state, Attack attack)
    {
        for (var seed = 1L; seed <= 500; seed++)
        {
            var candidate = state with { Rng = new RngState(seed, 0) };
            var attacked = GameEngine.Apply(candidate, attack, Map);
            if (attacked.IsSuccess && attacked.Value.State.Turn.PendingConquest is not null)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("No seed conquers.");
    }

    // ---- one step ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task B3_ACall_AppliesExactlyOneCommand_ThroughTheSubmitPath_AndCommitsItAlone()
    {
        var position = SetArmies(Position(GamePhase.Attack, restArmies: 20, ("francia", 10)), ("espana", 1));
        var (id, _) = await GameAt(position);
        var seat = position.ActiveSeat;
        var expectedCommand = new Attack(seat, "francia", "espana");
        var expected = GameEngine.Apply(position, expectedCommand, Map).Value;

        var result = await NewBotTurn().ExecuteAsync(Step(id));

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedCommand, result.Value.Command);
        Assert.Equal(expected.Events, result.Value.Events);
        Assert.Equal(position.Version + 1, result.Value.Game.Version);

        var (_, change) = Assert.Single(_repository.SaveCalls); // one call, one commit: the same one a human's action makes
        Assert.Equal(position.Version, change.ExpectedVersion);
        Assert.Equal(BotCommandIds.For(id, position.Version), change.CommandId);
        Assert.Equal(seat, change.ActorSeat);
        Assert.Equal(expected.Events, change.Events);
        Assert.Equal(expected.State with { Turn = expected.State.Turn with { BotActions = 1 } }, _repository.State(id));
        Assert.Equal(expected.Events, _repository.Stored(id).Select(e => e.Event));
    }

    [Fact]
    public async Task B3_EachCall_IsOneMoreStep_AndTheVersionGrowsByOne()
    {
        var (id, state) = await GameAt(Overwhelming());
        var bot = NewBotTurn();

        for (var step = 1; step <= 4; step++)
        {
            var result = await bot.ExecuteAsync(Step(id));

            Assert.True(result.IsSuccess);
            Assert.Equal(state.Version + step, result.Value.Game.Version);
            Assert.Equal(step, _repository.SaveCalls.Count);
        }
    }

    [Fact]
    public async Task B1_ABotThatProposesWhatTheEngineRefuses_IsRefusedLikeAnyAction_AndNothingIsSaved()
    {
        var (id, state) = await GameAt(Overwhelming());
        var theirs = state.Territories.First(t => t.Owner != state.ActiveSeat).Id;
        var cheating = new ScriptedPolicy((s, _) => new Attack(s.ActiveSeat, theirs, theirs)); // attacks from a country it does not own

        var result = await NewBotTurn(cheating).ExecuteAsync(Step(id));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.RuleViolation, result.Error.Kind);
        Assert.Equal(RuleViolations.OriginNotOwned, result.Error.Code);
        Assert.Empty(_repository.SaveCalls);
        Assert.Equal(state, _repository.State(id));
    }

    [Fact]
    public async Task B1_TheBot_ActsOnlyForTheActiveSeat()
    {
        var (id, state) = await GameAt(Overwhelming());
        var seen = new List<int>();
        var policy = new ScriptedPolicy((s, legal) =>
        {
            seen.Add(s.ActiveSeat);
            return new HeuristicBotPolicy(Map).Next(s, legal);
        });

        var result = await NewBotTurn(policy).ExecuteAsync(Step(id));

        Assert.Equal([state.ActiveSeat], seen);
        Assert.Equal(state.ActiveSeat, result.Value.Command.Seat);
    }

    // ---- who may be asked (B4) ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task B4_WhenTheActiveParticipantIsAHuman_TheBotIsRefused_AndTheStateIsUnchanged()
    {
        var position = Overwhelming();
        var human = WithKinds(position, Enumerable.Range(0, 2).Select(seat => seat == position.ActiveSeat ? H : B).ToArray());
        var (id, _) = await GameAt(human);

        var result = await NewBotTurn().ExecuteAsync(Step(id));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.NotBotTurn, result.Error.Kind);
        Assert.Equal(ErrorCodes.NotBotTurn, result.Error.Type);
        Assert.Equal(ErrorCodes.NotBotTurn, result.Error.Code);
        Assert.Empty(_repository.SaveCalls);
        Assert.Equal(human, _repository.State(id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task B4_ABotThatWonTheGame_AndAHumanThatWonIt_BothGetGameFinished(bool botWon)
    {
        var over = AllBots(Deal(3, H, H)) with { Phase = GamePhase.Finished, Winner = 0, WinReason = WinReason.Elimination };
        var (id, _) = await GameAt(botWon ? over : WithKinds(over, H, H));

        var result = await NewBotTurn().ExecuteAsync(Step(id));

        Assert.Equal(ErrorKind.RuleViolation, result.Error.Kind);
        Assert.Equal(RuleViolations.GameFinished, result.Error.Code);
        Assert.Empty(_repository.SaveCalls);
        Assert.Equal(GamePhase.Finished, _repository.State(id).Phase);
    }

    [Fact]
    public async Task B4_AnUnknownGame_IsNotFound()
    {
        var result = await NewBotTurn().ExecuteAsync(Step(Guid.NewGuid()));

        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
        Assert.Empty(_repository.SaveCalls);
    }

    [Fact]
    public async Task B3_ARequestMadeAgainstAStaleVersion_IsAVersionConflict_AndTheBotDoesNotAct()
    {
        var (id, state) = await GameAt(Overwhelming());
        var bot = NewBotTurn();

        var stale = await bot.ExecuteAsync(Step(id, expectedVersion: state.Version - 1));
        var current = await bot.ExecuteAsync(Step(id, expectedVersion: state.Version));
        var late = await bot.ExecuteAsync(Step(id, expectedVersion: state.Version)); // the version moved on with the step above

        Assert.Equal(ErrorKind.VersionConflict, stale.Error.Kind);
        Assert.True(current.IsSuccess);
        Assert.Equal(ErrorKind.VersionConflict, late.Error.Kind);
        Assert.Single(_repository.SaveCalls);
    }

    // ---- a step can be retried: its id is derived from the game and the version -------------------------------------------------

    [Fact]
    public void B3_TheCommandIdOfAStep_IsDeterministic_AndDiffersByGameAndByVersion()
    {
        var game = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var other = Guid.Parse("11111111-2222-3333-4444-555555555556");

        Assert.Equal(BotCommandIds.For(game, 7), BotCommandIds.For(game, 7));
        Assert.NotEqual(BotCommandIds.For(game, 7), BotCommandIds.For(game, 8));
        Assert.NotEqual(BotCommandIds.For(game, 7), BotCommandIds.For(other, 7));
        Assert.NotEqual(Guid.Empty, BotCommandIds.For(game, 0));
        Assert.Equal(200, Enumerable.Range(0, 200).Select(version => BotCommandIds.For(game, version)).Distinct().Count());
    }

    [Fact]
    public async Task B3_TwoCallsThatLoadedTheSameVersion_ApplyTheStepOnce_TheSecondIsAlreadyApplied()
    {
        var (id, state) = await GameAt(Overwhelming());
        var bot = NewBotTurn();
        Result<BotStepResult, UseCaseError> inner = default;
        _repository.BeforeSave = (_, _) =>
        {
            _repository.BeforeSave = null; // the other request gets in first: it loaded the same version, so it derives the same id
            inner = bot.ExecuteAsync(Step(id)).GetAwaiter().GetResult();
        };

        var outer = await bot.ExecuteAsync(Step(id));

        Assert.True(inner.IsSuccess);
        Assert.False(outer.IsSuccess);
        Assert.Equal(ErrorKind.AlreadyApplied, outer.Error.Kind);
        Assert.Equal(state.Version + 1, _repository.State(id).Version); // the step happened once
        Assert.Equal(inner.Value.Events, _repository.Stored(id).Select(e => e.Event));
        Assert.Equal(1, _repository.State(id).Turn.BotActions);
    }

    [Fact]
    public async Task B3_AStepRetriedAfterTheStoreFailed_IsTheSameStep_WithTheSameId()
    {
        var (id, state) = await GameAt(Overwhelming());
        var bot = NewBotTurn();
        _repository.FailNextSave = new InvalidOperationException("the process died mid-commit");

        await Assert.ThrowsAsync<InvalidOperationException>(() => bot.ExecuteAsync(Step(id)));
        Assert.Equal(state, _repository.State(id)); // nothing was committed

        var retry = await bot.ExecuteAsync(Step(id));

        Assert.True(retry.IsSuccess);
        Assert.Equal(2, _repository.SaveCalls.Count);
        Assert.Equal(_repository.SaveCalls[0].Change.CommandId, _repository.SaveCalls[1].Change.CommandId);
        Assert.Equal(_repository.SaveCalls[0].Change.NewState, _repository.SaveCalls[1].Change.NewState); // the same decision, the same dice
        Assert.Equal(state.Version + 1, _repository.State(id).Version);
    }

    // ---- what the bot plays -----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task B1_TheBotPlaysTheInitialPlacementToo_UntilTheHostilitiesBegin(int participants)
    {
        var (id, dealt) = await GameAt(AllBots(Deal(9, Enumerable.Repeat(H, participants).ToArray())));
        var bot = NewBotTurn();
        var turns = participants * 2; // two rounds, one command each

        for (var turn = 0; turn < turns; turn++)
        {
            var result = await bot.ExecuteAsync(Step(id));

            Assert.True(result.IsSuccess);
            Assert.IsType<PlaceInitialArmies>(result.Value.Command);
        }

        var after = _repository.State(id);
        Assert.Equal(GamePhase.Attack, after.Phase);
        Assert.Equal(dealt.Version + turns, after.Version);
        Assert.Equal(0, after.Turn.BotActions); // placing is one command per turn: nothing counted
        Assert.Equal(dealt.Territories.Sum(t => t.Armies) + participants * (participants == 2 ? 12 : 8), after.Territories.Sum(t => t.Armies));
    }

    [Fact]
    public async Task B1_AMandatoryTrade_IsMadeBeforeAnythingIsPlaced()
    {
        var cards = new[]
        {
            OfSymbol(CardSymbol.Galeon, 3), OfSymbol(CardSymbol.Canon, 1), OfSymbol(CardSymbol.Globo, 1),
        }.SelectMany(ids => ids).ToArray();
        var position = Position(GamePhase.Attack, restArmies: 20, ("francia", 3), ("sahara", 6));
        var reinforcing = WithHand(
            position with { Phase = GamePhase.Reinforcement, Turn = new TurnContext(5, ValueArray<ContinentPool>.Empty) },
            position.ActiveSeat,
            cards.Select(id => new HeldCard(id)).ToArray());
        var (id, _) = await GameAt(reinforcing);
        var bot = NewBotTurn();

        var first = await bot.ExecuteAsync(Step(id));
        var afterTrade = _repository.State(id);
        var second = await bot.ExecuteAsync(Step(id));

        Assert.IsType<TradeCards>(first.Value.Command);
        Assert.Equal(2, afterTrade.Participants[position.ActiveSeat].Cards.Count);
        Assert.Equal(1, afterTrade.Participants[position.ActiveSeat].TradesDone);
        Assert.Equal(5 + 4, afterTrade.Turn.PendingArmies); // the first trade is worth 4
        Assert.IsType<PlaceReinforcements>(second.Value.Command);
        Assert.Equal(GamePhase.Attack, _repository.State(id).Phase); // the whole pool, trade included, went out in the placement
        Assert.Equal(2, _repository.State(id).Turn.BotActions);
    }

    private static string[] OfSymbol(CardSymbol symbol, int count) =>
        Map.Territories.Where(t => t.Symbol == symbol).Take(count).Select(t => t.Id).ToArray();

    // ---- the cap (B2) -----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task B2_WithEndlessLegalAttacks_TheTurnStopsAtTheCap_AndEndsWithEndTurn()
    {
        var (id, state) = await GameAt(Overwhelming());
        var seat = state.ActiveSeat;

        var steps = await PlayTurn(NewBotTurn(), id);

        Assert.InRange(steps.Count, 60, 60 + 2); // the cap, plus the occupation and EndTurn that may be left to close
        Assert.IsType<EndTurn>(steps[^1].Command);
        Assert.NotEqual(seat, _repository.State(id).ActiveSeat);
        Assert.Equal(0, _repository.State(id).Turn.BotActions); // the next turn starts from zero
        Assert.Equal(1, steps.Count(s => s.Command is EndTurn)); // and nothing but the last step ended it
        Assert.True(steps.Count(s => s.Command is Attack) >= 20); // the bot was really attacking all along
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(12)]
    public async Task B2_ALowerCap_StopsEarlier_TheSameWay(int cap)
    {
        var (id, _) = await GameAt(Overwhelming());

        var steps = await PlayTurn(NewBotTurn(new HeuristicBotPolicy(Map, cap)), id);

        Assert.InRange(steps.Count, cap, cap + 2);
        Assert.IsType<EndTurn>(steps[^1].Command);
        Assert.NotEqual(Overwhelming().ActiveSeat, _repository.State(id).ActiveSeat);
    }

    [Fact]
    public async Task B2_WhenTheCapFallsInTheMiddleOfAConquest_TheOccupationIsFinishedBeforeTheTurnEnds()
    {
        // Cap 1: the first attack (dice chosen so that it conquers) reaches it, the occupation is still decided (with the minimum:
        // the bot is winding down) and only then the turn ends. A conquest is never left half done.
        var policy = new HeuristicBotPolicy(Map, maxActionsPerTurn: 1);
        var position = Walkover();
        var firstAttack = (Attack)policy.Next(position, LegalMoves.For(position, Map));
        var (id, _) = await GameAt(WithConqueringDice(position, firstAttack));

        var steps = await PlayTurn(NewBotTurn(policy), id);

        Assert.Equal(["Attack", "MoveAfterConquest", "EndTurn"], steps.Select(s => s.Command.GetType().Name));
        Assert.Equal(1, ((MoveAfterConquest)steps[1].Command).Armies);
    }

    // ---- resume (B3) ------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task B3_AfterThreeBotActions_AFreshInstanceContinuesAtTheFourth_WithTheCountIntact()
    {
        var (id, _) = await GameAt(Overwhelming());
        for (var step = 1; step <= 3; step++)
        {
            Assert.True((await NewBotTurn().ExecuteAsync(Step(id))).IsSuccess);
        }

        var crashed = _repository.State(id);
        Assert.Equal(3, crashed.Turn.BotActions);
        Assert.Equal(crashed.ActiveSeat, Overwhelming().ActiveSeat); // still the same bot's turn: it was interrupted mid-way

        // "A new process": new use-case instances over what the store holds; only the store carries over.
        var resumed = await NewBotTurn().ExecuteAsync(Step(id));

        Assert.True(resumed.IsSuccess);
        Assert.Equal(4, _repository.State(id).Turn.BotActions); // action four, not action one
        Assert.Equal(crashed.Version + 1, _repository.State(id).Version);
        var commandIds = _repository.SaveCalls.Select(call => call.Change.CommandId).ToArray();
        Assert.Equal(4, commandIds.Distinct().Count()); // no step was repeated
        Assert.Equal(Enumerable.Range(crashed.Version - 3, 4).Select(version => BotCommandIds.For(id, version)), commandIds);
        Assert.Equal(Enumerable.Range(1, _repository.Stored(id).Count), _repository.Stored(id).Select(e => e.Seq)); // the history has no gaps or repeats
    }

    [Fact]
    public async Task B3_AResumedTurn_StillStopsAtTheCapOfTheWholeTurn_NotOfTheSession()
    {
        var (id, state) = await GameAt(Overwhelming());
        var withCount = state with { Turn = state.Turn with { BotActions = 58 } }; // 58 were done before the "restart"
        var (resumedId, _) = await GameAt(withCount);

        var steps = await PlayTurn(NewBotTurn(), resumedId);

        Assert.InRange(steps.Count, 3, 4); // the 2 actions left up to 60, then the occupation that may be pending, then EndTurn
        Assert.IsType<EndTurn>(steps[^1].Command);
        Assert.Equal(state, _repository.State(id)); // the other game never moved
    }
}
