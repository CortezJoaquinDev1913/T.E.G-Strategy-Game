using FsCheck.Xunit;
using Teg.Domain.Commands;
using Teg.Domain.Events;
using Teg.Domain.Rules;
using Teg.Domain.State;
using Teg.Domain.Tests.Support;
using static Teg.Domain.Tests.Support.CardFixtures;
using static Teg.Domain.Tests.Support.CombatFixtures;
using static Teg.Domain.Tests.Support.GameFixtures;

namespace Teg.Domain.Tests.Properties;

/// <summary>
/// E1-E10: properties of WHOLE games over the real board, 2, 3 and 4 participants, from the deal to the end. A seeded random policy
/// plays every seat and picks ONLY from <see cref="LegalMoves.For"/> (so "never stuck" is part of the test: a game that is not
/// Finished always offers a move), and after EVERY accepted command:
/// <list type="bullet">
/// <item>the board still has the 50 countries, once each, each with a valid owner and at least one army; a participant is
/// <c>Eliminated</c> if and only if they own no country, and never comes back;</item>
/// <item>the 50 cards (draw pile, discard pile and all hands together) are exactly the 50 country ids, once each;</item>
/// <item><c>Version</c> grew by exactly one; the RNG kept its seed and never went back (it moves only when dice are rolled or the
/// discard pile is reshuffled);</item>
/// <item>the ACTIVE participant is not eliminated while the game goes on, and the phase machinery is consistent (a pending conquest
/// only in Ataque, reinforcements pending exactly in Refuerzo);</item>
/// <item>armies are CONSERVED: the total on the board grows only by the armies a placement puts on it (initial placement,
/// reinforcements) and by the country premiums (2 each) paid at the close of a turn, and shrinks only by the losses of the dice of
/// an attack; a trade, an occupation, a regroup and a phase change leave it alone, and every army of the pool is either placed or
/// still pending;</item>
/// <item>victory follows R§8 (the shared oracle <c>VictoryFixtures.AssertVictoryFollowsTheRules</c>): <c>Finished</c> iff there is a winner
/// and a reason, and Finished is TERMINAL: nothing is legal and the engine refuses everything with <c>GAME_FINISHED</c>.</item>
/// </list>
/// </summary>
public sealed class PlayoutPropertyTests
{
    private sealed class Tally
    {
        public int Games;
        public int Finished;
        public int Commands;
        public int Attacks;
        public int Conquests;
        public int Eliminations;
        public int Trades;
        public int ForcedTrades;
        public int Premiums;
        public int Regroups;
        public int Closes;
        public int Rounds;
        public readonly Dictionary<WinReason, int> Reasons = [];

        public string Report() =>
            $"{Games} games, {Finished} finished ({string.Join(", ", Reasons.Select(r => $"{r.Key}={r.Value}"))}), {Commands} commands, " +
            $"{Attacks} attacks, {Conquests} conquests, {Eliminations} eliminations, {Trades} trades ({ForcedTrades} forced), " +
            $"{Premiums} premiums, {Regroups} regroups, {Closes} closes, deepest round {Rounds}";
    }

    // ---- the invariants -----------------------------------------------------------------------------------------------

    private static void AssertTheBoard(GameState state)
    {
        // Exactly the 50 countries, once each and in map order: one entry, so one owner, per country.
        Assert.Equal(RealMap.Territories.Select(t => t.Id), state.Territories.Select(t => t.Id));
        Assert.All(state.Territories, t =>
        {
            Assert.InRange(t.Owner, 0, state.Participants.Count - 1);
            Assert.True(t.Armies >= 1, $"{t.Id} has {t.Armies} armies.");
        });
        Assert.All(state.Participants, p => Assert.Equal(state.Territories.All(t => t.Owner != p.Seat), p.Eliminated)); // eliminated iff no country
    }

    private static void AssertTheFiftyCards(GameState state)
    {
        var every = state.Deck.Draw
            .Concat(state.Deck.Discard)
            .Concat(state.Participants.SelectMany(p => p.Cards.Select(card => card.TerritoryId)))
            .ToArray();

        Assert.Equal(RealMap.Territories.Count, every.Length);
        Assert.True(every.ToHashSet().SetEquals(AllCardIds()), "The cards are not exactly the 50 country ids, once each.");
    }

    /// <summary>The armies a command puts on the board, or takes off it, by the rule: the exact change of the total on the board.</summary>
    private static int ExpectedChangeOfTheTotal(Playout.Step step) => step.Command switch
    {
        PlaceInitialArmies place => place.Placements.Sum(p => p.Armies),
        PlaceReinforcements place => place.Placements.Sum(p => p.Armies),
        Attack => -LossesOf(step),
        EndTurn => step.After.Events.OfType<BonusArmiesPlaced>().Sum(premium => premium.Armies), // the country premiums
        _ => 0, // a trade, an occupation, a regroup, a change of phase: armies only move around
    };

