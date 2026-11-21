using Teg.Application.Bots;
using Teg.Domain.Commands;
using Teg.Domain.Rules;
using Teg.Domain.State;

namespace Teg.Application.Tests.Support;

/// <summary>A policy that does whatever the test says (to stage a bot that proposes something illegal, for instance).</summary>
internal sealed class ScriptedPolicy(Func<GameState, LegalMoveSet, GameCommand> script) : IBotPolicy
{
    public GameCommand Next(GameState state, LegalMoveSet legal) => script(state, legal);
}

/// <summary>Remembers every decision of the policy it wraps: the state, the legal moves it was given and the command it chose.</summary>
internal sealed class RecordingPolicy(IBotPolicy inner, bool record = true) : IBotPolicy
{
    internal sealed record Decision(GameState State, LegalMoveSet Legal, GameCommand Command);

    public List<Decision> Decisions { get; } = [];

    public GameCommand Next(GameState state, LegalMoveSet legal)
    {
        var command = inner.Next(state, legal);
        if (record)
        {
            Decisions.Add(new Decision(state, legal, command));
        }

        return command;
    }
}
