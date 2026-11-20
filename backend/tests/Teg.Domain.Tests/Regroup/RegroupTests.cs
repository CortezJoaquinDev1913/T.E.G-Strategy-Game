using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Events;
using Teg.Domain.Map;
using Teg.Domain.Rules;
using Teg.Domain.State;
using Teg.Domain.Tests.Support;
using static Teg.Domain.Tests.Support.CombatFixtures;
using static Teg.Domain.Tests.Support.GameFixtures;
using static Teg.Domain.Tests.Support.RegroupFixtures;

namespace Teg.Domain.Tests.Regrouping;

/// <summary>
/// E6 / R§6, Reagrupe. Once in Reagrupe no attack is accepted in the turn. The participant moves armies from an own
/// country to an ADJACENT own country (land or bridge, both directions), always leaving one army behind, as often as they
/// like. There is no chaining: every country has a quota fixed WHEN the phase starts (<c>armies then - 1</c>), moving
/// spends the quota of the ORIGIN and does not add to the one of the destination, so armies that arrived in this phase
/// cannot be moved on. The phase is optional (the turn can end from Ataque or from Reagrupe without moving anything), and
/// moving changes neither ownership, nor the conquest counter, nor the total of armies on the board.
/// </summary>
public sealed class RegroupTests
{
    /// <summary>The same regroup state rebuilt from scratch, for <see cref="AssertRejected"/> (it builds the state twice).</summary>
    private static GameState Tiny() => TinyInRegroup();

    /// <summary>A regroup (accepted) on the <see cref="TinyBoard"/>.</summary>
    private static Transition Do(GameState state, Regroup move) => ApplyOk(state, move, TinyBoard.Map);

    /// <summary>Any other command (accepted) on the <see cref="TinyBoard"/>.</summary>
    private static Transition Run(GameState state, GameCommand command) => ApplyOk(state, command, TinyBoard.Map);

    // ---- StartRegroup: the phase change and the quotas -----------------------------------------------------------

