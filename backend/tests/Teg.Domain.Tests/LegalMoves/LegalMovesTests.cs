using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Map;
using Teg.Domain.Rules;
using Teg.Domain.Setup;
using Teg.Domain.State;
using Teg.Domain.Tests.Support;
using static Teg.Domain.Tests.Support.CardFixtures;
using static Teg.Domain.Tests.Support.CombatFixtures;
using static Teg.Domain.Tests.Support.GameFixtures;
using static Teg.Domain.Tests.Support.RegroupFixtures;
using static Teg.Domain.Tests.Support.ReinforcementFixtures;

// The namespace is NOT named LegalMoves: a namespace of that name would hide the class under test in this whole assembly.
namespace Teg.Domain.Tests.LegalMovesRules;

/// <summary>
/// A4 / B1: <c>LegalMoves.For(state, map)</c> describes what the ACTIVE participant may do, phase by phase, with descriptors
/// (pools, trades, attacks, regroups, the occupation range, the flags) instead of every combination. Each test pins one phase
/// or edge to hand-derived values; the SOUNDNESS and COMPLETENESS against the engine (every state below is also run through
/// <see cref="LegalMovesOracle.AssertAgrees"/>) is what makes the descriptors trustworthy. The sweeps over whole random games
/// are in <c>LegalMovesAgreementTests</c>.
/// </summary>
public sealed class LegalMovesTests
{
    private static LegalMoveSet Moves(GameState state, MapDefinition? map = null) => LegalMoves.For(state, map ?? RealMap);

    private static void AssertAgrees(GameState state, string context, MapDefinition? map = null) =>
        LegalMovesOracle.AssertAgrees(state, map ?? RealMap, context);

    private static string[] Ids(PlacementPool pool) => pool.TerritoryIds.ToArray();

    private static (string From, string To)[] Pairs(LegalMoveSet moves) =>
        moves.Attacks.Select(option => (option.From, option.To)).ToArray();

    /// <summary>Nothing but its flags and lists is offered: no placement, trade, attack, occupation or regroup.</summary>
    private static void AssertNothingButTheFlags(LegalMoveSet moves)
    {
        Assert.False(moves.CanPlace);
        Assert.False(moves.MustPlaceAll);
        Assert.False(moves.TradeMandatory);
        Assert.Empty(moves.Pools);
        Assert.Empty(moves.Trades);
        Assert.Empty(moves.Attacks);
        Assert.Null(moves.Occupation);
        Assert.Empty(moves.Regroups);
    }

