using FsCheck.Xunit;
using Teg.Domain.Rng;
using Teg.Domain.Rules;
using Teg.Domain.State;
using Teg.Domain.Tests.Support;
using static Teg.Domain.Tests.Support.GameFixtures;
using static Teg.Domain.Tests.Support.ReinforcementFixtures;

namespace Teg.Domain.Tests.LegalMovesRules;

/// <summary>
/// A4 / B1: the SOUNDNESS and COMPLETENESS of the legal-moves descriptor against the engine, over the states of many random
/// games (see <see cref="LegalMovesOracle"/>). For every sampled state, every command of a finite, systematically generated
/// universe (all attacks between any two countries, occupation counts 0 to 5, regroups with counts 0 to armies + 1, every
/// triple of the hand, EndTurn, StartRegroup, one army on every country, whole pools on every country, random multi-entry
/// placements) gets the same answer from the engine ("accepted?") and from the descriptor ("allowed?"), and what the
/// descriptor expands to is accepted. A disagreement in either direction fails with the command and the state's context.
/// The states come from games played by a random policy that picks only from the descriptors (<see cref="Playout"/>): whole games
/// from the deal, random boards in Ataque (lots of conquests, occupations and eliminations) and random Refuerzo states (hands up
/// to eight cards, continent bonuses, trades already made).
/// </summary>
public sealed class LegalMovesAgreementTests
{
    /// <summary>The first states of every kind of a playout are always checked; after that, one state in <see cref="Stride"/>.</summary>
    private const int FirstOfEachKind = 3;

    private sealed class Tally
    {
        private readonly Dictionary<string, int> _seen = [];

        public Dictionary<string, int> Checked { get; } = [];

        public int Universe { get; private set; }

        public int Accepted { get; private set; }

        public int Expanded { get; private set; }

        public int Steps { get; private set; }

        /// <summary>Checks the state a step started from if its kind is new for the playout, or on the stride.</summary>
        public void CheckSampled(Playout.Step step, int stride, string context)
        {
            Steps++;
            var kind = KindOf(step.Before, step.Moves);
            _seen[kind] = _seen.GetValueOrDefault(kind) + 1;
            if (_seen[kind] <= FirstOfEachKind || step.Index % stride == 0)
            {
                Check(step.Before, kind, $"{context}, step {step.Index}, {kind}");
            }
        }

        public void Check(GameState state, string kind, string context)
        {
            var agreement = LegalMovesOracle.AssertAgrees(state, RealMap, context);
            Checked[kind] = Checked.GetValueOrDefault(kind) + 1;
            Universe += agreement.Universe;
            Accepted += agreement.Accepted;
            Expanded += agreement.Expanded;
        }

        public void ResetSeen() => _seen.Clear();

