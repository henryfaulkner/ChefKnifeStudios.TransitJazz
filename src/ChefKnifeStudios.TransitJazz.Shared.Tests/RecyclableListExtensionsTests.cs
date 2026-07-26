using ChefKnifeStudios.TransitJazz.Shared.Collections;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Shared.Tests;

public class RecyclableListExtensionsTests
{
    // T016 — AsRecyclableList pre-sizes from a known-count source and copies all items in order.
    [Fact]
    public void AsRecyclableList_KnownCountSource_CopiesAllItemsInOrderAndPreSizes()
    {
        int[] source = Enumerable.Range(0, 500).ToArray();

        using RecyclableList<int> buffer = source.AsRecyclableList();

        Assert.Equal(500, buffer.Count);
        Assert.True(buffer.Capacity >= 500);
        Assert.Equal(source, buffer.ToList());
    }

    // T017 / AV-5 / SC-006 / G3 — double dispose is a no-op (buffer returned exactly once).
    [Fact]
    public void Dispose_CalledTwice_IsNoOp()
    {
        var list = new RecyclableList<int> { 1, 2, 3 };

        list.Dispose();
        list.Dispose(); // must not throw, must not double-return

        Assert.Empty(list);
    }

    // T018 — AsList yields a List<T> copy of the live region.
    [Fact]
    public void AsList_ReturnsListCopyOfLiveRegion()
    {
        using var pooled = new RecyclableList<int> { 10, 20, 30 };

        List<int> copy = pooled.AsList();

        Assert.IsType<List<int>>(copy);
        Assert.Equal(new[] { 10, 20, 30 }, copy);
        // It is a copy: mutating the copy does not affect the pooled list.
        copy.Add(40);
        Assert.Equal(3, pooled.Count);
    }

    // T018 — AsIList returns the same instance typed as IList<T>.
    [Fact]
    public void AsIList_ReturnsSameInstance()
    {
        using var pooled = new RecyclableList<int> { 1, 2 };

        IList<int> asIList = pooled.AsIList();

        Assert.Same(pooled, asIList);
    }

    // T021 / AV-8 / SC-006 / FR-012 — full enumeration disposes the underlying list exactly once.
    [Fact]
    public void AsEnumerableRegisteredToDispose_FullyEnumerated_DisposesUnderlyingListOnce()
    {
        var list = new RecyclableList<int> { 1, 2, 3 };

        List<int> observed = list.AsEnumerableRegisteredToDispose().ToList();

        Assert.Equal(new[] { 1, 2, 3 }, observed);
        // After enumeration the list was disposed: Count reset to 0, and a further Dispose is a no-op.
        Assert.Empty(list);
        list.Dispose(); // no throw — already disposed exactly once
    }

    // T022 — early break out of the foreach still disposes the underlying list.
    [Fact]
    public void AsEnumerableRegisteredToDispose_EarlyBreak_StillDisposesUnderlyingList()
    {
        var list = new RecyclableList<int> { 1, 2, 3, 4, 5 };

        foreach (int value in list.AsEnumerableRegisteredToDispose())
        {
            if (value == 2)
                break; // enumerator Dispose fires the finally
        }

        Assert.Empty(list); // disposed
        list.Dispose();     // no throw
    }
}
