---
description: "Task list for RecyclableList<T> pooled collection"
---

# Tasks: RecyclableList<T> Pooled Collection

**Input**: Design documents from `/specs/042-recyclable-list/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/recyclable-list-api.md, quickstart.md

**Tests**: INCLUDED — the spec explicitly requests them (FR-015; each user story defines an Independent Test). Test tasks precede the implementation they cover within each story.

**Organization**: Grouped by user story (US1 = method-scope collection, US2 = request-lifetime buffer, US3 = self-disposing sequence) so each is independently implementable and testable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 / US2 / US3, or SETUP / FOUND / POLISH
- File paths are repo-relative. Assembly is `ChefKnifeStudios.TransitJazz.Shared`; namespace root `ChefKnifeStudios.MartaJazz.Shared`.

## Path Conventions

- Production code: `src/ChefKnifeStudios.MartaJazz.Shared/Collections/`
- Tests: `src/ChefKnifeStudios.MartaJazz.Shared.Tests/` (NEW project)
- Solution: `ChefKnifeStudios.TransitJazz.sln` at repo root

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the folder + the new test project so all later work has a home.

- [X] T001 Create the `Collections/` folder under `src/ChefKnifeStudios.MartaJazz.Shared/` (no new package references — BCL only; do NOT edit the Shared `.csproj`).
- [X] T002 Create the new test project `src/ChefKnifeStudios.MartaJazz.Shared.Tests/ChefKnifeStudios.MartaJazz.Shared.Tests.csproj`, copying the csproj shape of `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI.Tests/…csproj` (`net10.0`, `Nullable` + `ImplicitUsings` enabled, `IsPackable=false`, `Microsoft.NET.Test.Sdk 17.*`, `xunit 2.*`, `xunit.runner.visualstudio 2.*`) with a single `ProjectReference` to `..\ChefKnifeStudios.MartaJazz.Shared\ChefKnifeStudios.MartaJazz.Shared.csproj`.
- [X] T003 Add the new test project to `ChefKnifeStudios.TransitJazz.sln` (`dotnet sln add …`); confirm `dotnet build` on the solution succeeds with the empty project.

**Checkpoint**: Solution builds; empty test project is wired into CI.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The core `RecyclableList<T>` type — every user story depends on it. **⚠️ No user-story work can begin until this phase is complete.**

- [X] T004 [FOUND] Create `src/ChefKnifeStudios.MartaJazz.Shared/Collections/RecyclableList.cs` with the non-generic host `public static class RecyclableList` exposing the DEBUG-only `Abandoned` signal + `RecyclableListAbandonedInfo` (see contracts §"non-generic host"; guard the event and info-capture with `#if DEBUG`).
- [X] T005 [FOUND] In the same file (or a partial), declare `public sealed class RecyclableList<T> : IList<T>, IReadOnlyList<T>, IDisposable` with fields `_array`, `_count`, `_disposed`, `_version` and a shared empty-array sentinel; implement the three constructors (empty / `capacity` with `ArgumentOutOfRangeException` on negative / `IEnumerable<T>` with `ArgumentNullException` on null, pre-sizing via `TryGetNonEnumeratedCount`). Enforces INV-1.
- [X] T006 [FOUND] Implement pooled growth: a private `Grow(int min)` that rents `>= max(min, doubled)` from `ArrayPool<T>.Shared`, copies `[0.._count)`, and returns the previous buffer with `clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>()`; plus `EnsureCapacity(int)`. Enforces INV-2/INV-3/INV-4.
- [X] T007 [FOUND] Implement the `IList<T>` surface: `Count`, `Capacity`, `IsReadOnly`, indexer (bounds vs. `_count`), `Add`, `Insert`, `Remove`, `RemoveAt`, `Clear` (reset count + null used region for refs, keep buffer), `Contains`, `IndexOf`, `CopyTo`, and a `_version`-checked `GetEnumerator()` (both generic and non-generic). Behavioral parity with `List<T>` (G1).
- [X] T008 [FOUND] Implement the extended surface: `AddRange(IEnumerable<T>)`, `AddRangeAsync(IAsyncEnumerable<T>, CancellationToken)`, `Sort()`/`Sort(IComparer<T>?)`, `BinarySearch(T)`/`BinarySearch(T, IComparer<T>?)`, and `Span<T> AsSpan()` over `[0.._count)`.
- [X] T009 [FOUND] Implement lifecycle: idempotent `Dispose()` (return current buffer once, set `_disposed`, swap to empty sentinel, `GC.SuppressFinalize`) and, under `#if DEBUG` only, a finalizer that raises `RecyclableList.Abandoned` when `!_disposed`. Enforces INV-3/INV-5 (G3).
- [X] T010 [FOUND] Add XML doc comments to all public members, explicitly stating: not thread-safe (G5), always use `using`, `AsSpan()` invalidated by growth, and `Capacity` may exceed requested capacity.

