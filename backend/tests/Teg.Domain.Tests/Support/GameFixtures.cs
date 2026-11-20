using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Map;
using Teg.Domain.Rng;
using Teg.Domain.Setup;
using Teg.Domain.State;

namespace Teg.Domain.Tests.Support;

/// <summary>
/// Builders shared by the placement, rotation and validation tests. Hand-built states stand in for the parts of
/// the game that later phases will make reachable through commands (reinforcement, combat, elimination).
/// </summary>
internal static class GameFixtures
{
    /// <summary>The real board (docs/mapa.json), loaded once.</summary>
    public static readonly MapDefinition RealMap = MapDefinition.LoadEmbedded();

    private static readonly PlayerColor[] TableColors =
        [PlayerColor.Azul, PlayerColor.Rojo, PlayerColor.Negro, PlayerColor.Amarillo];

    private static IEnumerable<Participant> Table(int participants, int[] eliminated) =>
        Enumerable.Range(0, participants)
            .Select(seat => new Participant(
                seat,
                $"Jugador {seat + 1}",
                ParticipantKind.Human,
                TableColors[seat],
                Eliminated: eliminated.Contains(seat)));

    /// <summary>A freshly dealt game of humans (initial placement, round 1).</summary>
    public static GameState Deal(int participants, long seed = 1)
    {
        var options = new GameOptions(
            Table(participants, []).Select(p => new ParticipantSetup(p.Name, p.Kind, p.Color)).ToArray());

        var result = GameSetup.Create(options, seed, RealMap);

        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Message);
        return result.Value;
    }

    /// <summary>
    /// A hand-built game in the middle of play on the real board: countries go round-robin to the living
    /// participants (1 army each). Eliminated participants own nothing, as the rules require (R§2).
    /// </summary>
    public static GameState InPlay(
        int participants,
        GamePhase phase,
        int round,
        int activeSeat,
        int firstSeat = 0,
        int[]? eliminated = null)
    {
        var dead = eliminated ?? [];
        var living = Enumerable.Range(0, participants).Except(dead).ToArray();

        return new GameState(
            Version: 0,
            Rng: new RngState(Seed: 1, Draws: 0),
            Phase: phase,
            PlacementRound: 2,
            Round: round,
            FirstSeat: firstSeat,
            ActiveSeat: activeSeat,
            Ranking: Enumerable.Range(0, participants)
                .Select(offset => (firstSeat + offset) % participants)
                .ToValueArray(),
            Participants: Table(participants, dead).ToValueArray(),
            Territories: RealMap.Territories
                .Select((t, index) => new TerritoryState(t.Id, living[index % living.Length], Armies: 1))
                .ToValueArray());
    }

    public static Transition ApplyOk(GameState state, GameCommand command, MapDefinition? map = null)
    {
        var result = GameEngine.Apply(state, command, map ?? RealMap);

        Assert.True(result.IsSuccess, result.IsSuccess ? null : $"{result.Error.Code}: {result.Error.Message}");
        return result.Value;
    }

    /// <summary>
    /// The command is refused with <paramref name="expectedCode"/> and the input state is untouched: it still
    /// equals (structurally) an independent twin built the same way (rule E4).
    /// </summary>
    public static void AssertRejected(
        string expectedCode,
        Func<GameState> build,
        Func<GameState, GameCommand> command,
        MapDefinition? map = null)
    {
        var state = build();
        var untouchedTwin = build();

        var result = GameEngine.Apply(state, command(state), map ?? RealMap);

        Assert.False(result.IsSuccess, "The command should have been rejected.");
        Assert.Equal(expectedCode, result.Error.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Error.Message));
        Assert.Equal(untouchedTwin, state);
    }

    public static PlaceInitialArmies Place(int seat, params (string Territory, int Armies)[] placements) =>
        new(seat, placements.Select(p => new ArmyPlacement(p.Territory, p.Armies)).ToValueArray());

    /// <summary>The active participant puts the entire pool of the current round on their first country.</summary>
    public static PlaceInitialArmies WholePoolOnFirstCountry(GameState state)
    {
        var seat = state.ActiveSeat;
        var pool = GameSetup.PlacementPools(state.Participants.Count)[state.PlacementRound - 1];

        return Place(seat, (OwnedBy(state, seat)[0], pool));
    }

    /// <summary>Every participant, in turn, places the whole pool of the current placement round (one turn each).</summary>
    public static GameState FinishPlacementRound(GameState state)
    {
        for (var turn = 0; turn < state.Participants.Count; turn++)
        {
            state = ApplyOk(state, WholePoolOnFirstCountry(state)).State;
        }

        return state;
    }

    /// <summary>A reinforcement pool: <paramref name="free"/> armies for any own country plus one bonus per continent (R§5).</summary>
    public static TurnContext Pool(int free, params (string Continent, int Armies)[] continents) =>
        new(free, continents.Select(c => new ContinentPool(c.Continent, c.Armies)).ToValueArray());

    public static PlaceReinforcements Reinforcing(int seat, params (string Territory, int Armies)[] placements) =>
        new(seat, placements.Select(p => new ArmyPlacement(p.Territory, p.Armies)).ToValueArray());

    public static TradeCards Trading(int seat, params string[] cards) => new(seat, cards.ToValueArray());

    /// <summary>
    /// The command that places the entire pending pool of the active participant in ONE go: every continent bonus on
    /// the first country of that continent (the participant owns all of them), the free armies on their first own country.
    /// </summary>
    public static PlaceReinforcements WholePoolCommand(GameState state, MapDefinition? map = null)
    {
        var board = map ?? RealMap;
        var placements = state.Turn.ContinentPools
            .Select(pool => (board.Continents.Single(c => c.Id == pool.ContinentId).TerritoryIds[0], pool.Armies))
            .ToList();
        if (state.Turn.FreeArmies > 0)
        {
            placements.Add((OwnedBy(state, state.ActiveSeat)[0], state.Turn.FreeArmies));
        }

        return Reinforcing(state.ActiveSeat, placements.ToArray());
    }

    /// <summary>The active participant places the entire pending pool in ONE command (see <see cref="WholePoolCommand"/>).</summary>
    public static GameState PlaceWholePool(GameState state, MapDefinition? map = null) =>
        ApplyOk(state, WholePoolCommand(state, map), map ?? RealMap).State;

    public static string[] OwnedBy(GameState state, int seat) =>
        state.Territories.Where(t => t.Owner == seat).Select(t => t.Id).ToArray();

    public static int ArmiesOf(GameState state, int seat) =>
        state.Territories.Where(t => t.Owner == seat).Sum(t => t.Armies);

    public static int ArmiesIn(GameState state, string territoryId) =>
        state.Territories.Single(t => t.Id == territoryId).Armies;
}
