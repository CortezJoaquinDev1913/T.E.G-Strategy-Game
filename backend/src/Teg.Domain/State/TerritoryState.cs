namespace Teg.Domain.State;

/// <summary>Current owner (a participant seat) and armies of one country.</summary>
public sealed record TerritoryState(string Id, int Owner, int Armies);
