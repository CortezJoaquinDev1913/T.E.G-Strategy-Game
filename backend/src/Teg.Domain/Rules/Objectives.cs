using Teg.Domain.Map;
using Teg.Domain.Rng;
using Teg.Domain.State;

namespace Teg.Domain.Rules;

/// <summary>
/// The objectives of R§8: the catalogue of the fifteen and the evaluation of the occupation ones (1 to 9). Pure: the
/// answer depends only on the countries owned and the map. The continent clauses are written with the continent ids of
/// docs/mapa.json; how many countries a whole continent has is read from the map.
/// </summary>
public static class Objectives
{
    /// <summary>R§8: objectives 1 to 9 ask for countries to be owned.</summary>
    public const int OccupationCount = 9;

    /// <summary>R§3, R§8: only a game of this many participants has secret objectives (2 and 3 play for the fifty countries).</summary>
    public const int SecretObjectiveParticipants = 4;

    private const string NorthAmerica = "america-del-norte";
    private const string SouthAmerica = "america-del-sur";
    private const string Europe = "europa";
    private const string Africa = "africa";
    private const string Asia = "asia";
    private const string Oceania = "oceania";

    /// <summary>The colour that objectives 10 to 15 ask to destroy, in the order of R§8.</summary>
    private static readonly PlayerColor[] DestructionTargets =
        [PlayerColor.Azul, PlayerColor.Rojo, PlayerColor.Negro, PlayerColor.Amarillo, PlayerColor.Verde, PlayerColor.Magenta];

    /// <summary>One continent clause: <c>Countries</c> of the continent, or all of it when <c>null</c>.</summary>
    private sealed record Requirement(string ContinentId, int? Countries);

    /// <summary>
    /// What an occupation objective asks: its continent clauses and, for objective 2 only, three more countries bordering
    /// one another anywhere (R§8, [ADAPTACIÓN]).
    /// </summary>
    private sealed record Goal(Requirement[] Continents, bool ConnectedTriple = false);

    private static Requirement Whole(string continentId) => new(continentId, null);

    private static Requirement Some(string continentId, int countries) => new(continentId, countries);

    private static readonly IReadOnlyDictionary<int, Goal> OccupationGoals = new Dictionary<int, Goal>
    {
        [1] = new([Whole(Africa), Some(NorthAmerica, 5), Some(Europe, 4)]),
        [2] = new([Whole(SouthAmerica), Some(Europe, 7)], ConnectedTriple: true),
        [3] = new([Whole(Asia), Some(SouthAmerica, 2)]),
        [4] = new([Whole(Europe), Some(Asia, 4), Some(SouthAmerica, 2)]),
        [5] = new([Whole(NorthAmerica), Some(Oceania, 2), Some(Asia, 4)]),
        [6] = new([Some(Oceania, 2), Some(Africa, 2), Some(SouthAmerica, 2), Some(Europe, 3), Some(NorthAmerica, 4), Some(Asia, 3)]),
        [7] = new([Whole(Oceania), Whole(NorthAmerica), Some(Europe, 2)]),
        [8] = new([Whole(SouthAmerica), Whole(Africa), Some(Asia, 4)]),
        [9] = new([Whole(Oceania), Whole(Africa), Some(NorthAmerica, 5)]),
    };

    /// <summary>The fifteen objectives of R§8, numbered 1 to 15 (the destruction ones with the colour the table names).</summary>
    public static IReadOnlyList<Objective> Catalogue { get; } = BuildCatalogue();

    private static IReadOnlyList<Objective> BuildCatalogue() =>
    [
        .. Enumerable.Range(1, OccupationCount).Select(number => (Objective)new Objective.Occupation(number)),
        .. DestructionTargets.Select((target, index) => new Objective.Destruction(OccupationCount + 1 + index, target)),
    ];

    /// <summary>
    /// Whether owning <paramref name="ownedTerritoryIds"/> fulfils <paramref name="objective"/>. Only occupation objectives
    /// are decided by the countries owned (owning more than the quota is fine). A destruction objective is decided by
    /// destroying a colour (the victory rules look for that kill), and "no objective" and the common objective count
    /// countries on their own terms (also the victory rules), so all three answer <c>false</c> here.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">An occupation objective outside 1 to 9.</exception>
    public static bool IsFulfilled(Objective objective, IReadOnlySet<string> ownedTerritoryIds, MapDefinition map) =>
        objective switch
        {
            Objective.Occupation occupation => IsAchieved(GoalOf(occupation), ownedTerritoryIds, map),
            _ => false,
        };

    /// <summary>
    /// The order in which the fifteen objectives are drawn: one Fisher-Yates shuffle of the numbers 1 to 15 with the game's
    /// generator (14 draws at least), so no objective can come up twice.
    /// </summary>
    public static IReadOnlyList<int> DrawSequence(SeededRng rng)
    {
        var numbers = Enumerable.Range(1, Catalogue.Count).ToArray();
        rng.Shuffle(numbers);

        return numbers;
    }

