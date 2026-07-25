---

description: "Task list for 046-discover-transit-city"
---

# Tasks: Discover Transit City (autonomous compatibility scout)

**Input**: Design documents from `specs/046-discover-transit-city/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md
(all present)

**Tests**: Not requested. This feature has no automated test suite by design (plan.md
Technical Context: "a skill's 'test' is a supervised dry-run") — verification is the
manual dry-run in Phase 6 / quickstart.md, not unit/contract tests.

**Organization**: Tasks are grouped by user story per spec.md. All four user stories in
this feature are Priority P1 and are tightly coupled facets of one coherent skill (a
discovery run that skips the aggregate score, or skips the BLOCKED path, is not a
meaningfully shippable increment on its own) — so tasks are sequenced in natural build
order (author the shared contracts first, then the orchestrator that uses them, then
validate), with each task still tagged to the user story it most directly serves for
traceability. There is no meaningful "MVP subset" narrower than the whole skill; the
Implementation Strategy section below explains this explicitly.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3, US4)
- File paths are exact and repo-relative

## Path Conventions

This feature has no `src/`/`tests/` split — it is a Claude Code skill (Markdown) plus a
generated docs artifact:
- Skill package: `.claude/skills/discover-transit-city/`
- Output artifact directory (verified during dry-run, not authored): `docs/city-compat/`
- Planning contracts (already authored by `/speckit-plan`, applied not re-derived):
  `specs/046-discover-transit-city/contracts/`

---

## Phase 1: Setup

**Purpose**: Create the skill package's directory structure before any content is written.

- [X] T001 Create the skill package directories: `.claude/skills/discover-transit-city/`
  and `.claude/skills/discover-transit-city/references/` (empty, ready for content)

**Checkpoint**: Directory structure exists; no content yet.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Apply the planning contracts verbatim into the skill's `references/` —
these are the shared inputs every stage of the orchestrator (`SKILL.md`, Phase 3+) reads.
Per `data-model.md`'s "Template provenance" section, this is direct application of an
already-authored contract, not re-derivation — copy the contract content, do not rewrite
it from scratch.

**⚠️ CRITICAL**: `SKILL.md` (Phase 3) references these files by path; they must exist
first.

- [X] T002 [P] Apply `specs/046-discover-transit-city/contracts/aggregate-score-formula.md`
  verbatim into a new file
  `.claude/skills/discover-transit-city/references/aggregate-score-formula.md`
- [X] T003 [P] Concatenate `specs/046-discover-transit-city/contracts/
  report-template-compatible.md` and `specs/046-discover-transit-city/contracts/
  report-template-blocked.md` into a new file
  `.claude/skills/discover-transit-city/references/report-templates.md`, prefixed with a
  short "pick exactly one template for this run's outcome; read
  `aggregate-score-formula.md` first — both templates require it" preamble (per
  `data-model.md`'s Template provenance section and `plan.md`'s Project Structure note)
- [X] T004 [P] Write
  `.claude/skills/discover-transit-city/references/feed-discovery-playbook.md` per
  design-doc §6 (`docs/DISCOVER_TRANSIT_CITY_SKILL_DESIGN_DOCUMENT.md`) and this feature's
  `contracts/skill-stage-contract.md` STAGE 3: search order (agency portal → Mobility
  Database → targeted WebSearch), the vehicle-positions-vs-trip-updates/alerts classifier,
  the failure→verdict table (no feed / trip-updates-only / key-gated / zero-vehicle-entities
  / static-404), the KEY-GATED vs. NO-USABLE-FEED classification rule (FR-012a), and the
  "do not fabricate — do not register for keys" rule stated bluntly (FR-007)
- [X] T005 [P] Write `.claude/skills/discover-transit-city/candidates.md` per
  `data-model.md`'s "Candidate pool entry" schema and design-doc §5's seed list: the
  `Rank | City | Authority | Region | Known static zip URL | Known GTFS-RT
  vehicle-positions URL | Rail? | Notes` table, keyless NA agencies (SEPTA, TriMet, RTD,
  King County Metro, Metro Transit, MTS, TransLink, STM, OC Transpo) ranked above EU
  agencies with known registration gates (TfL, IDFM), excluding the done-set
  `{marta, mbta, wmata, nymta, ttc, cta}` — verify each seed URL is still live before
  committing (design doc's own "verify at build time" instruction)

**Checkpoint**: All shared reference content exists and is internally consistent (the
templates reference the formula file by the same relative name it's saved under).

---

## Phase 3: User Story 1 - Weekly hands-free discovery of a new candidate city (Priority: P1) 🎯

**Goal**: An unattended `/discover-transit-city` invocation picks an unevaluated city,
resolves its authority, discovers its feeds, evaluates compatibility via `mj-gtfs` +
`gtfs-compatibility`, and delivers a report — with zero human interaction at any point.

**Independent Test**: Per spec.md — trigger the skill with no arguments and no human
present; confirm a PR eventually exists adding exactly one new report file, and `main` is
untouched.

### Implementation for User Story 1

- [X] T006 [US1] Write `.claude/skills/discover-transit-city/SKILL.md` frontmatter exactly
  per plan.md §3.1 / the design doc: `name: discover-transit-city`, and a `description`
  that names the hands-free/CRON/zero-argument nature so the router picks it for scheduled
  invocation and not for interactive "add this city" requests (which route to
  `add-transit-city` instead)
- [X] T007 [US1] In `SKILL.md`, write the STAGE 0 preflight block: restate the hard
  invariants inline (never ask the user anything; write exactly one file; commit only that
  file to a new branch; never touch `main`; never merge) and confirm working directory is
  the repo root — per `contracts/skill-stage-contract.md` STAGE 0 and spec.md FR-001
- [X] T008 [US1] In `SKILL.md`, write the STAGE 1 city-selection block: enumerate the
  done-set by parsing each `docs/city-compat/*.md` H1 (city + authority, not filename),
  walk `references/candidates.md` top-to-bottom for the first not-done row, fall back to
  `WebSearch` only once the curated list is exhausted, and end cleanly with a one-line "no
  candidate found" note (no file, no PR) if both arms produce nothing — per
  `contracts/skill-stage-contract.md` STAGE 1 and spec.md FR-002/FR-003/FR-016
- [X] T009 [US1] In `SKILL.md`, write the STAGE 2 authority-resolution block: trust the
  candidates row's named authority when present, otherwise `WebSearch "primary public
  transit authority {city}"` and apply the tie-break rule verbatim (largest urban-core
  network; the operator the city's GTFS is popularly identified with; prefer a unified
  GTFS-RT vehicle-positions feed) — per `contracts/skill-stage-contract.md` STAGE 2 and
  spec.md FR-004
- [X] T010 [US1] In `SKILL.md`, write the STAGE 3 feed-discovery block: point to
  `references/feed-discovery-playbook.md` for the search order and classifier, verify any
  GTFS-RT candidate is vehicle-positions (not trip-updates/alerts), detect and classify
  key-gating as KEY-GATED vs. NO-USABLE-FEED, and branch straight to STAGE 5 with the
  BLOCKED template on a dead end — per `contracts/skill-stage-contract.md` STAGE 3 and
  spec.md FR-005/FR-006/FR-007/FR-012a
- [X] T011 [US1] In `SKILL.md`, write the STAGE 4 evaluation block: read `mj-gtfs`'s
  `SKILL.md` first, run the parallel fetch (GTFS-RT protobuf, agency-slug'd static zip
  directory, rail JSON if applicable) in one tool dispatch, run the combined decode+align
  script in one tool call, honor `rt._diag_note` by re-decoding before proceeding (never
  writing a report off a suspected decode bug), and apply the `gtfs-compatibility.md`
  interpretation table for independent bus/rail verdicts — per
  `contracts/skill-stage-contract.md` STAGE 4 steps 1–6 and spec.md FR-008/FR-011
- [X] T012 [US1] In `SKILL.md`, add the STAGE 4 ground-truth-check sub-block: before
  calling any residual `alignment.unmatched_rt_ids` entry "needs new code," test it
  against the three `RouteIdNormalizer` transforms; determine which of the two real rail
  mechanisms (config-only `RailRouteIdMap` remap vs. bespoke `RailRealtimeAdapter`-style
  adapter) applies when rail is present; and note the platform's "unknown category"
  fallback for any residual route mismatch — per `contracts/skill-stage-contract.md` STAGE
  4 step 7 and spec.md FR-008a/FR-011/FR-012b
- [X] T013 [US1] Reference `Cities/ITransitCity.cs`, `Cities/CityConfig.cs`,
  `Cities/GtfsRtCity.cs`, `Cities/RouteIdNormalizer.cs`, and
  `RailRealtime/RailRealtimeAdapter.cs` (under
  `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/`) by path in the STAGE 4
  block so the running agent can re-verify the ground-truth facts (config-only extension
  shape, the three normalizer names, the two rail mechanisms) against current source if
  they've drifted since this plan was written — per plan.md's Technical Context
  ground-truth-authority note

**Checkpoint**: `SKILL.md` fully specifies stages 0–4; a manual read-through can trace a
hypothetical run from zero arguments through a computed verdict without any step requiring
human input.

---

## Phase 4: User Story 2 - Documented negative result when a feed isn't usable (Priority: P1)

**Goal**: When no usable feed exists, the skill still produces a complete, honest,
non-fabricated report and still opens a PR — a BLOCKED outcome is a successful run.

**Independent Test**: Per spec.md — point the skill at an authority known to have no
public real-time feed (or only a key-gated one); confirm it completes, opens a PR, and the
report states the specific blocking reason with unmeasured figures marked as such.

### Implementation for User Story 2

- [X] T014 [US2] In `SKILL.md`, write the STAGE 5 report-writing block: pick exactly one
  of `references/report-templates.md`'s two templates based on the stage-3/4 outcome
  (never blend them), compute the aggregate score first per
  `references/aggregate-score-formula.md` (see Phase 5), fill every placeholder from real
  measured data or the fixed `UNASSESSED`/`N/A` tokens, and confirm this is the run's only
  file write — per `contracts/skill-stage-contract.md` STAGE 5 and spec.md FR-009/FR-010/
  FR-012/FR-013
- [X] T015 [US2] In `SKILL.md`'s STAGE 5 block, add the BLOCKED-path emphasis: explicitly
  state that reaching STAGE 5 via a STAGE 3 dead end is a **successful run, not a
  failure**, and the BLOCKED template must still be filled completely (including whatever
  static-only data was reachable) and still result in an opened PR — per spec.md User
  Story 2 and FR-012

**Checkpoint**: `SKILL.md` now fully specifies the report-writing stage for both outcome
branches; a BLOCKED dry-run and a COMPATIBLE dry-run each produce a complete report by
following the same STAGE 5 instructions with a different template chosen.

---

## Phase 5: User Story 4 - One number tells me the effort required (Priority: P1)

**Goal**: Every report — regardless of outcome — opens with the deterministic 0–100
aggregate score and one of four effort tiers, computed identically across runs.

**Independent Test**: Per spec.md — recompute the score by hand from a report's own
published measured inputs using `references/aggregate-score-formula.md`; it must match
the number printed at the top of that report exactly.

> **Note**: The formula itself was already fully authored as a planning contract
> (`contracts/aggregate-score-formula.md`, applied in T002) and does not need to be
> re-derived here — these tasks wire the *orchestrator* to use it correctly and verify
> the wiring, which is why they follow, not precede, Phase 2's application step.

### Implementation for User Story 4

- [X] T016 [US4] In `SKILL.md`'s STAGE 5 block (from T014), add the scoring step as the
  literal first action of STAGE 5: read `references/aggregate-score-formula.md`, compute
  the required-fields-gated bus contribution (0–70), the rail mechanism/alignment lookup
  (0–20), and the credential lookup (0–10), sum them, and — on the BLOCKED path only —
  apply the KEY-GATED (cap 40) or NO-USABLE-FEED (cap 15) ceiling before mapping the result
  to one of the four effort tiers — per `contracts/skill-stage-contract.md` STAGE 5 step 2
  and spec.md FR-012c/FR-012d
- [X] T017 [US4] Confirm (by reading `references/report-templates.md`, produced in T003)
  that both templates' score block renders as the literal first content of the file, above
  the H1's surrounding context, exactly matching `contracts/report-template-compatible.md`
  and `contracts/report-template-blocked.md`'s placement — this is a verification task
  against T003's output, not new authoring, since the templates were applied verbatim

**Checkpoint**: `SKILL.md`'s STAGE 5 now computes and places the aggregate score before
any other report content, for both outcome branches.

---

## Phase 6: User Story 3 - Reports never bypass human review (Priority: P1)

**Goal**: Every run's only durable output is a PR against `main` with exactly one new
file; `main` is never committed to directly; onboarding is never triggered.

**Independent Test**: Per spec.md — run the skill repeatedly (including a run that picks
the "wrong" authority for an ambiguous city); confirm in every case the only durable
output is an open PR with a single new file, `main` never receives a direct commit, and no
onboarding activity begins.

### Implementation for User Story 3

- [X] T018 [US3] In `SKILL.md`, write the STAGE 6 delivery block: the exact git/gh sequence
  from `contracts/pr-delivery-contract.md` (`git checkout -b compat/{slug}` → `git add`
  only the one report file, aborting if `git status` shows anything else → `git commit`
  with the `compat: evaluate {AUTHORITY} ({City})` message → `git push -u origin
  compat/{slug}` → `gh pr create` with the required "does not onboard the city" sentence in
  the body) — per `contracts/skill-stage-contract.md` STAGE 6 and spec.md FR-013/FR-014
- [X] T019 [US3] In `SKILL.md`'s STAGE 6 block, add the degraded-path handling from
  `contracts/pr-delivery-contract.md`: if `git push` or `gh pr create` fails, leave the
  branch committed locally and end with a clear statement of what succeeded, what failed,
  and the branch name — never falling back to a commit on `main` — per spec.md FR-017 and
  the "cannot publish findings" edge case
- [X] T020 [US3] In `SKILL.md`, write the top-level risk-register/non-goals block (design
  doc §9): explicitly state the non-goals verbatim — no onboarding (`CityNames.cs`,
  `appsettings.json`, `CityFab.razor`, map origins, overlay text are never touched by this
  skill), no merging, no telemetry queries, no `main` commits ever, no interactive prompts
  — per spec.md FR-015 and User Story 3's acceptance scenarios

**Checkpoint**: `SKILL.md` is now a complete, self-contained six-stage orchestrator
(STAGE 0 through STAGE 6) plus the risk-register invariants; every stage from the design
is represented and cross-references the correct `references/` file.

---

## Phase 7: Manual Dry-Run & Scheduling (Cross-Cutting — Required Before Any Real Use)

**Purpose**: Prove the whole chain works unattended exactly once, per quickstart.md and
design-doc §10 step 5, **before** creating the recurring `/schedule` routine — this is not
optional polish, it's the only way to validate stages 1–3 actually find feeds without a
human present.

- [ ] T021 Invoke `/discover-transit-city` with no arguments (manual dry-run) and confirm
  per `specs/046-discover-transit-city/quickstart.md`'s full checklist: the correct
  candidate was picked (expected: SEPTA on a fresh pool), exactly one new
  `docs/city-compat/{slug}.md` file exists, the correct template was used with no leftover
  `<...>` placeholders, the aggregate score is present/first/reproducible by hand
  recomputation (SC-009), a PR exists targeting `main` with the required non-onboarding
  sentence, and `main` has zero new commits
- [ ] T022 If the dry-run (T021) surfaces any defect (wrong template chosen, a fabricated
  number, more than one file changed, a missed `rt._diag_note` re-decode, an incorrect
  score, a missing KEY-GATED/NO-USABLE-FEED classification), fix the corresponding
  `SKILL.md` section from Phase 3–6 and re-run T021 until clean — do not proceed to T023
  until a fully clean dry-run is achieved
- [ ] T023 Create the weekly `/schedule` cloud routine with the prompt
  `/discover-transit-city` and the belt-and-suspenders guardrail text from design-doc §8
  (write exactly one file under `docs/city-compat/`; commit only that file to a new
  `compat/{slug}` branch; open a PR to `main`; never commit to `main`; never merge; never
  edit application code; never run the `add-transit-city` onboarding flow) — only after
  T021/T022 confirm a clean dry-run

**Checkpoint**: The skill is proven to work unattended exactly once and is now scheduled
to run weekly without further human involvement.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Phase 1 (directories must exist). **BLOCKS**
  Phase 3 onward — `SKILL.md` cannot correctly reference `references/*.md` files that
  don't exist yet.
- **User Story phases (Phase 3–6)**: All depend on Phase 2. Unlike a typical feature,
  these four phases are **not independently parallelizable across different engineers**
  in the usual sense — they are four sections of the *same* `SKILL.md` file, so they must
  be authored sequentially in stage order (STAGE 0–2 → STAGE 3 → STAGE 4 → STAGE 5 → STAGE
  6) to avoid merge conflicts within one file, even though each section maps to a distinct
  user story for traceability.
- **Dry-run & scheduling (Phase 7)**: Depends on ALL of Phases 3–6 being complete —
  `SKILL.md` must be fully written before the first end-to-end dry-run can mean anything.

### User Story Dependencies

- **US1 (Phase 3)**: Depends on Phase 2 only (the applied contracts). No dependency on
  US2/US3/US4's SKILL.md sections, but shares the same file, so sequence, don't parallelize.
- **US2 (Phase 4)**: Builds directly on US1's STAGE 4 output (needs a stage-3/4 outcome to
  branch on) — sequenced after Phase 3.
- **US4 (Phase 5)**: Builds directly on US2's STAGE 5 skeleton (T014) — the score
  computation is inserted as STAGE 5's first action — sequenced after Phase 4.
- **US3 (Phase 6)**: Builds on a completed STAGE 5 (needs a written report file to
  deliver) — sequenced after Phase 5.

### Within Each Phase

- Phase 2's four contract-application tasks (T002–T005) are mutually independent
  (different destination files) and marked [P].
- Phase 3–6's tasks are NOT marked [P] — they are sequential edits to the same
  `SKILL.md` file (adding one stage's block after the previous one).
- Phase 7's tasks are strictly sequential (T021 → T022 → T023): each depends on the
  previous task's outcome.

### Parallel Opportunities

- **T002, T003, T004, T005** (Phase 2) can all run in parallel — four independent files
  in `.claude/skills/discover-transit-city/references/` and `candidates.md`, none reading
  another's output.
- No other tasks in this feature are parallelizable: Phase 3–6 all edit the single
  `SKILL.md` file in stage order, and Phase 7 is a strictly sequential validate-fix-schedule
  loop.

---

## Parallel Example: Phase 2 (Foundational)

```bash
# Launch all four contract-application tasks together:
Task: "Apply contracts/aggregate-score-formula.md into references/aggregate-score-formula.md"
Task: "Concatenate both report templates into references/report-templates.md"
Task: "Write references/feed-discovery-playbook.md per design doc §6 + skill-stage-contract STAGE 3"
Task: "Write candidates.md per data-model.md's Candidate pool entry schema + design doc §5 seed list"
```

---

## Implementation Strategy

### Why there is no narrower MVP than "the whole skill"

Every user story in this feature is Priority P1, and unlike a typical product feature,
they are not independently shippable slices — they are four correctness properties of one
six-stage orchestrator:
- A version that does US1 (discovery) but skips US2 (BLOCKED handling) would silently do
  nothing on the majority-likely outcome (per the design doc's own risk register: feed
  discovery failure is the *dominant* failure mode) — that's not a usable increment, it's a
  guaranteed early gap.
- A version that does US1+US2 but skips US4 (the score) ships a functioning but
  incomplete report format — every report would need a second pass later to backfill the
  score, which the user explicitly said is "highly impactful" to the final product.
- A version that skips US3 (the PR-only safety boundary) is not safe to run unattended at
  all — this is the one property that must be correct from the very first invocation.

So the practical delivery sequence is: **build the whole `SKILL.md` (Phases 1–6), then
validate it once by hand (Phase 7), then schedule it.** There is no intermediate
"deploy/demo" checkpoint before Phase 7 the way a typical multi-story feature would have,
because there is no running system to demo until the orchestrator file is complete and
proven end-to-end.

### Recommended sequence

1. Phase 1 (Setup) — trivial, does not block on anything else being decided.
2. Phase 2 (Foundational) — apply all four contracts in parallel; this is mechanical
   (the content already exists in `specs/046-discover-transit-city/contracts/`).
3. Phases 3 → 4 → 5 → 6, in that order, writing `SKILL.md` stage by stage. Each phase's
   checkpoint is a read-through sanity check, not a runnable test (there is no partial-run
   mode for a Markdown-instruction skill).
4. Phase 7 — the first and only real "test": one supervised dry-run, fix anything it
   surfaces, then schedule.

### Parallel Team Strategy

With multiple people: Phase 2's four tasks can be split across four people simultaneously.
Phases 3–6 cannot be split across people working concurrently (same file, sequential
stages) — one person should own the full `SKILL.md` authoring pass. Phase 7 is inherently
serial (one dry-run at a time).

---

## Notes

- [P] tasks = different files, no dependencies — only Phase 2 qualifies in this feature.
- [Story] label maps each `SKILL.md` section to the user story it primarily serves, for
  traceability back to spec.md — it does not imply independent parallel execution the way
  it would in a typical multi-component feature.
- This feature intentionally has no test-writing tasks: verification is the supervised
  dry-run (Phase 7), consistent with `plan.md`'s Technical Context ("no automated test
  suite... verification is behavioral").
- Never commit to `main` while executing these tasks or during the Phase 7 dry-run itself
  — the skill being built is itself required to never do this, and the person/agent
  building it should model that discipline throughout (see repo-wide "never auto-commit"
  guidance).
- Stop at the Phase 7 checkpoint and get the dry-run genuinely clean before creating the
  `/schedule` routine — a scheduled, recurring, unattended job compounds any defect weekly.
