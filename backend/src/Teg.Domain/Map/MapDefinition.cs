using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;

namespace Teg.Domain.Map;

/// <summary>
/// Immutable board definition (countries, continents, borders), loaded from docs/mapa.json,
/// which is embedded in this assembly as <c>mapa.json</c>.
/// </summary>
public sealed class MapDefinition
{
    private const string EmbeddedResourceName = "mapa.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly FrozenDictionary<string, Territory> _territoriesById;
    private readonly FrozenDictionary<string, FrozenSet<string>> _neighborsById;

    private MapDefinition(
        ImmutableArray<Territory> territories,
        ImmutableArray<Continent> continents,
        ImmutableArray<Border> borders)
    {
        Territories = territories;
        Continents = continents;
        Borders = borders;

        _territoriesById = territories.ToFrozenDictionary(t => t.Id);
        _neighborsById = BuildNeighbors(territories, borders);
    }

    public IReadOnlyList<Territory> Territories { get; }

    public IReadOnlyList<Continent> Continents { get; }

    /// <summary>The borders exactly as declared (one entry per undirected border).</summary>
    public IReadOnlyList<Border> Borders { get; }

    /// <summary>Loads the map embedded in this assembly.</summary>
    public static MapDefinition LoadEmbedded()
    {
        using var stream = typeof(MapDefinition).Assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{EmbeddedResourceName}' not found.");
        using var reader = new StreamReader(stream);

        return FromJson(reader.ReadToEnd());
    }

    /// <summary>Parses and validates a map document (same schema as docs/mapa.json).</summary>
    public static MapDefinition FromJson(string json)
    {
        var document = JsonSerializer.Deserialize<MapDocument>(json, JsonOptions)
            ?? throw new InvalidDataException("Map document is empty.");

        var territories = document.Territories
            .Select(t => new Territory(t.Id, t.Name, t.Continent, ParseSymbol(t.Id, t.Symbol)))
            .ToImmutableArray();

        var duplicate = territories.GroupBy(t => t.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException($"Duplicate territory id '{duplicate.Key}'.");
        }

        var known = territories.Select(t => t.Id).ToHashSet();
        var borders = document.Borders
            .Select(b => new Border(
                RequireKnown(known, b.A),
                RequireKnown(known, b.B),
                ParseBorderKind(b.Type)))
            .ToImmutableArray();

        var continents = document.Continents
            .Select(c => new Continent(c.Id, c.Name, c.Bonus, c.Territories.ToImmutableArray()))
            .ToImmutableArray();

        return new MapDefinition(territories, continents, borders);
    }

    /// <summary>Returns the country with this id, or throws <see cref="ArgumentException"/>.</summary>
    public Territory Territory(string id) =>
        _territoriesById.TryGetValue(id, out var territory)
            ? territory
            : throw new ArgumentException($"Unknown territory '{id}'.", nameof(id));

    /// <summary>Adjacent countries (land borders and bridges alike).</summary>
    public IReadOnlySet<string> Neighbors(string id) =>
        _neighborsById.TryGetValue(id, out var neighbors)
            ? neighbors
            : throw new ArgumentException($"Unknown territory '{id}'.", nameof(id));

    public bool AreAdjacent(string a, string b) => Neighbors(a).Contains(b);

    private static FrozenDictionary<string, FrozenSet<string>> BuildNeighbors(
        ImmutableArray<Territory> territories,
        ImmutableArray<Border> borders)
    {
        var sets = territories.ToDictionary(t => t.Id, _ => new HashSet<string>());
        foreach (var border in borders)
        {
            sets[border.A].Add(border.B);
            sets[border.B].Add(border.A);
        }

        return sets.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.ToFrozenSet());
    }

    private static string RequireKnown(HashSet<string> known, string id) =>
        known.Contains(id) ? id : throw new InvalidDataException($"Border references unknown territory '{id}'.");

    private static CardSymbol ParseSymbol(string territoryId, string symbol) => symbol switch
    {
        "galeon" => CardSymbol.Galeon,
        "globo" => CardSymbol.Globo,
        "canon" => CardSymbol.Canon,
        _ => throw new InvalidDataException($"Territory '{territoryId}' has unknown card symbol '{symbol}'."),
    };

    private static BorderKind ParseBorderKind(string type) => type switch
    {
        "land" => BorderKind.Land,
        "bridge" => BorderKind.Bridge,
        _ => throw new InvalidDataException($"Unknown border type '{type}'."),
    };

    // ---- JSON document shape (docs/mapa.json) ----------------------------------------------

    private sealed record MapDocument(
        List<ContinentDocument> Continents,
        List<TerritoryDocument> Territories,
        List<BorderDocument> Borders);

    private sealed record ContinentDocument(string Id, string Name, int Bonus, List<string> Territories);

    private sealed record TerritoryDocument(string Id, string Name, string Continent, string Symbol);

    private sealed record BorderDocument(string A, string B, string Type);
}
