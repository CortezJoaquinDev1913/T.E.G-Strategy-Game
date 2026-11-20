using Teg.Domain.Collections;
using Teg.Domain.Events;
using Teg.Domain.State;

namespace Teg.Domain;

/// <summary>What an accepted command produces: the new state and the events that explain the change, in order.</summary>
public sealed record Transition(GameState State, ValueArray<GameEvent> Events);
