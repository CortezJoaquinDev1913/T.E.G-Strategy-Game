using Teg.Domain.Collections;
using Teg.Domain.State;

namespace Teg.Domain.Commands;

/// <summary>
/// An action a participant asks the engine to perform. <paramref name="Seat"/> is who is acting: it must be the
/// active participant (R§4). Commands are plain data; <see cref="GameEngine.Apply"/> validates and executes them.
/// </summary>
public abstract record GameCommand(int Seat);

/// <summary>Initial placement (R§3): the whole pool of the current round, distributed over own countries.</summary>
public sealed record PlaceInitialArmies(int Seat, ValueArray<ArmyPlacement> Placements) : GameCommand(Seat);

/// <summary>Leaves Ataque for Reagrupe: attacking is over for this turn (R§6). Irreversible.</summary>
public sealed record StartRegroup(int Seat) : GameCommand(Seat);

/// <summary>Attack the enemy country <paramref name="To"/> from the own country <paramref name="From"/> (R§6).</summary>
public sealed record Attack(int Seat, string From, string To) : GameCommand(Seat);

/// <summary>Move <paramref name="Armies"/> from the own country <paramref name="From"/> to the own country <paramref name="To"/> (R§6).</summary>
public sealed record Regroup(int Seat, string From, string To, int Armies) : GameCommand(Seat);

/// <summary>Finishes the turn: Tarjeta is resolved and the next living participant becomes active (R§4).</summary>
public sealed record EndTurn(int Seat) : GameCommand(Seat);
