using Teg.Domain.Map;
using Teg.Domain.Rules;
using Teg.Domain.State;
using static Teg.Domain.Tests.Support.GameFixtures;

namespace Teg.Domain.Tests.ObjectiveRules;

/// <summary>
/// E9 / R§8: the nine OCCUPATION objectives (1 to 9) and the catalogue of the fifteen. Each objective has its own test
/// group: the exact quota fulfils it and, for EVERY clause, one country short does not, even when the participant owns
/// all the rest of the map (a surplus in another continent never compensates). The expectations come from the table of
/// R§8, typed here clause by clause and independent from the production code; the sizes of the whole-continent clauses
/// are read from the map. (The test namespace is not named <c>Objectives</c>: it would hide the class under test.)
/// </summary>
public sealed class ObjectiveTests
{
    private static readonly MapDefinition Map = RealMap;

    private const string NorthAmerica = "america-del-norte";
    private const string SouthAmerica = "america-del-sur";
    private const string Europe = "europa";
    private const string Africa = "africa";
    private const string Asia = "asia";
    private const string Oceania = "oceania";

    /// <summary>Three Asian countries bordering one another (a triangle): objective 2's "3 connected countries" (8.2 tests that clause).</summary>
    private static readonly string[] ConnectedTripleInAsia = ["china", "gobi", "mongolia"];

    // ---- the table of R§8, clause by clause -----------------------------------------------------------

    private static (string Continent, int Count) All(string continentId) => (continentId, CountriesOf(continentId).Length);

    private static (string Continent, int Count) Some(string continentId, int count) => (continentId, count);

    /// <summary>The CONTINENT clauses of each occupation objective. Objective 2 also asks for 3 connected countries (8.2).</summary>
    private static readonly Dictionary<int, (string Continent, int Count)[]> Clauses = new()
    {
        [1] = [All(Africa), Some(NorthAmerica, 5), Some(Europe, 4)],
        [2] = [All(SouthAmerica), Some(Europe, 7)],
        [3] = [All(Asia), Some(SouthAmerica, 2)],
        [4] = [All(Europe), Some(Asia, 4), Some(SouthAmerica, 2)],
        [5] = [All(NorthAmerica), Some(Oceania, 2), Some(Asia, 4)],
        [6] = [Some(Oceania, 2), Some(Africa, 2), Some(SouthAmerica, 2), Some(Europe, 3), Some(NorthAmerica, 4), Some(Asia, 3)],
        [7] = [All(Oceania), All(NorthAmerica), Some(Europe, 2)],
        [8] = [All(SouthAmerica), All(Africa), Some(Asia, 4)],
        [9] = [All(Oceania), All(Africa), Some(NorthAmerica, 5)],
    };

    private static string[] CountriesOf(string continentId) =>
        Map.Continents.Single(c => c.Id == continentId).TerritoryIds.ToArray();

    /// <summary>The first <c>Count</c> countries of each clause's continent, in map order.</summary>
    private static HashSet<string> Holding(IEnumerable<(string Continent, int Count)> clauses) =>
        clauses.SelectMany(clause => CountriesOf(clause.Continent).Take(clause.Count)).ToHashSet();

    /// <summary>
    /// The quota of the objective with ONE country less in clause <paramref name="clause"/>, plus every country of every
    /// continent the objective does not mention: whatever is owned elsewhere must not make up for the missing country.
    /// </summary>
    private static HashSet<string> OneShort(int number, int clause)
    {
        var clauses = Clauses[number];
        var mentioned = clauses.Select(c => c.Continent).ToHashSet();
        var owned = Holding(clauses.Select((c, index) => index == clause ? (c.Continent, c.Count - 1) : c));
        owned.UnionWith(Map.Continents.Where(c => !mentioned.Contains(c.Id)).SelectMany(c => c.TerritoryIds));

        return owned;
    }

    private static bool Fulfilled(int number, IReadOnlySet<string> owned) =>
        Objectives.IsFulfilled(new Objective.Occupation(number), owned, Map);

    private static void AssertNotFulfilledOneShort(int number, int clause)
    {
        var owned = OneShort(number, clause);

        Assert.False(
            Fulfilled(number, owned),
            $"Objective {number} must not be fulfilled with one country short in clause {clause} ({owned.Count} countries owned).");
    }

    private static HashSet<string> AllFiftyCountries() => Map.Territories.Select(t => t.Id).ToHashSet();

