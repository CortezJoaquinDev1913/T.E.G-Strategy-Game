using Teg.Application.Bots;
using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Map;
using Teg.Domain.Rules;
using Teg.Domain.State;
using static Teg.Application.Tests.Support.Games;

namespace Teg.Application.Tests.Bots;

/// <summary>
/// The heuristic policy on hand-made positions (B1): what it chooses in each phase, and that what it chooses is always something the
/// engine accepts. The positions are two participants on the real board: the active one owns the countries named, the other the rest.
/// </summary>
public class HeuristicBotPolicyTests
{
    private static readonly HeuristicBotPolicy Policy = new(Map);

    private static readonly string[] MapOrder = Map.Territories.Select(t => t.Id).ToArray();

    private static GameCommand Next(GameState state, HeuristicBotPolicy? policy = null) =>
        (policy ?? Policy).Next(state, LegalMoves.For(state, Map));

    /// <summary>The command, after checking that the engine accepts it.</summary>
    private static GameCommand Sound(GameState state, GameCommand command)
    {
        Advance(state, command);

        return command;
    }

    private static string[] OfSymbol(CardSymbol symbol, int count) =>
        Map.Territories.Where(t => t.Symbol == symbol).Take(count).Select(t => t.Id).ToArray();

    // ---- attack ---------------------------------------------------------------------------------------------------------------

    /// <summary>France has 10 armies; every enemy country has 20 unless said otherwise. France borders alemania, espana, gran-bretana and italia.</summary>
    private static GameState FranceAttacks(int alemania, int espana, int italia, int granBretana) =>
        SetArmies(
            Position(GamePhase.Attack, restArmies: 20, ("francia", 10)),
            ("alemania", alemania), ("espana", espana), ("italia", italia), ("gran-bretana", granBretana));

    [Theory]
    [InlineData(3, 1, 8, 9, "espana")] // espana is the weakest
    [InlineData(3, 9, 12, 12, "alemania")] // the only one it beats clearly
    [InlineData(9, 8, 9, 9, "espana")] // 10 armies against 8: 10 - 1 >= 8 + 1, the limit
    [InlineData(20, 20, 20, 8, "gran-bretana")]
    public void B1_Attacks_TheWeakestDefenderItBeatsClearly(int alemania, int espana, int italia, int granBretana, string target)
    {
        var state = FranceAttacks(alemania, espana, italia, granBretana);

        var command = Sound(state, Next(state));

        Assert.Equal(new Attack(state.ActiveSeat, "francia", target), command);
    }

    [Fact]
    public void B1_DoesNotAttack_WhenNoDefenderIsClearlyWeaker_AndEndsTheTurn()
    {
        var state = FranceAttacks(alemania: 9, espana: 9, italia: 9, granBretana: 9); // 10 against 9: not clearly

        var command = Sound(state, Next(state));

        Assert.Equal(new EndTurn(state.ActiveSeat), command);
    }

    [Fact]
    public void B1_OnATieBetweenDefenders_ItAttacksTheFirstInMapOrder()
    {
        var state = FranceAttacks(alemania: 2, espana: 2, italia: 20, granBretana: 20);
        var first = new[] { "alemania", "espana" }.MinBy(MapOrder.ToList().IndexOf)!;

        var command = (Attack)Sound(state, Next(state));

        Assert.Equal(first, command.To);
        Assert.Equal(first, ((Attack)Next(state)).To); // and the same again
    }

    [Fact]
    public void B1_AgainstTheSameDefender_ItAttacksFromTheBiggestStack()
    {
        // francia (10) and espana (6) both border gran-bretana (2); espana comes first in map order, but the bigger stack goes first.
        var state = SetArmies(
            Position(GamePhase.Attack, restArmies: 20, ("francia", 10), ("espana", 6)),
            ("gran-bretana", 2));
        Assert.True(MapOrder.ToList().IndexOf("espana") < MapOrder.ToList().IndexOf("francia"));

        var command = Sound(state, Next(state));

        Assert.Equal(new Attack(state.ActiveSeat, "francia", "gran-bretana"), command);
    }

