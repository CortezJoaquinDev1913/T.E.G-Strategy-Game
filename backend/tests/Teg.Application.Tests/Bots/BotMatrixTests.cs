using Teg.Application.Bots;
using Teg.Application.Tests.Support;
using Teg.Application.UseCases;
using Teg.Domain.Rules;
using Teg.Domain.State;
using static Teg.Application.Tests.Support.Games;

namespace Teg.Application.Tests.Bots;

/// <summary>
/// The verification matrix at use-case level (bot parts): tables that mix humans and bots. The bot acts only when it is the active
/// participant; when a human is, <c>BotTurn</c> is refused and changes nothing, and the human plays through the same submit path.
/// The humans here are played by the same heuristic (they only need legal commands).
/// </summary>
public class BotMatrixTests
{
    public static IEnumerable<object[]> Tables()
    {
        yield return ["1 human + 1 bot", new[] { H, ParticipantKind.Bot }];
        yield return ["2 humans + 1 bot", new[] { H, H, ParticipantKind.Bot }];
        yield return ["1 human + 3 bots", new[] { H, ParticipantKind.Bot, ParticipantKind.Bot, ParticipantKind.Bot }];
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public async Task B4_TheBotActsOnlyWhenItIsActive_AndIsRefusedOnTheTurnOfAHuman(string table, ParticipantKind[] kinds)
    {
        var repository = new InMemoryGameRepository();
        var submit = new SubmitAction(repository, Map);
        var policy = new HeuristicBotPolicy(Map);
        var bot = new BotTurn(repository, submit, policy, Map);
        var id = await repository.AddAsync(Deal(5, kinds));

        int humanActions = 0, botActions = 0, refusals = 0;
        var seatsThatActed = new HashSet<int>();

        for (var action = 0; action < 1_500 && repository.State(id).Phase != GamePhase.Finished; action++)
        {
            var state = repository.State(id);
            var active = state.Participants[state.ActiveSeat];

            // Ask the bot every time: it works on its own turns and is refused on the others, whatever the table.
            var asked = await bot.ExecuteAsync(new BotTurnRequest(id));
            if (active.Kind == ParticipantKind.Bot)
            {
                Assert.True(asked.IsSuccess, asked.IsSuccess ? null : $"{table}: {asked.Error.Code}");
                botActions++;
            }
            else
            {
                Assert.False(asked.IsSuccess, table);
                Assert.Equal(ErrorKind.NotBotTurn, asked.Error.Kind);
                Assert.Equal(state, repository.State(id)); // the refusal changed nothing
                refusals++;

                var command = policy.Next(state, LegalMoves.For(state, Map));
                var played = await submit.ExecuteAsync(new SubmitActionRequest(id, state.Version, Guid.NewGuid(), command));
                Assert.True(played.IsSuccess, played.IsSuccess ? null : $"{table}: {played.Error.Code}");
                humanActions++;
            }

            seatsThatActed.Add(active.Seat);
        }

        Assert.True(humanActions > 0 && botActions > 0, $"{table}: human {humanActions}, bot {botActions}");
        Assert.Equal(humanActions, refusals); // every human action was preceded by one refused bot call
        Assert.Equal(kinds.Length, seatsThatActed.Count); // the whole table took part
        var final = repository.State(id);
        Assert.True(final.Phase == GamePhase.Finished || final.Round >= 3, $"{table}: round {final.Round}"); // the rotation went round more than twice
    }

    [Fact]
    public async Task B4_ABotAlone_NeverMovesWhileTheTurnBelongsToAHuman()
    {
        var repository = new InMemoryGameRepository();
        var bot = new BotTurn(repository, new SubmitAction(repository, Map), new HeuristicBotPolicy(Map), Map);
        var dealt = Deal(8, H, ParticipantKind.Bot);
        var humanFirst = WithKinds(dealt, Enumerable.Range(0, 2).Select(seat => seat == dealt.ActiveSeat ? H : ParticipantKind.Bot).ToArray());
        var id = await repository.AddAsync(humanFirst);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var refused = await bot.ExecuteAsync(new BotTurnRequest(id));

            Assert.Equal(ErrorKind.NotBotTurn, refused.Error.Kind);
        }

        Assert.Equal(humanFirst, repository.State(id));
        Assert.Empty(repository.SaveCalls);
    }
}
