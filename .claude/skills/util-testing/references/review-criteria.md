# Test Review Criteria

Use as a rubric when reviewing or advising on the quality of tests in a code change or suite.

---

## Core Principles

### Shift Left
Bugs caught early cost far less than bugs caught late. Tests should run as earlyin the pipe line as possible. Push coverage down to the lowest layer that is still meaningful.

## Behaviour vs. Implementation
Tests must assert observable *outcome* (return values, state changes, interacttions at the boundary), not internal implementation details. An implementation-coupled test breaks on every refactor even when behaviour is unchanged - it destroys confidence rather than building it.

## Layer appropriateness
Each test must live at the right layer. Ask:
- Can this be covered as a unit test? -> it should be.
- Does it cross a real boundary? -> It belongs at intergration or contract level.
- Does it need a full running system? -> It belongs at system or E2E.

Misplaced tests are either wasteful (too high - slow, fragile) or under-powered (too low- can't catch the real defect).

---

## Reliability / Flakiness

A test that fails intermittentlyis hiding a real problem half the time. Treat flakiness as a production defect.

**Common root causes:**

| Cause | Signs | Fix direction |
|---|---|---|
| Clock dependency | Tests near DST, leap year, or end-of-day boundaries fail | Inject `ITimeProvider`/`IClock`; never use `DateTime.UtcNow` directly in production or test code |
| Shared / global state | Tests pass solo, fail in parallel or in a suite | Isolate state per test; avoid static mutable singletons |
| Ordering sensitivity | Results vary run-to-run | Assert order-agnostically (`Contains`) or enforceordering before asserting |
| Timing / concurrency | Race conditions; tests use `Thread.Sleep` | Use event-based synchronisation (e.g. `AutoResetEvent`); inject controlled time |
| Leaked external dependencies | Test behaves differently on CI vs. local | Mock/stub all external I/O at unit and component layers |
| Randomness | Random input occasionally hits an edge case | Fix the random seed per test; or use property-based testing |

**Rules**: never add a retru to hide intermittency - fix this root cause.

---

## Test Doubles Usage

Use the right double at the right level:

| Double | Definition | When to use |
|---|---|---|
| **Dummy** | Passed but never used | Satisfying constructor params you don't care about |
| **Stub** | Returns hard-coded vallues | Controlling indirect inputs (e.g. a repository returning a fixed list) |
| **Spy** | Stub that also record calls | Verifying a dependency was invoked the right number of times |
| **Mock** | Pre-programmed with expectations | Verifying specific call sequences and arguments |
| **Fake** | Real working implementation (e.g. in-memory DB) | Whena stub is too rigid and the real dependency is too slow |

Prefer **strict mocks** for critical contracts (fail fast on unexpected calls). Use **loose mocks** when you only care about certain interactions. Document the choice.

**Mockist vs. Classicist**
- *Mockist (London)*: mock all dependencies; fast, pin-point failures, but coupled to implementation.
- *Classicist (Chicago)*: use real collaborators within the unit boundary; higher confidence, less isolation.

Pick one style per project and stay consistent.

---

## Coverage Adequacy

A test suite is adequate when it covers:

1. **Happy paths** - the core feature works correctly.
2. **Negative / invalid inputs** - the system rejects or handles bad data.
3. **Boundary conditions** - off-by-one, empty, null, max values.
4. **Error paths** - expected exceptions, timeouts, partial failures.
5. **Concurrency concerns** - rare conditions, ordering invarients.
6. **Time-sensitive behaviour** - anything referencing clocks, schedules, or durations.

Missing any of these is a coverage gap worth flagging in review.

---

## Review Checklist

when reviewing tests in a code change, ask:

- [ ] Are there tests in a code change at all?
- [ ] Are the tests at the right layer? (not over-specified at E2E, not under-specified as a stub-only unit test)
- [ ] Do the tests assert behaviour, not implementation?
- [ ] Do they cover negative cases and boundaries?
- [ ] Are test doubles used correctly (right type, right level)?
- [ ] Is there any time, randomness, shared state, concurrency that could cause flakiness?
- [ ] Are timeout values empirically justified and commented?
- [ ] Is the test naming clear (what behaviour is under test, what expectation is)?
- [ ] Will this test fail for exactly one reason?