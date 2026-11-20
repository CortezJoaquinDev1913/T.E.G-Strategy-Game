using Teg.Domain.Map;

namespace Teg.Domain.Tests.Map;

/// <summary>
/// One group of tests per invariant of docs/mapa.md ("Invariantes"). Names cite the invariant number (M-n).
/// </summary>
public sealed class MapInvariantTests
{
    private static readonly MapDefinition Map = MapDefinition.LoadEmbedded();

    /// <summary>The 23 bridges of the table "Puentes sobre el agua" of docs/mapa.md: "a|b" with a before b, ordinal order.</summary>
    private static readonly string[] BridgeIds =
    [
        "alaska|kamchatka",
        "alemania|suecia",
        "arabia|etiopia",
        "australia|borneo",
        "australia|chile",
        "australia|java",
        "borneo|java",
        "borneo|malasia",
        "borneo|sumatra",
        "brasil|sahara",
        "china|japon",
        "egipto|turquia",
        "espana|gran-bretana",
        "espana|sahara",
        "etiopia|madagascar",
        "francia|gran-bretana",
        "gran-bretana|islandia",
        "groenlandia|islandia",
        "groenlandia|labrador",
        "japon|kamchatka",
        "java|sumatra",
        "madagascar|sudafrica",
        "malasia|sumatra",
    ];

    // ---- embedded resource -------------------------------------------------------------------

    [Fact]
    public void EmbeddedResource_LoadsMapaJson()
    {
        using var stream = typeof(MapDefinition).Assembly.GetManifestResourceStream("mapa.json");

        Assert.NotNull(stream);
        Assert.True(stream.Length > 0);
        Assert.Equal(50, MapDefinition.LoadEmbedded().Territories.Count);
    }

    // ---- M-1: 50 unique ids, split 10/6/9/6/15/4 -----------------------------------------------

