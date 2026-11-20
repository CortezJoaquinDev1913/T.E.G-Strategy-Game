using Teg.Domain.Commands;
using Teg.Domain.Events;
using Teg.Domain.Rng;
using Teg.Domain.Rules;
using Teg.Domain.State;
using static Teg.Domain.Tests.Support.GameFixtures;
using static Teg.Domain.Tests.Support.VictoryFixtures;

namespace Teg.Domain.Tests.VictoryRules;

/// <summary>
/// E9 / R§8: victory. The engine decides it right after the action that changes the ownership of countries: the attack that
/// conquers (the country changes owner AT ONCE, so the winning position is already on the board), including the elimination
/// it may cause. Only the ACTIVE participant can win, and the conditions have a priority when several hold together:
/// elimination (every other participant is out), then the common objective (30 countries with four participants, all fifty
/// with two or three), then the secret objective (an occupation one that the conquest completes, a destruction one whose
/// target the OWNER just eliminated). The game ends in <see cref="GamePhase.Finished"/> with the winner and the reason, and
/// accepts no more commands.
/// <para>
/// The scenarios are games on the real board: the attacker (seat 0, unless said otherwise) strikes <see cref="Prey"/> from
/// <see cref="Origin"/>. <c>owned</c> counts the countries of the attacker BEFORE the conquest, so one more after it.
/// </para>
/// </summary>
public sealed class VictoryTests
{
    private const int FourParticipantsCommonObjective = 30;

    /// <summary>The attacker owns <paramref name="owned"/> countries (Origin included), Prey belongs to seat 1; the rest go round-robin to <paramref name="rest"/>.</summary>
    private static GameState Race(int participants, int owned, int[] rest, params (int Seat, Objective Objective)[] objectives)
    {
        List<(string Id, int Owner, int Armies)> held = [Holds(0, Origin, armies: 6), Holds(1, Prey)];
        held.AddRange(HoldsAll(0, CountriesExcept(owned - 1, Origin, Prey)));

        return WithObjectives(Table(participants, 0, rest, held), objectives);
    }

    private static Transition Conquer(GameState before) => Capture(before, Origin, Prey);

    private static void AssertWon(Transition transition, GameState before, int winner, WinReason reason)
    {
        var state = transition.State;

        Assert.Equal(GamePhase.Finished, state.Phase);
        Assert.Equal((int?)winner, state.Winner);
        Assert.Equal((WinReason?)reason, state.WinReason);
        Assert.Equal(new GameWon(winner, reason), transition.Events[^1]); // the last fact of the history
        Assert.Single(transition.Events.OfType<GameWon>());
        Assert.Equal(before.Version + 1, state.Version);
    }

    private static void AssertTheGameGoesOn(Transition transition)
    {
        var state = transition.State;

        Assert.Equal(GamePhase.Attack, state.Phase);
        Assert.Null(state.Winner);
        Assert.Null(state.WinReason);
        Assert.DoesNotContain(transition.Events, e => e is GameWon);
        Assert.NotNull(state.Turn.PendingConquest); // the conquest waits for its occupation, as in any conquest
    }

    // ---- the common objective with four participants: 30 countries -----------------------------------------------------------------

    [Fact]
    public void R8_FourParticipants_TheThirtiethCountryWinsTheGame_ByTheCommonObjective()
    {
        var before = Race(4, owned: FourParticipantsCommonObjective - 1, rest: [1, 2, 3]);
        Assert.Equal(29, Owned(before, 0).Count);

        var transition = Conquer(before);

        AssertWon(transition, before, winner: 0, WinReason.CommonObjective);
        Assert.Equal(30, Owned(transition.State, 0).Count);
        Assert.Equal(
            [typeof(AttackResolved), typeof(TerritoryConquered), typeof(GameWon)],
            transition.Events.Select(e => e.GetType()).ToArray());
    }

