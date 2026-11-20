using FsCheck.Xunit;
using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Events;
using Teg.Domain.Rng;
using Teg.Domain.Rules;
using Teg.Domain.State;
using static Teg.Domain.Tests.Support.CombatFixtures;
using static Teg.Domain.Tests.Support.GameFixtures;
using static Teg.Domain.Tests.Support.ReinforcementFixtures;

namespace Teg.Domain.Tests.Combat;

/// <summary>
/// Property of E5 / R§2 / R§6 over RANDOM LEGAL attack sequences on the real board: the engine plays a whole turn of
/// attacks and occupations chosen by a seeded generator (three times out of four the weakest neighbour, otherwise any
/// enemy neighbour) and after EVERY command the board is still valid: 50 countries with exactly one owner each and at
/// least one army, the armies on the board only went down by exactly the losses of the dice, the cards are all still
/// somewhere (inheritance moves them, never loses them), a participant is <c>Eliminated</c> if and only if they own no
/// country, the version grows by one, the RNG advances by the dice rolled, and the conquest counter of the turn counts
/// the conquests.
/// </summary>
public sealed class ArmyConservationTests
{
    private const int MaxCommands = 1500;
    private const int CardsInPlay = 12;

    private sealed record Summary(int Attacks, int Conquests, int Eliminations, int Occupations, int RefusedOccupations);

    /// <summary>A dealt game with both placement rounds played: round 1, the first player to attack.</summary>
    private static GameState Dealt(int participants, long seed) =>
        FinishPlacementRound(FinishPlacementRound(Deal(participants, seed)));

    /// <summary>
    /// <see cref="Dealt"/> with 40 armies in every country of the attacker (so conquests and eliminations happen at
    /// scale), and twelve cards of the map handed out round-robin so that inheritance has something to move.
    /// </summary>
    private static GameState Lopsided(int participants, long seed)
    {
        var state = Dealt(participants, seed);
        var attacker = state.ActiveSeat;
        var strong = state with
        {
            Territories = state.Territories
                .Select(t => t.Owner == attacker ? t with { Armies = 40 } : t)
                .ToValueArray(),
        };

        return WithHands(strong, participants);
    }

    /// <summary>
    /// A game that is nearly over: every defender keeps only its first two countries, the attacker owns the rest with
    /// 40 armies in each, so the attacker eliminates defenders (and inherits their cards) within a few attacks.
    /// </summary>
    private static GameState Endgame(int participants, long seed)
    {
        var state = Lopsided(participants, seed);
        var attacker = state.ActiveSeat;
        var kept = new Dictionary<int, int>();

        var territories = state.Territories.Select(t =>
        {
            if (t.Owner == attacker)
            {
                return t;
            }

            kept[t.Owner] = kept.GetValueOrDefault(t.Owner) + 1;

            return kept[t.Owner] <= 2 ? t : new TerritoryState(t.Id, attacker, Armies: 40);
        }).ToValueArray();

        return state with { Territories = territories };
    }

    private static GameState WithHands(GameState state, int participants)
    {
        var cards = RealMap.Territories.Take(CardsInPlay).Select(t => t.Id).ToArray();

        for (var seat = 0; seat < participants; seat++)
        {
            var hand = cards.Where((_, index) => index % participants == seat).ToArray();
            state = WithHand(state, seat, hand);
        }

        return state;
    }

    private static string[] AllCards(GameState state) =>
        state.Participants.SelectMany(p => p.Cards).Order().ToArray();

    private static List<(string From, string To)> LegalAttacks(GameState state)
    {
        var seat = state.ActiveSeat;

        return state.Territories
            .Where(origin => origin.Owner == seat && origin.Armies >= 2)
            .SelectMany(origin => RealMap.Neighbors(origin.Id)
                .Where(id => state.FindTerritory(id)!.Owner != seat)
                .Order()
                .Select(id => (origin.Id, id)))
            .ToList();
    }

    private static void AssertTheBoardIsValid(GameState state, int participants)
    {
        Assert.Equal(RealMap.Territories.Count, state.Territories.Count);
        Assert.Equal(state.Territories.Count, state.Territories.Select(t => t.Id).Distinct().Count()); // one entry, so one owner, per country
        Assert.All(state.Territories, t =>
        {
            Assert.True(t.Armies >= 1, $"{t.Id} has {t.Armies} armies");
            Assert.InRange(t.Owner, 0, participants - 1);
            Assert.False(state.Participants[t.Owner].Eliminated, $"{t.Id} belongs to an eliminated participant");
        });
        Assert.All(
            state.Participants,
            p => Assert.Equal(state.Territories.All(t => t.Owner != p.Seat), p.Eliminated));
    }

