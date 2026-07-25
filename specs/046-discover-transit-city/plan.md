# Implementation Plan: Discover Transit City (autonomous compatibility scout)

**Branch**: `main` (no feature branch created — this feature builds a Claude Code *skill*,
delivered as tracked files in the repo, not application code requiring a review branch of
its own) | **Date**: 2026-07-25 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/046-discover-transit-city/spec.md`, derived
from `docs/DISCOVER_TRANSIT_CITY_SKILL_DESIGN_DOCUMENT.md`

**Note**: This template is filled in by the `/speckit.plan` command. See
`.specify/templates/plan-template.md` for the execution workflow.

## Summary

Build `discover-transit-city`, a hands-free Claude Code skill invoked with zero arguments
(by a weekly `/schedule` cloud routine) that autonomously: selects one not-yet-evaluated
North American/European transit authority from a curated pool (falling back to open
`WebSearch` once exhausted); resolves ambiguous multi-operator cities via a stated
tie-break rule; discovers the authority's GTFS-RT vehicle-position, static GTFS, and
(if applicable) rail-realtime feed URLs; fetches and decodes those feeds by delegating
entirely to the existing `mj-gtfs` skill (never reimplementing fetch/decode); evaluates
compatibility using the existing `gtfs-compatibility` interpretation rules **grounded
directly in the real `TransitDataWorker` source** (`src/Server/
ChefKnifeStudios.MartaJazz.Server.TransitDataWorker`) rather than only the generic skill
docs — distinguishing the platform's config-only extension points (`CityConfig`'s
`ApiKeyEnvVar`, `RouteIdNormalization`, `RailRouteIdMap`) from what genuinely needs new
code (a bespoke `ITransitCity` implementation or `RailRealtimeAdapter`-style class), and
naming the worker's actual "unknown category" fallback behavior for unresolved routes; and
writes exactly one `docs/city-compat/{slug}.md` report — using one of **two rigid,
field-by-field fill-in templates** (COMPATIBLE/PARTIAL vs. BLOCKED, the latter split into
KEY-GATED vs. NO-USABLE-FEED sub-classifications) so every run's output is structurally
identical and no field is invented — before committing that single file to a new
`compat/{slug}` branch and opening a PR against `main`. Every report opens with a
**deterministic 0–100 aggregate compatibility score and one of four effort tiers**
(Drop-in / Minor Config / Adapter Needed / Not Viable), computed by a single published
formula (`contracts/aggregate-score-formula.md`) shared by both templates — a
required-fields-gated, alignment-scaled bus contribution (0–70) plus a rail-mechanism
lookup (0–20) plus a credential-availability lookup (0–10), hard-capped for blocked
outcomes — so a maintainer can triage a week of candidate reports from one number alone.
The skill never touches `main` directly, never edits application code, and never begins
city onboarding (that remains `add-transit-city`'s separate, human-triggered job).

The technical approach is almost entirely **orchestration of existing machinery**: this
plan's only new artifacts are the skill's own instruction files (`SKILL.md`, a candidate
list, a feed-discovery playbook, and — the emphasis of this planning pass, per explicit
request — two literal, fill-in-the-blank report templates that an evaluation run copies
and completes field-by-field, rather than freehand composing prose to loosely match an
exemplar).

## Technical Context

**Language/Version**: N/A — this is a Claude Code **skill** (Markdown instructions read by
an LLM agent at runtime), not compiled software. Supporting scripts invoked by the skill
are PowerShell 5.1 + inline Python (both already used by `mj-gtfs`; no new language
introduced).
**Primary Dependencies**: `mj-gtfs` skill (feed fetch/decode), `gtfs-compatibility` function
(`.claude/skills/mj-data-explorer/functions/gtfs-compatibility.md`) (compatibility
interpretation rules), `WebSearch` tool (open-discovery fallback + authority resolution),
`git` + `gh` CLI (branch/PR delivery), `/schedule` (recurring cloud invocation). The
**ground-truth authority** for what "compatible" concretely requires is
`src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker` itself (read directly,
2026-07-25) — specifically `Cities/ITransitCity.cs`, `Cities/CityConfig.cs`,
`Cities/GtfsRtCity.cs`, `Cities/RouteIdNormalizer.cs`, `RailRealtime/RailRealtimeAdapter.cs`,
and `Worker.cs`'s `ResolveCategory`/`ProcessSpatialReconciliationAsync` — not merely the
skill docs that describe it at a higher level. See `research.md`'s "Ground every
'what compatible means' claim in `TransitDataWorker`'s actual source" decision and
`data-model.md`'s "Ground-truth reference" table for the specific facts this changed.
**Storage**: Flat Markdown files only — `docs/city-compat/*.md` (existing convention) is
both the durable "evaluated-city" record and the report artifact. No database, no new
persisted state.
**Testing**: No automated test suite (a skill's "test" is a supervised dry-run — see
quickstart.md). Verification is behavioral: confirm the produced PR structure, file count,
and template field completeness rather than unit tests.
**Target Platform**: Runs inside a Claude Code session — the skill's build target is
Windows 11 (this repo's dev environment, per `CLAUDE.md`) for the manual dry-run, and
Anthropic's cloud routine runtime for the scheduled, unattended weekly execution.
**Project Type**: Single skill package under `.claude/skills/` — no frontend/backend split
applies to this feature (it is tooling, not an application feature).
**Performance Goals**: N/A (not a performance-sensitive feature; a single run's wall-clock
time is bounded by feed-fetch latency and WebSearch round-trips, not optimized here).
**Constraints**: Must complete a full run with **zero human interaction** (FR-001); must
modify **exactly one** file (the new report) per run (FR-013); must **never** commit to
`main` (FR-014); must **never** fabricate a measurement (FR-008/FR-009); must **never**
attempt credential acquisition (FR-007); must **never** begin city onboarding (FR-015).
**Scale/Scope**: One evaluation per invocation; a curated pool of ~20 seed candidates
(§5 of the design doc) intended to outlast several months of weekly runs before leaning on
open discovery.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

The project constitution (`.specify/memory/constitution.md`, v3.3.2) governs the
**TransitJazz application** — its 3-service Azure architecture, GTFS ID mapping, generative
audio, map/UX principles, dark mode, etc. This feature does not touch any of that surface:

| Constitution area | Applies to this feature? | Why |
|---|---|---|
| I. Decoupled Cloud Architecture | No | This feature adds no service, no deployable artifact. It is a skill file set read by an agent, not a running component of the 3-tier app. |
| II. No Frontend Secrets | No | No frontend code touched. The skill itself must never acquire/store credentials at all (FR-007) — a stricter rule than II, not a violation of it. |
| III. Two-Pass Pipeline / VI. GTFS ID Mapping | Informs report content, doesn't bind (read-only) | The skill's compatibility evaluation reads the same `RouteJoinKey` alignment concept (via `gtfs-compatibility.md`) these principles define, and — per this clarification pass — is grounded directly in the live worker's actual source (`GtfsRtCity`, `RouteIdNormalizer`, `CityConfig`, `Worker.cs`'s `ResolveCategory`) so report fields describe the real config-only vs. new-code boundary correctly. This feature only *reads* that source for accuracy; it never modifies `TransitDataWorker` or any other application code — the boundary from Principle I still holds. |
| IV. OpenTelemetry / V. CI/CD / VII–XIII (map, audio, UX, dark mode, i18n) | No | No application code, UI, map layer, audio, or client surface is created or modified. Report docs are developer-facing Markdown, not end-user product surface. |

**Gate result: PASS — no violations, no complexity to justify.** This is the expected
outcome for a "build a new orchestrator skill" feature; the Complexity Tracking table below
is empty because none of the constitution's application-level gates are in scope.

*(Re-checked after Phase 1 design: still PASS — Phase 1 added only Markdown
templates/contracts and a candidates list; no code, service, or UI surface was introduced.)*

## Project Structure

### Documentation (this feature)

```text
specs/046-discover-transit-city/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
│   ├── report-template-compatible.md   # fill-in template, COMPATIBLE/PARTIAL outcome
│   ├── report-template-blocked.md      # fill-in template, BLOCKED/negative outcome
│   ├── aggregate-score-formula.md      # the deterministic 0-100 score + 4-tier effort mapping, shared by both templates
│   ├── skill-stage-contract.md         # the 6-stage orchestration contract (inputs/outputs per stage)
│   └── pr-delivery-contract.md         # the git/gh actions contract (branch naming, commit scope, PR body)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

This feature's "source code" is entirely skill instruction files (Markdown) plus one data
file — there is no compiled application code, so neither the Option 1 (single project) nor
Option 2/3 templates apply. The concrete deliverable tree:

```text
.claude/skills/discover-transit-city/
├── SKILL.md                              # orchestrator: frontmatter + 6-stage imperative checklist
├── candidates.md                         # curated ranked NA/EU candidate pool
└── references/
    ├── feed-discovery-playbook.md        # stage-3 detail: where to look, feed-type classifier,
    │                                       failure→verdict table, do-not-fabricate rule
    └── report-templates.md               # thin pointer into contracts/report-template-*.md
                                            # AND contracts/aggregate-score-formula.md
                                            # (the actual templates + formula are authored
                                            #  once, in specs/046.../contracts/, and copied
                                            #  verbatim into this file at implementation
                                            #  time — see data-model.md "Template provenance")

docs/city-compat/
└── {slug}.md                             # ONE new file per run — the only file a run may write
```

**Structure Decision**: Single skill package under `.claude/skills/discover-transit-city/`,
matching the existing `add-transit-city` / `mj-gtfs` / `mj-data-explorer` skill layout
convention already in this repo (frontmatter'd `SKILL.md` as the always-loaded router,
`references/` for progressive-disclosure depth). No `contracts/` folder ships inside the
skill package itself — `contracts/` is a **planning artifact** (this spec folder) whose
content becomes the literal text of `references/report-templates.md` at implementation time.
This mirrors how `043-toronto-ttc-transit/contracts/city-config.md` was a planning contract
that implementation then applied as real config edits — here the "config" being applied is
a template file's body, not application settings.

## Complexity Tracking

*(Empty — Constitution Check passed with no violations to justify.)*
