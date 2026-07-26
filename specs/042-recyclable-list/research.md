# Phase 0 Research: RecyclableList<T> Pooled Collection

No `NEEDS CLARIFICATION` markers remained in the spec. Research below records the load-bearing design decisions and the .NET/BCL facts they rely on.

## R1. Backing store: `ArrayPool<T>.Shared` vs. a custom pool

- **Decision**: Use `System.Buffers.ArrayPool<T>.Shared` for renting/returning backing arrays.
- **Rationale**: It is the BCL standard, allocation-free to access, thread-safe for rent/return (the *collection* is not thread-safe, but the shared pool is), and available on every .NET 10 target including Blazor WASM. `Rent(minLength)` returns an array of **at least** the requested length (often larger — a power-of-two bucket), which is exactly the "capacity ≥ count" contract we need.
- **Alternatives considered**: A bespoke pool (more code, no benefit, breaks cross-project reuse); `MemoryPool<T>` (returns `IMemoryOwner<T>`/`Memory<T>`, heavier and unnecessary for a plain `T[]`-backed list).
- **Consequences**: Because `Rent` may return a larger array than requested, `Capacity` reflects the rented array's actual `Length`, and `Count` (not `Length`) bounds valid elements. On `Return`, pass `clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>()` so reference elements are nulled out (prevents the pool from pinning managed objects alive) while pure value-type buffers skip the clear for speed.

## R2. Growth strategy

- **Decision**: Mirror `List<T>` growth — double the capacity (starting from a small default, e.g. 4) until it meets the required minimum; on the first growth from empty, rent the default-or-required size.
- **Rationale**: Preserves the amortized-O(1) `Add` behavior callers expect from `List<T>`, and keeps behavioral parity (SC-001). Doubling minimizes the number of rent/return churns.
- **Alternatives considered**: Grow-by-exact (defeats amortization, more rentals); grow-by-1.5× (marginal, diverges from `List<T>` expectations). Rejected.
- **Consequences**: When the caller pre-sizes via the capacity constructor or an explicit `EnsureCapacity`, no growth (hence no rental) occurs while filling up to that count — satisfies SC-004/FR-002.

## R3. Disposal & idempotency

- **Decision**: `Dispose()` returns the current buffer to the pool exactly once, sets a `_disposed` flag, replaces the field with a shared empty array sentinel, and calls `GC.SuppressFinalize(this)`. A guard makes repeated `Dispose()` calls no-ops (FR-008).
- **Rationale**: Idempotent disposal is required so `using` + an explicit `Dispose()` (or double registration) can't double-return a buffer to the pool — a double-return is a correctness hazard because the pool could then hand the same array to two callers.
- **Alternatives considered**: Throwing on second dispose (hostile to `using`/registration patterns). Rejected.
- **Consequences**: After disposal the instance is invalid for further use; continued use is documented as unsupported (spec edge case). We do **not** guarantee post-dispose operations throw in Release (that would cost per-op checks); DEBUG builds MAY assert.

## R4. Abandonment (leak) detection — DEBUG only

