using Teg.Domain.Map;
using Teg.Domain.Rng;
using Teg.Domain.Rules;
using Teg.Domain.State;
using static Teg.Domain.Tests.Support.GameFixtures;

namespace Teg.Domain.Tests.ObjectiveRules;

/// <summary>
/// E9 / R§8 [ADAPTACIÓN]: objective 2 is South America + 7 of Europe + "3 limítrofes entre sí en cualquier lugar", read as
/// 3 owned countries that are CONNECTED (each adjacent, by land or by bridge, to at least one of the other two) and
/// DISTINCT from the countries already counted for the rest of the objective: all of South America and 7 of Europe.
/// So the triple can never use a South American country, and it may use a European one only when the participant owns
/// more than 7 of them (every European country in the triple leaves one fewer for the quota of 7).
/// </summary>
public sealed class ConnectedCountriesTests
{
    private static readonly MapDefinition Map = RealMap;

    private const string SouthAmerica = "america-del-sur";
    private const string Europe = "europa";

    private static string[] CountriesOf(string continentId) =>
        Map.Continents.Single(c => c.Id == continentId).TerritoryIds.ToArray();

    private static string[] EuropeanCountries(int count) => CountriesOf(Europe).Take(count).ToArray();

    /// <summary>South America entire, the first <paramref name="europeanCountries"/> countries of Europe, and the extras.</summary>
    private static HashSet<string> Owning(int europeanCountries, params string[] extras)
    {
        var owned = CountriesOf(SouthAmerica).Concat(EuropeanCountries(europeanCountries)).ToHashSet();
        owned.UnionWith(extras);

        return owned;
    }

    private static bool Objective2(IReadOnlySet<string> owned) =>
        Objectives.IsFulfilled(new Objective.Occupation(2), owned, Map);

    /// <summary>How many of the three pairs of the triple border each other: 2 or 3 means connected, 0 or 1 does not.</summary>
    private static int BorderingPairs(string a, string b, string c) =>
        new[] { (a, b), (a, c), (b, c) }.Count(pair => Map.AreAdjacent(pair.Item1, pair.Item2));

    // ---- distinct from the countries already counted ---------------------------------------------------

    [Fact]
    public void R8_Objective2_SouthAmericaAndSevenOfEuropeAlone_IsNotFulfilled_TheTripleCannotReuseCountedCountries()
    {
        // Plenty of connected triples exist among these 13 countries, but every one of them would re-count a country.
        var owned = Owning(europeanCountries: 7);

        Assert.Equal(13, owned.Count);
        Assert.False(Objective2(owned));
    }

    [Fact]
    public void R8_Objective2_SouthAmericaAndAllNineOfEuropeAlone_IsNotFulfilled_ThreeEuropeanCountriesLeaveOnlySix()
    {
        var owned = Owning(europeanCountries: 9);

        Assert.Equal(15, owned.Count);
        Assert.False(Objective2(owned)); // the only triples left are European: 9 - 3 = 6 < 7
    }

    [Fact]
    public void R8_Objective2_ATripleThatBorrowsASouthAmericanCountry_IsNotEnough()
    {
        // mexico borders colombia, so {colombia, mexico, california} is connected, but colombia is already counted.
        var owned = Owning(europeanCountries: 7, "mexico", "california");

        Assert.True(Map.AreAdjacent("colombia", "mexico"));
        Assert.True(Map.AreAdjacent("mexico", "california"));
        Assert.False(Objective2(owned));
    }

    [Fact]
    public void R8_Objective2_OnlyTwoCountriesBesidesTheCountedOnes_IsNotFulfilled()
    {
        var owned = Owning(europeanCountries: 7, "china", "gobi");

        Assert.True(Map.AreAdjacent("china", "gobi"));
        Assert.False(Objective2(owned));
    }

    // ---- a connected triple elsewhere --------------------------------------------------------------------

    [Theory]
    [InlineData("china", "gobi", "mongolia")] // a triangle, by land
    [InlineData("kamchatka", "siberia", "taimir")] // a path, by land
    [InlineData("australia", "borneo", "sumatra")] // a path, by bridges only
    [InlineData("alaska", "kamchatka", "japon")] // a path, by bridges only, across two continents
    [InlineData("egipto", "sahara", "etiopia")] // a triangle in Africa
    public void R8_Objective2_ASouthAmericaSevenOfEuropeAndAConnectedTripleElsewhere_IsFulfilled(string a, string b, string c)
    {
        Assert.InRange(BorderingPairs(a, b, c), 2, 3); // the fixture really is a connected triple

        Assert.True(Objective2(Owning(europeanCountries: 7, a, b, c)));
    }

    [Theory]
    [InlineData("china", "gobi", "australia")] // two border each other, the third is far away
    [InlineData("alaska", "china", "sahara")] // none borders another
    [InlineData("china", "india", "siberia")] // two border each other, the third borders neither
    [InlineData("alaska", "yukon", "japon")] // two border each other, the third is across the sea from both
    public void R8_Objective2_ThreeCountriesThatAreNotConnected_AreNotEnough(string a, string b, string c)
    {
        Assert.InRange(BorderingPairs(a, b, c), 0, 1); // the fixture really is NOT connected

        Assert.False(Objective2(Owning(europeanCountries: 7, a, b, c)));
    }

    [Fact]
    public void R8_Objective2_ASpareEuropeanCountryDoesNotMatter_WhenTheTripleIsElsewhere()
    {
        var owned = Owning(europeanCountries: 8, "china", "gobi", "mongolia");

        Assert.True(Objective2(owned));
    }

