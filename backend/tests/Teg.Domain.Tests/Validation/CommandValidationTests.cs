using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Map;
using Teg.Domain.Rules;
using Teg.Domain.State;
using Teg.Domain.Tests.Support;
using static Teg.Domain.Tests.Support.GameFixtures;

namespace Teg.Domain.Tests.Validation;

/// <summary>
/// E4 / R§6: every command is validated before it changes anything. A refusal is a typed
/// <see cref="RuleViolation"/> with a stable code, and the input state stays exactly (structurally) as it was.
/// Attack and Regroup are validated here; resolving combat (phase 5) and executing a regroup (phase 6) come later,
/// so a VALID attack or regroup answers <see cref="RuleViolations.NotImplementedYet"/> and never silently succeeds.
/// </summary>
public sealed class CommandValidationTests
{
    public enum CommandKind
    {
        PlaceInitialArmies,
        StartRegroup,
        Attack,
        Regroup,
        EndTurn,
    }

    /// <summary>
    /// A command that would be valid for <paramref name="seat"/> in its own phase on the <see cref="TinyBoard"/>
    /// (seat 0 owns a, b, e, f; seat 1 owns c, d): so any refusal comes from phase or actor, not from the details.
    /// </summary>
    private static GameCommand CommandOf(CommandKind kind, int seat) => kind switch
    {
        CommandKind.PlaceInitialArmies => Place(seat, (seat == 1 ? "c" : "a", 6)),
        CommandKind.StartRegroup => new StartRegroup(seat),
        CommandKind.Attack => seat == 1 ? new Attack(seat, "c", "a") : new Attack(seat, "a", "c"),
        CommandKind.Regroup => seat == 1 ? new Regroup(seat, "c", "d", 1) : new Regroup(seat, "a", "b", 1),
        CommandKind.EndTurn => new EndTurn(seat),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static void Reject(string code, GamePhase phase, GameCommand command, int activeSeat = 0) =>
        AssertRejected(code, () => TinyBoard.State(phase, activeSeat), _ => command, TinyBoard.Map);

    private static void Reject(string code, Func<GameState> build, GameCommand command) =>
        AssertRejected(code, build, _ => command, TinyBoard.Map);

    private static GameState WithArmies(GameState state, string territoryId, int armies) =>
        state with
        {
            Territories = state.Territories
                .Select(t => t.Id == territoryId ? t with { Armies = armies } : t)
                .ToValueArray(),
        };

    // ---- wrong phase ----------------------------------------------------------------------------

    [Theory]
    [InlineData(CommandKind.PlaceInitialArmies, GamePhase.Reinforcement)]
    [InlineData(CommandKind.PlaceInitialArmies, GamePhase.Attack)]
    [InlineData(CommandKind.PlaceInitialArmies, GamePhase.Regroup)]
    [InlineData(CommandKind.PlaceInitialArmies, GamePhase.Card)]
    [InlineData(CommandKind.StartRegroup, GamePhase.InitialPlacement)]
    [InlineData(CommandKind.StartRegroup, GamePhase.Reinforcement)]
    [InlineData(CommandKind.StartRegroup, GamePhase.Regroup)]
    [InlineData(CommandKind.StartRegroup, GamePhase.Card)]
    [InlineData(CommandKind.Attack, GamePhase.InitialPlacement)]
    [InlineData(CommandKind.Attack, GamePhase.Reinforcement)]
    [InlineData(CommandKind.Attack, GamePhase.Regroup)]
    [InlineData(CommandKind.Attack, GamePhase.Card)]
    [InlineData(CommandKind.Regroup, GamePhase.InitialPlacement)]
    [InlineData(CommandKind.Regroup, GamePhase.Reinforcement)]
    [InlineData(CommandKind.Regroup, GamePhase.Attack)]
    [InlineData(CommandKind.Regroup, GamePhase.Card)]
    [InlineData(CommandKind.EndTurn, GamePhase.InitialPlacement)]
    [InlineData(CommandKind.EndTurn, GamePhase.Reinforcement)]
    [InlineData(CommandKind.EndTurn, GamePhase.Card)]
    public void ACommandOutsideItsPhase_IsRejectedAsWrongPhase(CommandKind kind, GamePhase phase)
    {
        Reject(RuleViolations.WrongPhase, phase, CommandOf(kind, seat: 0));
    }

    [Theory]
    [InlineData(CommandKind.PlaceInitialArmies, GamePhase.InitialPlacement)]
    [InlineData(CommandKind.StartRegroup, GamePhase.Attack)]
    [InlineData(CommandKind.EndTurn, GamePhase.Attack)]
    [InlineData(CommandKind.EndTurn, GamePhase.Regroup)]
    public void ACommandInItsOwnPhase_ByTheActiveParticipant_IsAccepted(CommandKind kind, GamePhase phase)
    {
        var transition = ApplyOk(TinyBoard.State(phase), CommandOf(kind, seat: 0), TinyBoard.Map);

        Assert.Equal(1, transition.State.Version);
    }

    [Theory]
    [InlineData(CommandKind.PlaceInitialArmies)]
    [InlineData(CommandKind.StartRegroup)]
    [InlineData(CommandKind.Attack)]
    [InlineData(CommandKind.Regroup)]
    [InlineData(CommandKind.EndTurn)]
    public void EveryCommandIsRefused_OnceTheGameIsFinished(CommandKind kind)
    {
        Reject(RuleViolations.GameFinished, GamePhase.Finished, CommandOf(kind, seat: 0));
    }

    // ---- wrong participant ---------------------------------------------------------------------------

    [Theory]
    [InlineData(CommandKind.PlaceInitialArmies, GamePhase.InitialPlacement)]
    [InlineData(CommandKind.StartRegroup, GamePhase.Attack)]
    [InlineData(CommandKind.Attack, GamePhase.Attack)]
    [InlineData(CommandKind.Regroup, GamePhase.Regroup)]
    [InlineData(CommandKind.EndTurn, GamePhase.Attack)]
    [InlineData(CommandKind.EndTurn, GamePhase.Regroup)]
    public void ACommandFromAParticipantWhoIsNotTheActiveOne_IsRejected(CommandKind kind, GamePhase phase)
    {
        // Seat 1 would be perfectly entitled to the command (it is valid for them); but seat 0 is the active one.
        Reject(RuleViolations.NotActiveParticipant, phase, CommandOf(kind, seat: 1), activeSeat: 0);
    }

    [Fact]
    public void ACommandFromASeatThatDoesNotExist_IsRejected()
    {
        Reject(RuleViolations.NotActiveParticipant, GamePhase.Attack, new EndTurn(7));
    }

    // ---- attack: rejections --------------------------------------------------------------------------

    [Theory]
    [InlineData("c", "a")] // enemy origin, own destination
    [InlineData("c", "d")] // enemy origin, enemy destination
    [InlineData("d", "b")] // enemy origin across a bridge
    public void Attack_FromACountryTheAttackerDoesNotOwn_IsRejected(string origin, string destination)
    {
        Reject(RuleViolations.OriginNotOwned, GamePhase.Attack, new Attack(0, origin, destination));
    }

    [Theory]
    [InlineData("a", "b")] // own country over a land border
    [InlineData("b", "f")] // own country over a bridge
    [InlineData("e", "f")]
    public void Attack_OnACountryOfTheAttacker_IsRejected_TheDestinationMustBeAnEnemy(string origin, string destination)
    {
        Reject(RuleViolations.DestinationNotEnemy, GamePhase.Attack, new Attack(0, origin, destination));
    }

    [Theory]
    [InlineData("a", "d")]
    [InlineData("b", "c")]
    [InlineData("f", "c")]
    [InlineData("f", "d")]
    public void Attack_OnAnEnemyCountryThatIsNotAdjacent_IsRejected(string origin, string destination)
    {
        Reject(RuleViolations.NotAdjacent, GamePhase.Attack, new Attack(0, origin, destination));
    }

    [Fact]
    public void Attack_FromACountryWithASingleArmy_IsRejected_OverLandAndOverABridge()
    {
        Reject(RuleViolations.OriginTooFewArmies, GamePhase.Attack, new Attack(0, "e", "c")); // e has 1 army; land border
        Reject(
            RuleViolations.OriginTooFewArmies,
            () => WithArmies(TinyBoard.State(GamePhase.Attack), "b", 1),
            new Attack(0, "b", "d")); // bridge border
    }

    [Theory]
    [InlineData("a", "no-existe")]
    [InlineData("no-existe", "c")]
    public void Attack_InvolvingACountryThatDoesNotExist_IsRejected(string origin, string destination)
    {
        Reject(RuleViolations.UnknownTerritory, GamePhase.Attack, new Attack(0, origin, destination));
    }

    // ---- attack: accepted by the validator ---------------------------------------------------------------

    [Theory]
    [InlineData(0, "a", "c")] // land
    [InlineData(0, "b", "d")] // bridge
    [InlineData(1, "c", "a")] // land, the other way
    [InlineData(1, "d", "b")] // bridge, the other way
    public void AttackValidator_AcceptsEnemyAdjacentCountries_OverLandAndOverBridges(int seat, string origin, string destination)
    {
        var state = TinyBoard.State(GamePhase.Attack, activeSeat: seat);

        Assert.Null(MoveValidator.ValidateAttack(state, new Attack(seat, origin, destination), TinyBoard.Map));
    }

    [Fact]
    public void AttackValidator_AcceptsAnOriginWithExactlyTwoArmies()
    {
        var state = WithArmies(TinyBoard.State(GamePhase.Attack), "b", 2);

        Assert.Null(MoveValidator.ValidateAttack(state, new Attack(0, "b", "d"), TinyBoard.Map));
    }

    [Fact]
    public void EveryBorderOfTheRealMap_LandOrBridge_AllowsAnAttackInBothDirections()
    {
        var borders = RealMap.Borders;
        Assert.Equal(8, borders.Count(b => b.Kind == BorderKind.Bridge)); // bridges are really covered
        Assert.True(borders.Count(b => b.Kind == BorderKind.Land) > 8);

        foreach (var border in borders)
        {
            foreach (var (origin, destination) in new[] { (border.A, border.B), (border.B, border.A) })
            {
                var state = AttackerOnlyOwns(origin);

                var violation = MoveValidator.ValidateAttack(state, new Attack(0, origin, destination), RealMap);

                Assert.True(
                    violation is null,
                    $"{origin} -> {destination} ({border.Kind}) was refused with {violation?.Code}.");
            }
        }
    }

    [Fact]
    public void EveryPairOfCountriesThatIsNotAdjacent_IsRefusedAsNotAdjacent_OnTheRealMap()
    {
        var ids = RealMap.Territories.Select(t => t.Id).ToArray();
        var checkedPairs = 0;

        foreach (var origin in ids)
        {
            var state = AttackerOnlyOwns(origin);

            foreach (var destination in ids.Where(id => id != origin && !RealMap.AreAdjacent(origin, id)))
            {
                var violation = MoveValidator.ValidateAttack(state, new Attack(0, origin, destination), RealMap);

                Assert.True(
                    violation?.Code == RuleViolations.NotAdjacent,
                    $"{origin} -> {destination} was answered with {violation?.Code ?? "no violation"}.");
                checkedPairs++;
            }
        }

        Assert.True(checkedPairs > 2000, $"Only {checkedPairs} non-adjacent pairs were checked.");
    }

    /// <summary>Real board in the Attack phase: seat 0 owns only <paramref name="countryId"/> (3 armies), seat 1 the rest.</summary>
    private static GameState AttackerOnlyOwns(string countryId) =>
        InPlay(2, GamePhase.Attack, round: 1, activeSeat: 0) with
        {
            Territories = RealMap.Territories
                .Select(t => t.Id == countryId ? new TerritoryState(t.Id, 0, 3) : new TerritoryState(t.Id, 1, 1))
                .ToValueArray(),
        };

    // ---- regroup: rejections --------------------------------------------------------------------------

    [Theory]
    [InlineData("c", "d")] // both enemy
    [InlineData("c", "a")] // enemy origin, own destination
    [InlineData("d", "b")] // enemy origin across a bridge
    public void Regroup_FromACountryTheParticipantDoesNotOwn_IsRejected(string origin, string destination)
    {
        Reject(RuleViolations.OriginNotOwned, GamePhase.Regroup, new Regroup(0, origin, destination, 1));
    }

    [Theory]
    [InlineData("a", "c")] // land
    [InlineData("b", "d")] // bridge
    public void Regroup_ToAnEnemyCountry_IsRejected_TheDestinationMustBeOwn(string origin, string destination)
    {
        Reject(RuleViolations.DestinationNotOwn, GamePhase.Regroup, new Regroup(0, origin, destination, 1));
    }

    [Theory]
    [InlineData("a", "f")]
    [InlineData("a", "e")]
    [InlineData("b", "e")]
    [InlineData("a", "a")] // a country is not its own neighbour
    public void Regroup_BetweenOwnCountriesThatAreNotAdjacent_IsRejected(string origin, string destination)
    {
        Reject(RuleViolations.NotAdjacent, GamePhase.Regroup, new Regroup(0, origin, destination, 1));
    }

    [Fact]
    public void Regroup_FromACountryWithASingleArmy_IsRejected()
    {
        Reject(RuleViolations.OriginTooFewArmies, GamePhase.Regroup, new Regroup(0, "e", "f", 1)); // e has 1 army
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-5)]
    public void Regroup_OfZeroOrNegativeArmies_IsRejected(int armies)
    {
        Reject(RuleViolations.InvalidArmyCount, GamePhase.Regroup, new Regroup(0, "a", "b", armies));
    }

