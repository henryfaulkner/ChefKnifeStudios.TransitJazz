# Quickstart: RecyclableList<T>

`RecyclableList<T>` is a drop-in `List<T>` replacement whose backing array is **rented from `ArrayPool<T>.Shared`** instead of heap-allocated. It realizes its benefit **only when disposed** — always use `using`.

Namespace: `using ChefKnifeStudios.MartaJazz.Shared.Collections;`

## Use case 1 — temporary method-scope collection (P1)

```csharp
using var buffer = new RecyclableList<int>(capacity: expectedCount); // pre-size to avoid mid-flight rentals
foreach (var x in source)
    buffer.Add(Transform(x));

Process(buffer.AsSpan());   // zero-copy read
// buffer disposes at end of scope -> backing array returned to the pool
```

## Use case 2 — API result buffer that outlives the method (P2)

Buffer DB/query rows to release the connection sooner, and tie the pool return to the **request** lifetime (registration lives in WebAPI, not Shared):

```csharp
var buffer = rows.AsRecyclableList();              // caller owns disposal
httpContext.Response.RegisterForDispose(buffer);   // returned to pool when the request ends
return buffer.AsIList();
```

## Use case 3 — returned sequence that disposes itself (P3)

```csharp
public IEnumerable<Foo> GetFoos()
{
    var list = ComputeFoos().AsRecyclableList();
    return list.AsEnumerableRegisteredToDispose();  // pool return happens when the caller finishes enumerating
}
```

## Rules of thumb

- **Always `using`** — an undisposed instance is no better than `List<T>` (and, in DEBUG, fails tests).
- **Pre-set capacity** when the count is known — avoids mid-flight rentals.
- **Not thread-safe** — synchronize if sharing across threads.
- Use `AsList()` (not LINQ `.ToList()`) when you need a `List<T>`.

## Verifying the implementation (manual + test walkthrough)

Run the new test project:

```powershell
dotnet test src/ChefKnifeStudios.MartaJazz.Shared.Tests/ChefKnifeStudios.MartaJazz.Shared.Tests.csproj
```

| # | Test | Confirms |
|---|------|----------|
| 1 | Add 100 items forcing growths → values/order match | SC-001 behavioral parity |
| 2 | Parity harness vs. `List<T>` across add/insert/remove/sort/search/clear | SC-001 (AV-2) |
| 3 | Pre-sized to 1000, add 1000, assert no growth past initial capacity | SC-004 (AV-3) |
| 4 | `using` growth workload returns all buffers | SC-002 (AV-4) |
| 5 | Double `Dispose()` is a no-op | SC-006/G3 (AV-5) |
| 6 | DEBUG: abandon + `GC.Collect()`+`WaitForPendingFinalizers()` → `Abandoned` fires → test fails on leak | SC-005 (AV-6) |
| 7 | `AsEnumerableRegisteredToDispose()` disposes after full enumeration | SC-006/FR-012 (AV-8) |
| 8 | Empty list: read/enumerate/clear/dispose don't throw | Edge cases |

**Allocation sanity check (SC-003)**: In a throwaway benchmark, accumulate a large sequence with `List<T>` vs. a pre-sized-then-grown `RecyclableList<T>` (disposed) and compare `GC.GetAllocatedBytesForCurrentThread()` deltas — the pooled version allocates measurably fewer backing arrays.

## Definition of done

- Both new source files + the new test project build under the solution; `dotnet test` for the Shared.Tests project is green.
- No new failures in the existing Server test projects (CI builds the whole solution).
- No new package references added to `ChefKnifeStudios.MartaJazz.Shared.csproj`.
