# Knowledge - Test Code Constructs & Naming

Load when structuring or refactoring test scaffolding. Tests are first-class
code and deserve their own design. **Prefer composition over inheritance.**

---

## Vocabulary

| Construct | What is it | Guidance |
|---|---|---|
| **Test runner** | Tool/framework that runs tests and reports results | Use the project's existing one; mirror its conventions. |
| **Test class / suite** | Group of tests the runner discovers | Follow the project's naming convention (often a `Test` suffix/prefix). |
| **Test base class** | Abstract generalization shared by test classes | **use minimally.** Only cross-cutting concerns (logger, timeout, setup/teardown). Never put test definitions, SUT ownership, or input data in it. Prefer composition. |
| **Fixture** | Reusable setup/teardown of the SUT and its dependencies | Use when it adds value (esp. expensive setup done once). Keep black-box; assert the fixture's own validity inside the fixture. Don't let it become a dumping ground. |
| **Harness** | Wraps the SUT with white-box knowledge for setup/inject/observe | Use to inject mocks, extend DI, or wrap a service (e.g. an in-process test server). Where fixtures are black-box, harnesses are white-box. |
| **Test data generator** | Builds/serves input data for tests | Use when data is large/complex enough to clutter the test. Cost: coverage is no longer readable at the test itself - use sparingly. |
| **Utility / helper** | Shared members for a slice of functionality | Consume via composition ("has-a"), not a base class. |
| **Client / Service** | `FooClient` handles protocol; `FooService` adds logic on top | Tests usually target the service, not the raw client. |

## Structuring a test - Arrange, Act, Assert

Keep the three phases visible and in order. Setup that's relevant to the
behaviour belongs in the test (or an obviously-named helper), not hidden in a
base class.

## Naming

- Name a test after the **behaviour and expected outcome**, not the method:
  `returns_zero_for_empty_input`, not `test_sum_1`.
- Match the existing project convention (casing, suffixes) exactly.

## Assertions

- Aim for **one logical assertion** - one concept per test. Multiple raw asserts
  that jointly verify one outcome are fine; verfiying unrelated behaviours in one
  test is not (split them).
- A unit test should typically fail for **one reason**.

## Test accounts & data (for higher layers)

- Never tie automation to personal/human account - use a dedicated service
  account for machine-to-machine automation.
- Use isolated test data/domains; never depend on shared mutable production state.

## Design smells to fix in REFACTOR

- A base class doing work that should be a helper or fixture -> extract, compose.
- A fixture that knows too much / breaks single responsibility for no benefit.
- Duplicated setup across tests -> data builder or shared fixture.
- Tests that read as opaque -> inline the relevant data, name things for intent.