namespace Teg.Domain.State;

/// <summary>
/// The objective a participant was given (R§8). A CLOSED type: the constructor is private, so the cases below are all
/// there is (the victory phase adds the one it needs, for a destruction objective that passed to the common one, as one
/// more nested record). Plain data with value equality; what each case demands is decided by <c>Objectives</c>.
/// </summary>
public abstract record Objective
{
    private Objective()
    {
    }

    /// <summary>
    /// No secret objective: what two and three participants have (their only goal is the common one, conquering the
    /// fifty countries) and what a hand-built participant starts with.
    /// </summary>
    public static Objective None { get; } = new NoObjective();

    /// <summary>The nested record of <see cref="None"/>.</summary>
    public sealed record NoObjective : Objective;

    /// <summary>One of the nine occupation objectives, 1 to 9: own the countries it names (R§8).</summary>
    public sealed record Occupation(int Number) : Objective;

    /// <summary>
    /// A destruction objective (10 to 15): <paramref name="Number"/> is the objective that was DRAWN (it tells the colour
    /// the table of R§8 named) and <paramref name="Target"/> the colour to destroy, which is the one that counts: it was
    /// fixed when the objectives were dealt and is never evaluated again (R§8: it is the player to the right when the
    /// drawn colour was the participant's own or nobody's).
    /// </summary>
    public sealed record Destruction(int Number, PlayerColor Target) : Objective;
}