    // ---- initial placement (R§3) ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(2, 0, 6)]
    [InlineData(2, 1, 6)]
    [InlineData(3, 0, 5)]
    [InlineData(3, 1, 3)]
    [InlineData(4, 0, 5)]
    [InlineData(4, 1, 3)]
    public void R3_InitialPlacement_OffersTheExactPoolOfTheRound_OnTheCountriesOfTheActiveParticipant(
        int participants,
        int roundsAlreadyPlayed,
        int pool)
    {
        var state = Deal(participants, seed: 3);
        for (var round = 0; round < roundsAlreadyPlayed; round++)
        {
            state = FinishPlacementRound(state);
        }

        var moves = Moves(state);

        Assert.Equal((state.ActiveSeat, GamePhase.InitialPlacement), (moves.Seat, moves.Phase));
        Assert.True(moves.CanPlace);
        Assert.True(moves.MustPlaceAll); // the whole pool of the round goes in ONE command
        var offered = Assert.Single(moves.Pools);
        Assert.Null(offered.ContinentId);
        Assert.Equal(pool, offered.Armies);
        Assert.Equal(OwnedBy(state, state.ActiveSeat), Ids(offered)); // all of theirs, in map order, and nothing else
        Assert.False(moves.CanEndTurn);
        Assert.False(moves.CanStartRegroup);
        Assert.False(moves.TradeMandatory);
        Assert.Empty(moves.Trades);
        Assert.Empty(moves.Attacks);
        Assert.Null(moves.Occupation);
        Assert.Empty(moves.Regroups);
        Assert.True(moves.HasMoves);
        AssertAgrees(state, $"{participants} participants, {roundsAlreadyPlayed} rounds played");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void R3_InitialPlacement_ExpandsToTheWholePoolOnEachOwnCountry(int participants)
    {
        var state = Deal(participants, seed: 5);
        var pool = GameSetup.PlacementPools(participants)[state.PlacementRound - 1];

        var commands = Moves(state).ToCommands().ToArray();

        var expected = OwnedBy(state, state.ActiveSeat).Select(id => Place(state.ActiveSeat, (id, pool))).ToArray();
        Assert.True(expected.Length >= 12, "A dealt participant owns at least 12 countries.");
        Assert.Equal<GameCommand>(expected, commands);
        Assert.All(commands, command => ApplyOk(state, command)); // and every one of them is accepted
    }

    [Fact]
    public void A4_TheDescriptor_IsForTheActiveParticipantOnly()
    {
        var dealt = Deal(3, seed: 3);
        var first = dealt.ActiveSeat;

        var next = ApplyOk(dealt, WholePoolOnFirstCountry(dealt)).State; // the turn passes to the next seat
        var moves = Moves(next);

        Assert.NotEqual(first, next.ActiveSeat);
        Assert.Equal(next.ActiveSeat, moves.Seat);
        var offered = Ids(Assert.Single(moves.Pools));
        Assert.Equal(OwnedBy(next, next.ActiveSeat), offered);
        Assert.Empty(offered.Intersect(OwnedBy(next, first))); // nothing of the one who just played
        AssertAgrees(next, "second seat of the first round");
    }

    [Fact]
    public void A4_TheDescriptorIsPure_TheSameStateGivesTheSameDescriptor_AndTheStateIsLeftAlone()
    {
        var state = RandomBoardIn(GamePhase.Attack, seed: 4);
        var twin = RandomBoardIn(GamePhase.Attack, seed: 4);

        var first = Moves(state);
        var second = Moves(state);

        Assert.NotEmpty(first.Attacks);
        Assert.Equal(first, second);
        Assert.Equal(twin, state);
    }

    private static GameState RandomBoardIn(GamePhase phase, long seed, int participants = 3) =>
        LegalMovesOracle.RandomBoard(participants, seed, phase);

    // ---- reinforcements (R§5) -----------------------------------------------------------------------------------------

    [Fact]
    public void R5_Reinforcement_OffersTheFreePoolOnEveryOwnCountry()
    {
        var state = ReinforcementOfSeatOne(SpreadCountries(16)); // 16 countries: 8 free armies, no continent

        var moves = Moves(state);

        Assert.Equal((1, GamePhase.Reinforcement), (moves.Seat, moves.Phase));
        var free = Assert.Single(moves.Pools);
        Assert.Null(free.ContinentId);
        Assert.Equal(8, free.Armies);
        Assert.Equal(OwnedBy(state, 1), Ids(free));
        Assert.True(moves.CanPlace);
        Assert.False(moves.MustPlaceAll); // a part of the pool can be placed now and the rest later
        Assert.False(moves.TradeMandatory);
        Assert.Empty(moves.Trades); // no cards
        Assert.False(moves.CanEndTurn); // armies are pending
        Assert.False(moves.CanStartRegroup);
        Assert.Empty(moves.Attacks);
        Assert.Null(moves.Occupation);
        AssertAgrees(state, "free pool only");
    }

    private static readonly string[] SouthAmericaAndOceania =
        [.. ContinentCountries("oceania"), .. ContinentCountries("america-del-sur"), .. SpreadCountries(8)];

    [Fact]
    public void R5_AContinentBonus_IsAPoolThatOnlyOffersTheCountriesOfThatContinent()
    {
        // 4 + 6 + 5 more countries: 15 -> 7 free armies; South America (+3) and Oceania (+2) are complete.
        var state = ReinforcementOfSeatOne(SouthAmericaAndOceania);

        var moves = Moves(state);

        Assert.Equal(3, moves.Pools.Count);
        var free = moves.Pools[0];
        Assert.Equal((null, 7), (free.ContinentId, free.Armies));
        Assert.Equal(OwnedBy(state, 1), Ids(free)); // the free armies can go anywhere
        var southAmerica = moves.Pools[1];
        Assert.Equal(("america-del-sur", 3), (southAmerica.ContinentId, southAmerica.Armies));
        Assert.Equal(ContinentCountries("america-del-sur"), Ids(southAmerica));
        var oceania = moves.Pools[2];
        Assert.Equal(("oceania", 2), (oceania.ContinentId, oceania.Armies));
        Assert.Equal(ContinentCountries("oceania"), Ids(oceania));
        Assert.True(moves.CanPlace);
        AssertAgrees(state, "two continent bonuses");
    }

    [Fact]
    public void R5_TwoPoolsOfTheSameSize_ExpandToEachPlacementOnlyOnce()
    {
        // Seat 1 owns exactly South America: 3 free armies (6 countries) and the 3 of the continent bonus, on the same 6 countries.
        var state = ReinforcementOfSeatOne(ContinentCountries("america-del-sur"));

        var moves = Moves(state);
        var commands = moves.ToCommands().ToArray();

        Assert.Equal<(string?, int)[]>([(null, 3), ("america-del-sur", 3)], PoolSizes(moves));
        Assert.Equal(6, commands.Length); // "3 armies on that country" once, whichever pool they come from
        Assert.Equal(commands.Length, commands.Distinct().Count());
        AssertAgrees(state, "two pools of three");
    }

    [Fact]
    public void R5_PlacingPartOfThePools_ShrinksThemAndDropsTheSpentOnes()
    {
        var state = ReinforcementOfSeatOne(SouthAmericaAndOceania);

        state = ApplyOk(state, Reinforcing(1, ("brasil", 1))).State; // one army of the South America bonus
        var afterOne = Moves(state);
        Assert.Equal<(string?, int)[]>([(null, 7), ("america-del-sur", 2), ("oceania", 2)], PoolSizes(afterOne));
        AssertAgrees(state, "after one army");

        state = ApplyOk(state, Reinforcing(1, ("australia", 2))).State; // the whole Oceania bonus
        Assert.Equal<(string?, int)[]>([(null, 7), ("america-del-sur", 2)], PoolSizes(Moves(state))); // a spent pool is gone

        state = ApplyOk(state, Reinforcing(1, ("alaska", 7))).State; // the whole free pool
        var onlyTheBonus = Moves(state);
        Assert.Equal<(string?, int)[]>([("america-del-sur", 2)], PoolSizes(onlyTheBonus));
        Assert.True(onlyTheBonus.CanPlace); // continent armies are still to be placed
        Assert.False(onlyTheBonus.CanEndTurn);
        AssertAgrees(state, "only the South America bonus left");
    }

    private static (string?, int)[] PoolSizes(LegalMoveSet moves) =>
        moves.Pools.Select(pool => (pool.ContinentId, pool.Armies)).ToArray();

    [Fact]
    public void R5_WhenTheLastArmyIsPlaced_TheReinforcementPhaseIsOver_AndTheDescriptorIsThatOfAttack()
    {
        var state = ReinforcementOfSeatOne(SpreadCountries(16));

        var attack = ApplyOk(state, WholePoolCommand(state)).State;
        var moves = Moves(attack);

        Assert.Equal(GamePhase.Attack, moves.Phase);
        Assert.Empty(moves.Pools);
        Assert.False(moves.CanPlace);
        Assert.True(moves.CanEndTurn);
        Assert.True(moves.CanStartRegroup);
    }

    // ---- trades (R§7) ------------------------------------------------------------------------------------------------

    private static GameState Holding(params string[] hand) => WithHand(ReinforcementOfSeatOne(SpreadCountries(16)), 1, hand);

    [Theory]
    [InlineData(CardSymbol.Galeon)]
    [InlineData(CardSymbol.Globo)]
    [InlineData(CardSymbol.Canon)]
    public void R7_ThreeCardsOfOneSymbol_AreOneTrade(CardSymbol symbol)
    {
        var hand = CardsOf(symbol, 3);
        var state = Holding(hand);

        var moves = Moves(state);

        var trade = Assert.Single(moves.Trades);
        Assert.Equal(hand, trade.Cards.ToArray());
        Assert.Equal(4, trade.Armies); // the first trade is worth 4
        Assert.False(moves.TradeMandatory);
        Assert.True(moves.CanPlace); // a trade is allowed, not forced
        AssertAgrees(state, $"three {symbol}");
    }

    [Fact]
    public void R7_OneCardOfEachSymbol_IsOneTrade()
    {
        var hand = OneOfEach();
        var state = Holding(hand);

        var trade = Assert.Single(Moves(state).Trades);

        Assert.Equal(hand, trade.Cards.ToArray());
        AssertAgrees(state, "one of each");
    }

    [Theory]
    [InlineData(CardSymbol.Galeon, CardSymbol.Globo)]
    [InlineData(CardSymbol.Canon, CardSymbol.Galeon)]
    public void R7_TwoOfASymbolAndOneOfAnother_AreNoTrade(CardSymbol pair, CardSymbol other)
    {
        var state = Holding([.. CardsOf(pair, 2), .. CardsOf(other, 1)]);

        Assert.Empty(Moves(state).Trades);
        AssertAgrees(state, "2 + 1");
    }

    [Fact]
    public void R7_AHandWithSeveralValidTriples_OffersEachOneOnce_InHandOrder()
    {
        var hand = CardsOf(CardSymbol.Galeon, 4);
        var state = Holding(hand);

        var trades = Moves(state).Trades.Select(trade => trade.Cards.ToArray()).ToArray();

        Assert.Equal(
            new[]
            {
                new[] { hand[0], hand[1], hand[2] },
                new[] { hand[0], hand[1], hand[3] },
                new[] { hand[0], hand[2], hand[3] },
                new[] { hand[1], hand[2], hand[3] },
            },
            trades);
        AssertAgrees(state, "four of one symbol");
    }

    [Fact]
    public void R7_OnlyTheValidTriplesOfAMixedHandAreOffered()
    {
        var galeones = CardsOf(CardSymbol.Galeon, 3);
        var globo = CardsOf(CardSymbol.Globo, 1);
        var state = Holding([.. galeones, .. globo]);

        var trade = Assert.Single(Moves(state).Trades); // the three galeones; any triple with the globo is two + one

        Assert.Equal(galeones, trade.Cards.ToArray());
        AssertAgrees(state, "three galeones and a globo");
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(1, 7)]
    [InlineData(2, 10)]
    [InlineData(3, 15)]
    [InlineData(4, 20)]
    public void R7_EachTradeSaysItsValue_FromTheTradesTheParticipantHasAlreadyMade(int tradesDone, int armies)
    {
        var state = WithTrades(Holding(CardsOf(CardSymbol.Globo, 3)), 1, tradesDone);

        var trade = Assert.Single(Moves(state).Trades);

        Assert.Equal(armies, trade.Armies);
    }

    [Fact]
    public void R7_WithFiveCards_ATradeIsMandatory_AndPlacingIsNotAllowedUntilTheHandIsSmaller()
    {
        // 2 galeones, 2 globos, 1 canon: no three of a kind, four one-of-each triples.
        string[] hand = [.. CardsOf(CardSymbol.Galeon, 2), .. CardsOf(CardSymbol.Globo, 2), .. CardsOf(CardSymbol.Canon, 1)];
        var state = Holding(hand);

        var moves = Moves(state);

        Assert.True(moves.TradeMandatory);
        Assert.False(moves.CanPlace); // TRADE_REQUIRED
        Assert.Equal(4, moves.Trades.Count);
        Assert.All(moves.Trades, trade => Assert.Equal(3, trade.Cards.Select(id => RealMap.Territory(id).Symbol).Distinct().Count()));
        Assert.Equal(8, Assert.Single(moves.Pools).Armies); // the pool is still described: it is what waits for the trade
        Assert.False(moves.CanEndTurn);
        Assert.True(moves.HasMoves);
        Assert.Empty(moves.ToCommands().OfType<PlaceReinforcements>());
        AssertAgrees(state, "five cards");

        var traded = ApplyOk(state, new TradeCards(1, moves.Trades[0].Cards)).State; // two cards left, 4 armies more

        var after = Moves(traded);
        Assert.False(after.TradeMandatory);
        Assert.True(after.CanPlace);
        Assert.Empty(after.Trades);
        Assert.Equal(12, Assert.Single(after.Pools).Armies);
        AssertAgrees(traded, "after the trade");
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(7, true)]
    public void R7_TheTradeBecomesMandatoryAtFiveCards(int cards, bool mandatory)
    {
        var state = Holding(CardsOf(CardSymbol.Canon, cards));

        var moves = Moves(state);

        Assert.Equal(mandatory, moves.TradeMandatory);
        Assert.Equal(!mandatory, moves.CanPlace);
        Assert.NotEmpty(moves.Trades); // a valid trade always exists then
        AssertAgrees(state, $"{cards} cards");
    }

    [Fact]
    public void A4_TheTrades_AreMadeOfTheActiveParticipantsCardsOnly()
    {
        var mine = CardsOf(CardSymbol.Globo, 3);
        var theirs = CardsOf(CardSymbol.Galeon, 3);
        var state = WithHand(Holding(mine), 0, theirs); // seat 0 holds three galeones of its own

        var trade = Assert.Single(Moves(state).Trades);

        Assert.Equal(mine, trade.Cards.ToArray());
        Assert.Empty(trade.Cards.Intersect(theirs));
    }

    // ---- attack (R§6) ------------------------------------------------------------------------------------------------

    [Fact]
    public void R6_Attack_OffersEveryOwnCountryWithTwoArmiesAgainstEveryAdjacentEnemy_ByLandOrByBridge()
    {
        // Seat 0 owns a 5, b 3, e 1, f 2; seat 1 owns c, d. Land: a-b a-c c-d c-e e-f. Bridges: b-d b-f.
        var state = TinyBoard.State(GamePhase.Attack);

        var moves = Moves(state, TinyBoard.Map);

        Assert.Equal<(string, string)[]>([("a", "c"), ("b", "d")], Pairs(moves)); // b-d is a bridge; e has one army; f only touches own countries
        Assert.True(moves.CanStartRegroup);
        Assert.True(moves.CanEndTurn);
        Assert.False(moves.CanPlace);
        Assert.Empty(moves.Pools);
        Assert.Null(moves.Occupation);
        AssertAgrees(state, "tiny board", TinyBoard.Map);
    }

    [Fact]
    public void R6_Attack_FollowsTheArmiesAndTheOwners()
    {
        // a drops to one army (no attack from it), e grows to five (it now attacks c).
        var state = Armies(TinyBoard.State(GamePhase.Attack), ("a", 1), ("e", 5));

        Assert.Equal<(string, string)[]>([("b", "d"), ("e", "c")], Pairs(Moves(state, TinyBoard.Map)));
        AssertAgrees(state, "a weak, e strong", TinyBoard.Map);

        // c (4 armies) passes to seat 0: nobody attacks it any more, and from c seat 0 now strikes d by land.
        var seatZeroHasC = Owners(state, ("c", 0));
        Assert.Equal<(string, string)[]>([("b", "d"), ("c", "d")], Pairs(Moves(seatZeroHasC, TinyBoard.Map)));
        AssertAgrees(seatZeroHasC, "c is seat 0's", TinyBoard.Map);
    }

    [Fact]
    public void R6_Attack_OfTheOtherParticipant_IsDescribedFromTheirSide()
    {
        var state = TinyBoard.State(GamePhase.Attack, activeSeat: 1); // seat 1 owns c 4 and d 2

        var moves = Moves(state, TinyBoard.Map);

        Assert.Equal(1, moves.Seat);
        Assert.Equal<(string, string)[]>([("c", "a"), ("c", "e"), ("d", "b")], Pairs(moves)); // c-a, c-e by land and d-b by the bridge
        AssertAgrees(state, "seat 1 attacks", TinyBoard.Map);
    }

    [Theory]
    [InlineData(2, 1L)]
    [InlineData(2, 2L)]
    [InlineData(3, 3L)]
    [InlineData(3, 4L)]
    [InlineData(4, 5L)]
    [InlineData(4, 6L)]
    public void R6_Attack_OnTheRealBoard_IsEveryBorderWithAnOwnOriginOfTwoArmiesAndAnEnemyDestination(int participants, long seed)
    {
        var state = LegalMovesOracle.RandomBoard(participants, seed, GamePhase.Attack);

        var moves = Moves(state);

        // The model walks the declared borders, both ways; the engine's neighbour sets are never used.
        var position = RealMap.Territories.Select((t, index) => (t.Id, index)).ToDictionary(entry => entry.Id, entry => entry.index);
        var expected = RealMap.Borders
            .SelectMany(border => new[] { (From: border.A, To: border.B), (From: border.B, To: border.A) })
            .Where(pair => state.FindTerritory(pair.From)!.Owner == state.ActiveSeat
                && state.FindTerritory(pair.From)!.Armies >= 2
                && state.FindTerritory(pair.To)!.Owner != state.ActiveSeat)
            .OrderBy(pair => position[pair.From])
            .ThenBy(pair => position[pair.To])
            .ToArray();
        Assert.True(expected.Length > 5, $"Only {expected.Length} attacks: the case would be vacuous.");
        Assert.Equal(expected, Pairs(moves));
        AssertAgrees(state, $"{participants} participants, seed {seed}");
    }

    [Fact]
    public void R6_TheFirstRoundOfPlay_HasTheSameAttackDescriptor()
    {
        var state = FinishPlacementRound(FinishPlacementRound(Deal(3, seed: 2)));

        var moves = Moves(state);

        Assert.True(state.IsHostilitiesRound);
        Assert.Equal(GamePhase.Attack, moves.Phase);
        Assert.True(moves.CanEndTurn); // no Refuerzo in the hostilities round: the turn opens in Ataque
        Assert.True(moves.CanStartRegroup);
        Assert.False(moves.CanPlace);
        AssertAgrees(state, "hostilities round");
    }

    // ---- conquest pending (R§6) ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(2, 1, 1)] // 2 + 1 armies, the conquering one moved in: 1 + 1 left on the board, at most 1 can stay in the country
    [InlineData(3, 1, 2)]
    [InlineData(3, 2, 2)]
    [InlineData(4, 1, 3)]
    [InlineData(5, 1, 3)]
    [InlineData(20, 3, 3)]
    public void R6_AConquestPending_OffersOnlyTheOccupation_From1ToMin3AndTheArmiesTheOriginCanSpare(
        int attackerArmies,
        int defenderArmies,
        int most)
    {
        var state = Conquer(ThreeSeats(attackerArmies, defenderArmies), "a", "c"); // seat 1 keeps nothing, seat 2 keeps d: no victory

        var moves = Moves(state, TinyBoard.Map);

        Assert.NotNull(state.Turn.PendingConquest);
        Assert.Equal(new OccupationRange(1, most), moves.Occupation);
        Assert.False(moves.CanEndTurn);
        Assert.False(moves.CanStartRegroup);
        Assert.Empty(moves.Attacks);
        Assert.False(moves.CanPlace);
        Assert.Empty(moves.Regroups);
        Assert.Equal(Enumerable.Range(1, most).Select(armies => (GameCommand)new MoveAfterConquest(0, armies)), moves.ToCommands());
        AssertAgrees(state, $"{attackerArmies} v {defenderArmies}", TinyBoard.Map);
    }

    [Fact]
    public void R6_AfterTheOccupation_TheConqueredCountryAttacksOn()
    {
        var pending = Conquer(ThreeSeats(5, 1), "a", "c"); // a 4, c 1; seat 2 owns d (2 armies), seat 0 owns a b c e f
        var occupied = ApplyOk(pending, new MoveAfterConquest(0, 3), TinyBoard.Map).State; // 5 armies between them: a 2, c 3

        var moves = Moves(occupied, TinyBoard.Map);

        Assert.Null(moves.Occupation);
        Assert.Contains(("c", "d"), Pairs(moves)); // the conquered country, now with 3 armies, strikes at d
        Assert.True(moves.CanEndTurn);
        Assert.True(moves.CanStartRegroup);
        AssertAgrees(occupied, "after the occupation", TinyBoard.Map);
    }

    // ---- reagrupe (R§6) ----------------------------------------------------------------------------------------------

    [Fact]
    public void R6_Regroup_OffersEveryAdjacentOwnPair_UpToTheQuotaOfTheOrigin()
    {
        // a 5 b 3 e 1 f 2 -> quotas a 4, b 2, e 0, f 1. Land a-b e-f, bridge b-f. c and d are seat 1's.
        var state = TinyInRegroup();

        var moves = Moves(state, TinyBoard.Map);

        // In map order: the origin first (a b c d e f), then the destination (e has one army, so nothing leaves it).
        Assert.Equal(
            new[] { new RegroupOption("a", "b", 4), new RegroupOption("b", "a", 2), new RegroupOption("b", "f", 2), new RegroupOption("f", "b", 1), new RegroupOption("f", "e", 1) },
            moves.Regroups.ToArray());
        Assert.Equal(GamePhase.Regroup, moves.Phase);
        Assert.True(moves.CanEndTurn);
        Assert.False(moves.CanStartRegroup); // already in Reagrupe: it cannot start again
        Assert.Empty(moves.Attacks); // no attack after StartRegroup
        Assert.False(moves.CanPlace);
        AssertAgrees(state, "tiny board regroup", TinyBoard.Map);
    }

    [Fact]
    public void R6_Regroup_TheQuotaOfTheOrigin_IsWhatLimitsIt_NotTheArmiesItHasNow()
    {
        var state = TinyInRegroup();

        state = ApplyOk(state, Move("a", "b", 3), TinyBoard.Map).State; // a 2 (quota 1), b 6 (quota still 2)
        var moves = Moves(state, TinyBoard.Map);

        Assert.Equal(1, MaxOf(moves, "a", "b"));
        Assert.Equal(2, MaxOf(moves, "b", "a")); // b holds 6, but 3 of them arrived in this phase: its quota is 2
        Assert.Equal(2, MaxOf(moves, "b", "f"));
        Assert.Equal(1, MaxOf(moves, "f", "b"));
        AssertAgrees(state, "after a regroup", TinyBoard.Map);
    }

    private static int MaxOf(LegalMoveSet moves, string from, string to) =>
        moves.Regroups.Single(option => option.From == from && option.To == to).MaxArmies;

    [Fact]
    public void R6_Regroup_ACountryThatSpentItsQuota_OffersNothing()
    {
        var state = ApplyOk(TinyInRegroup(), Move("a", "b", 4), TinyBoard.Map).State; // a 1, b 7

        var moves = Moves(state, TinyBoard.Map);

        Assert.DoesNotContain(moves.Regroups, option => option.From == "a"); // quota 0 and one army
        Assert.Equal(2, MaxOf(moves, "b", "a"));
        AssertAgrees(state, "a spent", TinyBoard.Map);
    }

    [Fact]
    public void R6_Regroup_ArmiesThatArrivedCannotMoveOn_TheChainOfTheRulesExampleIsNotOffered()
    {
        // Seat 0: francia 5, espana 1, sahara 1 (land francia-espana, bridge espana-sahara). The rest holds one army each.
        var state = ChainInRegroup(5, 1, 1);

        Assert.Contains(new RegroupOption(X, Y, 4), Moves(state).Regroups);

        state = ApplyOk(state, Move(X, Y, 4)).State; // espana now holds 5 armies, all of them arrived in this phase
        var moves = Moves(state);

        Assert.DoesNotContain(moves.Regroups, option => option.From == Y); // no chaining: nothing moves on from espana
        Assert.Empty(moves.Regroups); // and nobody else has an army to spare
        Assert.True(moves.CanEndTurn);
        AssertAgrees(state, "chain after the first move");
    }

    [Fact]
    public void R6_Regroup_WithNothingToSpare_OnlyEndingTheTurnIsLeft()
    {
        var state = TinyInRegroup(("a", 1), ("b", 1), ("f", 1)); // every own country has one army

        var moves = Moves(state, TinyBoard.Map);

        AssertNothingButTheFlags(moves);
        Assert.True(moves.CanEndTurn);
        Assert.True(moves.HasMoves);
        AssertAgrees(state, "nothing to spare", TinyBoard.Map);
    }

    [Theory]
    [InlineData(2, 11L)]
    [InlineData(3, 12L)]
    [InlineData(4, 13L)]
    public void R6_Regroup_OnTheRealBoard_IsEveryOwnBorderWithAnOriginOfTwoArmies_UpToItsArmiesMinusOne(int participants, long seed)
    {
        var board = LegalMovesOracle.RandomBoard(participants, seed, GamePhase.Attack);
        var state = ApplyOk(board, new StartRegroup(board.ActiveSeat)).State;

        var moves = Moves(state);

        var position = RealMap.Territories.Select((t, index) => (t.Id, index)).ToDictionary(entry => entry.Id, entry => entry.index);
        var expected = RealMap.Borders
            .SelectMany(border => new[] { (From: border.A, To: border.B), (From: border.B, To: border.A) })
            .Where(pair => state.FindTerritory(pair.From)!.Owner == state.ActiveSeat
                && state.FindTerritory(pair.To)!.Owner == state.ActiveSeat
                && state.FindTerritory(pair.From)!.Armies >= 2)
            .OrderBy(pair => position[pair.From])
            .ThenBy(pair => position[pair.To])
            .Select(pair => new RegroupOption(pair.From, pair.To, state.FindTerritory(pair.From)!.Armies - 1))
            .ToArray();
        Assert.True(expected.Length > 5, $"Only {expected.Length} regroups: the case would be vacuous.");
        Assert.Equal(expected, moves.Regroups.ToArray());
        AssertAgrees(state, $"{participants} participants, seed {seed}");
    }

    // ---- the end of the game and impossible states ---------------------------------------------------------------------

    [Fact]
    public void R8_AFinishedGame_OffersNothingAtAll()
    {
        var state = Conquer(LastStand(5, 1), "a", "c"); // seat 0 takes the last country of seat 1: the game is over

        var moves = Moves(state, TinyBoard.Map);

        Assert.Equal(GamePhase.Finished, state.Phase);
        Assert.Equal(GamePhase.Finished, moves.Phase);
        AssertNothingButTheFlags(moves);
        Assert.False(moves.CanEndTurn);
        Assert.False(moves.CanStartRegroup);
        Assert.False(moves.HasMoves);
        Assert.Empty(moves.ToCommands());
        AssertAgrees(state, "finished", TinyBoard.Map);
    }

    [Fact]
    public void R8_AFinishedGameOfTheRealBoard_OffersNothingAtAll()
    {
        // Two participants: seat 1 owns only the prey, seat 0 everything else. Taking it eliminates the last opponent.
        var lastStand = VictoryFixtures.Table(
            2,
            actor: 0,
            rest: [0],
            held: [VictoryFixtures.Holds(0, VictoryFixtures.Origin, armies: 6), VictoryFixtures.Holds(1, VictoryFixtures.Prey)]);
        var finished = VictoryFixtures.Capture(lastStand, VictoryFixtures.Origin, VictoryFixtures.Prey).State;

        Assert.Equal(GamePhase.Finished, finished.Phase);
        Assert.False(Moves(finished).HasMoves);
        AssertAgrees(finished, "finished on the real board");
    }

    [Fact]
    public void R6_AnEliminatedParticipant_IsNeverATarget_AndTheDescriptorGoesOn()
    {
        var pending = Conquer(ThreeSeats(5, 1), "a", "c"); // seat 1 loses its only country
        var state = ApplyOk(pending, new MoveAfterConquest(0, 3), TinyBoard.Map).State;

        var moves = Moves(state, TinyBoard.Map);

        Assert.True(state.Participants[1].Eliminated);
        Assert.NotEmpty(moves.Attacks);
        Assert.All(moves.Attacks, option => Assert.Equal(2, state.FindTerritory(option.To)!.Owner)); // seat 2 is the only enemy left
        AssertAgrees(state, "after an elimination", TinyBoard.Map);
    }

    [Fact]
    public void R4_TheCardPhase_HasNoPlayerDecision_SoNothingIsOffered()
    {
        var state = TinyBoard.State(GamePhase.Card);

        var moves = Moves(state, TinyBoard.Map);

        Assert.Equal(GamePhase.Card, moves.Phase);
        AssertNothingButTheFlags(moves);
        Assert.False(moves.CanEndTurn);
        Assert.False(moves.HasMoves);
        AssertAgrees(state, "card phase", TinyBoard.Map);
    }

    [Fact]
    public void R6_APendingConquestOutsideAtaque_LeavesNothingToDo()
    {
        var state = TinyBoard.State(GamePhase.Regroup);
        state = state with { Turn = state.Turn with { PendingConquest = new PendingConquest("a", "c") } };

        var moves = Moves(state, TinyBoard.Map);

        Assert.False(moves.HasMoves);
        Assert.Null(moves.Occupation); // the occupation is only accepted in Ataque
        AssertAgrees(state, "pending conquest in Regroup", TinyBoard.Map);
    }

    [Fact]
    public void R5_AnEmptyPool_LeavesNothingToPlace_AndTheTurnCannotEnd()
    {
        var state = InPlay(2, GamePhase.Reinforcement, round: 2, activeSeat: 0) with { Turn = Pool(0) };

        var moves = Moves(state);

        AssertNothingButTheFlags(moves);
        Assert.False(moves.CanEndTurn); // Refuerzo never ends by EndTurn
        Assert.False(moves.HasMoves);
        AssertAgrees(state, "empty pool");
    }

    [Fact]
    public void R5_AContinentPoolWithNoCountryOfThatContinent_CannotBePlaced()
    {
        // Seat 0 owns no country of Oceania (the countries go round-robin), yet its pool names a bonus there.
        var board = InPlay(2, GamePhase.Reinforcement, round: 2, activeSeat: 0) with { Turn = Pool(0, ("oceania", 2)) };
        var state = board with
        {
            Territories = board.Territories.Select(t => ContinentCountries("oceania").Contains(t.Id) ? t with { Owner = 1 } : t).ToValueArray(),
        };
        Assert.Empty(OwnedBy(state, 0).Intersect(ContinentCountries("oceania")));

        var moves = Moves(state);

        var pool = Assert.Single(moves.Pools);
        Assert.Equal(("oceania", 2), (pool.ContinentId, pool.Armies));
        Assert.Empty(pool.TerritoryIds);
        Assert.False(moves.CanPlace);
        AssertAgrees(state, "bonus without a country");
    }

    // ---- building a placement from a pool ------------------------------------------------------------------------------

    [Fact]
    public void A4_Place_BuildsTheInitialPlacementCommandOfTheActiveParticipant()
    {
        var state = Deal(2, seed: 7);
        var moves = Moves(state);
        var pool = moves.Pools.Single();
        var first = pool.TerritoryIds[0];
        var last = pool.TerritoryIds[^1];

        var command = moves.Place([new ArmyPlacement(first, 4), new ArmyPlacement(last, pool.Armies - 4)]);

        var placement = Assert.IsType<PlaceInitialArmies>(command);
        Assert.Equal(state.ActiveSeat, placement.Seat);
        Assert.Equal(
            new[] { new ArmyPlacement(first, 4), new ArmyPlacement(last, pool.Armies - 4) },
            placement.Placements.ToArray());
        ApplyOk(state, command);
    }

    [Fact]
    public void A4_Place_BuildsTheReinforcementCommand_ForTheBonusPoolsToo()
    {
        var state = ReinforcementOfSeatOne(SouthAmericaAndOceania);
        var moves = Moves(state);
        var southAmerica = moves.Pools.Single(pool => pool.ContinentId == "america-del-sur");

        var command = moves.Place([new ArmyPlacement(southAmerica.TerritoryIds[0], southAmerica.Armies)]);

        var placement = Assert.IsType<PlaceReinforcements>(command);
        Assert.Equal(1, placement.Seat);
        var placed = ApplyOk(state, command).State;
        Assert.DoesNotContain(Moves(placed).Pools, pool => pool.ContinentId == "america-del-sur"); // the bonus is spent
    }

    [Theory]
    [InlineData(GamePhase.Attack)]
    [InlineData(GamePhase.Regroup)]
    [InlineData(GamePhase.Finished)]
    public void A4_Place_IsRefusedWhereNothingIsPlaced(GamePhase phase)
    {
        var moves = Moves(RandomBoardIn(phase, seed: 1));

        Assert.Throws<InvalidOperationException>(() => moves.Place([new ArmyPlacement("alaska", 1)]));
    }
}