    [Fact]
    public void B1_ItPrefersTheAttackThatCompletesAContinent_OverTheWeakestDefender()
    {
        // The bot owns all of Europa but gran-bretana (6 armies), francia has 10. sahara (1 army) is the weakest defender in reach
        // (from espana, 3 armies), yet taking gran-bretana completes the continent.
        var europeWithoutBritain = new[] { "alemania", "espana", "francia", "islandia", "italia", "polonia", "rusia", "suecia" };
        var state = SetArmies(
            Position(GamePhase.Attack, restArmies: 20, europeWithoutBritain.Select(id => (id, id == "francia" ? 10 : 3)).ToArray()),
            ("gran-bretana", 6), ("sahara", 1));

        var command = Sound(state, Next(state));

        Assert.Equal(new Attack(state.ActiveSeat, "francia", "gran-bretana"), command);
    }

    // ---- occupation -----------------------------------------------------------------------------------------------------------

    private static GameState Conquered(string from, int fromArmies, string to, int toArmies, params (string Id, int Armies)[] otherMine)
    {
        var state = Position(GamePhase.Attack, restArmies: 20, [(from, fromArmies), (to, toArmies), .. otherMine]);

        return state with { Turn = TurnContext.None with { PendingConquest = new PendingConquest(from, to), ConquestsThisTurn = 1 } };
    }

    [Fact]
    public void B1_Occupation_MovesTheMaximum_WhenTheConqueredCountryBordersAnEnemy()
    {
        var state = Conquered("francia", 7, "espana", 3); // espana borders gran-bretana and sahara, which are the enemy's

        var command = Sound(state, Next(state));

        Assert.Equal(new MoveAfterConquest(state.ActiveSeat, 3), command); // min(3, 7 + 3 - 1)
    }

    [Fact]
    public void B1_Occupation_TheMaximumIsWhatTheRulesAllow()
    {
        var state = Conquered("francia", 2, "espana", 1); // the origin had 2 + 1 - 1... it keeps one: at most 2 stay

        var command = Sound(state, Next(state));

        Assert.Equal(new MoveAfterConquest(state.ActiveSeat, 2), command);
    }

    [Fact]
    public void B1_Occupation_MovesTheMinimum_WhenTheConqueredCountryIsInteriorAndNothingIsGainedThere()
    {
        // The bot also owns every neighbour of espana (francia, gran-bretana, sahara): armies there would face nobody.
        var state = Conquered("francia", 7, "espana", 3, ("gran-bretana", 2), ("sahara", 2));

        var command = Sound(state, Next(state));

        Assert.Equal(new MoveAfterConquest(state.ActiveSeat, 1), command);
    }

    // ---- reinforcement and trades ---------------------------------------------------------------------------------------------

    private static GameState Reinforcing(GameState position, int free, params ContinentPool[] bonuses) =>
        position with { Phase = GamePhase.Reinforcement, Turn = new TurnContext(free, bonuses.ToValueArray()) };

    [Fact]
    public void B1_Reinforces_TheFrontierCountryWithTheBestAdvantage_WithTheWholePool()
    {
        // francia (3 armies) faces alemania (4): 3 - 4 = -1; sahara (6) faces 20s: -14. espana and gran-bretana are not worth it.
        var position = SetArmies(
            Position(GamePhase.Attack, restArmies: 20, ("francia", 3), ("sahara", 6), ("espana", 1), ("gran-bretana", 1)),
            ("alemania", 4));
        var state = Reinforcing(position, free: 5);

        var command = Sound(state, Next(state));

        Assert.Equal(new PlaceReinforcements(state.ActiveSeat, new[] { new ArmyPlacement("francia", 5) }.ToValueArray()), command);
    }

    [Fact]
    public void B1_Reinforces_ADifferentCountry_WhenTheAdvantageChanges()
    {
        var position = Position(GamePhase.Attack, restArmies: 20, ("francia", 3), ("sahara", 30), ("espana", 1), ("gran-bretana", 1));
        var state = Reinforcing(position, free: 5);

        var command = Sound(state, Next(state));

        Assert.Equal(new PlaceReinforcements(state.ActiveSeat, new[] { new ArmyPlacement("sahara", 5) }.ToValueArray()), command);
    }

    [Fact]
    public void B1_Reinforces_AFrontierCountry_NeverAnInteriorOne_EvenWithAHugeStack()
    {
        // espana is interior (its neighbours are francia, gran-bretana, sahara and all are the bot's) and holds 40 armies.
        var position = Position(GamePhase.Attack, restArmies: 20, ("espana", 40), ("francia", 2), ("gran-bretana", 2), ("sahara", 2));
        var state = Reinforcing(position, free: 4);
        var firstFrontier = new[] { "francia", "gran-bretana", "sahara" }.MinBy(MapOrder.ToList().IndexOf)!; // equal advantage: map order

        var command = Sound(state, Next(state));

        Assert.Equal(new PlaceReinforcements(state.ActiveSeat, new[] { new ArmyPlacement(firstFrontier, 4) }.ToValueArray()), command);
    }

