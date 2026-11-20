using Teg.Domain.Commands;
using Teg.Domain.Events;
using Teg.Domain.Rng;
using Teg.Domain.Setup;
using Teg.Domain.State;
using Teg.Domain.Tests.Support;
using static Teg.Domain.Tests.Support.GameFixtures;

namespace Teg.Domain.Tests.Replay;

/// <summary>
/// E10, golden replay: the same seed, the same options and the same list of commands give EXACTLY the same game. A whole game is
/// recorded (the command list comes from the seeded random policy, which picks from the legal moves), then replayed on a fresh
/// <c>GameSetup.Create</c> with the same seed and options: after EVERY command the state and the list of events must be equal
/// (structural equality: <c>ValueArray</c> compares by value) to the recorded ones. A different seed is a different game, and a
/// replay that starts from a state restored from its persisted form (the seed and the draws taken) is identical too. Only
/// determinism is pinned: no literal outcome (who wins, how many attacks...), so a harmless rule change cannot break these tests.
/// </summary>
public sealed class ReplayTests
{
    private const int Cap = 600;

    // ---- the replay ------------------------------------------------------------------------------------------------------

    /// <summary>Applies <paramref name="recording"/>'s commands from <paramref name="start"/> (the state before command number <paramref name="from"/>), comparing every step.</summary>
    private static GameState AssertReplaysStepByStep(GameState start, ReplayFixtures.Recording recording, int from = 0)
    {
        var state = start;

        for (var index = from; index < recording.Commands.Count; index++)
        {
            var result = GameEngine.Apply(state, recording.Commands[index], RealMap);

            Assert.True(result.IsSuccess, $"Step {index} ({recording.Commands[index].GetType().Name}) was refused: {(result.IsSuccess ? "" : result.Error.Code)}.");
            Assert.Equal(recording.Transitions[index].Events, result.Value.Events); // the history of this command
            Assert.Equal(recording.Transitions[index].State, result.Value.State); // and the state it leaves
            state = result.Value.State;
        }

        return state;
    }

    [Theory]
    [InlineData(2, 21L)]
    [InlineData(3, 22L)]
    [InlineData(4, 23L)]
    public void E10_TheSameSeedOptionsAndCommands_ReplayTheSameGame_StepByStep(int participants, long seed)
    {
        var options = ReplayFixtures.Options(participants);
        var recording = ReplayFixtures.Record(options, seed, choiceSeed: seed * 7919, Cap);

        // A fresh game, created again from the same seed and options.
        var fresh = ReplayFixtures.Create(options, seed);
        Assert.Equal(recording.Start, fresh);
        var replayed = AssertReplaysStepByStep(fresh, recording);

        Assert.Equal(recording.Final, replayed);
        // The recording was a real game: attacks rolled dice, cards were dealt, turns went round.
        Assert.True(recording.Commands.Count >= 400, $"Only {recording.Commands.Count} commands.");
        Assert.True(recording.Events.OfType<AttackResolved>().Count() > 50);
        Assert.True(recording.Events.OfType<CardAwarded>().Any());
        Assert.True(recording.Events.OfType<TurnEnded>().Count() > 20);
        Assert.True(recording.Final.Rng.Draws > recording.Start.Rng.Draws);
    }

    [Fact]
    public void E10_RunningTheSameCommandsTwice_GivesEqualStatesAndEqualHistories()
    {
        var options = ReplayFixtures.Options(3);
        var recording = ReplayFixtures.Record(options, seed: 31, choiceSeed: 99, Cap);

        var firstRun = ReplayFixtures.Replay(ReplayFixtures.Create(options, 31), recording.Commands);
        var secondRun = ReplayFixtures.Replay(ReplayFixtures.Create(options, 31), recording.Commands);

        Assert.Equal(firstRun.Final, secondRun.Final);
        Assert.Equal(firstRun.Events, secondRun.Events); // the whole history, in order
        Assert.Equal(recording.Events, firstRun.Events);
        Assert.NotEmpty(firstRun.Events);
    }

    [Fact]
    public void E10_AGameThatEndsInVictory_ReplaysToTheSameWinnerAndTheSameLastEvent()
    {
        var options = ReplayFixtures.Options(4);

        // Seat 0 plays to conquer: with four participants the game ends when it holds 30 countries.
        var recording = ReplayFixtures.Record(options, seed: 41, choiceSeed: 41, cap: 1200, conqueror: 0);
        Assert.Equal(GamePhase.Finished, recording.Final.Phase);

        var replayed = AssertReplaysStepByStep(ReplayFixtures.Create(options, 41), recording);

        Assert.Equal(recording.Final, replayed);
        Assert.Equal(recording.Final.Winner, replayed.Winner);
        Assert.Equal(recording.Final.WinReason, replayed.WinReason);
        Assert.IsType<GameWon>(recording.Transitions[^1].Events[^1]);
    }

