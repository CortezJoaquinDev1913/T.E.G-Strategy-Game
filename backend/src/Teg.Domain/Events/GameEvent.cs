using Teg.Domain.Collections;
using Teg.Domain.State;

namespace Teg.Domain.Events;

/// <summary>
/// A fact that happened while applying a command. Events are the history of the game (R§9) and have value
/// equality, so a replay of the same seed and commands produces equal events (E10).
/// </summary>
public abstract record GameEvent;

/// <summary>A participant placed the pool of an initial placement round (R§3).</summary>
public sealed record InitialArmiesPlaced(int Seat, int PlacementRound, ValueArray<ArmyPlacement> Placements) : GameEvent;

/// <summary>
/// The Refuerzo pool of the turn that just started (R§5): <paramref name="CountryArmies"/> for the countries and the
/// bonus of every continent owned in full. Trades add their own armies later (<see cref="CardsTraded"/>).
/// </summary>
public sealed record ReinforcementPoolComputed(int Seat, int CountryArmies, ValueArray<ContinentPool> ContinentBonuses) : GameEvent;

/// <summary>Part (or all) of the pending reinforcements were placed (R§5).</summary>
public sealed record ReinforcementsPlaced(int Seat, ValueArray<ArmyPlacement> Placements) : GameEvent;

/// <summary>Three cards were traded for <paramref name="Armies"/> armies (R§7); the cards went to the discard pile.</summary>
public sealed record CardsTraded(int Seat, ValueArray<string> Cards, int Armies) : GameEvent;

/// <summary>
/// An attack was fought (R§6). Both dice lists are SORTED FROM THE HIGHEST DIE TO THE LOWEST, the order they are compared
/// in; they were rolled attacker first, then defender. Each lost pair cost its loser one army.
/// </summary>
public sealed record AttackResolved(
    int Seat,
    string From,
    string To,
    ValueArray<int> AttackerDice,
    ValueArray<int> DefenderDice,
    int AttackerLosses,
    int DefenderLosses) : GameEvent;

/// <summary>
/// The attack emptied <paramref name="To"/>: it now belongs to <paramref name="Seat"/> (its conquering army is already
/// there) and the occupation is pending (R§6). <paramref name="PreviousOwner"/> is who lost it.
/// </summary>
public sealed record TerritoryConquered(int Seat, string From, string To, int PreviousOwner) : GameEvent;

/// <summary>
/// <paramref name="Eliminated"/> lost their last country to <paramref name="By"/>, who inherits
/// <paramref name="InheritedCards"/> (their hand, which is now empty) (R§6).
/// </summary>
public sealed record ParticipantEliminated(int Eliminated, int By, ValueArray<string> InheritedCards) : GameEvent;

/// <summary>After a conquest the attacker left <paramref name="Armies"/> armies in total on <paramref name="To"/>, taken from <paramref name="From"/> (R§6).</summary>
public sealed record ArmiesMovedAfterConquest(int Seat, string From, string To, int Armies) : GameEvent;

/// <summary><paramref name="Armies"/> armies moved from the own country <paramref name="From"/> to the own country <paramref name="To"/> in Reagrupe (R§6).</summary>
public sealed record ArmiesRegrouped(int Seat, string From, string To, int Armies) : GameEvent;

/// <summary>The active participant moved on to <paramref name="To"/> inside their own turn.</summary>
public sealed record PhaseChanged(int Seat, GamePhase From, GamePhase To) : GameEvent;

/// <summary>
/// <paramref name="Seat"/>'s turn begins in <paramref name="Phase"/> (the first phase of the turn: Refuerzo, or
/// Ataque in the hostilities round) during <paramref name="Round"/> (0 while initial placement is running).
/// </summary>
public sealed record TurnStarted(int Seat, int Round, GamePhase Phase) : GameEvent;

/// <summary><paramref name="Seat"/>'s turn is over.</summary>
public sealed record TurnEnded(int Seat) : GameEvent;

/// <summary>The rotation returned to the first player: the round counter is now <paramref name="Round"/> (R§4).</summary>
public sealed record RoundAdvanced(int Round) : GameEvent;