        public string Report() =>
            $"{Steps} steps, checks {string.Join(", ", Checked.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"))}; " +
            $"{Universe} candidates, {Accepted} accepted, {Expanded} expanded";
    }

    private static string KindOf(GameState before, LegalMoveSet moves) => before.Phase switch
    {
        GamePhase.Reinforcement when moves.TradeMandatory => "reinforcement-mandatory-trade",
        GamePhase.Reinforcement when moves.Trades.Count > 0 => "reinforcement-with-trades",
        GamePhase.Reinforcement when before.Turn.ContinentPools.Count > 0 => "reinforcement-continent",
        GamePhase.Reinforcement => "reinforcement",
        GamePhase.Attack when moves.Occupation is not null => "conquest-pending",
        var phase => phase.ToString().ToLowerInvariant(),
    };

    // ---- whole games from the deal ---------------------------------------------------------------------------------

    private const int Games = 6;
    private const int GameLength = 500;
    private const int GameStride = 30;

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void A4_OverWholeRandomGames_TheDescriptorAndTheEngineAgreeOnEveryCandidateCommand(int participants)
    {
        var tally = new Tally();

        for (var seed = 1L; seed <= Games; seed++)
        {
            tally.ResetSeen();
            foreach (var step in Playout.Run(Deal(participants, seed), choiceSeed: seed * 7919, GameLength))
            {
                tally.CheckSampled(step, GameStride, $"{participants} participants, game {seed}");
            }
        }

        // Every phase of the game was looked at, over ~300,000 candidates, with real attacks and regroups to compare. Measured per
        // size (6 games of 500 commands): 18 initial placements, 59-75 attacks, 28-36 regroups, 31-42 pending conquests, ~85
        // Refuerzo states (16-18 with a mandatory trade) and 4,571-11,216 accepted commands; the floors leave a wide margin.
        Assert.True(tally.Checked.GetValueOrDefault("initialplacement") >= 2 * Games, tally.Report());
        Assert.True(tally.Checked.GetValueOrDefault("attack") >= 3 * Games, tally.Report());
        Assert.True(tally.Checked.GetValueOrDefault("regroup") >= 3 * Games, tally.Report());
        Assert.True(tally.Checked.GetValueOrDefault("conquest-pending") >= Games, tally.Report());
        Assert.True(tally.Checked.Where(kind => kind.Key.StartsWith("reinforcement")).Sum(kind => kind.Value) >= 8 * Games, tally.Report());
        Assert.True(tally.Accepted > 3000, tally.Report());
    }

    // ---- random boards in Ataque: conquests, occupations, eliminations --------------------------------------------------

    private const int Boards = 8;
    private const int BoardLength = 70;
    private const int BoardStride = 8;

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void A4_OverRandomBoardsInAtaque_ConquestsOccupationsAndRegroupsAgreeWithTheEngine(int participants)
    {
        var tally = new Tally();

        for (var seed = 1L; seed <= Boards; seed++)
        {
            tally.ResetSeen();
            var board = LegalMovesOracle.RandomBoard(participants, seed, GamePhase.Attack);
            foreach (var step in Playout.Run(board, choiceSeed: seed * 104729, BoardLength))
            {
                tally.CheckSampled(step, BoardStride, $"{participants} participants, board {seed}");
            }
        }

        // Measured per size (8 boards of 70 commands): 20-34 pending conquests, 45-54 attacks, 21-34 regroups, 5,661-14,478 accepted.
        Assert.True(tally.Checked.GetValueOrDefault("conquest-pending") >= 2 * Boards, tally.Report());
        Assert.True(tally.Checked.GetValueOrDefault("attack") >= 2 * Boards, tally.Report());
        Assert.True(tally.Checked.GetValueOrDefault("regroup") >= 2 * Boards, tally.Report());
        Assert.True(tally.Accepted > 3000, tally.Report());
    }

    // ---- random Refuerzo states: hands, bonuses, trades already made ----------------------------------------------------

    private const int ReinforcementCases = 70;

    /// <summary>
    /// Seat 1 starts Refuerzo owning a random set of 10 to 29 countries plus 0 to 2 whole continents (so continent bonuses appear),
    /// holding 0 to 8 random cards, with 0 to 5 trades already made.
    /// </summary>
    private static GameState RandomReinforcement(long seed)
    {
        var picks = new SeededRng(seed);
        var ids = RealMap.Territories.Select(t => t.Id).ToArray();
        var owned = new HashSet<string>(ids.OrderBy(_ => picks.NextInt(1000)).Take(10 + picks.NextInt(20)));
        for (var continents = picks.NextInt(3); continents > 0; continents--)
        {
            owned.UnionWith(RealMap.Continents[picks.NextInt(RealMap.Continents.Count)].TerritoryIds);
        }

        var hand = ids.OrderBy(_ => picks.NextInt(1000)).Take(picks.NextInt(9)).ToArray();

        return WithTrades(WithHand(ReinforcementOfSeatOne(owned), 1, hand), 1, picks.NextInt(6));
    }

    [Fact]
    public void A4_OverRandomReinforcementStates_PoolsTradesAndMandatoryTradesAgreeWithTheEngine()
    {
        var tally = new Tally();

        for (var seed = 1L; seed <= ReinforcementCases; seed++)
        {
            var start = RandomReinforcement(seed);
            tally.ResetSeen();

            // The state itself, then what a few random commands (trades, partial placements) leave behind.
            var steps = Playout.Record(start, choiceSeed: seed, maxCommands: 4);
            foreach (var step in steps)
            {
                tally.Check(step.Before, KindOf(step.Before, step.Moves), $"reinforcement {seed}, step {step.Index}");
            }
        }

        // Measured over the 70 cases: 39 states with a mandatory trade, 18 with an optional trade, 47 with a continent bonus
        // (plus 117 Ataque, 4 pending conquests and 10 Reagrupe states that the placements lead to), 10,910 accepted commands.
        Assert.True(tally.Checked.GetValueOrDefault("reinforcement-mandatory-trade") >= 20, tally.Report());
        Assert.True(tally.Checked.GetValueOrDefault("reinforcement-with-trades") >= 10, tally.Report());
        Assert.True(tally.Checked.GetValueOrDefault("reinforcement-continent") >= 20, tally.Report());
        Assert.True(tally.Accepted > 5000, tally.Report());
    }

    // ---- any seed --------------------------------------------------------------------------------------------------------

    [Property(MaxTest = 8)]
    public bool AnyRandomGame_TheDescriptorAndTheEngineAgree(long dealSeed, long choiceSeed, byte participantsPick)
    {
        var participants = 2 + participantsPick % 3;
        var tally = new Tally();

        foreach (var step in Playout.Run(Deal(participants, dealSeed), choiceSeed, maxCommands: 150))
        {
            tally.CheckSampled(step, stride: 25, $"seed {dealSeed}/{choiceSeed}, {participants} participants");
        }

        return tally.Accepted > 0; // the agreement is asserted inside; something was really compared
    }
}
