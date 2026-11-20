using Teg.Domain.Collections;
using Teg.Domain.Map;
using Teg.Domain.Rng;
using Teg.Domain.State;

namespace Teg.Domain.Tests.Support;

/// <summary>
/// A six-country board small enough to read at a glance, with land borders AND bridges, used to pin each
/// validation rule on exactly one violation.
/// <list type="bullet">
/// <item>Seat 0 owns a (5 armies), b (3), e (1) and f (2).</item>
/// <item>Seat 1 owns c (4) and d (2).</item>
/// <item>Land borders: a-b, a-c, c-d, c-e, e-f. Bridges: b-d, b-f.</item>
/// </list>
/// </summary>
internal static class TinyBoard
{
    private const string Json = """
        {
          "version": 1,
          "continents": [
            { "id": "c1", "name": "Continente Uno", "bonus": 2, "territories": ["a", "b", "c", "d", "e", "f"] }
          ],
          "territories": [
            { "id": "a", "name": "Alfa", "continent": "c1", "symbol": "galeon" },
            { "id": "b", "name": "Beta", "continent": "c1", "symbol": "globo" },
            { "id": "c", "name": "Gamma", "continent": "c1", "symbol": "canon" },
            { "id": "d", "name": "Delta", "continent": "c1", "symbol": "galeon" },
            { "id": "e", "name": "Epsilon", "continent": "c1", "symbol": "globo" },
            { "id": "f", "name": "Zeta", "continent": "c1", "symbol": "canon" }
          ],
          "borders": [
            { "a": "a", "b": "b", "type": "land" },
            { "a": "a", "b": "c", "type": "land" },
            { "a": "b", "b": "d", "type": "bridge" },
            { "a": "c", "b": "d", "type": "land" },
            { "a": "c", "b": "e", "type": "land" },
            { "a": "b", "b": "f", "type": "bridge" },
            { "a": "e", "b": "f", "type": "land" }
          ]
        }
        """;

    public static readonly MapDefinition Map = MapDefinition.FromJson(Json);

    /// <summary>Two participants (seats 0 and 1), the given phase, seat 0 (by default) acting.</summary>
    public static GameState State(GamePhase phase, int activeSeat = 0) => new(
        Version: 0,
        Rng: new RngState(Seed: 1, Draws: 0),
        Phase: phase,
        PlacementRound: 1,
        Round: phase == GamePhase.InitialPlacement ? 0 : 1,
        FirstSeat: 0,
        ActiveSeat: activeSeat,
        Ranking: new[] { 0, 1 }.ToValueArray(),
        Participants: new[]
        {
            new Participant(0, "Ana", ParticipantKind.Human, PlayerColor.Azul),
            new Participant(1, "Beto", ParticipantKind.Human, PlayerColor.Rojo),
        }.ToValueArray(),
        Territories: new[]
        {
            new TerritoryState("a", Owner: 0, Armies: 5),
            new TerritoryState("b", Owner: 0, Armies: 3),
            new TerritoryState("c", Owner: 1, Armies: 4),
            new TerritoryState("d", Owner: 1, Armies: 2),
            new TerritoryState("e", Owner: 0, Armies: 1),
            new TerritoryState("f", Owner: 0, Armies: 2),
        }.ToValueArray());
}
