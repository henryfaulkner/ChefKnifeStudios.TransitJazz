using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Shared.Collections;

/// <summary>
/// Non-generic host for cross-<typeparamref name="T"/> diagnostics shared by every
/// <see cref="RecyclableList{T}"/>. In DEBUG builds it exposes the <see cref="Abandoned"/>
/// signal that fires when a list is finalized without having been disposed, so leaks fail tests.
/// In Release builds neither the event nor the finalizer that raises it is compiled.
/// </summary>
public static class RecyclableList
{
#if DEBUG
    /// <summary>
    /// Raised (DEBUG only) when a <see cref="RecyclableList{T}"/> instance is finalized without
    /// having been disposed — i.e. its rented buffer was never returned to the pool via a
    /// <c>using</c> / <see cref="RecyclableList{T}.Dispose"/> call. Tests can subscribe to this to
    /// fail on leaks. Not compiled in Release; abandonment then produces no functional difference.
    /// </summary>
    public static event Action<RecyclableListAbandonedInfo>? Abandoned;

    internal static void RaiseAbandoned(RecyclableListAbandonedInfo info) => Abandoned?.Invoke(info);
#endif
}

#if DEBUG
/// <summary>
/// Diagnostic payload for <see cref="RecyclableList.Abandoned"/> identifying the leaked list.
/// DEBUG only.
/// </summary>
public sealed class RecyclableListAbandonedInfo
{
    internal RecyclableListAbandonedInfo(string elementTypeName, int count)
    {
        ElementTypeName = elementTypeName;
        Count = count;
    }

    /// <summary>The <c>T</c> element type name of the abandoned <see cref="RecyclableList{T}"/>.</summary>
    public string ElementTypeName { get; }

    /// <summary>The element count the list held when it was abandoned (a cheap allocation marker).</summary>
    public int Count { get; }

    /// <inheritdoc/>
    public override string ToString() =>
        $"RecyclableList<{ElementTypeName}> abandoned without Dispose (Count={Count}).";
}
#endif

/// <summary>
/// A drop-in, growable list backed by <see cref="ArrayPool{T}.Shared"/> instead of heap-allocated
/// arrays. Behaves like <see cref="List{T}"/> for every operation, but on capacity growth it rents
/// a larger buffer from the shared pool, copies, and returns the old buffer; on <see cref="Dispose"/>
/// it returns the current buffer. This reduces backing-array allocations and LOH pressure under
/// high-throughput accumulation.
/// </summary>
/// <remarks>
/// <para><b>Always dispose.</b> Use a <c>using</c> statement (or one of the
/// <see cref="RecyclableListExtensions"/> patterns) so the rented buffer returns to the pool.
/// A DEBUG-only finalizer raises <see cref="RecyclableList.Abandoned"/> if you forget.</para>
/// <para><b>Not thread-safe.</b> Concurrent mutation is undefined; the caller must synchronize.</para>
/// <para><see cref="AsSpan"/> is invalidated by any operation that grows the list.</para>
/// <para><see cref="Capacity"/> may exceed the capacity you requested because
/// <see cref="ArrayPool{T}.Rent"/> rounds up.</para>
/// </remarks>
/// <typeparam name="T">The element type.</typeparam>
public sealed class RecyclableList<T> : IList<T>, IReadOnlyList<T>, IDisposable
{
    // Shared empty sentinel: an empty list holds this (NOT a rental) so there is nothing to return
    // until the first growth. Never returned to the pool.
    private static readonly T[] s_empty = Array.Empty<T>();

    private T[] _array;
    private int _count;
    private bool _disposed;
    private int _version;

    /// <summary>Creates an empty list. No buffer is rented until the first add.</summary>
    public RecyclableList()
    {
        _array = s_empty;
    }

    /// <summary>Creates an empty list pre-sized to hold at least <paramref name="capacity"/> elements
    /// without a mid-fill rental.</summary>
    /// <param name="capacity">Initial capacity. Must be non-negative.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is negative.</exception>
    public RecyclableList(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        _array = capacity == 0 ? s_empty : ArrayPool<T>.Shared.Rent(capacity);
    }

