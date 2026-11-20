using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Events;
using Teg.Domain.Rng;
using Teg.Domain.Rules;
using Teg.Domain.State;
using Teg.Domain.Tests.Support;
using static Teg.Domain.Tests.Support.CombatFixtures;
using static Teg.Domain.Tests.Support.GameFixtures;
using DiceRules = Teg.Domain.Rules.Combat; // the namespace of these tests is also called Combat and would hide the class

namespace Teg.Domain.Tests.Combat;

/// <summary>
/// E5 / R§6, the dice. Part 1 tests the pure resolution directly with scripted dice: both sides sorted from the
/// highest die to the lowest and compared pair by pair, only min(attacker dice, defender dice) pairs count, a TIE goes
/// to the DEFENDER and every lost pair costs its loser exactly one army. Part 2 goes through
/// <see cref="GameEngine.Apply"/>: the attacker rolls min(3, origin - 1) dice and the defender min(3, destination), both
/// from the seeded RNG of the state (ATTACKER dice first, then the defender dice, one draw per die), the event stores
/// both lists SORTED FROM THE HIGHEST DIE TO THE LOWEST (the order they are compared in) with the losses, and replaying
/// the same state gives the same fight. Conquest has its own tests (ConquestTests, EliminationTests).
/// </summary>
public sealed class CombatTests
{
    private static readonly Attack AttackC = new(0, "a", "c");

    private static readonly (int Origin, int Destination)[] Matchups =
    [
        (2, 1), (2, 4), (2, 9), (3, 2), (3, 5), (4, 3), (4, 9), (9, 4), (9, 1), (50, 6),
    ];

    private static AttackResolved ResolutionOf(Transition transition) =>
        Assert.Single(transition.Events.OfType<AttackResolved>());

    private static IEnumerable<int[]> AllRolls(int dice) =>
        Enumerable.Range(0, (int)Math.Pow(6, dice))
            .Select(index => Enumerable.Range(0, dice)
                .Select(position => index / (int)Math.Pow(6, position) % 6 + 1)
                .ToArray());

    private static IEnumerable<int[]> Permutations(int[] items) =>
        items.Length <= 1
            ? [items]
            : items.SelectMany((item, index) => Permutations(items.Where((_, other) => other != index).ToArray())
                .Select(rest => new[] { item }.Concat(rest).ToArray()));

