using Teg.Application.Tests.Support;
using Teg.Application.UseCases;
using Teg.Application.Views;
using Teg.Domain.Setup;
using Teg.Domain.State;
using static Teg.Application.Tests.Support.Games;

namespace Teg.Application.Tests.UseCases;

/// <summary>A1: create a game (2-4 participants, at least one human, unique colours, the seed stored) and return the id and the state.</summary>
public class CreateGameTests
{
    private static (CreateGame UseCase, InMemoryGameRepository Repository, FixedSeedSource Seeds) Setup(params long[] seeds)
    {
        var repository = new InMemoryGameRepository();
        var source = new FixedSeedSource(seeds.Length == 0 ? [1] : seeds);

        return (new CreateGame(repository, source, Map), repository, source);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task A1_AValidTable_SavesTheDealtGameAtVersionZero_AndReturnsItsIdAndView(int participants)
    {
        var kinds = new[] { H, ParticipantKind.Bot, H, ParticipantKind.Bot }.Take(participants).ToArray();
        var (useCase, repository, _) = Setup(42);

        var result = await useCase.ExecuteAsync(new CreateGameRequest(Table(kinds)));

        Assert.True(result.IsSuccess);
        var view = result.Value;
        var dealt = GameSetup.Create(new GameOptions(Table(kinds)), 42, Map).Value;

        Assert.Equal(1, repository.AddCalls);
        Assert.Equal(dealt, repository.State(view.Id)); // exactly the deal of the domain, saved as it is
        Assert.Equal(0, view.Version);
        Assert.Equal(GameStatus.Active, view.Status);
        Assert.Equal(GamePhase.InitialPlacement, view.Phase);
        Assert.Equal(dealt.ActiveSeat, view.ActiveSeat);
        Assert.Equal(participants, view.Participants.Count);
        Assert.Equal(kinds, view.Participants.Select(p => p.Kind));
        Assert.Equal(50, view.Territories.Count);
        Assert.Equal(50, view.Participants.Sum(p => p.TerritoryCount));
    }

    [Fact]
    public async Task A1_TheSeedComesFromThePort_AndIsStored()
    {
        var (useCase, repository, seeds) = Setup(7, 8);

        var first = (await useCase.ExecuteAsync(new CreateGameRequest(Table(H, H)))).Value;
        var second = (await useCase.ExecuteAsync(new CreateGameRequest(Table(H, H)))).Value;

        Assert.Equal(2, seeds.Calls);
        Assert.Equal(7, repository.State(first.Id).Rng.Seed);
        Assert.Equal(8, repository.State(second.Id).Rng.Seed);
        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(repository.State(first.Id).Territories, repository.State(second.Id).Territories);
    }

    [Fact]
    public async Task A1_ASeedInTheRequest_IsUsedAsItIs_AndThePortIsNotAsked()
    {
        var (useCase, repository, seeds) = Setup(7);

        var created = (await useCase.ExecuteAsync(new CreateGameRequest(Table(H, ParticipantKind.Bot), Seed: -99))).Value;

        Assert.Equal(0, seeds.Calls);
        Assert.Equal(-99, repository.State(created.Id).Rng.Seed);
        Assert.Equal(Deal(-99, H, ParticipantKind.Bot), repository.State(created.Id));
    }

    [Fact]
    public async Task E10_TheSameSeed_GivesTheSameGame()
    {
        var (useCase, repository, _) = Setup(5);

        var first = (await useCase.ExecuteAsync(new CreateGameRequest(Table(H, H, H)))).Value;
        var second = (await useCase.ExecuteAsync(new CreateGameRequest(Table(H, H, H)))).Value;

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(repository.State(first.Id), repository.State(second.Id));
    }

    public static IEnumerable<object[]> InvalidTables()
    {
        var bots = ParticipantKind.Bot;
        var duplicateColour = new[]
        {
            new ParticipantSetup("Ana", H, PlayerColor.Azul),
            new ParticipantSetup("Beto", H, PlayerColor.Azul),
        };

        yield return ["no participants", Array.Empty<ParticipantSetup>(), SetupViolations.InvalidParticipantCount];
        yield return ["1 participant", Table(H), SetupViolations.InvalidParticipantCount];
        yield return ["5 participants", Table(H, H, H, H, H), SetupViolations.InvalidParticipantCount];
        yield return ["2 bots", Table(bots, bots), SetupViolations.NoHumanParticipant];
        yield return ["4 bots", Table(bots, bots, bots, bots), SetupViolations.NoHumanParticipant];
        yield return ["duplicate colours", duplicateColour, SetupViolations.DuplicateColor];
        yield return ["blank name", new[] { new ParticipantSetup("", H, PlayerColor.Azul), new ParticipantSetup("Beto", H, PlayerColor.Rojo) }, SetupViolations.BlankName];
        yield return ["whitespace name", new[] { new ParticipantSetup("Ana", H, PlayerColor.Azul), new ParticipantSetup("   ", H, PlayerColor.Rojo) }, SetupViolations.BlankName];
        yield return ["unknown colour", new[] { new ParticipantSetup("Ana", H, PlayerColor.Azul), new ParticipantSetup("Beto", H, (PlayerColor)99) }, SetupViolations.InvalidColor];
    }

    [Theory]
    [MemberData(nameof(InvalidTables))]
    public async Task A1_AnInvalidTable_IsRejectedWithTheDomainCode_AndNothingIsSaved(string table, ParticipantSetup[] participants, string code)
    {
        var (useCase, repository, _) = Setup();

        var result = await useCase.ExecuteAsync(new CreateGameRequest(participants));

        Assert.False(result.IsSuccess, table);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Equal(ErrorCodes.Validation, result.Error.Type);
        Assert.Equal(code, result.Error.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Error.Message));
        Assert.Equal(0, repository.AddCalls);
        Assert.Equal(0, repository.Count);
    }

    [Fact]
    public async Task A1_ARejectedTable_DoesNotBlockTheNextValidOne()
    {
        var (useCase, repository, _) = Setup(3);

        var rejected = await useCase.ExecuteAsync(new CreateGameRequest(Table(ParticipantKind.Bot, ParticipantKind.Bot)));
        var accepted = await useCase.ExecuteAsync(new CreateGameRequest(Table(H, ParticipantKind.Bot)));

        Assert.False(rejected.IsSuccess);
        Assert.True(accepted.IsSuccess);
        Assert.Equal(1, repository.Count);
    }
}