    [Fact]
    public void M1_HasFiftyTerritoriesWithUniqueIds()
    {
        Assert.Equal(50, Map.Territories.Count);
        Assert.Equal(50, Map.Territories.Select(t => t.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("america-del-norte", 10)]
    [InlineData("america-del-sur", 6)]
    [InlineData("europa", 9)]
    [InlineData("africa", 6)]
    [InlineData("asia", 15)]
    [InlineData("oceania", 4)]
    public void M1_ContinentHasExpectedNumberOfTerritories(string continentId, int expected)
    {
        var continent = Map.Continents.Single(c => c.Id == continentId);

        Assert.Equal(expected, continent.TerritoryIds.Count);
        Assert.Equal(expected, Map.Territories.Count(t => t.ContinentId == continentId));
    }

    [Theory]
    [InlineData("america-del-norte", 5)]
    [InlineData("america-del-sur", 3)]
    [InlineData("europa", 5)]
    [InlineData("africa", 3)]
    [InlineData("asia", 7)]
    [InlineData("oceania", 2)]
    public void M1_ContinentHasExpectedReinforcementBonus(string continentId, int expectedBonus)
    {
        Assert.Equal(expectedBonus, Map.Continents.Single(c => c.Id == continentId).Bonus);
    }

    // ---- M-2: symmetric, no self / duplicate borders, connected ---------------------------------

    [Fact]
    public void M2_BordersAreSymmetric()
    {
        foreach (var territory in Map.Territories)
        {
            foreach (var neighbor in Map.Neighbors(territory.Id))
            {
                Assert.Contains(territory.Id, Map.Neighbors(neighbor));
            }
        }

        // Guard against a vacuous pass: the loop above must have had something to check.
        Assert.True(Map.Territories.Sum(t => Map.Neighbors(t.Id).Count) > 0);
    }

    [Fact]
    public void M2_NoSelfBorders()
    {
        Assert.NotEmpty(Map.Borders);
        Assert.DoesNotContain(Map.Borders, b => b.A == b.B);
    }

    [Fact]
    public void M2_NoDuplicateBorders()
    {
        var undirectedPairs = Map.Borders
            .Select(b => string.CompareOrdinal(b.A, b.B) <= 0 ? $"{b.A}|{b.B}" : $"{b.B}|{b.A}")
            .ToList();

        Assert.NotEmpty(undirectedPairs);
        Assert.Equal(undirectedPairs.Count, undirectedPairs.Distinct().Count());
    }

    [Fact]
    public void M2_EveryBorderReferencesKnownTerritories()
    {
        var ids = Map.Territories.Select(t => t.Id).ToHashSet();

        Assert.All(Map.Borders, b =>
        {
            Assert.Contains(b.A, ids);
            Assert.Contains(b.B, ids);
        });
    }

    [Fact]
    public void M2_WholeGraphIsConnected()
    {
        var all = Map.Territories.Select(t => t.Id).ToHashSet();

        var reached = Reach(Map.Territories[0].Id, all);

        Assert.Equal(50, reached.Count);
    }

    // ---- M-3: every continent internally connected (bridges count as adjacency) -------------------

    [Theory]
    [InlineData("america-del-norte")]
    [InlineData("america-del-sur")]
    [InlineData("europa")]
    [InlineData("africa")]
    [InlineData("asia")]
    [InlineData("oceania")]
    public void M3_ContinentIsInternallyConnected(string continentId)
    {
        var members = Map.Continents.Single(c => c.Id == continentId).TerritoryIds.ToHashSet();

        var reached = Reach(members.First(), members);

        Assert.Equal(members.Count, reached.Count);
    }

    [Fact]
    public void M3_ContinentMembershipIsConsistentBetweenTerritoriesAndContinents()
    {
        foreach (var continent in Map.Continents)
        {
            foreach (var id in continent.TerritoryIds)
            {
                Assert.Equal(continent.Id, Map.Territory(id).ContinentId);
            }
        }

        Assert.Equal(50, Map.Continents.Sum(c => c.TerritoryIds.Count));
    }

    // ---- M-4: at least 2 neighbours per country ---------------------------------------------------

    [Fact]
    public void M4_EveryTerritoryHasAtLeastTwoNeighbors()
    {
        Assert.All(Map.Territories, t =>
            Assert.True(
                Map.Neighbors(t.Id).Count >= 2,
                $"{t.Id} has only {Map.Neighbors(t.Id).Count} neighbor(s)"));
    }

    [Fact]
    public void M4_BridgesCountAsAdjacency()
    {
        // groenlandia is joined to the rest of the map ONLY through bridges.
        Assert.Equal(
            ["islandia", "labrador"],
            Map.Neighbors("groenlandia").OrderBy(id => id, StringComparer.Ordinal));
        Assert.All(
            Map.Borders.Where(b => b.A == "groenlandia" || b.B == "groenlandia"),
            b => Assert.Equal(BorderKind.Bridge, b.Kind));
    }

    // ---- M-5: card symbols 17 / 17 / 16 -----------------------------------------------------------

    [Theory]
    [InlineData(CardSymbol.Galeon, 17)]
    [InlineData(CardSymbol.Globo, 17)]
    [InlineData(CardSymbol.Canon, 16)]
    public void M5_CardSymbolDistribution(CardSymbol symbol, int expected)
    {
        Assert.Equal(expected, Map.Territories.Count(t => t.Symbol == symbol));
    }

    // ---- M-6: continents reach the quotas of the 15 secret objectives -------------------------------

    [Theory]
    [InlineData("america-del-norte", 5)] // objectives 1, 9
    [InlineData("america-del-sur", 2)]   // objectives 3, 4, 6
    [InlineData("europa", 7)]            // objective 2
    [InlineData("africa", 2)]            // objective 6
    [InlineData("asia", 4)]              // objectives 4, 5, 8
    [InlineData("oceania", 2)]           // objectives 5, 6
    public void M6_ContinentIsLargeEnoughForObjectiveQuota(string continentId, int requiredByAnObjective)
    {
        var size = Map.Continents.Single(c => c.Id == continentId).TerritoryIds.Count;

        Assert.True(size >= requiredByAnObjective);
    }

    // ---- M-7: exactly the 23 bridges of the table --------------------------------------------------

    [Fact]
    public void M7_ThereAreExactlyTwentyThreeBridgesAndTheyAreTheDocumentedOnes()
    {
        var bridges = Map.Borders
            .Where(b => b.Kind == BorderKind.Bridge)
            .Select(b => string.CompareOrdinal(b.A, b.B) <= 0 ? $"{b.A}|{b.B}" : $"{b.B}|{b.A}")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(23, bridges.Count);
        Assert.Equal(BridgeIds, bridges);
    }

    [Fact]
    public void M7_BridgeEndpointsAreAdjacentBothWays()
    {
        Assert.Contains("kamchatka", Map.Neighbors("alaska"));
        Assert.Contains("alaska", Map.Neighbors("kamchatka"));
        Assert.True(Map.AreAdjacent("espana", "sahara"));
        Assert.True(Map.AreAdjacent("sahara", "espana"));
        Assert.False(Map.AreAdjacent("francia", "sahara")); // two hops away: land to espana, then the bridge
    }

    // ---- helpers --------------------------------------------------------------------------------

    /// <summary>Independent breadth-first search restricted to <paramref name="allowed"/>.</summary>
    private static HashSet<string> Reach(string start, HashSet<string> allowed)
    {
        var seen = new HashSet<string> { start };
        var queue = new Queue<string>([start]);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var next in Map.Neighbors(current))
            {
                if (allowed.Contains(next) && seen.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return seen;
    }
}
