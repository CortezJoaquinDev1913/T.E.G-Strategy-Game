using Teg.Application.Ports;
using Teg.Application.Views;
using Teg.Domain.Events;
using Teg.Domain.State;

namespace Teg.Application.Tests.Support;

/// <summary>
/// The fake <see cref="IGameRepository"/> of the use-case tests. It keeps what the real one will keep (the state, the events with
/// their sequence, the applied command ids) and obeys the SAME contract: <see cref="SaveAsync"/> checks the command id first, then
/// the version, and changes NOTHING unless it returns <see cref="SaveOutcome.Saved"/> (so it is all-or-nothing, like the one
/// transaction of the real one). It also records every call, and has two hooks to stage the failures a test needs:
/// <see cref="BeforeSave"/> (a concurrent writer gets in first) and <see cref="FailNextSave"/> (the store breaks down).
/// </summary>
internal sealed class InMemoryGameRepository : IGameRepository
{
    private sealed class Entry(GameState state)
    {
        public GameState State { get; set; } = state;

        public List<StoredEvent> Events { get; } = [];

        public HashSet<Guid> Commands { get; } = [];

        public long Touched { get; set; }
    }

    private readonly Dictionary<Guid, Entry> _games = [];
    private int _created;
    private long _clock;

    /// <summary>How many times a game was added.</summary>
    public int AddCalls { get; private set; }

    /// <summary>Every call to <see cref="SaveAsync"/>, in order, whatever it answered.</summary>
    public List<(Guid GameId, GameChange Change)> SaveCalls { get; } = [];

    /// <summary>Runs inside <see cref="SaveAsync"/> before anything is checked: a stand-in for a concurrent request.</summary>
    public Action<Guid, GameChange>? BeforeSave { get; set; }

    /// <summary>The next <see cref="SaveAsync"/> throws this and changes nothing.</summary>
    public Exception? FailNextSave { get; set; }

    public int Count => _games.Count;

    public IReadOnlyCollection<Guid> Ids => _games.Keys;

    /// <summary>The saved state of a game (throws when it does not exist).</summary>
    public GameState State(Guid id) => _games[id].State;

    /// <summary>Every saved event of the game, oldest first.</summary>
    public IReadOnlyList<StoredEvent> Stored(Guid id) => _games[id].Events;

    public bool Applied(Guid id, Guid commandId) => _games[id].Commands.Contains(commandId);

    public Task<Guid> AddAsync(GameState initial, CancellationToken ct = default)
    {
        AddCalls++;
        var id = new Guid(++_created, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]);
        _games[id] = new Entry(initial) { Touched = ++_clock };

        return Task.FromResult(id);
    }

    public Task<GameState?> FindAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_games.TryGetValue(id, out var entry) ? entry.State : null);

    public Task<bool> CommandAppliedAsync(Guid gameId, Guid commandId, CancellationToken ct = default) =>
        Task.FromResult(_games.TryGetValue(gameId, out var entry) && entry.Commands.Contains(commandId));

    public Task<SaveOutcome> SaveAsync(Guid gameId, GameChange change, CancellationToken ct = default)
    {
        SaveCalls.Add((gameId, change));
        BeforeSave?.Invoke(gameId, change);

        if (FailNextSave is { } failure)
        {
            FailNextSave = null;
            throw failure;
        }

        var entry = _games[gameId];
        if (entry.Commands.Contains(change.CommandId))
        {
            return Task.FromResult(SaveOutcome.AlreadyApplied);
        }

        if (entry.State.Version != change.ExpectedVersion)
        {
            return Task.FromResult(SaveOutcome.VersionConflict);
        }

        // From here on nothing can fail: state, events and command id go in together.
        entry.State = change.NewState;
        foreach (var @event in change.Events)
        {
            entry.Events.Add(new StoredEvent(entry.Events.Count + 1, change.NewState.Version, change.CommandId, change.ActorSeat, @event));
        }

        entry.Commands.Add(change.CommandId);
        entry.Touched = ++_clock;

        return Task.FromResult(SaveOutcome.Saved);
    }

    public Task<IReadOnlyList<GameSummary>> ListAsync(GameStatus? status, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<GameSummary>>(_games
            .OrderByDescending(pair => pair.Value.Touched)
            .Select(pair => GameSummary.From(pair.Key, pair.Value.State))
            .Where(summary => status is null || summary.Status == status)
            .ToArray());

    public Task<IReadOnlyList<StoredEvent>> EventsAsync(Guid gameId, int afterSeq, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<StoredEvent>>(_games[gameId].Events.Where(e => e.Seq > afterSeq).ToArray());
}