    [Fact]
    public void B1_Reinforces_ContinentBonusArmiesInsideTheirContinent()
    {
        var europe = Map.Continents.Single(c => c.Id == "europa").TerritoryIds;
        var position = Position(GamePhase.Attack, restArmies: 20, europe.Select(id => (id, 2)).Append(("sahara", 30)).ToArray());
        var state = Reinforcing(position, free: 3, new ContinentPool("europa", 5));

        var command = (PlaceReinforcements)Sound(state, Next(state));

        Assert.Equal(8, command.Placements.Sum(p => p.Armies));
        Assert.True(command.Placements.Where(p => europe.Contains(p.TerritoryId)).Sum(p => p.Armies) >= 5);
        Assert.Contains(new ArmyPlacement("sahara", 3), command.Placements); // the free armies go where the advantage is: the 30-army stack
    }

    [Fact]
    public void B1_Trades_BeforePlacing_WheneverATradeIsPossible()
    {
        var cards = OfSymbol(CardSymbol.Galeon, 3);
        var position = Position(GamePhase.Attack, restArmies: 20, ("francia", 3), ("sahara", 6));
        var state = WithHand(Reinforcing(position, free: 5), position.ActiveSeat, cards.Select(id => new HeldCard(id)).ToArray());

        var command = Sound(state, Next(state));

        Assert.Equal(new TradeCards(state.ActiveSeat, cards.ToValueArray()), command); // three cards: not mandatory, still traded first
    }

    [Fact]
    public void B1_DoesNotTrade_WithoutATradeToMake_AndPlaces()
    {
        var cards = OfSymbol(CardSymbol.Galeon, 2);
        var position = Position(GamePhase.Attack, restArmies: 20, ("francia", 3), ("sahara", 6));
        var state = WithHand(Reinforcing(position, free: 5), position.ActiveSeat, cards.Select(id => new HeldCard(id)).ToArray());

        var command = Sound(state, Next(state));

        Assert.IsType<PlaceReinforcements>(command);
    }

    [Fact]
    public void B1_AMandatoryTrade_ComesFirst_AndKeepsTheCardsThatWillPayAPremium()
    {
        // Five cards: three galeones, one canon, one globo. g0 is a country the bot owns and whose premium is still unclaimed,
        // so trading it would lose 2 armies: of the valid triples, the first that does not use it is (g1, canon, globo).
        var galeones = OfSymbol(CardSymbol.Galeon, 3);
        var canon = OfSymbol(CardSymbol.Canon, 1)[0];
        var globo = OfSymbol(CardSymbol.Globo, 1)[0];
        var position = Position(GamePhase.Attack, restArmies: 20, (galeones[0], 3)); // the only country it owns
        var hand = new[] { galeones[0], galeones[1], galeones[2], canon, globo }.Select(id => new HeldCard(id)).ToArray();
        var state = WithHand(Reinforcing(position, free: 5), position.ActiveSeat, hand);

        var command = Sound(state, Next(state));

        Assert.Equal(new TradeCards(state.ActiveSeat, new[] { galeones[1], canon, globo }.ToValueArray()), command);
    }

    [Fact]
    public void B1_ACardWhosePremiumWasAlreadyPaid_IsNotWorthKeeping()
    {
        // The same five cards as above, but the premium of g0 was already paid: nothing is lost by trading it, so the first triple stands.
        var galeones = OfSymbol(CardSymbol.Galeon, 3);
        var position = Position(GamePhase.Attack, restArmies: 20, (galeones[0], 3)); // the only country it owns
        var hand = new[] { galeones[0], galeones[1], galeones[2], OfSymbol(CardSymbol.Canon, 1)[0], OfSymbol(CardSymbol.Globo, 1)[0] }
            .Select((id, index) => new HeldCard(id, BonusClaimed: index == 0))
            .ToArray();
        var state = WithHand(Reinforcing(position, free: 5), position.ActiveSeat, hand);

        var command = Sound(state, Next(state));

        Assert.Equal(new TradeCards(state.ActiveSeat, galeones.ToValueArray()), command);
    }

    // ---- regroup --------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// espana (6 armies) has only own neighbours (francia, gran-bretana, sahara): an interior stack that could be brought to a border.
    /// Every enemy country has 20 armies: nothing can be attacked.
    /// </summary>
    private static GameState InteriorStack(int francia = 3, int granBretana = 2) =>
        Position(GamePhase.Attack, restArmies: 20, ("espana", 6), ("francia", francia), ("gran-bretana", granBretana), ("sahara", 2));

