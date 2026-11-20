using Teg.Domain.Map;
using Teg.Domain.Rng;
using Teg.Domain.Rules;
using Teg.Domain.Setup;
using Teg.Domain.State;
using static Teg.Domain.Tests.Support.GameFixtures;

namespace Teg.Domain.Tests.ObjectiveRules;

/// <summary>
/// E9 / R§2, R§8: the ASSIGNMENT of the destruction objectives 10 to 15 (azul, rojo, negro, amarillo, verde, magenta). If the
/// drawn colour is the participant's OWN or NOBODY plays it, the target becomes the colour of the "jugador de la derecha":
/// the participant who plays immediately BEFORE them in table order, circular, that is seat s-1 (the last seat for seat 0).
/// The final target is fixed when the objectives are dealt, and the objective keeps the number that was drawn. (Detecting
/// the kill and the fall back to the common objective belong to the victory phase.)
/// </summary>
public sealed class DestructionObjectiveAssignmentTests
{
    private static readonly MapDefinition Map = RealMap;

    private static readonly PlayerColor[] Table = [PlayerColor.Azul, PlayerColor.Rojo, PlayerColor.Negro, PlayerColor.Amarillo];

    /// <summary>The destruction objective that names each colour, as printed in R§8 (10 azul ... 15 magenta).</summary>
    private static int NumberFor(PlayerColor colour) => colour switch
    {
        PlayerColor.Azul => 10,
        PlayerColor.Rojo => 11,
        PlayerColor.Negro => 12,
        PlayerColor.Amarillo => 13,
        PlayerColor.Verde => 14,
        PlayerColor.Magenta => 15,
        _ => throw new ArgumentOutOfRangeException(nameof(colour)),
    };

    private static IReadOnlySet<string>[] Nobody() =>
        Enumerable.Range(0, 4).Select(_ => (IReadOnlySet<string>)new HashSet<string>()).ToArray();

    /// <summary>
    /// A draw that gives <paramref name="seat"/> the objective <paramref name="number"/> and every other seat an occupation
    /// objective (1 to 4), which no rule here touches.
    /// </summary>
    private static int[] DrawGiving(int seat, int number) =>
        Enumerable.Range(0, 4).Select(s => s == seat ? number : 1 + s).ToArray();

    private static Objective DealtTo(int seat, PlayerColor[] colours, int number) =>
        Objectives.Assign(colours, Nobody(), DrawGiving(seat, number), Map)[seat];

    // ---- the drawn colour is the participant's own ---------------------------------------------------------