    // ---- objective 1: Africa + 5 of North America + 4 of Europe ----------------------------------------

    [Fact]
    public void R8_Objective1_AfricaFiveOfNorthAmericaFourOfEurope_IsFulfilled()
    {
        var owned = Holding(Clauses[1]);

        Assert.Equal(6 + 5 + 4, owned.Count); // exactly the quota, nothing spare
        Assert.True(Fulfilled(1, owned));
    }

    [Theory]
    [InlineData(0)] // Africa
    [InlineData(1)] // North America
    [InlineData(2)] // Europe
    public void R8_Objective1_OneCountryShortInAnyClause_IsNotFulfilled(int clause) => AssertNotFulfilledOneShort(1, clause);

    // ---- objective 2: South America + 7 of Europe + 3 connected countries (the triple: 8.2) -------------

    [Fact]
    public void R8_Objective2_SouthAmericaSevenOfEuropeAndAConnectedTriple_IsFulfilled()
    {
        var owned = Holding(Clauses[2]);
        owned.UnionWith(ConnectedTripleInAsia);

        Assert.Equal(6 + 7 + 3, owned.Count);
        Assert.True(Fulfilled(2, owned));
    }

    [Theory]
    [InlineData(0)] // South America
    [InlineData(1)] // Europe
    public void R8_Objective2_OneCountryShortInAnyContinentClause_IsNotFulfilled(int clause) => AssertNotFulfilledOneShort(2, clause);

    // ---- objective 3: Asia + 2 of South America ---------------------------------------------------------

    [Fact]
    public void R8_Objective3_AsiaTwoOfSouthAmerica_IsFulfilled()
    {
        var owned = Holding(Clauses[3]);

        Assert.Equal(15 + 2, owned.Count);
        Assert.True(Fulfilled(3, owned));
    }

    [Theory]
    [InlineData(0)] // Asia
    [InlineData(1)] // South America
    public void R8_Objective3_OneCountryShortInAnyClause_IsNotFulfilled(int clause) => AssertNotFulfilledOneShort(3, clause);

    // ---- objective 4: Europe + 4 of Asia + 2 of South America ------------------------------------------

    [Fact]
    public void R8_Objective4_EuropeFourOfAsiaTwoOfSouthAmerica_IsFulfilled()
    {
        var owned = Holding(Clauses[4]);

        Assert.Equal(9 + 4 + 2, owned.Count);
        Assert.True(Fulfilled(4, owned));
    }

    [Theory]
    [InlineData(0)] // Europe
    [InlineData(1)] // Asia
    [InlineData(2)] // South America
    public void R8_Objective4_OneCountryShortInAnyClause_IsNotFulfilled(int clause) => AssertNotFulfilledOneShort(4, clause);

    // ---- objective 5: North America + 2 of Oceania + 4 of Asia -----------------------------------------

    [Fact]
    public void R8_Objective5_NorthAmericaTwoOfOceaniaFourOfAsia_IsFulfilled()
    {
        var owned = Holding(Clauses[5]);

        Assert.Equal(10 + 2 + 4, owned.Count);
        Assert.True(Fulfilled(5, owned));
    }

    [Theory]
    [InlineData(0)] // North America
    [InlineData(1)] // Oceania
    [InlineData(2)] // Asia
    public void R8_Objective5_OneCountryShortInAnyClause_IsNotFulfilled(int clause) => AssertNotFulfilledOneShort(5, clause);

    // ---- objective 6: 2 Oceania + 2 Africa + 2 South America + 3 Europe + 4 North America + 3 Asia ------

    [Fact]
    public void R8_Objective6_SixPartialContinents_IsFulfilled()
    {
        var owned = Holding(Clauses[6]);

        Assert.Equal(2 + 2 + 2 + 3 + 4 + 3, owned.Count);
        Assert.True(Fulfilled(6, owned));
    }

    [Theory]
    [InlineData(0)] // Oceania
    [InlineData(1)] // Africa
    [InlineData(2)] // South America
    [InlineData(3)] // Europe
    [InlineData(4)] // North America
    [InlineData(5)] // Asia
    public void R8_Objective6_OneCountryShortInAnyClause_IsNotFulfilled(int clause) => AssertNotFulfilledOneShort(6, clause);

    // ---- objective 7: Oceania + North America + 2 of Europe --------------------------------------------

    [Fact]
    public void R8_Objective7_OceaniaNorthAmericaTwoOfEurope_IsFulfilled()
    {
        var owned = Holding(Clauses[7]);

        Assert.Equal(4 + 10 + 2, owned.Count);
        Assert.True(Fulfilled(7, owned));
    }

