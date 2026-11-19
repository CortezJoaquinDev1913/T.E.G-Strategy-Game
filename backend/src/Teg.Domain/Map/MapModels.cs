namespace Teg.Domain.Map;

/// <summary>Symbol printed on a country card (docs/mapa.md: 17 galeones, 17 globos, 16 cañones).</summary>
public enum CardSymbol
{
    Galeon,
    Globo,
    Canon,
}

/// <summary>How two countries touch. Both kinds count as adjacency for attacking and regrouping.</summary>
public enum BorderKind
{
    Land,
    Bridge,
}

public sealed record Territory(string Id, string Name, string ContinentId, CardSymbol Symbol);

public sealed record Continent(string Id, string Name, int Bonus, IReadOnlyList<string> TerritoryIds);

/// <summary>An undirected border between <paramref name="A"/> and <paramref name="B"/>.</summary>
public sealed record Border(string A, string B, BorderKind Kind);
