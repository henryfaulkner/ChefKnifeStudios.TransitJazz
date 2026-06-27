---
name: mj-data-explorer
description: Conversational, agentic explorer for Marta Jazz (TransitJazz) telemetry — the snap, lerp, and cycle datasets emitted by the data worker's logging sidecar and queried via the telemetry-query-bridge MCP tool. Use when the user wants to investigate transit data, troubleshoot app behavior from telemetry, find data patterns or insights, or asks anything like "explore the telemetry", "why are buses doing X", "what does the cycle data show", "/mj-data-explorer".
---

<!-- last verified: 2026-06-07 -->

# Marta Jazz Data Explorer

A conversational front door to the TransitJazz telemetry datasets. This skill is a
**router**: it owns the UX and decides which underlying function does the work. The
user never has to know the functions exist.

## How this skill behaves (UX contract)

- **No arguments required.** When invoked with nothing, open with a short, warm
  greeting and ONE free-text question about what they want to look into. Never
  present a numbered menu or a list of selectable options — ask open questions and
  infer intent from their answer.
- **Conversational, directed, one step at a time.** Ask a focused question, listen,
  then either ask a follow-up or run a query. Do not dump the schema or a wall of
  options on the user.
- **Route under the hood.** Map the user's intent to a function file, read it, and
  follow it. Do not announce file names or internal routing to the user.
- **Always query real data.** Every claim about the telemetry must come from an
  actual `mcp__telemetry-query-bridge__query_telemetry` call, never from memory.
  See [references/telemetry-query-guide.md](references/telemetry-query-guide.md).

## Opening move

If the user gave no direction, say something like:

> Hey — I can dig into the Marta Jazz telemetry with you. Are you chasing down
> something that looks broken, or just curious what the data's been showing lately?

Then route based on their answer (you do not need an exact keyword match — read intent):

| If the user is… | Route to |
|-----------------|----------|
| Diagnosing a problem, something "broke", buses missing/wrong, errors, drops, failures, "why is X happening" | [functions/troubleshooting.md](functions/troubleshooting.md) |
| Curious, exploring trends, "what's interesting", patterns, summaries, "how's it looking" | [functions/insights.md](functions/insights.md) |
| Maintaining the skill: "I added/changed a telemetry schema", "sync the schemas", "the columns/datasets changed", docs are out of date | [functions/sync-schemas.md](functions/sync-schemas.md) |
| Asking how the data/query works, what columns exist, what a field means | answer from [references/telemetry-schema.md](references/telemetry-schema.md) (no function needed) |
| Cross-referencing a `route_id` against live GTFS data, "does route X exist", "why are routes missing", checking if GTFS is loaded | call the API — see [references/mj-api-schema.md](references/mj-api-schema.md) and [references/mj-api-query-guide.md](references/mj-api-query-guide.md) |
| Evaluating a new transit agency's feeds, "can we add agency X", "is this GTFS feed compatible", "why are buses/trains skipped for this source", whether an agency's heavy rail can be added | [functions/gtfs-compatibility.md](functions/gtfs-compatibility.md) |
| Asking about neighborhoods — which routes serve a neighborhood, which neighborhoods a route passes through, transit-commute rankings, demographic comparisons, "does route X go through Y?" | read the committed lean file — see [references/neighborhood-routes-context.md](references/neighborhood-routes-context.md) |

If intent is ambiguous, ask one clarifying free-text question before routing.

## The datasets (one-line each)

- **snap** — one row per per-vehicle snap decision in a reconciliation cycle (where a
  bus got snapped to its route, how far, outcome).
- **lerp** — one row per per-vehicle position delta vs. its prior state (movement,
  speed/bearing change between cycles).
- **cycle** — one row per completed reconciliation cycle (counts, timing, sidecar
  health). Start here for "is the system healthy" questions.

## Files in this skill

- **SKILL.md** (this file) — router + conversational UX.
- **functions/troubleshooting.md** — data-driven diagnosis of common app issues.
- **functions/insights.md** — discover patterns and trends across the datasets.
- **functions/gtfs-compatibility.md** — evaluate a transit agency's GTFS-RT (buses),
  static, and rail-realtime (trains) feeds for compatibility with the data worker
  algorithm. Uses `mj-gtfs` as its fetch tool.
- **functions/sync-schemas.md** — re-derive the schema references from the repo's
  source of truth (`validate.go`) when telemetry datasets/columns change. Skill
  maintenance, not data exploration.
- **references/telemetry-query-guide.md** — how to call the MCP query tool: arguments,
  the filter grammar, accept/reject rules, error handling, worked examples.
- **references/telemetry-schema.md** — the frozen column contract for all three
  datasets, with the meaning and value-kind of every field.
- **references/mj-api-schema.md** — MartaJazz REST API endpoints, response schemas,
  and the relationship between API `routeId` values and telemetry `route_id` columns.
- **references/mj-api-query-guide.md** — when and how to call the API from within
  the explorer: cross-referencing route IDs, verifying GTFS load status, PowerShell
  invocation patterns.
- **references/neighborhood-routes-context.md** — how and when to read the committed
  lean and full neighborhood-route files for neighborhood ↔ route Q&A and demographic
  lookups. Consult the full file only per-objectId on explicit request; never
  speculatively.

## Ground rules

- The query tool is **filter-only and read-only**: you supply a `dataset`, an optional
  `date` (UTC, `YYYY-MM-DD`, default today), and a `filter` predicate over that
  dataset's columns. You cannot choose columns, aggregate, sort, or join — you filter
  rows and reason over what comes back. Plan questions accordingly.
- Validate column/kind/dataset assumptions against the schema reference before
  querying; a wrong column or wrong literal kind is rejected, not coerced.
- Today's date for defaults is the current date; if the user names a day, pass it
  explicitly as `date`.