**Checkpoint**: `RecyclableList<T>` compiles and is usable. User stories can now proceed in parallel.

---

## Phase 3: User Story 1 — Drop-in growable collection with pooled backing (Priority: P1) 🎯 MVP

**Goal**: A correct, `List<T>`-equivalent, disposable pooled list for method-scope use.

**Independent Test**: Add items forcing multiple growths; verify read/write/enumerate/remove/sort/search/clear match `List<T>`; verify disposal returns the buffer; verify pre-sizing avoids mid-fill rentals.

### Tests for User Story 1 ⚠️ (write first, expect fail until Phase 2 done)

- [X] T011 [P] [US1] Create `src/ChefKnifeStudios.MartaJazz.Shared.Tests/RecyclableListTests.cs`; add growth test — add 100 items to an empty list, assert `Count==100` and indices `0..99` preserve insertion order (AV-1, SC-001).
- [X] T012 [P] [US1] In `RecyclableListTests.cs`, add a parity harness that applies the same operation sequence (add/insert/remove/sort/search/clear/indexer) to a `RecyclableList<T>` and a `List<T>` and asserts identical observable results (AV-2, SC-001).
- [X] T013 [P] [US1] Add a pre-size test — `new RecyclableList<int>(1000)`, add 1000 items, assert `Capacity` never grows past the initial rental while filling (no mid-fill rental) (AV-3, SC-004).
- [X] T014 [P] [US1] Add edge-case tests — empty list read/enumerate/`Clear`/`Dispose` do not throw; negative capacity throws `ArgumentOutOfRangeException`; null source throws `ArgumentNullException`; `AsSpan()` over empty list is length 0.

### Implementation for User Story 1

- [X] T015 [US1] Run T011–T014 and fix any parity/growth/pre-size defects in `RecyclableList.cs` until green (implementation already landed in Phase 2; this task closes the loop for US1).

**Checkpoint**: US1 fully functional — the MVP pooled list is correct and disposable.

---

## Phase 4: User Story 2 — API result buffer that outlives the immediate scope (Priority: P2)

**Goal**: Buffer results into a pooled list whose pool-return is tied to an externally-owned (request) lifetime.

**Independent Test**: Buffer items, tie disposal to an external lifetime, verify the buffer returns to the pool exactly once when that lifetime ends (not when the creating method returns).

### Tests for User Story 2 ⚠️

- [X] T016 [P] [US2] Create `src/ChefKnifeStudios.MartaJazz.Shared.Tests/RecyclableListExtensionsTests.cs`; test `AsRecyclableList()` from a known-count `IEnumerable` pre-sizes and copies all items in order; caller-owned disposal returns the buffer once.
- [X] T017 [P] [US2] Add a double-dispose test — calling `Dispose()` twice is a no-op and returns the buffer exactly once (simulates method-return + external-lifetime-end double registration) (AV-5, SC-006, G3).
- [X] T018 [P] [US2] Add `AsList()` / `AsIList()` tests — `AsList()` yields a `List<T>` copy of the live region; `AsIList()` returns the same instance typed as `IList<T>`.

### Implementation for User Story 2

- [X] T019 [US2] Create `src/ChefKnifeStudios.MartaJazz.Shared/Collections/RecyclableListExtensions.cs` with `AsRecyclableList<T>`, `AsList<T>`, `AsIList<T>` (per contracts §RecyclableListExtensions), with XML docs noting caller-owned disposal for `AsRecyclableList`. Run T016–T018 to green.
- [X] T020 [US2] Document the request-lifetime registration pattern (`httpContext.Response.RegisterForDispose(buffer)`) in the `AsRecyclableList` XML doc and confirm Shared adds NO ASP.NET reference (registration stays in WebAPI — FR-014). (No Shared code beyond the doc note.)

**Checkpoint**: US1 + US2 both work independently; buffering + external-lifetime disposal verified.

---

## Phase 5: User Story 3 — Returned sequence that disposes itself when consumed (Priority: P3)

**Goal**: A returned `IEnumerable<T>` that returns its backing buffer to the pool automatically when enumeration completes — caller writes no disposal code.

**Independent Test**: Produce a self-disposing sequence, fully enumerate it, verify the buffer returns to the pool exactly once at end of enumeration; verify early `foreach` break still disposes.

### Tests for User Story 3 ⚠️

- [X] T021 [P] [US3] In `RecyclableListExtensionsTests.cs`, test `AsEnumerableRegisteredToDispose()` — full enumeration disposes the underlying list exactly once at the end (AV-8, SC-006, FR-012).
- [X] T022 [P] [US3] Add an early-termination test — breaking out of a `foreach` over the self-disposing sequence still disposes the underlying list (enumerator `Dispose` fires the `finally`).