    /// <summary>The armies the dice of an attack took off the board, from both sides.</summary>
    private static int LossesOf(Playout.Step step)
    {
        var fight = step.After.Events.OfType<AttackResolved>().Single();

        return fight.AttackerLosses + fight.DefenderLosses;
    }

    private static void AssertEveryStep(Playout.Step step, Tally tally)
    {
        var (before, command, transition) = (step.Before, step.Command, step.After);
        var after = transition.State;

        // E10 / A3: one command, one version.
        Assert.Equal(before.Version + 1, after.Version);

        // E1, E2: the board, the cards, the participants.
        AssertTheBoard(after);
        AssertTheFiftyCards(after);
        Assert.Equal(before.Participants.Count, after.Participants.Count);
        Assert.All(before.Participants.Where(p => p.Eliminated), p => Assert.True(after.Participants[p.Seat].Eliminated)); // for good
        Assert.All(before.Participants, p => Assert.True(after.Participants[p.Seat].TradesDone >= p.TradesDone));

        // E3: somebody always plays, and a participant with no country never does (unless the game is over).
        Assert.InRange(after.ActiveSeat, 0, after.Participants.Count - 1);
        Assert.True(after.Phase == GamePhase.Finished || !after.Participants[after.ActiveSeat].Eliminated);

        // E10: the generator keeps its seed and never goes back; only dice and a reshuffle move it.
        Assert.Equal(before.Rng.Seed, after.Rng.Seed);
        Assert.True(after.Rng.Draws >= before.Rng.Draws);
        if (command is not (Attack or EndTurn))
        {
            Assert.Equal(before.Rng, after.Rng);
        }

        if (command is Attack)
        {
            Assert.True(after.Rng.Draws >= before.Rng.Draws + 2, "An attack rolls at least one die on each side."); // so a replay fights the same fight
        }

        // The phase machinery.
        Assert.True(after.Turn.PendingConquest is null || after.Phase == GamePhase.Attack);
        Assert.Equal(after.Phase == GamePhase.Reinforcement, after.Turn.PendingArmies > 0);

        // E1-E8: armies are conserved (see the type summary).
        Assert.Equal(TotalArmies(before) + ExpectedChangeOfTheTotal(step), TotalArmies(after));
        AssertThePoolIsAccountedFor(step);

        // E9: victory, and Finished is terminal.
        VictoryFixtures.AssertVictoryFollowsTheRules(before, transition);
        Assert.Equal(after.Phase == GamePhase.Finished, after.Winner is not null);
        Assert.Equal(after.Phase == GamePhase.Finished, after.WinReason is not null);

        Count(step, tally);
    }

    /// <summary>Every army of the Refuerzo pool is either placed on the board or still pending; a trade adds exactly its value to the pool.</summary>
    private static void AssertThePoolIsAccountedFor(Playout.Step step)
    {
        var (before, after) = (step.Before.Turn.PendingArmies, step.After.State.Turn.PendingArmies);

        switch (step.Command)
        {
            case PlaceReinforcements place:
                Assert.Equal(before - place.Placements.Sum(p => p.Armies), after);
                break;
            case TradeCards:
                Assert.Equal(before + step.After.Events.OfType<CardsTraded>().Single().Armies, after);
                break;
            case EndTurn:
                Assert.Equal(0, before); // a turn only ends with nothing left to place
                break;
            default:
                Assert.Equal(before, after);
                break;
        }
    }

    private static void Count(Playout.Step step, Tally tally)
    {
        tally.Commands++;
        tally.Rounds = Math.Max(tally.Rounds, step.After.State.Round);
        tally.Conquests += step.After.Events.OfType<TerritoryConquered>().Count();
        tally.Eliminations += step.After.Events.OfType<ParticipantEliminated>().Count();
        tally.Premiums += step.After.Events.OfType<BonusArmiesPlaced>().Count();

        switch (step.Command)
        {
            case Attack:
                tally.Attacks++;
                break;
            case TradeCards:
                tally.Trades++;
                tally.ForcedTrades += step.Moves.TradeMandatory ? 1 : 0;
                break;
            case Regroup:
                tally.Regroups++;
                break;
            case EndTurn:
                tally.Closes++;
                break;
        }
    }

