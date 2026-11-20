using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Events;
using Teg.Domain.Rules;
using Teg.Domain.State;
using static Teg.Domain.Tests.Support.CombatFixtures;
using static Teg.Domain.Tests.Support.GameFixtures;

namespace Teg.Domain.Tests.Support;

/// <summary>
/// Builders of the destruction and victory tests: games on the REAL board (docs/mapa.json), because the common objective
/// counts countries (30 of 50) and the secret objectives name real continents. Every scenario is Ataque of round 2 with a
/// chosen participant acting; the attacker always strikes from <see cref="Origin"/> to <see cref="Prey"/> (they border by
/// land), so a scenario only has to say who owns what.
/// </summary>
internal static class VictoryFixtures
{
    /// <summary>The country the attacker strikes from.</summary>
    public const string Origin = "china";

    /// <summary>The country the attacker conquers (borders <see cref="Origin"/> by land).</summary>
    public const string Prey = "mongolia";

    /// <summary>A second country bordering <see cref="Origin"/> and <see cref="Prey"/> by land: the next target of a double kill.</summary>
    public const string SecondPrey = "gobi";

    /// <summary>The colours of the four seats of every scenario (the ones <see cref="InPlay"/> gives them).</summary>
    public static readonly PlayerColor[] Colours =
        [PlayerColor.Azul, PlayerColor.Rojo, PlayerColor.Negro, PlayerColor.Amarillo];

    /// <summary>The destruction objective that names <paramref name="target"/>: 10 azul, 11 rojo, 12 negro, 13 amarillo (R§8).</summary>
    public static Objective.Destruction Destroy(PlayerColor target) => new(10 + (int)target, target);

    /// <summary>A country, its owner and its armies: one entry of a scenario.</summary>
    public static (string Id, int Owner, int Armies) Holds(int owner, string id, int armies = 1) => (id, owner, armies);

    /// <summary>The countries of <paramref name="ids"/>, owned by <paramref name="owner"/> with one army each.</summary>
    public static IEnumerable<(string Id, int Owner, int Armies)> HoldsAll(int owner, IEnumerable<string> ids) =>
        ids.Select(id => Holds(owner, id));

    /// <summary>The first <paramref name="count"/> countries of the map in map order that are NOT in <paramref name="except"/>.</summary>
    public static string[] CountriesExcept(int count, params string[] except) =>
        RealMap.Territories.Select(t => t.Id).Except(except).Take(count).ToArray();

    /// <summary>
    /// Ataque of round 2 on the real board, <paramref name="actor"/> acting. <paramref name="held"/> fixes the owner and the
    /// armies of the countries it names; every OTHER country (one army) is dealt round-robin to the seats of
    /// <paramref name="rest"/>. A participant left with no country is eliminated, as the rules require (R§2). Nobody has an
    /// objective yet (see <see cref="WithObjectives"/>).
    /// </summary>
    public static GameState Table(
        int participants,
        int actor,
        int[] rest,
        IEnumerable<(string Id, int Owner, int Armies)> held)
    {
        var fixedHoldings = held.ToDictionary(h => h.Id);
        Assert.Empty(fixedHoldings.Keys.Except(RealMap.Territories.Select(t => t.Id))); // a typo must not silently shrink the set

        var territories = new List<TerritoryState>();
        var dealt = 0;
        foreach (var country in RealMap.Territories)
        {
            if (fixedHoldings.TryGetValue(country.Id, out var holding))
            {
                territories.Add(new TerritoryState(country.Id, holding.Owner, holding.Armies));
            }
            else
            {
                territories.Add(new TerritoryState(country.Id, rest[dealt % rest.Length], Armies: 1));
                dealt++;
            }
        }

        var skeleton = InPlay(participants, GamePhase.Attack, round: 2, activeSeat: actor);

        return skeleton with
        {
            Territories = territories.ToValueArray(),
            Participants = skeleton.Participants
                .Select(p => p with { Eliminated = territories.All(t => t.Owner != p.Seat) })
                .ToValueArray(),
        };
    }

