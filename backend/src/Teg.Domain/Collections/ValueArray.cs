using System.Collections;
using System.Collections.Immutable;

namespace Teg.Domain.Collections;

/// <summary>
/// Immutable list with VALUE equality (same elements, same order). Records compare their collection members
/// by reference, which would make two independently built game states "different"; using this type keeps
/// <c>GameState</c> equality structural, as the determinism rule (E10) requires.
/// </summary>
public sealed class ValueArray<T> : IReadOnlyList<T>, IEquatable<ValueArray<T>>
{
    public static readonly ValueArray<T> Empty = new([]);

    private readonly ImmutableArray<T> _items;

    /// <summary>Copies <paramref name="items"/>: later changes to the source do not affect this instance.</summary>
    public ValueArray(IEnumerable<T> items)
    {
        _items = items.ToImmutableArray();
    }

    public int Count => _items.Length;

    public T this[int index] => _items[index];

    public bool Equals(ValueArray<T>? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (_items.Length != other._items.Length)
        {
            return false;
        }

        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < _items.Length; i++)
        {
            if (!comparer.Equals(_items[i], other._items[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as ValueArray<T>);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in _items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static bool operator ==(ValueArray<T>? left, ValueArray<T>? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(ValueArray<T>? left, ValueArray<T>? right) => !(left == right);
}

public static class ValueArrayExtensions
{
    public static ValueArray<T> ToValueArray<T>(this IEnumerable<T> items) => new(items);
}