    /// <summary>Nothing is legal in a finished game and every command is refused with GAME_FINISHED (the game is terminal).</summary>
    private static void AssertFinishedIsTerminal(GameState finished)
    {
        Assert.Equal(GamePhase.Finished, finished.Phase);
        Assert.NotNull(finished.Winner);
        Assert.NotNull(finished.WinReason);
        Assert.False(LegalMoves.For(finished, RealMap).HasMoves);

        var universe = LegalMovesOracle.Universe(finished, RealMap);
        Assert.NotEmpty(universe);
        Assert.All(universe, command =>
        {
            var refused = GameEngine.Apply(finished, command, RealMap);
            Assert.False(refused.IsSuccess, $"{LegalMovesOracle.Describe(command)} was accepted in a finished game.");
            Assert.Equal(RuleViolations.GameFinished, refused.Error.Code);
        });
    }

    /// <summary>Plays a whole game checking every step; returns whether it ended in victory.</summary>
    private static bool PlayChecked(GameState start, long choiceSeed, int cap, Tally tally, int? conqueror = null)
    {
        tally.Games++;
        AssertTheBoard(start);
        AssertTheFiftyCards(start);

        var last = start;
        foreach (var step in Playout.Run(start, choiceSeed, cap, conqueror: conqueror))
        {
            AssertEveryStep(step, tally);
            last = step.After.State;
        }

        if (last.Phase != GamePhase.Finished)
        {
            return false;
        }

        tally.Finished++;
        tally.Reasons[last.WinReason!.Value] = tally.Reasons.GetValueOrDefault(last.WinReason.Value) + 1;
        AssertFinishedIsTerminal(last);

        return true;
    }

    // ---- sweeps ---------------------------------------------------------------------------------------------------------

    private const int Games = 5;
    private const int ConquerorGames = 8;
    private const int Cap = 1200;

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void E1_E10_OverWholeRandomGames_EveryInvariantHoldsAfterEveryCommand_AndTheGameIsNeverStuck(int participants)
    {
        var tally = new Tally();

        for (var seed = 1L; seed <= Games; seed++)
        {
            PlayChecked(Deal(participants, seed), choiceSeed: seed * 7919, Cap, tally);
        }

        // Measured over these 5 games per size (at most 1200 commands each): 5,535 / 6,000 / 6,000 commands, 2,403-2,563 attacks,
        // 854-1,028 conquests, 76-97 trades (36-55 of them forced), 116-146 premiums, 535-647 regroups, 584-645 turns closed, up to
        // round 35-66. A random policy rarely wins a whole game (1 of 15 finished: 2 participants), which is why the next test
        // plays one seat to win; the floors below leave a wide margin and prove that the whole machinery was exercised.
        Assert.True(tally.Commands > 4000, tally.Report());
        Assert.True(tally.Attacks > 1500, tally.Report());
        Assert.True(tally.Conquests > 500, tally.Report());
        Assert.True(tally.Trades > 50, tally.Report());
        Assert.True(tally.ForcedTrades > 20, tally.Report());
        Assert.True(tally.Premiums > 80, tally.Report());
        Assert.True(tally.Regroups > 300, tally.Report());
        Assert.True(tally.Rounds > 25, tally.Report());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void E9_WhenOneSeatPlaysToConquer_TheGamesReachVictory_EveryStepIsChecked_AndFinishedIsTerminal(int participants)
    {
        var tally = new Tally();

        for (var seed = 1L; seed <= ConquerorGames; seed++)
        {
            PlayChecked(Deal(participants, seed), choiceSeed: seed * 104729, Cap, tally, conqueror: (int)(seed % participants));
        }

        // Measured over these 8 games per size (at most 1200 commands each): finished 5 (2 participants, all by elimination),
        // 3 (3 participants, by elimination) and 8 (4 participants, all by the common objective of 30 countries); 83-137 trades,
        // 5-7 eliminations at 2 and 3 participants. Every finished game is then asked for a move of every kind and refuses it all.
        Assert.True(tally.Finished >= 2, tally.Report());
        Assert.True(tally.Conquests > 500, tally.Report());
        Assert.True(tally.Reasons.GetValueOrDefault(participants == 4 ? WinReason.CommonObjective : WinReason.Elimination) >= 2, tally.Report());
    }

    [Property(MaxTest = 10)]
    public bool AnyRandomGame_KeepsEveryInvariant_AndIsNeverStuck(long dealSeed, long choiceSeed, byte participantsPick, bool conquering)
    {
        var participants = 2 + participantsPick % 3;
        var tally = new Tally();

        PlayChecked(
            Deal(participants, dealSeed),
            choiceSeed,
            cap: 500,
            tally,
            conqueror: conquering ? (int)(((dealSeed % participants) + participants) % participants) : null);

        return tally.Commands > 0; // the invariants are asserted inside
    }
}
