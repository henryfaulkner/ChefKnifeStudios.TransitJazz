# Feature Specification: RecyclableList<T> Pooled Collection

**Feature Branch**: `042-recyclable-list`
**Created**: 2026-07-13
**Status**: Draft
**Input**: User description: "docs/RECYCLABLE_LIST.md implement in the ChefKnifeStudios.MartaJazz.Shared C# project"

## Clarifications

### Session 2026-07-13

Clarifications below apply the testing methodology in `C:\Projects\skill-util-testing` (test classification / pyramid, test constructs, and reliability guidance) to this feature's test strategy.

- Q: How should the two flaky-prone tests (DEBUG abandonment via non-deterministic finalization, and the SC-003 allocation-delta comparison) be made reliable? → A: Deterministic seams, no retries — the abandonment test forces `GC.Collect()` + `GC.WaitForPendingFinalizers()`, observes a static latch (never a sleep), and carries a per-test timeout; the allocation test measures `GC.GetAllocatedBytesForCurrentThread()` deltas. No retry wrappers on any test.
- Q: Which test layers should this feature's tests occupy, per the test-pyramid guidance? → A: Unit-only (Tier 0) — every behavior (parity, growth, disposal, extensions, abandonment, allocation) is proven with fast, isolated unit tests that run every commit; there is no separate perf/soak tier.
- Q: Should the SC-003 allocation-reduction assertion be a hard CI gate or advisory? → A: Hard assert with a generous margin — the pooled path MUST allocate strictly fewer backing-array bytes than `List<T>`, with a documented safety margin wide enough that normal CI/runtime variance never trips it.

## User Scenarios & Testing *(mandatory)*

The "users" of this feature are the developers of the TransitJazz codebase (server API, worker, and any shared consumers). The feature delivers a reusable collection type that behaves like the standard growable list but reuses its internal storage from a shared pool so that high-throughput code paths produce far less garbage for the runtime to reclaim.

### User Story 1 - Drop-in growable collection with pooled backing (Priority: P1)

A developer needs a temporary, method-scoped collection to accumulate and process items. They reach for the pooled list, wrap it in a scoped-disposal block, add items, enumerate or transform them, and let it release its backing storage back to the pool at the end of the scope — without changing any of the familiar list-style calls they already know.

**Why this priority**: This is the core value of the feature and the foundation every other use case builds on. Without a correct, list-compatible collection whose backing storage is rented and returned, nothing else matters. It is independently shippable as an MVP: a correct pooled list that behaves identically to the standard list for all common operations, plus disposal, already delivers the memory benefit for the most common (method-scope) usage.

**Independent Test**: Can be fully tested by creating an instance, adding a mix of items that forces one or more capacity growths, verifying that read/write/enumerate/remove operations produce results identical to the standard list for the same input sequence, and confirming that disposal returns the backing storage to the pool. Delivers value because any temporary-collection site can adopt it immediately.

**Acceptance Scenarios**:

1. **Given** a new empty pooled list, **When** the developer adds enough items to exceed the initial capacity multiple times, **Then** all items are retained in insertion order and every element is readable by index exactly as with a standard list.
2. **Given** a pooled list populated with items, **When** the developer enumerates, indexes, inserts, removes, sorts, searches, and clears it, **Then** the observable results match those of a standard list performing the same operations on the same data.
3. **Given** a pooled list created inside a scoped-disposal block, **When** the block exits, **Then** the backing storage is returned to the shared pool and only the small list wrapper remains for normal reclamation.
4. **Given** a pooled list whose count is known in advance, **When** the developer pre-sizes it to that count before adding items, **Then** no additional pooled storage is rented while adding up to that count.

---

### User Story 2 - API result buffer that outlives the immediate scope (Priority: P2)

A developer buffering the results of a data query wants to release the underlying data connection sooner by copying rows into a pooled buffer, while ensuring the buffer's backing storage survives for the full lifetime of the request and is returned to the pool only when the request completes.

**Why this priority**: This is the second canonical usage and the one that motivated the feature for the high-throughput feed/status endpoints, but it depends on the correct core collection (Story 1) existing first. It is independently testable and independently valuable for request-scoped buffering.

**Independent Test**: Can be tested by buffering a set of items into a pooled list, registering the instance for disposal against a request-scoped lifetime, and verifying the backing storage is returned exactly once when that lifetime ends (and not before, even though the creating method has already returned).

**Acceptance Scenarios**:

1. **Given** a pooled list buffering query results, **When** the creating method returns but the request is still in flight, **Then** the buffered items remain valid and readable.
2. **Given** a pooled list registered against a request lifetime, **When** the request completes, **Then** the backing storage is returned to the pool exactly once.

