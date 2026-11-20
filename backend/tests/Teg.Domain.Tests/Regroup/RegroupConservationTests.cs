using FsCheck.Xunit;
using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Events;
using Teg.Domain.Rng;
using Teg.Domain.Rules;
using Teg.Domain.State;
using static Teg.Domain.Tests.Support.CombatFixtures;
using static Teg.Domain.Tests.Support.GameFixtures;

namespace Teg.Domain.Tests.Regrouping;

/// <summary>
/// Property of E6 / R§6 over RANDOM regroup sequences (legal and illegal) on the real board. A seeded generator proposes
/// hundreds of regroups; the engine's answer is compared, move by move, with an INDEPENDENT model of the rule (only the
/// armies at the start of the phase and how many each country has sent out so far: a regroup is allowed exactly when both
/// countries are the participant's, they are different and adjacent, the count is at least one and not more than
/// <c>start - 1 - sent out</c>). After EVERY command: the total of armies on the board is unchanged, every country has at
/// least one army, no owner changed, each country holds exactly <c>start - sent + received</c>, each quota is
/// <c>start - 1 - sent</c>, no country ever sent more than <c>start - 1</c> (what arrived never moved on), the version
/// grew by one, no dice were rolled, and a refusal changed nothing.
/// </summary>
public sealed class RegroupConservationTests
{
    private const int AttemptsPerGame = 250;

    private sealed record Summary(int Attempts, int Accepted, int Refused, int ChainingRefused);

    /// <summary>
    /// A game in Ataque on the real board where seat <c>s</c> (the active one) owns about 60% of the countries and every
    /// other seat owns at least one; every country holds 1 to 9 armies. All of it decided by <paramref name="seed"/>.
    /// </summary>
    private static GameState RandomBoard(int participants, long seed)
    {
        var rng = new SeededRng(seed);
        var active = rng.NextInt(participants);
        var template = InPlay(participants, GamePhase.Attack, round: 2, activeSeat: active);

        return template with
        {
            Territories = template.Territories
                .Select((t, index) => new TerritoryState(
                    t.Id,
                    Owner: index < participants ? index : rng.NextInt(10) < 6 ? active : rng.NextInt(participants),
                    Armies: 1 + rng.NextInt(9)))
                .ToValueArray(),
        };
    }

    private static Summary Play(GameState attackState, long choiceSeed)
    {
        var choices = new SeededRng(choiceSeed);
        var seat = attackState.ActiveSeat;
        var state = ApplyOk(attackState, new StartRegroup(seat), RealMap).State;

        var ids = RealMap.Territories.Select(t => t.Id).ToArray();
        var owner = state.Territories.ToDictionary(t => t.Id, t => t.Owner);
        var start = state.Territories.ToDictionary(t => t.Id, t => t.Armies); // armies when Reagrupe began
        var sent = ids.ToDictionary(id => id, _ => 0);
        var received = ids.ToDictionary(id => id, _ => 0);
        var owned = ids.Where(id => owner[id] == seat).ToArray();
        var totalAtTheStart = TotalArmies(state);
        var (accepted, refused, chainingRefused) = (0, 0, 0);

        AssertTheBoard(state, owner, start, sent, received, seat, totalAtTheStart);

        for (var attempt = 0; attempt < AttemptsPerGame; attempt++)
        {
            var origin = choices.NextInt(10) < 9 ? owned[choices.NextInt(owned.Length)] : ids[choices.NextInt(ids.Length)];
            var neighbours = RealMap.Neighbors(origin).Order().ToArray();
            var pick = choices.NextInt(20);
            var destination = pick < 16 ? neighbours[choices.NextInt(neighbours.Length)]
                : pick < 19 ? ids[choices.NextInt(ids.Length)]
                : origin;
            var quotaLeft = start[origin] - 1 - sent[origin];
            var armies = choices.NextInt(8) == 0
                ? choices.NextInt(3) - 1 // -1, 0 or 1: now and then something that is not even a move
                : 1 + choices.NextInt(Math.Max(1, quotaLeft + 2)); // 1 to quota + 1: often one too many

            var modelAllows = owner[origin] == seat
                && owner[destination] == seat
                && origin != destination
                && RealMap.AreAdjacent(origin, destination)
                && armies >= 1
                && armies <= quotaLeft;

            var before = state;
            var result = GameEngine.Apply(before, new Regroup(seat, origin, destination, armies), RealMap);

            Assert.True(
                modelAllows == result.IsSuccess,
                $"{origin} -> {destination} x{armies} (quota left {quotaLeft}): the model says {modelAllows}, the engine "
                + (result.IsSuccess ? "accepted it." : $"refused it with {result.Error.Code}."));

            if (result.IsSuccess)
            {
                state = result.Value.State;
                sent[origin] += armies;
                received[destination] += armies;
                accepted++;

                GameEvent[] expectedEvents = [new ArmiesRegrouped(seat, origin, destination, armies)];
                Assert.Equal(expectedEvents, result.Value.Events.ToArray());
                Assert.Equal(before.Version + 1, state.Version);
            }
            else
            {
                refused++;
                Assert.False(string.IsNullOrWhiteSpace(result.Error.Code));

                // The chaining case: everything is fine except that the armies asked for would come from what ARRIVED.
                var armiesAreThere = armies >= 1 && armies <= before.FindTerritory(origin)!.Armies - 1;
                if (owner[origin] == seat && owner[destination] == seat && origin != destination
                    && RealMap.AreAdjacent(origin, destination) && armiesAreThere)
                {
                    Assert.Equal(RuleViolations.RegroupQuotaExceeded, result.Error.Code);
                    chainingRefused++;
                }
            }

            Assert.Equal(before.Rng, state.Rng); // moving armies rolls no dice
            AssertTheBoard(state, owner, start, sent, received, seat, totalAtTheStart);
        }

        Assert.All(owned, id => Assert.InRange(sent[id], 0, start[id] - 1)); // never more than armies at the start - 1

        var ended = ApplyOk(state, new EndTurn(seat), RealMap).State; // the phase is optional and the next turn starts clean
        Assert.Equal(totalAtTheStart, TotalArmies(ended));
        Assert.Empty(ended.Turn.RegroupQuotas);

        return new Summary(AttemptsPerGame, accepted, refused, chainingRefused);
    }

