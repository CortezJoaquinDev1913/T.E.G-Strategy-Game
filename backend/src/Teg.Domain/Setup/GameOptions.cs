using Teg.Domain.State;

namespace Teg.Domain.Setup;

/// <summary>One row of "Nueva partida": name, kind and colour of a participant.</summary>
public sealed record ParticipantSetup(string Name, ParticipantKind Kind, PlayerColor Color);

/// <summary>Everything the user chooses when creating a game. List order = table (registration) order.</summary>
public sealed record GameOptions(IReadOnlyList<ParticipantSetup> Participants);