### Implementation for User Story 3

- [X] T023 [US3] Add `AsEnumerableRegisteredToDispose<T>(this RecyclableList<T>)` to `RecyclableListExtensions.cs` as a `try { yield return each } finally { list.Dispose(); }` iterator. Run T021–T022 to green.

**Checkpoint**: All three canonical usage patterns are implemented and independently verified.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Leak detection, allocation validation, and final verification across all stories.

- [X] T024 [P] [POLISH] In `RecyclableListTests.cs`, add the DEBUG abandonment test — subscribe to `RecyclableList.Abandoned`, create + abandon (no dispose) an instance inside a non-inlined helper so the ref is collectible, then `GC.Collect()` + `GC.WaitForPendingFinalizers()`, and assert the static latch fired. Guard with `#if DEBUG` (inert in Release). **Deterministic seam only** — NO `Thread.Sleep`, NO retry wrapper; add a per-test timeout (`[Fact(Timeout=…)]`) with a comment stating the chosen value and why (AV-6/AV-7, SC-005).
- [X] T025 [P] [POLISH] Add the allocation-comparison test — measure `GC.GetAllocatedBytesForCurrentThread()` deltas for a large accumulation via `List<T>` vs. a disposed, pre-sized-then-grown `RecyclableList<T>`, and assert the pooled path allocates **strictly fewer** backing-array bytes. This is a **hard, build-failing `Assert`** (not advisory logging), with a **wide, documented safety margin** so normal CI/runtime variance never trips it. Unit-level (Tier 0), NO retry, NO sleep (SC-003, SC-007).
- [X] T026 [POLISH] Run `dotnet test src/ChefKnifeStudios.MartaJazz.Shared.Tests/…csproj` and `dotnet build` on the whole solution; confirm the new tests are green and no existing Server test project regressed (SC-007, Principle V).
- [X] T027 [POLISH] Walk quickstart.md's three usage snippets against the final API to confirm they compile as written; fix any signature drift in code or quickstart.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies — start immediately. T001→T002→T003 sequential (project must exist before adding to sln).
- **Foundational (Phase 2)**: depends on Setup. **BLOCKS all user stories.** T004→T005→T006→T007/T008→T009→T010 are mostly sequential (same file `RecyclableList.cs`).
- **User Stories (Phase 3–5)**: all depend on Phase 2. US1/US2/US3 are then independent and may proceed in parallel — note US2 and US3 both edit `RecyclableListExtensions.cs`, so their *implementation* tasks (T019, T023) serialize on that file even though their tests are parallel.
- **Polish (Phase 6)**: depends on all targeted stories complete.

### Within Each User Story

- Tests (T011–T014, T016–T018, T021–T022) written first and expected to fail until Phase 2 code exists.
- Extensions file (T019) before the self-disposing iterator (T023) since both live in `RecyclableListExtensions.cs`.

### Parallel Opportunities

- All `[P]` test tasks within a story (different assertions, and US1 tests vs. US2/US3 tests are in different files) can be authored in parallel.
- US2 tests (T016–T018) and US3 tests (T021–T022) touch the same file — treat as parallel *authoring* but a single sequential edit stream if one agent owns the file.
- Polish T024 and T025 are `[P]` (independent test methods).

---

## Implementation Strategy

### MVP First (US1 only)

1. Phase 1 Setup → 2. Phase 2 Foundational (the whole type) → 3. Phase 3 US1 → **STOP & VALIDATE** parity + disposal. This alone delivers the pooled-list memory benefit for the most common (method-scope) usage.

### Incremental Delivery

Setup + Foundational → US1 (MVP, method-scope) → US2 (request buffer + conversions) → US3 (self-disposing sequence) → Polish (leak detection + allocation proof). Each story adds value without breaking the previous.

---

## Notes

- No new package references anywhere — BCL `System.Buffers` only (FR-014).
- All size assertions compare against `Count`, never `Capacity`/`Length` (`ArrayPool.Rent` over-allocates).
- The abandonment finalizer and its test are DEBUG-only; Release must build and behave identically without them (INV-5).
- **Test reliability (per `C:\Projects\skill-util-testing`)**: all tests are unit-level / Tier 0 (run every commit); behavior-named, Arrange-Act-Assert, one logical assertion each, matching the repo's xUnit convention. No retry wrappers and no fixed `Thread.Sleep` waits anywhere — reliability comes from deterministic seams (forced GC + `WaitForPendingFinalizers`, source-/order-independent assertions, per-test timeouts). The allocation comparison is a hard gate with a wide documented margin.
- Per repo policy, do NOT auto-commit — leave staging/commit to the user.