    [Fact]
    public void R6_StartRegroup_MovesFromAttackToRegroup_AndFixesTheQuotaOfEveryOwnCountry_AsItsArmiesMinusOne()
    {
        var attack = TinyBoard.State(GamePhase.Attack); // seat 0: a 5, b 3, e 1, f 2; seat 1: c 4, d 2

        var transition = ApplyOk(attack, new StartRegroup(0), TinyBoard.Map);
        var next = transition.State;

        RegroupQuota[] quotas = [new("a", 4), new("b", 2), new("e", 0), new("f", 1)]; // map order, own countries only
        var expected = attack with
        {
            Phase = GamePhase.Regroup,
            Version = attack.Version + 1,
            Turn = attack.Turn with { RegroupQuotas = quotas.ToValueArray() },
        };
        Assert.Equal(expected, next);
        GameEvent[] expectedEvents = [new PhaseChanged(0, GamePhase.Attack, GamePhase.Regroup)];
        Assert.Equal(expectedEvents, transition.Events.ToArray());
        Assert.Equal(new[] { 4, 2, 0, 1 }, new[] { "a", "b", "e", "f" }.Select(next.Turn.RegroupQuotaOf));
        Assert.Equal(new[] { 0, 0 }, new[] { "c", "d" }.Select(next.Turn.RegroupQuotaOf)); // what is not theirs has no quota
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void R6_TheQuotaIsArmiesMinusOne_ForEveryCountryOfTheActiveParticipant_AndOnlyForThem(int participants)
    {
        for (var seat = 0; seat < participants; seat++)
        {
            var attack = InPlay(participants, GamePhase.Attack, round: 2, activeSeat: seat);
            attack = attack with
            {
                Territories = attack.Territories.Select((t, index) => t with { Armies = 1 + index % 6 }).ToValueArray(),
            };

            var next = ApplyOk(attack, new StartRegroup(seat)).State;

            var owned = next.Territories.Where(t => t.Owner == seat).ToArray();
            Assert.True(owned.Length >= 12, $"Seat {seat} owns only {owned.Length} countries.");
            Assert.Equal(owned.Select(t => new RegroupQuota(t.Id, t.Armies - 1)), next.Turn.RegroupQuotas);
            Assert.Contains(next.Turn.RegroupQuotas, q => q.Movable > 0);
            Assert.All(
                next.Territories.Where(t => t.Owner != seat),
                t => Assert.Equal(0, next.Turn.RegroupQuotaOf(t.Id)));
        }
    }

    [Fact]
    public void R6_TheQuotaIsTakenFromTheArmiesAtTheStartOfRegroup_SoItCountsWhatTheAttacksChanged()
    {
        // a 6 armies conquers c (1 army) and occupies it with 3: a keeps 3, the conquered c has 3 and is now seat 0's.
        var occupied = ApplyOk(Conquer(Arena(6, 1), "a", "c"), new MoveAfterConquest(0, 3), TinyBoard.Map).State;

        var next = ApplyOk(occupied, new StartRegroup(0), TinyBoard.Map).State;

        RegroupQuota[] expected = [new("a", 2), new("b", 2), new("c", 2), new("e", 0), new("f", 1)];
        Assert.Equal(expected, next.Turn.RegroupQuotas);
    }

    [Fact]
    public void R6_TheQuotasLiveInsideTheRegroupPhase_ThereAreNoneInAttack_AndTheNextTurnStartsWithNone()
    {
        var attacked = ApplyOk(Conquer(Arena(6, 1), "a", "c"), new MoveAfterConquest(0, 2), TinyBoard.Map).State;
        Assert.Empty(attacked.Turn.RegroupQuotas); // the attacks happened: still no quota until the phase starts

        var regroup = ApplyOk(attacked, new StartRegroup(0), TinyBoard.Map).State;
        Assert.NotEmpty(regroup.Turn.RegroupQuotas);

        var ended = Run(regroup with { Round = 2 }, new EndTurn(0)).State; // next: seat 1, Refuerzo
        Assert.Equal(1, ended.ActiveSeat);
        Assert.Empty(ended.Turn.RegroupQuotas);
        Assert.Equal(Pool(3), ended.Turn); // a fresh context: just the new pool, nothing carried over
    }

    [Fact]
    public void R6_TheQuotasAreRecomputedFromTheBoardInEveryTurn_NotCarriedOverFromTheLastOne()
    {
        // Turn 1 (seat 0): a sends 4 to b: a 1, b 7. Seat 1 ends. Turn 2 of seat 0 (Refuerzo +3 on a): a 4, b 7, e 1, f 2.
        var state = Do(Tiny(), Move("a", "b", 4)).State;
        state = Run(state, new EndTurn(0)).State; // round 1, seat 1 in Ataque
        state = Run(state, new EndTurn(1)).State; // the rotation wraps: round 2, seat 0 in Refuerzo
        Assert.Equal((0, 2, GamePhase.Reinforcement), (state.ActiveSeat, state.Round, state.Phase));
        state = PlaceWholePool(state, TinyBoard.Map);
        Assert.Equal(GamePhase.Attack, state.Phase);

        var regroup = ApplyOk(state, new StartRegroup(0), TinyBoard.Map).State;

        RegroupQuota[] expected = [new("a", 3), new("b", 6), new("e", 0), new("f", 1)];
        Assert.Equal(expected, regroup.Turn.RegroupQuotas);
    }

    [Fact]
    public void R6_RegroupingDoesNotTouchTheRestOfTheTurn_TheConquestCounterSurvivesStartRegroupAndEveryMove()
    {
        var occupied = ApplyOk(Conquer(Arena(6, 1), "a", "c"), new MoveAfterConquest(0, 3), TinyBoard.Map).State;
        Assert.Equal(1, occupied.Turn.ConquestsThisTurn);

        var regroup = ApplyOk(occupied, new StartRegroup(0), TinyBoard.Map).State;
        var moved = Moved(regroup, TinyBoard.Map, Move("a", "b", 1), Move("c", "a", 2));

        Assert.Equal(1, regroup.Turn.ConquestsThisTurn);
        Assert.Equal(1, moved.Turn.ConquestsThisTurn);
        Assert.Null(moved.Turn.PendingConquest);
        Assert.Equal(0, moved.FindTerritory("c")!.Owner); // moving armies out of the conquered country does not undo the conquest
    }

    // ---- once in Reagrupe, nobody attacks any more -----------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void R6_OnceInRegroup_AnAttackIsRejectedAsWrongPhase_ForTheRestOfTheTurn(bool afterAMove)
    {
        AssertRejected(
            RuleViolations.WrongPhase,
            () => afterAMove ? Do(Tiny(), Move("a", "b", 1)).State : Tiny(),
            _ => new Attack(0, "a", "c"), // a perfectly good attack in Ataque
            TinyBoard.Map);
    }

    // ---- executing a regroup -----------------------------------------------------------------------------------

    [Theory]
    [InlineData("a", "b", 1)] // land, the smallest move
    [InlineData("a", "b", 4)] // land, everything but one army
    [InlineData("b", "a", 2)] // land, the other way
    [InlineData("b", "f", 2)] // bridge
    [InlineData("f", "b", 1)] // bridge, the other way
    [InlineData("f", "e", 1)] // land, onto a country with one army
    public void R6_ARegroup_MovesTheArmiesBetweenOwnAdjacentCountries_OverLandAndOverBridges_InBothDirections(
        string origin,
        string destination,
        int armies)
    {
        var before = Tiny();

        var transition = Do(before, Move(origin, destination, armies));

        var expected = before with
        {
            Version = before.Version + 1,
            Territories = before.Territories
                .Select(t => t.Id == origin ? t with { Armies = t.Armies - armies }
                    : t.Id == destination ? t with { Armies = t.Armies + armies }
                    : t)
                .ToValueArray(),
            Turn = before.Turn with
            {
                RegroupQuotas = before.Turn.RegroupQuotas
                    .Select(q => q.TerritoryId == origin ? q with { Movable = q.Movable - armies } : q) // only the origin pays
                    .ToValueArray(),
            },
        };
        Assert.Equal(expected, transition.State); // owners, phase, turn, rng, cards: everything else is as it was
        GameEvent[] expectedEvents = [new ArmiesRegrouped(0, origin, destination, armies)];
        Assert.Equal(expectedEvents, transition.Events.ToArray());
        Assert.Equal(before.Turn.RegroupQuotaOf(destination), transition.State.Turn.RegroupQuotaOf(destination)); // no gain
        Assert.Equal((GamePhase.Regroup, 0), (transition.State.Phase, transition.State.ActiveSeat));
    }

    [Fact]
    public void R6_EveryBorderOfTheRealMap_LandOrBridge_AllowsARegroupBetweenOwnCountries_InBothDirections()
    {
        var moves = 0;

        foreach (var border in RealMap.Borders)
        {
            foreach (var (origin, destination) in new[] { (border.A, border.B), (border.B, border.A) })
            {
                var attack = Armies(
                    Owners(InPlay(2, GamePhase.Attack, round: 2, activeSeat: 0), (origin, 0), (destination, 0)),
                    (origin, 3));
                var regroup = ApplyOk(attack, new StartRegroup(0)).State;

                var next = ApplyOk(regroup, Move(origin, destination, 2)).State;

                Assert.Equal(1, ArmiesIn(next, origin));
                Assert.Equal(ArmiesIn(regroup, destination) + 2, ArmiesIn(next, destination));
                moves++;
            }
        }

        Assert.Equal(2 * RealMap.Borders.Count, moves);
        Assert.Equal(23, RealMap.Borders.Count(b => b.Kind == BorderKind.Bridge)); // bridges really included
    }

    [Fact]
    public void R6_SeveralRegroupsInOnePhase_FromTheSameOriginUntilItsQuotaIsSpent_AndToDifferentDestinations()
    {
        var state = Tiny(); // a 5 (quota 4), b 3 (quota 2), f 2 (quota 1)
        var versionAtTheStart = state.Version;

        // a spends its whole quota in three moves; b sends one army to each of its two neighbours.
        var moves = new[]
        {
            Move("a", "b", 1), Move("a", "b", 1), Move("a", "b", 2), Move("b", "a", 1), Move("b", "f", 1),
        };
        foreach (var move in moves)
        {
            state = Do(state, move).State;
        }

        Assert.Equal(versionAtTheStart + moves.Length, state.Version); // +1 for every accepted command
        var armies = new[] { "a", "b", "e", "f" }.Select(id => ArmiesIn(state, id));
        Assert.Equal(new[] { 2, 5, 1, 3 }, armies); // a: 5 - 4 + 1 (b sent one back); b: 3 + 4 - 2; f: 2 + 1
        Assert.Equal(0, state.Turn.RegroupQuotaOf("a")); // spent: 4 sent out in total (a still has 2 armies)
        Assert.Equal(0, state.Turn.RegroupQuotaOf("b")); // spent: one to a, one to f
        Assert.Equal(1, state.Turn.RegroupQuotaOf("f")); // untouched
        Assert.Equal(RuleViolations.RegroupQuotaExceeded, GameEngine.Apply(state, Move("a", "b", 1), TinyBoard.Map).Error.Code);
    }

    [Fact]
    public void R6_AnOriginThatSpentItsWholeQuota_CannotSendMore_EvenWhenItHasArmiesToSpare()
    {
        // b 3 (quota 2) receives 3 from a: 6 armies. It may still send ONLY its own quota: two armies in all.
        AssertRejected(
            RuleViolations.RegroupQuotaExceeded,
            () => Moved(Tiny(), TinyBoard.Map, Move("a", "b", 3), Move("b", "f", 1), Move("b", "a", 1)),
            _ => Move("b", "f", 1),
            TinyBoard.Map);
    }

    // ---- no chaining (the example of the rules) ---------------------------------------------------------------------

    [Fact]
    public void R6_NoChaining_ArmiesThatArrivedInThisPhaseCannotBeMovedOn_OnlyTheOwnQuotaCan()
    {
        // a - b - f. b has 3 armies at the start (quota 2). a sends 3 to b: b has 6, but still a quota of 2.
        Func<GameState> afterTheFirstMove = () => Moved(Tiny(), TinyBoard.Map, Move("a", "b", 3));
        Assert.Equal(6, ArmiesIn(afterTheFirstMove(), "b"));

        AssertRejected(RuleViolations.RegroupQuotaExceeded, afterTheFirstMove, _ => Move("b", "f", 3), TinyBoard.Map);

        var allowed = Do(afterTheFirstMove(), Move("b", "f", 2)).State; // its own quota is fine
        Assert.Equal((4, 4), (ArmiesIn(allowed, "b"), ArmiesIn(allowed, "f")));
        AssertRejected(
            RuleViolations.RegroupQuotaExceeded,
            () => Do(afterTheFirstMove(), Move("b", "f", 2)).State, // the quota is spent, b still has 4 armies
            _ => Move("b", "f", 1),
            TinyBoard.Map);
    }

    [Fact]
    public void R6_NoChaining_WhenTheIntermediateCountryHasASingleArmyAtTheStart_ItsQuotaIsZero()
    {
        // b has 1 army when Reagrupe starts: quota 0. After a sends it 2 armies it has 3, and still cannot send any.
        Func<GameState> afterTheFirstMove = () => Moved(TinyInRegroup(("b", 1)), TinyBoard.Map, Move("a", "b", 2));
        Assert.Equal(3, ArmiesIn(afterTheFirstMove(), "b"));

        AssertRejected(RuleViolations.RegroupQuotaExceeded, afterTheFirstMove, _ => Move("b", "f", 1), TinyBoard.Map);
        AssertRejected(RuleViolations.RegroupQuotaExceeded, afterTheFirstMove, _ => Move("b", "a", 1), TinyBoard.Map);
    }

    [Theory]
    [InlineData(1, 1, 1, false)] // Y: 1 at the start (quota 0), 2 now: nothing may move
    [InlineData(1, 5, 1, false)] // Y: 1 at the start, 6 now: still nothing
    [InlineData(2, 4, 1, true)] // Y: 2 at the start (quota 1), 6 now: exactly its own quota
    [InlineData(2, 4, 2, false)] // ...and one more is chaining
    [InlineData(4, 2, 3, true)] // Y: 4 at the start (quota 3), 6 now
    [InlineData(4, 2, 4, false)] // 6 armies could spare 4, but the quota is 3
    public void R6_NoChaining_OnTheRealMap_XToYAndThenYToZ_YMayOnlyMoveOnItsOwnQuota(
        int yAtTheStart,
        int arrivedFromX,
        int askedOfY,
        bool accepted)
    {
        Assert.False(RealMap.AreAdjacent(X, Z)); // Z can only be reached through Y
        var regroup = ChainInRegroup(xArmies: arrivedFromX + 1, yArmies: yAtTheStart, zArmies: 1);
        var arrived = ApplyOk(regroup, Move(X, Y, arrivedFromX)).State;
        Assert.Equal(yAtTheStart + arrivedFromX, ArmiesIn(arrived, Y));

        var result = GameEngine.Apply(arrived, Move(Y, Z, askedOfY), RealMap);

        Assert.Equal(accepted, result.IsSuccess);
        if (accepted)
        {
            Assert.Equal((yAtTheStart + arrivedFromX - askedOfY, 1 + askedOfY), (ArmiesIn(result.Value.State, Y), ArmiesIn(result.Value.State, Z)));
        }
        else
        {
            Assert.Equal(RuleViolations.RegroupQuotaExceeded, result.Error.Code);
            Assert.False(string.IsNullOrWhiteSpace(result.Error.Message));
        }
    }

    // ---- rejections: typed, and the state stays as it was -------------------------------------------------------------

    [Theory]
    [InlineData(RuleViolations.OriginNotOwned, "c", "d", 1)]
    [InlineData(RuleViolations.OriginNotOwned, "c", "a", 1)]
    [InlineData(RuleViolations.DestinationNotOwn, "a", "c", 1)] // land, to an enemy
    [InlineData(RuleViolations.DestinationNotOwn, "b", "d", 1)] // bridge, to an enemy
    [InlineData(RuleViolations.NotAdjacent, "a", "f", 1)]
    [InlineData(RuleViolations.NotAdjacent, "a", "e", 1)]
    [InlineData(RuleViolations.NotAdjacent, "b", "e", 1)]
    [InlineData(RuleViolations.NotAdjacent, "a", "a", 1)] // a country is not its own neighbour
    [InlineData(RuleViolations.UnknownTerritory, "a", "no-existe", 1)]
    [InlineData(RuleViolations.UnknownTerritory, "no-existe", "b", 1)]
    [InlineData(RuleViolations.InvalidArmyCount, "a", "b", 0)]
    [InlineData(RuleViolations.InvalidArmyCount, "a", "b", -3)]
    [InlineData(RuleViolations.NotEnoughArmies, "a", "b", 5)] // one army must stay: a has 5, quota 4
    [InlineData(RuleViolations.NotEnoughArmies, "b", "f", 3)]
    [InlineData(RuleViolations.OriginTooFewArmies, "e", "f", 1)] // e has a single army
    public void R6_ARegroupThatBreaksARule_IsRejectedWithItsCode_AndTheStateIsUntouched(
        string code,
        string origin,
        string destination,
        int armies)
    {
        AssertRejected(code, Tiny, _ => Move(origin, destination, armies), TinyBoard.Map);
    }

    [Fact]
    public void R6_ARegroupFromAParticipantWhoIsNotTheActiveOne_IsRejected()
    {
        AssertRejected(RuleViolations.NotActiveParticipant, Tiny, _ => Move("c", "d", 1, seat: 1), TinyBoard.Map);
    }

    [Fact]
    public void R6_ARegroupOutsideTheRegroupPhase_IsRejectedAsWrongPhase_InAttackToo()
    {
        AssertRejected(
            RuleViolations.WrongPhase,
            () => TinyBoard.State(GamePhase.Attack),
            _ => Move("a", "b", 1),
            TinyBoard.Map);
    }

    [Fact]
    public void R6_ARejectedRegroup_DoesNotCountAsAnAction_TheVersionStaysAndTheNextValidOneStillWorks()
    {
        var state = Tiny();

        var refused = GameEngine.Apply(state, Move("a", "b", 99), TinyBoard.Map);
        var accepted = Do(state, Move("a", "b", 4));

        Assert.False(refused.IsSuccess);
        Assert.Equal(state.Version + 1, accepted.State.Version);
    }

    // ---- Reagrupe is optional ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(GamePhase.Attack)]
    [InlineData(GamePhase.Regroup)]
    public void R6_RegroupIsOptional_TheTurnCanEndFromAttackOrFromRegroupWithoutMovingAnything(GamePhase phase)
    {
        var state = phase == GamePhase.Attack ? TinyBoard.State(GamePhase.Attack) : Tiny();
        Assert.Equal(phase == GamePhase.Regroup, state.Turn.RegroupQuotas.Count > 0);

        var ended = Run(state, new EndTurn(0));

        Assert.Equal(1, ended.State.ActiveSeat);
        Assert.Equal(state.Territories, ended.State.Territories); // nothing moved, nothing changed
        Assert.Empty(ended.State.Turn.RegroupQuotas);
    }

    [Fact]
    public void R6_TheTurnEndsAfterRegrouping_AndTheMovedArmiesStayWhereTheyWere()
    {
        var state = Moved(Tiny(), TinyBoard.Map, Move("a", "b", 4), Move("f", "e", 1));

        var ended = Run(state, new EndTurn(0)).State;

        Assert.Equal(1, ended.ActiveSeat);
        Assert.Equal(new[] { 1, 7, 4, 2, 2, 1 }, ended.Territories.Select(t => t.Armies)); // a b c d e f: moves are final
        Assert.Equal(new[] { 0, 0, 1, 1, 0, 0 }, ended.Territories.Select(t => t.Owner));
    }
}
