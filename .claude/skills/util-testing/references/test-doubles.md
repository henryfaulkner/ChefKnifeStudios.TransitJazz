# Knowledge - Test Doubles & Isolation Strategy

Load when deciding whether to fake a dependency and what kind of double to use.

---

## The System under Test (SUT)

The SUT is the "thing" a test tests - a function, class, set of classes, or a
whole deployed solution depending on the layer. The SUT *has* dependencies;
those are not part of the SUT.

- **Inactive dependencies** - can be replaced with a double. Changes to them
  should *never* fail the test.
- **Active dependencies** - can't easily be replaced (e.g. static/extension
  methods, the language runtime). They're effectively part of what you're testing.

**Goal:** a test failure should be caused by a change to the SUT or its active
dependencies - nothing else.

## The double hierarchy

From simplest to most complex (each is a kind of the previous):

| Double | What it is | Use when |
|---|---|---|
| **Dummy** | Passed around but never used | Only to satify a constructor/signature (e.g. an unused metrics service). |
| **Stub** | Returns hardcoded, test-specific values | You need the SUT to receive canned inputs - a fixed entity, or a thrown exception. |
| **Spy** | A stub that records how it was used | You want to assert something happened *and* control what it returns (e.g. all count). |
| **Mock** | Pre-programmed with expectations of that calls it should receive; can self-verify | You're seerting the *interaction* - which calls, with which args, is the behaviour. |
| **Fake** | A working lightweight implementation | You need real-ish behaviour cheaply (in-memory DB, dictionary-backed repo). Costliest; use sparingly. |

Prefer the simplest double that does the job. Reach for a **fake** only when a
stub/mock would make the test unreadable or wouldn't exercise real logic.

## Strict vs. loose mocks

- **Strict** - allows only explicitly configured behaviour; unexpected calls fail
  the test. Precise, but brittle to unrelated changes.
- **Loose** - tolerates unexpected calls. More resilient, but can hide misuse.

Default to loose unless the interaction contract is exactly what you're testing.

## Mockist (London) vs. classicist (Chicago)

| | Mockist / London | Classicist / Chicago |
|---|---|---|
| Isolation | Mock all collaborators | Use real collaborators |
| Asserts on | Interactions | Resulting state |
| Failure locality | Pinpoints the exact unit | Broader, higher-confidence |
| Speed to write | Faster | Slower |
| Confidence | Lower (mocks can drift from reality) | Higher |

Neither is "better." Pick one **per project/suite** based on the app and the
team's experience, and **stick with it** — don't switch more than ~30% into a
project without a strong reason. Note: component tests as defined here are
essentially classicist; a classicist team rarely needs both layers, while a
mockist team may see some overlap (a minor cost).

## Rules of thumb

- Double **inactive** dependencies only; never build tests whose pass/fail
  depends on incidental collaborators.
- Don't over-mock: mocking everything produces tests that verify your mocks, not
  your system. This is the classicist critique of the mockist style — weigh it.
- A test that breaks whenever *unrelated* code changes is a smell — you've
  probably coupled it to implementation detail via mocks.
