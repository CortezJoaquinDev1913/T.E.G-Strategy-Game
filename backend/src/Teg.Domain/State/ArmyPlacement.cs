namespace Teg.Domain.State;

/// <summary>An amount of armies put on one country (initial placement now, reinforcements later).</summary>
public sealed record ArmyPlacement(string TerritoryId, int Armies);