    // ---- a different seed ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void E10_ADifferentSeed_IsADifferentGame(int participants)
    {
        var options = ReplayFixtures.Options(participants);

        var one = ReplayFixtures.Record(options, seed: 51, choiceSeed: 7, Cap);
        var other = ReplayFixtures.Record(options, seed: 52, choiceSeed: 7, Cap); // the same policy, another game

        Assert.NotEqual(one.Start, other.Start); // another deal
        Assert.NotEqual(one.Start.Deck, other.Start.Deck);
        Assert.NotEqual(one.Events, other.Events); // another history
        Assert.NotEqual(one.Final, other.Final);
    }

    [Fact]
    public void E10_TheCommandsOfOneGame_DoNotReproduceTheHistoryOfAnotherSeed()
    {
        var options = ReplayFixtures.Options(3);
        var recording = ReplayFixtures.Record(options, seed: 61, choiceSeed: 5, Cap);

        // The same options and commands on another seed: the first placement may even be accepted, but the game is not the same one.
        var otherSeed = ReplayFixtures.Create(options, 62);
        var state = otherSeed;
        var identical = 0;
        foreach (var (command, recorded) in recording.Commands.Zip(recording.Transitions))
        {
            var result = GameEngine.Apply(state, command, RealMap);
            if (!result.IsSuccess || result.Value.State != recorded.State)
            {
                break;
            }

            identical++;
            state = result.Value.State;
        }

        Assert.True(identical < recording.Commands.Count, "Another seed replayed the whole game identically.");
        Assert.Equal(0, identical); // the very first state already differs: another deal
    }

    // ---- restoring from the persisted form --------------------------------------------------------------------------------

    [Theory]
    [InlineData(2, 71L)]
    [InlineData(3, 72L)]
    [InlineData(4, 73L)]
    public void E10_AReplayFromAStateRestoredFromSeedAndDraws_IsIdenticalToTheRecordedTail(int participants, long seed)
    {
        var options = ReplayFixtures.Options(participants);
        var recording = ReplayFixtures.Record(options, seed, choiceSeed: seed, Cap);
        var firstAttack = recording.Commands.ToList().FindIndex(command => command is Attack);
        var lastClose = recording.Commands.ToList().FindLastIndex(command => command is EndTurn);
        Assert.True(firstAttack > 0 && lastClose > firstAttack);

        // Right at the start, just before the first dice, in the middle, and just before the last turn closed.
        int[] cuts = [0, 1, firstAttack, recording.Commands.Count / 3, recording.Commands.Count * 2 / 3, lastClose];
        foreach (var cut in cuts)
        {
            var saved = cut == 0 ? recording.Start : recording.Transitions[cut - 1].State; // the game after `cut` commands
            var restored = ReplayFixtures.Restore(saved);

            Assert.Equal(saved, restored); // the persisted form loses nothing
            var finalState = AssertReplaysStepByStep(restored, recording, from: cut);
            Assert.Equal(recording.Final, finalState);
        }
    }

    [Fact]
    public void E10_TheRngOfASavedGame_IsRebuiltFromTheSeedAndTheNumberOfDraws_WithoutReplayingThem()
    {
        var recording = ReplayFixtures.Record(ReplayFixtures.Options(2), seed: 81, choiceSeed: 3, Cap);
        var saved = recording.Transitions[recording.Commands.Count / 2].State;
        Assert.True(saved.Rng.Draws > 100, $"Only {saved.Rng.Draws} draws: not a real game.");

        // The generator, consuming every draw one by one from the seed...
        var walked = new SeededRng(saved.Rng.Seed);
        for (long draw = 0; draw < saved.Rng.Draws; draw++)
        {
            walked.NextUInt64();
        }

        // ...and the generator rebuilt at once from the two numbers a database stores, are the same generator.
        var rebuilt = new SeededRng(saved.Rng.Seed, saved.Rng.Draws);
        Assert.Equal(walked.State, rebuilt.State);
        Assert.Equal(saved.Rng, rebuilt.State);
        Assert.Equal(Enumerable.Range(0, 20).Select(_ => walked.NextUInt64()), Enumerable.Range(0, 20).Select(_ => rebuilt.NextUInt64()));
    }
}