    [Theory]
    [InlineData("a", "b", 5)] // a has 5 armies: at most 4 can move
    [InlineData("a", "b", 6)]
    [InlineData("b", "f", 3)] // b has 3 armies, across a bridge: at most 2
    [InlineData("f", "e", 2)] // f has 2 armies: at most 1
    public void Regroup_OfMoreArmiesThanTheOriginCanSpare_IsRejected_AtLeastOneMustStayBehind(
        string origin,
        string destination,
        int armies)
    {
        Reject(RuleViolations.NotEnoughArmies, GamePhase.Regroup, new Regroup(0, origin, destination, armies));
    }

    [Theory]
    [InlineData("a", "no-existe")]
    [InlineData("no-existe", "b")]
    public void Regroup_InvolvingACountryThatDoesNotExist_IsRejected(string origin, string destination)
    {
        Reject(RuleViolations.UnknownTerritory, GamePhase.Regroup, new Regroup(0, origin, destination, 1));
    }

    // ---- regroup: accepted by the validator ------------------------------------------------------------

    [Theory]
    [InlineData("a", "b", 1)] // land, the smallest move
    [InlineData("a", "b", 4)] // land, everything but one army
    [InlineData("b", "f", 2)] // bridge, everything but one army
    [InlineData("f", "b", 1)] // bridge, the other way
    [InlineData("f", "e", 1)] // land, the other way
    public void RegroupValidator_AcceptsOwnAdjacentCountries_OverLandAndOverBridges_LeavingOneArmy(
        string origin,
        string destination,
        int armies)
    {
        var state = TinyBoard.State(GamePhase.Regroup);

        Assert.Null(MoveValidator.ValidateRegroup(state, new Regroup(0, origin, destination, armies), TinyBoard.Map));
    }

