---
name: util-testing
description: Plan, write, or review automated tests with an appropriate test layer, reliable isolation, and project-native test conventions.
---

# Testing guidance

Use the repository's existing test framework and conventions. Prove behaviour at the
lowest layer that gives meaningful confidence; do not add broad tests when a focused one
will establish the requirement.

For test design, read [classification.md](references/classification.md). For test code
and doubles, read [constructs.md](references/constructs.md) and
[test-doubles.md](references/test-doubles.md). Read [reliability.md](references/reliability.md)
before introducing time, concurrency, ordering, randomness, shared state, or environment
dependence. Use [review-criteria.md](references/review-criteria.md) when reviewing tests,
and [test-types.md](references/test-types.md) when a more specific test category matters.

Prefer deterministic tests, explicit assertions of observable behaviour, and targeted
coverage of the relevant happy path, failure path, and boundary conditions. Treat a flaky
test as a defect to diagnose, not a reason to add retries.
