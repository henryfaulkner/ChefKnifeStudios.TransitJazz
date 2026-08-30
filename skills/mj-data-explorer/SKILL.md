---
name: mj-data-explorer
description: Conversational, agentic explorer for Marta Jazz (TransitJazz) GTFS and API data — evaluates a transit agency's GTFS-RT, static, and rail-realtime feeds for compatibility with the data worker, cross-references route IDs against the live MartaJazz REST API, and answers neighborhood ↔ route questions. Use when the user wants to assess a new transit agency's feeds, check whether a route exists or why routes are missing, verify GTFS load status, or ask which routes serve a neighborhood — "is this GTFS feed compatible", "can we add agency X", "does route Y exist", "/mj-data-explorer".
---

<!-- last verified: 2026-08-30 -->

# Marta Jazz Data Explorer

A conversational front door to the TransitJazz GTFS and API data. This skill is a
**router**: it owns the UX and decides which underlying function does the work. The
user never has to know the functions exist.

> **Scope note (feature 055).** This skill previously also explored the Parquet
> telemetry datasets written by the data worker's logging sidecar. That sidecar, its
> storage, and the `telemetry-query-bridge` MCP tool were retired in feature 055.
> Worker diagnosis now goes through centralized structured logs — use the
> `transitjazz-logs` skill for that, not this one.

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
- **Always query real data.** Every claim must come from an actual fetch — an
  `mj-gtfs` decode or a live API call — never from memory.

## Opening move

If the user gave no direction, say something like:

> Hey — I can dig into the Marta Jazz GTFS and route data with you. Are you sizing up
> a new agency's feeds, or chasing down something about the routes we already load?

Then route based on their answer (you do not need an exact keyword match — read intent):

| If the user is… | Route to |
|-----------------|----------|
| Evaluating a new transit agency's feeds, "can we add agency X", "is this GTFS feed compatible", "why are buses/trains skipped for this source", whether an agency's heavy rail can be added | [functions/gtfs-compatibility.md](functions/gtfs-compatibility.md) |
| Cross-referencing a `route_id` against live GTFS data, "does route X exist", "why are routes missing", checking if GTFS is loaded | call the API — see [references/mj-api-schema.md](references/mj-api-schema.md) and [references/mj-api-query-guide.md](references/mj-api-query-guide.md) |
| Asking about neighborhoods — which routes serve a neighborhood, which neighborhoods a route passes through, transit-commute rankings, demographic comparisons, "does route X go through Y?" | read the committed lean file — see [references/neighborhood-routes-context.md](references/neighborhood-routes-context.md) |
| Diagnosing worker behavior — something "broke", tones missing, cities failing, publish errors, "why did the worker do X" | **not this skill.** Hand off to the `transitjazz-logs` skill, which queries the centralized structured logs. |

If intent is ambiguous, ask one clarifying free-text question before routing.

## Files in this skill

- **SKILL.md** (this file) — router + conversational UX.
- **functions/gtfs-compatibility.md** — evaluate a transit agency's GTFS-RT (buses),
  static, and rail-realtime (trains) feeds for compatibility with the data worker
  algorithm. Uses `mj-gtfs` as its fetch tool.
- **references/mj-api-schema.md** — MartaJazz REST API endpoints, response schemas,
  and the meaning of the API's `routeId` values.
- **references/mj-api-query-guide.md** — when and how to call the API from within
  the explorer: cross-referencing route IDs, verifying GTFS load status, PowerShell
  invocation patterns.
- **references/neighborhood-routes-context.md** — how and when to read the committed
  lean and full neighborhood-route files for neighborhood ↔ route Q&A and demographic
  lookups. Consult the full file only per-objectId on explicit request; never
  speculatively.

## Ground rules

- **Fetch before you claim.** Decode a real feed via `mj-gtfs`, or call the real API,
  before asserting anything about an agency's data. Never answer a compatibility
  question from memory or from a prior report.
- **Never invent a measurement.** If a number was not measured this run, say so
  plainly rather than estimating it.
- **`functions/gtfs-compatibility.md` is a shared dependency.** The
  `discover-transit-city` skill delegates its entire evaluation stage to that file.
  Changing its interpretation table changes that skill's output too.
- **Worker runtime behavior is out of scope.** Anything about what the worker actually
  did on a given cycle — anomalies, failures, tone counts, publish outcomes — belongs
  to the `transitjazz-logs` skill and the centralized log stream.
