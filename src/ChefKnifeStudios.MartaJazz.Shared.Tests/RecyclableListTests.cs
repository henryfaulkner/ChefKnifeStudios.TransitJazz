using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ChefKnifeStudios.MartaJazz.Shared.Collections;
using Xunit;

namespace ChefKnifeStudios.MartaJazz.Shared.Tests;

public class RecyclableListTests
{
    // T011 / AV-1 / SC-001 — growth from empty preserves order and count.
    [Fact]
    public void Add_HundredItemsToEmptyList_PreservesCountAndInsertionOrder()
    {
        using var list = new RecyclableList<int>();

        for (int i = 0; i < 100; i++)
            list.Add(i);

        Assert.Equal(100, list.Count);
        for (int i = 0; i < 100; i++)
            Assert.Equal(i, list[i]);
    }

    // T012 / AV-2 / SC-001 — same operation sequence yields identical results to List<T>.
    [Fact]
    public void OperationSequence_MatchesListOfT()
    {
        using var pooled = new RecyclableList<int>();
        var reference = new List<int>();

        void Apply(Action<IList<int>> op)
        {
            op(pooled);
            op(reference);
            AssertSameSequence(reference, pooled);
        }

        // add
        for (int i = 0; i < 50; i++)
            Apply(l => l.Add(i * 3));
        // insert in the middle and at the ends
        Apply(l => l.Insert(0, -1));
        Apply(l => l.Insert(l.Count, 999));
        Apply(l => l.Insert(25, 12345));
        // indexer set
        Apply(l => l[10] = 7777);
        // remove by value and by index
        Apply(l => l.Remove(999));
        Apply(l => l.RemoveAt(0));
        // contains / indexof parity (read-only, assert directly)
        Assert.Equal(reference.Contains(7777), pooled.Contains(7777));
        Assert.Equal(reference.IndexOf(12345), pooled.IndexOf(12345));
        // sort
        pooled.Sort();
        reference.Sort();
        AssertSameSequence(reference, pooled);
        // binary search on the now-sorted region
        Assert.Equal(reference.BinarySearch(12345), pooled.BinarySearch(12345));
        // clear
        Apply(l => l.Clear());
        Assert.Empty(pooled);
    }

    // T013 / AV-3 / SC-004 — pre-sized list never grows while filling to the requested count.
    [Fact]
    public void PreSizedList_FillingToCapacity_DoesNotRentAgain()
    {
        using var list = new RecyclableList<int>(1000);
        int initialCapacity = list.Capacity;

        for (int i = 0; i < 1000; i++)
        {
            list.Add(i);
            Assert.Equal(initialCapacity, list.Capacity); // no mid-fill rental
        }

        Assert.Equal(1000, list.Count);
        Assert.True(list.Capacity >= 1000);
    }

    // T014 — empty-list operations do not throw.
    [Fact]
    public void EmptyList_ReadEnumerateClearDispose_DoNotThrow()
    {
        var list = new RecyclableList<string>();

        Assert.Empty(list);          // count + enumerate
        Assert.DoesNotContain("x", list);
        Assert.Equal(-1, list.IndexOf("x"));
        list.Clear();                // no-op, no throw
        Assert.Equal(0, list.AsSpan().Length);
        list.Dispose();              // no throw on empty
    }

    // T014 — negative capacity throws ArgumentOutOfRangeException (parity with List<T>).
    [Fact]
    public void Constructor_NegativeCapacity_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecyclableList<int>(-1));
    }

    // T014 — null source throws ArgumentNullException (parity with List<T>).
    [Fact]
    public void Constructor_NullSource_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => new RecyclableList<int>((IEnumerable<int>)null!));
    }

    // T014 — AsSpan over an empty list is length 0.
    [Fact]
    public void AsSpan_EmptyList_IsLengthZero()
    {
        using var list = new RecyclableList<int>();
        Assert.Equal(0, list.AsSpan().Length);
    }

    // Enumerator invalidates on concurrent modification (List<T> parity).
    [Fact]
    public void GetEnumerator_ModifiedDuringEnumeration_Throws()
    {
        using var list = new RecyclableList<int> { 1, 2, 3 };

        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (var _ in list)
                list.Add(99);
        });
    }

    private static void AssertSameSequence(List<int> expected, RecyclableList<int> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(expected, actual.ToList());
    }

#if DEBUG
    // T024 / AV-6 / AV-7 / SC-005 — DEBUG-only: an abandoned (never-disposed) list raises the
    // Abandoned signal when finalized. Deterministic seam only: forced GC + WaitForPendingFinalizers,
    // no Thread.Sleep, no retry. The 5s timeout is a generous ceiling on a single forced GC +
    // finalizer drain; if the finalizer never runs the test fails fast rather than hanging.
    // async Task (with no await) because xUnit v2 only honors [Fact(Timeout=...)] on async tests.
    [Fact(Timeout = 5000)]
    public async Task AbandonedList_FinalizedWithoutDispose_RaisesAbandonedSignal()
    {
        await Task.Yield();
        bool fired = false;
        void Handler(RecyclableListAbandonedInfo info) => fired = true;
        RecyclableList.Abandoned += Handler;
        try
        {
            AbandonWithoutDisposing();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.True(fired, "Abandoned signal should fire for a finalized, never-disposed list.");
        }
        finally
        {
            RecyclableList.Abandoned -= Handler;
        }
    }

    // Non-inlined so the local goes out of scope and the instance becomes collectible.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AbandonWithoutDisposing()
    {
        var leaked = new RecyclableList<int>();
        for (int i = 0; i < 10; i++)
            leaked.Add(i); // force a real rental so there is a buffer to leak
        // intentionally NOT disposed and NOT returned
    }
#endif

    // T025 / SC-003 / SC-007 — the pooled path allocates strictly fewer backing-array bytes than
    // List<T> for a large accumulation. Hard, build-failing assert with a wide safety margin
    // (pooled must allocate < half of List<T>) so normal runtime variance never trips it.
    // Unit-level (Tier 0), no retry, no sleep.
    [Fact]
    public void PooledAccumulation_AllocatesStrictlyFewerBytesThanList()
    {
        const int n = 100_000;

        // Warm up at the SAME size so ArrayPool holds buffers large enough for the measured run to
        // reuse — the pooled advantage is buffer REUSE across calls, not a single cold rental.
        // (Also primes JIT/type init for both paths.)
        RunListAccumulation(n);
        RunPooledAccumulation(n);
        RunPooledAccumulation(n);

        long listBytes = MeasureAllocated(() => RunListAccumulation(n));
        long pooledBytes = MeasureAllocated(() => RunPooledAccumulation(n));

        Assert.True(
            pooledBytes < listBytes,
            $"Pooled path should allocate fewer bytes than List<T>. pooled={pooledBytes}, list={listBytes}");
        // Wide documented margin: pooled path (rented buffers, returned) should be well under half.
        Assert.True(
            pooledBytes < listBytes / 2,
            $"Pooled path should allocate < half of List<T>. pooled={pooledBytes}, list={listBytes}");
    }

    private static long MeasureAllocated(Action action)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        long after = GC.GetAllocatedBytesForCurrentThread();
        return after - before;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunListAccumulation(int n)
    {
        var list = new List<int>();
        for (int i = 0; i < n; i++)
            list.Add(i);
        GC.KeepAlive(list);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunPooledAccumulation(int n)
    {
        using var list = new RecyclableList<int>();
        for (int i = 0; i < n; i++)
            list.Add(i);
    }
}