    [Fact]
    public void EveryBorderOfTheRealMap_LandOrBridge_AllowsARegroupBetweenOwnCountries_InBothDirections()
    {
        foreach (var border in RealMap.Borders)
        {
            foreach (var (origin, destination) in new[] { (border.A, border.B), (border.B, border.A) })
            {
                var state = InPlay(2, GamePhase.Regroup, round: 2, activeSeat: 0) with
                {
                    Territories = RealMap.Territories
                        .Select(t => t.Id == origin
                            ? new TerritoryState(t.Id, 0, 3)
                            : new TerritoryState(t.Id, t.Id == destination ? 0 : 1, 1))
                        .ToValueArray(),
                };

                var violation = MoveValidator.ValidateRegroup(state, new Regroup(0, origin, destination, 2), RealMap);

                Assert.True(
                    violation is null,
                    $"{origin} -> {destination} ({border.Kind}) was refused with {violation?.Code}.");
            }
        }
    }

    // ---- valid attacks and regroups are not resolved yet (documented stand-in) -------------------------------

    [Fact]
    public void AValidAttack_IsNotResolvedYet_ItAnswersNotImplementedYet_AndNeverSucceedsSilently()
    {
        Reject(RuleViolations.NotImplementedYet, GamePhase.Attack, new Attack(0, "a", "c"));
    }

