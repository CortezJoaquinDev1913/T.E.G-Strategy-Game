using System.Text.Json;
using Teg.Application.Ports;
using Teg.Application.Tests.Support;
using Teg.Application.UseCases;
using Teg.Application.Views;
using Teg.Domain;
using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Events;
using Teg.Domain.Rules;
using Teg.Domain.State;
using static Teg.Application.Tests.Support.Games;

namespace Teg.Application.Tests.UseCases;

/// <summary>A4 and A5: loading a game, the legal-moves query, the list of games and the history. Read models are projections of the saved state.</summary>
public class QueryTests
{
    private readonly InMemoryGameRepository _repository = new();

    private async Task<Guid> Saved(GameState state) => await _repository.AddAsync(state);

    // ---- GetLegalMoves (A4) -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A4_LegalMoves_AreThoseOfTheActiveParticipant_InInitialPlacement()
    {
        var state = Deal(3, H, H);
        var id = await Saved(state);

        var result = await new GetLegalMoves(_repository, Map).ExecuteAsync(id);

        Assert.True(result.IsSuccess);
        var moves = result.Value;
        Assert.Equal(state.ActiveSeat, moves.Seat);
        Assert.Equal(GamePhase.InitialPlacement, moves.Phase);
        Assert.True(moves.MustPlaceAll);
        var pool = Assert.Single(moves.Pools);
        Assert.Equal(6, pool.Armies);
        Assert.Equal(state.Territories.Where(t => t.Owner == state.ActiveSeat).Select(t => t.Id), pool.TerritoryIds);
        Assert.Equal(LegalMoves.For(state, Map), moves);
    }

    [Fact]
    public async Task A4_LegalMoves_FollowTheSavedState_InAttack()
    {
        var state = FinishInitialPlacement(Deal(3, H, H));
        var id = await Saved(state);

        var moves = (await new GetLegalMoves(_repository, Map).ExecuteAsync(id)).Value;

        Assert.Equal(state.ActiveSeat, moves.Seat);
        Assert.Equal(GamePhase.Attack, moves.Phase);
        Assert.True(moves.CanEndTurn);
        Assert.True(moves.CanStartRegroup);
        Assert.NotEmpty(moves.Attacks);
        Assert.All(moves.Attacks, attack => Assert.Equal(state.ActiveSeat, state.FindTerritory(attack.From)!.Owner));
        Assert.Equal(LegalMoves.For(state, Map), moves);
    }