    // ---- part 1: how many dice -------------------------------------------------------------------

    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 3)]
    [InlineData(5, 3)]
    [InlineData(40, 3)]
    public void R6_TheAttackerRollsOneDieLessThanItsArmies_AtMostThree(int originArmies, int dice)
    {
        Assert.Equal(dice, DiceRules.AttackerDiceCount(originArmies));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 3)]
    [InlineData(50, 3)]
    public void R6_TheDefenderRollsOneDiePerArmy_AtMostThree(int destinationArmies, int dice)
    {
        Assert.Equal(dice, DiceRules.DefenderDiceCount(destinationArmies));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-3)]
    public void AnOriginThatCannotAttack_HasNoDiceCount(int originArmies)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DiceRules.AttackerDiceCount(originArmies));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ADestinationWithoutArmies_HasNoDiceCount(int destinationArmies)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DiceRules.DefenderDiceCount(destinationArmies));
    }

    // ---- part 1: comparing the dice -----------------------------------------------------------------

    [Theory]
    [InlineData(new[] { 5, 3, 1 }, new[] { 4, 3, 2 }, 2, 1)] // 5>4 attacker wins; 3=3 tie, defender wins; 1<2 defender wins
    [InlineData(new[] { 6, 3, 1 }, new[] { 4, 3 }, 1, 1)] // 6>4 attacker wins; 3=3 tie, defender wins; the lone 1 is ignored
    [InlineData(new[] { 5, 3 }, new[] { 5, 3 }, 2, 0)] // the tie scenario of the spec: the attacker loses BOTH
    [InlineData(new[] { 6, 6, 6 }, new[] { 1, 1, 1 }, 0, 3)]
    [InlineData(new[] { 1, 1, 1 }, new[] { 6, 6, 6 }, 3, 0)]
    [InlineData(new[] { 6, 1 }, new[] { 5, 2 }, 1, 1)] // one pair each way
    public void R6_TheDiceAreComparedPairByPair_TheHighestAgainstTheHighest(
        int[] attacker,
        int[] defender,
        int attackerLosses,
        int defenderLosses)
    {
        Assert.Equal(new DiceOutcome(attackerLosses, defenderLosses), DiceRules.Resolve(attacker, defender));
    }

    [Fact]
    public void R6_TheOrderTheDiceAreGivenIn_DoesNotMatter_BothSidesAreSortedFromTheHighestFirst()
    {
        var combinations = 0;

        foreach (var attacker in Permutations([5, 3, 1]))
        {
            foreach (var defender in Permutations([4, 3, 2]))
            {
                Assert.Equal(new DiceOutcome(2, 1), DiceRules.Resolve(attacker, defender));
                combinations++;
            }
        }

        Assert.Equal(36, combinations); // 6 orders of each side
    }

    [Theory]
    [InlineData(new[] { 6, 6, 6 }, new[] { 1 }, 0, 1)] // one pair only: the other two attacker dice are ignored
    [InlineData(new[] { 1, 1, 1 }, new[] { 6 }, 1, 0)] // ...so the attacker loses ONE army, not three
    [InlineData(new[] { 1 }, new[] { 6, 6, 6 }, 1, 0)] // the extra defender dice are ignored too
    [InlineData(new[] { 6 }, new[] { 1, 1, 1 }, 0, 1)]
    [InlineData(new[] { 6, 2 }, new[] { 4, 3, 1 }, 1, 1)] // 6>4, 2<3 and the extra 1 is ignored
    [InlineData(new[] { 3, 3 }, new[] { 6, 5, 5 }, 2, 0)]
    public void R6_OnlyAsManyPairsAsTheSideWithFewerDiceHas_AreCompared(
        int[] attacker,
        int[] defender,
        int attackerLosses,
        int defenderLosses)
    {
        Assert.Equal(new DiceOutcome(attackerLosses, defenderLosses), DiceRules.Resolve(attacker, defender));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void R6_ATieGoesToTheDefender_EvenOneAgainstOne(int face)
    {
        Assert.Equal(new DiceOutcome(AttackerLosses: 1, DefenderLosses: 0), DiceRules.Resolve([face], [face]));
    }

    [Theory]
    [InlineData(new[] { 4, 4 }, new[] { 4, 4 }, 2)]
    [InlineData(new[] { 6, 2, 1 }, new[] { 1, 2, 6 }, 3)] // equal dice in a different order still tie pair by pair
    [InlineData(new[] { 5, 5, 5 }, new[] { 5, 5, 5 }, 3)]
    public void R6_EveryTiedPair_IsLostByTheAttacker(int[] attacker, int[] defender, int attackerLosses)
    {
        Assert.Equal(new DiceOutcome(attackerLosses, 0), DiceRules.Resolve(attacker, defender));
    }

    [Fact]
    public void R6_EveryCombinationOfOneToThreeDiceAgainstOneToThree_AgreesWithAnIndependentOracle_AndEachPairCostsOneArmy()
    {
        var combinations = 0;

        for (var attackerDice = 1; attackerDice <= 3; attackerDice++)
        {
            for (var defenderDice = 1; defenderDice <= 3; defenderDice++)
            {
                foreach (var attacker in AllRolls(attackerDice))
                {
                    foreach (var defender in AllRolls(defenderDice))
                    {
                        var outcome = DiceRules.Resolve(attacker, defender);

                        Assert.Equal(Oracle(attacker, defender), (outcome.AttackerLosses, outcome.DefenderLosses));
                        Assert.Equal(Math.Min(attackerDice, defenderDice), outcome.AttackerLosses + outcome.DefenderLosses);
                        combinations++;
                    }
                }
            }
        }

        // (6^1 + 6^2 + 6^3)^2: the loops really ran over every roll of every size.
        Assert.Equal(66_564, combinations);
    }

    [Fact]
    public void Resolve_DoesNotChangeTheListsItIsGiven()
    {
        int[] attacker = [1, 5, 3];
        int[] defender = [2, 4, 3];

        DiceRules.Resolve(attacker, defender);

        Assert.Equal([1, 5, 3], attacker);
        Assert.Equal([2, 4, 3], defender);
    }

    [Theory]
    [InlineData(new int[0], new[] { 3 })]
    [InlineData(new[] { 3 }, new int[0])]
    [InlineData(new[] { 1, 2, 3, 4 }, new[] { 3 })]
    [InlineData(new[] { 3 }, new[] { 1, 2, 3, 4 })]
    [InlineData(new[] { 0 }, new[] { 3 })]
    [InlineData(new[] { 3 }, new[] { 7 })]
    public void Resolve_RefusesDiceThatNobodyCanRoll(int[] attacker, int[] defender)
    {
        Assert.ThrowsAny<ArgumentException>(() => DiceRules.Resolve(attacker, defender));
    }

    // ---- part 2: through the engine ------------------------------------------------------------------

    [Theory]
    [InlineData(2, 1, 1, 1)]
    [InlineData(2, 5, 1, 3)]
    [InlineData(3, 2, 2, 2)]
    [InlineData(4, 3, 3, 3)]
    [InlineData(9, 7, 3, 3)]
    [InlineData(9, 1, 3, 1)]
    public void R6_TheEngineRollsMinThreeOriginMinusOneDiceForTheAttacker_AndMinThreeDestinationForTheDefender(
        int origin,
        int destination,
        int attackerDice,
        int defenderDice)
    {
        var resolved = ResolutionOf(ApplyOk(Arena(origin, destination), AttackC, TinyBoard.Map));

        Assert.Equal(attackerDice, resolved.AttackerDice.Count);
        Assert.Equal(defenderDice, resolved.DefenderDice.Count);
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(2L)]
    [InlineData(3L)]
    [InlineData(42L)]
    [InlineData(1234567L)]
    [InlineData(-9L)]
    [InlineData(long.MaxValue)]
    public void R6_TheDiceComeFromTheSeededRngOfTheState_AttackerDiceFirstThenTheDefenderDice(long seed)
    {
        var state = Arena(8, 5) with { Rng = new RngState(seed, Draws: 7) }; // 7 draws were taken before this attack
        var (attacker, defender, after) = ReplayDice(state.Rng, attackerDice: 3, defenderDice: 3);

        var transition = ApplyOk(state, AttackC, TinyBoard.Map);

        var resolved = ResolutionOf(transition);
        Assert.Equal(attacker, resolved.AttackerDice.ToArray());
        Assert.Equal(defender, resolved.DefenderDice.ToArray());
        Assert.Equal(after, transition.State.Rng);
        Assert.Equal(seed, transition.State.Rng.Seed);
        Assert.Equal(state.Rng.Draws + 6, transition.State.Rng.Draws); // six dice, one raw draw each
    }

    [Fact]
    public void R6_TheStoredDiceAreSortedFromTheHighestToTheLowest_EachBetweenOneAndSix()
    {
        for (var seed = 1L; seed <= 60; seed++)
        {
            var state = Arena(9, 9) with { Rng = new RngState(seed, Draws: 0) };

            var resolved = ResolutionOf(ApplyOk(state, AttackC, TinyBoard.Map));

            Assert.Equal(resolved.AttackerDice.OrderDescending(), resolved.AttackerDice);
            Assert.Equal(resolved.DefenderDice.OrderDescending(), resolved.DefenderDice);
            Assert.All(resolved.AttackerDice.Concat(resolved.DefenderDice), die => Assert.InRange(die, 1, 6));
        }
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(2L)]
    [InlineData(3L)]
    [InlineData(5L)]
    [InlineData(8L)]
    [InlineData(13L)]
    public void R6_AFightThatLeavesTheDefenderWithArmies_OnlyChangesTheArmies_ItsRngAndTheVersion(long seed)
    {
        var state = Arena(6, 9) with { Rng = new RngState(seed, Draws: 0) }; // 9 defenders: nobody can empty it in one attack
        var (attacker, defender, after) = ReplayDice(state.Rng, attackerDice: 3, defenderDice: 3);
        var (attackerLosses, defenderLosses) = Oracle(attacker, defender);

        var transition = ApplyOk(state, AttackC, TinyBoard.Map);

        var expected = Armies(state, ("a", 6 - attackerLosses), ("c", 9 - defenderLosses)) with
        {
            Rng = after,
            Version = 1,
        };
        Assert.Equal(expected, transition.State);
        GameEvent[] events =
        [
            new AttackResolved(0, "a", "c", attacker.ToValueArray(), defender.ToValueArray(), attackerLosses, defenderLosses),
        ];
        Assert.Equal(events, transition.Events.ToArray());
    }

    [Fact]
    public void R6_AFightThatDoesNotEmptyTheDestination_LeavesOwnershipUnchanged_AndTheTurnInAttack()
    {
        var state = Arena(6, 9);

        var next = ApplyOk(state, AttackC, TinyBoard.Map).State;

        Assert.Equal(1, next.FindTerritory("c")!.Owner);
        Assert.Equal(0, next.FindTerritory("a")!.Owner);
        Assert.Equal((GamePhase.Attack, 0), (next.Phase, next.ActiveSeat));
        Assert.Equal(state.Turn, next.Turn); // no conquest: nothing pending, nothing counted
        Assert.Null(next.Turn.PendingConquest);
        Assert.Equal(0, next.Turn.ConquestsThisTurn);
    }

    [Fact]
    public void R6_ADefenderWhoRollsHighEnough_HoldsItsOnlyArmy_AndTheAttackerPaysOne_EvenWithThreeDice()
    {
        var state = WithDice(Arena(10, 1), "a", "c", (attacker, defender) => defender[0] > attacker[0]);

        var next = ApplyOk(state, AttackC, TinyBoard.Map).State;

        Assert.Equal(9, ArmiesIn(next, "a")); // three attacker dice, but only one pair: one loss, not three
        Assert.Equal(1, ArmiesIn(next, "c"));
        Assert.Equal(1, next.FindTerritory("c")!.Owner);
    }

    [Fact]
    public void R6_ATieBetweenTheBestDice_IsLostByTheAttacker_ThroughTheEngineToo()
    {
        var state = WithDice(Arena(10, 1), "a", "c", (attacker, defender) => defender[0] == attacker[0]);

        var next = ApplyOk(state, AttackC, TinyBoard.Map).State;

        Assert.Equal(9, ArmiesIn(next, "a"));
        Assert.Equal(1, ArmiesIn(next, "c"));
        Assert.Equal(1, next.FindTerritory("c")!.Owner);
    }

    [Fact]
    public void R6_AttackerWithTwoDiceAgainstThreeDefenders_LosingBothPairs_LeavesTheDefenderUntouched()
    {
        var state = WithDice(Arena(3, 3), "a", "c", (attacker, defender) => Oracle(attacker, defender) == (2, 0));

        var next = ApplyOk(state, AttackC, TinyBoard.Map).State;

        Assert.Equal(1, ArmiesIn(next, "a")); // 3 - 2
        Assert.Equal(3, ArmiesIn(next, "c")); // the third defender die was ignored
    }

    [Fact]
    public void R6_EachSideCanLoseOneArmyInTheSameFight()
    {
        var state = WithDice(Arena(5, 6), "a", "c", (attacker, defender) => Oracle(attacker, defender) == (1, 2));

        var next = ApplyOk(state, AttackC, TinyBoard.Map).State;

        Assert.Equal(4, ArmiesIn(next, "a"));
        Assert.Equal(4, ArmiesIn(next, "c"));
    }

    [Fact]
    public void R6_OverManySeeds_NoCountryDropsBelowOneArmy_EveryComparedPairCostsOneArmy_AndTheRngAdvancesByTheDiceRolled()
    {
        const int seeds = 150;
        var fights = 0;

        foreach (var (origin, destination) in Matchups)
        {
            var attackerDice = Math.Min(3, origin - 1);
            var defenderDice = Math.Min(3, destination);

            for (var seed = 1L; seed <= seeds; seed++)
            {
                var state = Arena(origin, destination) with { Rng = new RngState(seed, Draws: 0) };

                var transition = ApplyOk(state, AttackC, TinyBoard.Map);
                var resolved = ResolutionOf(transition);
                var next = transition.State;

                Assert.All(next.Territories, t => Assert.True(t.Armies >= 1, $"{t.Id} has {t.Armies} armies"));
                Assert.Equal(attackerDice, resolved.AttackerDice.Count);
                Assert.Equal(defenderDice, resolved.DefenderDice.Count);
                Assert.Equal(Math.Min(attackerDice, defenderDice), resolved.AttackerLosses + resolved.DefenderLosses);
                Assert.Equal(TotalArmies(state) - resolved.AttackerLosses - resolved.DefenderLosses, TotalArmies(next));
                Assert.Equal(attackerDice + defenderDice, next.Rng.Draws - state.Rng.Draws);
                Assert.Equal(ReplayDice(state.Rng, attackerDice, defenderDice).After, next.Rng);
                Assert.Equal(1, next.Version);
                fights++;
            }
        }

        Assert.Equal(Matchups.Length * seeds, fights);
    }

    [Fact]
    public void E10_TheSameStateAndTheSameAttack_GiveTheSameFightTwice()
    {
        var state = Arena(7, 4) with { Rng = new RngState(99, Draws: 12) };

        var first = ApplyOk(state, AttackC, TinyBoard.Map);
        var second = ApplyOk(state, AttackC, TinyBoard.Map);

        Assert.Equal(first, second);
    }

    [Fact]
    public void E10_DifferentSeeds_GiveDifferentDice_SoTheRngIsReallyUsed()
    {
        var rolls = Enumerable.Range(1, 20)
            .Select(seed => ResolutionOf(ApplyOk(Arena(9, 9) with { Rng = new RngState(seed, 0) }, AttackC, TinyBoard.Map)))
            .Select(resolved => (resolved.AttackerDice, resolved.DefenderDice))
            .Distinct()
            .Count();

        Assert.True(rolls > 10, $"Only {rolls} different fights out of 20 seeds.");
    }

    [Fact]
    public void R6_AnyNumberOfAttacks_AreAllowedInATurn_FromTheSameCountryOrFromAnother_AgainstAnyColour()
    {
        // Seat 0 has a (12) and b (9); seat 1 holds c with 20 armies and seat 2 holds d with 20: five fights cannot
        // conquer anything, and the origins always keep enough armies to roll three dice.
        var state = Armies(ThreeSeats(12, 20), ("b", 9), ("d", 20));
        (string From, string To)[] attacks = [("a", "c"), ("a", "c"), ("b", "d"), ("a", "c"), ("b", "d")];
        var fought = new List<AttackResolved>();

        foreach (var (origin, destination) in attacks)
        {
            var (attackerDice, defenderDice) = DiceCounts(state, origin, destination);
            var expectedDice = ReplayDice(state.Rng, attackerDice, defenderDice);

            var transition = ApplyOk(state, new Attack(0, origin, destination), TinyBoard.Map);

            var resolved = ResolutionOf(transition);
            Assert.Equal(expectedDice.Attacker, resolved.AttackerDice.ToArray()); // each fight continues the RNG of the previous one
            Assert.Equal(expectedDice.Defender, resolved.DefenderDice.ToArray());
            Assert.Equal(GamePhase.Attack, transition.State.Phase);
            Assert.Equal(0, transition.State.ActiveSeat);
            fought.Add(resolved);
            state = transition.State;
        }

        Assert.Equal(attacks.Select(a => (a.From, a.To)), fought.Select(f => (f.From, f.To)));
        Assert.Equal(5, state.Version);
        Assert.Equal(5 * 6L, state.Rng.Draws); // five fights of three dice against three dice
        Assert.Equal((0, 1, 2), (state.FindTerritory("a")!.Owner, state.FindTerritory("c")!.Owner, state.FindTerritory("d")!.Owner));
        Assert.Equal(0, state.Turn.ConquestsThisTurn);
    }

    [Fact]
    public void E4_ARejectedAttack_RollsNoDice_TheStateKeepsItsRngAndItsVersion()
    {
        AssertRejected(
            RuleViolations.NotAdjacent,
            () => Arena(6, 3) with { Rng = new RngState(5, Draws: 3) },
            _ => new Attack(0, "a", "d"), // a and d do not touch
            TinyBoard.Map);
    }
}
