using Teg.Application.Bots;
using Teg.Application.UseCases;
using Teg.Domain.Commands;
using Teg.Domain.State;
using static Teg.Application.Tests.Support.Games;

namespace Teg.Application.Tests.Support;

/// <summary>
/// Whole games in which EVERY participant is a bot, driven by repeated <c>BotTurn</c> calls over the in-memory repository (the way the
/// UI's BotRunner will drive them). <c>CreateGame</c> refuses all bots, so the game is dealt for humans and then turned into bots.
/// </summary>
internal static class BotGames
{
    internal sealed record Run(
        int Participants,
        long Seed,
        bool Finished,
        int Steps,
        int Turns,
        int MaxActionsInATurn,
        int Attacks,
        int Conquests,
        GameState Final,
        IReadOnlyList<RecordingPolicy.Decision> Decisions);

    /// <summary>
    /// Plays until somebody wins or <paramref name="maxSteps"/> bot steps were taken. Every step must be accepted: any error fails the run.
    /// <paramref name="recordDecisions"/> keeps every state, legal-move set and command seen (heavy: for the sweeps that inspect them).
    /// </summary>
    public static async Task<Run> Play(
        int participants, long seed, int maxSteps, int cap = HeuristicBotPolicy.DefaultMaxActionsPerTurn, bool recordDecisions = true)
    {
        var repository = new InMemoryGameRepository();
        var id = await repository.AddAsync(AllBots(Deal(seed, Enumerable.Repeat(H, participants).ToArray())));
        var policy = new RecordingPolicy(new HeuristicBotPolicy(Map, cap), recordDecisions);
        var bot = new BotTurn(repository, new SubmitAction(repository, Map), policy, Map);

        var steps = 0;
        var turns = 0;
        var inThisTurn = 0;
        var longest = 0;
        var attacks = 0;
        var conquests = 0;

        while (repository.State(id).Phase != GamePhase.Finished && steps < maxSteps)
        {
            var before = repository.State(id);
            var result = await bot.ExecuteAsync(new BotTurnRequest(id));
            Assert.True(
                result.IsSuccess,
                result.IsSuccess ? null : $"seed {seed}, {participants} participants, step {steps}: {result.Error.Code}: {result.Error.Message}");

            steps++;
            inThisTurn++;
            attacks += result.Value.Command is Attack ? 1 : 0;
            conquests += result.Value.Command is Attack && repository.State(id).Turn.PendingConquest is not null ? 1 : 0;

            var after = repository.State(id);
            if (after.ActiveSeat != before.ActiveSeat || after.Phase == GamePhase.Finished)
            {
                turns++;
                longest = Math.Max(longest, inThisTurn);
                inThisTurn = 0;
            }
        }

        return new Run(
            participants, seed, repository.State(id).Phase == GamePhase.Finished, steps, turns, Math.Max(longest, inThisTurn), attacks, conquests,
            repository.State(id), policy.Decisions);
    }
}