    [Theory]
    [InlineData(12, false)]
    [InlineData(28, false)] // 29 after the conquest: one short
    [InlineData(29, true)] // 30
    [InlineData(35, true)] // more than 30 wins as well
    public void R8_FourParticipants_TheCommonObjectiveIsThirtyCountries(int owned, bool wins)
    {
        var before = Race(4, owned, rest: [1, 2, 3]);

        var transition = Conquer(before);

        if (wins)
        {
            AssertWon(transition, before, winner: 0, WinReason.CommonObjective);
        }
        else
        {
            AssertTheGameGoesOn(transition);
        }
    }

    private static readonly Objective[] AttackersObjectives =
        [Objective.None, new Objective.Occupation(4), Destroy(PlayerColor.Rojo), Objective.Common];

    [Theory]
    [InlineData(0)] // no objective
    [InlineData(1)] // an occupation objective far from fulfilled
    [InlineData(2)] // a destruction objective whose target (Rojo, seat 1) is still alive
    [InlineData(3)] // the common objective itself: a destruction objective that fell back
    public void R8_FourParticipants_TheCommonObjectiveAppliesWhateverTheSecretObjectiveIs(int objective)
    {
        var before = Race(4, owned: 29, rest: [1, 2, 3], (0, AttackersObjectives[objective]));

        var transition = Conquer(before);

        AssertWon(transition, before, winner: 0, WinReason.CommonObjective);
    }

    [Fact]
    public void R8_FourParticipants_TheThresholdIsTheTableNotTheLivingCount_TwoEliminatedParticipantsDoNotRaiseIt()
    {
        // Seats 2 and 3 own nothing: with two living participants the bar is still 30 countries, not 50.
        var before = Race(4, owned: 29, rest: [1]);
        Assert.All([2, 3], seat => Assert.True(before.Participants[seat].Eliminated));

        var won = Conquer(before);
        AssertWon(won, before, winner: 0, WinReason.CommonObjective);

        var oneShort = Conquer(Race(4, owned: 28, rest: [1]));
        AssertTheGameGoesOn(oneShort);
    }

    // ---- two and three participants: all the countries ------------------------------------------------------------------------------

    [Theory]
    [InlineData(2, 29)] // 30 countries: not enough with two participants
    [InlineData(3, 29)]
    [InlineData(2, 45)]
    [InlineData(3, 44)]
    public void R8_TwoOrThreeParticipants_ThirtyCountriesOrEvenMoreAreNotEnough(int participants, int owned)
    {
        var before = Race(participants, owned, rest: Enumerable.Range(1, participants - 1).ToArray());

        var transition = Conquer(before);

        Assert.True(Owned(transition.State, 0).Count >= FourParticipantsCommonObjective);
        AssertTheGameGoesOn(transition);
    }

    [Fact]
    public void R8_TwoParticipants_ConqueringTheLastCountryOfTheOpponent_WinsByElimination_WithAllFiftyCountries()
    {
        var before = Race(2, owned: 49, rest: [0]); // seat 1 owns only Prey

        var transition = Conquer(before);

        AssertWon(transition, before, winner: 0, WinReason.Elimination);
        Assert.Equal(RealMap.Territories.Count, Owned(transition.State, 0).Count);
        Assert.True(transition.State.Participants[1].Eliminated);
    }

    [Fact]
    public void R8_ThreeParticipants_EliminatingOneOpponentDoesNotEndTheGame_EliminatingTheSecondDoes()
    {
        // Seat 0 owns 48 countries, seat 1 only Prey, seat 2 only SecondPrey (both border Origin).
        var table = Table(
            3,
            actor: 0,
            rest: [0],
            [Holds(0, Origin, armies: 12), Holds(1, Prey), Holds(2, SecondPrey)]);
        Assert.Equal(48, Owned(table, 0).Count);

        var first = Capture(table, Origin, Prey);
        Assert.True(first.State.Participants[1].Eliminated);
        AssertTheGameGoesOn(first); // seat 2 is still alive

        var occupied = ApplyOk(first.State, new MoveAfterConquest(0, 3)).State;
        var second = Capture(occupied, Origin, SecondPrey);

        AssertWon(second, occupied, winner: 0, WinReason.Elimination);
        Assert.Equal(50, Owned(second.State, 0).Count);
    }

