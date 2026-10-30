---
name: speckit-auto
description: Orchestrates specification, planning, testing enrichment, and task generation with sequential Sol 6.1 High effort subagents. Use when the user invokes speckit-auto or asks to automate this Spec Kit workflow for a feature using a source design document.
---

# Spec Kit Auto

Turn a feature and its source design document into implementation-ready Spec Kit artifacts. Run four fresh phase subagents in order: **specify → plan → testing enrichment → tasks**. Stop after task generation; implementation requires a separate request.

## Inputs

```text
$speckit-auto feature: "{FEATURE}" design: "{DESIGN_DOCUMENT}" [guidance: "{GUIDANCE}"]
```

Accept equivalent natural language and documents supplied as attachments or earlier in the conversation. `{FEATURE}` is the main per-run variable: a feature name plus any requested scope. `{DESIGN_DOCUMENT}` is the originating design document's path, supplied contents, or accessible resource. `{GUIDANCE}` is optional additional direction. These are substitution tokens, never literal feature names or paths.

Resolve inputs from the invocation and available conversation context; do not ask for information already supplied. Read the source document fully. Resolve local paths against the repository root and pass absolute paths to subagents; supply the full contents when the source has no accessible path. Ask only for a missing or ambiguous feature/source, or a consequential unresolved scope decision. Do not silently reuse an unrelated feature's document.

## Prepare and delegate

1. Read repository `AGENTS.md` instructions and the current plan it references. Confirm `.specify/` and these dependencies are available: `speckit-specify`, `speckit-plan`, [util-testing](../util-testing/SKILL.md), and `speckit-tasks`. Resolve the Spec Kit skills' installed `SKILL.md` paths from the available skill catalog and include those paths in phase messages. If a required dependency or the source document cannot be read, report the specific blocker before starting a phase.
2. Record the initial working tree state. Preserve existing user changes. Never create commits or execute commit-producing hooks, including optional or mandatory Spec Kit hooks; pass this restriction to every subagent. Preserve unrelated `AGENTS.md` content when planning updates its Spec Kit marker.
3. For **each** phase call `collaboration.spawn_agent` with `model: "gpt-6.1-sol"`, `reasoning_effort: "high"`, and `fork_turns: "none"`. Use task names such as `specify`, `plan`, `testing`, and `tasks`. If this model or delegation is unavailable, report the blocker rather than silently switching models or running the phase yourself.
4. Construct each phase message from the shared handoff below plus its phase prompt. Substitute all tokens before dispatch. Wait for completion and inspect the produced artifacts before spawning the next phase. Do not run phase workers concurrently.
5. On a clarification request, obtain the user's answer through the parent and resume the same phase with that answer. On a failed gate, return the concrete issue to that phase for correction. Do not advance while a required gate remains unresolved. If progress requires unavailable input or access, report completed artifacts and the exact blocker.

### Shared handoff for every phase

```text
Repository root: {REPO_ROOT}
Feature: {FEATURE}
Primary design source: {DESIGN_DOCUMENT_ABSOLUTE_PATH_OR_FULL_CONTENTS}
User guidance and resolved clarifications: {GUIDANCE_AND_ANSWERS}
Active feature directory and prior artifacts: {RESOLVED_PATHS_OR_NOT_CREATED_YET}
Prior phase results and remaining issues: {HANDOFF}

Read and follow repository AGENTS.md. Read the named skill's SKILL.md and execute its workflow, including applicable references; naming a skill alone does not execute it. Use the source document as the main feature-design input, applying explicit user direction and repository constraints. Surface material conflicts rather than silently changing scope. Never create commits or run commit-producing hooks. Preserve existing user changes. Work only on this phase's documentation and required Spec Kit metadata; do not implement production code or write feature test code. Return completion status, absolute artifact paths, checks performed, assumptions, and unresolved questions/blockers. Return questions to the parent without inventing answers that require user input.
```

## 1. Specify

Phase prompt:

