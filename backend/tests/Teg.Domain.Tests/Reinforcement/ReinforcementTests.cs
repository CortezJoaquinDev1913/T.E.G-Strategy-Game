using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Events;
using Teg.Domain.Map;
using Teg.Domain.Rules;
using Teg.Domain.State;
using Teg.Domain.Tests.Support;
using static Teg.Domain.Tests.Support.GameFixtures;
using static Teg.Domain.Tests.Support.ReinforcementFixtures;

namespace Teg.Domain.Tests.Reinforcement;

/// <summary>
/// E7 / R§5: at the start of every turn of round 2 on, the participant earns armies = max(3, countries / 2) +
/// the bonus of every continent they own IN FULL at that moment (+ trades, see TradeTests). The pool is computed
/// ONCE and lives in the state (<see cref="GameState.Turn"/>). Continent armies may only go to that continent;
/// the others go anywhere. Placements can be partial; the phase ends, by itself, when everything is placed, and
/// not before. Seat 1 is the participant whose turn is under test (see <see cref="ReinforcementFixtures"/>).
/// </summary>
public sealed class ReinforcementTests
{
    private const string Asia = "asia";
    private const string Oceania = "oceania";

    // ---- R§5.1: the countries ------------------------------------------------------------------

    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 3)]
    [InlineData(5, 3)]
    [InlineData(6, 3)]
    [InlineData(7, 3)]
    [InlineData(8, 4)]
    [InlineData(9, 4)]
    [InlineData(15, 7)]
    [InlineData(25, 12)]
    public void R5_TheCountriesGiveAtLeastThree_OtherwiseHalfOfThemRoundedDown(int countries, int armies)
    {
        var state = ReinforcementOfSeatOne(SpreadCountries(countries));

        Assert.Equal((1, 2, GamePhase.Reinforcement), (state.ActiveSeat, state.Round, state.Phase));
        Assert.Equal(Pool(armies), state.Turn);
    }

    [Fact]
    public void R5_FiftyCountries_AreWorthTwentyFive_PlusEveryContinentBonus()
    {
        var all = RealMap.Territories.Select(t => t.Id);

        var started = ApplyOk(SeatOneTurnComesNext(all), new EndTurn(0));

        Assert.Equal(
            Pool(25, ("america-del-norte", 5), ("america-del-sur", 3), ("europa", 5), ("africa", 3), (Asia, 7), (Oceania, 2)),
            started.State.Turn);
        Assert.Equal(50, started.State.Turn.PendingArmies);
    }

    // ---- R§5.2: the continents ------------------------------------------------------------------

    [Theory]
    [InlineData(Asia, 15, 7, 7)]
    [InlineData("europa", 9, 4, 5)]
    [InlineData("america-del-norte", 10, 5, 5)]
    [InlineData("america-del-sur", 6, 3, 3)]
    [InlineData("africa", 6, 3, 3)]
    [InlineData(Oceania, 4, 3, 2)]
    public void R5_AContinentOwnedInFull_AddsItsBonus_ToTheArmiesOfTheCountries(
        string continent,
        int countries,
        int countryArmies,
        int bonus)
    {
        var owned = ContinentCountries(continent);
        Assert.Equal(countries, owned.Length); // the theory data matches the real board

        var state = ReinforcementOfSeatOne(owned);

        Assert.Equal(Pool(countryArmies, (continent, bonus)), state.Turn);
    }

    [Theory]
    [InlineData(Asia, 7)] // 14 of its 15 countries
    [InlineData(Oceania, 3)]
    public void R5_AContinentMissingOneCountry_GivesNoBonus(string continent, int countryArmies)
    {
        var allButOne = ContinentCountries(continent).SkipLast(1);

        var state = ReinforcementOfSeatOne(allButOne);

        Assert.Equal(Pool(countryArmies), state.Turn);
    }

    [Fact]
    public void R5_SeveralContinents_AddUp_ListedInMapOrder()
    {
        var asiaAndOceania = ContinentCountries(Asia).Concat(ContinentCountries(Oceania)); // 19 countries
        var americasAndAfrica = ContinentCountries("america-del-norte")
            .Concat(ContinentCountries("america-del-sur"))
            .Concat(ContinentCountries("africa")); // 22 countries

        var first = ReinforcementOfSeatOne(asiaAndOceania).Turn;
        var second = ReinforcementOfSeatOne(americasAndAfrica).Turn;

        Assert.Equal(Pool(9, (Asia, 7), (Oceania, 2)), first);
        Assert.Equal(18, first.PendingArmies);
        Assert.Equal(Pool(11, ("america-del-norte", 5), ("america-del-sur", 3), ("africa", 3)), second);
        Assert.Equal(22, second.PendingArmies);
    }

    [Theory]
    [InlineData(new[] { "p", "q" }, 3, "x", 9)] // continent x is worth 9 on this board (the real board never says 9)
    [InlineData(new[] { "r", "s", "t" }, 3, "y", 4)]
    public void R5_TheBonusIsReadFromTheMap_NotFromConstants(string[] owned, int countryArmies, string continent, int bonus)
    {
        var before = BonusBoard.BeforeSeatOne(owned);

        var started = ApplyOk(before, new EndTurn(0), BonusBoard.Map);

        Assert.Equal(Pool(countryArmies, (continent, bonus)), started.State.Turn);
    }

    [Fact]
    public void R5_TheTurnChange_AnnouncesThePoolInAnEvent_RightAfterTurnStarted()
    {
        var owned = ContinentCountries(Oceania).Concat(SpreadCountries(12)); // 16 countries
        var before = SeatOneTurnComesNext(owned);

        var started = ApplyOk(before, new EndTurn(0));

        GameEvent[] expected =
        [
            new PhaseChanged(0, GamePhase.Attack, GamePhase.Card),
            new TurnEnded(0),
            new TurnStarted(1, Round: 2, GamePhase.Reinforcement),
            new ReinforcementPoolComputed(1, CountryArmies: 8, new[] { new ContinentPool(Oceania, 2) }.ToValueArray()),
        ];
        Assert.Equal(expected, started.Events.ToArray());
    }

    [Theory]
    [InlineData(2, 12)]
    [InlineData(3, 8)]
    [InlineData(4, 6)]
    public void R4_TheFirstReinforcementOfTheGame_BelongsToTheFirstPlayer_InRoundTwo(int participants, int firstPlayerArmies)
    {
        var state = FinishPlacementRound(FinishPlacementRound(Deal(participants, seed: 7)));

        // The hostilities round (round 1) offers nothing to place.
        Assert.Equal((GamePhase.Attack, 1), (state.Phase, state.Round));
        Assert.Equal(TurnContext.None, state.Turn);

        for (var turn = 0; turn < participants; turn++)
        {
            state = ApplyOk(state, new EndTurn(state.ActiveSeat)).State;
        }

        Assert.Equal((state.FirstSeat, 2, GamePhase.Reinforcement), (state.ActiveSeat, state.Round, state.Phase));
        Assert.Equal(firstPlayerArmies, state.Turn.FreeArmies); // the first player holds the extra country of a split
    }

    [Fact]
    public void R5_ThePoolIsComputedOnce_ChangesAfterTheTurnStartedDoNotReachIt()
    {
        var start = ReinforcementOfSeatOne(SpreadCountries(16)); // 8 armies, no continent
        var own = OwnedBy(start, 1)[0];
        var afterPartial = ApplyOk(start, Reinforcing(1, (own, 3))).State;

        // Seat 1 "conquers" all of Oceania and several more countries while the phase is open.
        var gains = ContinentCountries(Oceania).Concat(SpreadCountries(25).Skip(16)).ToHashSet();
        var changed = afterPartial with
        {
            Territories = afterPartial.Territories.Select(t => gains.Contains(t.Id) ? t with { Owner = 1 } : t).ToValueArray(),
        };

        var next = ApplyOk(changed, Reinforcing(1, (own, 1))).State;

        Assert.Equal(Pool(4), next.Turn); // 8 - 3 - 1: no Oceania bonus, no extra country armies
    }

    // ---- placing: partial placements, the phase moves on by itself --------------------------------------

    [Fact]
    public void R5_APartialPlacement_ReducesThePool_AndKeepsThePhaseOpen()
    {
        var start = ReinforcementOfSeatOne(SpreadCountries(16)); // 8 armies
        var own = OwnedBy(start, 1)[0];

        var placed = ApplyOk(start, Reinforcing(1, (own, 3)));

        var expectedState = start with
        {
            Version = start.Version + 1,
            Turn = Pool(5),
            Territories = start.Territories.Select(t => t.Id == own ? t with { Armies = 4 } : t).ToValueArray(),
        };
        Assert.Equal(expectedState, placed.State);
        GameEvent[] expectedEvents = [new ReinforcementsPlaced(1, new[] { new ArmyPlacement(own, 3) }.ToValueArray())];
        Assert.Equal(expectedEvents, placed.Events.ToArray());
    }

    [Fact]
    public void R5_PlacingTheRest_InASecondCommand_FinishesThePhase_AndMovesToAttack()
    {
        var start = ReinforcementOfSeatOne(SpreadCountries(16)); // 8 armies
        var owned = OwnedBy(start, 1);
        var afterFirst = ApplyOk(start, Reinforcing(1, (owned[0], 3))).State;

        var second = ApplyOk(afterFirst, Reinforcing(1, (owned[1], 5)));

        GameEvent[] expectedEvents =
        [
            new ReinforcementsPlaced(1, new[] { new ArmyPlacement(owned[1], 5) }.ToValueArray()),
            new PhaseChanged(1, GamePhase.Reinforcement, GamePhase.Attack),
        ];
        Assert.Equal(expectedEvents, second.Events.ToArray());
        Assert.Equal((GamePhase.Attack, 1, 2), (second.State.Phase, second.State.ActiveSeat, second.State.Round));
        Assert.Equal(TurnContext.None, second.State.Turn);
        Assert.Equal((4, 6), (ArmiesIn(second.State, owned[0]), ArmiesIn(second.State, owned[1])));
        Assert.Equal(afterFirst.Version + 1, second.State.Version);
    }

    [Fact]
    public void R5_ThePoolCanBePlacedInASingleCommand_OverSeveralCountries_SummingRepeatedOnes()
    {
        var start = ReinforcementOfSeatOne(SpreadCountries(16)); // 8 armies
        var owned = OwnedBy(start, 1);

        var placed = ApplyOk(start, Reinforcing(1, (owned[0], 2), (owned[1], 3), (owned[0], 3))).State;

        Assert.Equal(GamePhase.Attack, placed.Phase);
        Assert.Equal((6, 4), (ArmiesIn(placed, owned[0]), ArmiesIn(placed, owned[1])));
        Assert.Equal(ArmiesOf(start, 1) + 8, ArmiesOf(placed, 1));
    }

    [Fact]
    public void R5_AfterThePhase_NoMoreReinforcementsCanBePlaced()
    {
        AssertRejected(
            RuleViolations.WrongPhase,
            () => PlaceWholePool(ReinforcementOfSeatOne(SpreadCountries(16))),
            state => Reinforcing(1, (OwnedBy(state, 1)[0], 1)));
    }

    // ---- the phase cannot end before everything is placed -----------------------------------------------

    [Fact]
    public void R5_ThePhaseCannotEndBeforePlacing_TheTurnCannotBeEnded()
    {
        AssertRejected(
            RuleViolations.ArmiesPending,
            () => ReinforcementOfSeatOne(SpreadCountries(16)),
            _ => new EndTurn(1));
    }

    [Fact]
    public void R5_ThePhaseCannotEndEitherAfterAPartialPlacement()
    {
        AssertRejected(
            RuleViolations.ArmiesPending,
            () =>
            {
                var start = ReinforcementOfSeatOne(SpreadCountries(16));
                return ApplyOk(start, Reinforcing(1, (OwnedBy(start, 1)[0], 7))).State; // 1 army left
            },
            _ => new EndTurn(1));
    }

    [Fact]
    public void R5_OnceEverythingIsPlaced_TheTurnCanBeEnded()
    {
        var attack = PlaceWholePool(ReinforcementOfSeatOne(SpreadCountries(16)));

        var ended = ApplyOk(attack, new EndTurn(1));

        Assert.Equal((0, 3), (ended.State.ActiveSeat, ended.State.Round)); // seat 1 was last: round 2 is over
    }

    [Fact]
    public void R5_ARemainingContinentBonus_KeepsThePhaseOpen_EvenIfTheFreeArmiesAreGone()
    {
        var start = AsiaAndFourOthers();
        var outside = OutsideAsia(start)[0];

        var afterFree = ApplyOk(start, Reinforcing(1, (outside, 9))).State;

        Assert.Equal(GamePhase.Reinforcement, afterFree.Phase);
        Assert.Equal(Pool(0, (Asia, 7)), afterFree.Turn);
    }

    // ---- R§5.2: the continent armies only go to their continent -----------------------------------------

    /// <summary>Seat 1 owns Asia (15) and the first country of 4 other continents: 19 countries = 9 free + 7 for Asia.</summary>
    private static GameState AsiaAndFourOthers()
    {
        var others = new[] { "america-del-norte", "america-del-sur", "europa", "africa" }.Select(c => ContinentCountries(c)[0]);

        var state = ReinforcementOfSeatOne(ContinentCountries(Asia).Concat(others));
        Assert.Equal(Pool(9, (Asia, 7)), state.Turn);

        return state;
    }

    private static string[] OutsideAsia(GameState state) =>
        OwnedBy(state, 1).Except(ContinentCountries(Asia)).ToArray();

    [Fact]
    public void R5_TheContinentArmies_CanBePlacedInThatContinent_BeforeTheFreeOnes()
    {
        var start = AsiaAndFourOthers();
        var asia = ContinentCountries(Asia);

        var sevenInAsia = ApplyOk(start, Reinforcing(1, (asia[0], 7))).State;
        var twelveInAsia = ApplyOk(start, Reinforcing(1, (asia[0], 5), (asia[1], 7))).State;

        Assert.Equal(Pool(9), sevenInAsia.Turn); // the bonus is used first: the free armies stay free
        Assert.Equal(Pool(4), twelveInAsia.Turn); // 7 from the bonus + 5 free
        Assert.Equal(
            (6, 8, 1), // 1 + 5, 1 + 7, untouched
            (ArmiesIn(twelveInAsia, asia[0]), ArmiesIn(twelveInAsia, asia[1]), ArmiesIn(twelveInAsia, asia[2])));
    }

    [Fact]
    public void R5_TheFreeArmies_CanGoAnywhere_ThoseOfAContinentOnlyThere()
    {
        var start = AsiaAndFourOthers();
        var outside = OutsideAsia(start);
        var asia = ContinentCountries(Asia);

        var split = ApplyOk(start, Reinforcing(1, (outside[0], 4), (outside[1], 5), (asia[0], 3))).State;
        var all = ApplyOk(start, Reinforcing(1, (outside[0], 9), (asia[0], 7)));

        Assert.Equal(Pool(0, (Asia, 4)), split.Turn);
        Assert.Equal(GamePhase.Attack, all.State.Phase);
    }

    [Theory]
    [InlineData(10)] // 10 outside Asia: the free armies are 9
    [InlineData(16)] // the whole pool outside Asia
    public void R5_TheContinentArmies_CannotGoOutsideTheirContinent(int armiesOutsideAsia)
    {
        AssertRejected(
            RuleViolations.ContinentBonusMisplaced,
            AsiaAndFourOthers,
            state => Reinforcing(1, (OutsideAsia(state)[0], armiesOutsideAsia)));
    }

    [Fact]
    public void R5_AfterUsingTheFreeArmies_TheRestOfAContinentBonus_CannotGoElsewhere()
    {
        AssertRejected(
            RuleViolations.ContinentBonusMisplaced,
            () =>
            {
                var start = AsiaAndFourOthers();
                return ApplyOk(start, Reinforcing(1, (OutsideAsia(start)[0], 9))).State; // 7 for Asia remain
            },
            state => Reinforcing(1, (OutsideAsia(state)[0], 1)));
    }

    [Fact]
    public void R5_EachContinentBonus_OnlyGoesToItsOwnContinent()
    {
        // Asia 7 + Oceania 2 + 9 free = 18 armies, and every country of seat 1 is in one of the two continents.
        GameState Build() => ReinforcementOfSeatOne(ContinentCountries(Asia).Concat(ContinentCountries(Oceania)));
        var asia = ContinentCountries(Asia);
        var oceania = ContinentCountries(Oceania);

        // 18 armies on Asia: the 2 of Oceania have nowhere to go.
        AssertRejected(RuleViolations.ContinentBonusMisplaced, Build, _ => Reinforcing(1, (asia[0], 18)));

        // 16 on Asia (7 bonus + 9 free) is fine and leaves exactly the Oceania bonus.
        var afterAsia = ApplyOk(Build(), Reinforcing(1, (asia[0], 16))).State;
        Assert.Equal(Pool(0, (Oceania, 2)), afterAsia.Turn);
        Assert.Equal(GamePhase.Reinforcement, afterAsia.Phase);

        var done = ApplyOk(afterAsia, Reinforcing(1, (oceania[0], 2))).State;
        Assert.Equal(GamePhase.Attack, done.Phase);
    }

    // ---- rejections: typed violation, state unchanged ---------------------------------------------------

    private static GameState Start() => ReinforcementOfSeatOne(SpreadCountries(16)); // 8 armies

    [Fact]
    public void Placing_OnACountryOfAnotherParticipant_IsRejected()
    {
        var foreign = ContinentCountries(Oceania)[0]; // seat 0's

        AssertRejected(RuleViolations.TerritoryNotOwned, Start, _ => Reinforcing(1, (foreign, 1)));
    }

    [Fact]
    public void Placing_OnAnOwnedAndAForeignCountry_RejectsTheWholeCommand()
    {
        var foreign = ContinentCountries(Oceania)[0];

        AssertRejected(
            RuleViolations.TerritoryNotOwned,
            Start,
            state => Reinforcing(1, (OwnedBy(state, 1)[0], 2), (foreign, 1)));
    }

    [Fact]
    public void Placing_OnACountryThatDoesNotExist_IsRejected()
    {
        AssertRejected(RuleViolations.UnknownTerritory, Start, _ => Reinforcing(1, ("atlantis", 1)));
    }

    [Theory]
    [InlineData(9)]
    [InlineData(100)]
    public void Placing_MoreThanThePool_IsRejected(int armies)
    {
        AssertRejected(
            RuleViolations.ReinforcementsExceeded,
            Start,
            state => Reinforcing(1, (OwnedBy(state, 1)[0], armies)));
    }

    [Fact]
    public void Placing_MoreThanWhatRemains_AfterAPartialPlacement_IsRejected()
    {
        AssertRejected(
            RuleViolations.ReinforcementsExceeded,
            () =>
            {
                var start = Start();
                return ApplyOk(start, Reinforcing(1, (OwnedBy(start, 1)[0], 3))).State; // 5 remain
            },
            state => Reinforcing(1, (OwnedBy(state, 1)[0], 3), (OwnedBy(state, 1)[1], 3)));
    }

    [Fact]
    public void Placing_AMixOfEntriesThatExceedsThePool_IsRejected()
    {
        AssertRejected(
            RuleViolations.ReinforcementsExceeded,
            Start,
            state => Reinforcing(1, (OwnedBy(state, 1)[0], 4), (OwnedBy(state, 1)[1], 4), (OwnedBy(state, 1)[2], 1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Placing_AnEntryWithoutAPositiveNumberOfArmies_IsRejected(int armies)
    {
        AssertRejected(
            RuleViolations.InvalidArmyCount,
            Start,
            state => Reinforcing(1, (OwnedBy(state, 1)[0], 3), (OwnedBy(state, 1)[1], armies)));
    }

    [Fact]
    public void Placing_Nothing_IsRejected()
    {
        AssertRejected(RuleViolations.InvalidArmyCount, Start, _ => Reinforcing(1));
    }

    [Fact]
    public void Placing_ByAnotherParticipant_IsRejected_EvenOnTheirOwnCountry()
    {
        AssertRejected(
            RuleViolations.NotActiveParticipant,
            Start,
            state => Reinforcing(0, (OwnedBy(state, 0)[0], 1)));
    }

    [Theory]
    [InlineData(GamePhase.InitialPlacement)]
    [InlineData(GamePhase.Attack)]
    [InlineData(GamePhase.Regroup)]
    [InlineData(GamePhase.Card)]
    public void Placing_OutsideTheReinforcementPhase_IsRejected_ThereIsNoRefuerzoInTheHostilitiesRoundEither(GamePhase phase)
    {
        AssertRejected(
            RuleViolations.WrongPhase,
            () => TinyBoard.State(phase) with { Turn = Pool(3) },
            _ => Reinforcing(0, ("a", 1)),
            TinyBoard.Map);
    }

    [Fact]
    public void Placing_InAFinishedGame_IsRejected()
    {
        AssertRejected(
            RuleViolations.GameFinished,
            () => TinyBoard.State(GamePhase.Finished) with { Turn = Pool(3) },
            _ => Reinforcing(0, ("a", 1)),
            TinyBoard.Map);
    }

    [Fact]
    public void Placing_InTheReinforcementPhase_OfAHandBuiltState_IsAccepted_ThePhaseIsTheOnlyDifference()
    {
        var state = TinyBoard.State(GamePhase.Reinforcement) with { Turn = Pool(3) };

        var placed = ApplyOk(state, Reinforcing(0, ("a", 3)), TinyBoard.Map);

        Assert.Equal((GamePhase.Attack, 8), (placed.State.Phase, ArmiesIn(placed.State, "a")));
    }

    // ---- the codes ------------------------------------------------------------------------------

    [Theory]
    [InlineData(RuleViolations.ArmiesPending, "ARMIES_PENDING")]
    [InlineData(RuleViolations.TradeRequired, "TRADE_REQUIRED")]
    [InlineData(RuleViolations.ReinforcementsExceeded, "REINFORCEMENTS_EXCEEDED")]
    [InlineData(RuleViolations.ContinentBonusMisplaced, "CONTINENT_BONUS_MISPLACED")]
    public void TheReinforcementViolationCodes_AreStable_BecauseTheApiExposesThem(string code, string literal)
    {
        Assert.Equal(literal, code);
    }

    /// <summary>
    /// A board with two continents worth 9 (x: p, q) and 4 (y: r, s, t): the real board never uses these numbers,
    /// so only a bonus taken from the map data can pass.
    /// </summary>
    private static class BonusBoard
    {
        public static readonly MapDefinition Map = MapDefinition.FromJson("""
            {
              "version": 1,
              "continents": [
                { "id": "x", "name": "Equis", "bonus": 9, "territories": ["p", "q"] },
                { "id": "y", "name": "Ye", "bonus": 4, "territories": ["r", "s", "t"] }
              ],
              "territories": [
                { "id": "p", "name": "P", "continent": "x", "symbol": "galeon" },
                { "id": "q", "name": "Q", "continent": "x", "symbol": "globo" },
                { "id": "r", "name": "R", "continent": "y", "symbol": "canon" },
                { "id": "s", "name": "S", "continent": "y", "symbol": "galeon" },
                { "id": "t", "name": "T", "continent": "y", "symbol": "globo" }
              ],
              "borders": [
                { "a": "p", "b": "q", "type": "land" },
                { "a": "q", "b": "r", "type": "land" },
                { "a": "r", "b": "s", "type": "land" },
                { "a": "s", "b": "t", "type": "land" }
              ]
            }
            """);

        /// <summary>Round 2, seat 0 ends its turn in Ataque; seat 1 owns exactly the named countries.</summary>
        public static GameState BeforeSeatOne(params string[] ownedBySeatOne) =>
            TinyBoard.State(GamePhase.Attack) with
            {
                Round = 2,
                Territories = Map.Territories
                    .Select(t => new TerritoryState(t.Id, ownedBySeatOne.Contains(t.Id) ? 1 : 0, Armies: 1))
                    .ToValueArray(),
            };
    }
}
