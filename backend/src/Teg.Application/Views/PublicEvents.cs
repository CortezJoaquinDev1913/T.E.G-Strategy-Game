using Teg.Application.Ports;
using Teg.Domain.Collections;
using Teg.Domain.Events;

namespace Teg.Application.Views;

/// <summary>
/// The events as the whole table may read them (R§2, R§8). The store keeps the full record (a replay needs it); what is shown hides
/// which card was drawn or inherited and drops the announcement that a destruction objective became the common one, since it
/// would reveal a secret.
/// </summary>
public static class PublicEvents
{
    /// <summary>What replaces the id of a card the table may not see.</summary>
    public const string HiddenCard = "";

    /// <summary>The public form of an event, or <c>null</c> when the table must not see it at all.</summary>
    public static GameEvent? Of(GameEvent gameEvent) => gameEvent switch
    {
        ObjectiveFellBackToCommon => null,
        CardAwarded awarded => awarded with { Card = HiddenCard },
        ParticipantEliminated eliminated => eliminated with
        {
            InheritedCards = eliminated.InheritedCards.Select(_ => HiddenCard).ToValueArray(),
        },
        _ => gameEvent,
    };

    /// <summary>The public form of every event that is shown, in order.</summary>
    public static IReadOnlyList<GameEvent> OfAll(IEnumerable<GameEvent> events) =>
        events.Select(Of).OfType<GameEvent>().ToArray();

    /// <summary>The stored events that are shown, in their public form; each keeps the sequence number the store gave it.</summary>
    public static IReadOnlyList<StoredEvent> OfStored(IEnumerable<StoredEvent> events) =>
        events.Select(stored => Of(stored.Event) is { } shown ? stored with { Event = shown } : null).OfType<StoredEvent>().ToArray();
}