    [Fact]
    public void B1_WithNothingToAttack_ItStartsTheRegroup_WhenAnInteriorStackCanReachABorder()
    {
        var state = InteriorStack();

        Assert.Equal(new StartRegroup(state.ActiveSeat), Sound(state, Next(state)));
    }

    [Fact]
    public void B1_Regroup_BringsTheInteriorStackToTheBorderWithTheBestAdvantage()
    {
        var started = Advance(InteriorStack(), new StartRegroup(InteriorStack().ActiveSeat));

        var command = Sound(started, Next(started));

        // quota 6 - 1 = 5 armies; francia (3 armies, faces 20s) is the best of the three borders
        Assert.Equal(new Regroup(started.ActiveSeat, "espana", "francia", 5), command);
    }

    [Fact]
    public void B1_Regroup_ChoosesAnotherBorder_WhenItHasTheBetterAdvantage()
    {
        var state = InteriorStack(francia: 3, granBretana: 12);
        var started = Advance(state, new StartRegroup(state.ActiveSeat));

        var command = Sound(started, Next(started));

        Assert.Equal(new Regroup(started.ActiveSeat, "espana", "gran-bretana", 5), command);
    }

    [Fact]
    public void B1_WithNoInteriorStackToMove_ItEndsTheTurnWithoutRegrouping()
    {
        var state = FranceAttacks(alemania: 20, espana: 20, italia: 20, granBretana: 20); // one country, nothing interior, nothing to attack
        var started = Advance(state, new StartRegroup(state.ActiveSeat));

        Assert.Equal(new EndTurn(state.ActiveSeat), Sound(state, Next(state))); // from Ataque: straight to the end
        Assert.Equal(new EndTurn(state.ActiveSeat), Sound(started, Next(started))); // from Reagrupe: nothing worth moving
    }

    // ---- initial placement ----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(2, 6)]
    [InlineData(3, 5)]
    [InlineData(4, 5)]
    public void B1_InitialPlacement_PutsTheWholePoolOnAFrontierCountry(int participants, int pool)
    {
        var state = AllBots(Deal(7, Enumerable.Repeat(H, participants).ToArray()));

        var command = (PlaceInitialArmies)Sound(state, Next(state));

        var placement = Assert.Single(command.Placements);
        Assert.Equal(pool, placement.Armies);
        Assert.Equal(state.ActiveSeat, state.FindTerritory(placement.TerritoryId)!.Owner);
        Assert.Contains(Map.Neighbors(placement.TerritoryId), neighbour => state.FindTerritory(neighbour)!.Owner != state.ActiveSeat);
    }

    [Fact]
    public void B1_InitialPlacement_SecondRoundUsesTheSecondPool()
    {
        var state = AllBots(Deal(7, H, H, H));
        for (var turn = 0; turn < 3; turn++)
        {
            state = Advance(state, Next(state));
        }

        Assert.Equal(2, state.PlacementRound);
        var command = (PlaceInitialArmies)Sound(state, Next(state));

        Assert.Equal(3, command.Placements.Sum(p => p.Armies));
    }

    [Fact]
    public void B1_InitialPlacement_IsNotSubjectToTheCap()
    {
        var state = AllBots(Deal(7, H, H));
        state = state with { Turn = state.Turn with { BotActions = 1000 } };

        Assert.IsType<PlaceInitialArmies>(Sound(state, Next(state)));
    }

    // ---- the cap (B2) -------------------------------------------------------------------------------------------------------