    [Fact]
    public async Task A4_LegalMovesOfAnUnknownGame_AreNotFound()
    {
        var result = await new GetLegalMoves(_repository, Map).ExecuteAsync(Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
    }

    // ---- GetGame (A4, A5) -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A4_TheGameView_CarriesThePublicState()
    {
        var state = Deal(11, H, ParticipantKind.Bot, H);
        var id = await Saved(state);

        var result = await new GetGame(_repository).ExecuteAsync(id);

        Assert.True(result.IsSuccess);
        var view = result.Value;
        Assert.Equal(id, view.Id);
        Assert.Equal(0, view.Version);
        Assert.Equal(GameStatus.Active, view.Status);
        Assert.Equal(GamePhase.InitialPlacement, view.Phase);
        Assert.Equal(1, view.PlacementRound);
        Assert.Equal(0, view.Round);
        Assert.Equal(state.FirstSeat, view.FirstSeat);
        Assert.Equal(state.ActiveSeat, view.ActiveSeat);
        Assert.Equal(state.Ranking, view.Ranking);
        Assert.Equal(state.Territories.Select(t => (t.Id, t.Owner, t.Armies)), view.Territories.Select(t => (t.Id, t.Owner, t.Armies)));
        Assert.Equal(["Jugador 1", "Jugador 2", "Jugador 3"], view.Participants.Select(p => p.Name));
        Assert.Equal([H, ParticipantKind.Bot, H], view.Participants.Select(p => p.Kind));
        Assert.Equal(state.Participants.Select(p => p.Color), view.Participants.Select(p => p.Color));
        Assert.Equal([17, 17, 16], view.Participants.Select(p => p.TerritoryCount).OrderDescending());
        Assert.All(view.Participants, p => Assert.Equal((false, 0, 0), (p.Eliminated, p.CardCount, p.TradesDone)));
        Assert.Equal(50, view.DrawPileCount);
        Assert.Equal(0, view.DiscardPileCount);
        Assert.Null(view.Winner);
        Assert.Null(view.WinReason);
        Assert.Equal(0, view.Turn.BotActions);
    }

    [Fact]
    public async Task A4_TheTurnOfTheView_ShowsThePendingPoolAndConquest()
    {
        var playing = FinishInitialPlacement(Deal(3, H, H));
        var pending = playing with
        {
            Phase = GamePhase.Reinforcement,
            Turn = new TurnContext(4, new[] { new ContinentPool("europa", 5) }.ToValueArray())
            {
                ConquestsThisTurn = 2,
                PendingConquest = new PendingConquest("francia", "espana"),
                BotActions = 6,
            },
        };

        var view = (await new GetGame(_repository).ExecuteAsync(await Saved(pending))).Value;

        Assert.Equal(4, view.Turn.FreeArmies);
        var pool = Assert.Single(view.Turn.ContinentPools);
        Assert.Equal(("europa", 5), (pool.ContinentId, pool.Armies));
        Assert.Equal(2, view.Turn.ConquestsThisTurn);
        Assert.Equal(("francia", "espana"), (view.Turn.PendingConquest!.From, view.Turn.PendingConquest.To));
        Assert.Equal(6, view.Turn.BotActions);
    }

    [Fact]
    public async Task A4_OnlyTheActiveParticipant_SeesItsObjectiveAndItsCards()
    {
        var dealt = Deal(21, H, H, H, H); // four participants: each one holds a secret objective
        var hands = dealt with
        {
            Participants = dealt.Participants
                .Select(p => p with { Cards = new[] { new HeldCard("francia"), new HeldCard("espana", BonusClaimed: true) }.Take(p.Seat % 3).ToValueArray() })
                .ToValueArray(),
        };
        var numbers = new HashSet<int?>();

        for (var seat = 0; seat < 4; seat++)
        {
            var active = hands with { ActiveSeat = seat };
            var view = (await new GetGame(_repository).ExecuteAsync(await Saved(active))).Value;
            var own = active.Participants[seat];

            // What a participant sees is theirs: one objective, their own hand; the rest of the table shows counts only.
            var graph = ObjectGraph.Walk(view);
            var objective = Assert.Single(graph.OfType<ObjectiveView>());
            Assert.Equal(objective, view.ActivePrivate!.Objective);
            Assert.Equal(seat, view.ActivePrivate.Seat);
            Assert.Equal(own.Cards.Select(c => (c.TerritoryId, c.BonusClaimed)), view.ActivePrivate.Cards.Select(c => (c.TerritoryId, c.BonusClaimed)));
            Assert.Equal(own.Cards.Count, graph.OfType<CardView>().Count());
            Assert.Equal(active.Participants.Select(p => p.Cards.Count), view.Participants.Select(p => p.CardCount));
            Assert.DoesNotContain(graph, o => o.GetType().Namespace?.StartsWith("Teg.Domain", StringComparison.Ordinal) == true);
            numbers.Add(objective.Number);
        }

        Assert.Equal(4, numbers.Count); // four views, four different objectives: nobody saw somebody else's
    }

    public static IEnumerable<object[]> ObjectiveCases()
    {
        yield return [Objective.None, ObjectiveKind.None, null!, null!];
        yield return [Objective.Common, ObjectiveKind.Common, null!, null!];
        yield return [new Objective.Occupation(7), ObjectiveKind.Occupation, 7, null!];
        yield return [new Objective.Destruction(13, PlayerColor.Amarillo), ObjectiveKind.Destruction, 13, PlayerColor.Amarillo];
    }

    [Theory]
    [MemberData(nameof(ObjectiveCases))]
    public async Task A4_TheObjectiveOfTheActiveParticipant_IsShownByKind(Objective objective, ObjectiveKind kind, int? number, PlayerColor? target)
    {
        var state = Deal(3, H, H);
        var given = state with
        {
            Participants = state.Participants.Select(p => p.Seat == state.ActiveSeat ? p with { Objective = objective } : p).ToValueArray(),
        };

        var view = (await new GetGame(_repository).ExecuteAsync(await Saved(given))).Value;

        Assert.Equal(new ObjectiveView(kind, number, target), view.ActivePrivate!.Objective);
    }

    [Fact]
    public async Task A4_TheViewHidesWhatMustStayHidden_TheSeedAndTheOrderOfTheDeck()
    {
        var view = (await new GetGame(_repository).ExecuteAsync(await Saved(Deal(21, H, H, H, H)))).Value;

        Assert.Equal(50, view.DrawPileCount); // how many, never which
        Assert.DoesNotContain(ObjectGraph.Walk(view), o => o.GetType().Namespace?.StartsWith("Teg.Domain", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task A4_AFinishedGame_ShowsTheWinner_AndNoPrivateData()
    {
        var state = Deal(3, H, H) with { Phase = GamePhase.Finished, Winner = 1, WinReason = WinReason.Elimination };

        var view = (await new GetGame(_repository).ExecuteAsync(await Saved(state))).Value;

        Assert.Equal(GameStatus.Finished, view.Status);
        Assert.Equal(GamePhase.Finished, view.Phase);
        Assert.Equal(1, view.Winner);
        Assert.Equal(WinReason.Elimination, view.WinReason);
        Assert.Null(view.ActivePrivate);
    }

    [Fact]
    public async Task A4_AnUnknownGame_IsNotFound()
    {
        var result = await new GetGame(_repository).ExecuteAsync(Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
        Assert.Equal(ErrorCodes.NotFound, result.Error.Code);
    }

    [Fact]
    public async Task A5_AReloadedGame_ShowsTheSamePhaseActiveParticipantAndVersion()
    {
        var submit = new SubmitAction(_repository, Map);
        var id = await Saved(Deal(3, H, H));
        GameView last = null!;
        for (var version = 0; version < 5; version++)
        {
            var state = _repository.State(id);
            var step = await submit.ExecuteAsync(new SubmitActionRequest(id, version, Guid.NewGuid(), version < 4 ? WholePool(state) : new StartRegroup(state.ActiveSeat)));
            last = step.Value.Game;
        }

        var reloaded = (await new GetGame(_repository).ExecuteAsync(id)).Value; // "a new process": nothing but the store

        Assert.Equal(GamePhase.Regroup, reloaded.Phase);
        Assert.Equal(5, reloaded.Version);
        Assert.Equal(last.ActiveSeat, reloaded.ActiveSeat);
        Assert.Equal(JsonSerializer.Serialize(last), JsonSerializer.Serialize(reloaded)); // the views carry lists: compare them as the API will send them
    }

    // ---- ListGames --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheList_HoldsEveryGame_TheMostRecentlyChangedFirst_WithWhatIsNeededToResume()
    {
        var first = await Saved(Deal(1, H, ParticipantKind.Bot));
        var second = await Saved(Deal(2, H, H, H));
        var third = await Saved(Deal(3, H, H, H, H));
        var submit = new SubmitAction(_repository, Map);
        var firstState = _repository.State(first);
        await submit.ExecuteAsync(new SubmitActionRequest(first, 0, Guid.NewGuid(), WholePool(firstState))); // the first game is touched last

        var games = await new ListGames(_repository).ExecuteAsync();

        Assert.Equal([first, third, second], games.Select(g => g.Id));
        var summary = games[0];
        Assert.Equal(GameStatus.Active, summary.Status);
        Assert.Equal(1, summary.Version);
        Assert.Equal(GamePhase.InitialPlacement, summary.Phase);
        Assert.Equal(_repository.State(first).ActiveSeat, summary.ActiveSeat);
        Assert.Equal(["Jugador 1", "Jugador 2"], summary.Participants.Select(p => p.Name));
        Assert.Equal([H, ParticipantKind.Bot], summary.Participants.Select(p => p.Kind));
        Assert.Null(summary.Winner);
    }

    [Fact]
    public async Task TheList_CanBeFilteredByStatus()
    {
        var running = await Saved(Deal(1, H, H));
        var over = await Saved(Deal(2, H, H) with { Phase = GamePhase.Finished, Winner = 0, WinReason = WinReason.CommonObjective });
        var list = new ListGames(_repository);

        var active = await list.ExecuteAsync(GameStatus.Active);
        var finished = await list.ExecuteAsync(GameStatus.Finished);
        var all = await list.ExecuteAsync();

        Assert.Equal([running], active.Select(g => g.Id));
        Assert.Equal([over], finished.Select(g => g.Id));
        Assert.Equal(2, all.Count);
        Assert.Equal((0, GameStatus.Finished), (finished[0].Winner, finished[0].Status));
    }

    [Fact]
    public async Task TheList_OfAnEmptyStore_IsEmpty()
    {
        Assert.Empty(await new ListGames(_repository).ExecuteAsync());
    }

    // ---- GetHistory -------------------------------------------------------------------------------------------------------

    private async Task<(Guid Id, int Events)> GameWithHistory()
    {
        var submit = new SubmitAction(_repository, Map);
        var id = await Saved(Deal(3, H, H));
        var events = 0;
        for (var version = 0; version < 3; version++)
        {
            var step = await submit.ExecuteAsync(new SubmitActionRequest(id, version, Guid.NewGuid(), WholePool(_repository.State(id))));
            events += step.Value.Events.Count;
        }

        return (id, events);
    }

    [Fact]
    public async Task TheHistory_ListsTheSavedEventsInOrder_WithTheirSequenceVersionAndActor()
    {
        var (id, count) = await GameWithHistory();

        var history = (await new GetHistory(_repository).ExecuteAsync(id)).Value;

        Assert.Equal(count, history.Count);
        Assert.Equal(Enumerable.Range(1, count), history.Select(e => e.Seq));
        Assert.Equal(_repository.Stored(id).Select(e => e.Event), history.Select(e => e.Event));
        Assert.Equal(new[] { 1, 2, 3 }, history.Select(e => e.Version).Distinct());
        Assert.IsType<InitialArmiesPlaced>(history[0].Event);
        Assert.Equal(((InitialArmiesPlaced)history[0].Event).Seat, history[0].ActorSeat);
    }

    [Fact]
    public async Task TheHistory_ReturnsOnlyWhatCameAfterTheGivenSequence()
    {
        var (id, count) = await GameWithHistory();

        var tail = (await new GetHistory(_repository).ExecuteAsync(id, afterSeq: 3)).Value;
        var none = (await new GetHistory(_repository).ExecuteAsync(id, afterSeq: count)).Value;

        Assert.Equal(Enumerable.Range(4, count - 3), tail.Select(e => e.Seq));
        Assert.Empty(none);
    }

    [Fact]
    public async Task TheHistory_OfAnUnknownGame_IsNotFound()
    {
        var result = await new GetHistory(_repository).ExecuteAsync(Guid.NewGuid());

        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
    }

    [Fact]
    public async Task TheHistory_MasksTheHandSecrets_AndDropsTheObjectiveFallback()
    {
        var state = Deal(3, H, H);
        var id = await Saved(state);
        GameEvent[] raw =
        [
            new CardAwarded(1, "francia"),
            new ParticipantEliminated(0, 1, new[] { "alaska", "sahara" }.ToValueArray()),
            new ObjectiveFellBackToCommon(1),
            new CardsTraded(1, new[] { "a", "b", "c" }.ToValueArray(), 4),
        ];
        await _repository.SaveAsync(id, new GameChange(0, state with { Version = 1 }, raw, Guid.NewGuid(), 1));

        var history = (await new GetHistory(_repository).ExecuteAsync(id)).Value;

        Assert.Equal(3, history.Count); // the fallback is not announced: it would reveal a secret objective
        Assert.Equal(new CardAwarded(1, string.Empty), history[0].Event); // that a card was drawn is public; which one is not
        var eliminated = Assert.IsType<ParticipantEliminated>(history[1].Event);
        Assert.Equal(2, eliminated.InheritedCards.Count); // how many cards changed hands, not which
        Assert.All(eliminated.InheritedCards, card => Assert.Equal(string.Empty, card));
        Assert.Equal(raw[3], history[2].Event); // traded cards are laid on the table: public
        Assert.Equal([1, 2, 4], history.Select(e => e.Seq)); // sequence numbers stay those of the store, gaps included
        Assert.Contains(_repository.Stored(id), e => e.Event is CardAwarded { Card: "francia" }); // the store keeps the full record
    }
}
