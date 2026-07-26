# API Contract: RecyclableList<T>

The public API this feature exposes to other code in the solution. Namespace: `ChefKnifeStudios.TransitJazz.Shared.Collections`. Assembly: `ChefKnifeStudios.TransitJazz.Shared`.

## `public sealed class RecyclableList<T> : IList<T>, IReadOnlyList<T>, IDisposable`

### Constructors

```csharp
public RecyclableList();                        // empty, no rental until first Add
public RecyclableList(int capacity);            // rents buffer >= capacity; throws ArgumentOutOfRangeException if capacity < 0
public RecyclableList(IEnumerable<T> source);   // pre-sized when count known; throws ArgumentNullException if source is null
```

### Properties

```csharp
public int Count { get; }                       // valid element count
public int Capacity { get; }                    // backing buffer length (>= Count; may exceed requested capacity)
public bool IsReadOnly => false;
public T this[int index] { get; set; }          // bounds-checked against Count
```

### Standard IList<T> operations (behavior identical to List<T>)

```csharp
public void Add(T item);
public void Insert(int index, T item);
public bool Remove(T item);
public void RemoveAt(int index);
public void Clear();                            // Count -> 0, buffer retained, used region cleared
public bool Contains(T item);
public int IndexOf(T item);
public void CopyTo(T[] array, int arrayIndex);
public IEnumerator<T> GetEnumerator();          // throws InvalidOperationException on concurrent modification
```

### Extended operations (from source doc surface)

```csharp
public void AddRange(IEnumerable<T> items);                 // throws ArgumentNullException if items is null
public ValueTask AddRangeAsync(IAsyncEnumerable<T> items,
                               CancellationToken ct = default);
public void Sort();
public void Sort(IComparer<T>? comparer);
public int BinarySearch(T item);                            // caller ensures sorted region
public int BinarySearch(T item, IComparer<T>? comparer);
public Span<T> AsSpan();                                    // view over [0..Count); invalidated by any growth
public void EnsureCapacity(int capacity);                  // pre-grow to avoid mid-flight rentals
```

### Disposal

```csharp
public void Dispose();   // returns the current backing buffer to ArrayPool<T>.Shared exactly once (idempotent)
```

**Contract guarantees**
- G1 — Behavioral parity: for any sequence of the above operations, observable results equal those of `List<T>` on the same inputs.
- G2 — Pooled growth: each capacity growth returns the previous buffer to the pool and rents a larger one.
- G3 — Single return: each rented buffer returns to the pool exactly once (on growth or dispose); double-dispose is a no-op.
- G4 — Reference hygiene: buffers holding reference-type elements are cleared on return.
- G5 — Not thread-safe: concurrent mutation is undefined; caller synchronizes.

## `public static class RecyclableList` (non-generic host, DEBUG signal)

```csharp
#if DEBUG
public static event Action<RecyclableListAbandonedInfo>? Abandoned;   // fires when an instance is finalized undisposed
#endif
```

- Release builds compile out both the finalizer and the event; abandonment produces no functional difference.
- `RecyclableListAbandonedInfo` carries diagnostic detail (element type name and, where cheap to capture, an allocation marker) to identify the leak in a failing test.

## `public static class RecyclableListExtensions`

```csharp
public static RecyclableList<T> AsRecyclableList<T>(this IEnumerable<T> source);
public static List<T>           AsList<T>(this RecyclableList<T> list);
public static IList<T>          AsIList<T>(this RecyclableList<T> list);
public static IEnumerable<T>    AsEnumerableRegisteredToDispose<T>(this RecyclableList<T> list);
```

- `AsRecyclableList`: pre-sizes when `source.TryGetNonEnumeratedCount` succeeds; **caller owns disposal** of the result.
- `AsList`: returns a `List<T>` copy of the live region; the "avoid an extra copy for `List<T>` inputs" rule applies to already-`List<T>` sources of the `IEnumerable` fast path (documented in XML doc on the method).
- `AsIList`: identity cast to `IList<T>`.
- `AsEnumerableRegisteredToDispose`: one-shot enumerable; disposes the underlying `RecyclableList<T>` when enumeration completes or the enumerator is disposed early — caller writes no disposal code.

## Request-lifetime usage (documented pattern, NOT a Shared API)

The request-buffer use case is realized by the type being `IDisposable` plus the ASP.NET-side registration, which lives in the WebAPI project (Shared must not reference ASP.NET):

```csharp
// inside a WebAPI endpoint handler:
var buffer = rows.AsRecyclableList();
httpContext.Response.RegisterForDispose(buffer);   // returned to pool when the request ends
return buffer.AsIList();
```

## Acceptance vectors (map to spec scenarios / SCs)

| ID | Input / action | Expected |
|----|----------------|----------|
| AV-1 | Add 100 items to an empty list (forces multiple growths) | Count=100, indices 0..99 in insertion order (SC-001) |
| AV-2 | Same op sequence on `RecyclableList<T>` and `List<T>` (add/insert/remove/sort/search/clear) | Identical observable results (G1, SC-001) |
| AV-3 | `new RecyclableList<T>(capacity: 1000)` then add 1000 items, counting rentals | Zero mid-fill rentals (SC-004) |
| AV-4 | `using` block that grows N times | N old buffers + 1 final buffer returned; zero abandoned (SC-002) |
| AV-5 | `Dispose()` called twice | Second call is a no-op; buffer returned only once (G3, SC-006) |
| AV-6 | DEBUG: create + abandon (no dispose), force GC | `Abandoned` fires; a test observing it fails (SC-005) |
| AV-7 | Release: create + abandon | No functional difference; no signal (SC-005) |
| AV-8 | `AsEnumerableRegisteredToDispose()` fully enumerated | Underlying buffer returned exactly once at end of enumeration (SC-006, FR-012) |
| AV-9 | `httpContext.Response.RegisterForDispose(buffer)` then request completes | Buffer returned exactly once at request end (FR-011, SC-006) |