- **Decision**: Declare a finalizer compiled only under `#if DEBUG`. If the finalizer runs (meaning the instance was GC'd without `Dispose()`), raise a **static** `RecyclableList.Abandoned` event (or invoke a static hook) carrying diagnostic info. In Release, no finalizer is emitted, so there is zero finalization cost and identical behavior. Tests subscribe to the hook and fail if it fires.
- **Rationale**: FR-007/SC-005 require abandonment to fail tests without affecting Release. A DEBUG-only finalizer is the standard idiom (used by e.g. pooled/rented-buffer helpers). `GC.SuppressFinalize` in `Dispose()` ensures correctly-disposed instances never trip it.
- **Alternatives considered**: Always-on finalizer with a runtime flag (adds finalization queue pressure in Release — rejected); analyzer-only detection (can't catch dynamic leaks — rejected as sole mechanism).
- **Consequences**: The `Abandoned` signal is process-global static state; tests must reset/observe it carefully (subscribe → force GC → assert). Because finalization is non-deterministic, the abandonment test forces `GC.Collect()` + `GC.WaitForPendingFinalizers()`.

## R5. `AsSpan()` over live contents

- **Decision**: Expose `Span<T> AsSpan()` returning `_array.AsSpan(0, _count)` — a view over the valid region only.
- **Rationale**: Zero-copy bulk read/write for hot paths; the source doc lists it in the API surface. The span is invalidated by any growth (buffer swap) — documented, same hazard as `CollectionsMarshal.AsSpan(List<T>)`.
- **Consequences**: Callers must not hold the span across mutations.

## R6. Extension helpers & the three canonical patterns

- **Decision**: A static `RecyclableListExtensions` provides:
  - `AsRecyclableList<T>(this IEnumerable<T> source)` — build a pooled list from a sequence (pre-sized when the source has a known count via `TryGetNonEnumeratedCount`).
  - `AsList<T>(this RecyclableList<T>)` — produce a `List<T>` without an extra copy when the source is already backed by a `List<T>`-compatible layout; otherwise copy once. (Per the doc's "avoid an extra copy for `List<T>` inputs" rule; in practice a `RecyclableList<T>` copies its live region into a new `List<T>` — the "no extra copy" guarantee applies to the `IEnumerable` overload when the input is already a `List<T>`.)
  - `AsIList<T>(this RecyclableList<T>)` — return the instance typed as `IList<T>`.
  - `AsEnumerableRegisteredToDispose<T>(this RecyclableList<T>)` — return a one-shot `IEnumerable<T>` that disposes the list when enumeration completes (self-disposing sequence, FR-012).
- **Rationale**: These realize use cases 2 and 3 from the source doc without baking web/DI concerns into the Shared type. The request-lifetime pattern (use case 2) is satisfied by the type simply being `IDisposable` — a WebAPI caller registers it via `HttpResponse.RegisterForDispose(...)`/`RegisterForDisposeAsync`, which lives in the WebAPI project, not Shared. The plan therefore keeps Shared free of ASP.NET dependencies and documents the registration pattern in the quickstart.
- **Alternatives considered**: Putting an ASP.NET-aware `RegisterForDispose` helper in Shared (would drag `Microsoft.AspNetCore` into a WASM-consumed library — rejected, violates the "no dependency unavailable to consumers" requirement FR-014).
- **Consequences**: The self-disposing enumerable is implemented with a `try/finally` iterator (`yield`) so disposal fires whether enumeration completes or the enumerator is disposed early (e.g. `foreach` break).

## R7. `AddRangeAsync`

- **Decision**: Provide `AddRangeAsync(IAsyncEnumerable<T>)` that awaits items and appends them, growing as needed.
- **Rationale**: Listed in the source doc's API surface; supports buffering async query results (use case 2). Kept minimal — `await foreach` + `Add`.
- **Consequences**: Not thread-safe; caller must not mutate concurrently.

## R8. Test framework & project shape

- **Decision**: New `ChefKnifeStudios.TransitJazz.Shared.Tests` xUnit v2 project, csproj copied from the existing `*.Tests` projects (`Microsoft.NET.Test.Sdk 17.*`, `xunit 2.*`, `xunit.runner.visualstudio 2.*`, `IsPackable=false`, `Nullable`+`ImplicitUsings` enabled), `ProjectReference` to Shared. Added to `ChefKnifeStudios.TransitJazz.sln`.
- **Rationale**: Consistency with the repo's two existing test projects; no new tooling. CI already builds the solution, so the tests run automatically (Principle V).
- **Alternatives considered**: Co-locating tests in an existing Server test project (wrong dependency direction — Shared shouldn't be tested through a Server assembly). Rejected.

## Open risks / notes

- Non-deterministic finalization makes the abandonment test inherently timing-sensitive; mitigate with forced GC + `WaitForPendingFinalizers` and a static latch rather than an event race.
- `ArrayPool` returns arrays ≥ requested; all size assertions in tests must compare against `Count`, never `Capacity`/`Length`.
