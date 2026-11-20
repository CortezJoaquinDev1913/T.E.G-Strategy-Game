using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Events;
using Teg.Domain.Rng;
using Teg.Domain.Rules;
using Teg.Domain.State;
using static Teg.Domain.Tests.Support.GameFixtures;
using static Teg.Domain.Tests.Support.VictoryFixtures;

namespace Teg.Domain.Tests.ObjectiveRules;

/// <summary>
/// E9 / R§8: the destruction objectives (10 to 15) DURING the game. A destruction objective is fulfilled only when its OWNER
/// eliminates the target colour; if another participant eliminates that colour first, the objective passes by itself to the
/// common one (30 countries, as in a four-participant game) without announcement, and an event of the history says so.
/// Detecting a kill is a matter of who ELIMINATES whom (an attack conquering the last country of a participant), on the
/// colour the objective finally aims at (fixed at the deal): the drawn colour no longer matters.
/// <para>
/// Every scenario has four seats (Azul, Rojo, Negro, Amarillo) on the real board: the active seat attacks
/// <see cref="Prey"/> from <see cref="Origin"/> and the victim owns only <see cref="Prey"/>, so the conquest eliminates the
/// victim; everybody else keeps many countries. (That the owner's own kill WINS the game is the business of the victory
/// tests; here it only must not be taken for a fall back.)
/// </para>
/// </summary>
public sealed class DestructionObjectiveTests
{
    private static GameEvent[] FellBack(Transition transition) =>
        transition.Events.Where(e => e is ObjectiveFellBackToCommon).ToArray();

    private static Participant[] Participants(GameState state) => state.Participants.ToArray();

    // ---- another participant kills the target: the objective passes to the common one --------------------------------------

    [Theory]
    [InlineData(0, 2, 1)] // Ana (Azul) eliminates Negro; Rojo's holder aims at Negro
    [InlineData(0, 2, 3)]
    [InlineData(1, 0, 3)] // the killer and the holder are never the same participant here
    [InlineData(3, 1, 0)]
    [InlineData(2, 3, 1)]
    [InlineData(1, 3, 2)]
    public void R8_WhenAnotherParticipantEliminatesTheTarget_TheObjectiveFallsBackToTheCommonOne(int killer, int victim, int holder)
    {
        var before = Kill(killer, victim, (holder, Destroy(Colours[victim])));

        var transition = Capture(before, Origin, Prey);

        // The holder's objective is now the common one; the victim is out; nothing else about anybody changed.
        Participant[] expected =
        [
            .. before.Participants.Select(p =>
                p.Seat == victim ? p with { Eliminated = true }
                : p.Seat == holder ? p with { Objective = Objective.Common }
                : p),
        ];
        Assert.Equal(expected, Participants(transition.State));
        Assert.Equal(
            [typeof(AttackResolved), typeof(TerritoryConquered), typeof(ParticipantEliminated), typeof(ObjectiveFellBackToCommon)],
            transition.Events.Select(e => e.GetType()).ToArray());
        Assert.Equal(new ObjectiveFellBackToCommon(holder), transition.Events[3]);
    }

    // ---- the owner's own kill is not a fall back -----------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 3)]
    [InlineData(2, 0)]
    [InlineData(3, 2)]
    public void R8_TheOwnersOwnKill_IsNotAFallBack_TheObjectiveStaysAsItWas(int owner, int victim)
    {
        var objective = Destroy(Colours[victim]);
        var before = Kill(owner, victim, (owner, objective));

        var transition = Capture(before, Origin, Prey);

        Assert.True(transition.State.Participants[victim].Eliminated); // the kill did happen
        Assert.Equal(objective, transition.State.Participants[owner].Objective);
        Assert.Empty(FellBack(transition));
    }

    // ---- two holders aim at the same colour (a redirected target) -------------------------------------------------------------

    /// <summary>
    /// Seat 0 drew "destroy Rojo" (11); seat 2 drew "destroy Verde" (14), a colour nobody plays, which becomes the
    /// right-hand colour of seat 2: Rojo. Both finally aim at Rojo (seat 1), through the real assignment.
    /// </summary>
    private static Objective[] TwoAimAtRojo()
    {
        IReadOnlySet<string>[] nobodyOwnsAnything = [.. Enumerable.Range(0, 4).Select(_ => (IReadOnlySet<string>)new HashSet<string>())];
        var dealt = Objectives.Assign(Colours, nobodyOwnsAnything, [11, 2, 14, 3], RealMap);

        Assert.Equal(new Objective.Destruction(11, PlayerColor.Rojo), dealt[0]);
        Assert.Equal(new Objective.Destruction(14, PlayerColor.Rojo), dealt[2]);

        return [.. dealt];
    }

    [Fact]
    public void R8_TwoHoldersAimingAtTheSameColour_BothFallBack_WhenAThirdParticipantEliminatesIt()
    {
        var before = WithObjectives(Kill(3, 1), TwoAimAtRojo());

        var transition = Capture(before, Origin, Prey);

        Assert.Equal(Objective.Common, transition.State.Participants[0].Objective);
        Assert.Equal(Objective.Common, transition.State.Participants[2].Objective);
        Assert.Equal(before.Participants[3].Objective, transition.State.Participants[3].Objective); // the killer's own one is untouched
        Assert.Equal(new GameEvent[] { new ObjectiveFellBackToCommon(0), new ObjectiveFellBackToCommon(2) }, FellBack(transition)); // seat order
    }