---

### User Story 3 - Returned sequence that disposes itself when consumed (Priority: P3)

A developer wants to return a lazily-consumed sequence to a caller without forcing the caller to manage disposal explicitly, such that the pooled backing storage is returned automatically once the caller finishes enumerating the sequence.

**Why this priority**: This is a convenience/ergonomics layer over the core collection for the "returned sequence" pattern. It is the least critical of the three canonical uses and depends on Stories 1–2 being sound, so it is prioritized last while still being independently demonstrable.

**Independent Test**: Can be tested by producing a self-disposing sequence from a pooled list, fully enumerating it, and verifying the backing storage is returned to the pool once enumeration completes — with the caller writing no disposal code.

**Acceptance Scenarios**:

1. **Given** a self-disposing sequence produced from a pooled list, **When** the caller fully enumerates it, **Then** the backing storage is returned to the pool automatically at the end of enumeration.
2. **Given** a self-disposing sequence, **When** the caller enumerates it, **Then** the caller is not required to write any explicit disposal handling for the pooled storage.

---

### Edge Cases

- **Empty collection**: Reading, enumerating, clearing, and disposing an empty instance must behave exactly as a standard empty list and must not fail when returning (a possibly empty) backing store to the pool.
- **Zero / minimal capacity growth**: The first add on an empty instance and repeated growths must always yield a backing store large enough for the new count, and the previous backing store must be returned to the pool as part of each growth.
- **Abandoned (never disposed) instance**: An instance that is never disposed must still function correctly for the caller; its backing store is simply reclaimed normally, forfeiting the pooling benefit rather than corrupting data.
- **Leak detection in test builds**: In debug/test builds, abandoning an instance without disposing it must be surfaced (a signal/notification) so that leaks fail tests, without affecting release-build behavior.
- **Double disposal**: Disposing an instance more than once must be safe and must not return the same backing store to the pool twice.
- **Use after disposal**: Continuing to use an instance after it has been disposed is not supported; the behavior must be defined (predictable failure or documented invalidity) rather than silently returning pooled storage that may now be in use elsewhere.
- **Cross-thread sharing**: The collection is not safe for concurrent mutation from multiple threads; this constraint must be documented and callers are responsible for synchronization when sharing.
- **Conversion to a standard list**: Producing a standard list from an instance must avoid an unnecessary extra copy when the source is already backed by standard-list-compatible storage.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The collection MUST expose the full standard growable-list contract (indexed read/write, count, add, add-range, insert, remove, remove-at, clear, contains, index-of, enumerate) with behavior indistinguishable from the standard list for equivalent inputs.
- **FR-002**: The collection MUST support construction as empty, with a pre-set capacity, and from an existing sequence of items.
- **FR-003**: The collection MUST back its storage with rented buffers from the shared array pool rather than freshly heap-allocated arrays.
- **FR-004**: On capacity growth, the collection MUST rent a larger buffer from the pool, copy existing items into it, and return the previous buffer to the pool.
- **FR-005**: The collection MUST be disposable; on disposal it MUST return its current backing buffer to the pool, leaving only the lightweight wrapper for normal reclamation.
- **FR-006**: The collection MUST remain correct when never disposed — an abandoned instance forfeits the pooling benefit but MUST NOT corrupt data or double-return buffers.
- **FR-007**: In debug/test builds, the collection MUST surface a detectable signal when an instance is abandoned (finalized without having been disposed) so that leaks can fail automated tests; this instrumentation MUST NOT alter release-build behavior or results.
- **FR-008**: Disposal MUST be idempotent — disposing more than once MUST NOT return a buffer to the pool more than once.
- **FR-009**: The collection MUST provide the additional list-family operations described by the source document, including range-append (synchronous and asynchronous), sort, binary search, and a span view over the live contents.
- **FR-010**: The feature MUST provide conversion helpers to obtain a pooled list from an existing sequence, to obtain a standard list from a pooled list without an unnecessary copy when the source is already standard-list-compatible, to obtain the standard list interface, and to obtain a self-disposing sequence.
- **FR-011**: The feature MUST support a request-lifetime buffering pattern where the instance's disposal can be tied to an externally-owned lifetime so its backing buffer is returned exactly once when that lifetime ends, not when the creating method returns.
- **FR-012**: The feature MUST support a returned-sequence pattern where a produced sequence returns its backing buffer to the pool automatically once enumeration completes, requiring no explicit disposal by the consumer.
- **FR-013**: The collection MUST NOT be required to be thread-safe; the not-thread-safe constraint MUST be documented so callers know to synchronize when sharing across threads.
- **FR-014**: The feature MUST live in the shared library so it is consumable by the server, worker, and any other project referencing that shared library, and MUST NOT introduce a runtime dependency that is unavailable to those consumers.
- **FR-015**: The feature MUST be covered by automated tests that verify list-equivalent behavior, capacity-growth buffer rent/return, disposal, idempotent disposal, the request-lifetime and self-disposing-sequence patterns, and abandonment detection in test builds.

