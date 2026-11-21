using Teg.Domain.Commands;
using Teg.Domain.State;
using Teg.Domain.Tests.Support;
using static Teg.Domain.Tests.Support.CombatFixtures;
using static Teg.Domain.Tests.Support.GameFixtures;

namespace Teg.Domain.Tests.Turns;

/// <summary>
/// <c>TurnContext.BotActions</c> (design: "bot action count" of the turn): how many actions a bot has taken in the CURRENT turn, so
/// that the cap of the bot survives a reload. It is data the Application layer writes; the contract the engine owes it is
/// (1) it starts at zero, (2) it survives every command that does NOT end the turn (the engine only ever copies the turn context
/// with <c>with</c>), and (3) it starts from zero again for whoever receives the turn (<c>EndTurn</c> builds a fresh context).
/// </summary>
public class BotActionsCounterTests
{
    private const int Counted = 7;

    private static GameState Counting(GameState state) => state with { Turn = state.Turn with { BotActions = Counted } };

    [Fact]
    public void ANewTurnContext_HasTakenNoBotActions()
    {
        Assert.Equal(0, TurnContext.None.BotActions);
        Assert.Equal(0, Pool(5).BotActions);
        Assert.Equal(0, Deal(participants: 3).Turn.BotActions);
    }

    [Fact]
    public void TheCounterIsPartOfTheTurnContextEquality()
    {
        // The persisted turn_context must carry it: two turns that differ only by the counter are different states.
        Assert.NotEqual(TurnContext.None, TurnContext.None with { BotActions = 1 });
        Assert.Equal(TurnContext.None with { BotActions = 4 }, TurnContext.None with { BotActions = 4 });
    }

    [Fact]
    public void APartialPlacement_KeepsTheCounter()
    {
        var state = Counting(InPlay(2, GamePhase.Reinforcement, round: 2, activeSeat: 0) with { Turn = Pool(5) });
        var first = OwnedBy(state, 0)[0];

        var after = ApplyOk(state, Reinforcing(0, (first, 2))).State;

        Assert.Equal(GamePhase.Reinforcement, after.Phase);
        Assert.Equal(3, after.Turn.PendingArmies);
        Assert.Equal(Counted, after.Turn.BotActions);
    }

    [Fact]
    public void TheLastPlacement_ThatOpensTheAttack_KeepsTheCounter()
    {
        var state = Counting(InPlay(2, GamePhase.Reinforcement, round: 2, activeSeat: 0) with { Turn = Pool(3) });

        var after = ApplyOk(state, WholePoolCommand(state)).State;

        Assert.Equal(GamePhase.Attack, after.Phase);
        Assert.Equal(Counted, after.Turn.BotActions);
    }

    [Fact]
    public void StartingTheRegroup_KeepsTheCounter()
    {
        var state = Counting(InPlay(2, GamePhase.Attack, round: 2, activeSeat: 0));

        var after = ApplyOk(state, new StartRegroup(0)).State;

        Assert.Equal(GamePhase.Regroup, after.Phase);
        Assert.Equal(Counted, after.Turn.BotActions);
    }

    [Fact]
    public void AConquestAndItsOccupation_KeepTheCounter()
    {
        var conquered = Conquer(Counting(Arena(attackerArmies: 6, defenderArmies: 1)), "a", "c");
        Assert.NotNull(conquered.Turn.PendingConquest);
        Assert.Equal(Counted, conquered.Turn.BotActions);

        var occupied = ApplyOk(conquered, new MoveAfterConquest(0, 2), TinyBoard.Map).State;

        Assert.Null(occupied.Turn.PendingConquest);
        Assert.Equal(Counted, occupied.Turn.BotActions);
    }

    [Theory]
    [InlineData(GamePhase.Attack)]
    [InlineData(GamePhase.Regroup)]
    public void EndingTheTurn_StartsTheNextTurnFromZero(GamePhase phase)
    {
        var state = Counting(InPlay(2, phase, round: 2, activeSeat: 0));

        var after = ApplyOk(state, new EndTurn(0)).State;

        Assert.Equal(1, after.ActiveSeat);
        Assert.Equal(0, after.Turn.BotActions);
    }

    [Fact]
    public void EndingTheTurn_IntoTheHostilitiesRound_AlsoStartsFromZero()
    {
        // Round 1 (hostilities) turns open in Ataque with an empty context instead of a pool: still no leftover counter.
        var state = Counting(InPlay(3, GamePhase.Attack, round: 1, activeSeat: 0));

        var after = ApplyOk(state, new EndTurn(0)).State;

        Assert.Equal(GamePhase.Attack, after.Phase);
        Assert.Equal(1, after.ActiveSeat);
        Assert.Equal(0, after.Turn.BotActions);
    }
}
