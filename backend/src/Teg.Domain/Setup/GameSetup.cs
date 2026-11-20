using Teg.Domain.Collections;
using Teg.Domain.Map;
using Teg.Domain.Rng;
using Teg.Domain.State;

namespace Teg.Domain.Setup;

/// <summary>
/// Creates a game: validates the participants (R§2) and performs the initial deal (R§3).
/// Pure: no I/O, no clock, no static RNG; everything random comes from the seed.
/// </summary>
public static class GameSetup
{
    public const int MinParticipants = 2;
    public const int MaxParticipants = 4;

    /// <summary>
    /// Army pools of the two initial placement rounds (round 1, round 2): 6 + 6 for two participants,
    /// 5 + 3 for three or four (R§3).
    /// </summary>
    public static IReadOnlyList<int> PlacementPools(int participantCount) => participantCount switch
    {
        2 => [6, 6],
        3 or 4 => [5, 3],
        _ => throw new ArgumentOutOfRangeException(
            nameof(participantCount),
            participantCount,
            $"A game has {MinParticipants} to {MaxParticipants} participants."),
    };

    /// <summary>
    /// Validates the options, ranks the participants with one die each (ties re-rolled among the tied),
    /// shuffles and deals the countries, each with one army of its owner. The first player (rank 1) is the
    /// active participant of initial placement round 1.
    /// </summary>
    public static Result<GameState, RuleViolation> Create(GameOptions options, long seed, MapDefinition map)
    {
        var violation = Validate(options.Participants);
        if (violation is not null)
        {
            return Result<GameState, RuleViolation>.Fail(violation);
        }

        var rng = new SeededRng(seed);
        var ranking = InitialRanking.Determine(options.Participants.Count, rng.RollDie);
        var territories = DealCountries(map, ranking, rng);

        return Result<GameState, RuleViolation>.Ok(new GameState(
            Version: 0,
            Rng: rng.State,
            Phase: GamePhase.InitialPlacement,
            PlacementRound: 1,
            Round: 0,
            FirstSeat: ranking[0],
            ActiveSeat: ranking[0],
            Ranking: ranking.ToValueArray(),
            Participants: options.Participants
                .Select((setup, seat) => new Participant(seat, setup.Name, setup.Kind, setup.Color))
                .ToValueArray(),
            Territories: territories));
    }

    /// <summary>
    /// Shuffles the countries and deals them one by one in rank order (rank 1, rank 2, ... and around again),
    /// so the extra countries of an uneven split (50 / 3, 50 / 4) land on the top ranks (R§3).
    /// </summary>
    private static ValueArray<TerritoryState> DealCountries(MapDefinition map, IReadOnlyList<int> ranking, SeededRng rng)
    {
        var shuffledIds = map.Territories.Select(t => t.Id).ToArray();
        rng.Shuffle(shuffledIds);

        var ownerById = shuffledIds
            .Select((id, position) => (Id: id, Owner: ranking[position % ranking.Count]))
            .ToDictionary(deal => deal.Id, deal => deal.Owner);

        return map.Territories
            .Select(t => new TerritoryState(t.Id, ownerById[t.Id], Armies: 1))
            .ToValueArray();
    }

    private static RuleViolation? Validate(IReadOnlyList<ParticipantSetup> participants)
    {
        if (participants.Count is < MinParticipants or > MaxParticipants)
        {
            return new RuleViolation(
                SetupViolations.InvalidParticipantCount,
                $"A game has {MinParticipants} to {MaxParticipants} participants, got {participants.Count}.");
        }

        if (participants.Any(p => string.IsNullOrWhiteSpace(p.Name)))
        {
            return new RuleViolation(SetupViolations.BlankName, "Every participant needs a name.");
        }

        if (participants.Any(p => !Enum.IsDefined(p.Color)))
        {
            return new RuleViolation(
                SetupViolations.InvalidColor,
                "Colours must be one of: azul, rojo, negro, amarillo, verde, magenta.");
        }

        if (participants.Select(p => p.Color).Distinct().Count() != participants.Count)
        {
            return new RuleViolation(SetupViolations.DuplicateColor, "Each participant needs a different colour.");
        }

        if (!participants.Any(p => p.Kind == ParticipantKind.Human))
        {
            return new RuleViolation(SetupViolations.NoHumanParticipant, "At least one participant must be human.");
        }

        return null;
    }
}