    [Theory]
    [InlineData(0, 3)] // the right-hand player of seat 0 is the LAST seat
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    public void R8_Destruction_TheOwnColour_BecomesTheColourOfThePlayerToTheRight(int seat, int rightHandSeat)
    {
        var dealt = DealtTo(seat, Table, NumberFor(Table[seat]));

        // The number is the one that was drawn (it names the participant's own colour); the target is the right-hand colour.
        Assert.Equal(new Objective.Destruction(NumberFor(Table[seat]), Table[rightHandSeat]), dealt);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    public void R8_Destruction_TheRightHandPlayerFollowsTheSeats_NotTheOrderOfTheColours(int seat, int rightHandSeat)
    {
        PlayerColor[] colours = [PlayerColor.Verde, PlayerColor.Magenta, PlayerColor.Azul, PlayerColor.Rojo];

        var dealt = DealtTo(seat, colours, NumberFor(colours[seat]));

        Assert.Equal(new Objective.Destruction(NumberFor(colours[seat]), colours[rightHandSeat]), dealt);
    }

    // ---- nobody plays the drawn colour -------------------------------------------------------------------------

    [Theory]
    [InlineData(0, PlayerColor.Magenta, 3, PlayerColor.Amarillo)] // wraps around to the last seat
    [InlineData(1, PlayerColor.Verde, 0, PlayerColor.Azul)]
    [InlineData(2, PlayerColor.Verde, 1, PlayerColor.Rojo)]
    [InlineData(3, PlayerColor.Magenta, 2, PlayerColor.Negro)]
    public void R8_Destruction_AColourNobodyPlays_BecomesTheColourOfThePlayerToTheRight(
        int seat,
        PlayerColor absent,
        int rightHandSeat,
        PlayerColor expectedTarget)
    {
        Assert.DoesNotContain(absent, Table); // the fixture: with four players, verde and magenta are never played
        Assert.Equal(expectedTarget, Table[rightHandSeat]);

        var dealt = DealtTo(seat, Table, NumberFor(absent));

        Assert.Equal(new Objective.Destruction(NumberFor(absent), expectedTarget), dealt);
    }

    [Fact]
    public void R8_Destruction_WithAnotherSetOfColours_TheAbsentOnesAreTheOthers()
    {
        PlayerColor[] colours = [PlayerColor.Verde, PlayerColor.Magenta, PlayerColor.Azul, PlayerColor.Rojo];

        // Now negro and amarillo are the colours nobody plays: seat 2 (azul) drawing negro aims at seat 1 (magenta)...
        Assert.Equal(new Objective.Destruction(12, PlayerColor.Magenta), DealtTo(2, colours, 12));
        Assert.Equal(new Objective.Destruction(13, PlayerColor.Rojo), DealtTo(0, colours, 13));

        // ...while verde and magenta, absent in the other set, are played here and stay as drawn.
        Assert.Equal(new Objective.Destruction(14, PlayerColor.Verde), DealtTo(2, colours, 14));
        Assert.Equal(new Objective.Destruction(15, PlayerColor.Magenta), DealtTo(3, colours, 15));
    }

    // ---- the drawn colour is played by somebody else: it stays ------------------------------------------------

    [Theory]
    [InlineData(0, PlayerColor.Rojo)]
    [InlineData(0, PlayerColor.Amarillo)]
    [InlineData(1, PlayerColor.Azul)]
    [InlineData(2, PlayerColor.Amarillo)]
    [InlineData(3, PlayerColor.Negro)]
    [InlineData(3, PlayerColor.Azul)]
    public void R8_Destruction_AColourAnotherParticipantPlays_StaysAsDrawn_EvenIfItIsNotTheRightHandOne(int seat, PlayerColor played)
    {
        Assert.Contains(played, Table);
        Assert.NotEqual(Table[seat], played);

        var dealt = DealtTo(seat, Table, NumberFor(played));

        Assert.Equal(new Objective.Destruction(NumberFor(played), played), dealt);
    }

    // ---- every seat x every colour, against the rule written as a formula --------------------------------------

    public static TheoryData<int, PlayerColor> EverySeatAndEveryColour()
    {
        var data = new TheoryData<int, PlayerColor>();
        foreach (var seat in Enumerable.Range(0, 4))
        {
            foreach (var colour in Enum.GetValues<PlayerColor>())
            {
                data.Add(seat, colour);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EverySeatAndEveryColour))]
    public void R8_Destruction_TheTargetIsTheDrawnColourUnlessItIsOwnOrAbsent_ThenTheRightHandColour(int seat, PlayerColor drawn)
    {
        var rightHand = Table[(seat + Table.Length - 1) % Table.Length];
        var expectedTarget = drawn != Table[seat] && Table.Contains(drawn) ? drawn : rightHand;

        var dealt = DealtTo(seat, Table, NumberFor(drawn));

        Assert.Equal(new Objective.Destruction(NumberFor(drawn), expectedTarget), dealt);
        Assert.NotEqual(Table[seat], ((Objective.Destruction)dealt).Target); // nobody is ever sent after themselves
    }

    [Fact]
    public void R8_Destruction_AllFourParticipantsDrawingTheirOwnColour_EachAimsAtTheRightHandPlayer()
    {
        // Every target is decided on the real colours at the table, not on one another's replacement.
        var dealt = Objectives.Assign(Table, Nobody(), [10, 11, 12, 13], Map);

        Assert.Equal(
            new Objective[]
            {
                new Objective.Destruction(10, PlayerColor.Amarillo),
                new Objective.Destruction(11, PlayerColor.Azul),
                new Objective.Destruction(12, PlayerColor.Rojo),
                new Objective.Destruction(13, PlayerColor.Negro),
            },
            dealt);
    }

    [Fact]
    public void R8_Destruction_ANumberUsedByAReplacedObjective_IsNotDealtAgain()
    {
        // 14 (verde) is replaced for seat 0, but the objective dealt is still number 14: a second 14 in the draw is skipped.
        var dealt = Objectives.Assign(Table, Nobody(), [14, 14, 2, 3, 4], Map);

        Assert.Equal(new Objective.Destruction(14, PlayerColor.Amarillo), dealt[0]);
        Assert.Equal([14, 2, 3, 4], dealt.Select(o => o is Objective.Destruction d ? d.Number : ((Objective.Occupation)o).Number).ToArray());
    }

    // ---- the game that is created --------------------------------------------------------------------------------

    [Fact]
    public void R8_Create_EveryDestructionTarget_IsAPlayedColourThatIsNotTheOwners_FixedByTheRule()
    {
        int own = 0, absent = 0, kept = 0;

        for (var seed = 0L; seed < 500; seed++)
        {
            // A different set of four colours, in a different order, for each seed.
            var shuffled = Enum.GetValues<PlayerColor>().ToArray();
            new SeededRng(seed + 1_000_000).Shuffle(shuffled);
            PlayerColor[] colours = [.. shuffled.Take(4)];
            var options = new GameOptions(
                colours.Select((colour, seat) => new ParticipantSetup($"Jugador {seat + 1}", ParticipantKind.Human, colour)).ToArray());

            var state = GameSetup.Create(options, seed, Map).Value;

            foreach (var participant in state.Participants.Where(p => p.Objective is Objective.Destruction))
            {
                var objective = (Objective.Destruction)participant.Objective;
                var drawn = Enum.GetValues<PlayerColor>().Single(colour => NumberFor(colour) == objective.Number);
                var rightHand = colours[(participant.Seat + colours.Length - 1) % colours.Length];

                if (drawn == participant.Color)
                {
                    own++;
                    Assert.Equal(rightHand, objective.Target);
                }
                else if (!colours.Contains(drawn))
                {
                    absent++;
                    Assert.Equal(rightHand, objective.Target);
                }
                else
                {
                    kept++;
                    Assert.Equal(drawn, objective.Target);
                }

                Assert.Contains(objective.Target, colours);
                Assert.NotEqual(participant.Color, objective.Target);
            }
        }

        // Not vacuous: each of the three situations came up many times (measured over these 500 games: 123, 276 and 423).
        Assert.True(own > 80, $"own colour drawn {own} times");
        Assert.True(absent > 200, $"absent colour drawn {absent} times");
        Assert.True(kept > 300, $"another player's colour drawn {kept} times");
    }
}
