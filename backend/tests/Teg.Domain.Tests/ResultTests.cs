namespace Teg.Domain.Tests;

/// <summary>
/// The engine reports rule violations as values (design: <c>Result&lt;Transition, RuleViolation&gt;</c>),
/// never as exceptions used for control flow.
/// </summary>
public sealed class ResultTests
{
    [Fact]
    public void Ok_ExposesTheValue_AndHasNoError()
    {
        var result = Result<int, string>.Ok(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void Fail_ExposesTheError_AndHasNoValue()
    {
        var result = Result<int, string>.Fail("NOPE");

        Assert.False(result.IsSuccess);
        Assert.Equal("NOPE", result.Error);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void RuleViolation_CarriesACodeAndAMessage()
    {
        var violation = new RuleViolation("NOT_ADJACENT", "Countries do not border each other.");

        Assert.Equal("NOT_ADJACENT", violation.Code);
        Assert.Equal("Countries do not border each other.", violation.Message);
    }
}
