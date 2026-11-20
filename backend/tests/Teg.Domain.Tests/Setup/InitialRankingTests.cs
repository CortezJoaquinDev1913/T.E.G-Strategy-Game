using Teg.Domain.Setup;

namespace Teg.Domain.Tests.Setup;

/// <summary>
/// R§3: one die per participant; ties are re-rolled ONLY among the tied until a strict ranking exists.
/// The dice are scripted, so the exact number and order of rolls is part of what is verified.
/// </summary>
public sealed class InitialRankingTests
{
    /// <summary>Feeds the scripted rolls in order. Over-consumption throws; under-consumption leaves the queue non-empty.</summary>
    private sealed class ScriptedDice(params int[] rolls)
    {
        private readonly Queue<int> _rolls = new(rolls);

        public int Remaining => _rolls.Count;

        public int Roll() => _rolls.Dequeue();
    }

    [Fact]
    public void WithoutTies_TheHighestRollRanksFirst_AndEveryoneRollsExactlyOnce()
    {
        var dice = new ScriptedDice(2, 6, 4); // seat 0 -> 2, seat 1 -> 6, seat 2 -> 4

        var ranking = InitialRanking.Determine(3, dice.Roll);

        Assert.Equal([1, 2, 0], ranking.ToArray());
        Assert.Equal(0, dice.Remaining);
    }

    [Theory]
    [InlineData(4, 2, new[] { 0, 1 })]
    [InlineData(1, 3, new[] { 1, 0 })]
    public void WithTwoParticipants_TheBetterRollWins(int rollSeat0, int rollSeat1, int[] expected)
    {
        var dice = new ScriptedDice(rollSeat0, rollSeat1);

        var ranking = InitialRanking.Determine(2, dice.Roll);

        Assert.Equal(expected, ranking.ToArray());
        Assert.Equal(0, dice.Remaining);
    }

    [Fact]
    public void ATieAtTheTop_IsRerolledOnlyBetweenTheTiedSeats()
    {
        // seats 0 and 1 tie on 5; seat 2 rolled 2 and is not rolled again. Re-roll: seat 0 -> 3, seat 1 -> 6.
        var dice = new ScriptedDice(5, 5, 2, 3, 6);

        var ranking = InitialRanking.Determine(3, dice.Roll);

        Assert.Equal([1, 0, 2], ranking.ToArray());
        Assert.Equal(0, dice.Remaining);
    }

    [Fact]
    public void ATieInTheMiddle_StaysInsideItsOwnRanks_AndRepeatsUntilBroken()
    {
        // seat 0 -> 6 and seat 3 -> 1 are settled. Seats 1 and 2 tie on 3, tie again on 4, then 2 vs 5.
        var dice = new ScriptedDice(6, 3, 3, 1, 4, 4, 2, 5);

        var ranking = InitialRanking.Determine(4, dice.Roll);

        Assert.Equal([0, 2, 1, 3], ranking.ToArray());
        Assert.Equal(0, dice.Remaining);
    }

    [Fact]
    public void EveryoneTied_RerollsEveryone_ThenOnlyWhoStillTies()
    {
        // all roll 4 -> everybody re-rolls: 1, 1, 6 -> seat 2 first; seats 0 and 1 still tie -> 2 vs 3.
        var dice = new ScriptedDice(4, 4, 4, 1, 1, 6, 2, 3);

        var ranking = InitialRanking.Determine(3, dice.Roll);

        Assert.Equal([2, 1, 0], ranking.ToArray());
        Assert.Equal(0, dice.Remaining);
    }

    [Fact]
    public void TwoSeparateTies_AreResolvedHighestGroupFirst()
    {
        // seats 0,1 tie on 3 and seats 2,3 tie on 5. The group on 5 re-rolls first (1, 2), then the group on 3 (6, 5).
        var dice = new ScriptedDice(3, 3, 5, 5, 1, 2, 6, 5);

        var ranking = InitialRanking.Determine(4, dice.Roll);

        Assert.Equal([3, 2, 0, 1], ranking.ToArray());
        Assert.Equal(0, dice.Remaining);
    }

    [Fact]
    public void RepeatedFullTies_ThatSplitIntoTwoPairs_AreResolvedStepByStep()
    {
        // 2,2,2,2 and 1,1,1,1 tie completely; 3,3,4,4 splits into pairs {2,3} on 4 and {0,1} on 3.
        // Pair {2,3} re-rolls first: 6 vs 5 -> 2, 3. Pair {0,1} next: 1 vs 2 -> 1, 0.
        var dice = new ScriptedDice(2, 2, 2, 2, 1, 1, 1, 1, 3, 3, 4, 4, 6, 5, 1, 2);

        var ranking = InitialRanking.Determine(4, dice.Roll);

        Assert.Equal([2, 3, 1, 0], ranking.ToArray());
        Assert.Equal(0, dice.Remaining);
    }
}
