using Teg.Domain.Map;

namespace Teg.Domain.Tests.Map;

/// <summary>
/// Loader/lookup behaviour on a tiny hand-built map, so the invariants in
/// <see cref="MapInvariantTests"/> are proven against a loader that really parses and validates.
/// </summary>
public sealed class MapDefinitionTests
{
    private const string TinyMapJson = """
        {
          "version": 1,
          "continents": [
            { "id": "c1", "name": "Continente Uno", "bonus": 2, "territories": ["a", "b"] },
            { "id": "c2", "name": "Continente Dos", "bonus": 3, "territories": ["c"] }
          ],
          "territories": [
            { "id": "a", "name": "Alfa", "continent": "c1", "symbol": "galeon" },
            { "id": "b", "name": "Beta", "continent": "c1", "symbol": "globo" },
            { "id": "c", "name": "Gamma", "continent": "c2", "symbol": "canon" }
          ],
          "borders": [
            { "a": "a", "b": "b", "type": "land" },
            { "a": "b", "b": "c", "type": "bridge" }
          ]
        }
        """;

    [Fact]
    public void FromJson_ParsesTerritoriesContinentsAndBorders()
    {
        var map = MapDefinition.FromJson(TinyMapJson);

        Assert.Equal(["a", "b", "c"], map.Territories.Select(t => t.Id));
        Assert.Equal(new Territory("c", "Gamma", "c2", CardSymbol.Canon), map.Territory("c"));
        Assert.Equal(3, map.Continents.Single(c => c.Id == "c2").Bonus);
        Assert.Equal(["a", "b"], map.Continents.Single(c => c.Id == "c1").TerritoryIds);
        Assert.Equal(
            [new Border("a", "b", BorderKind.Land), new Border("b", "c", BorderKind.Bridge)],
            map.Borders);
    }

    [Fact]
    public void Neighbors_AreUndirectedAndIncludeBridges()
    {
        var map = MapDefinition.FromJson(TinyMapJson);

        Assert.Equal(["a", "c"], map.Neighbors("b").Order(StringComparer.Ordinal));
        Assert.Equal(["b"], map.Neighbors("a"));
        Assert.Equal(["b"], map.Neighbors("c"));
        Assert.True(map.AreAdjacent("c", "b"));
        Assert.False(map.AreAdjacent("a", "c"));
    }

    [Fact]
    public void Neighbors_OfUnknownTerritory_Throws()
    {
        var map = MapDefinition.FromJson(TinyMapJson);

        Assert.Throws<ArgumentException>(() => map.Neighbors("zzz"));
    }

    [Fact]
    public void FromJson_BorderToUnknownTerritory_Throws()
    {
        var broken = TinyMapJson.Replace("\"b\": \"c\", \"type\": \"bridge\"", "\"b\": \"zzz\", \"type\": \"bridge\"");

        var ex = Assert.Throws<InvalidDataException>(() => MapDefinition.FromJson(broken));

        Assert.Contains("zzz", ex.Message);
    }

    [Fact]
    public void FromJson_DuplicateTerritoryId_Throws()
    {
        var broken = TinyMapJson.Replace("\"id\": \"b\", \"name\": \"Beta\"", "\"id\": \"a\", \"name\": \"Beta\"");

        Assert.Throws<InvalidDataException>(() => MapDefinition.FromJson(broken));
    }

    [Fact]
    public void FromJson_UnknownCardSymbol_Throws()
    {
        var broken = TinyMapJson.Replace("\"canon\"", "\"espada\"");

        Assert.Throws<InvalidDataException>(() => MapDefinition.FromJson(broken));
    }
}