    /// <summary>Creates a list containing the elements of <paramref name="source"/>, pre-sized when
    /// the source count is known.</summary>
    /// <param name="source">The elements to copy in.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    public RecyclableList(IEnumerable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.TryGetNonEnumeratedCount(out int count) && count > 0)
            _array = ArrayPool<T>.Shared.Rent(count);
        else
            _array = s_empty;
        AddRange(source);
    }

    /// <summary>The number of valid elements in the list.</summary>
    public int Count => _count;

    /// <summary>The length of the backing buffer. Always &gt;= <see cref="Count"/>; may exceed a
    /// requested capacity because the pool rounds up.</summary>
    public int Capacity => _array.Length;

    /// <inheritdoc/>
    public bool IsReadOnly => false;

    /// <summary>Gets or sets the element at <paramref name="index"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not in [0, Count).</exception>
    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _array[index];
        }
        set
        {
            if ((uint)index >= (uint)_count)
                throw new ArgumentOutOfRangeException(nameof(index));
            _array[index] = value;
            _version++;
        }
    }

    /// <inheritdoc/>
    public void Add(T item)
    {
        if (_count == _array.Length)
            Grow(_count + 1);
        _array[_count++] = item;
        _version++;
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not in [0, Count].</exception>
    public void Insert(int index, T item)
    {
        if ((uint)index > (uint)_count)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (_count == _array.Length)
            Grow(_count + 1);
        if (index < _count)
            Array.Copy(_array, index, _array, index + 1, _count - index);
        _array[index] = item;
        _count++;
        _version++;
    }

    /// <inheritdoc/>
    public bool Remove(T item)
    {
        int i = IndexOf(item);
        if (i < 0)
            return false;
        RemoveAt(i);
        return true;
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not in [0, Count).</exception>
    public void RemoveAt(int index)
    {
        if ((uint)index >= (uint)_count)
            throw new ArgumentOutOfRangeException(nameof(index));
        _count--;
        if (index < _count)
            Array.Copy(_array, index + 1, _array, index, _count - index);
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            _array[_count] = default!;
        _version++;
    }

    /// <summary>Removes all elements. The backing buffer is retained (not returned to the pool);
    /// the used region is cleared for reference types so the pool does not pin managed graphs.</summary>
    public void Clear()
    {
        if (_count > 0 && RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            Array.Clear(_array, 0, _count);
        _count = 0;
        _version++;
    }

    /// <inheritdoc/>
    public bool Contains(T item) => IndexOf(item) >= 0;

    /// <inheritdoc/>
    public int IndexOf(T item) => Array.IndexOf(_array, item, 0, _count);

    /// <inheritdoc/>
    public void CopyTo(T[] array, int arrayIndex) => Array.Copy(_array, 0, array, arrayIndex, _count);

    /// <summary>Appends the elements of <paramref name="items"/>, pre-growing when the count is known.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    public void AddRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.TryGetNonEnumeratedCount(out int count) && count > 0)
            EnsureCapacity(_count + count);
        foreach (T item in items)
            Add(item);
    }

    /// <summary>Asynchronously appends the elements of <paramref name="items"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    public async ValueTask AddRangeAsync(IAsyncEnumerable<T> items, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        await foreach (T item in items.WithCancellation(ct).ConfigureAwait(false))
            Add(item);
    }

    /// <summary>Sorts the elements in place using the default comparer.</summary>
    public void Sort()
    {
        Array.Sort(_array, 0, _count);
        _version++;
    }

    /// <summary>Sorts the elements in place using <paramref name="comparer"/> (or the default comparer when null).</summary>
    public void Sort(IComparer<T>? comparer)
    {
        Array.Sort(_array, 0, _count, comparer);
        _version++;
    }

    /// <summary>Searches the sorted live region for <paramref name="item"/>. The caller must ensure
    /// the region is sorted.</summary>
    public int BinarySearch(T item) => Array.BinarySearch(_array, 0, _count, item);

    /// <summary>Searches the sorted live region for <paramref name="item"/> using
    /// <paramref name="comparer"/>. The caller must ensure the region is sorted.</summary>
    public int BinarySearch(T item, IComparer<T>? comparer) => Array.BinarySearch(_array, 0, _count, item, comparer);

    /// <summary>Returns a span over the live region <c>[0, Count)</c>.</summary>
    /// <remarks>The span is invalidated by any operation that grows the list (the backing buffer is
    /// replaced), and by <see cref="Dispose"/>. Do not retain it across such operations.</remarks>
    public Span<T> AsSpan() => _array.AsSpan(0, _count);

    /// <summary>Ensures the backing buffer can hold at least <paramref name="capacity"/> elements
    /// without a further rental, pre-growing if necessary.</summary>
    public void EnsureCapacity(int capacity)
    {
        if (capacity > _array.Length)
            Grow(capacity);
    }

    /// <summary>Grows the backing buffer to at least <paramref name="min"/> elements: rents a larger
    /// buffer from the pool, copies the live region, and returns the previous rental (clearing it for
    /// reference types).</summary>
    private void Grow(int min)
    {
        int doubled = _array.Length == 0 ? 4 : _array.Length * 2;
        int desired = Math.Max(min, doubled);
        T[] newArray = ArrayPool<T>.Shared.Rent(desired);
        if (_count > 0)
            Array.Copy(_array, newArray, _count);
        T[] old = _array;
        _array = newArray;
        // Only the empty sentinel is not a rental — never return it.
        if (old.Length != 0)
            ArrayPool<T>.Shared.Return(old, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
    }

    /// <summary>Returns an enumerator that throws <see cref="InvalidOperationException"/> if the list
    /// is modified during enumeration (parity with <see cref="List{T}"/>).</summary>
    public IEnumerator<T> GetEnumerator()
    {
        int version = _version;
        for (int i = 0; i < _count; i++)
        {
            if (version != _version)
                throw new InvalidOperationException("Collection was modified during enumeration.");
            yield return _array[i];
        }
        if (version != _version)
            throw new InvalidOperationException("Collection was modified during enumeration.");
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Returns the backing buffer to <see cref="ArrayPool{T}.Shared"/>. Idempotent: a second
    /// call is a no-op, so the buffer is returned exactly once.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        T[] old = _array;
        _array = s_empty;
        _count = 0;
        if (old.Length != 0)
            ArrayPool<T>.Shared.Return(old, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        GC.SuppressFinalize(this);
    }

#if DEBUG
    /// <summary>DEBUG-only finalizer: signals <see cref="RecyclableList.Abandoned"/> when a list is
    /// collected without having been disposed. Not compiled in Release (no finalizer overhead).</summary>
    ~RecyclableList()
    {
        if (!_disposed)
            RecyclableList.RaiseAbandoned(new RecyclableListAbandonedInfo(typeof(T).Name, _count));
    }
#endif
}