    // ---- priority: elimination beats the common and the secret objectives --------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)] // the kill of Rojo (seat 1) also fulfils this destruction objective
    [InlineData(3)]
    public void R8_FourParticipants_EliminatingTheLastOpponent_WinsByElimination_OverTheCommonAndTheSecretObjectives(int objective)
    {
        // Seats 2 and 3 are out; seat 0 owns 49 countries and takes the last country of seat 1: 50 countries, and the kill.
        var before = WithObjectives(
            Table(4, actor: 0, rest: [0], [Holds(0, Origin, armies: 6), Holds(1, Prey)]),
            (0, AttackersObjectives[objective]));
        Assert.Equal(49, Owned(before, 0).Count);

        var transition = Conquer(before);

        AssertWon(transition, before, winner: 0, WinReason.Elimination);
    }

    // ---- priority: the common objective beats the secret one -----------------------------------------------------------------------------

    [Fact]
    public void R8_WhenOneConquestCompletesTheCommonAndTheSecretObjective_TheReasonIsTheCommonOne()
    {
        // Objective 3 (Asia + 2 of South America) will be complete with Prey (Asia), and so will the thirty countries.
        string[] southAmerica = [.. ContinentOf("america-del-sur").Take(2)];
        var asiaButPrey = ContinentOf("asia").Where(id => id != Prey).ToArray();
        var thirteenMore = CountriesExcept(13, [.. ContinentOf("asia"), .. southAmerica, .. ContinentOf("america-del-sur")]);
        List<(string Id, int Owner, int Armies)> held = [Holds(1, Prey)];
        held.AddRange(HoldsAll(0, [.. asiaButPrey.Where(id => id != Origin), .. southAmerica, .. thirteenMore]));
        held.Add(Holds(0, Origin, armies: 6));
        var before = WithObjectives(Table(4, 0, [1, 2, 3], held), (0, new Objective.Occupation(3)));
        Assert.Equal(29, Owned(before, 0).Count);
        Assert.False(Objectives.IsFulfilled(new Objective.Occupation(3), Owned(before, 0), RealMap));

        var transition = Conquer(before);

        Assert.True(Objectives.IsFulfilled(new Objective.Occupation(3), Owned(transition.State, 0), RealMap)); // both hold
        AssertWon(transition, before, winner: 0, WinReason.CommonObjective);
    }

    private static string[] ContinentOf(string continentId) =>
        RealMap.Continents.Single(c => c.Id == continentId).TerritoryIds.ToArray();

    // ---- the secret objective: occupation -------------------------------------------------------------------------------------------------

    /// <summary>The clauses of R§8 for objectives 1 to 9, typed here independently from the production code.</summary>
    private static readonly Dictionary<int, (string Continent, int Count)[]> Clauses = new()
    {
        [1] = [("africa", 6), ("america-del-norte", 5), ("europa", 4)],
        [2] = [("america-del-sur", 6), ("europa", 7)], // plus a connected triple (below)
        [3] = [("asia", 15), ("america-del-sur", 2)],
        [4] = [("europa", 9), ("asia", 4), ("america-del-sur", 2)],
        [5] = [("america-del-norte", 10), ("oceania", 2), ("asia", 4)],
        [6] = [("oceania", 2), ("africa", 2), ("america-del-sur", 2), ("europa", 3), ("america-del-norte", 4), ("asia", 3)],
        [7] = [("oceania", 4), ("america-del-norte", 10), ("europa", 2)],
        [8] = [("america-del-sur", 6), ("africa", 6), ("asia", 4)],
        [9] = [("oceania", 4), ("africa", 6), ("america-del-norte", 5)],
    };

    /// <summary>Three Asian countries bordering one another: the "3 connected countries" of objective 2.</summary>
    private static readonly string[] ConnectedTriple = ["china", "gobi", "mongolia"];

