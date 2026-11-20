using Teg.Domain.Map;
using Teg.Domain.Setup;
using Teg.Domain.State;

namespace Teg.Domain.Tests.Setup;

/// <summary>
/// R§2: 2 to 4 participants, each with a name, a kind and a unique colour; at least one human.
/// Invalid input is returned as a typed <see cref="RuleViolation"/>, never thrown.
/// </summary>
public sealed class GameSetupValidationTests
{
    private static readonly MapDefinition Map = MapDefinition.LoadEmbedded();

    private static ParticipantSetup Human(string name, PlayerColor color) => new(name, ParticipantKind.Human, color);

    private static ParticipantSetup Bot(string name, PlayerColor color) => new(name, ParticipantKind.Bot, color);

    private static Result<GameState, RuleViolation> Create(params ParticipantSetup[] participants) =>
        GameSetup.Create(new GameOptions(participants), seed: 1, Map);

    private static void AssertRejected(string expectedCode, Result<GameState, RuleViolation> result)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(expectedCode, result.Error.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Error.Message));
    }

    // ---- valid ---------------------------------------------------------------------------------

    [Fact]
    public void TwoHumans_AreAccepted()
    {
        var result = Create(Human("Ana", PlayerColor.Azul), Human("Beto", PlayerColor.Rojo));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Participants.Count);
    }

    [Fact]
    public void FourParticipants_WithOneHumanAndThreeBots_AreAccepted()
    {
        var result = Create(
            Human("Ana", PlayerColor.Verde),
            Bot("B1", PlayerColor.Magenta),
            Bot("B2", PlayerColor.Negro),
            Bot("B3", PlayerColor.Amarillo));

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value.Participants.Count);
    }

    [Fact]
    public void ThreeHumans_AreAccepted()
    {
        var result = Create(
            Human("Ana", PlayerColor.Azul),
            Human("Beto", PlayerColor.Rojo),
            Human("Cata", PlayerColor.Negro));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void AllSixColours_AreAvailable()
    {
        foreach (var color in Enum.GetValues<PlayerColor>())
        {
            var other = color == PlayerColor.Azul ? PlayerColor.Rojo : PlayerColor.Azul;

            Assert.True(Create(Human("Ana", color), Bot("Bot", other)).IsSuccess, $"colour {color}");
        }

        Assert.Equal(6, Enum.GetValues<PlayerColor>().Length);
    }

    // ---- participant count ---------------------------------------------------------------------

    [Fact]
    public void NoParticipants_AreRejected()
    {
        AssertRejected("INVALID_PARTICIPANT_COUNT", Create());
    }

    [Fact]
    public void OneParticipant_IsRejected()
    {
        AssertRejected("INVALID_PARTICIPANT_COUNT", Create(Human("Ana", PlayerColor.Azul)));
    }

    [Fact]
    public void FiveParticipants_AreRejected()
    {
        var result = Create(
            Human("A", PlayerColor.Azul),
            Human("B", PlayerColor.Rojo),
            Human("C", PlayerColor.Negro),
            Human("D", PlayerColor.Amarillo),
            Human("E", PlayerColor.Verde));

        AssertRejected("INVALID_PARTICIPANT_COUNT", result);
    }

    // ---- at least one human --------------------------------------------------------------------

    [Fact]
    public void TwoBots_AreRejected_BecauseThereIsNoHuman()
    {
        AssertRejected("NO_HUMAN_PARTICIPANT", Create(Bot("B1", PlayerColor.Azul), Bot("B2", PlayerColor.Rojo)));
    }

    [Fact]
    public void FourBots_AreRejected_BecauseThereIsNoHuman()
    {
        var result = Create(
            Bot("B1", PlayerColor.Azul),
            Bot("B2", PlayerColor.Rojo),
            Bot("B3", PlayerColor.Negro),
            Bot("B4", PlayerColor.Verde));

        AssertRejected("NO_HUMAN_PARTICIPANT", result);
    }

    // ---- colours -------------------------------------------------------------------------------

    [Fact]
    public void TwoParticipantsWithTheSameColour_AreRejected()
    {
        AssertRejected(
            "DUPLICATE_COLOR",
            Create(Human("Ana", PlayerColor.Rojo), Bot("Bot", PlayerColor.Rojo)));
    }

    [Fact]
    public void ADuplicateColour_IsDetectedEvenWhenItIsNotAdjacent()
    {
        var result = Create(
            Human("Ana", PlayerColor.Verde),
            Human("Beto", PlayerColor.Azul),
            Human("Cata", PlayerColor.Verde));

        AssertRejected("DUPLICATE_COLOR", result);
    }

    [Fact]
    public void AColourOutsideTheSixAllowed_IsRejected()
    {
        AssertRejected(
            "INVALID_COLOR",
            Create(Human("Ana", (PlayerColor)99), Human("Beto", PlayerColor.Rojo)));
    }

    // ---- names ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankName_IsRejected(string name)
    {
        AssertRejected("BLANK_NAME", Create(Human(name, PlayerColor.Azul), Human("Beto", PlayerColor.Rojo)));
    }

    [Fact]
    public void ANullName_IsRejected()
    {
        AssertRejected("BLANK_NAME", Create(Human(null!, PlayerColor.Azul), Bot("Bot", PlayerColor.Rojo)));
    }

    // ---- precedence ----------------------------------------------------------------------------

    [Fact]
    public void TheCountIsCheckedFirst_ThenNames_ThenColours_ThenHumans()
    {
        // A single bot breaks "count" and "human"; count wins.
        AssertRejected("INVALID_PARTICIPANT_COUNT", Create(Bot("B", PlayerColor.Azul)));

        // Blank name + duplicate colour + no human: the name is reported.
        AssertRejected("BLANK_NAME", Create(Bot("", PlayerColor.Azul), Bot("B", PlayerColor.Azul)));

        // Duplicate colour + no human: the colour is reported.
        AssertRejected("DUPLICATE_COLOR", Create(Bot("A", PlayerColor.Azul), Bot("B", PlayerColor.Azul)));
    }
}
