using Teg.Domain.State;

namespace Teg.Application.Views;

/// <summary>Whether a game is still being played.</summary>
public enum GameStatus
{
    Active,
    Finished,
}

/// <summary>One participant as the whole table sees them: their cards are only counted (R§2).</summary>
public sealed record ParticipantView(
    int Seat,
    string Name,
    ParticipantKind Kind,
    PlayerColor Color,
    bool Eliminated,
    int CardCount,
    int TradesDone,
    int TerritoryCount);

public sealed record TerritoryView(string Id, int Owner, int Armies);

public sealed record ContinentPoolView(string ContinentId, int Armies);

public sealed record PendingConquestView(string From, string To);

/// <summary>What belongs to the current turn: the armies still to place, the conquests so far, the occupation pending, the bot actions so far.</summary>
public sealed record TurnView(
    int FreeArmies,
    IReadOnlyList<ContinentPoolView> ContinentPools,
    int ConquestsThisTurn,
    PendingConquestView? PendingConquest,
    int BotActions);

public enum ObjectiveKind
{
    None,
    Common,
    Occupation,
    Destruction,
}

/// <summary>
/// A secret objective (R§8) as the active participant is shown it: <paramref name="Number"/> is the objective of the table of R§8
/// (occupation 1-9, destruction 10-15; null for none and common) and <paramref name="Target"/> the colour to destroy.
/// </summary>
public sealed record ObjectiveView(ObjectiveKind Kind, int? Number, PlayerColor? Target)
{
    internal static ObjectiveView Of(Objective objective) => objective switch
    {
        Objective.Occupation occupation => new ObjectiveView(ObjectiveKind.Occupation, occupation.Number, null),
        Objective.Destruction destruction => new ObjectiveView(ObjectiveKind.Destruction, destruction.Number, destruction.Target),
        Objective.CommonObjective => new ObjectiveView(ObjectiveKind.Common, null, null),
        Objective.NoObjective => new ObjectiveView(ObjectiveKind.None, null, null),
        _ => throw new ArgumentException($"Unknown objective {objective.GetType().Name}.", nameof(objective)),
    };
}

public sealed record CardView(string TerritoryId, bool BonusClaimed);

/// <summary>
/// What only the ACTIVE participant may see (the design's <c>activePrivate</c>): their objective and their cards. Hiding is cosmetic
/// (R§2, the screen is shared), and the UI shows it after "Empezar turno"; what this view guarantees is that it holds nobody else's.
/// </summary>
public sealed record ActivePrivateView(int Seat, ObjectiveView Objective, IReadOnlyList<CardView> Cards);

/// <summary>
/// The public state of a game (A4): what any participant at the table may see. It holds no seed, no order of the deck, nobody's
/// objective and nobody's hand but those of the active participant, in <paramref name="ActivePrivate"/>.
/// </summary>
/// <param name="ActivePrivate">The objective and the cards of the active participant; <c>null</c> once the game is finished.</param>
public sealed record GameView(
    Guid Id,
    int Version,
    GameStatus Status,
    GamePhase Phase,
    int PlacementRound,
    int Round,
    int FirstSeat,
    int ActiveSeat,
    IReadOnlyList<int> Ranking,
    IReadOnlyList<ParticipantView> Participants,
    IReadOnlyList<TerritoryView> Territories,
    TurnView Turn,
    int DrawPileCount,
    int DiscardPileCount,
    int? Winner,
    WinReason? WinReason,
    ActivePrivateView? ActivePrivate)
{
    /// <summary>The view of a saved state.</summary>
    public static GameView From(Guid id, GameState state) => new(
        id,
        state.Version,
        StatusOf(state),
        state.Phase,
        state.PlacementRound,
        state.Round,
        state.FirstSeat,
        state.ActiveSeat,
        state.Ranking.ToArray(),
        ParticipantsOf(state),
        state.Territories.Select(t => new TerritoryView(t.Id, t.Owner, t.Armies)).ToArray(),
        TurnOf(state.Turn),
        state.Deck.Draw.Count,
        state.Deck.Discard.Count,
        state.Winner,
        state.WinReason,
        ActivePrivateOf(state));

    internal static GameStatus StatusOf(GameState state) =>
        state.Phase == GamePhase.Finished ? GameStatus.Finished : GameStatus.Active;

    internal static IReadOnlyList<ParticipantView> ParticipantsOf(GameState state)
    {
        var territories = state.Territories.ToLookup(t => t.Owner);

        return state.Participants
            .Select(p => new ParticipantView(
                p.Seat, p.Name, p.Kind, p.Color, p.Eliminated, p.Cards.Count, p.TradesDone, territories[p.Seat].Count()))
            .ToArray();
    }

    private static TurnView TurnOf(TurnContext turn) => new(
        turn.FreeArmies,
        turn.ContinentPools.Select(pool => new ContinentPoolView(pool.ContinentId, pool.Armies)).ToArray(),
        turn.ConquestsThisTurn,
        turn.PendingConquest is { } pending ? new PendingConquestView(pending.From, pending.To) : null,
        turn.BotActions);

    private static ActivePrivateView? ActivePrivateOf(GameState state)
    {
        if (state.Phase == GamePhase.Finished)
        {
            return null;
        }

        var active = state.Participants[state.ActiveSeat];

        return new ActivePrivateView(
            active.Seat,
            ObjectiveView.Of(active.Objective),
            active.Cards.Select(card => new CardView(card.TerritoryId, card.BonusClaimed)).ToArray());
    }
}

/// <summary>A game in the resume list: enough to recognise it and to know whose turn it is.</summary>
public sealed record GameSummary(
    Guid Id,
    GameStatus Status,
    int Version,
    GamePhase Phase,
    int Round,
    int ActiveSeat,
    IReadOnlyList<ParticipantView> Participants,
    int? Winner)
{
    /// <summary>The summary of a saved state (a repository that projects straight from its tables builds the same thing).</summary>
    public static GameSummary From(Guid id, GameState state) => new(
        id,
        GameView.StatusOf(state),
        state.Version,
        state.Phase,
        state.Round,
        state.ActiveSeat,
        GameView.ParticipantsOf(state),
        state.Winner);
}