    private static void AssertTheBoard(
        GameState state,
        Dictionary<string, int> owner,
        Dictionary<string, int> start,
        Dictionary<string, int> sent,
        Dictionary<string, int> received,
        int seat,
        int totalAtTheStart)
    {
        Assert.Equal(totalAtTheStart, TotalArmies(state));
        Assert.Equal((GamePhase.Regroup, seat), (state.Phase, state.ActiveSeat));
        Assert.All(state.Territories, t =>
        {
            Assert.True(t.Armies >= 1, $"{t.Id} has {t.Armies} armies");
            Assert.Equal(owner[t.Id], t.Owner);
            Assert.Equal(start[t.Id] - sent[t.Id] + received[t.Id], t.Armies);
            Assert.Equal(owner[t.Id] == seat ? start[t.Id] - 1 - sent[t.Id] : 0, state.Turn.RegroupQuotaOf(t.Id));
        });
    }

    // ---- sweeps ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void R6_OverRandomBoards_EveryRegroupAgreesWithTheModel_AndTheBoardStaysValid(int participants)
    {
        var total = new List<Summary>();

        for (var seed = 1L; seed <= 40; seed++)
        {
            total.Add(Play(RandomBoard(participants, seed), choiceSeed: seed * 7919));
        }

        // Measured per row of 40 games x 250 attempts: about 1600-2100 accepted, 7900-8400 refused, 1400-1850 chained.
        Assert.True(total.Sum(s => s.Accepted) > 1200, $"Only {total.Sum(s => s.Accepted)} regroups were accepted: the sweep would be vacuous.");
        Assert.True(total.Sum(s => s.Refused) > 5000, $"Only {total.Sum(s => s.Refused)} regroups were refused.");
        Assert.True(
            total.Sum(s => s.ChainingRefused) > 800,
            $"Only {total.Sum(s => s.ChainingRefused)} chained regroups were refused: no-chaining would be barely exercised.");
    }

    [Property(MaxTest = 60)]
    public bool AnyRandomRegroupSequence_OfAnyBoard_KeepsEveryInvariant(long boardSeed, long choiceSeed, byte participantsPick)
    {
        var participants = 2 + participantsPick % 3;

        var summary = Play(RandomBoard(participants, boardSeed), choiceSeed); // the invariants are asserted inside Play

        return summary.Accepted > 0; // the generator really moved armies
    }
}
