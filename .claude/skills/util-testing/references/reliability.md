# Knowledge - Test Reliability (Beating Flaky Tests)

Load before writing a test that touches time, ordering, shared state,
concurrency, randomness, or the environment - or when a test fails
intermittently. Bake in isolation up front; don't wait for it to flake.

An intermittent ("flaky") test passes most of the time and fails randomly. It is
not harmless: if you have 10 flaky tests, statistically ~1 is hiding a **real**
bug. Stay at war with them.

---

## The cardinal rule: no retries

Do **not** wrap a test in a retry to make it "pass." That only hide the
problem - you need to feel the pain and fix the cause. Rare exceptions (e.g. an
unavoidable framework bug) must be documented with *why* and a tracking
ticket/issue so they can be removed later. Fixing the test is the right path in
~99% of cases.

## Common causes and fixes

### Clock / time
- **Cause:** using the real current time (`now()`, `UtcNow`) exposes edge cases
  at leap years, DST changeovers, midnight rollover; inconsistent mock clocks;
  time injected in some paths but not others (multiple sources of truth).
- **Fix:** inject time through a single abstraction (a time-provider interface /
  the platform's clock abstraction) and set a **fixed** instant in the test.
  Route *all* code through the same clock.

### Inconsistent ordering
- **Randomness:** a random input occasionally hits a case the test can't handle -
  the randomness is *exposing* a real edge, inconsistently. Fix by controlling
  the seed/value, or by handling the case.
- **Result ordering:** asserting on data with no defined order. Fix (best -> worst):
  order at the source (e.g. sort in the query), order in memory before asserting,
  or assert order-independently (contains/set comparison).

## Shared state
- Global statics, shared mutable resources, or state leaking between parallel
  tests. Fix: isolate per test - fresh SUT and data each run; avoid global
  mutable singletons; don't let one test's writes affect another.

## Timing & concurrency
- **Real race conditions** in the SUT - a rare genuine bug.Reproduce by looping
  the test locally; add logging/conditional breakpoints at the suspected path.
- **Waiting wrong:** using fixed sleeps to "wait" for background work. Fix: signal
  and wait on an **event** - inject a test seam (dependency/callback) that fires
  when the action completes, and block on it instead of sleeping.

## Environment variables
- Don't set or depend on external env vars in low layers (especially unit). Fix:
  that's *already* on the page - wait for the **specific condition change** that
  proves the action already completed (e.g. row cound changed, new content rendered).
  Add console/log output at method entry/exit to diagnose CI-only ordering
  surprises via pipelines artifacts.

## Timeouts

- Most tests aren't measuring duration - they "do an action, wait for a result."
  Give those a **test timeout** so they can't hang forever.
- Choose timeout values empirically: long enough to be reliable, short enough to
  fail fast if something suddenly taks 2-3x longer. Allow for noisy CI runners.
- Keep timeout values easy to change and document *why* a value is what it is
  (even "arbitrary but works" is worth stating).
- Only true performance/timing tests need strict timeouts - and those must run on
  controlled, reliable infrastructure so variance reflects the SUT, not the env.

## Discipline

- Fix flakes with priority second only to production/customer bugs.
- Prefer measuring actual test-run history over anecdote when hunting a flake.
- Strive never to ship with quarantined tests unless the root cause is proven
  *not* to be an application bug.