    /// <summary>Plays the turn of the first player until it has no attack left (or the command cap); checks every step.</summary>
    private static Summary Play(GameState start, long choiceSeed)
    {
        var participants = start.Participants.Count;
        var choices = new SeededRng(choiceSeed);
        var cardsAtTheStart = AllCards(start);
        var state = start;
        var (attacks, conquests, eliminations, occupations, refused) = (0, 0, 0, 0, 0);
        var armiesLost = 0;

        AssertTheBoardIsValid(state, participants);

        for (var step = 0; step < MaxCommands; step++)
        {
            GameCommand command;
            if (state.Turn.PendingConquest is { } pending)
            {
                var limit = Math.Min(3, state.FindTerritory(pending.From)!.Armies + state.FindTerritory(pending.To)!.Armies - 1);

                if (choices.NextInt(5) == 0) // now and then, ask for one army too many: it must be refused, nothing changes
                {
                    var tooMany = GameEngine.Apply(state, new MoveAfterConquest(state.ActiveSeat, limit + 1), RealMap);
                    Assert.False(tooMany.IsSuccess);
                    Assert.Contains(
                        tooMany.Error.Code,
                        new[] { RuleViolations.NotEnoughArmies, RuleViolations.OccupationLimitExceeded });
                    refused++;
                }

                command = new MoveAfterConquest(state.ActiveSeat, 1 + choices.NextInt(limit));
                occupations++;
            }
            else
            {
                var legal = LegalAttacks(state);
                if (legal.Count == 0)
                {
                    break;
                }

                var (origin, destination) = choices.NextInt(4) == 0
                    ? legal[choices.NextInt(legal.Count)]
                    : legal.MinBy(a => state.FindTerritory(a.To)!.Armies);
                command = new Attack(state.ActiveSeat, origin, destination);
            }

            var before = state;
            var transition = ApplyOk(before, command, RealMap);
            state = transition.State;

            Assert.Equal(before.Version + 1, state.Version);
            AssertTheBoardIsValid(state, participants);
            Assert.Equal(cardsAtTheStart, AllCards(state));
            Assert.All(state.Participants.Where(p => p.Eliminated), p => Assert.Empty(p.Cards));

            if (command is Attack attack)
            {
                var fight = Assert.Single(transition.Events.OfType<AttackResolved>());
                var (attackerDice, defenderDice) = DiceCounts(before, attack.From, attack.To);
                armiesLost += fight.AttackerLosses + fight.DefenderLosses;

                Assert.Equal(Math.Min(attackerDice, defenderDice), fight.AttackerLosses + fight.DefenderLosses);
                Assert.Equal(TotalArmies(before) - fight.AttackerLosses - fight.DefenderLosses, TotalArmies(state));
                Assert.Equal(before.Rng.Draws + attackerDice + defenderDice, state.Rng.Draws);
                Assert.Equal(ReplayDice(before.Rng, attackerDice, defenderDice).After, state.Rng);

                attacks++;
                conquests += transition.Events.OfType<TerritoryConquered>().Count();
                eliminations += transition.Events.OfType<ParticipantEliminated>().Count();
                Assert.Equal(conquests, state.Turn.ConquestsThisTurn);
                Assert.Equal(
                    transition.Events.OfType<TerritoryConquered>().Any(),
                    state.Turn.PendingConquest is not null);
            }
            else
            {
                Assert.Equal(TotalArmies(before), TotalArmies(state)); // moving armies in changes no total
                Assert.Equal(before.Rng, state.Rng); // and rolls no dice
                Assert.Null(state.Turn.PendingConquest);
            }

            Assert.Equal(TotalArmies(start) - armiesLost, TotalArmies(state));
            Assert.Equal((GamePhase.Attack, start.ActiveSeat), (state.Phase, state.ActiveSeat));
        }

        return new Summary(attacks, conquests, eliminations, occupations, refused);
    }

    // ---- sweeps ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void R6_OverDealtGames_RandomLegalAttacksKeepTheBoardValid_AndOnlyTheLossesTakeArmiesAway(int participants)
    {
        var total = new List<Summary>();

        for (var seed = 1L; seed <= 80; seed++)
        {
            total.Add(Play(Dealt(participants, seed), choiceSeed: seed * 7919));
        }

        Assert.True(total.Sum(s => s.Attacks) > 150, $"Only {total.Sum(s => s.Attacks)} attacks: the sweep would be vacuous.");
        Assert.True(total.Sum(s => s.Conquests) > 30, $"Only {total.Sum(s => s.Conquests)} conquests.");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void R6_OverLopsidedGames_ManyConquestsHappen_AndEveryInvariantHolds(int participants)
    {
        var total = new List<Summary>();

        for (var seed = 1L; seed <= 25; seed++)
        {
            total.Add(Play(Lopsided(participants, seed), choiceSeed: seed * 104729));
        }

        Assert.True(total.Sum(s => s.Conquests) > 300, $"Only {total.Sum(s => s.Conquests)} conquests.");
        Assert.True(total.Sum(s => s.RefusedOccupations) > 50, $"Only {total.Sum(s => s.RefusedOccupations)} refused occupations.");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void R6_OverEndgames_EliminationsHappenAtScale_TheCardsAreInheritedNeverLost_AndEveryInvariantHolds(int participants)
    {
        var total = new List<Summary>();

        for (var seed = 1L; seed <= 25; seed++)
        {
            total.Add(Play(Endgame(participants, seed), choiceSeed: seed * 15485863));
        }

        // Every playout ends when the defenders are gone or cannot be reached: most of them are eliminated.
        Assert.True(
            total.Sum(s => s.Eliminations) >= 25 * (participants - 1) / 2,
            $"Only {total.Sum(s => s.Eliminations)} eliminations.");
    }

    [Property(MaxTest = 60)]
    public bool AnyRandomPlayout_OfAnyDeal_KeepsTheBoardValid(long dealSeed, long choiceSeed, byte participantsPick, bool endgame)
    {
        var participants = 2 + participantsPick % 3;
        var start = endgame ? Endgame(participants, dealSeed) : Lopsided(participants, dealSeed);

        var summary = Play(start, choiceSeed);

        return summary.Attacks > 0; // the generator really attacked; the invariants are asserted inside Play
    }
}