> Run `$speckit-specify` for `{FEATURE}`. Use `{DESIGN_DOCUMENT}` as the main source guiding the feature's eventual end-to-end development. Create one feature specification and its quality checklist. Return the resolved feature directory, spec path, checklist result, and branch if applicable.

Gate: inspect `spec.md` and `checklists/requirements.md`; require completed quality checks and no unresolved clarification markers. Capture the actual directory created by specify, not one inferred from a branch or guessed feature number. Verify `.specify/feature.json` selects it and carry that exact directory into every remaining phase. Before each downstream phase, verify feature selection still matches; never operate on another feature's artifacts.

## 2. Plan

Phase prompt:

> Run `$speckit-plan` for the `{FEATURE}` feature in `{FEATURE_DIR}`. Read the completed specification and originating design document. Produce the implementation plan and the supporting artifacts required by that workflow. Keep the plan aligned with the feature scope and repository constitution.

Gate: inspect `plan.md`, `research.md`, `data-model.md`, `quickstart.md`, and applicable `contracts/` outputs as required by the planning skill. Require resolved research unknowns, justified constitution checks, and the correct `AGENTS.md` plan reference. Record any legitimately inapplicable conditional outputs. Do not rerun plan setup simply to inspect artifacts: it can overwrite the completed plan.

## 3. Enrich the plan with testing

Phase prompt:

> Use `$util-testing` to enrich `{FEATURE_DIR}/plan.md` for `{FEATURE}` with meaningful test coverage and a TDD-focused implementation workflow. Read the specification, original design document, supporting plan artifacts, and existing repository tests. Read util-testing's classification reference and other references required by the proposed tests. Edit the plan directly; do not regenerate it through `$speckit-plan` and do not implement tests yet.

Require the enriched plan to include:

- A coverage matrix mapping requirement/scenario IDs to observable assertions, test layer, proposed test project/file paths, and happy, failure, and boundary cases where relevant. Identify justified manual checks or coverage gaps explicitly.
- Repository-native test frameworks and conventions, the lowest meaningful test layer, deterministic fixtures and doubles, and isolation/cleanup for any real dependencies. Address time, concurrency, shared state, or environment dependence where applicable.
- A practical red–green–refactor sequence for each behavior slice: write a meaningful failing test, verify the intended failure, implement the smallest change, then refactor while tests pass. Include appropriate focused commands and broader validation gates based on actual repository tooling. Mark commands needing unavailable infrastructure rather than claiming execution.
- Testing dependencies and acceptance gates that task generation can translate into ordered work, with explicit TDD requested. Avoid arbitrary coverage percentages, redundant broad tests, and tests that merely mirror implementation.

Gate: inspect the updated `plan.md` for actionable coverage and TDD sequencing, preserved design decisions, and consistency with the specification. Synchronize supporting planning documents only where the enrichment requires it. Do not rewrite feature requirements merely to accommodate a testing approach.

## 4. Generate tasks

Phase prompt:

> Run `$speckit-tasks` for the `{FEATURE}` feature in `{FEATURE_DIR}`. Explicitly use the testing-enriched plan and originating design document. TDD is requested: include the planned test tasks before their corresponding implementation tasks, intended-failure verification, refactoring, and validation gates. Preserve story organization, exact file paths, dependencies, and truthful parallel markers.

Gate: inspect `tasks.md` for the skill's checklist format, sequential unique IDs, story labels, and clear file paths. Verify coverage from the enriched plan is represented, tests precede dependent implementation, and parallel work does not bypass TDD dependencies. Require independently testable stories and acceptance criteria consistent with the spec. Do not run `$speckit-implement`.

## Report

Return the feature, source document, actual feature directory, links to the specification, enriched plan, and tasks, plus relevant supporting artifacts. Summarize phase/gate outcomes, task count, suggested MVP, and any assumptions or outstanding blockers. Distinguish artifact review from tests actually executed. Leave all changes uncommitted for user review.
