# Phase 1 Data Model: RecyclableList<T> Pooled Collection

This is a data-structure feature, so the "data model" is the type's internal state and invariants rather than persisted entities.

## Entity: `RecyclableList<T>` (`: IList<T>, IReadOnlyList<T>, IDisposable`)

### Fields (internal state)

| Field | Type | Meaning |
|-------|------|---------|
| `_array` | `T[]` | The current backing buffer, **rented from `ArrayPool<T>.Shared`** (or a shared empty sentinel when count 0 / disposed). |
| `_count` | `int` | Number of valid elements. `0 <= _count <= _array.Length`. |
| `_disposed` | `bool` | True once the backing buffer has been returned to the pool. |
| `_version` | `int` | Mutation counter for enumerator invalidation (parity with `List<T>`). |

### Public surface (state-affecting members)

| Member | Category | Notes |
|--------|----------|-------|
| `Count` | read | `_count`. |
| `Capacity` | read (`_array.Length`) | May exceed the requested capacity because `Rent` rounds up. |
| `this[int index]` | read/write | Bounds-checked against `_count`, not `Capacity`. |
| `Add(T)` | mutate | Grows if `_count == _array.Length`. |
| `AddRange(IEnumerable<T>)` | mutate | Pre-grows when count known. |
| `AddRangeAsync(IAsyncEnumerable<T>)` | mutate (async) | `await foreach` + append. |
| `Insert(int, T)` / `RemoveAt(int)` / `Remove(T)` | mutate | Shift within `_array[0.._count]`. |
| `Clear()` | mutate | Resets `_count = 0`; clears references in the used region for GC; keeps the buffer. |
| `Contains` / `IndexOf` | read | Linear scan over `[0.._count)`. |
| `CopyTo(T[], int)` | read | Standard `IList<T>` copy. |
| `Sort()` / `Sort(IComparer<T>)` | mutate | In-place over `[0.._count)`. |
| `BinarySearch(T)` | read | Over `[0.._count)`; caller ensures sorted. |
| `AsSpan()` | read (view) | `_array.AsSpan(0, _count)`; invalidated by growth. |
| `GetEnumerator()` | read | Throws on concurrent modification via `_version`. |
| `Dispose()` | lifecycle | Idempotent; returns buffer once; `SuppressFinalize`. |

### Constructors

1. `RecyclableList()` — empty; `_array` = shared empty array (no rental until first add).
2. `RecyclableList(int capacity)` — rents a buffer ≥ `capacity`; `_count = 0`. (Pre-sizing path, FR-002/SC-004.)
3. `RecyclableList(IEnumerable<T> source)` — pre-sizes when count is known, then appends `source`.

### Invariants

- **INV-1 (capacity)**: `0 <= _count <= _array.Length` at all times.
- **INV-2 (borrowed buffer)**: `_array`, when non-empty, is always a live rental from `ArrayPool<T>.Shared` — never a `new T[]` retained past a growth. On growth the old buffer is returned; on dispose the current buffer is returned.
- **INV-3 (single return)**: Every rented buffer is returned to the pool **exactly once** — on growth (old buffer) or on dispose (current buffer). `_disposed` prevents a second return. (Backs SC-006.)
- **INV-4 (reference hygiene)**: On `Return`, reference-containing element types are cleared (`clearArray: true`) so the pool does not pin managed graphs; `Clear()` likewise nulls the used region.
- **INV-5 (Release inertness)**: The abandonment finalizer exists only under `#if DEBUG`; Release builds emit no finalizer and behave identically aside from lacking leak detection.

### State transitions (lifecycle)

```
[new] --add/grow--> [populated, buffer rented]
   |                      |
   | (never disposed)     | Dispose()
   v                      v
[abandoned]            [disposed: buffer returned once, _disposed=true]
   |                      |
   | GC finalize (DEBUG)  | Dispose() again --> no-op (INV-3)
   v
 raise RecyclableList.Abandoned  (Release: no finalizer, silent normal GC)
```

## Entity: `RecyclableListExtensions` (static)

| Extension | Signature | Behavior |
|-----------|-----------|----------|
| `AsRecyclableList` | `IEnumerable<T> -> RecyclableList<T>` | Pre-size via `TryGetNonEnumeratedCount`, then append. Caller owns disposal. |
| `AsList` | `RecyclableList<T> -> List<T>` | Copy live region into a new `List<T>` (single copy). The "no extra copy for `List<T>` inputs" guarantee applies to the `IEnumerable`→`List` fast path, not to a pooled source. |
| `AsIList` | `RecyclableList<T> -> IList<T>` | Identity cast. |
| `AsEnumerableRegisteredToDispose` | `RecyclableList<T> -> IEnumerable<T>` | `try { yield each } finally { list.Dispose() }` — disposes when enumeration completes or the enumerator is disposed. |

## Entity: `RecyclableList.Abandoned` signal (static, DEBUG-only)

| Aspect | Detail |
|--------|--------|
| Shape | Static event/hook `event Action<AbandonedInfo>? Abandoned` on a non-generic `RecyclableList` host type (so all `T` share one sink). |
| Fires when | A DEBUG-built instance is finalized without having been disposed. |
| Payload | Diagnostic info (e.g. captured allocation stack or a type/marker) sufficient to identify the leak site in a failing test. |
| Release | Not compiled — no finalizer, no event invocation. |

## Validation rules (from requirements)

- `capacity` constructor argument MUST be `>= 0` (throw `ArgumentOutOfRangeException` on negative — parity with `List<T>`).
- Indexer / `Insert` / `RemoveAt` bounds are validated against `_count`.
- `AddRange`/constructor `source` MUST NOT be null (throw `ArgumentNullException` — parity with `List<T>`).
