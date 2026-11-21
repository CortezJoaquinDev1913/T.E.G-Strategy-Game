using System.Collections.Concurrent;
using Teg.Application.Bots;
using Teg.Application.Tests.Support;
using Teg.Domain;
using Teg.Domain.Commands;
using Teg.Domain.Rules;
using Teg.Domain.State;
using static Teg.Application.Tests.Support.Games;

namespace Teg.Application.Tests.Bots;

/// <summary>
/// Whole games of bots against bots (B1-B3): never an illegal command, always deterministic, never more than the cap in a turn, and
/// games that END. One class per table size, so that the three sweeps run in parallel.
/// </summary>
public abstract class BotGameTestsBase(int participants)
{
    /// <summary>A generous total: far more steps than any game needs (the longest of 40 games per size took under 7,000).</summary>
    private const int MaxSteps = 15_000;

    private const int InspectedGames = 6;
    private const int TerminationGames = 20;

    private static readonly HeuristicBotPolicy Fresh = new(Map);

    /// <summary>The inspected games of each table size, played once and shared by the tests of that size (they keep every decision).</summary>
    private static readonly ConcurrentDictionary<int, Lazy<Task<List<BotGames.Run>>>> Inspected = new();

    private Task<List<BotGames.Run>> Runs() =>
        Inspected.GetOrAdd(participants, size => new Lazy<Task<List<BotGames.Run>>>(async () =>
        {
            var runs = new List<BotGames.Run>();
            for (long seed = 1; seed <= InspectedGames; seed++)
            {
                runs.Add(await BotGames.Play(size, seed, MaxSteps));
            }

            return runs;
        })).Value;

    [Fact]
    public async Task B1_EveryCommandOfTheBot_IsLegal_ForEveryPhaseAndSituation()
    {
        var runs = await Runs();
        var phases = new HashSet<GamePhase>();
        var kinds = new HashSet<Type>();
        var pendingOccupations = 0;

        foreach (var decision in runs.SelectMany(run => run.Decisions))
        {
            // The engine is the authority: the command it is given must be accepted...
            Assert.True(GameEngine.Apply(decision.State, decision.Command, Map).IsSuccess, $"{decision.Command} in {decision.State.Phase}");

            // ...and it must come from what the descriptor offers: whole commands are listed, placements spend the pools on their countries.
            if (decision.Command is PlaceInitialArmies or PlaceReinforcements)
            {
                var placements = decision.Command is PlaceInitialArmies initial ? initial.Placements : ((PlaceReinforcements)decision.Command).Placements;
                Assert.Equal(decision.Legal.Pools.Sum(pool => pool.Armies), placements.Sum(p => p.Armies));
                Assert.All(placements, p => Assert.Contains(decision.Legal.Pools, pool => pool.TerritoryIds.Contains(p.TerritoryId)));
            }
            else
            {
                Assert.Contains(decision.Command, decision.Legal.ToCommands());
            }

            Assert.Equal(decision.State.ActiveSeat, decision.Command.Seat);
            phases.Add(decision.State.Phase);
            kinds.Add(decision.Command.GetType());
            pendingOccupations += decision.Legal.Occupation is null ? 0 : 1;
        }

        // The sweep really visited what it claims to cover.
        Assert.Equal(
            new[] { GamePhase.InitialPlacement, GamePhase.Reinforcement, GamePhase.Attack, GamePhase.Regroup },
            phases.Order());
        Assert.Equal(
            new[] { typeof(PlaceInitialArmies), typeof(PlaceReinforcements), typeof(TradeCards), typeof(Attack), typeof(MoveAfterConquest), typeof(StartRegroup), typeof(Regroup), typeof(EndTurn) }
                .Select(type => type.Name).Order(),
            kinds.Select(type => type.Name).Order());
        Assert.True(pendingOccupations > 20, $"only {pendingOccupations} occupations");
    }

    [Fact]
    public async Task B1_TheSweep_ReachesMandatoryTrades_PendingOccupations_AndEveryEndOfTurn()
    {
        var decisions = (await Runs()).SelectMany(run => run.Decisions).ToList();

        Assert.Contains(decisions, d => d.Legal.TradeMandatory && d.Command is TradeCards); // the rule that a trade comes first
        Assert.Contains(decisions, d => d.Command is EndTurn && d.State.Phase == GamePhase.Attack);
        Assert.Contains(decisions, d => d.Command is EndTurn && d.State.Phase == GamePhase.Regroup);
        Assert.Contains(decisions, d => d.Command is MoveAfterConquest m && m.Armies == d.Legal.Occupation!.Max && m.Armies > d.Legal.Occupation.Min);
        Assert.Contains(decisions, d => d.Command is MoveAfterConquest m && m.Armies == d.Legal.Occupation!.Min && m.Armies < d.Legal.Occupation.Max);
    }

    [Fact]
    public async Task B3_TheSameStateGivesTheSameCommand_AcrossWholeGames()
    {
        var run = (await Runs())[0];

        var sampled = run.Decisions.Where((_, index) => index % 3 == 0).ToList();
        Assert.True(sampled.Count > 50, $"only {sampled.Count} decisions sampled");
        foreach (var decision in sampled)
        {
            Assert.Equal(decision.Command, Fresh.Next(decision.State, decision.Legal));
        }

        // And the whole game replays identically: same seed, same bots, same decisions, same final state.
        var again = await BotGames.Play(participants, seed: 1, MaxSteps);
        Assert.Equal(run.Final, again.Final);
        Assert.Equal(run.Decisions.Select(d => d.Command), again.Decisions.Select(d => d.Command));
    }

    [Fact]
    public async Task B2_NoTurnOfTheBot_GoesBeyondTheCap_PlusItsClosingSteps()
    {
        var runs = await Runs();

        Assert.All(runs, run => Assert.InRange(run.MaxActionsInATurn, 1, HeuristicBotPolicy.DefaultMaxActionsPerTurn + 2));
    }

    [Fact]
    public async Task B2_ALowCap_KeepsEveryTurnShort_AndTheGamesStillRun()
    {
        const int cap = 6;

        var run = await BotGames.Play(participants, seed: 2, maxSteps: 8_000, cap, recordDecisions: false);

        // Refuerzo (trades and the placement) is outside the cap: it cannot be skipped. The cap does bite: some turn is cut short by it.
        Assert.InRange(run.MaxActionsInATurn, cap, cap + 4);
        Assert.True(run.Turns > 50, $"only {run.Turns} turns");
    }

    [Fact]
    public async Task B3_MostGamesReachAResult_WithoutEverFailingAStep()
    {
        var finished = 0;
        var longest = 0;
        for (long seed = 1; seed <= TerminationGames; seed++)
        {
            var run = await BotGames.Play(participants, seed, MaxSteps, recordDecisions: false); // any rejected step fails inside Play

            finished += run.Finished ? 1 : 0;
            longest = Math.Max(longest, run.Steps);
            if (run.Finished)
            {
                Assert.NotNull(run.Final.Winner);
                Assert.NotNull(run.Final.WinReason);
            }
        }

        // Measured over seeds 1-40: 40 of 40 at every size (the longest game took under 7,000 steps). The floor leaves margin for tuning.
        Assert.True(finished >= TerminationGames * 9 / 10, $"{participants} participants: {finished} of {TerminationGames} finished (longest {longest} steps)");
    }
}

public class BotGamesOfTwoTests() : BotGameTestsBase(2);

public class BotGamesOfThreeTests() : BotGameTestsBase(3);

public class BotGamesOfFourTests() : BotGameTestsBase(4);
