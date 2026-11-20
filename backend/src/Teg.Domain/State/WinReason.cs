namespace Teg.Domain.State;

/// <summary>Why the game ended (R§8). When several conditions hold at once, the first of them in this order is the reason.</summary>
public enum WinReason
{
    /// <summary>Every other participant was eliminated: the winner is the last one with countries.</summary>
    Elimination,

    /// <summary>The common objective: 30 countries with four participants, all fifty with two or three.</summary>
    CommonObjective,

    /// <summary>The winner's secret objective: an occupation objective completed, or the destruction of the colour it named.</summary>
    SecretObjective,
}