### Key Entities *(include if feature involves data)*

- **Pooled list**: A growable, index-addressable, disposable collection of elements of a single element type. Key attributes: current item count, current capacity (size of the rented backing buffer), and a disposed state. Relationships: holds a backing buffer borrowed from the shared pool; presents itself through the standard list interface; can be produced from or converted to a standard sequence/list.
- **Backing buffer**: A rented block of contiguous storage borrowed from the shared pool that holds the collection's elements. Its size is at least the current count; it is replaced (and the old one returned) on growth and returned on disposal. Never owned outright by the collection — always borrowed.
- **Abandonment signal**: A debug/test-build-only notification raised when an instance is reclaimed without having been disposed, used to fail tests on leaks. Has no effect in release builds.
- **Self-disposing sequence**: A one-shot enumerable view produced from a pooled list that returns the backing buffer to the pool when its enumeration completes, transferring lifetime ownership to the act of consumption.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For every common list operation on identical input sequences, the pooled list produces results identical to the standard list in 100% of tested cases (no observable behavioral difference for callers).
- **SC-002**: When used with scoped disposal, a workload that grows the collection N times returns N previous backing buffers to the pool and one final buffer on disposal — i.e., zero backing buffers are abandoned to normal reclamation across the workload.
- **SC-003**: A representative high-throughput accumulation workload run with the pooled list (properly disposed) allocates **strictly fewer** backing-array bytes than the same workload run with the standard list, measured by the runtime's per-thread allocated-bytes counter. This is a **hard, build-failing assertion** with a documented safety margin wide enough that normal CI/runtime variance never trips it (advisory-only logging is NOT sufficient).
- **SC-004**: Pre-sizing the collection to a known final count results in zero mid-workload buffer rentals while filling up to that count in 100% of tested cases.
- **SC-005**: In debug/test builds, 100% of abandoned (undisposed) instances are detected and cause the associated test to fail; in release builds, abandonment produces no functional difference. The detection test MUST be deterministic — it forces finalization (`GC.Collect()` + `GC.WaitForPendingFinalizers()`) and observes a static latch/signal rather than sleeping, and carries a per-test timeout; it MUST NOT use a retry wrapper.
- **SC-006**: The request-lifetime and self-disposing-sequence patterns each return the backing buffer to the pool exactly once — never zero times (leak) and never twice (double-return) — across all tested scenarios.
- **SC-007**: The feature builds and all its tests pass within the existing shared library and its test project, with no new failures introduced elsewhere in the solution. All tests are **unit-level (run every commit)** — there is no separate performance/soak tier — and NONE use retry wrappers or fixed `Thread.Sleep` waits (reliability is achieved by deterministic seams: fixed forced-GC, order-independent or source-ordered assertions, and per-test timeouts).

## Assumptions

- The pooled list is a developer-facing utility of the codebase; the "users" are the project's own developers rather than end users of the transit application.
- The three canonical usage patterns (method-scope, request-buffer, returned-sequence) described in the source document define the intended scope; no additional collection abstractions (e.g., dictionaries, sets, concurrent variants) are in scope for this feature.
- The shared array pool referenced by the source document is the standard shared pool available to all consumers of the shared library; no custom pool implementation is in scope.
- Thread safety is intentionally out of scope for the collection itself; callers requiring concurrent access will add their own synchronization.
- The abandonment/leak-detection mechanism is intended for debug/test configurations only and is expected to compile out (or be inert) in release configuration so production behavior and performance are unaffected.
- The source document `docs/RECYCLABLE_LIST.md` is the authoritative description of the intended API surface and behavior; where it lists API members by name, those members are expected to exist with equivalent behavior.
- The feature is additive to the shared library and does not modify existing shared types or their serialization behavior.
- **Test strategy** (per the `C:\Projects\skill-util-testing` methodology): tests live entirely at the **unit** layer (base of the pyramid, execution Tier 0, run every commit), since a self-contained pooled collection needs no cross-boundary/system coverage. Tests follow behavior-named conventions and Arrange-Act-Assert with one logical assertion per test, mirroring the repo's existing xUnit convention. Reliability is designed in up front: deterministic forced-GC for the abandonment test (no sleeps, no retries, per-test timeout), size assertions made against `Count` (never the over-allocating pooled capacity), and a wide, documented margin on the allocation comparison so it is a hard gate without being flaky.