    /// <summary>
    /// Deals one objective to each seat, in seat order, taking the next numbers of <paramref name="drawSequence"/>
    /// (normally <see cref="DrawSequence"/>). <paramref name="colours"/> and <paramref name="ownedBySeat"/> are indexed by
    /// seat. A drawn objective the seat's own countries already fulfil is redrawn (R§8): the next number is taken and the
    /// rejected one is out, handed to nobody. A number is never dealt twice, even if the sequence repeats it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The sequence ran out before every seat had an objective.</exception>
    public static IReadOnlyList<Objective> Assign(
        IReadOnlyList<PlayerColor> colours,
        IReadOnlyList<IReadOnlySet<string>> ownedBySeat,
        IEnumerable<int> drawSequence,
        MapDefinition map)
    {
        var dealt = new HashSet<int>();
        using var draws = drawSequence.GetEnumerator();
        var objectives = new List<Objective>(colours.Count);

        for (var seat = 0; seat < colours.Count; seat++)
        {
            objectives.Add(WithFinalTarget(DrawFor(ownedBySeat[seat], draws, dealt, map), seat, colours));
        }

        return objectives;
    }

    /// <summary>
    /// R§2, R§8: a destruction objective whose colour is the participant's own, or one nobody plays, aims instead at the
    /// "jugador de la derecha": the participant who plays immediately before, in table order (registration order, circular),
    /// so seat 0's is the last seat. Decided here, once, on the colours at the table; the objective keeps its number.
    /// </summary>
    private static Objective WithFinalTarget(Objective drawn, int seat, IReadOnlyList<PlayerColor> colours)
    {
        if (drawn is not Objective.Destruction destruction)
        {
            return drawn;
        }

        var unusable = destruction.Target == colours[seat] || !colours.Contains(destruction.Target);

        return unusable ? destruction with { Target = colours[(seat + colours.Count - 1) % colours.Count] } : destruction;
    }

    private static Objective DrawFor(IReadOnlySet<string> owned, IEnumerator<int> draws, HashSet<int> dealt, MapDefinition map)
    {
        while (draws.MoveNext())
        {
            var number = draws.Current;
            if (dealt.Contains(number))
            {
                continue;
            }

            var candidate = Catalogue[number - 1]; // the catalogue is numbered 1 to 15 in order
            if (IsFulfilled(candidate, owned, map))
            {
                continue;
            }

            dealt.Add(number);

            return candidate;
        }

        throw new InvalidOperationException("The draw ran out of objectives before every participant had one.");
    }

    private static Goal GoalOf(Objective.Occupation occupation) =>
        OccupationGoals.TryGetValue(occupation.Number, out var goal)
            ? goal
            : throw new ArgumentOutOfRangeException(
                nameof(occupation),
                occupation.Number,
                $"Occupation objectives are numbered 1 to {OccupationCount}.");

    private static bool IsAchieved(Goal goal, IReadOnlySet<string> owned, MapDefinition map)
    {
        // Countries owned beyond the quota of each continent clause: the room a connected triple has to take from there.
        var spareByContinent = new Dictionary<string, int>();
        foreach (var requirement in goal.Continents)
        {
            var continent = map.Continents.Single(c => c.Id == requirement.ContinentId);
            var spare = continent.TerritoryIds.Count(owned.Contains) - (requirement.Countries ?? continent.TerritoryIds.Count);
            if (spare < 0)
            {
                return false;
            }

            spareByContinent[continent.Id] = spare;
        }

        return !goal.ConnectedTriple || HasConnectedTripleBesideTheQuota(owned, spareByContinent, map);
    }

    /// <summary>
    /// Looks for three owned countries, connected and distinct from the ones the continent clauses already count: the
    /// triple may take from a clause's continent only as many countries as the participant owns beyond that clause's quota
    /// (none for a whole-continent clause), and from any other continent as many as it likes. A connected triple always
    /// has a country that borders the other two (the centre of a path or any corner of a triangle), so the search walks
    /// every owned country with every pair of its owned neighbours (land borders and bridges alike).
    /// </summary>
    private static bool HasConnectedTripleBesideTheQuota(
        IReadOnlySet<string> owned,
        IReadOnlyDictionary<string, int> spareByContinent,
        MapDefinition map)
    {
        foreach (var centre in owned)
        {
            var around = map.Neighbors(centre).Where(owned.Contains).ToArray();
            for (var first = 0; first < around.Length; first++)
            {
                for (var second = first + 1; second < around.Length; second++)
                {
                    string[] triple = [centre, around[first], around[second]];
                    var fits = triple
                        .GroupBy(id => map.Territory(id).ContinentId)
                        .All(group => !spareByContinent.TryGetValue(group.Key, out var spare) || group.Count() <= spare);
                    if (fits)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