    [Fact]
    public void R8_TwoHoldersAimingAtTheSameColour_OnlyTheOneWhoDoesNotKillFallsBack()
    {
        var objectives = TwoAimAtRojo();
        var before = WithObjectives(Kill(0, 1), objectives); // seat 0 (an owner) eliminates Rojo

        var transition = Capture(before, Origin, Prey);

        Assert.Equal(objectives[0], transition.State.Participants[0].Objective); // the owner keeps the objective it just fulfilled
        Assert.Equal(Objective.Common, transition.State.Participants[2].Objective); // the other one lost the race
        Assert.Equal(new GameEvent[] { new ObjectiveFellBackToCommon(2) }, FellBack(transition));
    }

    // ---- the colour that counts is the final one, not the drawn one ------------------------------------------------------------

    /// <summary>Seat 1 drew its OWN colour (11, Rojo): its objective aims at the colour on its right, seat 0's Azul.</summary>
    private static Objective RedirectedFromItsOwnColour()
    {
        IReadOnlySet<string>[] nobodyOwnsAnything = [.. Enumerable.Range(0, 4).Select(_ => (IReadOnlySet<string>)new HashSet<string>())];
        var dealt = Objectives.Assign(Colours, nobodyOwnsAnything, [1, 11, 2, 3], RealMap)[1];

        Assert.Equal(new Objective.Destruction(11, PlayerColor.Azul), dealt);

        return dealt;
    }

    [Fact]
    public void R8_ARedirectedObjective_FallsBackWhenItsFinalTargetIsEliminated()
    {
        var before = Kill(3, 0, (1, RedirectedFromItsOwnColour())); // seat 3 eliminates Azul

        var transition = Capture(before, Origin, Prey);

        Assert.Equal(Objective.Common, transition.State.Participants[1].Objective);
        Assert.Equal(new GameEvent[] { new ObjectiveFellBackToCommon(1) }, FellBack(transition));
    }

    [Fact]
    public void R8_ARedirectedObjective_IgnoresTheDrawnColour_ItsEliminationChangesNothing()
    {
        // Seat 1's objective was drawn as "destroy Rojo" but aims at Azul: if somebody eliminates Rojo (seat 1 itself) nothing
        // happens to the objective, and nobody else is touched.
        var objective = RedirectedFromItsOwnColour();
        var before = Kill(0, 1, (1, objective));

        var transition = Capture(before, Origin, Prey);

        Assert.True(transition.State.Participants[1].Eliminated);
        Assert.Equal(objective, transition.State.Participants[1].Objective);
        Assert.Empty(FellBack(transition));
    }

    // ---- nothing to change --------------------------------------------------------------------------------------------------------

    [Fact]
    public void R8_EliminatingAColourNobodyTargets_ChangesNoObjective()
    {
        // Seat 3 (Amarillo) is eliminated; the destruction objectives aim at Azul and Rojo, the other seat has an occupation one.
        var before = Kill(
            0,
            3,
            (1, Destroy(PlayerColor.Azul)),
            (2, Destroy(PlayerColor.Rojo)),
            (0, new Objective.Occupation(4)));

        var transition = Capture(before, Origin, Prey);

        Participant[] expected = [.. before.Participants.Select(p => p.Seat == 3 ? p with { Eliminated = true } : p)];
        Assert.Equal(expected, Participants(transition.State));
        Assert.Empty(FellBack(transition));
    }

    [Fact]
    public void R8_AHolderThatAlreadyFellBack_IsNotAffectedByLaterEliminations()
    {
        var before = Kill(0, 2, (1, Objective.Common), (3, Destroy(PlayerColor.Rojo)));

        var transition = Capture(before, Origin, Prey);

        Assert.Equal(Objective.Common, transition.State.Participants[1].Objective);
        Assert.Equal(Destroy(PlayerColor.Rojo), transition.State.Participants[3].Objective); // another colour: not its target
        Assert.Empty(FellBack(transition));
    }

    [Fact]
    public void R8_TheFallBackHappensOnce_ASecondEliminationLaterDoesNotTouchTheCommonObjective()
    {
        // Seat 1 aims at Negro (seat 2). Seat 0 eliminates Negro (seat 1 falls back), then Amarillo (seat 3): one fall back only.
        // Seat 2 owns only "mongolia" and seat 3 only "gobi"; both border seat 0's "china".
        var table = Table(
            4,
            actor: 0,
            rest: [0, 1],
            [Holds(0, Origin, armies: 12), Holds(2, Prey), Holds(3, SecondPrey)]);
        var first = Capture(WithObjectives(table, (1, Destroy(PlayerColor.Negro))), Origin, Prey);
        Assert.Equal(Objective.Common, first.State.Participants[1].Objective);
        Assert.Equal([new ObjectiveFellBackToCommon(1)], FellBack(first));

        var occupied = ApplyOk(first.State, new MoveAfterConquest(0, 3)).State;
        var second = Capture(occupied, Origin, SecondPrey);

        Assert.True(second.State.Participants[3].Eliminated);
        Assert.Equal(Objective.Common, second.State.Participants[1].Objective);
        Assert.Empty(FellBack(second));
    }

