namespace Teg.Domain.Setup;

/// <summary>
/// R§3: the initial ranking. Every participant rolls one die (in seat order); the highest roll ranks first.
/// Ties are re-rolled ONLY among the tied seats, repeatedly, until every position is strict. When several
/// groups tie, the group with the higher roll re-rolls first.
/// </summary>
public static class InitialRanking
{
    /// <param name="participantCount">Seats are <c>0..participantCount-1</c>.</param>
    /// <param name="rollDie">Returns a die value (1-6); called once per contender per round of rolling.</param>
    /// <returns>The seats from rank 1 to the last rank.</returns>
    public static IReadOnlyList<int> Determine(int participantCount, Func<int> rollDie)
    {
        var ranking = new List<int>(participantCount);
        Resolve(Enumerable.Range(0, participantCount).ToList(), rollDie, ranking);

        return ranking;
    }

    private static void Resolve(List<int> contenders, Func<int> rollDie, List<int> ranking)
    {
        if (contenders.Count == 1)
        {
            ranking.Add(contenders[0]);
            return;
        }

        // ToList() forces the rolls to happen now, in seat order.
        var rolls = contenders.Select(seat => (Seat: seat, Roll: rollDie())).ToList();

        foreach (var tier in rolls.GroupBy(r => r.Roll).OrderByDescending(group => group.Key))
        {
            Resolve(tier.Select(r => r.Seat).ToList(), rollDie, ranking);
        }
    }
}
