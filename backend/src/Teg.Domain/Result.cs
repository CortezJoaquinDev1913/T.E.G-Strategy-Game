namespace Teg.Domain;

/// <summary>
/// A refused action. <paramref name="Code"/> is a stable SCREAMING_SNAKE identifier (it is part of the API
/// contract, e.g. <c>NOT_ADJACENT</c>); <paramref name="Message"/> is for humans.
/// </summary>
public sealed record RuleViolation(string Code, string Message);

/// <summary>
/// Outcome of a domain operation that can be refused: either a value or an error. Rule violations are
/// values, never exceptions used for control flow.
/// </summary>
public readonly struct Result<TValue, TError>
{
    private readonly TValue? _value;
    private readonly TError? _error;

    private Result(bool isSuccess, TValue? value, TError? error)
    {
        IsSuccess = isSuccess;
        _value = value;
        _error = error;
    }

    public bool IsSuccess { get; }

    /// <summary>The produced value. Throws <see cref="InvalidOperationException"/> on a failure.</summary>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("The result is a failure and has no value.");

    /// <summary>The refusal. Throws <see cref="InvalidOperationException"/> on a success.</summary>
    public TError Error => IsSuccess
        ? throw new InvalidOperationException("The result is a success and has no error.")
        : _error!;

    public static Result<TValue, TError> Ok(TValue value) => new(true, value, default);

    public static Result<TValue, TError> Fail(TError error) => new(false, default, error);
}