    /// <summary>
    /// Four seats. The victim owns ONLY <see cref="Prey"/>; the attacker strikes from <see cref="Origin"/> (6 armies); the rest
    /// of the countries are spread over the living seats, so conquering <see cref="Prey"/> eliminates the victim and nobody
    /// comes near thirty countries. <paramref name="objectives"/> gives some seats an objective.
    /// </summary>
    public static GameState Kill(int attacker, int victim, params (int Seat, Objective Objective)[] objectives)
    {
        int[] living = [.. Enumerable.Range(0, 4).Where(seat => seat != victim)];
        var table = Table(4, attacker, living, [Holds(attacker, Origin, armies: 6), Holds(victim, Prey)]);

        return WithObjectives(table, objectives);
    }

    /// <summary>The same state with these objectives: <c>objectives[seat]</c> is the objective of that seat.</summary>
    public static GameState WithObjectives(GameState state, IReadOnlyList<Objective> objectives)
    {
        Assert.Equal(state.Participants.Count, objectives.Count);

        return state with
        {
            Participants = state.Participants
                .Select(p => p with { Objective = objectives[p.Seat] })
                .ToValueArray(),
        };
    }

    /// <summary>Gives these seats these objectives, everybody else none.</summary>
    public static GameState WithObjectives(GameState state, params (int Seat, Objective Objective)[] given) =>
        WithObjectives(
            state,
            Enumerable.Range(0, state.Participants.Count)
                .Select(seat => given.Where(g => g.Seat == seat).Select(g => g.Objective).SingleOrDefault() ?? Objective.None)
                .ToArray());

    /// <summary>
    /// The attack from <paramref name="from"/> to <paramref name="to"/> on the real board, with dice that empty the destination
    /// (the search of <see cref="WithDice"/>): the transition of the conquering attack, the occupation left pending.
    /// </summary>
    public static Transition Capture(GameState state, string from, string to)
    {
        var rigged = WithDice(state, from, to, Conquers(state.FindTerritory(to)!.Armies));

        return ApplyOk(rigged, new Attack(state.ActiveSeat, from, to));
    }

    /// <summary>The ids of the countries a seat owns.</summary>
    public static HashSet<string> Owned(GameState state, int seat) => OwnedBy(state, seat).ToHashSet();

    /// <summary>
    /// R§8 checked from the outside on ANY accepted command of a game on the real board (the playout tests call it after every
    /// step): only a conquest can end the game; a conquest that leaves the active participant as the last one standing, with the
    /// countries of the common objective (30 with four participants, all of them otherwise) or with their secret objective met
    /// ends it, and nothing else does; the winner and the reason are in the state and in the closing <see cref="GameWon"/>, and
    /// the reason is the first that holds in the order elimination, common objective, secret objective.
    /// </summary>
    public static void AssertVictoryFollowsTheRules(GameState before, Transition transition)
    {
        var state = transition.State;
        var actor = before.ActiveSeat;
        var won = transition.Events.OfType<GameWon>().SingleOrDefault();

        if (!transition.Events.Any(e => e is TerritoryConquered))
        {
            Assert.Null(won); // only the conquest of a country can win the game
            Assert.Null(state.Winner);
            Assert.Null(state.WinReason);

            return;
        }

        var owned = Owned(state, actor);
        var everyoneElseIsOut = state.Participants.All(p => p.Seat == actor || p.Eliminated);
        var commonObjective = owned.Count >= (state.Participants.Count == 4 ? 30 : state.Territories.Count);
        var secretObjective = state.Participants[actor].Objective switch
        {
            Objective.Occupation occupation => Objectives.IsFulfilled(occupation, owned, RealMap),
            Objective.Destruction destruction => transition.Events
                .OfType<ParticipantEliminated>()
                .Any(kill => kill.By == actor && state.Participants[kill.Eliminated].Color == destruction.Target),
            _ => false,
        };
        WinReason? expected = everyoneElseIsOut ? WinReason.Elimination
            : commonObjective ? WinReason.CommonObjective
            : secretObjective ? WinReason.SecretObjective
            : null;

        Assert.Equal(expected, state.WinReason);
        Assert.Equal(expected is null ? (int?)null : actor, state.Winner);
        Assert.Equal(expected is null ? 0 : 1, transition.Events.OfType<GameWon>().Count());
        Assert.Equal(expected is not null, state.Phase == GamePhase.Finished);
        if (won is not null)
        {
            Assert.Equal(new GameWon(actor, expected!.Value), transition.Events[^1]);
            Assert.Null(state.Turn.PendingConquest);
        }
    }
}
