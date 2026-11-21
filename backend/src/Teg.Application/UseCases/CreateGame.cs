using Teg.Application.Ports;
using Teg.Application.Views;
using Teg.Domain;
using Teg.Domain.Map;
using Teg.Domain.Setup;

namespace Teg.Application.UseCases;

/// <summary>
/// "Nueva partida" (A1): the participants of the table (name, kind, colour) in table order and, optionally, the seed of the deal.
/// Without a seed one comes from the <see cref="ISeedSource"/>.
/// </summary>
public sealed record CreateGameRequest(IReadOnlyList<ParticipantSetup> Participants, long? Seed = null);

/// <summary>
/// Creates a game (A1): 2 to 4 participants, at least one human, different colours (the domain's <see cref="GameSetup"/> judges it
/// and a refusal is <see cref="ErrorKind.Validation"/> with its code; nothing is saved). The seed is stored with the game (it is
/// part of the state) and comes from the request or from the <see cref="ISeedSource"/>: the use case reads no clock and draws no
/// random number, so with the same seed it deals the same game (E10).
/// </summary>
public sealed class CreateGame(IGameRepository games, ISeedSource seeds, MapDefinition map)
{
    public async Task<Result<GameView, UseCaseError>> ExecuteAsync(CreateGameRequest request, CancellationToken ct = default)
    {
        var seed = request.Seed ?? seeds.NextSeed();
        var created = GameSetup.Create(new GameOptions(request.Participants), seed, map);
        if (!created.IsSuccess)
        {
            return Result<GameView, UseCaseError>.Fail(UseCaseError.FromRule(ErrorKind.Validation, created.Error));
        }

        var id = await games.AddAsync(created.Value, ct);

        return Result<GameView, UseCaseError>.Ok(GameView.From(id, created.Value));
    }
}