    [Theory]
    [InlineData(0)] // Oceania
    [InlineData(1)] // North America
    [InlineData(2)] // Europe
    public void R8_Objective7_OneCountryShortInAnyClause_IsNotFulfilled(int clause) => AssertNotFulfilledOneShort(7, clause);

    // ---- objective 8: South America + Africa + 4 of Asia -----------------------------------------------

    [Fact]
    public void R8_Objective8_SouthAmericaAfricaFourOfAsia_IsFulfilled()
    {
        var owned = Holding(Clauses[8]);

        Assert.Equal(6 + 6 + 4, owned.Count);
        Assert.True(Fulfilled(8, owned));
    }

    [Theory]
    [InlineData(0)] // South America
    [InlineData(1)] // Africa
    [InlineData(2)] // Asia
    public void R8_Objective8_OneCountryShortInAnyClause_IsNotFulfilled(int clause) => AssertNotFulfilledOneShort(8, clause);

    // ---- objective 9: Oceania + Africa + 5 of North America --------------------------------------------

    [Fact]
    public void R8_Objective9_OceaniaAfricaFiveOfNorthAmerica_IsFulfilled()
    {
        var owned = Holding(Clauses[9]);

        Assert.Equal(4 + 6 + 5, owned.Count);
        Assert.True(Fulfilled(9, owned));
    }

    [Theory]
    [InlineData(0)] // Oceania
    [InlineData(1)] // Africa
    [InlineData(2)] // North America
    public void R8_Objective9_OneCountryShortInAnyClause_IsNotFulfilled(int clause) => AssertNotFulfilledOneShort(9, clause);

    // ---- surplus, emptiness and the other kinds of objective ---------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void R8_OwningMoreThanTheQuota_StillFulfilsTheObjective_EvenAllFiftyCountries(int number)
    {
        Assert.True(Fulfilled(number, AllFiftyCountries()));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(9)]
    public void R8_OneSpareCountryInEveryClauseContinent_StillFulfilsTheObjective(int number)
    {
        var quota = Clauses[number];
        var owned = Holding(quota.Select(c => (c.Continent, Math.Min(c.Count + 1, CountriesOf(c.Continent).Length))));

        Assert.True(owned.Count > Holding(quota).Count, "There must be a spare country, or the test proves nothing.");
        Assert.True(Fulfilled(number, owned));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void R8_OwningNothing_FulfilsNoOccupationObjective(int number)
    {
        Assert.False(Fulfilled(number, new HashSet<string>()));
    }

    [Fact]
    public void R8_DestructionObjectivesAndNoObjective_AreNotDecidedByTheCountriesOwned_EvenWithAllOfThem()
    {
        var everything = AllFiftyCountries();

        // The counterpart passes in the tests above: all fifty fulfil every occupation objective.
        Assert.True(Fulfilled(1, everything));
        Assert.False(Objectives.IsFulfilled(new Objective.Destruction(10, PlayerColor.Azul), everything, Map));
        Assert.False(Objectives.IsFulfilled(new Objective.Destruction(15, PlayerColor.Magenta), everything, Map));
        Assert.False(Objectives.IsFulfilled(Objective.None, everything, Map));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(-1)]
    public void R8_AnOccupationObjectiveOutsideOneToNine_IsRefused(int number)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Fulfilled(number, AllFiftyCountries()));
    }

    // ---- the catalogue of the fifteen ---------------------------------------------------------------------

    [Fact]
    public void R8_TheCatalogueListsTheFifteenObjectivesInOrder_NineOccupationAndSixDestruction()
    {
        var catalogue = Objectives.Catalogue;

        Assert.Equal(15, catalogue.Count);
        Assert.Equal(
            Enumerable.Range(1, 9).Select(number => (Objective)new Objective.Occupation(number)),
            catalogue.Take(9));
        Assert.Equal(
            new Objective[]
            {
                new Objective.Destruction(10, PlayerColor.Azul),
                new Objective.Destruction(11, PlayerColor.Rojo),
                new Objective.Destruction(12, PlayerColor.Negro),
                new Objective.Destruction(13, PlayerColor.Amarillo),
                new Objective.Destruction(14, PlayerColor.Verde),
                new Objective.Destruction(15, PlayerColor.Magenta),
            },
            catalogue.Skip(9));
    }
}
