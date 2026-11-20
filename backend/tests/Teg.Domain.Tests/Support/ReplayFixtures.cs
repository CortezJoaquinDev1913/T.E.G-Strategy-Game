using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Events;
using Teg.Domain.Rng;
using Teg.Domain.Setup;
using Teg.Domain.State;
using static Teg.Domain.Tests.Support.GameFixtures;

namespace Teg.Domain.Tests.Support;

/// <summary>
/// What the replay tests need: the options of a game, a recorded game (its command list and every transition it produced) and the
/// state a game is restored to from its persisted form.
/// </summary>
internal static class ReplayFixtures
{
    private static readonly PlayerColor[] Colours =
        [PlayerColor.Azul, PlayerColor.Rojo, PlayerColor.Negro, PlayerColor.Amarillo];

    /// <summary>A game recorded as the engine played it: the state it started in, the commands, and what each one produced, in order.</summary>
    internal sealed record Recording(GameState Start, IReadOnlyList<GameCommand> Commands, IReadOnlyList<Transition> Transitions)
    {
        /// <summary>The state the last command left (the start when there was none).</summary>
        public GameState Final => Transitions.Count == 0 ? Start : Transitions[^1].State;

        /// <summary>The whole history: every event of every command, in order.</summary>
        public IReadOnlyList<GameEvent> Events => Transitions.SelectMany(transition => transition.Events).ToList();
    }

    /// <summary>One human and bots, the first colours of the palette: what "Nueva partida" would send.</summary>
    public static GameOptions Options(int participants) =>
        new(Enumerable.Range(0, participants)
            .Select(seat => new ParticipantSetup($"Jugador {seat + 1}", seat == 0 ? ParticipantKind.Human : ParticipantKind.Bot, Colours[seat]))
            .ToArray());

    /// <summary>A new game from <paramref name="options"/> and <paramref name="seed"/> on the real board.</summary>
    public static GameState Create(GameOptions options, long seed)
    {
        var result = GameSetup.Create(options, seed, RealMap);
        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Message);

        return result.Value;
    }

    /// <summary>
    /// Creates the game and plays it with the seeded random policy (picking only legal moves) until somebody wins or
    /// <paramref name="cap"/> commands were played; every command must be accepted.
    /// </summary>
    public static Recording Record(GameOptions options, long seed, long choiceSeed, int cap, int? conqueror = null)
    {
        var start = Create(options, seed);
        var steps = Playout.Record(start, choiceSeed, cap, conqueror: conqueror);

        return new Recording(start, steps.Select(step => step.Command).ToList(), steps.Select(step => step.After).ToList());
    }

    /// <summary>Applies <paramref name="commands"/> one after the other from <paramref name="start"/>; each one must be accepted.</summary>
    public static Recording Replay(GameState start, IReadOnlyList<GameCommand> commands)
    {
        var transitions = new List<Transition>();
        var state = start;

        foreach (var command in commands)
        {
            var transition = ApplyOk(state, command);
            transitions.Add(transition);
            state = transition.State;
        }

        return new Recording(start, commands, transitions);
    }

    /// <summary>
    /// The state of a saved game rebuilt from its persisted form: every collection and record is a NEW object (nothing is shared with
    /// <paramref name="saved"/> but strings and numbers), and the generator comes from the two numbers a database keeps, the seed
    /// and the draws taken, through <see cref="SeededRng"/>'s O(1) restore.
    /// </summary>
    public static GameState Restore(GameState saved)
    {
        var rng = new SeededRng(saved.Rng.Seed, saved.Rng.Draws);

        return saved with
        {
            Rng = rng.State,
            Ranking = saved.Ranking.ToArray().ToValueArray(),
            Participants = saved.Participants
                .Select(p => p with { Cards = p.Cards.Select(card => card with { }).ToValueArray() })
                .ToValueArray(),
            Territories = saved.Territories.Select(t => t with { }).ToValueArray(),
            Deck = new Deck(saved.Deck.Draw.ToArray().ToValueArray(), saved.Deck.Discard.ToArray().ToValueArray()),
            Turn = new TurnContext(
                saved.Turn.FreeArmies,
                saved.Turn.ContinentPools.Select(pool => pool with { }).ToValueArray())
            {
                ConquestsThisTurn = saved.Turn.ConquestsThisTurn,
                PendingConquest = saved.Turn.PendingConquest is { } pending ? pending with { } : null,
                RegroupQuotas = saved.Turn.RegroupQuotas.Select(quota => quota with { }).ToValueArray(),
            },
        };
    }
}