    [Fact]
    public void AValidRegroup_IsNotExecutedYet_ItAnswersNotImplementedYet_AndNeverSucceedsSilently()
    {
        Reject(RuleViolations.NotImplementedYet, GamePhase.Regroup, new Regroup(0, "a", "b", 1));
    }

    // ---- stable codes (API contract) -----------------------------------------------------------------------

    [Theory]
    [InlineData(RuleViolations.GameFinished, "GAME_FINISHED")]
    [InlineData(RuleViolations.WrongPhase, "WRONG_PHASE")]
    [InlineData(RuleViolations.NotActiveParticipant, "NOT_ACTIVE_PARTICIPANT")]
    [InlineData(RuleViolations.UnknownTerritory, "UNKNOWN_TERRITORY")]
    [InlineData(RuleViolations.TerritoryNotOwned, "TERRITORY_NOT_OWNED")]
    [InlineData(RuleViolations.OriginNotOwned, "ORIGIN_NOT_OWNED")]
    [InlineData(RuleViolations.DestinationNotEnemy, "DESTINATION_NOT_ENEMY")]
    [InlineData(RuleViolations.DestinationNotOwn, "DESTINATION_NOT_OWN")]
    [InlineData(RuleViolations.NotAdjacent, "NOT_ADJACENT")]
    [InlineData(RuleViolations.OriginTooFewArmies, "ORIGIN_TOO_FEW_ARMIES")]
    [InlineData(RuleViolations.InvalidArmyCount, "INVALID_ARMY_COUNT")]
    [InlineData(RuleViolations.NotEnoughArmies, "NOT_ENOUGH_ARMIES")]
    [InlineData(RuleViolations.WrongPlacementTotal, "WRONG_PLACEMENT_TOTAL")]
    [InlineData(RuleViolations.NotImplementedYet, "NOT_IMPLEMENTED_YET")]
    public void TheViolationCodes_AreTheStableStringsOfTheApiContract(string code, string expectedLiteral)
    {
        Assert.Equal(expectedLiteral, code);
    }
}