    [Fact]
    public void R8_AConquestThatEliminatesNobody_ChangesNoObjective()
    {
        // The victim also owns "gobi", so conquering "mongolia" does not eliminate them.
        var table = Table(
            4,
            actor: 0,
            rest: [0, 1, 3],
            [Holds(0, Origin, armies: 6), Holds(2, Prey), Holds(2, SecondPrey)]);
        var before = WithObjectives(table, (1, Destroy(PlayerColor.Negro)));

        var transition = Capture(before, Origin, Prey);

        Assert.False(transition.State.Participants[2].Eliminated);
        Assert.Equal(before.Participants[1].Objective, transition.State.Participants[1].Objective);
        Assert.Empty(FellBack(transition));
    }

    // ---- holders that are out of the game ------------------------------------------------------------------------------------------

    [Fact]
    public void R8_AHolderEliminatedEarlier_IsLeftAlone_WhenItsTargetIsEliminated()
    {
        // Seat 1 owns nothing (eliminated before) and still holds "destroy Negro"; seat 0 now eliminates Negro (seat 2).
        var table = Table(4, actor: 0, rest: [0, 3], [Holds(0, Origin, armies: 6), Holds(2, Prey)]);
        Assert.True(table.Participants[1].Eliminated);
        var before = WithObjectives(table, (1, Destroy(PlayerColor.Negro)));

        var transition = Capture(before, Origin, Prey);

        Assert.True(transition.State.Participants[2].Eliminated);
        Assert.Equal(Destroy(PlayerColor.Negro), transition.State.Participants[1].Objective);
        Assert.Empty(FellBack(transition));
    }

    [Fact]
    public void R8_AHolderEliminatedByTheConquest_IsLeftAlone_ItsObjectiveIsNotRewritten()
    {
        // The victim (seat 2) holds "destroy Rojo" and Rojo is alive: eliminating the holder rewrites nothing.
        var objective = Destroy(PlayerColor.Rojo);
        var before = Kill(0, 2, (2, objective));

        var transition = Capture(before, Origin, Prey);

        Assert.True(transition.State.Participants[2].Eliminated);
        Assert.Equal(objective, transition.State.Participants[2].Objective);
        Assert.Empty(FellBack(transition));
    }

    // ---- the engine against a model, over many random scenarios ---------------------------------------------------------------------

    /// <summary>
    /// What R§8 says, written as a plain rule over the objectives BEFORE the kill: a living participant, other than the killer,
    /// whose destruction objective aims at the colour of the victim passes to the common objective; nobody else changes.
    /// </summary>
    private static Objective Expected(Objective before, int seat, int killer, int victim) =>
        before is Objective.Destruction destruction
        && seat != killer
        && seat != victim
        && destruction.Target == Colours[victim]
            ? Objective.Common
            : before;

    [Fact]
    public void R8_OverRandomScenarios_TheEngineAgreesWithTheRule_ForEveryParticipantObjective()
    {
        var choices = new SeededRng(8_004);
        var fellBack = 0;
        var kept = 0;

        for (var scenario = 0; scenario < 600; scenario++)
        {
            var killer = choices.NextInt(4);
            var victim = (killer + 1 + choices.NextInt(3)) % 4; // any seat but the killer
            Objective[] objectives =
            [
                .. Enumerable.Range(0, 4).Select(_ => choices.NextInt(3) switch
                {
                    0 => (Objective)new Objective.Occupation(1 + choices.NextInt(9)),
                    1 => Destroy(Colours[choices.NextInt(4)]),
                    _ => Objective.Common,
                }),
            ];
            var before = WithObjectives(Kill(killer, victim), objectives);

            var transition = Capture(before, Origin, Prey);

            Objective[] expected = [.. Enumerable.Range(0, 4).Select(seat => Expected(objectives[seat], seat, killer, victim))];
            Assert.Equal(expected, transition.State.Participants.Select(p => p.Objective).ToArray());
            Assert.Equal(
                Enumerable.Range(0, 4).Where(seat => expected[seat] != objectives[seat]).Select(seat => new ObjectiveFellBackToCommon(seat)),
                FellBack(transition).Cast<ObjectiveFellBackToCommon>());
            fellBack += expected.Where((objective, seat) => objective != objectives[seat]).Count();
            kept += expected.Where((objective, seat) => objective == objectives[seat] && objective is Objective.Destruction).Count();
        }

        // Both outcomes happen in the sweep (measured over these 600 scenarios: 111 fall backs, 647 destruction objectives kept):
        // the agreement is not vacuous.
        Assert.True(fellBack > 60, $"Only {fellBack} fall backs in the sweep.");
        Assert.True(kept > 300, $"Only {kept} destruction objectives kept.");
    }
}