    // ---- the triple may mix Europe with another continent, only with spare European countries ----------

    [Theory]
    [InlineData("sahara")] // bridge to espana: {sahara, espana, francia}
    [InlineData("aral")] // land border with rusia: {aral, rusia, polonia}
    [InlineData("groenlandia")] // bridge to islandia: {groenlandia, islandia, gran-bretana}
    public void R8_Objective2_WithAllNineOfEurope_ATripleMixingEuropeAndAnotherContinent_IsFulfilled(string extra)
    {
        // Two European countries plus the extra one leave exactly 7: the only valid triples mix the continents.
        var owned = Owning(europeanCountries: 9, extra);

        Assert.Equal(16, owned.Count);
        Assert.True(Objective2(owned));
    }

    [Theory]
    [InlineData("egipto")] // borders no European country
    [InlineData("alaska")]
    [InlineData("china")]
    public void R8_Objective2_WithAllNineOfEurope_AnExtraCountryThatBordersNoEuropeanOne_IsNotEnough(string extra)
    {
        var owned = Owning(europeanCountries: 9, extra);

        Assert.DoesNotContain(Map.Neighbors(extra), id => CountriesOf(Europe).Contains(id)); // the fixture
        Assert.False(Objective2(owned));
    }

    [Fact]
    public void R8_Objective2_WithEightOfEurope_AnExtraCountryAndOneEuropeanOneIsNotEnough_ButTwoExtrasAre()
    {
        // 8 European countries: the triple may take only 1 of them. {sahara, espana, X} needs a second European country (6 < 7)...
        Assert.False(Objective2(Owning(europeanCountries: 8, "sahara")));

        // ...but with a second African country connected to sahara the triple {espana, sahara, egipto} takes just one.
        Assert.True(Objective2(Owning(europeanCountries: 8, "sahara", "egipto")));
    }

    // ---- the rest of the objective still applies ---------------------------------------------------------

    [Fact]
    public void R8_Objective2_ATripleDoesNotMakeUpForMissingSouthAmericaOrEurope()
    {
        var triple = new[] { "china", "gobi", "mongolia" };

        var missingOneSouthAmerican = CountriesOf(SouthAmerica).Skip(1).Concat(EuropeanCountries(7)).Concat(triple).ToHashSet();
        var missingOneOfTheSevenEuropean = CountriesOf(SouthAmerica).Concat(EuropeanCountries(6)).Concat(triple).ToHashSet();

        Assert.False(Objective2(missingOneSouthAmerican));
        Assert.False(Objective2(missingOneOfTheSevenEuropean));
        Assert.True(Objective2(Owning(europeanCountries: 7, triple))); // the same holdings with the full quota
    }

    // ---- against an independent brute-force model -----------------------------------------------------------

    /// <summary>
    /// The definition, spelled out the long way: South America entire, and SOME three countries among the rest of the owned
    /// ones, connected (at least two of their three pairs border), that leave 7 owned European countries uncounted.
    /// </summary>
    private static bool BruteForceObjective2(HashSet<string> owned)
    {
        var southAmerica = CountriesOf(SouthAmerica);
        var europe = CountriesOf(Europe).ToHashSet();
        if (!southAmerica.All(owned.Contains))
        {
            return false;
        }

        var candidates = owned.Except(southAmerica).Order(StringComparer.Ordinal).ToArray();
        var ownedEurope = owned.Count(europe.Contains);
        for (var i = 0; i < candidates.Length; i++)
        {
            for (var j = i + 1; j < candidates.Length; j++)
            {
                for (var k = j + 1; k < candidates.Length; k++)
                {
                    var triple = new[] { candidates[i], candidates[j], candidates[k] };
                    if (BorderingPairs(triple[0], triple[1], triple[2]) >= 2 && ownedEurope - triple.Count(europe.Contains) >= 7)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    [Fact]
    public void R8_Objective2_AgreesWithABruteForceModel_OnManyRandomHoldings()
    {
        var otherCountries = Map.Territories
            .Select(t => t.Id)
            .Except(CountriesOf(SouthAmerica))
            .Except(CountriesOf(Europe))
            .ToArray();
        int fulfilled = 0, notFulfilled = 0;

        for (var seed = 0L; seed < 1500; seed++)
        {
            var rng = new SeededRng(seed);
            var europeanCountries = CountriesOf(Europe).ToArray();
            rng.Shuffle(europeanCountries);
            var owned = CountriesOf(SouthAmerica)
                .Concat(europeanCountries.Take(7 + rng.NextInt(3))) // 7, 8 or 9 European countries
                .ToHashSet();
            var chanceOfOwningAnother = new[] { 3, 6, 10, 15 }[rng.NextInt(4)]; // sparse holdings: the triple is borderline
            owned.UnionWith(otherCountries.Where(_ => rng.NextInt(100) < chanceOfOwningAnother));

            var expected = BruteForceObjective2(owned);

            Assert.True(
                expected == Objective2(owned),
                $"seed {seed}: the model says {expected}; owned = {string.Join(",", owned.Order(StringComparer.Ordinal))}");

            if (expected)
            {
                fulfilled++;
            }
            else
            {
                notFulfilled++;
            }
        }

        // Not vacuous: both answers happen plenty of times (measured over these 1500 holdings: 273 and 1227).
        Assert.True(fulfilled > 150, $"fulfilled = {fulfilled}");
        Assert.True(notFulfilled > 600, $"notFulfilled = {notFulfilled}");
    }
}
