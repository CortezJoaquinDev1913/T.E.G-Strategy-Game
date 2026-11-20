using Teg.Domain.Collections;

namespace Teg.Domain.Tests.Collections;

/// <summary>
/// Immutable list with value equality, so two <c>GameState</c> records built independently from the same
/// inputs compare equal (needed by the determinism / replay rule E10).
/// </summary>
public sealed class ValueArrayTests
{
    [Fact]
    public void SameElementsInTheSameOrder_AreEqual_AndShareAHashCode()
    {
        var first = new ValueArray<int>([1, 2, 3]);
        var second = new ValueArray<int>([1, 2, 3]);

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void DifferentElements_OrderOrLength_AreNotEqual()
    {
        var baseline = new ValueArray<int>([1, 2, 3]);

        Assert.NotEqual(baseline, new ValueArray<int>([1, 2, 4]));
        Assert.NotEqual(baseline, new ValueArray<int>([3, 2, 1]));
        Assert.NotEqual(baseline, new ValueArray<int>([1, 2]));
        Assert.True(baseline != new ValueArray<int>([1, 2]));
    }

    [Fact]
    public void ElementsAreComparedByValue_NotByReference()
    {
        var first = new ValueArray<string>([new string('a', 3), "b"]);
        var second = new ValueArray<string>(["aaa", "b"]);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ActsAsAReadOnlyList()
    {
        var items = new ValueArray<string>(["x", "y", "z"]);

        Assert.Equal(3, items.Count);
        Assert.Equal("y", items[1]);
        Assert.Equal(["x", "y", "z"], items.ToArray());
    }

    [Fact]
    public void ConstructionCopiesTheSource_SoLaterChangesDoNotLeakIn()
    {
        var source = new List<int> { 1, 2, 3 };
        var items = new ValueArray<int>(source);

        source.Add(4);
        source[0] = 99;

        Assert.Equal([1, 2, 3], items.ToArray());
    }

    [Fact]
    public void ToValueArray_BuildsFromAnySequence()
    {
        var items = Enumerable.Range(1, 4).Select(i => i * 10).ToValueArray();

        Assert.Equal(new ValueArray<int>([10, 20, 30, 40]), items);
    }

    [Fact]
    public void Empty_IsEqualToAnotherEmpty()
    {
        Assert.Equal(new ValueArray<int>([]), ValueArray<int>.Empty);
        Assert.Empty(ValueArray<int>.Empty);
    }
}