    /// <summary>The countries of the clauses (the first ones of each continent in map order), plus the connected triple for objective 2.</summary>
    private static (List<string> Quota, List<string> Clause) QuotaOf(int number)
    {
        var clause = Clauses[number].SelectMany(c => ContinentOf(c.Continent).Take(c.Count)).ToList();
        List<string> quota = [.. clause];
        if (number == 2)
        {
            quota.AddRange(ConnectedTriple);
        }

        return (quota, clause);
    }

    /// <summary>
    /// A country of the clauses to conquer (<c>Prey</c>) and an owned neighbour to strike from, so that owning the quota but
    /// that country is one short of the objective.
    /// </summary>
    private static (string Origin, string Prey) StrikePoints(int number)
    {
        var (quota, clause) = QuotaOf(number);
        foreach (var prey in clause)
        {
            var origin = quota.FirstOrDefault(country => country != prey && RealMap.AreAdjacent(country, prey));
            if (origin is not null)
            {
                return (origin, prey);
            }
        }

        throw new InvalidOperationException($"No country of objective {number} borders another one of its quota.");
    }

    /// <summary>
    /// The attacker owns <paramref name="owned"/> (striking from <paramref name="origin"/> with 6 armies), seat 1 owns
    /// <paramref name="prey"/> and the rest of the world is spread over seats 1 to 3: nobody is near thirty countries.
    /// </summary>
    private static GameState OccupationGame(int number, IEnumerable<string> owned, string origin, string prey)
    {
        List<(string Id, int Owner, int Armies)> held = [Holds(1, prey)];
        held.AddRange(owned.Select(id => Holds(0, id, id == origin ? 6 : 1)));

        return WithObjectives(Table(4, 0, [1, 2, 3], held), (0, new Objective.Occupation(number)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void R8_AnOccupationObjectiveCompletedByTheConquest_WinsBySecretObjective(int number)
    {
        var (origin, prey) = StrikePoints(number);
        var owned = QuotaOf(number).Quota.Where(id => id != prey).ToList();
        var before = OccupationGame(number, owned, origin, prey);
        Assert.False(Objectives.IsFulfilled(new Objective.Occupation(number), Owned(before, 0), RealMap)); // not yet...

        var transition = Capture(before, origin, prey);

        Assert.True(Owned(transition.State, 0).Count < FourParticipantsCommonObjective); // so the common objective is not the cause
        AssertWon(transition, before, winner: 0, WinReason.SecretObjective);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void R8_AnOccupationObjectiveStillOneCountryShortAfterTheConquest_DoesNotWin(int number)
    {
        var (origin, prey) = StrikePoints(number);
        var quota = QuotaOf(number).Quota;
        var missing = quota.Last(id => id != prey && id != origin); // one more country the attacker does not own
        var before = OccupationGame(number, quota.Where(id => id != prey && id != missing), origin, prey);

        var transition = Capture(before, origin, prey);

        Assert.False(Objectives.IsFulfilled(new Objective.Occupation(number), Owned(transition.State, 0), RealMap));
        AssertTheGameGoesOn(transition);
    }

    // ---- the secret objective: destruction ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 3)]
    [InlineData(2, 0)]
    [InlineData(3, 2)]
    public void R8_TheOwnerEliminatingTheTarget_WinsBySecretObjective(int owner, int victim)
    {
        var before = Kill(owner, victim, (owner, Destroy(Colours[victim])));

        var transition = Capture(before, Origin, Prey);

        Assert.True(transition.State.Participants[victim].Eliminated);
        Assert.Equal(3, transition.State.Participants.Count(p => !p.Eliminated)); // not an elimination victory: two others are alive
        AssertWon(transition, before, winner: owner, WinReason.SecretObjective);
    }

    [Fact]
    public void R8_TheOwnerEliminatingAnotherColour_DoesNotWin()
    {
        // Seat 0 must destroy Rojo and eliminates Negro: no victory, and its objective is untouched.
        var before = Kill(0, 2, (0, Destroy(PlayerColor.Rojo)));

        var transition = Capture(before, Origin, Prey);

        Assert.True(transition.State.Participants[2].Eliminated);
        AssertTheGameGoesOn(transition);
        Assert.Equal(Destroy(PlayerColor.Rojo), transition.State.Participants[0].Objective);
    }

    [Fact]
    public void R8_AnotherParticipantEliminatingTheTarget_DoesNotMakeTheHolderWin_ItPlaysForTheCommonObjective()
    {
        var before = Kill(0, 2, (1, Destroy(PlayerColor.Negro))); // seat 0 eliminates Negro, seat 1 is the holder

        var transition = Capture(before, Origin, Prey);

        AssertTheGameGoesOn(transition);
        Assert.Equal(Objective.Common, transition.State.Participants[1].Objective);
    }

    [Fact]
    public void R8_ARedirectedDestructionObjective_IsWonByEliminatingItsFinalTarget()
    {
        // Seat 1 drew "destroy Rojo" (its own colour): the objective aims at Azul, on its right. Seat 1 acts and eliminates Azul.
        IReadOnlySet<string>[] nobodyOwnsAnything = [.. Enumerable.Range(0, 4).Select(_ => (IReadOnlySet<string>)new HashSet<string>())];
        var redirected = Objectives.Assign(Colours, nobodyOwnsAnything, [1, 11, 2, 3], RealMap)[1];
        Assert.Equal(new Objective.Destruction(11, PlayerColor.Azul), redirected);
        var before = Kill(1, 0, (1, redirected));

        var transition = Capture(before, Origin, Prey);

        AssertWon(transition, before, winner: 1, WinReason.SecretObjective);
    }

    // ---- only the active participant can win -------------------------------------------------------------------------------------------------

    [Fact]
    public void R8_AnOccupationObjectiveAlreadyFulfilledByAnotherParticipant_DoesNotWin_OnTheTurnOfTheActiveOne()
    {
        // Seat 1 owns Oceania, Africa and 5 countries of North America (objective 9) and holds it; seat 0, the active one, conquers elsewhere.
        var seatOnes = QuotaOf(9).Quota;
        List<(string Id, int Owner, int Armies)> held = [Holds(0, Origin, armies: 6), Holds(2, Prey)];
        held.AddRange(HoldsAll(1, seatOnes));
        var before = WithObjectives(Table(4, 0, [0, 2, 3], held), (1, new Objective.Occupation(9)));
        Assert.True(Objectives.IsFulfilled(new Objective.Occupation(9), Owned(before, 1), RealMap)); // it IS fulfilled, on seat 0's turn

        var transition = Conquer(before);

        AssertTheGameGoesOn(transition);
    }

    [Fact]
    public void R8_AParticipantWithThirtyCountries_DoesNotWin_OnAnotherParticipantsTurn()
    {
        // Seat 1 owns 31 countries; seat 0 (the active one) takes Prey from seat 2.
        List<(string Id, int Owner, int Armies)> held = [Holds(0, Origin, armies: 6), Holds(2, Prey)];
        held.AddRange(HoldsAll(1, CountriesExcept(31, Origin, Prey)));
        var before = Table(4, 0, [0, 2, 3], held);
        Assert.Equal(31, Owned(before, 1).Count);

        var transition = Conquer(before);

        AssertTheGameGoesOn(transition);
    }

    // ---- the state after a victory -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void R8_AWinningConquest_LeavesTheBoardAsTheConquestMadeIt_AndClearsThePendingOccupation()
    {
        var before = Race(4, owned: 29, rest: [1, 2, 3]);

        var transition = Conquer(before);
        var state = transition.State;

        // The conquering army is already on the country: the board is valid and nothing is left to decide.
        Assert.Null(state.Turn.PendingConquest);
        Assert.Equal(1, state.Turn.ConquestsThisTurn);
        Assert.Equal(0, state.FindTerritory(Prey)!.Owner);
        Assert.Equal(1, state.FindTerritory(Prey)!.Armies);
        Assert.Equal(5, state.FindTerritory(Origin)!.Armies); // 6, minus the conquering army: the attacker lost no army
        Assert.Equal(0, state.ActiveSeat); // the winner stays the active seat
        Assert.All(state.Participants, p => Assert.False(p.Eliminated)); // seat 1 still owns other countries
    }

    [Fact]
    public void R8_ANonWinningConquest_KeepsThePendingOccupation_AsAlways()
    {
        var transition = Conquer(Race(4, owned: 12, rest: [1, 2, 3]));

        Assert.Equal(new PendingConquest(Origin, Prey), transition.State.Turn.PendingConquest);
        AssertTheGameGoesOn(transition);
    }

    /// <summary>One of every command the engine knows, for <paramref name="seat"/>.</summary>
    private static GameCommand[] EveryCommand(int seat) =>
    [
        Place(seat, ("alaska", 1)),
        Reinforcing(seat, ("alaska", 1)),
        Trading(seat, "alaska", "china", "brasil"),
        new StartRegroup(seat),
        new Attack(seat, Origin, SecondPrey),
        new MoveAfterConquest(seat, 1),
        new Regroup(seat, Origin, SecondPrey, 1),
        new EndTurn(seat),
    ];

    public static TheoryData<int, int> EveryCommandFromTheWinnerAndFromAnotherSeat()
    {
        var data = new TheoryData<int, int>();
        for (var command = 0; command < EveryCommand(0).Length; command++)
        {
            data.Add(command, 0); // the winner, who is still the active seat
            data.Add(command, 1); // someone else
        }

        return data;
    }

    [Fact]
    public void R8_TheRejectedCommandsTheory_CoversEveryCommandTypeOfTheEngine()
    {
        var concrete = typeof(GameCommand).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(GameCommand)) && !t.IsAbstract).ToHashSet();

        Assert.Equal(concrete, EveryCommand(0).Select(c => c.GetType()).ToHashSet());
    }

    [Theory]
    [MemberData(nameof(EveryCommandFromTheWinnerAndFromAnotherSeat))]
    public void R8_AfterTheVictory_EveryCommandIsRejectedAsGameFinished_AndTheStateIsUntouched(int command, int seat)
    {
        AssertRejected(
            RuleViolations.GameFinished,
            () => Conquer(Race(4, owned: 29, rest: [1, 2, 3])).State,
            _ => EveryCommand(seat)[command]);
    }

    // ---- determinism --------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void R10_TheSameGameAndTheSameCommand_GiveTheSameWinnerAndTheSameEvents()
    {
        var first = Conquer(Race(4, owned: 29, rest: [1, 2, 3]));
        var second = Conquer(Race(4, owned: 29, rest: [1, 2, 3]));

        Assert.Equal(first.State, second.State);
        Assert.Equal(first.Events.ToArray(), second.Events.ToArray());
        Assert.Equal((int?)0, second.State.Winner);
        Assert.Equal((WinReason?)WinReason.CommonObjective, second.State.WinReason);
    }

    // ---- the engine against a model, over many random scenarios --------------------------------------------------------------------------------

    private sealed record Scenario(GameState Before, int Actor, int Victim, Objective Objective);

    private static Scenario RandomScenario(SeededRng choices)
    {
        var participants = 2 + choices.NextInt(3);
        var actor = choices.NextInt(participants);
        var victim = (actor + 1 + choices.NextInt(participants - 1)) % participants;
        List<int> alive = [];
        foreach (var seat in Enumerable.Range(0, participants).Where(seat => seat != actor && seat != victim))
        {
            if (choices.NextInt(2) == 0)
            {
                alive.Add(seat);
            }
        }

        var keepsAnother = choices.NextInt(3) == 0; // the victim also owns SecondPrey: the conquest does not eliminate it
        var extra = keepsAnother ? 1 : 0;
        var wanted = new[] { 5, 12, 28, 29, 30, 44 }[choices.NextInt(6)];
        var owned = alive.Count == 0 ? 49 - extra : Math.Min(wanted, 49 - extra - alive.Count);

        string[] kept = keepsAnother ? [Origin, Prey, SecondPrey] : [Origin, Prey];
        List<(string Id, int Owner, int Armies)> held = [Holds(actor, Origin, armies: 6), Holds(victim, Prey)];
        if (keepsAnother)
        {
            held.Add(Holds(victim, SecondPrey));
        }

        held.AddRange(HoldsAll(actor, CountriesExcept(owned - 1, kept)));

        // Only four-participant games have secret objectives (R§8).
        Objective objective = participants < 4
            ? Objective.None
            : choices.NextInt(5) switch
            {
                0 => Objective.None,
                1 => new Objective.Occupation(1 + choices.NextInt(9)),
                2 => Destroy(Colours[victim]),
                3 => Destroy(Colours[(actor + 1 + choices.NextInt(3)) % 4]),
                _ => Objective.Common,
            };
        var table = Table(participants, actor, alive.Count > 0 ? [.. alive] : [actor], held);

        return new Scenario(WithObjectives(table, (actor, objective)), actor, victim, objective);
    }

    /// <summary>R§8 written as a plain rule over the board after the conquest, independent from the engine's flags.</summary>
    private static WinReason? Model(Scenario scenario, GameState after)
    {
        var owned = Owned(after, scenario.Actor);
        if (owned.Count == RealMap.Territories.Count)
        {
            return WinReason.Elimination; // every other country, hence every other participant, is gone
        }

        if (owned.Count >= (after.Participants.Count == 4 ? FourParticipantsCommonObjective : RealMap.Territories.Count))
        {
            return WinReason.CommonObjective;
        }

        var victimIsOut = after.Territories.All(t => t.Owner != scenario.Victim);
        var secret = scenario.Objective switch
        {
            Objective.Occupation occupation => Objectives.IsFulfilled(occupation, owned, RealMap),
            Objective.Destruction destruction => victimIsOut && Colours[scenario.Victim] == destruction.Target,
            _ => false,
        };

        return secret ? WinReason.SecretObjective : null;
    }

    [Fact]
    public void R8_OverRandomScenarios_TheEngineDecidesVictoryAsTheRuleDoes()
    {
        var choices = new SeededRng(8_005);
        var outcomes = new Dictionary<string, int>();

        for (var run = 0; run < 1500; run++)
        {
            var scenario = RandomScenario(choices);

            var transition = Conquer(scenario.Before);

            var expected = Model(scenario, transition.State);
            Assert.Equal(expected, transition.State.WinReason);
            Assert.Equal(expected is null ? (int?)null : scenario.Actor, transition.State.Winner);
            Assert.Equal(expected is null ? GamePhase.Attack : GamePhase.Finished, transition.State.Phase);
            Assert.Equal(expected is null ? 0 : 1, transition.Events.OfType<GameWon>().Count());
            var outcome = expected?.ToString() ?? "GoesOn";
            outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;
        }

        // Every outcome happens in the sweep, so the agreement is not vacuous. Measured over these 1500 scenarios: 677 games go
        // on, 571 eliminations, 216 common wins, 36 secret wins; the floors leave a wide margin.
        Assert.True(outcomes.GetValueOrDefault("GoesOn") > 300, $"Only {outcomes.GetValueOrDefault("GoesOn")} games went on.");
        Assert.True(outcomes.GetValueOrDefault("Elimination") > 250, $"Only {outcomes.GetValueOrDefault("Elimination")} eliminations.");
        Assert.True(outcomes.GetValueOrDefault("CommonObjective") > 100, $"Only {outcomes.GetValueOrDefault("CommonObjective")} common wins.");
        Assert.True(outcomes.GetValueOrDefault("SecretObjective") > 15, $"Only {outcomes.GetValueOrDefault("SecretObjective")} secret wins.");
    }
}
