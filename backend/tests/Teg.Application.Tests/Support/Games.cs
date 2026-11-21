using Teg.Domain;
using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Map;
using Teg.Domain.Rules;
using Teg.Domain.Setup;
using Teg.Domain.State;

namespace Teg.Application.Tests.Support;

/// <summary>
/// Builders of the Application tests (the Domain test project's support code is not shared on purpose: a test project does not
/// reference another). Games are dealt by the real <c>GameSetup.Create</c>; later states are reached with real commands through the
/// engine, or edited with <c>with</c> where a test needs a situation no short game reaches.
/// </summary>
internal static class Games
{
    /// <summary>Short names for the kinds of a table: <c>Games.Deal(1, H, B)</c> is one human and one bot.</summary>
    public const ParticipantKind H = ParticipantKind.Human;

    public const ParticipantKind B = ParticipantKind.Bot;

    /// <summary>The real board (docs/mapa.json), loaded once.</summary>
    public static readonly MapDefinition Map = MapDefinition.LoadEmbedded();

    private static readonly PlayerColor[] TableColors =
        [PlayerColor.Azul, PlayerColor.Rojo, PlayerColor.Negro, PlayerColor.Amarillo, PlayerColor.Verde];

    /// <summary>The rows of "Nueva partida": "Jugador 1", "Jugador 2"... with the kinds given and different colours.</summary>
    public static IReadOnlyList<ParticipantSetup> Table(params ParticipantKind[] kinds) =>
        kinds.Select((kind, seat) => new ParticipantSetup($"Jugador {seat + 1}", kind, TableColors[seat])).ToArray();

    /// <summary>A freshly dealt game (initial placement, round 1) with these kinds around the table.</summary>
    public static GameState Deal(long seed, params ParticipantKind[] kinds)
    {
        var created = GameSetup.Create(new GameOptions(Table(kinds)), seed, Map);
        Assert.True(created.IsSuccess, created.IsSuccess ? null : created.Error.Message);

        return created.Value;
    }

    /// <summary>The same game with other kinds around the table (to get what <c>CreateGame</c> refuses, such as all bots).</summary>
    public static GameState WithKinds(GameState state, params ParticipantKind[] kinds) =>
        state with { Participants = state.Participants.Select(p => p with { Kind = kinds[p.Seat] }).ToValueArray() };

    public static GameState AllBots(GameState state) =>
        WithKinds(state, Enumerable.Repeat(ParticipantKind.Bot, state.Participants.Count).ToArray());

    /// <summary>The command of the active participant that places the first pool whole on its first country (initial placement or Refuerzo).</summary>
    public static GameCommand WholePool(GameState state, int country = 0)
    {
        var moves = LegalMoves.For(state, Map);
        var pool = moves.Pools[0];

        return moves.Place([new ArmyPlacement(pool.TerritoryIds[country], pool.Armies)]);
    }

    public static GameState Advance(GameState state, GameCommand command)
    {
        var applied = GameEngine.Apply(state, command, Map);
        Assert.True(applied.IsSuccess, applied.IsSuccess ? null : $"{applied.Error.Code}: {applied.Error.Message}");

        return applied.Value.State;
    }

    /// <summary>Every participant places their two pools: the game is now in the hostilities round, in Ataque, seat <c>FirstSeat</c> acting.</summary>
    public static GameState FinishInitialPlacement(GameState state)
    {
        while (state.Phase == GamePhase.InitialPlacement)
        {
            state = Advance(state, WholePool(state));
        }

        return state;
    }

    /// <summary>The same state with these countries given to <paramref name="seat"/> with this many armies.</summary>
    public static GameState Own(GameState state, int seat, params (string Id, int Armies)[] countries)
    {
        var byId = countries.ToDictionary(c => c.Id, c => c.Armies);

        return state with
        {
            Territories = state.Territories
                .Select(t => byId.TryGetValue(t.Id, out var armies) ? t with { Owner = seat, Armies = armies } : t)
                .ToValueArray(),
        };
    }

    /// <summary>The same state with these countries holding this many armies (their owners do not change).</summary>
    public static GameState SetArmies(GameState state, params (string Id, int Armies)[] countries)
    {
        var byId = countries.ToDictionary(c => c.Id, c => c.Armies);

        return state with
        {
            Territories = state.Territories
                .Select(t => byId.TryGetValue(t.Id, out var armies) ? t with { Armies = armies } : t)
                .ToValueArray(),
        };
    }

    /// <summary>The same state with this hand for the participant (the cards are taken as they are: tests choose what makes sense).</summary>
    public static GameState WithHand(GameState state, int seat, params HeldCard[] cards) =>
        state with
        {
            Participants = state.Participants.Select(p => p.Seat == seat ? p with { Cards = cards.ToValueArray() } : p).ToValueArray(),
        };

    /// <summary>
    /// A hand-made position of a two-participant game of bots on the real board: the active participant owns exactly
    /// <paramref name="mine"/> (with those armies) and the other owns every other country with <paramref name="restArmies"/> armies each.
    /// The turn context is empty, whatever the phase (a test that needs a pool or a pending conquest sets it afterwards).
    /// </summary>
    public static GameState Position(GamePhase phase, int restArmies, params (string Id, int Armies)[] mine)
    {
        var start = FinishInitialPlacement(AllBots(Deal(1, H, H)));
        var seat = start.ActiveSeat;
        var armiesOfMine = mine.ToDictionary(c => c.Id, c => c.Armies);

        return start with
        {
            Phase = phase,
            Round = 2,
            Turn = TurnContext.None,
            Territories = start.Territories
                .Select(t => armiesOfMine.TryGetValue(t.Id, out var armies)
                    ? t with { Owner = seat, Armies = armies }
                    : t with { Owner = 1 - seat, Armies = restArmies })
                .ToValueArray(),
        };
    }

    public static IEnumerable<string> OwnedBy(GameState state, int seat) =>
        state.Territories.Where(t => t.Owner == seat).Select(t => t.Id);
}