    [Fact]
    public void B2_TheDefaultCapIsSixty()
    {
        Assert.Equal(60, HeuristicBotPolicy.DefaultMaxActionsPerTurn);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void B2_ACapBelowOne_IsRefused(int cap)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeuristicBotPolicy(Map, cap));
    }

    private static GameState WithActions(GameState state, int actions) =>
        state with { Turn = state.Turn with { BotActions = actions } };

    [Theory]
    [InlineData(59, false)] // one action left before the cap: it still attacks
    [InlineData(60, true)] // the cap is reached: it closes the turn although the attack is still favourable
    [InlineData(75, true)]
    public void B2_AtTheCap_TheBotEndsTheTurn_EvenIfAnAttackIsStillFavourable(int actions, bool endsTheTurn)
    {
        var state = WithActions(FranceAttacks(alemania: 3, espana: 1, italia: 8, granBretana: 9), actions);

        var command = Sound(state, Next(state));

        Assert.Equal(endsTheTurn ? new EndTurn(state.ActiveSeat) : new Attack(state.ActiveSeat, "francia", "espana"), command);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void B2_TheCapIsAParameter(int actions, bool endsTheTurn)
    {
        var state = WithActions(FranceAttacks(alemania: 3, espana: 1, italia: 8, granBretana: 9), actions);

        var command = Sound(state, Next(state, new HeuristicBotPolicy(Map, maxActionsPerTurn: 3)));

        Assert.Equal(endsTheTurn, command is EndTurn);
    }

    [Fact]
    public void B2_AtTheCap_APendingOccupationIsFinishedWithTheMinimum()
    {
        var state = WithActions(Conquered("francia", 7, "espana", 3), 60); // it would move 3 (frontier) before the cap

        var command = Sound(state, Next(state));

        Assert.Equal(new MoveAfterConquest(state.ActiveSeat, 1), command);
    }

    [Fact]
    public void B2_AtTheCap_InReagrupe_TheBotJustEndsTheTurn()
    {
        var started = Advance(InteriorStack(), new StartRegroup(InteriorStack().ActiveSeat)); // a useful regroup is available
        var state = WithActions(started, 60);

        Assert.Equal(new EndTurn(state.ActiveSeat), Sound(state, Next(state)));
    }

    [Fact]
    public void B2_AtTheCap_InRefuerzo_TheBotPlacesEveryPoolOnItsFirstCountry_ThenCanEndTheTurn()
    {
        var europe = Map.Continents.Single(c => c.Id == "europa").TerritoryIds;
        var position = Position(GamePhase.Attack, restArmies: 20, europe.Select(id => (id, 2)).Append(("sahara", 30)).ToArray());
        var state = WithActions(Reinforcing(position, free: 3, new ContinentPool("europa", 5)), 60);
        var firstOwned = MapOrder.First(id => state.FindTerritory(id)!.Owner == state.ActiveSeat);
        var firstEuropean = europe.OrderBy(MapOrder.ToList().IndexOf).First();

        var command = Sound(state, Next(state));

        Assert.Equal(
            new PlaceReinforcements(state.ActiveSeat, new[] { new ArmyPlacement(firstOwned, 3), new ArmyPlacement(firstEuropean, 5) }.ToValueArray()),
            command);

        var afterwards = Advance(state, command);
        Assert.Equal(GamePhase.Attack, afterwards.Phase);
        Assert.Equal(new EndTurn(state.ActiveSeat), Next(WithActions(afterwards, 61)));
    }

    [Fact]
    public void B2_AtTheCap_AMandatoryTradeStillComesBeforeThePlacement()
    {
        var galeones = OfSymbol(CardSymbol.Galeon, 3);
        var position = Position(GamePhase.Attack, restArmies: 20, ("francia", 3), ("sahara", 6));
        var hand = new[] { galeones[0], galeones[1], galeones[2], OfSymbol(CardSymbol.Canon, 1)[0], OfSymbol(CardSymbol.Globo, 1)[0] }
            .Select(id => new HeldCard(id)).ToArray();
        var state = WithActions(WithHand(Reinforcing(position, free: 5), position.ActiveSeat, hand), 60);

        Assert.IsType<TradeCards>(Sound(state, Next(state))); // nothing can be placed with five cards in hand
    }

    // ---- purity ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void B1_ThereIsNothingForABotToDo_InAFinishedGame()
    {
        var state = Deal(3, H, H) with { Phase = GamePhase.Finished, Winner = 0, WinReason = WinReason.Elimination };

        Assert.Throws<InvalidOperationException>(() => Next(state));
    }

    [Fact]
    public void B3_TheSameStateGivesTheSameCommand_FromAnyInstance_AnyNumberOfTimes()
    {
        GameState[] states =
        [
            FranceAttacks(3, 1, 8, 9),
            Reinforcing(Position(GamePhase.Attack, 20, ("francia", 3), ("sahara", 6)), free: 5),
            Conquered("francia", 7, "espana", 3),
            Advance(InteriorStack(), new StartRegroup(InteriorStack().ActiveSeat)),
            AllBots(Deal(5, H, H, H)),
        ];

        foreach (var state in states)
        {
            var first = Next(state);
            var again = Next(state);
            var other = Next(state, new HeuristicBotPolicy(Map));

            Assert.Equal(first, again);
            Assert.Equal(first, other);
        }
    }
}
